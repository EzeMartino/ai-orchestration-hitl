using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Data.FinancialAnalysis.Extraction;
using Orchestration.Domain.AnalysisSessions;
using Orchestration.Domain.FinancialMetricsExtraction;
using Orchestration.Infrastructure.Persistence;
using Orchestration.Tests.Agents;

namespace Orchestration.Tests.Agents.Data.FinancialAnalysis;

public sealed class FinancialMetricsExtractionDraftServiceTests
{
    [Fact]
    public async Task CreateOrReplaceAsync_Should_replace_pending_and_keep_terminal_history()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var service = CreateService(dbContext);

        var confirmed = await CreateDraftAsync(
            service,
            session.Id,
            userId,
            fileName: "confirmed.pdf",
            contentHash: "confirmed-hash");
        var confirmedEntity = await dbContext.FinancialMetricsExtractionDrafts
            .SingleAsync(x => x.Id == confirmed.Id);
        confirmedEntity.Confirm(userId, DateTimeOffset.UtcNow.AddMinutes(1));
        await dbContext.SaveChangesAsync();

        await CreateDraftAsync(
            service,
            session.Id,
            userId,
            fileName: "old-pending.pdf",
            contentHash: "old-hash");
        dbContext.ResetSaveChangesCount();

        var replacementPayload = CreatePayload(
            proposedInput: CreateInput(company: "Replacement Co"));
        var result = await service.CreateOrReplaceAsync(
            session.Id,
            userId,
            new CreateFinancialMetricsExtractionDraftRequest(
                OriginalFileName: "replacement.pdf",
                FileSizeBytes: 9876,
                ContentHash: "replacement-hash",
                Payload: replacementPayload),
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Success);
        result.Draft.Should().NotBeNull();
        result.Draft!.OriginalFileName.Should().Be("replacement.pdf");
        result.Draft.FileSizeBytes.Should().Be(9876);
        result.Draft.ContentHash.Should().Be("replacement-hash");
        result.Draft.Payload.ProposedInput.Company.Should().Be("Replacement Co");
        dbContext.SaveChangesCount.Should().Be(1);

        var drafts = await dbContext.FinancialMetricsExtractionDrafts
            .Where(x => x.SessionId == session.Id)
            .ToArrayAsync();
        drafts.Should().HaveCount(2);
        drafts.Should().ContainSingle(x =>
            x.Status == FinancialMetricsExtractionDraftStatus.PendingReview &&
            x.OriginalFileName == "replacement.pdf");
        drafts.Should().ContainSingle(x =>
            x.Status == FinancialMetricsExtractionDraftStatus.Confirmed &&
            x.OriginalFileName == "confirmed.pdf");
    }

    [Fact]
    public async Task GetPendingAsync_Should_enforce_user_and_session_ownership()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var otherSession = await AddSessionAsync(dbContext, userId);
        var service = CreateService(dbContext);
        var created = await CreateDraftAsync(service, session.Id, userId);

        var own = await service.GetPendingAsync(
            session.Id,
            userId,
            CancellationToken.None);
        var otherUser = await service.GetPendingAsync(
            session.Id,
            Guid.NewGuid(),
            CancellationToken.None);
        var otherSessionResult = await service.GetPendingAsync(
            otherSession.Id,
            userId,
            CancellationToken.None);

        own.Should().NotBeNull();
        own!.Id.Should().Be(created.Id);
        otherUser.Should().BeNull();
        otherSessionResult.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_Should_reject_unknown_candidate_without_mutation()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var service = CreateService(dbContext);
        var draft = await CreateDraftAsync(service, session.Id, userId);
        var entity = await dbContext.FinancialMetricsExtractionDrafts
            .SingleAsync(x => x.Id == draft.Id);
        var originalJson = entity.PayloadJson;

        var result = await service.UpdateAsync(
            draft.Id,
            session.Id,
            userId,
            new UpdateFinancialMetricsExtractionDraftRequest(
                Candidates:
                [
                    new FinancialMetricCandidateReviewUpdate(
                        CandidateId: Guid.NewGuid(),
                        Decision: FinancialMetricCandidateReviewStates.Accepted,
                        Value: null,
                        Currency: null,
                        Unit: null,
                        MetadataValue: null)
                ],
                ProposedInput: draft.Payload.ProposedInput),
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Invalid);
        entity.PayloadJson.Should().Be(originalJson);
    }

    [Fact]
    public async Task UpdateAsync_Should_reject_duplicate_candidate_updates_without_mutation()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var service = CreateService(dbContext);
        var draft = await CreateDraftAsync(service, session.Id, userId);
        var candidateId = draft.Payload.Candidates.Single().Id;
        var entity = await dbContext.FinancialMetricsExtractionDrafts
            .SingleAsync(x => x.Id == draft.Id);
        var originalJson = entity.PayloadJson;
        var update = new FinancialMetricCandidateReviewUpdate(
            CandidateId: candidateId,
            Decision: FinancialMetricCandidateReviewStates.Accepted,
            Value: null,
            Currency: null,
            Unit: null,
            MetadataValue: null);

        var result = await service.UpdateAsync(
            draft.Id,
            session.Id,
            userId,
            new UpdateFinancialMetricsExtractionDraftRequest(
                Candidates: [update, update],
                ProposedInput: draft.Payload.ProposedInput),
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Invalid);
        entity.PayloadJson.Should().Be(originalJson);
    }

    [Fact]
    public async Task UpdateAsync_Should_apply_accepted_and_rejected_decisions_and_resolve_conflict()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var accepted = CreateMetricCandidate(
            value: 100m,
            reviewState: FinancialMetricCandidateReviewStates.Conflict);
        var rejected = CreateMetricCandidate(
            value: 120m,
            reviewState: FinancialMetricCandidateReviewStates.Conflict);
        var conflict = CreateMetricConflict(accepted, rejected);
        var payload = CreatePayload(
            candidates: [accepted, rejected],
            conflicts: [conflict]);
        var service = CreateService(dbContext);
        var draft = await CreateDraftAsync(
            service,
            session.Id,
            userId,
            payload: payload);

        var result = await service.UpdateAsync(
            draft.Id,
            session.Id,
            userId,
            new UpdateFinancialMetricsExtractionDraftRequest(
                Candidates:
                [
                    Decision(accepted.Id, FinancialMetricCandidateReviewStates.Accepted),
                    Decision(rejected.Id, FinancialMetricCandidateReviewStates.Rejected)
                ],
                ProposedInput: CreateInput(metricValue: 100m)),
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Success);
        result.Draft!.Payload.Candidates.Should().Contain(x =>
            x.Id == accepted.Id &&
            x.ReviewState == FinancialMetricCandidateReviewStates.Accepted);
        result.Draft.Payload.Candidates.Should().Contain(x =>
            x.Id == rejected.Id &&
            x.ReviewState == FinancialMetricCandidateReviewStates.Rejected);
        result.Draft.Payload.Conflicts.Should().BeEmpty();
        result.Draft.Payload.MissingFields.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_Should_retain_original_and_append_human_corrected_metric()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var original = CreateMetricCandidate(
            value: 100m,
            reviewState: FinancialMetricCandidateReviewStates.Inferred,
            sourcePage: 7);
        var service = CreateService(dbContext);
        var draft = await CreateDraftAsync(
            service,
            session.Id,
            userId,
            payload: CreatePayload(candidates: [original]));
        var proposed = CreateInput(
            metricValue: 125m,
            metricSource: FinancialMetricCandidateSourceKinds.HumanCorrected);

        var result = await service.UpdateAsync(
            draft.Id,
            session.Id,
            userId,
            new UpdateFinancialMetricsExtractionDraftRequest(
                Candidates:
                [
                    new FinancialMetricCandidateReviewUpdate(
                        CandidateId: original.Id,
                        Decision: FinancialMetricCandidateReviewStates.HumanCorrected,
                        Value: 125m,
                        Currency: "USD",
                        Unit: "USD_million",
                        MetadataValue: null)
                ],
                ProposedInput: proposed),
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Success);
        var candidates = result.Draft!.Payload.Candidates;
        candidates.Should().HaveCount(2);
        candidates.Should().Contain(x =>
            x.Id == original.Id &&
            x.ReviewState == FinancialMetricCandidateReviewStates.Rejected);
        var corrected = candidates.Single(x => x.Id != original.Id);
        corrected.Name.Should().Be(original.Name);
        corrected.Period.Should().Be(original.Period);
        corrected.Value.Should().Be(125m);
        corrected.SourcePage.Should().Be(7);
        corrected.SourceKind.Should().Be(FinancialMetricCandidateSourceKinds.HumanCorrected);
        corrected.ReviewState.Should().Be(FinancialMetricCandidateReviewStates.HumanCorrected);
        corrected.Evidence.Should().BeEmpty();
        corrected.InferenceExplanation.Should().BeNull();
        result.Draft.Payload.ProposedInput.Metrics.Single().Source
            .Should().Be(FinancialMetricCandidateSourceKinds.HumanCorrected);
    }

    [Fact]
    public async Task UpdateAsync_Should_append_human_corrected_metadata_candidate()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var metadata = CreateMetadataCandidate(
            fieldName: "company",
            value: "Old Co",
            reviewState: FinancialMetricCandidateReviewStates.Inferred);
        var payload = CreatePayload() with
        {
            MetadataCandidates = [metadata]
        };
        var service = CreateService(dbContext);
        var draft = await CreateDraftAsync(
            service,
            session.Id,
            userId,
            payload: payload);

        var result = await service.UpdateAsync(
            draft.Id,
            session.Id,
            userId,
            new UpdateFinancialMetricsExtractionDraftRequest(
                Candidates:
                [
                    new FinancialMetricCandidateReviewUpdate(
                        CandidateId: metadata.Id,
                        Decision: FinancialMetricCandidateReviewStates.HumanCorrected,
                        Value: null,
                        Currency: null,
                        Unit: null,
                        MetadataValue: "Corrected Co")
                ],
                ProposedInput: CreateInput(company: "Corrected Co")),
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Success);
        var metadataCandidates = result.Draft!.Payload.MetadataCandidates;
        metadataCandidates.Should().HaveCount(2);
        metadataCandidates.Should().Contain(x =>
            x.Id == metadata.Id &&
            x.ReviewState == FinancialMetricCandidateReviewStates.Rejected);
        metadataCandidates.Should().ContainSingle(x =>
            x.Id != metadata.Id &&
            x.Value == "Corrected Co" &&
            x.SourceKind == FinancialMetricCandidateSourceKinds.HumanCorrected &&
            x.ReviewState == FinancialMetricCandidateReviewStates.HumanCorrected &&
            x.Evidence == "");
        result.Draft.Payload.ProposedInput.Company.Should().Be("Corrected Co");
    }

    [Theory]
    [InlineData("candidate")]
    [InlineData("conflict")]
    [InlineData("missing")]
    [InlineData("metadata")]
    public async Task ConfirmAsync_Should_reject_unresolved_review_state(
        string blocker)
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var metric = CreateMetricCandidate();
        var payload = blocker switch
        {
            "candidate" => CreatePayload(
                candidates:
                [
                    metric with
                    {
                        ReviewState = FinancialMetricCandidateReviewStates.Inferred
                    }
                ]),
            "conflict" => CreatePayload(
                candidates: [metric],
                conflicts: [CreateMetricConflict(metric)]),
            "missing" => CreatePayload(missingFields: ["currency"]),
            "metadata" => CreatePayload() with
            {
                MetadataCandidates =
                [
                    CreateMetadataCandidate(
                        reviewState: FinancialMetricCandidateReviewStates.Missing)
                ]
            },
            _ => throw new InvalidOperationException()
        };
        var sessionService = new StubStructuredFinancialMetricsSessionService();
        var publisher = new FakeActivityEventPublisher();
        var service = CreateService(dbContext, sessionService, publisher);
        var draft = await CreateDraftAsync(
            service,
            session.Id,
            userId,
            payload: payload);

        var result = await service.ConfirmAsync(
            draft.Id,
            session.Id,
            userId,
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Invalid);
        sessionService.SaveRequests.Should().BeEmpty();
        publisher.PublishedEvents.Should().BeEmpty();
        (await dbContext.FinancialMetricsExtractionDrafts.SingleAsync(x => x.Id == draft.Id))
            .Status.Should().Be(FinancialMetricsExtractionDraftStatus.PendingReview);
    }

    [Fact]
    public async Task ConfirmAsync_Should_save_reviewed_input_then_confirm_and_publish_once()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var proposed = CreateInput(
            metricValue: 125m,
            metricSource: FinancialMetricCandidateSourceKinds.HumanCorrected);
        var payload = CreatePayload(
            proposedInput: proposed,
            candidates:
            [
                CreateMetricCandidate(
                    value: 125m,
                    sourceKind: FinancialMetricCandidateSourceKinds.HumanCorrected,
                    reviewState: FinancialMetricCandidateReviewStates.HumanCorrected)
            ]);
        var sessionService = new StubStructuredFinancialMetricsSessionService();
        var publisher = new FakeActivityEventPublisher();
        var service = CreateService(dbContext, sessionService, publisher);
        var draft = await CreateDraftAsync(
            service,
            session.Id,
            userId,
            fileName: "reviewed.pdf",
            contentHash: "reviewed-hash",
            payload: payload);

        var result = await service.ConfirmAsync(
            draft.Id,
            session.Id,
            userId,
            CancellationToken.None);
        var idempotent = await service.ConfirmAsync(
            draft.Id,
            session.Id,
            userId,
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Success);
        result.Draft!.Status.Should().Be("confirmed");
        idempotent.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Success);
        sessionService.SaveRequests.Should().ContainSingle();
        var saveRequest = sessionService.SaveRequests.Single();
        saveRequest.SessionId.Should().Be(session.Id);
        saveRequest.Input.Metrics.Single().Source
            .Should().Be(FinancialMetricCandidateSourceKinds.HumanCorrected);
        saveRequest.Provenance.Should().BeEquivalentTo(
            new StructuredFinancialMetricsProvenanceInput(
                IngestionMethod: "pdf_file_reviewed",
                OriginalFileName: "reviewed.pdf",
                FileSizeBytes: 4096,
                ContentHash: "reviewed-hash"));
        publisher.PublishedEvents.Should().ContainSingle(x =>
            x.SessionId == session.Id &&
            x.Type == "financial_metrics_extraction_review_confirmed");
    }

    [Fact]
    public async Task ConfirmAsync_Should_revalidate_and_keep_invalid_draft_pending()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var invalidInput = CreateInput(metrics: []);
        var payload = CreatePayload(
            proposedInput: invalidInput,
            candidates: [],
            missingFields: []);
        var sessionService = new StubStructuredFinancialMetricsSessionService();
        var publisher = new FakeActivityEventPublisher();
        var service = CreateService(dbContext, sessionService, publisher);
        var draft = await CreateDraftAsync(
            service,
            session.Id,
            userId,
            payload: payload);

        var result = await service.ConfirmAsync(
            draft.Id,
            session.Id,
            userId,
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Invalid);
        result.ValidationIssues.Should().Contain(x => x.Code == "METRICS_REQUIRED");
        sessionService.SaveRequests.Should().BeEmpty();
        publisher.PublishedEvents.Should().BeEmpty();
        (await dbContext.FinancialMetricsExtractionDrafts.SingleAsync(x => x.Id == draft.Id))
            .Status.Should().Be(FinancialMetricsExtractionDraftStatus.PendingReview);
    }

    [Theory]
    [InlineData("null", FinancialMetricsExtractionDraftResultKind.NotFound)]
    [InlineData("invalid", FinancialMetricsExtractionDraftResultKind.Invalid)]
    [InlineData("throws", FinancialMetricsExtractionDraftResultKind.Conflict)]
    public async Task ConfirmAsync_Should_keep_pending_when_session_save_fails(
        string failureMode,
        FinancialMetricsExtractionDraftResultKind expectedKind)
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var sessionService = new StubStructuredFinancialMetricsSessionService
        {
            FailureMode = failureMode
        };
        var publisher = new FakeActivityEventPublisher();
        var service = CreateService(dbContext, sessionService, publisher);
        var draft = await CreateDraftAsync(service, session.Id, userId);

        var result = await service.ConfirmAsync(
            draft.Id,
            session.Id,
            userId,
            CancellationToken.None);

        result.Kind.Should().Be(expectedKind);
        publisher.PublishedEvents.Should().BeEmpty();
        (await dbContext.FinancialMetricsExtractionDrafts.SingleAsync(x => x.Id == draft.Id))
            .Status.Should().Be(FinancialMetricsExtractionDraftStatus.PendingReview);
    }

    [Fact]
    public async Task DiscardAsync_Should_leave_active_context_unchanged_and_publish_once()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        session.SetContext("""{"structuredFinancialMetrics":{"documentId":"existing"}}""");
        await dbContext.SaveChangesAsync();
        var originalContext = session.ContextJson;
        var sessionService = new StubStructuredFinancialMetricsSessionService();
        var publisher = new FakeActivityEventPublisher();
        var service = CreateService(dbContext, sessionService, publisher);
        var draft = await CreateDraftAsync(service, session.Id, userId);

        var result = await service.DiscardAsync(
            draft.Id,
            session.Id,
            userId,
            CancellationToken.None);
        var idempotent = await service.DiscardAsync(
            draft.Id,
            session.Id,
            userId,
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Success);
        result.Draft!.Status.Should().Be("discarded");
        idempotent.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Success);
        session.ContextJson.Should().Be(originalContext);
        sessionService.SaveRequests.Should().BeEmpty();
        publisher.PublishedEvents.Should().ContainSingle(x =>
            x.SessionId == session.Id &&
            x.Type == "financial_metrics_extraction_review_discarded");
    }

    [Fact]
    public async Task Terminal_drafts_Should_reject_updates_and_cross_terminal_actions()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var confirmedSession = await AddSessionAsync(dbContext, userId);
        var discardedSession = await AddSessionAsync(dbContext, userId);
        var service = CreateService(dbContext);
        var confirmed = await CreateDraftAsync(
            service,
            confirmedSession.Id,
            userId);
        var discarded = await CreateDraftAsync(
            service,
            discardedSession.Id,
            userId);
        var confirmedEntity = await dbContext.FinancialMetricsExtractionDrafts
            .SingleAsync(x => x.Id == confirmed.Id);
        var discardedEntity = await dbContext.FinancialMetricsExtractionDrafts
            .SingleAsync(x => x.Id == discarded.Id);
        confirmedEntity.Confirm(userId, DateTimeOffset.UtcNow.AddMinutes(1));
        discardedEntity.Discard(userId, DateTimeOffset.UtcNow.AddMinutes(1));
        await dbContext.SaveChangesAsync();
        var update = new UpdateFinancialMetricsExtractionDraftRequest(
            Candidates:
            [
                Decision(
                    confirmed.Payload.Candidates.Single().Id,
                    FinancialMetricCandidateReviewStates.Accepted)
            ],
            ProposedInput: confirmed.Payload.ProposedInput);

        var updateConfirmed = await service.UpdateAsync(
            confirmed.Id,
            confirmedSession.Id,
            userId,
            update,
            CancellationToken.None);
        var confirmDiscarded = await service.ConfirmAsync(
            discarded.Id,
            discardedSession.Id,
            userId,
            CancellationToken.None);
        var discardConfirmed = await service.DiscardAsync(
            confirmed.Id,
            confirmedSession.Id,
            userId,
            CancellationToken.None);

        updateConfirmed.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Conflict);
        confirmDiscarded.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Conflict);
        discardConfirmed.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Conflict);
    }

    [Fact]
    public async Task Every_operation_Should_enforce_ownership()
    {
        await using var dbContext = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, ownerId);
        var service = CreateService(dbContext);
        var createAsOther = await service.CreateOrReplaceAsync(
            session.Id,
            otherUserId,
            CreateRequest(),
            CancellationToken.None);
        var draft = await CreateDraftAsync(service, session.Id, ownerId);
        var update = new UpdateFinancialMetricsExtractionDraftRequest(
            Candidates:
            [
                Decision(
                    draft.Payload.Candidates.Single().Id,
                    FinancialMetricCandidateReviewStates.Accepted)
            ],
            ProposedInput: draft.Payload.ProposedInput);

        var getAsOther = await service.GetPendingAsync(
            session.Id,
            otherUserId,
            CancellationToken.None);
        var updateAsOther = await service.UpdateAsync(
            draft.Id,
            session.Id,
            otherUserId,
            update,
            CancellationToken.None);
        var confirmAsOther = await service.ConfirmAsync(
            draft.Id,
            session.Id,
            otherUserId,
            CancellationToken.None);
        var discardAsOther = await service.DiscardAsync(
            draft.Id,
            session.Id,
            otherUserId,
            CancellationToken.None);

        createAsOther.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.NotFound);
        getAsOther.Should().BeNull();
        updateAsOther.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.NotFound);
        confirmAsOther.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.NotFound);
        discardAsOther.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.NotFound);
    }

    [Theory]
    [InlineData("""{"schemaVersion":2}""")]
    [InlineData("""{"schemaVersion":1,"proposedInput":null,"candidates":null}""")]
    [InlineData("""
        {
          "schemaVersion": 1,
          "proposedInput": {
            "documentId": "document-1",
            "company": "Acme",
            "currency": "USD",
            "unit": "USD_million",
            "metrics": []
          },
          "candidates": [],
          "conflicts": [
            {
              "kind": "metric_value",
              "fieldName": "value",
              "metricName": "revenue",
              "period": "2025A",
              "proposedValue": "100",
              "metricCandidates": [null],
              "metadataCandidates": []
            }
          ],
          "missingFields": [],
          "fallbackReasons": [],
          "validationIssues": [],
          "diagnostics": { "reasonCodes": [] },
          "metadataCandidates": []
        }
        """)]
    public async Task ConfirmAsync_Should_handle_unsupported_or_malformed_payload_safely(
        string payloadJson)
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var entity = FinancialMetricsExtractionDraft.Create(
            session.Id,
            userId,
            "malformed.pdf",
            100,
            "malformed-hash",
            payloadJson,
            DateTimeOffset.UtcNow);
        dbContext.FinancialMetricsExtractionDrafts.Add(entity);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        var result = await service.ConfirmAsync(
            entity.Id,
            session.Id,
            userId,
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Invalid);
        entity.Status.Should().Be(FinancialMetricsExtractionDraftStatus.PendingReview);
    }

    private static CountingOrchestrationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<OrchestrationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new CountingOrchestrationDbContext(options);
    }

    private static async Task<AnalysisSession> AddSessionAsync(
        OrchestrationDbContext dbContext,
        Guid userId)
    {
        var session = AnalysisSession.Create(userId);
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        return session;
    }

    private static FinancialMetricsExtractionDraftService CreateService(
        OrchestrationDbContext dbContext,
        StubStructuredFinancialMetricsSessionService? sessionService = null,
        FakeActivityEventPublisher? publisher = null)
    {
        return new FinancialMetricsExtractionDraftService(
            dbContext,
            new StructuredFinancialMetricsValidator(),
            sessionService ?? new StubStructuredFinancialMetricsSessionService(),
            publisher ?? new FakeActivityEventPublisher());
    }

    private static async Task<FinancialMetricsExtractionDraftDto> CreateDraftAsync(
        IFinancialMetricsExtractionDraftService service,
        Guid sessionId,
        Guid userId,
        string fileName = "report.pdf",
        string contentHash = "content-hash",
        FinancialMetricsExtractionDraftPayload? payload = null)
    {
        var result = await service.CreateOrReplaceAsync(
            sessionId,
            userId,
            new CreateFinancialMetricsExtractionDraftRequest(
                OriginalFileName: fileName,
                FileSizeBytes: 4096,
                ContentHash: contentHash,
                Payload: payload ?? CreatePayload()),
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Success);
        return result.Draft!;
    }

    private static CreateFinancialMetricsExtractionDraftRequest CreateRequest()
    {
        return new CreateFinancialMetricsExtractionDraftRequest(
            OriginalFileName: "report.pdf",
            FileSizeBytes: 4096,
            ContentHash: "content-hash",
            Payload: CreatePayload());
    }

    private static FinancialMetricsExtractionDraftPayload CreatePayload(
        StructuredFinancialMetricsInput? proposedInput = null,
        IReadOnlyList<FinancialMetricCandidate>? candidates = null,
        IReadOnlyList<FinancialMetricCandidateConflict>? conflicts = null,
        IReadOnlyList<string>? missingFields = null)
    {
        return new FinancialMetricsExtractionDraftPayload(
            SchemaVersion: FinancialMetricsExtractionDraftPayload.CurrentSchemaVersion,
            ProposedInput: proposedInput ?? CreateInput(),
            Candidates: candidates ?? [CreateMetricCandidate()],
            Conflicts: conflicts ?? [],
            MissingFields: missingFields ?? [],
            FallbackReasons: [],
            ValidationIssues: [],
            Diagnostics: new FinancialMetricsExtractionDiagnostics
            {
                NativeTextAvailable = true,
                MarkItDownAttempted = true,
                MarkItDownSucceeded = true,
                PageCount = 3,
                CandidateCount = candidates?.Count ?? 1,
                ConflictCount = conflicts?.Count ?? 0,
                TotalDurationMilliseconds = 25
            });
    }

    private static StructuredFinancialMetricsInput CreateInput(
        string documentId = "document-1",
        string? company = "Acme",
        decimal metricValue = 100m,
        string metricSource = "reported",
        IReadOnlyList<StructuredFinancialMetricInput>? metrics = null)
    {
        return new StructuredFinancialMetricsInput(
            DocumentId: documentId,
            Company: company,
            Currency: "USD",
            Unit: "USD_million",
            Metrics: metrics ??
            [
                new StructuredFinancialMetricInput(
                    Name: "revenue",
                    Period: "2025A",
                    Value: metricValue,
                    Unit: "USD_million",
                    Currency: "USD",
                    Source: metricSource,
                    SourcePage: 7,
                    Confidence: 0.95m)
            ]);
    }

    private static FinancialMetricCandidate CreateMetricCandidate(
        decimal value = 100m,
        string sourceKind = FinancialMetricCandidateSourceKinds.Reported,
        string reviewState = FinancialMetricCandidateReviewStates.Accepted,
        int? sourcePage = 7)
    {
        return new FinancialMetricCandidate(
            Id: Guid.NewGuid(),
            Name: "revenue",
            Period: "2025A",
            Value: value,
            Currency: "USD",
            Unit: "USD_million",
            SourceKind: sourceKind,
            Confidence: 0.95m,
            SourcePage: sourcePage,
            Evidence: "Revenue 100",
            ExtractionStrategy: "native_table",
            ReviewState: reviewState,
            InferenceExplanation: null);
    }

    private static FinancialDocumentMetadataCandidate CreateMetadataCandidate(
        string fieldName = "company",
        string value = "Acme",
        string reviewState = FinancialMetricCandidateReviewStates.Accepted)
    {
        return new FinancialDocumentMetadataCandidate(
            Id: Guid.NewGuid(),
            FieldName: fieldName,
            Value: value,
            SourceKind: FinancialMetricCandidateSourceKinds.Reported,
            Confidence: 0.95m,
            SourcePage: 1,
            Evidence: value,
            ExtractionStrategy: "native_text",
            ReviewState: reviewState,
            InferenceExplanation: null);
    }

    private static FinancialMetricCandidateConflict CreateMetricConflict(
        params FinancialMetricCandidate[] candidates)
    {
        return new FinancialMetricCandidateConflict(
            Kind: "metric_value",
            FieldName: "value",
            MetricName: "revenue",
            Period: "2025A",
            ProposedValue: "100",
            MetricCandidates: candidates,
            MetadataCandidates: []);
    }

    private static FinancialMetricCandidateReviewUpdate Decision(
        Guid candidateId,
        string decision)
    {
        return new FinancialMetricCandidateReviewUpdate(
            CandidateId: candidateId,
            Decision: decision,
            Value: null,
            Currency: null,
            Unit: null,
            MetadataValue: null);
    }

    private sealed class CountingOrchestrationDbContext(
        DbContextOptions<OrchestrationDbContext> options)
        : OrchestrationDbContext(options)
    {
        public int SaveChangesCount { get; private set; }

        public override Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            SaveChangesCount++;
            return base.SaveChangesAsync(cancellationToken);
        }

        public void ResetSaveChangesCount()
        {
            SaveChangesCount = 0;
        }
    }

    private sealed class StubStructuredFinancialMetricsSessionService
        : IStructuredFinancialMetricsSessionService
    {
        public List<SaveStructuredFinancialMetricsRequest> SaveRequests { get; } = [];

        public string? FailureMode { get; init; }

        public Task<FinancialMetricsSessionSaveResult?> SaveAsync(
            Guid sessionId,
            StructuredFinancialMetricsInput input,
            CancellationToken cancellationToken)
        {
            return SaveAsync(
                new SaveStructuredFinancialMetricsRequest(sessionId, input, null),
                cancellationToken);
        }

        public Task<FinancialMetricsSessionSaveResult?> SaveAsync(
            SaveStructuredFinancialMetricsRequest request,
            CancellationToken cancellationToken)
        {
            SaveRequests.Add(request);

            if (FailureMode == "throws")
            {
                throw new InvalidOperationException("Session save failed.");
            }

            if (FailureMode == "null")
            {
                return Task.FromResult<FinancialMetricsSessionSaveResult?>(null);
            }

            if (FailureMode == "invalid")
            {
                return Task.FromResult<FinancialMetricsSessionSaveResult?>(
                    new FinancialMetricsSessionSaveResult(
                        request.SessionId,
                        false,
                        null,
                        [
                            new FinancialMetricsValidationIssue(
                                "SESSION_SAVE_INVALID",
                                "Session save rejected input.",
                                null,
                                null,
                                "Error")
                        ],
                        []));
            }

            return Task.FromResult<FinancialMetricsSessionSaveResult?>(
                new FinancialMetricsSessionSaveResult(
                    request.SessionId,
                    true,
                    new StructuredFinancialMetricsContext(
                        request.Input.DocumentId,
                        request.Input.Company,
                        request.Input.Currency,
                        request.Input.Unit,
                        [],
                        [],
                        DateTimeOffset.UtcNow),
                    [],
                    []));
        }

        public Task<StructuredFinancialMetricsContext?> GetAsync(
            Guid sessionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<StructuredFinancialMetricsContext?>(null);
        }
    }
}
