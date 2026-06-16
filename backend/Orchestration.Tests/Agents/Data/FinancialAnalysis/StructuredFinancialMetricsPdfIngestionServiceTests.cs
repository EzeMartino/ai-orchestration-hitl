using FluentAssertions;
using Orchestration.Application.Activity;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Data.FinancialAnalysis.Extraction;

namespace Orchestration.Tests.Agents.Data.FinancialAnalysis;

public sealed class StructuredFinancialMetricsPdfIngestionServiceTests
{
    private static readonly Guid SessionId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid UserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task IngestAsync_Should_save_complete_deterministic_result_without_markitdown_or_llm()
    {
        var fixture = new Fixture();
        fixture.PdfExtractor.Result = PdfResult(isValid: true, nativeTextAvailable: true);
        fixture.CompletenessEvaluator.Decision = new FinancialMetricsExtractionDecision(false, []);

        var result = await fixture.Service.IngestAsync(Request());

        result.Outcome.Should().Be(FinancialMetricsFileOutcome.Accepted);
        result.SaveResult.Should().NotBeNull();
        fixture.SessionService.SaveRequests.Should().ContainSingle();
        fixture.SessionService.SaveRequests.Single().Provenance.Should().Be(
            new StructuredFinancialMetricsProvenanceInput(
                "pdf_file",
                "metrics.pdf",
                3,
                "sha256:abc"));
        fixture.MarkdownConverter.Calls.Should().Be(0);
        fixture.SemanticAgent.Calls.Should().Be(0);
    }

    [Fact]
    public async Task IngestAsync_Should_convert_searchable_pdf_then_run_semantic_agent_when_native_text_exists()
    {
        var fixture = new Fixture();
        fixture.PdfExtractor.Result = PdfResult(isValid: true, nativeTextAvailable: true);
        fixture.CompletenessEvaluator.Decision =
            new FinancialMetricsExtractionDecision(true, ["metric_coverage_below_threshold"]);
        fixture.Reconciler.Result = Reconciliation(canAutoAccept: true);

        await fixture.Service.IngestAsync(Request(SemanticOptions(mode: "AutoAccept")));

        fixture.OcrService.Calls.Should().Be(0);
        fixture.MarkdownConverter.Calls.Should().Be(1);
        fixture.MarkdownConverter.SeenPdfBytes.Should().ContainSingle()
            .Which.Should().Equal([1, 2, 3]);
        fixture.SemanticAgent.Requests.Should().ContainSingle();
        fixture.SemanticAgent.Requests.Single().MaxEvidenceExcerptCharacters.Should().Be(123);
        fixture.SemanticAgent.Requests.Single().MaxMarkdownChunks.Should().Be(7);
        fixture.SemanticAgent.Requests.Single().MaxSourcePage.Should().Be(1);
    }

    [Fact]
    public async Task IngestAsync_Should_create_searchable_pdf_before_markitdown_for_image_only_pdf()
    {
        var fixture = new Fixture();
        fixture.PdfExtractor.Result = PdfResult(isValid: true, nativeTextAvailable: false);
        fixture.CompletenessEvaluator.Decision =
            new FinancialMetricsExtractionDecision(true, ["native_text_missing"]);
        fixture.OcrService.Result = new SearchablePdfOcrResult(true, [9, 8, 7], null);
        fixture.Reconciler.Result = Reconciliation(canAutoAccept: true);

        await fixture.Service.IngestAsync(Request(SemanticOptions(mode: "AutoAccept")));

        fixture.OcrService.Calls.Should().Be(1);
        fixture.OcrService.SeenPdfBytes.Should().ContainSingle().Which.Should().Equal([1, 2, 3]);
        fixture.MarkdownConverter.SeenPdfBytes.Should().ContainSingle().Which.Should().Equal([9, 8, 7]);
        fixture.SemanticAgent.Calls.Should().Be(1);
    }

    [Fact]
    public async Task IngestAsync_Should_create_review_from_deterministic_candidates_when_semantic_agent_fails()
    {
        var fixture = new Fixture();
        fixture.PdfExtractor.Result = PdfResult(isValid: false, nativeTextAvailable: true);
        fixture.CompletenessEvaluator.Decision =
            new FinancialMetricsExtractionDecision(true, ["metric_coverage_below_threshold"]);
        fixture.SemanticAgent.ParseResult =
            new FinancialDocumentExtractionParseResult(false, null, "model unavailable");

        var result = await fixture.Service.IngestAsync(Request(SemanticOptions()));

        result.Outcome.Should().Be(FinancialMetricsFileOutcome.ReviewRequired);
        result.ReviewDraft.Should().NotBeNull();
        fixture.SessionService.SaveRequests.Should().BeEmpty();
        fixture.DraftService.Requests.Should().ContainSingle();
        fixture.DraftService.Requests.Single().Payload.Candidates
            .Should().Contain(candidate => candidate.ExtractionStrategy == "deterministic_pdf_parser");
        fixture.DraftService.Requests.Single().Payload.Diagnostics.SemanticSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task IngestAsync_Should_return_review_required_for_inferred_candidate()
    {
        var fixture = new Fixture();
        fixture.PdfExtractor.Result = PdfResult(isValid: true, nativeTextAvailable: true);
        fixture.CompletenessEvaluator.Decision =
            new FinancialMetricsExtractionDecision(true, ["unit_missing"]);
        fixture.Reconciler.Result = Reconciliation(
            candidates:
            [
                Candidate(
                    "revenue",
                    reviewState: FinancialMetricCandidateReviewStates.Inferred,
                    sourceKind: FinancialMetricCandidateSourceKinds.Inferred)
            ],
            requiresReview: true,
            canAutoAccept: false);

        var result = await fixture.Service.IngestAsync(Request(SemanticOptions(mode: "AutoAccept")));

        result.Outcome.Should().Be(FinancialMetricsFileOutcome.ReviewRequired);
        fixture.DraftService.Requests.Should().ContainSingle();
        fixture.SessionService.SaveRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task IngestAsync_Should_return_review_required_for_conflict()
    {
        var conflict = new FinancialMetricCandidateConflict(
            "metric",
            "value",
            "revenue",
            "2024A",
            "100",
            [Candidate("revenue")],
            []);
        var fixture = new Fixture();
        fixture.PdfExtractor.Result = PdfResult(isValid: true, nativeTextAvailable: true);
        fixture.CompletenessEvaluator.Decision =
            new FinancialMetricsExtractionDecision(true, ["conflict"]);
        fixture.Reconciler.Result = Reconciliation(
            conflicts: [conflict],
            requiresReview: true,
            canAutoAccept: false);

        var result = await fixture.Service.IngestAsync(Request(SemanticOptions(mode: "AutoAccept")));

        result.Outcome.Should().Be(FinancialMetricsFileOutcome.ReviewRequired);
        result.ReviewDraft!.Payload.Conflicts.Should().ContainSingle();
        fixture.SessionService.SaveRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task IngestAsync_Should_never_auto_accept_semantic_candidates_in_review_only_mode()
    {
        var fixture = new Fixture();
        fixture.PdfExtractor.Result = PdfResult(isValid: true, nativeTextAvailable: true);
        fixture.CompletenessEvaluator.Decision =
            new FinancialMetricsExtractionDecision(true, ["metric_coverage_below_threshold"]);
        fixture.Reconciler.Result = Reconciliation(canAutoAccept: true);

        var result = await fixture.Service.IngestAsync(Request(SemanticOptions(mode: "ReviewOnly")));

        result.Outcome.Should().Be(FinancialMetricsFileOutcome.ReviewRequired);
        fixture.DraftService.Requests.Should().ContainSingle();
        fixture.SessionService.SaveRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task IngestAsync_Should_auto_accept_only_when_reconciliation_allows_it()
    {
        var fixture = new Fixture();
        fixture.PdfExtractor.Result = PdfResult(isValid: true, nativeTextAvailable: true);
        fixture.CompletenessEvaluator.Decision =
            new FinancialMetricsExtractionDecision(true, ["metric_coverage_below_threshold"]);
        fixture.Reconciler.Result = Reconciliation(canAutoAccept: true);

        var result = await fixture.Service.IngestAsync(Request(SemanticOptions(mode: "AutoAccept")));

        result.Outcome.Should().Be(FinancialMetricsFileOutcome.Accepted);
        fixture.SessionService.SaveRequests.Should().ContainSingle();
        fixture.SessionService.SaveRequests.Single().Provenance!.IngestionMethod
            .Should().Be("pdf_file_semantic");
        fixture.DraftService.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task IngestAsync_Should_record_shadow_diagnostics_but_persist_only_valid_deterministic_input()
    {
        var fixture = new Fixture();
        var deterministic = CompleteInput(company: "Deterministic Co");
        var semantic = CompleteInput(company: "Semantic Co");
        fixture.PdfExtractor.Result = PdfResult(input: deterministic, isValid: true, nativeTextAvailable: true);
        fixture.CompletenessEvaluator.Decision =
            new FinancialMetricsExtractionDecision(true, ["shadow_probe"]);
        fixture.Reconciler.Result = Reconciliation(proposedInput: semantic, canAutoAccept: true);

        var result = await fixture.Service.IngestAsync(Request(SemanticOptions(mode: "Shadow")));

        result.Outcome.Should().Be(FinancialMetricsFileOutcome.Accepted);
        fixture.SessionService.SaveRequests.Should().ContainSingle();
        fixture.SessionService.SaveRequests.Single().Input.Company.Should().Be("Deterministic Co");
        fixture.SessionService.SaveRequests.Single().Provenance!.IngestionMethod.Should().Be("pdf_file");
        fixture.DraftService.Requests.Should().BeEmpty();
        fixture.ActivityPublisher.Events.Should().Contain(e => e.Message.Contains("shadow_probe"));
    }

    [Fact]
    public async Task IngestAsync_Should_not_save_when_no_acceptable_path_exists()
    {
        var fixture = new Fixture();
        fixture.PdfExtractor.Result = PdfResultWithoutInput(nativeTextAvailable: false);
        fixture.CompletenessEvaluator.Decision =
            new FinancialMetricsExtractionDecision(true, ["metric_coverage_below_threshold"]);
        fixture.OcrService.Result = new SearchablePdfOcrResult(false, [], "ocr unavailable");

        var result = await fixture.Service.IngestAsync(Request(SemanticOptions()));

        result.Outcome.Should().Be(FinancialMetricsFileOutcome.Failed);
        result.Errors.Should().Contain(issue => issue.Code == "pdf_ingestion_failed");
        fixture.SessionService.SaveRequests.Should().BeEmpty();
        fixture.DraftService.Requests.Should().BeEmpty();
    }

    private static StructuredFinancialMetricsPdfIngestionRequest Request(
        FinancialMetricsExtractionOptions? options = null)
    {
        return new StructuredFinancialMetricsPdfIngestionRequest(
            SessionId: SessionId,
            UserId: UserId,
            PdfBytes: [1, 2, 3],
            DocumentId: "doc-1",
            Company: "Vista Energy",
            Currency: "USD",
            Unit: "USD_million",
            OriginalFileName: "metrics.pdf",
            FileSizeBytes: 3,
            ContentHash: "sha256:abc")
        {
            ExtractionOptions = options ?? new FinancialMetricsExtractionOptions(),
            PdfExtractionOptions = new StructuredFinancialMetricsPdfExtractionOptions()
        };
    }

    private static FinancialMetricsExtractionOptions SemanticOptions(string mode = "ReviewOnly")
    {
        return new FinancialMetricsExtractionOptions
        {
            SemanticEnrichmentEnabled = true,
            Mode = mode,
            MaxEvidenceExcerptCharacters = 123,
            MaxMarkdownChunks = 7
        };
    }

    private static StructuredFinancialMetricsPdfExtractionResult PdfResult(
        StructuredFinancialMetricsInput? input = null,
        bool isValid = true,
        bool nativeTextAvailable = false)
    {
        return new StructuredFinancialMetricsPdfExtractionResult(
            isValid,
            input ?? CompleteInput(),
            isValid
                ? []
                :
                [
                    new FinancialMetricsValidationIssue(
                        "deterministic_invalid",
                        "Deterministic extraction is incomplete.",
                        null,
                        null,
                        "Error")
                ],
            [],
            UsedOcr: false,
            NativeTextAvailable: nativeTextAvailable);
    }

    private static StructuredFinancialMetricsPdfExtractionResult PdfResultWithoutInput(
        bool nativeTextAvailable)
    {
        return new StructuredFinancialMetricsPdfExtractionResult(
            false,
            null,
            [
                new FinancialMetricsValidationIssue(
                    "deterministic_invalid",
                    "No deterministic metrics were extracted.",
                    null,
                    null,
                    "Error")
            ],
            [],
            UsedOcr: false,
            NativeTextAvailable: nativeTextAvailable);
    }

    private static FinancialMetricReconciliationResult Reconciliation(
        StructuredFinancialMetricsInput? proposedInput = null,
        IReadOnlyList<FinancialMetricCandidate>? candidates = null,
        IReadOnlyList<FinancialMetricCandidateConflict>? conflicts = null,
        IReadOnlyList<string>? missingFields = null,
        bool requiresReview = false,
        bool canAutoAccept = false)
    {
        return new FinancialMetricReconciliationResult(
            proposedInput ?? CompleteInput(company: "Semantic Co"),
            candidates ?? [Candidate("revenue")],
            conflicts ?? [],
            missingFields ?? [],
            requiresReview,
            canAutoAccept);
    }

    private static StructuredFinancialMetricsInput CompleteInput(string company = "Vista Energy")
    {
        return new StructuredFinancialMetricsInput(
            "doc-1",
            company,
            "USD",
            "USD_million",
            [Metric("revenue"), Metric("ebitda")]);
    }

    private static StructuredFinancialMetricInput Metric(string name)
    {
        return new StructuredFinancialMetricInput(
            name,
            "2024A",
            100m,
            "USD_million",
            "USD",
            "pdf_extraction",
            1,
            0.95m);
    }

    private static FinancialMetricCandidate Candidate(
        string name,
        string reviewState = FinancialMetricCandidateReviewStates.Explicit,
        string sourceKind = FinancialMetricCandidateSourceKinds.Reported)
    {
        return new FinancialMetricCandidate(
            Guid.NewGuid(),
            name,
            "2024A",
            100m,
            "USD",
            "USD_million",
            sourceKind,
            0.95m,
            1,
            "Reported revenue.",
            "semantic_markdown_agent",
            reviewState,
            null);
    }

    private sealed class Fixture
    {
        public FakePdfExtractor PdfExtractor { get; } = new();
        public FakeCompletenessEvaluator CompletenessEvaluator { get; } = new();
        public FakeMarkdownConverter MarkdownConverter { get; } = new();
        public FakeOcrService OcrService { get; } = new();
        public FakeSemanticAgent SemanticAgent { get; } = new();
        public FakeReconciler Reconciler { get; } = new();
        public FakeDraftService DraftService { get; } = new();
        public FakeSessionService SessionService { get; } = new();
        public FakeActivityPublisher ActivityPublisher { get; } = new();

        public StructuredFinancialMetricsPdfIngestionService Service { get; }

        public Fixture()
        {
            Service = new StructuredFinancialMetricsPdfIngestionService(
                PdfExtractor,
                CompletenessEvaluator,
                MarkdownConverter,
                OcrService,
                SemanticAgent,
                Reconciler,
                DraftService,
                SessionService,
                ActivityPublisher);
        }
    }

    private sealed class FakePdfExtractor : IStructuredFinancialMetricsPdfExtractor
    {
        public StructuredFinancialMetricsPdfExtractionResult Result { get; set; } =
            PdfResult(isValid: true);

        public int Calls { get; private set; }

        public Task<StructuredFinancialMetricsPdfExtractionResult> ExtractAsync(
            Stream pdf,
            StructuredFinancialMetricsPdfExtractionRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;

            return Task.FromResult(Result);
        }
    }

    private sealed class FakeCompletenessEvaluator
        : IFinancialMetricsExtractionCompletenessEvaluator
    {
        public FinancialMetricsExtractionDecision Decision { get; set; } =
            new(false, []);

        public FinancialMetricsExtractionDecision Evaluate(
            StructuredFinancialMetricsPdfExtractionResult result,
            FinancialMetricsExtractionOptions options)
        {
            return Decision;
        }
    }

    private sealed class FakeMarkdownConverter : IFinancialDocumentMarkdownConverter
    {
        public int Calls { get; private set; }
        public List<byte[]> SeenPdfBytes { get; } = [];

        public Task<FinancialDocumentMarkdownResult> ConvertPdfAsync(
            Stream pdf,
            int maxCharacters,
            CancellationToken cancellationToken)
        {
            Calls++;
            SeenPdfBytes.Add(ReadAll(pdf));

            return Task.FromResult(new FinancialDocumentMarkdownResult(
                true,
                "Revenue 100 EBITDA 25",
                false,
                null));
        }
    }

    private sealed class FakeOcrService : ISearchablePdfOcrService
    {
        public int Calls { get; private set; }
        public List<byte[]> SeenPdfBytes { get; } = [];
        public SearchablePdfOcrResult Result { get; set; } =
            new(true, [4, 5, 6], null);

        public Task<SearchablePdfOcrResult> CreateSearchablePdfAsync(
            Stream pdf,
            StructuredFinancialMetricsPdfExtractionOptions options,
            CancellationToken cancellationToken)
        {
            Calls++;
            SeenPdfBytes.Add(ReadAll(pdf));

            return Task.FromResult(Result);
        }
    }

    private sealed class FakeSemanticAgent : IFinancialDocumentExtractionAgent
    {
        public int Calls { get; private set; }
        public List<FinancialDocumentExtractionRequest> Requests { get; } = [];
        public FinancialDocumentExtractionParseResult ParseResult { get; set; } =
            new(true, new FinancialDocumentExtractionResult(null, null, null, [Candidate("revenue")]), null);

        public Task<FinancialDocumentExtractionParseResult> ExtractAsync(
            FinancialDocumentExtractionRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;
            Requests.Add(request);

            return Task.FromResult(ParseResult);
        }
    }

    private sealed class FakeReconciler : IFinancialMetricCandidateReconciler
    {
        public FinancialMetricReconciliationResult Result { get; set; } =
            Reconciliation();

        public FinancialMetricReconciliationResult Reconcile(
            StructuredFinancialMetricsInput deterministicInput,
            FinancialDocumentExtractionResult? semanticResult,
            FinancialMetricsExtractionOptions options)
        {
            return Result;
        }
    }

    private sealed class FakeDraftService : IFinancialMetricsExtractionDraftService
    {
        public List<CreateFinancialMetricsExtractionDraftRequest> Requests { get; } = [];

        public Task<FinancialMetricsExtractionDraftServiceResult> CreateOrReplaceAsync(
            Guid sessionId,
            Guid userId,
            CreateFinancialMetricsExtractionDraftRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var now = DateTimeOffset.UtcNow;
            var draft = new FinancialMetricsExtractionDraftDto(
                Guid.NewGuid(),
                sessionId,
                "PendingReview",
                request.OriginalFileName,
                request.FileSizeBytes,
                request.ContentHash,
                request.Payload,
                now,
                now,
                null);

            return Task.FromResult(FinancialMetricsExtractionDraftServiceResult.Success(draft));
        }

        public Task<FinancialMetricsExtractionDraftServiceResult> GetPendingAsync(
            Guid sessionId,
            Guid userId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FinancialMetricsExtractionDraftServiceResult> UpdateAsync(
            Guid draftId,
            Guid sessionId,
            Guid userId,
            UpdateFinancialMetricsExtractionDraftRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FinancialMetricsExtractionDraftServiceResult> ConfirmAsync(
            Guid draftId,
            Guid sessionId,
            Guid userId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FinancialMetricsExtractionDraftServiceResult> DiscardAsync(
            Guid draftId,
            Guid sessionId,
            Guid userId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeSessionService : IStructuredFinancialMetricsSessionService
    {
        public List<SaveStructuredFinancialMetricsRequest> SaveRequests { get; } = [];

        public Task<FinancialMetricsSessionSaveResult?> SaveAsync(
            SaveStructuredFinancialMetricsRequest request,
            CancellationToken cancellationToken)
        {
            SaveRequests.Add(request);

            return Task.FromResult<FinancialMetricsSessionSaveResult?>(
                new FinancialMetricsSessionSaveResult(
                    request.SessionId,
                    true,
                    null,
                    [],
                    []));
        }

        public Task<FinancialMetricsSessionSaveResult?> StageAsync(
            SaveStructuredFinancialMetricsRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FinancialMetricsSessionSaveResult?> SaveAsync(
            Guid sessionId,
            StructuredFinancialMetricsInput input,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<StructuredFinancialMetricsContext?> GetAsync(
            Guid sessionId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeActivityPublisher : IActivityEventPublisher
    {
        public List<ActivityEvent> Events { get; } = [];

        public Task PublishAsync(
            ActivityEvent activityEvent,
            CancellationToken cancellationToken = default)
        {
            Events.Add(activityEvent);

            return Task.CompletedTask;
        }
    }

    private static byte[] ReadAll(Stream stream)
    {
        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);

        return memoryStream.ToArray();
    }
}
