using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Orchestration.Application.Activity;
using Orchestration.Application.Persistence;
using Orchestration.Domain.FinancialMetricsExtraction;

namespace Orchestration.Application.Agents.Data.FinancialAnalysis.Extraction;

public sealed class FinancialMetricsExtractionDraftService(
    IOrchestrationDbContext dbContext,
    IStructuredFinancialMetricsValidator validator,
    IStructuredFinancialMetricsSessionService sessionService,
    IActivityEventPublisher activityPublisher)
    : IFinancialMetricsExtractionDraftService
{
    private const int MaxDiagnosticReasonCodes = 64;
    private const int MaxDiagnosticReasonCodeLength = 128;
    private const string HumanReviewExtractionStrategy = "human_review";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

    private readonly IOrchestrationDbContext _dbContext =
        dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    private readonly IStructuredFinancialMetricsValidator _validator =
        validator ?? throw new ArgumentNullException(nameof(validator));
    private readonly IStructuredFinancialMetricsSessionService _sessionService =
        sessionService ?? throw new ArgumentNullException(nameof(sessionService));
    private readonly IActivityEventPublisher _activityPublisher =
        activityPublisher ?? throw new ArgumentNullException(nameof(activityPublisher));

    public async Task<FinancialMetricsExtractionDraftServiceResult> CreateOrReplaceAsync(
        Guid sessionId,
        Guid userId,
        CreateFinancialMetricsExtractionDraftRequest request,
        CancellationToken cancellationToken)
    {
        if (sessionId == Guid.Empty || userId == Guid.Empty)
        {
            return FinancialMetricsExtractionDraftServiceResult.Invalid(
                "Session ID and user ID are required.");
        }

        if (request is null)
        {
            return FinancialMetricsExtractionDraftServiceResult.Invalid(
                "Draft request is required.");
        }

        if (!TryNormalizePayload(request.Payload, out var payload, out var payloadError))
        {
            return FinancialMetricsExtractionDraftServiceResult.Invalid(payloadError);
        }

        string payloadJson;

        try
        {
            payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException)
        {
            return FinancialMetricsExtractionDraftServiceResult.Invalid(
                "Draft payload could not be serialized.");
        }

        var sessionExists = await _dbContext.AnalysisSessions.AnyAsync(
            x => x.Id == sessionId && x.UserId == userId,
            cancellationToken);

        if (!sessionExists)
        {
            return FinancialMetricsExtractionDraftServiceResult.NotFound(
                "Analysis session was not found.");
        }

        FinancialMetricsExtractionDraft replacement;

        try
        {
            replacement = FinancialMetricsExtractionDraft.Create(
                sessionId,
                userId,
                request.OriginalFileName,
                request.FileSizeBytes,
                request.ContentHash,
                payloadJson,
                DateTimeOffset.UtcNow);
        }
        catch (ArgumentException exception)
        {
            return FinancialMetricsExtractionDraftServiceResult.Invalid(
                exception.Message);
        }

        var existingPending = await _dbContext.FinancialMetricsExtractionDrafts
            .Where(x =>
                x.SessionId == sessionId &&
                x.UserId == userId &&
                x.Status == FinancialMetricsExtractionDraftStatus.PendingReview)
            .ToArrayAsync(cancellationToken);

        if (existingPending.Length > 0)
        {
            _dbContext.FinancialMetricsExtractionDrafts.RemoveRange(existingPending);
        }

        _dbContext.FinancialMetricsExtractionDrafts.Add(replacement);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return FinancialMetricsExtractionDraftServiceResult.Conflict(
                "The pending draft changed while it was being replaced.");
        }
        catch (DbUpdateException)
        {
            return FinancialMetricsExtractionDraftServiceResult.Conflict(
                "The pending draft could not be replaced.");
        }

        return FinancialMetricsExtractionDraftServiceResult.Success(
            ToDto(replacement, payload));
    }

    public async Task<FinancialMetricsExtractionDraftDto?> GetPendingAsync(
        Guid sessionId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (sessionId == Guid.Empty || userId == Guid.Empty)
        {
            return null;
        }

        var draft = await _dbContext.FinancialMetricsExtractionDrafts
            .Where(x =>
                x.SessionId == sessionId &&
                x.UserId == userId &&
                x.Status == FinancialMetricsExtractionDraftStatus.PendingReview)
            .OrderByDescending(x => x.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (draft is null ||
            !TryDeserializePayload(draft.PayloadJson, out var payload, out _))
        {
            return null;
        }

        return ToDto(draft, payload);
    }

    public async Task<FinancialMetricsExtractionDraftServiceResult> UpdateAsync(
        Guid draftId,
        Guid sessionId,
        Guid userId,
        UpdateFinancialMetricsExtractionDraftRequest request,
        CancellationToken cancellationToken)
    {
        if (draftId == Guid.Empty || sessionId == Guid.Empty || userId == Guid.Empty)
        {
            return FinancialMetricsExtractionDraftServiceResult.Invalid(
                "Draft ID, session ID, and user ID are required.");
        }

        if (request is null ||
            request.Candidates is null ||
            request.ProposedInput is null)
        {
            return FinancialMetricsExtractionDraftServiceResult.Invalid(
                "Review candidates and proposed input are required.");
        }

        var draft = await FindOwnedDraftAsync(
            draftId,
            sessionId,
            userId,
            cancellationToken);

        if (draft is null)
        {
            return FinancialMetricsExtractionDraftServiceResult.NotFound(
                "Review draft was not found.");
        }

        if (draft.Status != FinancialMetricsExtractionDraftStatus.PendingReview)
        {
            return FinancialMetricsExtractionDraftServiceResult.Conflict(
                "Only pending review drafts can be edited.");
        }

        if (!TryDeserializePayload(draft.PayloadJson, out var payload, out var payloadError))
        {
            return FinancialMetricsExtractionDraftServiceResult.Invalid(payloadError);
        }

        if (!string.Equals(
                payload.ProposedInput.DocumentId,
                request.ProposedInput.DocumentId,
                StringComparison.Ordinal))
        {
            return FinancialMetricsExtractionDraftServiceResult.Invalid(
                "The proposed input DocumentId cannot be changed.");
        }

        if (!TryApplyReviewUpdates(
                payload,
                request,
                out var updatedCandidates,
                out var updatedMetadataCandidates,
                out var decisions,
                out var updateError))
        {
            return FinancialMetricsExtractionDraftServiceResult.Invalid(updateError);
        }

        var proposedInput = request.ProposedInput with
        {
            Metrics = request.ProposedInput.Metrics?.ToArray()!
        };

        if (!TryValidateHumanCorrectionCoherence(
                proposedInput,
                updatedCandidates,
                updatedMetadataCandidates,
                decisions,
                out var coherenceError))
        {
            return FinancialMetricsExtractionDraftServiceResult.Invalid(coherenceError);
        }

        var conflicts = payload.Conflicts
            .Where(conflict => !IsResolvedConflict(
                conflict,
                updatedCandidates,
                updatedMetadataCandidates,
                decisions))
            .ToArray();
        var missingFields = ComputeMissingFields(proposedInput);
        var validation = _validator.Validate(proposedInput);
        var validationIssues = validation.Errors
            .Concat(validation.Warnings)
            .ToArray();
        var updatedPayload = payload with
        {
            ProposedInput = proposedInput,
            Candidates = updatedCandidates,
            Conflicts = conflicts,
            MissingFields = missingFields,
            ValidationIssues = validationIssues,
            MetadataCandidates = updatedMetadataCandidates
        };

        if (!TryNormalizePayload(
                updatedPayload,
                out updatedPayload,
                out var normalizationError))
        {
            return FinancialMetricsExtractionDraftServiceResult.Invalid(
                normalizationError);
        }

        string payloadJson;

        try
        {
            payloadJson = JsonSerializer.Serialize(updatedPayload, JsonOptions);
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException)
        {
            return FinancialMetricsExtractionDraftServiceResult.Invalid(
                "Updated draft payload could not be serialized.");
        }

        try
        {
            draft.UpdatePayload(payloadJson, DateTimeOffset.UtcNow);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return FinancialMetricsExtractionDraftServiceResult.Conflict(
                "The review draft changed while it was being updated.");
        }

        return FinancialMetricsExtractionDraftServiceResult.Success(
            ToDto(draft, updatedPayload));
    }

    public async Task<FinancialMetricsExtractionDraftServiceResult> ConfirmAsync(
        Guid draftId,
        Guid sessionId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (draftId == Guid.Empty || sessionId == Guid.Empty || userId == Guid.Empty)
        {
            return FinancialMetricsExtractionDraftServiceResult.Invalid(
                "Draft ID, session ID, and user ID are required.");
        }

        var draft = await FindOwnedDraftAsync(
            draftId,
            sessionId,
            userId,
            cancellationToken);

        if (draft is null)
        {
            return FinancialMetricsExtractionDraftServiceResult.NotFound(
                "Review draft was not found.");
        }

        if (!TryDeserializePayload(draft.PayloadJson, out var payload, out var payloadError))
        {
            return FinancialMetricsExtractionDraftServiceResult.Invalid(payloadError);
        }

        if (draft.Status == FinancialMetricsExtractionDraftStatus.Confirmed)
        {
            return FinancialMetricsExtractionDraftServiceResult.Success(
                ToDto(draft, payload));
        }

        if (draft.Status == FinancialMetricsExtractionDraftStatus.Discarded)
        {
            return FinancialMetricsExtractionDraftServiceResult.Conflict(
                "A discarded draft cannot be confirmed.");
        }

        var blockingCandidates = payload.Candidates
            .Any(candidate => !IsResolvedReviewState(candidate.ReviewState)) ||
            payload.MetadataCandidates
                .Any(candidate => !IsResolvedReviewState(candidate.ReviewState));

        if (blockingCandidates ||
            payload.Conflicts.Count > 0 ||
            payload.MissingFields.Count > 0)
        {
            return FinancialMetricsExtractionDraftServiceResult.Invalid(
                "The review draft still contains unresolved candidates, conflicts, or missing fields.");
        }

        var validation = _validator.Validate(payload.ProposedInput);
        var validationIssues = validation.Errors
            .Concat(validation.Warnings)
            .ToArray();

        if (!validation.IsValid)
        {
            return FinancialMetricsExtractionDraftServiceResult.Invalid(
                "The reviewed financial metrics are invalid.",
                validationIssues);
        }

        FinancialMetricsSessionSaveResult? saveResult;

        try
        {
            saveResult = await _sessionService.SaveAsync(
                new SaveStructuredFinancialMetricsRequest(
                    SessionId: sessionId,
                    Input: payload.ProposedInput,
                    Provenance: new StructuredFinancialMetricsProvenanceInput(
                        IngestionMethod: "pdf_file_reviewed",
                        OriginalFileName: draft.OriginalFileName,
                        FileSizeBytes: draft.FileSizeBytes,
                        ContentHash: draft.ContentHash)),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return FinancialMetricsExtractionDraftServiceResult.Conflict(
                "The reviewed financial metrics could not be saved.");
        }

        if (saveResult is null)
        {
            return FinancialMetricsExtractionDraftServiceResult.NotFound(
                "Analysis session was not found while saving reviewed metrics.");
        }

        if (!saveResult.IsValid || saveResult.Context is null)
        {
            var saveIssues = saveResult.Errors
                .Concat(saveResult.Warnings)
                .ToArray();

            return FinancialMetricsExtractionDraftServiceResult.Invalid(
                "The reviewed financial metrics were rejected while saving.",
                saveIssues);
        }

        try
        {
            draft.Confirm(userId, DateTimeOffset.UtcNow);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return FinancialMetricsExtractionDraftServiceResult.Conflict(
                "The review draft changed while it was being confirmed.");
        }

        await _activityPublisher.PublishAsync(
            new ActivityEvent(
                sessionId,
                "financial_metrics_extraction_review_confirmed",
                "DataAgent",
                "Financial metrics extraction review confirmed.",
                DateTimeOffset.UtcNow),
            cancellationToken);

        return FinancialMetricsExtractionDraftServiceResult.Success(
            ToDto(draft, payload));
    }

    public async Task<FinancialMetricsExtractionDraftServiceResult> DiscardAsync(
        Guid draftId,
        Guid sessionId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (draftId == Guid.Empty || sessionId == Guid.Empty || userId == Guid.Empty)
        {
            return FinancialMetricsExtractionDraftServiceResult.Invalid(
                "Draft ID, session ID, and user ID are required.");
        }

        var draft = await FindOwnedDraftAsync(
            draftId,
            sessionId,
            userId,
            cancellationToken);

        if (draft is null)
        {
            return FinancialMetricsExtractionDraftServiceResult.NotFound(
                "Review draft was not found.");
        }

        if (!TryDeserializePayload(draft.PayloadJson, out var payload, out var payloadError))
        {
            return FinancialMetricsExtractionDraftServiceResult.Invalid(payloadError);
        }

        if (draft.Status == FinancialMetricsExtractionDraftStatus.Discarded)
        {
            return FinancialMetricsExtractionDraftServiceResult.Success(
                ToDto(draft, payload));
        }

        if (draft.Status == FinancialMetricsExtractionDraftStatus.Confirmed)
        {
            return FinancialMetricsExtractionDraftServiceResult.Conflict(
                "A confirmed draft cannot be discarded.");
        }

        try
        {
            draft.Discard(userId, DateTimeOffset.UtcNow);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return FinancialMetricsExtractionDraftServiceResult.Conflict(
                "The review draft changed while it was being discarded.");
        }

        await _activityPublisher.PublishAsync(
            new ActivityEvent(
                sessionId,
                "financial_metrics_extraction_review_discarded",
                "DataAgent",
                "Financial metrics extraction review discarded.",
                DateTimeOffset.UtcNow),
            cancellationToken);

        return FinancialMetricsExtractionDraftServiceResult.Success(
            ToDto(draft, payload));
    }

    private async Task<FinancialMetricsExtractionDraft?> FindOwnedDraftAsync(
        Guid draftId,
        Guid sessionId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.FinancialMetricsExtractionDrafts
            .FirstOrDefaultAsync(
                x =>
                    x.Id == draftId &&
                    x.SessionId == sessionId &&
                    x.UserId == userId,
                cancellationToken);
    }

    private static bool TryApplyReviewUpdates(
        FinancialMetricsExtractionDraftPayload payload,
        UpdateFinancialMetricsExtractionDraftRequest request,
        out IReadOnlyList<FinancialMetricCandidate> updatedCandidates,
        out IReadOnlyList<FinancialDocumentMetadataCandidate> updatedMetadataCandidates,
        out IReadOnlyDictionary<Guid, FinancialMetricCandidateReviewUpdate> decisions,
        out string error)
    {
        var decisionMap = new Dictionary<Guid, FinancialMetricCandidateReviewUpdate>();

        foreach (var update in request.Candidates)
        {
            if (update is null || update.CandidateId == Guid.Empty)
            {
                return FailUpdates(
                    "Every review update must target a candidate ID.",
                    out updatedCandidates,
                    out updatedMetadataCandidates,
                    out decisions,
                    out error);
            }

            if (!decisionMap.TryAdd(update.CandidateId, update))
            {
                return FailUpdates(
                    "Candidate review updates cannot contain duplicate IDs.",
                    out updatedCandidates,
                    out updatedMetadataCandidates,
                    out decisions,
                    out error);
            }

            if (!IsAllowedDecision(update.Decision))
            {
                return FailUpdates(
                    "Candidate review decision is not supported.",
                    out updatedCandidates,
                    out updatedMetadataCandidates,
                    out decisions,
                    out error);
            }
        }

        var metricIds = payload.Candidates.Select(x => x.Id).ToHashSet();
        var metadataIds = payload.MetadataCandidates.Select(x => x.Id).ToHashSet();

        if (decisionMap.Keys.Any(id => !metricIds.Contains(id) && !metadataIds.Contains(id)))
        {
            return FailUpdates(
                "Candidate review update targeted an unknown candidate ID.",
                out updatedCandidates,
                out updatedMetadataCandidates,
                out decisions,
                out error);
        }

        var metrics = payload.Candidates.ToList();
        var metadata = payload.MetadataCandidates.ToList();

        foreach (var update in decisionMap.Values)
        {
            var metricIndex = metrics.FindIndex(x => x.Id == update.CandidateId);

            if (metricIndex >= 0)
            {
                var original = metrics[metricIndex];

                if (update.Decision == FinancialMetricCandidateReviewStates.HumanCorrected)
                {
                    if (update.Value is null &&
                        update.Currency is null &&
                        update.Unit is null)
                    {
                        return FailUpdates(
                            "Human-corrected metric updates must include an edited value, currency, or unit.",
                            out updatedCandidates,
                            out updatedMetadataCandidates,
                            out decisions,
                            out error);
                    }

                    metrics[metricIndex] = original with
                    {
                        ReviewState = FinancialMetricCandidateReviewStates.Rejected
                    };
                    metrics.Add(original with
                    {
                        Id = Guid.NewGuid(),
                        Value = update.Value ?? original.Value,
                        Currency = NormalizeOptional(update.Currency) ?? original.Currency,
                        Unit = NormalizeOptional(update.Unit) ?? original.Unit,
                        SourceKind = FinancialMetricCandidateSourceKinds.HumanCorrected,
                        Confidence = 1m,
                        Evidence = "",
                        ExtractionStrategy = HumanReviewExtractionStrategy,
                        ReviewState = FinancialMetricCandidateReviewStates.HumanCorrected,
                        InferenceExplanation = null
                    });
                }
                else
                {
                    metrics[metricIndex] = original with
                    {
                        ReviewState = update.Decision
                    };
                }

                continue;
            }

            var metadataIndex = metadata.FindIndex(x => x.Id == update.CandidateId);
            var originalMetadata = metadata[metadataIndex];

            if (update.Decision == FinancialMetricCandidateReviewStates.HumanCorrected)
            {
                if (string.IsNullOrWhiteSpace(update.MetadataValue))
                {
                    return FailUpdates(
                        "Human-corrected metadata updates must include a text value.",
                        out updatedCandidates,
                        out updatedMetadataCandidates,
                        out decisions,
                        out error);
                }

                metadata[metadataIndex] = originalMetadata with
                {
                    ReviewState = FinancialMetricCandidateReviewStates.Rejected
                };
                metadata.Add(originalMetadata with
                {
                    Id = Guid.NewGuid(),
                    Value = update.MetadataValue.Trim(),
                    SourceKind = FinancialMetricCandidateSourceKinds.HumanCorrected,
                    Confidence = 1m,
                    Evidence = "",
                    ExtractionStrategy = HumanReviewExtractionStrategy,
                    ReviewState = FinancialMetricCandidateReviewStates.HumanCorrected,
                    InferenceExplanation = null
                });
            }
            else
            {
                metadata[metadataIndex] = originalMetadata with
                {
                    ReviewState = update.Decision
                };
            }
        }

        updatedCandidates = metrics.ToArray();
        updatedMetadataCandidates = metadata.ToArray();
        decisions = decisionMap;
        error = "";
        return true;
    }

    private static bool TryValidateHumanCorrectionCoherence(
        StructuredFinancialMetricsInput proposedInput,
        IReadOnlyList<FinancialMetricCandidate> candidates,
        IReadOnlyList<FinancialDocumentMetadataCandidate> metadataCandidates,
        IReadOnlyDictionary<Guid, FinancialMetricCandidateReviewUpdate> decisions,
        out string error)
    {
        if (proposedInput.Metrics is null)
        {
            error = "Proposed input metrics cannot be null.";
            return false;
        }

        foreach (var decision in decisions.Values.Where(x =>
                     x.Decision == FinancialMetricCandidateReviewStates.HumanCorrected))
        {
            var originalMetric = candidates.FirstOrDefault(x =>
                x.Id == decision.CandidateId);

            if (originalMetric is not null)
            {
                var corrected = candidates.LastOrDefault(x =>
                    x.Id != originalMetric.Id &&
                    x.Name == originalMetric.Name &&
                    x.Period == originalMetric.Period &&
                    x.ReviewState == FinancialMetricCandidateReviewStates.HumanCorrected);

                if (corrected is null ||
                    !proposedInput.Metrics.Any(metric =>
                        string.Equals(
                            metric.Name?.Trim(),
                            corrected.Name.Trim(),
                            StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(
                            metric.Period?.Trim(),
                            corrected.Period.Trim(),
                            StringComparison.OrdinalIgnoreCase) &&
                        metric.Value == corrected.Value &&
                        SameOptional(metric.Currency, corrected.Currency) &&
                        SameOptional(metric.Unit, corrected.Unit) &&
                        string.Equals(
                            metric.Source,
                            FinancialMetricCandidateSourceKinds.HumanCorrected,
                            StringComparison.Ordinal)))
                {
                    error =
                        "Proposed input must contain each human-corrected metric and its source.";
                    return false;
                }

                continue;
            }

            var originalMetadata = metadataCandidates.FirstOrDefault(x =>
                x.Id == decision.CandidateId);
            var correctedMetadata = metadataCandidates.LastOrDefault(x =>
                x.Id != decision.CandidateId &&
                originalMetadata is not null &&
                string.Equals(
                    x.FieldName,
                    originalMetadata.FieldName,
                    StringComparison.OrdinalIgnoreCase) &&
                x.ReviewState == FinancialMetricCandidateReviewStates.HumanCorrected);

            if (correctedMetadata is null ||
                !MetadataMatchesProposal(correctedMetadata, proposedInput))
            {
                error =
                    "Proposed input must contain each human-corrected metadata value.";
                return false;
            }
        }

        error = "";
        return true;
    }

    private static bool MetadataMatchesProposal(
        FinancialDocumentMetadataCandidate candidate,
        StructuredFinancialMetricsInput proposedInput)
    {
        var proposalValue = candidate.FieldName.Trim().ToLowerInvariant() switch
        {
            "company" => proposedInput.Company,
            "currency" => proposedInput.Currency,
            "unit" => proposedInput.Unit,
            _ => null
        };

        return string.Equals(
            NormalizeOptional(proposalValue),
            NormalizeOptional(candidate.Value),
            StringComparison.Ordinal);
    }

    private static bool IsResolvedConflict(
        FinancialMetricCandidateConflict conflict,
        IReadOnlyList<FinancialMetricCandidate> candidates,
        IReadOnlyList<FinancialDocumentMetadataCandidate> metadataCandidates,
        IReadOnlyDictionary<Guid, FinancialMetricCandidateReviewUpdate> decisions)
    {
        var involvedIds = conflict.MetricCandidates
            .Select(x => x.Id)
            .Concat(conflict.MetadataCandidates.Select(x => x.Id))
            .Distinct()
            .ToArray();

        if (involvedIds.Length == 0)
        {
            return false;
        }

        var reviewStates = candidates
            .Select(x => (x.Id, x.ReviewState))
            .Concat(metadataCandidates.Select(x => (x.Id, x.ReviewState)))
            .ToDictionary(x => x.Id, x => x.ReviewState);

        if (involvedIds.Any(id =>
                !reviewStates.TryGetValue(id, out var state) ||
                !IsResolvedReviewState(state)))
        {
            return false;
        }

        return involvedIds.Any(id =>
            reviewStates[id] is
                FinancialMetricCandidateReviewStates.Accepted or
                FinancialMetricCandidateReviewStates.HumanCorrected ||
            decisions.TryGetValue(id, out var decision) &&
            decision.Decision == FinancialMetricCandidateReviewStates.HumanCorrected);
    }

    private static IReadOnlyList<string> ComputeMissingFields(
        StructuredFinancialMetricsInput input)
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(input.Company))
        {
            missing.Add("company");
        }

        if (string.IsNullOrWhiteSpace(input.Currency))
        {
            missing.Add("currency");
        }

        if (string.IsNullOrWhiteSpace(input.Unit))
        {
            missing.Add("unit");
        }

        if (input.Metrics is null || input.Metrics.Count == 0)
        {
            missing.Add("metrics");
        }

        return missing.ToArray();
    }

    private static bool TryDeserializePayload(
        string payloadJson,
        out FinancialMetricsExtractionDraftPayload payload,
        out string error)
    {
        try
        {
            var deserialized = JsonSerializer.Deserialize<
                FinancialMetricsExtractionDraftPayload>(payloadJson, JsonOptions);

            return TryNormalizePayload(deserialized, out payload, out error);
        }
        catch (JsonException)
        {
            payload = null!;
            error = "Draft payload is malformed.";
            return false;
        }
        catch (NotSupportedException)
        {
            payload = null!;
            error = "Draft payload contains unsupported values.";
            return false;
        }
    }

    private static bool TryNormalizePayload(
        FinancialMetricsExtractionDraftPayload? payload,
        out FinancialMetricsExtractionDraftPayload normalized,
        out string error)
    {
        if (payload is null)
        {
            return FailPayload("Draft payload is required.", out normalized, out error);
        }

        if (payload.SchemaVersion !=
            FinancialMetricsExtractionDraftPayload.CurrentSchemaVersion)
        {
            return FailPayload(
                "Draft payload schema version is not supported.",
                out normalized,
                out error);
        }

        if (payload.ProposedInput is null ||
            payload.ProposedInput.Metrics is null ||
            payload.Candidates is null ||
            payload.MetadataCandidates is null ||
            payload.Conflicts is null ||
            payload.MissingFields is null ||
            payload.FallbackReasons is null ||
            payload.ValidationIssues is null ||
            payload.Diagnostics is null ||
            payload.Diagnostics.ReasonCodes is null)
        {
            return FailPayload(
                "Draft payload collections and required objects cannot be null.",
                out normalized,
                out error);
        }

        if (payload.Candidates.Any(x =>
                x is null ||
                x.Id == Guid.Empty ||
                string.IsNullOrWhiteSpace(x.Name) ||
                string.IsNullOrWhiteSpace(x.Period) ||
                string.IsNullOrWhiteSpace(x.SourceKind) ||
                string.IsNullOrWhiteSpace(x.ReviewState)) ||
            payload.MetadataCandidates.Any(x =>
                x is null ||
                x.Id == Guid.Empty ||
                string.IsNullOrWhiteSpace(x.FieldName) ||
                x.Value is null ||
                string.IsNullOrWhiteSpace(x.SourceKind) ||
                string.IsNullOrWhiteSpace(x.ReviewState)))
        {
            return FailPayload(
                "Draft payload contains a malformed candidate.",
                out normalized,
                out error);
        }

        var candidateIds = payload.Candidates
            .Select(x => x.Id)
            .Concat(payload.MetadataCandidates.Select(x => x.Id))
            .ToArray();

        if (candidateIds.Distinct().Count() != candidateIds.Length)
        {
            return FailPayload(
                "Draft payload contains duplicate candidate IDs.",
                out normalized,
                out error);
        }

        var candidateIdSet = candidateIds.ToHashSet();
        var normalizedConflicts = new List<FinancialMetricCandidateConflict>();

        foreach (var conflict in payload.Conflicts)
        {
            if (conflict is null ||
                conflict.MetricCandidates is null ||
                conflict.MetadataCandidates is null ||
                conflict.MetricCandidates.Any(x => x is null) ||
                conflict.MetadataCandidates.Any(x => x is null))
            {
                return FailPayload(
                    "Draft payload contains a malformed conflict.",
                    out normalized,
                    out error);
            }

            var conflictIds = conflict.MetricCandidates
                .Select(x => x.Id)
                .Concat(conflict.MetadataCandidates.Select(x => x.Id))
                .ToArray();

            if (conflictIds.Any(id => id == Guid.Empty || !candidateIdSet.Contains(id)))
            {
                return FailPayload(
                    "Draft payload conflict references an unknown candidate.",
                    out normalized,
                    out error);
            }

            normalizedConflicts.Add(conflict with
            {
                MetricCandidates = conflict.MetricCandidates.ToArray(),
                MetadataCandidates = conflict.MetadataCandidates.ToArray()
            });
        }

        if (!HasNonNegativeDiagnostics(payload.Diagnostics))
        {
            return FailPayload(
                "Draft diagnostics counts and durations cannot be negative.",
                out normalized,
                out error);
        }

        var reasonCodes = payload.Diagnostics.ReasonCodes.ToArray();

        if (reasonCodes.Length > MaxDiagnosticReasonCodes ||
            reasonCodes.Any(code =>
                string.IsNullOrWhiteSpace(code) ||
                code.Length > MaxDiagnosticReasonCodeLength))
        {
            return FailPayload(
                "Draft diagnostic reason codes exceed allowed bounds.",
                out normalized,
                out error);
        }

        normalized = payload with
        {
            ProposedInput = payload.ProposedInput with
            {
                Metrics = payload.ProposedInput.Metrics.ToArray()
            },
            Candidates = payload.Candidates.ToArray(),
            Conflicts = normalizedConflicts.ToArray(),
            MissingFields = payload.MissingFields.ToArray(),
            FallbackReasons = payload.FallbackReasons.ToArray(),
            ValidationIssues = payload.ValidationIssues.ToArray(),
            Diagnostics = payload.Diagnostics with
            {
                ReasonCodes = reasonCodes
            },
            MetadataCandidates = payload.MetadataCandidates.ToArray()
        };
        error = "";
        return true;
    }

    private static bool HasNonNegativeDiagnostics(
        FinancialMetricsExtractionDiagnostics diagnostics)
    {
        return IsNonNegative(diagnostics.PageCount) &&
            IsNonNegative(diagnostics.MarkdownCharacterCount) &&
            IsNonNegative(diagnostics.CandidateCount) &&
            IsNonNegative(diagnostics.ConflictCount) &&
            IsNonNegative(diagnostics.NativeTextDurationMilliseconds) &&
            IsNonNegative(diagnostics.OcrDurationMilliseconds) &&
            IsNonNegative(diagnostics.MarkItDownDurationMilliseconds) &&
            IsNonNegative(diagnostics.SemanticDurationMilliseconds) &&
            IsNonNegative(diagnostics.TotalDurationMilliseconds);
    }

    private static bool IsNonNegative(int? value)
    {
        return value is null or >= 0;
    }

    private static bool IsNonNegative(long? value)
    {
        return value is null or >= 0;
    }

    private static bool IsAllowedDecision(string? decision)
    {
        return decision is
            FinancialMetricCandidateReviewStates.Accepted or
            FinancialMetricCandidateReviewStates.Rejected or
            FinancialMetricCandidateReviewStates.HumanCorrected;
    }

    private static bool IsResolvedReviewState(string? reviewState)
    {
        return reviewState is
            FinancialMetricCandidateReviewStates.Explicit or
            FinancialMetricCandidateReviewStates.Accepted or
            FinancialMetricCandidateReviewStates.Rejected or
            FinancialMetricCandidateReviewStates.HumanCorrected;
    }

    private static bool SameOptional(string? left, string? right)
    {
        return string.Equals(
            NormalizeOptional(left),
            NormalizeOptional(right),
            StringComparison.Ordinal);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static FinancialMetricsExtractionDraftDto ToDto(
        FinancialMetricsExtractionDraft draft,
        FinancialMetricsExtractionDraftPayload payload)
    {
        return new FinancialMetricsExtractionDraftDto(
            draft.Id,
            draft.SessionId,
            draft.Status switch
            {
                FinancialMetricsExtractionDraftStatus.PendingReview => "pending_review",
                FinancialMetricsExtractionDraftStatus.Confirmed => "confirmed",
                FinancialMetricsExtractionDraftStatus.Discarded => "discarded",
                _ => "unknown"
            },
            draft.OriginalFileName,
            draft.FileSizeBytes,
            draft.ContentHash,
            payload,
            draft.CreatedAt,
            draft.UpdatedAt,
            draft.CompletedAt);
    }

    private static bool FailUpdates(
        string failure,
        out IReadOnlyList<FinancialMetricCandidate> updatedCandidates,
        out IReadOnlyList<FinancialDocumentMetadataCandidate> updatedMetadataCandidates,
        out IReadOnlyDictionary<Guid, FinancialMetricCandidateReviewUpdate> decisions,
        out string error)
    {
        updatedCandidates = [];
        updatedMetadataCandidates = [];
        decisions = new Dictionary<Guid, FinancialMetricCandidateReviewUpdate>();
        error = failure;
        return false;
    }

    private static bool FailPayload(
        string failure,
        out FinancialMetricsExtractionDraftPayload normalized,
        out string error)
    {
        normalized = null!;
        error = failure;
        return false;
    }
}
