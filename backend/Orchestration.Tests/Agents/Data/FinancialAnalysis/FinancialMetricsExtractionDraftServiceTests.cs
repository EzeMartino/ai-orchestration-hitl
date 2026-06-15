using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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

    [Theory]
    [InlineData("concurrency")]
    [InlineData("update")]
    public async Task CreateOrReplaceAsync_Should_retry_in_same_context_after_save_failure(
        string exceptionKind)
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var service = CreateService(dbContext);
        await CreateDraftAsync(
            service,
            session.Id,
            userId,
            fileName: "old.pdf",
            contentHash: "old-hash");
        var request = new CreateFinancialMetricsExtractionDraftRequest(
            OriginalFileName: "replacement.pdf",
            FileSizeBytes: 8192,
            ContentHash: "replacement-hash",
            Payload: CreatePayload());
        dbContext.NextSaveException = CreateSaveException(exceptionKind);

        var failed = await service.CreateOrReplaceAsync(
            session.Id,
            userId,
            request,
            CancellationToken.None);
        var retry = await service.CreateOrReplaceAsync(
            session.Id,
            userId,
            request,
            CancellationToken.None);

        failed.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Conflict);
        retry.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Success);
        var persisted = await dbContext.FinancialMetricsExtractionDrafts
            .Where(x =>
                x.SessionId == session.Id &&
                x.Status == FinancialMetricsExtractionDraftStatus.PendingReview)
            .ToArrayAsync();
        persisted.Should().ContainSingle();
        persisted.Single().ContentHash.Should().Be("replacement-hash");
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

        own.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Success);
        own.Draft!.Id.Should().Be(created.Id);
        otherUser.Kind.Should().Be(
            FinancialMetricsExtractionDraftResultKind.NotFound);
        otherSessionResult.Kind.Should().Be(
            FinancialMetricsExtractionDraftResultKind.NotFound);
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

    [Theory]
    [InlineData("concurrency")]
    [InlineData("update")]
    public async Task UpdateAsync_Should_retry_in_same_context_after_save_failure(
        string exceptionKind)
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var service = CreateService(dbContext);
        var draft = await CreateDraftAsync(service, session.Id, userId);
        var request = CreateMetricAdditionUpdate(draft, 225m);
        dbContext.NextSaveException = CreateSaveException(exceptionKind);

        var failed = await service.UpdateAsync(
            draft.Id,
            session.Id,
            userId,
            request,
            CancellationToken.None);
        var retry = await service.UpdateAsync(
            draft.Id,
            session.Id,
            userId,
            request,
            CancellationToken.None);

        failed.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Conflict);
        retry.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Success);
        retry.Draft!.Payload.Candidates.Should().ContainSingle(x =>
            x.Name == "ebitda" &&
            x.Value == 225m &&
            x.ReviewState ==
                FinancialMetricCandidateReviewStates.HumanCorrected);
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
    public async Task UpdateAsync_Should_reject_changed_metric_without_selected_candidate()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var candidate = CreateMetricCandidate(
            reviewState: FinancialMetricCandidateReviewStates.Inferred);
        var service = CreateService(dbContext);
        var draft = await CreateDraftAsync(
            service,
            session.Id,
            userId,
            payload: CreatePayload(candidates: [candidate]));
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
                    Decision(
                        candidate.Id,
                        FinancialMetricCandidateReviewStates.Rejected)
                ],
                ProposedInput: CreateInput(metricValue: 125m)),
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Invalid);
        result.Errors.Should().ContainSingle().Which.Should().Be(
            "Each proposed metric must have exactly one accepted or human-corrected resolution.");
        entity.PayloadJson.Should().Be(originalJson);
    }

    [Fact]
    public async Task UpdateAsync_Should_reject_changed_metadata_without_selected_candidate()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var metadata = CreateMetadataCandidate(
            value: "Acme",
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
                    Decision(
                        metadata.Id,
                        FinancialMetricCandidateReviewStates.Rejected)
                ],
                ProposedInput: CreateInput(company: "Other")),
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Invalid);
        result.Errors.Should().ContainSingle().Which.Should().Be(
            "Each proposed metadata value must have exactly one accepted or human-corrected resolution.");
        entity.PayloadJson.Should().Be(originalJson);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("source_page")]
    [InlineData("confidence")]
    public async Task UpdateAsync_Should_reject_accepted_metric_provenance_tampering(
        string field)
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var candidate = CreateMetricCandidate();
        var service = CreateService(dbContext);
        var draft = await CreateDraftAsync(
            service,
            session.Id,
            userId,
            payload: CreatePayload(candidates: [candidate]));
        var entity = await dbContext.FinancialMetricsExtractionDrafts
            .SingleAsync(x => x.Id == draft.Id);
        var originalJson = entity.PayloadJson;
        var proposedMetric = CreateInput().Metrics.Single();
        proposedMetric = field switch
        {
            "source" => proposedMetric with
            {
                Source = FinancialMetricCandidateSourceKinds.Inferred
            },
            "source_page" => proposedMetric with
            {
                SourcePage = 99
            },
            "confidence" => proposedMetric with
            {
                Confidence = 0.1m
            },
            _ => throw new InvalidOperationException()
        };

        var result = await service.UpdateAsync(
            draft.Id,
            session.Id,
            userId,
            new UpdateFinancialMetricsExtractionDraftRequest(
                Candidates:
                [
                    Decision(
                        candidate.Id,
                        FinancialMetricCandidateReviewStates.Accepted)
                ],
                ProposedInput: CreateInput(metrics: [proposedMetric])),
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Invalid);
        result.Errors.Should().ContainSingle().Which.Should().Be(
            "Each selected metric resolution must match the proposed input.");
        entity.PayloadJson.Should().Be(originalJson);
    }

    [Theory]
    [InlineData("deterministic")]
    [InlineData("semantic")]
    public async Task UpdateAsync_Should_accept_reconciler_source_round_trip(
        string candidateOrigin)
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var reconciliation = CreateReconciliation(candidateOrigin);
        var service = CreateService(dbContext);
        var draft = await CreateDraftAsync(
            service,
            session.Id,
            userId,
            payload: CreateReconciledPayload(reconciliation));
        var decisions = reconciliation.Candidates
            .Select(candidate => Decision(
                candidate.Id,
                FinancialMetricCandidateReviewStates.Accepted))
            .Concat(reconciliation.MetadataCandidates.Select(candidate =>
                Decision(
                    candidate.Id,
                    FinancialMetricCandidateReviewStates.Accepted)))
            .ToArray();

        var result = await service.UpdateAsync(
            draft.Id,
            session.Id,
            userId,
            new UpdateFinancialMetricsExtractionDraftRequest(
                Candidates: decisions,
                ProposedInput: reconciliation.ProposedInput),
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Success);
        result.Draft!.Payload.ProposedInput.Metrics.Single().Source.Should().Be(
            reconciliation.ProposedInput.Metrics.Single().Source);
    }

    [Theory]
    [InlineData("borrowed_currency", 2)]
    [InlineData("borrowed_unit", 2)]
    [InlineData("compatible_multi_support", 3)]
    public async Task Update_and_confirm_Should_accept_reconciler_composite_proposal(
        string composition,
        int expectedSelectedCandidates)
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var reconciliation = CreateCompositeReconciliation(composition);
        var service = CreateService(dbContext);
        var draft = await CreateDraftAsync(
            service,
            session.Id,
            userId,
            payload: CreateReconciledPayload(reconciliation));

        var updated = await service.UpdateAsync(
            draft.Id,
            session.Id,
            userId,
            new UpdateFinancialMetricsExtractionDraftRequest(
                Candidates: SelectAll(reconciliation),
                ProposedInput: reconciliation.ProposedInput),
            CancellationToken.None);
        var confirmed = await service.ConfirmAsync(
            draft.Id,
            session.Id,
            userId,
            CancellationToken.None);

        updated.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Success);
        updated.Draft!.Payload.Candidates.Should().Contain(candidate =>
            candidate.ReviewState == FinancialMetricCandidateReviewStates.Accepted);
        updated.Draft.Payload.Candidates.Count(candidate =>
            candidate.ReviewState == FinancialMetricCandidateReviewStates.Accepted)
            .Should().Be(expectedSelectedCandidates);
        confirmed.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Success);
        confirmed.Draft!.Status.Should().Be("confirmed");
    }

    [Fact]
    public async Task UpdateAsync_Should_reject_tampered_reconciler_composite_proposal()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var reconciliation = CreateCompositeReconciliation("borrowed_currency");
        var proposedMetric = reconciliation.ProposedInput.Metrics.Single() with
        {
            Unit = "shares"
        };
        var service = CreateService(dbContext);
        var draft = await CreateDraftAsync(
            service,
            session.Id,
            userId,
            payload: CreateReconciledPayload(reconciliation));

        var result = await service.UpdateAsync(
            draft.Id,
            session.Id,
            userId,
            new UpdateFinancialMetricsExtractionDraftRequest(
                Candidates: SelectAll(reconciliation),
                ProposedInput: reconciliation.ProposedInput with
                {
                    Metrics = [proposedMetric]
                }),
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Invalid);
        result.Errors.Should().ContainSingle().Which.Should().Be(
            "Selected metric supporters are incompatible with the proposed input.");
    }

    [Fact]
    public async Task UpdateAsync_Should_reject_incompatible_composite_field_mixing()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var primary = CreateMetricCandidate(
            reviewState: FinancialMetricCandidateReviewStates.Explicit) with
        {
            Currency = null,
            Unit = "shares"
        };
        var incompatibleSupporter = CreateMetricCandidate(
            reviewState: FinancialMetricCandidateReviewStates.Explicit) with
        {
            Currency = "USD",
            Unit = "USD_million",
            Confidence = 0.9m
        };
        var payload = CreatePayload(
            proposedInput: CreateInput(metrics:
            [
                CreateInput().Metrics.Single() with
                {
                    Currency = null,
                    Unit = "shares"
                }
            ]),
            candidates: [primary, incompatibleSupporter]);
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
                    Decision(
                        primary.Id,
                        FinancialMetricCandidateReviewStates.Accepted),
                    Decision(
                        incompatibleSupporter.Id,
                        FinancialMetricCandidateReviewStates.Accepted)
                ],
                ProposedInput: CreateInput(metrics:
                [
                    CreateInput().Metrics.Single() with
                    {
                        Currency = "USD",
                        Unit = "shares"
                    }
                ])),
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Invalid);
        result.Errors.Should().ContainSingle().Which.Should().Be(
            "Selected metric supporters are incompatible with the proposed input.");
    }

    [Fact]
    public async Task UpdateAsync_Should_reject_competing_primary_candidates()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var first = CreateMetricCandidate(
            reviewState: FinancialMetricCandidateReviewStates.Explicit);
        var second = first with
        {
            Id = Guid.NewGuid()
        };
        var service = CreateService(dbContext);
        var draft = await CreateDraftAsync(
            service,
            session.Id,
            userId,
            payload: CreatePayload(candidates: [first, second]));

        var result = await service.UpdateAsync(
            draft.Id,
            session.Id,
            userId,
            new UpdateFinancialMetricsExtractionDraftRequest(
                Candidates:
                [
                    Decision(first.Id, FinancialMetricCandidateReviewStates.Accepted),
                    Decision(second.Id, FinancialMetricCandidateReviewStates.Accepted)
                ],
                ProposedInput: CreateInput()),
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Invalid);
        result.Errors.Should().ContainSingle().Which.Should().Be(
            "Each proposed metric must have exactly one selected primary candidate.");
    }

    [Theory]
    [InlineData("deterministic")]
    [InlineData("semantic")]
    public async Task UpdateAsync_Should_reject_reconciler_source_kind_tampering(
        string candidateOrigin)
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var reconciliation = CreateReconciliation(candidateOrigin);
        var candidate = reconciliation.Candidates.Single();
        var proposedMetric = reconciliation.ProposedInput.Metrics.Single() with
        {
            Source = candidate.SourceKind
        };
        var service = CreateService(dbContext);
        var draft = await CreateDraftAsync(
            service,
            session.Id,
            userId,
            payload: CreateReconciledPayload(reconciliation));
        var decisions = reconciliation.Candidates
            .Select(item => Decision(
                item.Id,
                FinancialMetricCandidateReviewStates.Accepted))
            .Concat(reconciliation.MetadataCandidates.Select(item =>
                Decision(
                    item.Id,
                    FinancialMetricCandidateReviewStates.Accepted)))
            .ToArray();

        var result = await service.UpdateAsync(
            draft.Id,
            session.Id,
            userId,
            new UpdateFinancialMetricsExtractionDraftRequest(
                Candidates: decisions,
                ProposedInput: reconciliation.ProposedInput with
                {
                    Metrics = [proposedMetric]
                }),
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Invalid);
        result.Errors.Should().ContainSingle().Which.Should().Be(
            "Each selected metric resolution must match the proposed input.");
    }

    [Fact]
    public async Task UpdateAsync_Should_reject_noncanonical_human_correction_provenance()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var original = CreateMetricCandidate(
            reviewState: FinancialMetricCandidateReviewStates.Inferred);
        var service = CreateService(dbContext);
        var draft = await CreateDraftAsync(
            service,
            session.Id,
            userId,
            payload: CreatePayload(candidates: [original]));
        var proposedMetric = CreateInput(
            metricValue: 125m,
            metricSource: FinancialMetricCandidateSourceKinds.HumanCorrected)
            .Metrics.Single() with
        {
            SourcePage = 99,
            Confidence = 0.1m
        };

        var result = await service.UpdateAsync(
            draft.Id,
            session.Id,
            userId,
            new UpdateFinancialMetricsExtractionDraftRequest(
                Candidates: [HumanMetricCorrection(original.Id, 125m)],
                ProposedInput: CreateInput(metrics: [proposedMetric])),
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Invalid);
        result.Errors.Should().ContainSingle().Which.Should().Be(
            "Each selected metric resolution must match the proposed input.");
    }

    [Theory]
    [InlineData("company")]
    [InlineData("currency")]
    [InlineData("unit")]
    public async Task UpdateAsync_Should_reject_direct_metadata_fill_without_candidate(
        string fieldName)
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var originalInput = WithoutMetadataField(CreateInput(), fieldName);
        var payload = CreatePayload(proposedInput: originalInput) with
        {
            MetadataCandidates = CreateMetadataCandidates(originalInput)
        };
        var service = CreateService(dbContext);
        var draft = await CreateDraftAsync(
            service,
            session.Id,
            userId,
            payload: payload);
        var entity = await dbContext.FinancialMetricsExtractionDrafts
            .SingleAsync(x => x.Id == draft.Id);
        var originalJson = entity.PayloadJson;

        var result = await service.UpdateAsync(
            draft.Id,
            session.Id,
            userId,
            new UpdateFinancialMetricsExtractionDraftRequest(
                Candidates: [],
                ProposedInput: CreateInput()),
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Invalid);
        result.Errors.Should().ContainSingle().Which.Should().Be(
            "Each proposed metadata value must have exactly one accepted or human-corrected resolution.");
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
        result.Draft.Payload.Conflicts.Should().ContainSingle();
        result.Draft.Payload.Conflicts.Single().Should().Match(
            (FinancialMetricCandidateConflict item) =>
                item.IsResolved &&
                item.SelectedCandidateId == accepted.Id &&
                item.ResolutionDecision ==
                FinancialMetricCandidateReviewStates.Accepted);
        result.Draft.Payload.MissingFields.Should().BeEmpty();

        var confirmed = await service.ConfirmAsync(
            draft.Id,
            session.Id,
            userId,
            CancellationToken.None);

        confirmed.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Success);
    }

    [Fact]
    public async Task UpdateAsync_Should_track_human_corrected_conflict_resolution()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var correctedOriginal = CreateMetricCandidate(
            value: 100m,
            reviewState: FinancialMetricCandidateReviewStates.Conflict);
        var rejected = CreateMetricCandidate(
            value: 120m,
            reviewState: FinancialMetricCandidateReviewStates.Conflict);
        var service = CreateService(dbContext);
        var draft = await CreateDraftAsync(
            service,
            session.Id,
            userId,
            payload: CreatePayload(
                candidates: [correctedOriginal, rejected],
                conflicts: [CreateMetricConflict(correctedOriginal, rejected)]));
        var proposedMetric = CreateInput(
            metricValue: 125m,
            metricSource: FinancialMetricCandidateSourceKinds.HumanCorrected)
            .Metrics.Single() with
        {
            Confidence = 1m
        };

        var result = await service.UpdateAsync(
            draft.Id,
            session.Id,
            userId,
            new UpdateFinancialMetricsExtractionDraftRequest(
                Candidates:
                [
                    HumanMetricCorrection(correctedOriginal.Id, 125m),
                    Decision(rejected.Id, FinancialMetricCandidateReviewStates.Rejected)
                ],
                ProposedInput: CreateInput(metrics: [proposedMetric])),
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Success);
        var corrected = result.Draft!.Payload.Candidates.Single(candidate =>
            candidate.ReviewState ==
            FinancialMetricCandidateReviewStates.HumanCorrected);
        result.Draft.Payload.Conflicts.Should().ContainSingle();
        result.Draft.Payload.Conflicts.Single().Should().Match(
            (FinancialMetricCandidateConflict item) =>
                item.IsResolved &&
                item.SelectedCandidateId == corrected.Id &&
                item.ResolutionDecision ==
                FinancialMetricCandidateReviewStates.HumanCorrected &&
                item.MetricCandidates.Any(candidate =>
                    candidate.Id == corrected.Id));
    }

    [Fact]
    public async Task UpdateAsync_Should_reject_metric_value_conflict_when_accepted_value_differs_from_proposal()
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
        var payload = CreatePayload(
            candidates: [accepted, rejected],
            conflicts: [CreateMetricConflict(accepted, rejected)]);
        var service = CreateService(dbContext);
        var draft = await CreateDraftAsync(
            service,
            session.Id,
            userId,
            payload: payload);
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
                    Decision(accepted.Id, FinancialMetricCandidateReviewStates.Accepted),
                    Decision(rejected.Id, FinancialMetricCandidateReviewStates.Rejected)
                ],
                ProposedInput: CreateInput(metricValue: 999m)),
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Invalid);
        entity.PayloadJson.Should().Be(originalJson);
    }

    [Fact]
    public async Task UpdateAsync_Should_reject_metric_currency_conflict_when_accepted_currency_differs_from_proposal()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var accepted = CreateMetricCandidate(
            reviewState: FinancialMetricCandidateReviewStates.Conflict) with
        {
            Currency = "USD"
        };
        var rejected = CreateMetricCandidate(
            reviewState: FinancialMetricCandidateReviewStates.Conflict) with
        {
            Currency = "EUR"
        };
        var conflict = new FinancialMetricCandidateConflict(
            Kind: "metric_currency",
            FieldName: "currency",
            MetricName: "revenue",
            Period: "2025A",
            ProposedValue: "USD",
            MetricCandidates: [accepted, rejected],
            MetadataCandidates: []);
        var payload = CreatePayload(
            candidates: [accepted, rejected],
            conflicts: [conflict]);
        var proposal = CreateInput(metrics:
        [
            CreateInput().Metrics.Single() with
            {
                Currency = "EUR"
            }
        ]);
        var service = CreateService(dbContext);
        var draft = await CreateDraftAsync(
            service,
            session.Id,
            userId,
            payload: payload);
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
                    Decision(accepted.Id, FinancialMetricCandidateReviewStates.Accepted),
                    Decision(rejected.Id, FinancialMetricCandidateReviewStates.Rejected)
                ],
                ProposedInput: proposal),
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Invalid);
        entity.PayloadJson.Should().Be(originalJson);
    }

    [Fact]
    public async Task UpdateAsync_Should_reject_metadata_conflict_when_accepted_value_differs_from_proposal()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var accepted = CreateMetadataCandidate(
            value: "Acme",
            reviewState: FinancialMetricCandidateReviewStates.Conflict);
        var rejected = CreateMetadataCandidate(
            value: "Other",
            reviewState: FinancialMetricCandidateReviewStates.Conflict);
        var conflict = new FinancialMetricCandidateConflict(
            Kind: "metadata_value",
            FieldName: "company",
            MetricName: null,
            Period: null,
            ProposedValue: "Acme",
            MetricCandidates: [],
            MetadataCandidates: [accepted, rejected]);
        var payload = CreatePayload(conflicts: [conflict]) with
        {
            MetadataCandidates = [accepted, rejected]
        };
        var service = CreateService(dbContext);
        var draft = await CreateDraftAsync(
            service,
            session.Id,
            userId,
            payload: payload);
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
                    Decision(accepted.Id, FinancialMetricCandidateReviewStates.Accepted),
                    Decision(rejected.Id, FinancialMetricCandidateReviewStates.Rejected)
                ],
                ProposedInput: CreateInput(company: "Other")),
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Invalid);
        entity.PayloadJson.Should().Be(originalJson);
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
        var proposedMetric = CreateInput(
            metricValue: 125m,
            metricSource: FinancialMetricCandidateSourceKinds.HumanCorrected)
            .Metrics.Single() with
        {
            Confidence = 1m
        };
        var proposed = CreateInput(metrics: [proposedMetric]);

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
            MetadataCandidates =
            [
                metadata,
                .. CreateMetadataCandidates(CreateInput())
                    .Where(candidate => candidate.FieldName != "company")
            ]
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
        metadataCandidates.Should().HaveCount(4);
        metadataCandidates.Count(x => x.FieldName == "company").Should().Be(2);
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

    [Fact]
    public async Task UpdateAsync_Should_add_audited_human_corrected_metric_candidate()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var originalInput = CreateInput();
        var addedMetric = new StructuredFinancialMetricInput(
            Name: "ebitda",
            Period: "2025A",
            Value: 75m,
            Unit: "USD_million",
            Currency: "USD",
            Source: FinancialMetricCandidateSourceKinds.HumanCorrected,
            SourcePage: null,
            Confidence: 1m);
        var proposedInput = originalInput with
        {
            Metrics = [.. originalInput.Metrics, addedMetric]
        };
        var service = CreateService(dbContext);
        var draft = await CreateDraftAsync(
            service,
            session.Id,
            userId,
            payload: CreatePayload(proposedInput: originalInput));

        var result = await service.UpdateAsync(
            draft.Id,
            session.Id,
            userId,
            new UpdateFinancialMetricsExtractionDraftRequest(
                Candidates: [],
                ProposedInput: proposedInput)
            {
                MetricAdditions =
                [
                    new FinancialMetricCandidateAddition(
                        Name: " ebitda ",
                        Period: " 2025A ",
                        Value: 75m,
                        Currency: " USD ",
                        Unit: " USD_million ")
                ]
            },
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Success);
        var added = result.Draft!.Payload.Candidates.Single(x =>
            x.Name == "ebitda");
        added.Id.Should().NotBe(Guid.Empty);
        added.Period.Should().Be("2025A");
        added.Value.Should().Be(75m);
        added.Currency.Should().Be("USD");
        added.Unit.Should().Be("USD_million");
        added.SourceKind.Should().Be(
            FinancialMetricCandidateSourceKinds.HumanCorrected);
        added.Confidence.Should().Be(1m);
        added.SourcePage.Should().BeNull();
        added.Evidence.Should().BeEmpty();
        added.ExtractionStrategy.Should().Be("human_review");
        added.ReviewState.Should().Be(
            FinancialMetricCandidateReviewStates.HumanCorrected);
        added.InferenceExplanation.Should().BeNull();
        result.Draft.Payload.MissingFields.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_Should_add_audited_human_corrected_metadata_candidate()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var originalInput = CreateInput(company: null);
        var service = CreateService(dbContext);
        var draft = await CreateDraftAsync(
            service,
            session.Id,
            userId,
            payload: CreatePayload(proposedInput: originalInput));

        var result = await service.UpdateAsync(
            draft.Id,
            session.Id,
            userId,
            new UpdateFinancialMetricsExtractionDraftRequest(
                Candidates: [],
                ProposedInput: CreateInput(company: "New Co"))
            {
                MetadataAdditions =
                [
                    new FinancialDocumentMetadataCandidateAddition(
                        FieldName: " company ",
                        Value: " New Co ")
                ]
            },
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Success);
        var added = result.Draft!.Payload.MetadataCandidates.Single(x =>
            x.FieldName == "company");
        added.Id.Should().NotBe(Guid.Empty);
        added.Value.Should().Be("New Co");
        added.SourceKind.Should().Be(
            FinancialMetricCandidateSourceKinds.HumanCorrected);
        added.Confidence.Should().Be(1m);
        added.SourcePage.Should().BeNull();
        added.Evidence.Should().BeEmpty();
        added.ExtractionStrategy.Should().Be("human_review");
        added.ReviewState.Should().Be(
            FinancialMetricCandidateReviewStates.HumanCorrected);
        added.InferenceExplanation.Should().BeNull();
        result.Draft.Payload.MissingFields.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_Should_reject_malformed_metric_addition()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var service = CreateService(dbContext);
        var draft = await CreateDraftAsync(service, session.Id, userId);

        var result = await service.UpdateAsync(
            draft.Id,
            session.Id,
            userId,
            new UpdateFinancialMetricsExtractionDraftRequest(
                Candidates: [],
                ProposedInput: draft.Payload.ProposedInput)
            {
                MetricAdditions =
                [
                    new FinancialMetricCandidateAddition(
                        Name: " ",
                        Period: "2025A",
                        Value: 75m,
                        Currency: "USD",
                        Unit: "USD_million")
                ]
            },
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Invalid);
        result.Errors.Should().ContainSingle().Which.Should().Be(
            "Metric candidate additions contain a malformed entry.");
    }

    [Fact]
    public async Task UpdateAsync_Should_reject_malformed_metadata_addition()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var service = CreateService(dbContext);
        var draft = await CreateDraftAsync(service, session.Id, userId);

        var result = await service.UpdateAsync(
            draft.Id,
            session.Id,
            userId,
            new UpdateFinancialMetricsExtractionDraftRequest(
                Candidates: [],
                ProposedInput: draft.Payload.ProposedInput)
            {
                MetadataAdditions =
                [
                    new FinancialDocumentMetadataCandidateAddition(
                        FieldName: "company",
                        Value: " ")
                ]
            },
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Invalid);
        result.Errors.Should().ContainSingle().Which.Should().Be(
            "Metadata candidate additions contain a malformed entry.");
    }

    [Fact]
    public async Task UpdateAsync_Should_reject_duplicate_metric_additions()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var service = CreateService(dbContext);
        var draft = await CreateDraftAsync(service, session.Id, userId);
        var addition = new FinancialMetricCandidateAddition(
            Name: "ebitda",
            Period: "2025A",
            Value: 75m,
            Currency: "USD",
            Unit: "USD_million");

        var result = await service.UpdateAsync(
            draft.Id,
            session.Id,
            userId,
            new UpdateFinancialMetricsExtractionDraftRequest(
                Candidates: [],
                ProposedInput: draft.Payload.ProposedInput)
            {
                MetricAdditions =
                [
                    addition,
                    addition with
                    {
                        Name = " EBITDA ",
                        Period = " 2025A "
                    }
                ]
            },
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Invalid);
        result.Errors.Should().ContainSingle().Which.Should().Be(
            "Metric candidate additions cannot contain duplicate keys.");
    }

    [Fact]
    public async Task UpdateAsync_Should_reject_duplicate_metadata_additions()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var service = CreateService(dbContext);
        var draft = await CreateDraftAsync(
            service,
            session.Id,
            userId,
            payload: CreatePayload(proposedInput: CreateInput(company: null)));

        var result = await service.UpdateAsync(
            draft.Id,
            session.Id,
            userId,
            new UpdateFinancialMetricsExtractionDraftRequest(
                Candidates: [],
                ProposedInput: draft.Payload.ProposedInput)
            {
                MetadataAdditions =
                [
                    new FinancialDocumentMetadataCandidateAddition(
                        FieldName: "company",
                        Value: "One"),
                    new FinancialDocumentMetadataCandidateAddition(
                        FieldName: " COMPANY ",
                        Value: "Two")
                ]
            },
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Invalid);
        result.Errors.Should().ContainSingle().Which.Should().Be(
            "Metadata candidate additions cannot contain duplicate fields.");
    }

    [Fact]
    public async Task UpdateAsync_Should_reject_metric_addition_collision()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var service = CreateService(dbContext);
        var draft = await CreateDraftAsync(service, session.Id, userId);

        var result = await service.UpdateAsync(
            draft.Id,
            session.Id,
            userId,
            new UpdateFinancialMetricsExtractionDraftRequest(
                Candidates: [],
                ProposedInput: draft.Payload.ProposedInput)
            {
                MetricAdditions =
                [
                    new FinancialMetricCandidateAddition(
                        Name: " REVENUE ",
                        Period: " 2025A ",
                        Value: 100m,
                        Currency: "USD",
                        Unit: "USD_million")
                ]
            },
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Invalid);
        result.Errors.Should().ContainSingle().Which.Should().Be(
            "Metric candidate addition collides with an existing candidate.");
    }

    [Fact]
    public async Task UpdateAsync_Should_reject_metadata_addition_collision()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var service = CreateService(dbContext);
        var draft = await CreateDraftAsync(service, session.Id, userId);

        var result = await service.UpdateAsync(
            draft.Id,
            session.Id,
            userId,
            new UpdateFinancialMetricsExtractionDraftRequest(
                Candidates: [],
                ProposedInput: draft.Payload.ProposedInput)
            {
                MetadataAdditions =
                [
                    new FinancialDocumentMetadataCandidateAddition(
                        FieldName: " COMPANY ",
                        Value: "Acme")
                ]
            },
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Invalid);
        result.Errors.Should().ContainSingle().Which.Should().Be(
            "Metadata candidate addition collides with an existing candidate.");
    }

    [Fact]
    public async Task UpdateAsync_Should_reject_added_metric_that_differs_from_proposal()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var originalInput = CreateInput();
        var proposedInput = originalInput with
        {
            Metrics =
            [
                .. originalInput.Metrics,
                new StructuredFinancialMetricInput(
                    Name: "ebitda",
                    Period: "2025A",
                    Value: 80m,
                    Unit: "USD_million",
                    Currency: "USD",
                    Source: FinancialMetricCandidateSourceKinds.HumanCorrected,
                    SourcePage: null,
                    Confidence: 1m)
            ]
        };
        var service = CreateService(dbContext);
        var draft = await CreateDraftAsync(
            service,
            session.Id,
            userId,
            payload: CreatePayload(proposedInput: originalInput));

        var result = await service.UpdateAsync(
            draft.Id,
            session.Id,
            userId,
            new UpdateFinancialMetricsExtractionDraftRequest(
                Candidates: [],
                ProposedInput: proposedInput)
            {
                MetricAdditions =
                [
                    new FinancialMetricCandidateAddition(
                        Name: "ebitda",
                        Period: "2025A",
                        Value: 75m,
                        Currency: "USD",
                        Unit: "USD_million")
                ]
            },
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Invalid);
        result.Errors.Should().ContainSingle().Which.Should().Be(
            "Each selected metric resolution must match the proposed input.");
    }

    [Fact]
    public async Task UpdateAsync_Should_reject_two_human_corrections_for_same_metric_key_with_different_values()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var first = CreateMetricCandidate(
            value: 100m,
            reviewState: FinancialMetricCandidateReviewStates.Inferred);
        var second = CreateMetricCandidate(
            value: 110m,
            reviewState: FinancialMetricCandidateReviewStates.Inferred);
        var service = CreateService(dbContext);
        var draft = await CreateDraftAsync(
            service,
            session.Id,
            userId,
            payload: CreatePayload(candidates: [first, second]));
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
                    HumanMetricCorrection(first.Id, 125m),
                    HumanMetricCorrection(second.Id, 130m)
                ],
                ProposedInput: CreateInput(
                    metricValue: 130m,
                    metricSource: FinancialMetricCandidateSourceKinds.HumanCorrected)),
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Invalid);
        entity.PayloadJson.Should().Be(originalJson);
    }

    [Fact]
    public async Task UpdateAsync_Should_reject_two_human_corrections_for_same_metric_key_with_same_value()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var first = CreateMetricCandidate(
            value: 100m,
            reviewState: FinancialMetricCandidateReviewStates.Inferred);
        var second = CreateMetricCandidate(
            value: 110m,
            reviewState: FinancialMetricCandidateReviewStates.Inferred);
        var service = CreateService(dbContext);
        var draft = await CreateDraftAsync(
            service,
            session.Id,
            userId,
            payload: CreatePayload(candidates: [first, second]));
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
                    HumanMetricCorrection(first.Id, 125m),
                    HumanMetricCorrection(second.Id, 125m)
                ],
                ProposedInput: CreateInput(
                    metricValue: 125m,
                    metricSource: FinancialMetricCandidateSourceKinds.HumanCorrected)),
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Invalid);
        entity.PayloadJson.Should().Be(originalJson);
    }

    [Fact]
    public async Task UpdateAsync_Should_reject_duplicate_human_corrections_for_same_metadata_field()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var first = CreateMetadataCandidate(
            value: "Acme One",
            reviewState: FinancialMetricCandidateReviewStates.Inferred);
        var second = CreateMetadataCandidate(
            value: "Acme Two",
            reviewState: FinancialMetricCandidateReviewStates.Inferred);
        var payload = CreatePayload() with
        {
            MetadataCandidates = [first, second]
        };
        var service = CreateService(dbContext);
        var draft = await CreateDraftAsync(
            service,
            session.Id,
            userId,
            payload: payload);
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
                    HumanMetadataCorrection(first.Id, "Corrected Co"),
                    HumanMetadataCorrection(second.Id, "Corrected Co")
                ],
                ProposedInput: CreateInput(company: "Corrected Co")),
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Invalid);
        entity.PayloadJson.Should().Be(originalJson);
    }

    [Theory]
    [InlineData("confidence_null")]
    [InlineData("confidence_low")]
    [InlineData("confidence_high")]
    [InlineData("source_page_zero")]
    [InlineData("source_blank")]
    [InlineData("unit_blank")]
    [InlineData("currency_blank")]
    [InlineData("name_case")]
    [InlineData("name_whitespace")]
    [InlineData("period_case")]
    [InlineData("period_whitespace")]
    [InlineData("source_whitespace")]
    [InlineData("unit_whitespace")]
    [InlineData("currency_whitespace")]
    [InlineData("document_id_whitespace")]
    [InlineData("company_whitespace")]
    [InlineData("document_currency_whitespace")]
    [InlineData("document_unit_whitespace")]
    [InlineData("duplicate_replacement")]
    [InlineData("duplicate_ignore")]
    public async Task UpdateAsync_Should_reject_noncanonical_proposed_input(
        string mutation)
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
                Candidates: [],
                ProposedInput: CreateNonCanonicalInput(mutation)),
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Invalid);
        result.Errors.Should().ContainSingle().Which.Should().Be(
            "The reviewed financial metrics must already be in canonical form.");
        result.ValidationIssues.Should().Contain(issue =>
            issue.Code == "NON_CANONICAL_REVIEWED_INPUT");
        entity.PayloadJson.Should().Be(originalJson);
    }

    [Theory]
    [InlineData("confidence_null")]
    [InlineData("confidence_low")]
    [InlineData("confidence_high")]
    [InlineData("source_page_zero")]
    [InlineData("source_blank")]
    [InlineData("unit_blank")]
    [InlineData("currency_blank")]
    [InlineData("name_case")]
    [InlineData("name_whitespace")]
    [InlineData("period_case")]
    [InlineData("period_whitespace")]
    [InlineData("source_whitespace")]
    [InlineData("unit_whitespace")]
    [InlineData("currency_whitespace")]
    [InlineData("document_id_whitespace")]
    [InlineData("company_whitespace")]
    [InlineData("document_currency_whitespace")]
    [InlineData("document_unit_whitespace")]
    [InlineData("duplicate_replacement")]
    [InlineData("duplicate_ignore")]
    public async Task ConfirmAsync_Should_reject_noncanonical_persisted_input_before_staging(
        string mutation)
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var sessionService = new StubStructuredFinancialMetricsSessionService();
        var service = CreateService(dbContext, sessionService);
        var draft = await CreateDraftAsync(service, session.Id, userId);
        var entity = await dbContext.FinancialMetricsExtractionDrafts
            .SingleAsync(x => x.Id == draft.Id);
        var tamperedPayload = CreatePayload(
            proposedInput: CreateNonCanonicalInput(mutation));
        entity.UpdatePayload(
            JsonSerializer.Serialize(tamperedPayload),
            DateTimeOffset.UtcNow.AddMinutes(1));
        await dbContext.SaveChangesAsync();

        var result = await service.ConfirmAsync(
            draft.Id,
            session.Id,
            userId,
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Invalid);
        result.Errors.Should().ContainSingle().Which.Should().Be(
            "The reviewed financial metrics must already be in canonical form.");
        result.ValidationIssues.Should().Contain(issue =>
            issue.Code == "NON_CANONICAL_REVIEWED_INPUT");
        sessionService.StageRequests.Should().BeEmpty();
        entity.Status.Should().Be(
            FinancialMetricsExtractionDraftStatus.PendingReview);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("state_mismatch")]
    public async Task CreateOrReplaceAsync_Should_reject_tampered_conflict_resolution(
        string tamper)
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var accepted = CreateMetricCandidate();
        var rejected = CreateMetricCandidate(
            value: 120m,
            reviewState: FinancialMetricCandidateReviewStates.Rejected);
        var selectedId = tamper == "unknown"
            ? Guid.NewGuid()
            : rejected.Id;
        var conflict = CreateMetricConflict(accepted, rejected) with
        {
            IsResolved = true,
            SelectedCandidateId = selectedId,
            ResolutionDecision = FinancialMetricCandidateReviewStates.Accepted
        };
        var service = CreateService(dbContext);

        var result = await service.CreateOrReplaceAsync(
            session.Id,
            userId,
            new CreateFinancialMetricsExtractionDraftRequest(
                OriginalFileName: "report.pdf",
                FileSizeBytes: 4096,
                ContentHash: "content-hash",
                Payload: CreatePayload(
                    candidates: [accepted, rejected],
                    conflicts: [conflict])),
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Invalid);
        result.Errors.Should().ContainSingle().Which.Should().Be(
            "Draft payload contains an invalid conflict resolution.");
    }

    [Fact]
    public async Task CreateOrReplaceAsync_Should_reject_cross_typed_conflict_candidate_id()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var metric = CreateMetricCandidate();
        var metadata = CreateMetadataCandidate();
        var crossTypedSnapshot = metric with
        {
            Id = metadata.Id
        };
        var payload = CreatePayload(
            candidates: [metric],
            conflicts: [CreateMetricConflict(crossTypedSnapshot)]) with
        {
            MetadataCandidates = [metadata]
        };
        var service = CreateService(dbContext);

        var result = await service.CreateOrReplaceAsync(
            session.Id,
            userId,
            new CreateFinancialMetricsExtractionDraftRequest(
                OriginalFileName: "report.pdf",
                FileSizeBytes: 4096,
                ContentHash: "content-hash",
                Payload: payload),
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Invalid);
        result.Errors.Should().ContainSingle().Which.Should().Be(
            "Draft payload conflict references an unknown candidate.");
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
            "missing" => CreatePayload(
                proposedInput: CreateInput() with
                {
                    Currency = null
                },
                missingFields: []),
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
    public async Task ConfirmAsync_Should_reject_accepted_metric_that_differs_from_proposal()
    {
        var payload = CreatePayload(
            proposedInput: CreateInput(metricValue: 999m),
            candidates: [CreateMetricCandidate(value: 100m)]);

        await AssertConfirmCoherenceBlockedAsync(
            payload,
            "Each selected metric resolution must match the proposed input.");
    }

    [Theory]
    [InlineData("source")]
    [InlineData("source_page")]
    [InlineData("confidence")]
    public async Task ConfirmAsync_Should_reject_accepted_metric_provenance_tampering(
        string field)
    {
        var proposedMetric = CreateInput().Metrics.Single();
        proposedMetric = field switch
        {
            "source" => proposedMetric with
            {
                Source = FinancialMetricCandidateSourceKinds.Inferred
            },
            "source_page" => proposedMetric with
            {
                SourcePage = 99
            },
            "confidence" => proposedMetric with
            {
                Confidence = 0.1m
            },
            _ => throw new InvalidOperationException()
        };
        var payload = CreatePayload(
            proposedInput: CreateInput(metrics: [proposedMetric]));

        await AssertConfirmCoherenceBlockedAsync(
            payload,
            "Each selected metric resolution must match the proposed input.");
    }

    [Theory]
    [InlineData("deterministic")]
    [InlineData("semantic")]
    public async Task ConfirmAsync_Should_accept_reconciler_source_round_trip(
        string candidateOrigin)
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var reconciliation = CreateReconciliation(candidateOrigin);
        var service = CreateService(dbContext);
        var draft = await CreateDraftAsync(
            service,
            session.Id,
            userId,
            payload: CreateReconciledPayload(
                reconciliation,
                markAccepted: true));

        var result = await service.ConfirmAsync(
            draft.Id,
            session.Id,
            userId,
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Success);
        result.Draft!.Status.Should().Be("confirmed");
    }

    [Theory]
    [InlineData("deterministic")]
    [InlineData("semantic")]
    public async Task ConfirmAsync_Should_reject_reconciler_source_kind_tampering(
        string candidateOrigin)
    {
        var reconciliation = CreateReconciliation(candidateOrigin);
        var candidate = reconciliation.Candidates.Single();
        var proposedMetric = reconciliation.ProposedInput.Metrics.Single() with
        {
            Source = candidate.SourceKind
        };
        var payload = CreateReconciledPayload(
            reconciliation,
            markAccepted: true) with
        {
            ProposedInput = reconciliation.ProposedInput with
            {
                Metrics = [proposedMetric]
            }
        };

        await AssertConfirmCoherenceBlockedAsync(
            payload,
            "Each selected metric resolution must match the proposed input.");
    }

    [Fact]
    public async Task ConfirmAsync_Should_reject_noncanonical_human_corrected_provenance()
    {
        var corrected = CreateMetricCandidate(
            value: 125m,
            sourceKind: FinancialMetricCandidateSourceKinds.HumanCorrected,
            reviewState: FinancialMetricCandidateReviewStates.HumanCorrected) with
        {
            Confidence = 1m
        };
        var proposedMetric = CreateInput(
            metricValue: 125m,
            metricSource: FinancialMetricCandidateSourceKinds.HumanCorrected)
            .Metrics.Single() with
        {
            SourcePage = 99,
            Confidence = 0.1m
        };
        var payload = CreatePayload(
            proposedInput: CreateInput(metrics: [proposedMetric]),
            candidates: [corrected]);

        await AssertConfirmCoherenceBlockedAsync(
            payload,
            "Each selected metric resolution must match the proposed input.");
    }

    [Theory]
    [InlineData("company")]
    [InlineData("currency")]
    [InlineData("unit")]
    public async Task ConfirmAsync_Should_reject_nonblank_metadata_without_candidate(
        string fieldName)
    {
        var inputWithoutField = WithoutMetadataField(CreateInput(), fieldName);
        var payload = CreatePayload(proposedInput: CreateInput()) with
        {
            MetadataCandidates = CreateMetadataCandidates(inputWithoutField)
        };

        await AssertConfirmCoherenceBlockedAsync(
            payload,
            "Each proposed metadata value must have exactly one accepted or human-corrected resolution.");
    }

    [Fact]
    public async Task ConfirmAsync_Should_reject_duplicate_selected_metric_candidates()
    {
        var accepted = CreateMetricCandidate(value: 100m);
        var duplicate = accepted with
        {
            Id = Guid.NewGuid()
        };
        var payload = CreatePayload(
            proposedInput: CreateInput(metricValue: 100m),
            candidates: [accepted, duplicate]);

        await AssertConfirmCoherenceBlockedAsync(
            payload,
            "Each proposed metric must have exactly one selected primary candidate.");
    }

    [Fact]
    public async Task ConfirmAsync_Should_reject_accepted_metadata_that_differs_from_proposal()
    {
        var payload = CreatePayload(
            proposedInput: CreateInput(company: "Other")) with
        {
            MetadataCandidates = [CreateMetadataCandidate(value: "Acme")]
        };

        await AssertConfirmCoherenceBlockedAsync(
            payload,
            "Each selected metadata resolution must match the proposed input.");
    }

    [Fact]
    public async Task ConfirmAsync_Should_reject_duplicate_selected_metadata_candidates()
    {
        var accepted = CreateMetadataCandidate(value: "Acme");
        var corrected = CreateMetadataCandidate(
            value: "Acme",
            reviewState: FinancialMetricCandidateReviewStates.HumanCorrected) with
        {
            SourceKind = FinancialMetricCandidateSourceKinds.HumanCorrected
        };
        var payload = CreatePayload() with
        {
            MetadataCandidates = [accepted, corrected]
        };

        await AssertConfirmCoherenceBlockedAsync(
            payload,
            "Each metadata field can have only one accepted or human-corrected resolution.");
    }

    [Fact]
    public async Task ConfirmAsync_Should_reject_proposed_metric_without_selected_candidate()
    {
        var payload = CreatePayload(
            proposedInput: CreateInput(metricValue: 125m),
            candidates:
            [
                CreateMetricCandidate(
                    value: 100m,
                    reviewState: FinancialMetricCandidateReviewStates.Rejected)
            ]);

        await AssertConfirmCoherenceBlockedAsync(
            payload,
            "Each proposed metric must have exactly one accepted or human-corrected resolution.");
    }

    [Fact]
    public async Task ConfirmAsync_Should_reject_proposed_metadata_without_selected_candidate()
    {
        var payload = CreatePayload(
            proposedInput: CreateInput(company: "Other")) with
        {
            MetadataCandidates =
            [
                CreateMetadataCandidate(
                    value: "Acme",
                    reviewState: FinancialMetricCandidateReviewStates.Rejected)
            ]
        };

        await AssertConfirmCoherenceBlockedAsync(
            payload,
            "Each proposed metadata value must have exactly one accepted or human-corrected resolution.");
    }

    [Fact]
    public async Task ConfirmAsync_Should_ignore_rejected_candidates_not_in_proposed_input()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var proposedInput = CreateInput();
        var payload = CreatePayload(
            proposedInput: proposedInput,
            candidates:
            [
                CreateMetricCandidate(),
                CreateMetricCandidate(
                    value: 50m,
                    reviewState: FinancialMetricCandidateReviewStates.Rejected) with
                {
                    Name = "expenses"
                }
            ]) with
        {
            MetadataCandidates =
            [
                .. CreateMetadataCandidates(proposedInput),
                CreateMetadataCandidate(
                    fieldName: "industry",
                    value: "Technology",
                    reviewState: FinancialMetricCandidateReviewStates.Rejected)
            ]
        };
        var service = CreateService(dbContext);
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

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Success);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("metrics")]
    public async Task ConfirmAsync_Should_recompute_missing_fields_before_staging(
        string missingField)
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var proposedInput = missingField == "company"
            ? CreateInput(company: null)
            : CreateInput(metrics: []);
        var payload = CreatePayload(
            proposedInput: proposedInput,
            missingFields: []);
        var sessionService = new StubStructuredFinancialMetricsSessionService();
        var publisher = new FakeActivityEventPublisher();
        var service = CreateService(
            dbContext,
            sessionService,
            publisher,
            new AlwaysValidValidator());
        var draft = await CreateDraftAsync(
            service,
            session.Id,
            userId,
            payload: payload);
        dbContext.ResetSaveChangesCount();

        var result = await service.ConfirmAsync(
            draft.Id,
            session.Id,
            userId,
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Invalid);
        dbContext.SaveChangesCount.Should().Be(0);
        sessionService.StageRequests.Should().BeEmpty();
        publisher.PublishedEvents.Should().BeEmpty();
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
        sessionService.StageRequests.Should().ContainSingle();
        sessionService.SaveRequests.Should().BeEmpty();
        var saveRequest = sessionService.StageRequests.Single();
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
    public async Task ConfirmAsync_Should_stage_and_save_session_and_draft_once_atomically()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var attachedPublisher = new FakeActivityEventPublisher();
        var sessionService = new StructuredFinancialMetricsSessionService(
            dbContext,
            new StructuredFinancialMetricsValidator(),
            new FinancialMetricInputMapper(),
            attachedPublisher);
        var reviewPublisher = new FakeActivityEventPublisher();
        var service = CreateService(dbContext, sessionService, reviewPublisher);
        var draft = await CreateDraftAsync(service, session.Id, userId);
        dbContext.ResetSaveChangesCount();

        var result = await service.ConfirmAsync(
            draft.Id,
            session.Id,
            userId,
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Success);
        dbContext.SaveChangesCount.Should().Be(1);
        attachedPublisher.PublishedEvents.Should().BeEmpty();
        reviewPublisher.PublishedEvents.Should().ContainSingle(x =>
            x.Type == "financial_metrics_extraction_review_confirmed");
        session.ContextJson.Should().Contain("pdf_file_reviewed");
        (await dbContext.FinancialMetricsExtractionDrafts.SingleAsync(x => x.Id == draft.Id))
            .Status.Should().Be(FinancialMetricsExtractionDraftStatus.Confirmed);
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
        dbContext.ResetSaveChangesCount();

        var result = await service.ConfirmAsync(
            draft.Id,
            session.Id,
            userId,
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Invalid);
        result.ValidationIssues.Should().Contain(x => x.Code == "METRICS_REQUIRED");
        dbContext.SaveChangesCount.Should().Be(0);
        sessionService.StageRequests.Should().BeEmpty();
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
        dbContext.ResetSaveChangesCount();

        var result = await service.ConfirmAsync(
            draft.Id,
            session.Id,
            userId,
            CancellationToken.None);

        result.Kind.Should().Be(expectedKind);
        dbContext.SaveChangesCount.Should().Be(0);
        sessionService.StageRequests.Should().ContainSingle();
        sessionService.SaveRequests.Should().BeEmpty();
        publisher.PublishedEvents.Should().BeEmpty();
        (await dbContext.FinancialMetricsExtractionDrafts.SingleAsync(x => x.Id == draft.Id))
            .Status.Should().Be(FinancialMetricsExtractionDraftStatus.PendingReview);
    }

    [Fact]
    public async Task ConfirmAsync_Should_not_commit_staged_context_when_atomic_save_conflicts()
    {
        var databaseName = Guid.NewGuid().ToString();
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = new DbContextOptionsBuilder<OrchestrationDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;
        await using var dbContext = new CountingOrchestrationDbContext(options);
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        session.SetContext("{\"planner\":{\"summary\":\"before\"}}");
        await dbContext.SaveChangesAsync();
        var originalContext = session.ContextJson;
        var sessionService = new StagingSessionService(dbContext);
        var publisher = new FakeActivityEventPublisher();
        var service = CreateService(dbContext, sessionService, publisher);
        var draft = await CreateDraftAsync(service, session.Id, userId);
        dbContext.ResetSaveChangesCount();
        dbContext.NextSaveException = new DbUpdateConcurrencyException(
            "Injected concurrency conflict.");

        var result = await service.ConfirmAsync(
            draft.Id,
            session.Id,
            userId,
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Conflict);
        sessionService.StageRequests.Should().ContainSingle();
        sessionService.SaveRequests.Should().BeEmpty();
        dbContext.SaveChangesCount.Should().Be(1);
        publisher.PublishedEvents.Should().BeEmpty();

        await using var freshContext = new OrchestrationDbContext(options);
        var persistedSession = await freshContext.AnalysisSessions
            .SingleAsync(x => x.Id == session.Id);
        var persistedDraft = await freshContext.FinancialMetricsExtractionDrafts
            .SingleAsync(x => x.Id == draft.Id);
        persistedSession.ContextJson.Should().Be(originalContext);
        persistedDraft.Status.Should().Be(
            FinancialMetricsExtractionDraftStatus.PendingReview);
    }

    [Theory]
    [InlineData("concurrency")]
    [InlineData("update")]
    public async Task ConfirmAsync_Should_retry_in_same_context_after_save_failure(
        string exceptionKind)
    {
        var databaseName = Guid.NewGuid().ToString();
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = new DbContextOptionsBuilder<OrchestrationDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;
        await using var dbContext = new CountingOrchestrationDbContext(options);
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var sessionService = new StructuredFinancialMetricsSessionService(
            dbContext,
            new StructuredFinancialMetricsValidator(),
            new FinancialMetricInputMapper(),
            new FakeActivityEventPublisher());
        var publisher = new FakeActivityEventPublisher();
        var service = CreateService(dbContext, sessionService, publisher);
        var draft = await CreateDraftAsync(service, session.Id, userId);
        dbContext.NextSaveException = CreateSaveException(exceptionKind);

        var failed = await service.ConfirmAsync(
            draft.Id,
            session.Id,
            userId,
            CancellationToken.None);
        var retry = await service.ConfirmAsync(
            draft.Id,
            session.Id,
            userId,
            CancellationToken.None);

        failed.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Conflict);
        retry.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Success);
        publisher.PublishedEvents.Should().ContainSingle(x =>
            x.Type == "financial_metrics_extraction_review_confirmed");

        await using var verificationContext = new OrchestrationDbContext(options);
        var persistedSession = await verificationContext.AnalysisSessions
            .SingleAsync(x => x.Id == session.Id);
        var persistedDraft = await verificationContext
            .FinancialMetricsExtractionDrafts
            .SingleAsync(x => x.Id == draft.Id);
        persistedSession.ContextJson.Should().Contain("structuredFinancialMetrics");
        persistedDraft.Status.Should().Be(
            FinancialMetricsExtractionDraftStatus.Confirmed);
    }

    [Fact]
    public async Task ConfirmAsync_Should_retry_after_stage_exception_dirties_context()
    {
        var databaseName = Guid.NewGuid().ToString();
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = new DbContextOptionsBuilder<OrchestrationDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;
        await using var dbContext = new CountingOrchestrationDbContext(options);
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        session.SetContext("""{"planner":{"summary":"keep"}}""");
        await dbContext.SaveChangesAsync();
        var draft = await CreateDraftAsync(
            CreateService(dbContext),
            session.Id,
            userId);
        var sessionService = new DirtyThenThrowSessionService(
            dbContext,
            new InvalidOperationException("Injected stage failure."));
        var publisher = new FakeActivityEventPublisher();
        var service = CreateService(dbContext, sessionService, publisher);

        var failed = await service.ConfirmAsync(
            draft.Id,
            session.Id,
            userId,
            CancellationToken.None);
        var retry = await service.ConfirmAsync(
            draft.Id,
            session.Id,
            userId,
            CancellationToken.None);

        failed.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Conflict);
        retry.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Success);
        publisher.PublishedEvents.Should().ContainSingle();

        await using var verificationContext = new OrchestrationDbContext(options);
        var persistedSession = await verificationContext.AnalysisSessions
            .SingleAsync(x => x.Id == session.Id);
        persistedSession.ContextJson.Should().Contain("\"planner\"");
        persistedSession.ContextJson.Should().Contain(
            "\"structuredFinancialMetrics\"");
        persistedSession.ContextJson.Should().NotContain("\"dirty\"");
    }

    [Fact]
    public async Task ConfirmAsync_Should_propagate_stage_cancellation_and_recover_context()
    {
        var databaseName = Guid.NewGuid().ToString();
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = new DbContextOptionsBuilder<OrchestrationDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;
        await using var dbContext = new CountingOrchestrationDbContext(options);
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        session.SetContext("""{"planner":{"summary":"keep"}}""");
        await dbContext.SaveChangesAsync();
        var draft = await CreateDraftAsync(
            CreateService(dbContext),
            session.Id,
            userId);
        using var cancellationSource = new CancellationTokenSource();
        var sessionService = new DirtyThenThrowSessionService(
            dbContext,
            new OperationCanceledException(cancellationSource.Token),
            cancellationSource);
        var publisher = new FakeActivityEventPublisher();
        var service = CreateService(dbContext, sessionService, publisher);

        var action = () => service.ConfirmAsync(
            draft.Id,
            session.Id,
            userId,
            cancellationSource.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();

        var retry = await service.ConfirmAsync(
            draft.Id,
            session.Id,
            userId,
            CancellationToken.None);

        retry.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Success);
        publisher.PublishedEvents.Should().ContainSingle();

        await using var verificationContext = new OrchestrationDbContext(options);
        var persistedSession = await verificationContext.AnalysisSessions
            .SingleAsync(x => x.Id == session.Id);
        persistedSession.ContextJson.Should().Contain("\"planner\"");
        persistedSession.ContextJson.Should().Contain(
            "\"structuredFinancialMetrics\"");
        persistedSession.ContextJson.Should().NotContain("\"dirty\"");
    }

    [Fact]
    public async Task ConfirmAsync_Should_conflict_on_stale_payload_and_retry_with_current_payload()
    {
        var databaseName = Guid.NewGuid().ToString();
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = new DbContextOptionsBuilder<OrchestrationDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;
        var userId = Guid.NewGuid();
        Guid sessionId;
        FinancialMetricsExtractionDraftDto draft;

        await using (var setupContext = new CountingOrchestrationDbContext(options))
        {
            var session = await AddSessionAsync(setupContext, userId);
            sessionId = session.Id;
            draft = await CreateDraftAsync(
                CreateService(setupContext),
                sessionId,
                userId);
        }

        await using var staleContext = new CountingOrchestrationDbContext(options);
        await staleContext.FinancialMetricsExtractionDrafts
            .SingleAsync(x => x.Id == draft.Id);

        await Task.Delay(10);

        await using (var concurrentContext = new CountingOrchestrationDbContext(options))
        {
            var concurrentService = CreateService(concurrentContext);
            var addedMetric = new StructuredFinancialMetricInput(
                Name: "ebitda",
                Period: "2025A",
                Value: 250m,
                Unit: "USD_million",
                Currency: "USD",
                Source: FinancialMetricCandidateSourceKinds.HumanCorrected,
                SourcePage: null,
                Confidence: 1m);
            var update = await concurrentService.UpdateAsync(
                draft.Id,
                sessionId,
                userId,
                new UpdateFinancialMetricsExtractionDraftRequest(
                    Candidates: [],
                    ProposedInput: draft.Payload.ProposedInput with
                    {
                        Metrics =
                        [
                            .. draft.Payload.ProposedInput.Metrics,
                            addedMetric
                        ]
                    })
                {
                    MetricAdditions =
                    [
                        new FinancialMetricCandidateAddition(
                            Name: "ebitda",
                            Period: "2025A",
                            Value: 250m,
                            Currency: "USD",
                            Unit: "USD_million")
                    ]
                },
                CancellationToken.None);

            update.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Success);
        }

        var attachedPublisher = new FakeActivityEventPublisher();
        var sessionService = new StructuredFinancialMetricsSessionService(
            staleContext,
            new StructuredFinancialMetricsValidator(),
            new FinancialMetricInputMapper(),
            attachedPublisher);
        var reviewPublisher = new FakeActivityEventPublisher();
        var staleService = CreateService(
            staleContext,
            sessionService,
            reviewPublisher);

        var staleConfirm = await staleService.ConfirmAsync(
            draft.Id,
            sessionId,
            userId,
            CancellationToken.None);

        staleConfirm.Kind.Should().Be(
            FinancialMetricsExtractionDraftResultKind.Conflict);
        reviewPublisher.PublishedEvents.Should().BeEmpty();

        await using (var verificationContext = new OrchestrationDbContext(options))
        {
            var persistedSession = await verificationContext.AnalysisSessions
                .SingleAsync(x => x.Id == sessionId);
            var persistedDraft = await verificationContext
                .FinancialMetricsExtractionDrafts
                .SingleAsync(x => x.Id == draft.Id);

            persistedSession.ContextJson.Should().Be("{}");
            persistedDraft.Status.Should().Be(
                FinancialMetricsExtractionDraftStatus.PendingReview);
        }

        var retry = await staleService.ConfirmAsync(
            draft.Id,
            sessionId,
            userId,
            CancellationToken.None);

        retry.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Success);
        reviewPublisher.PublishedEvents.Should().ContainSingle();

        await using var retryVerificationContext =
            new OrchestrationDbContext(options);
        var confirmedSession = await retryVerificationContext.AnalysisSessions
            .SingleAsync(x => x.Id == sessionId);
        var confirmedDraft = await retryVerificationContext
            .FinancialMetricsExtractionDrafts
            .SingleAsync(x => x.Id == draft.Id);
        using var contextDocument = JsonDocument.Parse(
            confirmedSession.ContextJson);
        var persistedMetrics = contextDocument.RootElement
            .GetProperty("structuredFinancialMetrics")
            .GetProperty("metrics")
            .EnumerateArray()
            .ToArray();
        var persistedValue = persistedMetrics
            .Single(metric => metric.GetProperty("name").GetString() == "ebitda")
            .GetProperty("value")
            .GetDecimal();

        persistedMetrics.Should().HaveCount(2);
        persistedValue.Should().Be(250m);
        confirmedDraft.Status.Should().Be(
            FinancialMetricsExtractionDraftStatus.Confirmed);
    }

    [Fact]
    public async Task ConfirmAsync_Should_conflict_when_session_context_changes_concurrently()
    {
        var databaseName = Guid.NewGuid().ToString();
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = new DbContextOptionsBuilder<OrchestrationDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;
        var userId = Guid.NewGuid();
        Guid sessionId;
        FinancialMetricsExtractionDraftDto draft;

        await using (var setupContext = new CountingOrchestrationDbContext(options))
        {
            var session = await AddSessionAsync(setupContext, userId);
            sessionId = session.Id;
            draft = await CreateDraftAsync(
                CreateService(setupContext),
                sessionId,
                userId);
        }

        await using var staleContext = new CountingOrchestrationDbContext(options);
        await staleContext.AnalysisSessions.SingleAsync(x => x.Id == sessionId);

        const string concurrentContextJson =
            """{"planner":{"summary":"concurrent"}}""";

        await using (var concurrentContext = new OrchestrationDbContext(options))
        {
            var concurrentSession = await concurrentContext.AnalysisSessions
                .SingleAsync(x => x.Id == sessionId);
            concurrentSession.SetContext(concurrentContextJson);
            await concurrentContext.SaveChangesAsync();
        }

        var sessionService = new StructuredFinancialMetricsSessionService(
            staleContext,
            new StructuredFinancialMetricsValidator(),
            new FinancialMetricInputMapper(),
            new FakeActivityEventPublisher());
        var reviewPublisher = new FakeActivityEventPublisher();
        var service = CreateService(
            staleContext,
            sessionService,
            reviewPublisher);

        var result = await service.ConfirmAsync(
            draft.Id,
            sessionId,
            userId,
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Conflict);
        reviewPublisher.PublishedEvents.Should().BeEmpty();

        await using var verificationContext = new OrchestrationDbContext(options);
        var persistedSession = await verificationContext.AnalysisSessions
            .SingleAsync(x => x.Id == sessionId);
        var persistedDraft = await verificationContext
            .FinancialMetricsExtractionDrafts
            .SingleAsync(x => x.Id == draft.Id);

        persistedSession.ContextJson.Should().Be(concurrentContextJson);
        persistedDraft.Status.Should().Be(
            FinancialMetricsExtractionDraftStatus.PendingReview);
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

    [Theory]
    [InlineData("concurrency")]
    [InlineData("update")]
    public async Task DiscardAsync_Should_retry_in_same_context_after_save_failure(
        string exceptionKind)
    {
        var databaseName = Guid.NewGuid().ToString();
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = new DbContextOptionsBuilder<OrchestrationDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;
        await using var dbContext = new CountingOrchestrationDbContext(options);
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var publisher = new FakeActivityEventPublisher();
        var service = CreateService(
            dbContext,
            new StubStructuredFinancialMetricsSessionService(),
            publisher);
        var draft = await CreateDraftAsync(service, session.Id, userId);
        dbContext.NextSaveException = CreateSaveException(exceptionKind);

        var failed = await service.DiscardAsync(
            draft.Id,
            session.Id,
            userId,
            CancellationToken.None);
        var retry = await service.DiscardAsync(
            draft.Id,
            session.Id,
            userId,
            CancellationToken.None);

        failed.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Conflict);
        retry.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Success);
        publisher.PublishedEvents.Should().ContainSingle(x =>
            x.Type == "financial_metrics_extraction_review_discarded");

        await using var verificationContext = new OrchestrationDbContext(options);
        var persistedDraft = await verificationContext
            .FinancialMetricsExtractionDrafts
            .SingleAsync(x => x.Id == draft.Id);
        persistedDraft.Status.Should().Be(
            FinancialMetricsExtractionDraftStatus.Discarded);
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
        getAsOther.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.NotFound);
        updateAsOther.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.NotFound);
        confirmAsOther.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.NotFound);
        discardAsOther.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.NotFound);
    }

    [Theory]
    [InlineData("""{"schemaVersion":2}""")]
    [InlineData("""{"schemaVersion":1,"proposedInput":null,"candidates":null}""")]
    public async Task GetPendingAsync_Should_return_invalid_identity_for_corrupted_payload(
        string payloadJson)
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var entity = FinancialMetricsExtractionDraft.Create(
            session.Id,
            userId,
            "corrupted.pdf",
            100,
            "corrupted-hash",
            payloadJson,
            DateTimeOffset.UtcNow);
        dbContext.FinancialMetricsExtractionDrafts.Add(entity);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        var result = await service.GetPendingAsync(
            session.Id,
            userId,
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Invalid);
        result.Draft.Should().BeNull();
        result.DraftIdentity.Should().NotBeNull();
        result.DraftIdentity!.Id.Should().Be(entity.Id);
        result.DraftIdentity.SessionId.Should().Be(session.Id);
        result.DraftIdentity.Status.Should().Be("pending_review");
        result.DraftIdentity.OriginalFileName.Should().Be("corrupted.pdf");
    }

    [Theory]
    [InlineData("""{"schemaVersion":2}""")]
    [InlineData("""{"schemaVersion":1,"proposedInput":null,"candidates":null}""")]
    public async Task DiscardAsync_Should_discard_corrupted_payload_idempotently(
        string payloadJson)
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var entity = FinancialMetricsExtractionDraft.Create(
            session.Id,
            userId,
            "corrupted.pdf",
            100,
            "corrupted-hash",
            payloadJson,
            DateTimeOffset.UtcNow);
        dbContext.FinancialMetricsExtractionDrafts.Add(entity);
        await dbContext.SaveChangesAsync();
        var publisher = new FakeActivityEventPublisher();
        var service = CreateService(
            dbContext,
            new StubStructuredFinancialMetricsSessionService(),
            publisher);

        var otherUser = await service.DiscardAsync(
            entity.Id,
            session.Id,
            Guid.NewGuid(),
            CancellationToken.None);
        var discarded = await service.DiscardAsync(
            entity.Id,
            session.Id,
            userId,
            CancellationToken.None);
        var idempotent = await service.DiscardAsync(
            entity.Id,
            session.Id,
            userId,
            CancellationToken.None);

        otherUser.Kind.Should().Be(
            FinancialMetricsExtractionDraftResultKind.NotFound);
        discarded.Kind.Should().Be(
            FinancialMetricsExtractionDraftResultKind.Success);
        discarded.Draft.Should().BeNull();
        discarded.DraftIdentity!.Id.Should().Be(entity.Id);
        discarded.DraftIdentity.Status.Should().Be("discarded");
        idempotent.Kind.Should().Be(
            FinancialMetricsExtractionDraftResultKind.Success);
        idempotent.Draft.Should().BeNull();
        idempotent.DraftIdentity!.Status.Should().Be("discarded");
        publisher.PublishedEvents.Should().ContainSingle(x =>
            x.Type == "financial_metrics_extraction_review_discarded");
        entity.Status.Should().Be(
            FinancialMetricsExtractionDraftStatus.Discarded);
    }

    [Fact]
    public async Task DiscardAsync_Should_reject_confirmed_corrupted_payload()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var entity = FinancialMetricsExtractionDraft.Create(
            session.Id,
            userId,
            "corrupted.pdf",
            100,
            "corrupted-hash",
            """{"schemaVersion":2}""",
            DateTimeOffset.UtcNow);
        entity.Confirm(userId, DateTimeOffset.UtcNow.AddMinutes(1));
        dbContext.FinancialMetricsExtractionDrafts.Add(entity);
        await dbContext.SaveChangesAsync();
        var publisher = new FakeActivityEventPublisher();
        var service = CreateService(
            dbContext,
            new StubStructuredFinancialMetricsSessionService(),
            publisher);

        var result = await service.DiscardAsync(
            entity.Id,
            session.Id,
            userId,
            CancellationToken.None);

        result.Kind.Should().Be(
            FinancialMetricsExtractionDraftResultKind.Conflict);
        publisher.PublishedEvents.Should().BeEmpty();
        entity.Status.Should().Be(
            FinancialMetricsExtractionDraftStatus.Confirmed);
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

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task UpdateAsync_Should_reject_malformed_conflict_field_name(
        string? fieldName)
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var candidate = CreateMetricCandidate();
        var conflict = CreateMetricConflict(candidate) with
        {
            FieldName = fieldName!
        };
        var payload = CreatePayload(
            candidates: [candidate],
            conflicts: [conflict]);
        var entity = FinancialMetricsExtractionDraft.Create(
            session.Id,
            userId,
            "malformed-conflict.pdf",
            100,
            "malformed-conflict-hash",
            JsonSerializer.Serialize(
                payload,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            DateTimeOffset.UtcNow);
        dbContext.FinancialMetricsExtractionDrafts.Add(entity);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        var result = await service.UpdateAsync(
            entity.Id,
            session.Id,
            userId,
            new UpdateFinancialMetricsExtractionDraftRequest(
                Candidates: [],
                ProposedInput: payload.ProposedInput),
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Invalid);
        result.Errors.Should().ContainSingle().Which.Should().Be(
            "Draft payload contains a malformed conflict.");
        entity.Status.Should().Be(FinancialMetricsExtractionDraftStatus.PendingReview);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("name")]
    [InlineData("period")]
    public async Task UpdateAsync_Should_reject_malformed_proposed_metric(
        string malformedField)
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
                Candidates: [],
                ProposedInput: CreateInput(
                    metrics: CreateMalformedMetrics(malformedField))),
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Invalid);
        result.Errors.Should().ContainSingle().Which.Should().Be(
            "Proposed input contains a malformed metric.");
        entity.PayloadJson.Should().Be(originalJson);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("name")]
    [InlineData("period")]
    public async Task ConfirmAsync_Should_reject_malformed_persisted_proposed_metric(
        string malformedField)
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var payload = CreatePayload() with
        {
            ProposedInput = CreateInput(
                metrics: CreateMalformedMetrics(malformedField))
        };
        var entity = FinancialMetricsExtractionDraft.Create(
            session.Id,
            userId,
            "malformed-metric.pdf",
            100,
            "malformed-metric-hash",
            JsonSerializer.Serialize(
                payload,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
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
        result.Errors.Should().ContainSingle().Which.Should().Be(
            "Proposed input contains a malformed metric.");
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
        IStructuredFinancialMetricsSessionService? sessionService = null,
        FakeActivityEventPublisher? publisher = null,
        IStructuredFinancialMetricsValidator? validator = null)
    {
        return new FinancialMetricsExtractionDraftService(
            dbContext,
            validator ?? new StructuredFinancialMetricsValidator(),
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

    private static async Task AssertConfirmCoherenceBlockedAsync(
        FinancialMetricsExtractionDraftPayload payload,
        string expectedError)
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var session = await AddSessionAsync(dbContext, userId);
        var sessionService = new StubStructuredFinancialMetricsSessionService();
        var publisher = new FakeActivityEventPublisher();
        var service = CreateService(dbContext, sessionService, publisher);
        var draft = await CreateDraftAsync(
            service,
            session.Id,
            userId,
            payload: payload);
        dbContext.ResetSaveChangesCount();

        var result = await service.ConfirmAsync(
            draft.Id,
            session.Id,
            userId,
            CancellationToken.None);

        result.Kind.Should().Be(FinancialMetricsExtractionDraftResultKind.Invalid);
        result.Errors.Should().ContainSingle().Which.Should().Be(expectedError);
        dbContext.SaveChangesCount.Should().Be(0);
        sessionService.StageRequests.Should().BeEmpty();
        sessionService.SaveRequests.Should().BeEmpty();
        publisher.PublishedEvents.Should().BeEmpty();
        (await dbContext.FinancialMetricsExtractionDrafts.SingleAsync(x => x.Id == draft.Id))
            .Status.Should().Be(FinancialMetricsExtractionDraftStatus.PendingReview);
    }

    private static CreateFinancialMetricsExtractionDraftRequest CreateRequest()
    {
        return new CreateFinancialMetricsExtractionDraftRequest(
            OriginalFileName: "report.pdf",
            FileSizeBytes: 4096,
            ContentHash: "content-hash",
            Payload: CreatePayload());
    }

    private static UpdateFinancialMetricsExtractionDraftRequest
        CreateMetricAdditionUpdate(
            FinancialMetricsExtractionDraftDto draft,
            decimal value)
    {
        var addedMetric = new StructuredFinancialMetricInput(
            Name: "ebitda",
            Period: "2025A",
            Value: value,
            Unit: "USD_million",
            Currency: "USD",
            Source: FinancialMetricCandidateSourceKinds.HumanCorrected,
            SourcePage: null,
            Confidence: 1m);

        return new UpdateFinancialMetricsExtractionDraftRequest(
            Candidates: [],
            ProposedInput: draft.Payload.ProposedInput with
            {
                Metrics = [.. draft.Payload.ProposedInput.Metrics, addedMetric]
            })
        {
            MetricAdditions =
            [
                new FinancialMetricCandidateAddition(
                    Name: "ebitda",
                    Period: "2025A",
                    Value: value,
                    Currency: "USD",
                    Unit: "USD_million")
            ]
        };
    }

    private static Exception CreateSaveException(string exceptionKind)
    {
        return exceptionKind switch
        {
            "concurrency" => new DbUpdateConcurrencyException(
                "Injected concurrency conflict."),
            "update" => new DbUpdateException("Injected update failure."),
            _ => throw new InvalidOperationException()
        };
    }

    private static FinancialMetricsExtractionDraftPayload CreatePayload(
        StructuredFinancialMetricsInput? proposedInput = null,
        IReadOnlyList<FinancialMetricCandidate>? candidates = null,
        IReadOnlyList<FinancialMetricCandidateConflict>? conflicts = null,
        IReadOnlyList<string>? missingFields = null)
    {
        var input = proposedInput ?? CreateInput();

        return new FinancialMetricsExtractionDraftPayload(
            SchemaVersion: FinancialMetricsExtractionDraftPayload.CurrentSchemaVersion,
            ProposedInput: input,
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
            })
        {
            MetadataCandidates = CreateMetadataCandidates(input)
        };
    }

    private static StructuredFinancialMetricsInput CreateInput(
        string documentId = "document-1",
        string? company = "Acme",
        decimal metricValue = 100m,
        string metricSource = "native_table",
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

    private static StructuredFinancialMetricsInput CreateNonCanonicalInput(
        string mutation)
    {
        var input = CreateInput();
        var metric = input.Metrics.Single();

        return mutation switch
        {
            "confidence_null" => input with
            {
                Metrics = [metric with { Confidence = null }]
            },
            "confidence_low" => input with
            {
                Metrics = [metric with { Confidence = -0.1m }]
            },
            "confidence_high" => input with
            {
                Metrics = [metric with { Confidence = 1.1m }]
            },
            "source_page_zero" => input with
            {
                Metrics = [metric with { SourcePage = 0 }]
            },
            "source_blank" => input with
            {
                Metrics = [metric with { Source = " " }]
            },
            "unit_blank" => input with
            {
                Metrics = [metric with { Unit = " " }]
            },
            "currency_blank" => input with
            {
                Metrics = [metric with { Currency = " " }]
            },
            "name_case" => input with
            {
                Metrics = [metric with { Name = "Revenue" }]
            },
            "name_whitespace" => input with
            {
                Metrics = [metric with { Name = " revenue " }]
            },
            "period_case" => input with
            {
                Metrics = [metric with { Period = "2025a" }]
            },
            "period_whitespace" => input with
            {
                Metrics = [metric with { Period = " 2025A " }]
            },
            "source_whitespace" => input with
            {
                Metrics = [metric with { Source = " native_table " }]
            },
            "unit_whitespace" => input with
            {
                Metrics = [metric with { Unit = " USD_million " }]
            },
            "currency_whitespace" => input with
            {
                Metrics = [metric with { Currency = " USD " }]
            },
            "document_id_whitespace" => input with
            {
                DocumentId = " document-1 "
            },
            "company_whitespace" => input with
            {
                Company = " Acme "
            },
            "document_currency_whitespace" => input with
            {
                Currency = " USD "
            },
            "document_unit_whitespace" => input with
            {
                Unit = " USD_million "
            },
            "duplicate_replacement" => input with
            {
                Metrics =
                [
                    metric with { Confidence = 0.6m },
                    metric with
                    {
                        Name = "Revenue",
                        Value = 125m,
                        Confidence = 0.9m
                    }
                ]
            },
            "duplicate_ignore" => input with
            {
                Metrics =
                [
                    metric,
                    metric with
                    {
                        Name = "Revenue",
                        Value = 125m
                    }
                ]
            },
            _ => throw new InvalidOperationException()
        };
    }

    private static FinancialMetricReconciliationResult CreateReconciliation(
        string candidateOrigin)
    {
        var reconciler = new FinancialMetricCandidateReconciler(
            new StructuredFinancialMetricsValidator());
        var deterministicInput = candidateOrigin == "deterministic"
            ? CreateInput(metricSource: "pdf_native")
            : CreateInput(metrics: []);
        var semanticResult = candidateOrigin == "semantic"
            ? new FinancialDocumentExtractionResult(
                Company: null,
                Currency: null,
                Unit: null,
                Metrics:
                [
                    new FinancialMetricCandidate(
                        Id: Guid.NewGuid(),
                        Name: "revenue",
                        Period: "2025A",
                        Value: 100m,
                        Currency: "USD",
                        Unit: "USD_million",
                        SourceKind:
                            FinancialMetricCandidateSourceKinds.Reported,
                        Confidence: 0.95m,
                        SourcePage: 7,
                        Evidence: "Revenue 100",
                        ExtractionStrategy: "semantic_markdown_agent",
                        ReviewState:
                            FinancialMetricCandidateReviewStates.Explicit,
                        InferenceExplanation: null)
                ],
                MetadataCandidates: [])
            : null;

        return reconciler.Reconcile(
            deterministicInput,
            semanticResult,
            new FinancialMetricsExtractionOptions
            {
                Mode = "ReviewOnly",
                AutomaticAcceptanceConfidence = 0.9m
            });
    }

    private static FinancialMetricReconciliationResult
        CreateCompositeReconciliation(string composition)
    {
        var metric = CreateInput(metricSource: "pdf_native").Metrics.Single();
        var primary = composition switch
        {
            "borrowed_currency" => metric with
            {
                Currency = null
            },
            "borrowed_unit" => metric with
            {
                Unit = null
            },
            "compatible_multi_support" => metric with
            {
                Currency = null,
                Unit = null,
                Confidence = 0.99m
            },
            _ => throw new InvalidOperationException()
        };
        IReadOnlyList<FinancialMetricCandidate> supporters = composition switch
        {
            "borrowed_currency" =>
            [
                CompositeSupporter("USD", "USD_million", 0.9m)
            ],
            "borrowed_unit" =>
            [
                CompositeSupporter("USD", "USD_million", 0.9m)
            ],
            "compatible_multi_support" =>
            [
                CompositeSupporter("USD", null, 0.98m),
                CompositeSupporter("USD", "USD_million", 0.97m)
            ],
            _ => throw new InvalidOperationException()
        };
        var reconciler = new FinancialMetricCandidateReconciler(
            new StructuredFinancialMetricsValidator());

        return reconciler.Reconcile(
            CreateInput(metrics: [primary]),
            new FinancialDocumentExtractionResult(
                Company: null,
                Currency: null,
                Unit: null,
                Metrics: supporters,
                MetadataCandidates: []),
            new FinancialMetricsExtractionOptions
            {
                Mode = "ReviewOnly",
                AutomaticAcceptanceConfidence = 0.9m
            });
    }

    private static FinancialMetricCandidate CompositeSupporter(
        string? currency,
        string? unit,
        decimal confidence)
    {
        return new FinancialMetricCandidate(
            Id: Guid.NewGuid(),
            Name: "revenue",
            Period: "2025A",
            Value: 100m,
            Currency: currency,
            Unit: unit,
            SourceKind: FinancialMetricCandidateSourceKinds.Reported,
            Confidence: confidence,
            SourcePage: 7,
            Evidence: "Revenue 100",
            ExtractionStrategy: "semantic_markdown_agent",
            ReviewState: FinancialMetricCandidateReviewStates.Explicit,
            InferenceExplanation: null);
    }

    private static IReadOnlyList<FinancialMetricCandidateReviewUpdate> SelectAll(
        FinancialMetricReconciliationResult reconciliation)
    {
        return reconciliation.Candidates
            .Select(candidate => Decision(
                candidate.Id,
                FinancialMetricCandidateReviewStates.Accepted))
            .Concat(reconciliation.MetadataCandidates.Select(candidate =>
                Decision(
                    candidate.Id,
                    FinancialMetricCandidateReviewStates.Accepted)))
            .ToArray();
    }

    private static FinancialMetricsExtractionDraftPayload
        CreateReconciledPayload(
            FinancialMetricReconciliationResult reconciliation,
            bool markAccepted = false)
    {
        var candidates = markAccepted
            ? reconciliation.Candidates
                .Select(candidate => candidate with
                {
                    ReviewState =
                        FinancialMetricCandidateReviewStates.Accepted
                })
                .ToArray()
            : reconciliation.Candidates;
        var metadataCandidates = markAccepted
            ? reconciliation.MetadataCandidates
                .Select(candidate => candidate with
                {
                    ReviewState =
                        FinancialMetricCandidateReviewStates.Accepted
                })
                .ToArray()
            : reconciliation.MetadataCandidates;

        return CreatePayload(
            proposedInput: reconciliation.ProposedInput,
            candidates: candidates,
            conflicts: reconciliation.Conflicts,
            missingFields: reconciliation.MissingFields) with
        {
            MetadataCandidates = metadataCandidates
        };
    }

    private static StructuredFinancialMetricsInput WithoutMetadataField(
        StructuredFinancialMetricsInput input,
        string fieldName)
    {
        return fieldName switch
        {
            "company" => input with
            {
                Company = null
            },
            "currency" => input with
            {
                Currency = null
            },
            "unit" => input with
            {
                Unit = null
            },
            _ => throw new InvalidOperationException()
        };
    }

    private static IReadOnlyList<StructuredFinancialMetricInput>
        CreateMalformedMetrics(string malformedField)
    {
        var metric = CreateInput().Metrics.Single();

        return malformedField switch
        {
            "null" => [null!],
            "name" =>
            [
                metric with
                {
                    Name = " "
                }
            ],
            "period" =>
            [
                metric with
                {
                    Period = " "
                }
            ],
            _ => throw new InvalidOperationException()
        };
    }

    private static IReadOnlyList<FinancialDocumentMetadataCandidate>
        CreateMetadataCandidates(StructuredFinancialMetricsInput input)
    {
        var candidates = new List<FinancialDocumentMetadataCandidate>();

        if (input.Company is not null)
        {
            candidates.Add(CreateMetadataCandidate("company", input.Company));
        }

        if (input.Currency is not null)
        {
            candidates.Add(CreateMetadataCandidate("currency", input.Currency));
        }

        if (input.Unit is not null)
        {
            candidates.Add(CreateMetadataCandidate("unit", input.Unit));
        }

        return candidates;
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

    private static FinancialMetricCandidateReviewUpdate HumanMetricCorrection(
        Guid candidateId,
        decimal value)
    {
        return new FinancialMetricCandidateReviewUpdate(
            CandidateId: candidateId,
            Decision: FinancialMetricCandidateReviewStates.HumanCorrected,
            Value: value,
            Currency: "USD",
            Unit: "USD_million",
            MetadataValue: null);
    }

    private static FinancialMetricCandidateReviewUpdate HumanMetadataCorrection(
        Guid candidateId,
        string value)
    {
        return new FinancialMetricCandidateReviewUpdate(
            CandidateId: candidateId,
            Decision: FinancialMetricCandidateReviewStates.HumanCorrected,
            Value: null,
            Currency: null,
            Unit: null,
            MetadataValue: value);
    }

    private sealed class CountingOrchestrationDbContext(
        DbContextOptions<OrchestrationDbContext> options)
        : OrchestrationDbContext(options)
    {
        public int SaveChangesCount { get; private set; }

        public Exception? NextSaveException { get; set; }

        public override Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            SaveChangesCount++;

            if (NextSaveException is not null)
            {
                var exception = NextSaveException;
                NextSaveException = null;
                return Task.FromException<int>(exception);
            }

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
        public List<SaveStructuredFinancialMetricsRequest> StageRequests { get; } = [];

        public List<SaveStructuredFinancialMetricsRequest> SaveRequests { get; } = [];

        public string? FailureMode { get; init; }

        public Task<FinancialMetricsSessionSaveResult?> StageAsync(
            SaveStructuredFinancialMetricsRequest request,
            CancellationToken cancellationToken)
        {
            StageRequests.Add(request);
            return BuildResult(request);
        }

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
            return BuildResult(request);
        }

        private Task<FinancialMetricsSessionSaveResult?> BuildResult(
            SaveStructuredFinancialMetricsRequest request)
        {
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

    private sealed class StagingSessionService(
        OrchestrationDbContext dbContext)
        : IStructuredFinancialMetricsSessionService
    {
        public List<SaveStructuredFinancialMetricsRequest> StageRequests { get; } = [];

        public List<SaveStructuredFinancialMetricsRequest> SaveRequests { get; } = [];

        public async Task<FinancialMetricsSessionSaveResult?> StageAsync(
            SaveStructuredFinancialMetricsRequest request,
            CancellationToken cancellationToken)
        {
            StageRequests.Add(request);
            var session = await dbContext.AnalysisSessions
                .SingleOrDefaultAsync(x => x.Id == request.SessionId, cancellationToken);

            if (session is null)
            {
                return null;
            }

            var context = new StructuredFinancialMetricsContext(
                request.Input.DocumentId,
                request.Input.Company,
                request.Input.Currency,
                request.Input.Unit,
                [],
                [],
                DateTimeOffset.UtcNow,
                new StructuredFinancialMetricsProvenance(
                    "pdf_file_reviewed",
                    "report.pdf",
                    4096,
                    "content-hash",
                    1,
                    0));
            session.SetContext(JsonSerializer.Serialize(new
            {
                structuredFinancialMetrics = context
            }));

            return new FinancialMetricsSessionSaveResult(
                request.SessionId,
                true,
                context,
                [],
                []);
        }

        public Task<FinancialMetricsSessionSaveResult?> SaveAsync(
            Guid sessionId,
            StructuredFinancialMetricsInput input,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Confirm must not call SaveAsync.");
        }

        public Task<FinancialMetricsSessionSaveResult?> SaveAsync(
            SaveStructuredFinancialMetricsRequest request,
            CancellationToken cancellationToken)
        {
            SaveRequests.Add(request);
            throw new InvalidOperationException("Confirm must not call SaveAsync.");
        }

        public Task<StructuredFinancialMetricsContext?> GetAsync(
            Guid sessionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<StructuredFinancialMetricsContext?>(null);
        }
    }

    private sealed class DirtyThenThrowSessionService
        : IStructuredFinancialMetricsSessionService
    {
        private readonly OrchestrationDbContext _dbContext;
        private readonly Exception _firstException;
        private readonly CancellationTokenSource? _cancellationSource;
        private readonly StructuredFinancialMetricsSessionService _inner;
        private bool _hasThrown;

        public DirtyThenThrowSessionService(
            OrchestrationDbContext dbContext,
            Exception firstException,
            CancellationTokenSource? cancellationSource = null)
        {
            _dbContext = dbContext;
            _firstException = firstException;
            _cancellationSource = cancellationSource;
            _inner = new StructuredFinancialMetricsSessionService(
                dbContext,
                new StructuredFinancialMetricsValidator(),
                new FinancialMetricInputMapper(),
                new FakeActivityEventPublisher());
        }

        public async Task<FinancialMetricsSessionSaveResult?> StageAsync(
            SaveStructuredFinancialMetricsRequest request,
            CancellationToken cancellationToken)
        {
            if (!_hasThrown)
            {
                _hasThrown = true;
                var session = await _dbContext.AnalysisSessions
                    .SingleAsync(
                        x => x.Id == request.SessionId,
                        cancellationToken);
                session.SetContext("""{"dirty":true}""");
                _cancellationSource?.Cancel();
                throw _firstException;
            }

            return await _inner.StageAsync(request, cancellationToken);
        }

        public Task<FinancialMetricsSessionSaveResult?> SaveAsync(
            Guid sessionId,
            StructuredFinancialMetricsInput input,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Confirm must not call SaveAsync.");
        }

        public Task<FinancialMetricsSessionSaveResult?> SaveAsync(
            SaveStructuredFinancialMetricsRequest request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Confirm must not call SaveAsync.");
        }

        public Task<StructuredFinancialMetricsContext?> GetAsync(
            Guid sessionId,
            CancellationToken cancellationToken)
        {
            return _inner.GetAsync(sessionId, cancellationToken);
        }
    }

    private sealed class AlwaysValidValidator : IStructuredFinancialMetricsValidator
    {
        public FinancialMetricsValidationResult Validate(
            StructuredFinancialMetricsInput input)
        {
            return new FinancialMetricsValidationResult(
                true,
                [],
                [],
                []);
        }
    }
}
