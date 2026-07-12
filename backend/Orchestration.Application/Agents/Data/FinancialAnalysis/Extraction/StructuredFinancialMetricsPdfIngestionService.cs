using System.Diagnostics;
using Microsoft.Extensions.Options;
using Orchestration.Application.Activity;
using Orchestration.Application.Agents.Data.FinancialAnalysis;

namespace Orchestration.Application.Agents.Data.FinancialAnalysis.Extraction;

public sealed class StructuredFinancialMetricsPdfIngestionService
    : IStructuredFinancialMetricsPdfIngestionService
{
    private const string AgentName = "DataAgent";
    private const string EventType = "FinancialMetricsPdfIngestion";

    private readonly IStructuredFinancialMetricsPdfExtractor _pdfExtractor;
    private readonly IFinancialMetricsExtractionCompletenessEvaluator
        _completenessEvaluator;
    private readonly IFinancialDocumentMarkdownConverter _markdownConverter;
    private readonly IFinancialDocumentProcessingGate _processingGate;
    private readonly ISearchablePdfOcrService _ocrService;
    private readonly IFinancialDocumentExtractionAgent _semanticAgent;
    private readonly IFinancialMetricCandidateReconciler _reconciler;
    private readonly IFinancialMetricsExtractionDraftService _draftService;
    private readonly IStructuredFinancialMetricsSessionService _sessionService;
    private readonly IActivityEventPublisher _activityPublisher;
    private readonly FinancialMetricsExtractionOptions _extractionOptions;
    private readonly StructuredFinancialMetricsPdfExtractionOptions
        _pdfExtractionOptions;

    public StructuredFinancialMetricsPdfIngestionService(
        IStructuredFinancialMetricsPdfExtractor pdfExtractor,
        IFinancialMetricsExtractionCompletenessEvaluator completenessEvaluator,
        IFinancialDocumentMarkdownConverter markdownConverter,
        IFinancialDocumentProcessingGate processingGate,
        ISearchablePdfOcrService ocrService,
        IFinancialDocumentExtractionAgent semanticAgent,
        IFinancialMetricCandidateReconciler reconciler,
        IFinancialMetricsExtractionDraftService draftService,
        IStructuredFinancialMetricsSessionService sessionService,
        IActivityEventPublisher activityPublisher,
        IOptions<FinancialMetricsExtractionOptions> extractionOptions,
        IOptions<StructuredFinancialMetricsPdfExtractionOptions> pdfExtractionOptions)
    {
        _pdfExtractor = pdfExtractor;
        _completenessEvaluator = completenessEvaluator;
        _markdownConverter = markdownConverter;
        _processingGate = processingGate;
        _ocrService = ocrService;
        _semanticAgent = semanticAgent;
        _reconciler = reconciler;
        _draftService = draftService;
        _sessionService = sessionService;
        _activityPublisher = activityPublisher;
        _extractionOptions = (extractionOptions ?? throw new ArgumentNullException(
            nameof(extractionOptions))).Value;
        _pdfExtractionOptions = (pdfExtractionOptions ?? throw new ArgumentNullException(
            nameof(pdfExtractionOptions))).Value;
    }

    public async Task<StructuredFinancialMetricsPdfIngestionResult> IngestAsync(
        StructuredFinancialMetricsPdfIngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        var total = Stopwatch.StartNew();
        var reasonCodes = new List<string>();
        var diagnostics = new MutableDiagnostics();

        if (request.PdfBytes.Length == 0)
        {
            return await FailAsync(
                request,
                diagnostics,
                total,
                "pdf_ingestion_failed",
                "PDF bytes are required.",
                reasonCodes,
                cancellationToken);
        }

        using var deterministicPdf = CreatePdfStream(request.PdfBytes);
        var deterministicResult = await _pdfExtractor.ExtractAsync(
            deterministicPdf,
            new StructuredFinancialMetricsPdfExtractionRequest(
                request.DocumentId,
                request.Company,
                request.Currency,
                request.Unit,
                request.OriginalFileName),
            cancellationToken);

        diagnostics.NativeTextAvailable = deterministicResult.NativeTextAvailable;
        var decision = _completenessEvaluator.Evaluate(
            deterministicResult,
            _extractionOptions);
        reasonCodes.AddRange(decision.ReasonCodes);

        if (!decision.RequiresSemanticFallback)
        {
            if (deterministicResult is { IsValid: true, Input: not null })
            {
                return await SaveAcceptedAsync(
                    request,
                    deterministicResult.Input,
                    "pdf_file",
                    deterministicResult.Errors,
                    deterministicResult.Warnings,
                    diagnostics,
                    total,
                    reasonCodes,
                    cancellationToken);
            }

            return await CreateDeterministicReviewOrFailureAsync(
                request,
                deterministicResult,
                diagnostics,
                total,
                reasonCodes,
                cancellationToken);
        }

        await PublishFallbackReasonsAsync(request.SessionId, reasonCodes, cancellationToken);

        if (!_extractionOptions.SemanticEnrichmentEnabled)
        {
            if (IsShadowMode(_extractionOptions))
            {
                return await CreateShadowSaveOrFailureAsync(
                    request,
                    deterministicResult,
                    diagnostics,
                    total,
                    reasonCodes,
                    cancellationToken);
            }

            return await CreateDeterministicReviewOrFailureAsync(
                request,
                deterministicResult,
                diagnostics,
                total,
                reasonCodes,
                cancellationToken);
        }

        var deterministicInput = deterministicResult.Input ?? EmptyInput(request);

        var semantic = await TryRunSemanticFallbackAsync(
            request,
            deterministicResult,
            diagnostics,
            reasonCodes,
            cancellationToken);

        if (semantic.Result is null)
        {
            if (IsShadowMode(_extractionOptions))
            {
                return await CreateShadowSaveOrFailureAsync(
                    request,
                    deterministicResult,
                    diagnostics,
                    total,
                    reasonCodes,
                    cancellationToken);
            }

            if (deterministicResult.Input is not null)
            {
                var validationIssues =
                    deterministicResult.Errors.Concat(deterministicResult.Warnings).ToArray();
                return await CreateReviewAsync(
                    request,
                    deterministicResult.Input,
                    CreateDeterministicCandidates(deterministicResult.Input),
                    [],
                    GetMissingFields(deterministicResult.Input),
                    validationIssues,
                    diagnostics,
                    total,
                    reasonCodes,
                    cancellationToken);
            }

            return await FailAsync(
                request,
                diagnostics,
                total,
                "pdf_ingestion_failed",
                semantic.FailureReason ?? "Semantic extraction failed and no deterministic metrics were available.",
                reasonCodes,
                cancellationToken);
        }

        var reconciliation = _reconciler.Reconcile(
            deterministicInput,
            semantic.Result,
            _extractionOptions);
        diagnostics.CandidateCount = reconciliation.Candidates.Count;
        diagnostics.ConflictCount = reconciliation.Conflicts.Count;

        if (IsShadowMode(_extractionOptions))
        {
            return await CreateShadowSaveOrFailureAsync(
                request,
                deterministicResult,
                diagnostics,
                total,
                reasonCodes,
                cancellationToken);
        }

        if (IsAutoAcceptMode(_extractionOptions)
            && reconciliation.CanAutoAccept
            && !reconciliation.RequiresReview)
        {
            return await SaveAcceptedAsync(
                request,
                reconciliation.ProposedInput,
                "pdf_file_semantic",
                [],
                [],
                diagnostics,
                total,
                reasonCodes,
                cancellationToken);
        }

        return await CreateReviewAsync(
            request,
            reconciliation.ProposedInput,
            reconciliation.Candidates,
            reconciliation.Conflicts,
            reconciliation.MissingFields,
            deterministicResult.Errors.Concat(deterministicResult.Warnings).ToArray(),
            diagnostics,
            total,
            reasonCodes,
            cancellationToken,
            reconciliation.MetadataCandidates);
    }

    private async Task<SemanticFallbackResult> TryRunSemanticFallbackAsync(
        StructuredFinancialMetricsPdfIngestionRequest request,
        StructuredFinancialMetricsPdfExtractionResult deterministicResult,
        MutableDiagnostics diagnostics,
        List<string> reasonCodes,
        CancellationToken cancellationToken)
    {
        using var processingLease = await _processingGate.EnterAsync(cancellationToken);
        byte[] markdownPdfBytes = request.PdfBytes;

        if (!deterministicResult.NativeTextAvailable)
        {
            diagnostics.OcrAttempted = true;
            SearchablePdfOcrResult ocr;
            try
            {
                using var ocrPdf = CreatePdfStream(request.PdfBytes);
                ocr = await _ocrService.CreateSearchablePdfAsync(
                    ocrPdf,
                    _pdfExtractionOptions,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                reasonCodes.Add("ocr_failed");

                return new SemanticFallbackResult(null, "ocr_failed");
            }

            diagnostics.OcrSucceeded = ocr.Succeeded;

            if (!ocr.Succeeded)
            {
                reasonCodes.Add("ocr_failed");

                return new SemanticFallbackResult(null, ocr.FailureReason);
            }

            markdownPdfBytes = ocr.PdfBytes;
        }

        diagnostics.MarkItDownAttempted = true;
        FinancialDocumentMarkdownResult markdown;
        try
        {
            using var markdownPdf = CreatePdfStream(markdownPdfBytes);
            using var conversionTimeout =
                CreateTimeoutTokenSource(
                    cancellationToken,
                    _extractionOptions.ConversionTimeoutSeconds);
            markdown = await _markdownConverter.ConvertPdfAsync(
                markdownPdf,
                _extractionOptions.MaxMarkdownCharacters,
                GetMaxSourcePage(_pdfExtractionOptions),
                conversionTimeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            reasonCodes.Add("markitdown_timeout");

            return new SemanticFallbackResult(null, "markitdown_timeout");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            reasonCodes.Add("markitdown_failed");

            return new SemanticFallbackResult(null, "markitdown_failed");
        }

        diagnostics.MarkItDownSucceeded = markdown.Succeeded;

        if (!markdown.Succeeded)
        {
            reasonCodes.Add("markitdown_failed");

            return new SemanticFallbackResult(null, markdown.FailureReason);
        }

        diagnostics.MarkdownCharacterCount = markdown.Markdown.Length;
        diagnostics.SemanticAttempted = true;
        FinancialDocumentExtractionParseResult semantic;
        try
        {
            semantic = await _semanticAgent.ExtractAsync(
                new FinancialDocumentExtractionRequest(
                    markdown.Markdown,
                    _extractionOptions.MaxEvidenceExcerptCharacters,
                    _extractionOptions.MaxMarkdownChunks,
                    GetMaxSourcePage(_pdfExtractionOptions)),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            reasonCodes.Add("semantic_failed");

            return new SemanticFallbackResult(null, "semantic_failed");
        }

        diagnostics.SemanticSucceeded = semantic.Succeeded;

        if (!semantic.Succeeded)
        {
            reasonCodes.Add("semantic_failed");

            return new SemanticFallbackResult(null, semantic.FailureReason);
        }

        return new SemanticFallbackResult(semantic.Result, semantic.FailureReason);
    }

    private async Task<StructuredFinancialMetricsPdfIngestionResult>
        SaveAcceptedAsync(
            StructuredFinancialMetricsPdfIngestionRequest request,
            StructuredFinancialMetricsInput input,
            string ingestionMethod,
            IReadOnlyList<FinancialMetricsValidationIssue> errors,
            IReadOnlyList<FinancialMetricsValidationIssue> warnings,
            MutableDiagnostics diagnostics,
            Stopwatch total,
            IReadOnlyList<string> reasonCodes,
            CancellationToken cancellationToken)
    {
        var saveResult = await _sessionService.SaveAsync(
            new SaveStructuredFinancialMetricsRequest(
                request.SessionId,
                input,
                CreateProvenance(request, ingestionMethod)),
            cancellationToken);

        if (saveResult is null || !saveResult.IsValid)
        {
            return await FailAsync(
                request,
                diagnostics,
                total,
                "pdf_ingestion_failed",
                "Financial metrics save failed.",
                reasonCodes,
                cancellationToken,
                saveResult?.Errors ?? []);
        }

        await PublishOutcomeAsync(
            request.SessionId,
            FinancialMetricsFileOutcome.Accepted,
            diagnostics,
            total,
            reasonCodes,
            cancellationToken);

        return new StructuredFinancialMetricsPdfIngestionResult(
            FinancialMetricsFileOutcome.Accepted,
            saveResult,
            null,
            errors,
            warnings);
    }

    private async Task<StructuredFinancialMetricsPdfIngestionResult>
        CreateDeterministicReviewOrFailureAsync(
            StructuredFinancialMetricsPdfIngestionRequest request,
            StructuredFinancialMetricsPdfExtractionResult deterministicResult,
            MutableDiagnostics diagnostics,
            Stopwatch total,
            IReadOnlyList<string> reasonCodes,
            CancellationToken cancellationToken)
    {
        if (deterministicResult.Input is null)
        {
            return await FailAsync(
                request,
                diagnostics,
                total,
                "pdf_ingestion_failed",
                "No usable deterministic financial metrics were extracted.",
                reasonCodes,
                cancellationToken,
                deterministicResult.Errors);
        }

        return await CreateReviewAsync(
            request,
            deterministicResult.Input,
            CreateDeterministicCandidates(deterministicResult.Input),
            [],
            GetMissingFields(deterministicResult.Input),
            deterministicResult.Errors.Concat(deterministicResult.Warnings).ToArray(),
            diagnostics,
            total,
            reasonCodes,
            cancellationToken);
    }

    private async Task<StructuredFinancialMetricsPdfIngestionResult>
        CreateShadowSaveOrFailureAsync(
            StructuredFinancialMetricsPdfIngestionRequest request,
            StructuredFinancialMetricsPdfExtractionResult deterministicResult,
            MutableDiagnostics diagnostics,
            Stopwatch total,
            IReadOnlyList<string> reasonCodes,
            CancellationToken cancellationToken)
    {
        if (deterministicResult is { IsValid: true, Input: not null })
        {
            return await SaveAcceptedAsync(
                request,
                deterministicResult.Input,
                "pdf_file",
                deterministicResult.Errors,
                deterministicResult.Warnings,
                diagnostics,
                total,
                reasonCodes,
                cancellationToken);
        }

        return await FailAsync(
            request,
            diagnostics,
            total,
            "pdf_ingestion_failed",
            "Deterministic financial metrics are incomplete in shadow mode.",
            reasonCodes,
            cancellationToken,
            deterministicResult.Errors);
    }

    private async Task<StructuredFinancialMetricsPdfIngestionResult>
        CreateReviewAsync(
            StructuredFinancialMetricsPdfIngestionRequest request,
            StructuredFinancialMetricsInput proposedInput,
            IReadOnlyList<FinancialMetricCandidate> candidates,
            IReadOnlyList<FinancialMetricCandidateConflict> conflicts,
            IReadOnlyList<string> missingFields,
            IReadOnlyList<FinancialMetricsValidationIssue> validationIssues,
            MutableDiagnostics diagnostics,
            Stopwatch total,
            IReadOnlyList<string> reasonCodes,
            CancellationToken cancellationToken,
            IReadOnlyList<FinancialDocumentMetadataCandidate>? metadataCandidates = null)
    {
        var payload = new FinancialMetricsExtractionDraftPayload(
            FinancialMetricsExtractionDraftPayload.CurrentSchemaVersion,
            proposedInput,
            candidates,
            conflicts,
            missingFields,
            reasonCodes,
            validationIssues,
            diagnostics.ToImmutable(total, reasonCodes))
        {
            MetadataCandidates = metadataCandidates ?? []
        };

        var draft = await _draftService.CreateOrReplaceAsync(
            request.SessionId,
            request.UserId,
            new CreateFinancialMetricsExtractionDraftRequest(
                request.OriginalFileName,
                request.FileSizeBytes,
                request.ContentHash,
                payload),
            cancellationToken);

        if (draft.Kind != FinancialMetricsExtractionDraftResultKind.Success
            || draft.Draft is null)
        {
            return await FailAsync(
                request,
                diagnostics,
                total,
                "pdf_ingestion_failed",
                draft.Errors.FirstOrDefault() ?? "Financial metrics review draft could not be created.",
                reasonCodes,
                cancellationToken,
                draft.ValidationIssues);
        }

        await PublishOutcomeAsync(
            request.SessionId,
            FinancialMetricsFileOutcome.ReviewRequired,
            diagnostics,
            total,
            reasonCodes,
            cancellationToken);

        return new StructuredFinancialMetricsPdfIngestionResult(
            FinancialMetricsFileOutcome.ReviewRequired,
            null,
            draft.Draft,
            validationIssues
                .Where(issue => !IsWarning(issue))
                .ToArray(),
            validationIssues
                .Where(IsWarning)
                .ToArray());
    }

    private async Task<StructuredFinancialMetricsPdfIngestionResult> FailAsync(
        StructuredFinancialMetricsPdfIngestionRequest request,
        MutableDiagnostics diagnostics,
        Stopwatch total,
        string code,
        string message,
        IReadOnlyList<string> reasonCodes,
        CancellationToken cancellationToken,
        IReadOnlyList<FinancialMetricsValidationIssue>? additionalIssues = null)
    {
        await PublishOutcomeAsync(
            request.SessionId,
            FinancialMetricsFileOutcome.Failed,
            diagnostics,
            total,
            reasonCodes,
            cancellationToken);

        var errors = new List<FinancialMetricsValidationIssue>
        {
            new(code, message, null, null, "Error")
        };
        errors.AddRange(additionalIssues ?? []);

        return new StructuredFinancialMetricsPdfIngestionResult(
            FinancialMetricsFileOutcome.Failed,
            null,
            null,
            errors,
            []);
    }

    private async Task PublishFallbackReasonsAsync(
        Guid sessionId,
        IReadOnlyList<string> reasonCodes,
        CancellationToken cancellationToken)
    {
        foreach (var reasonCode in reasonCodes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await PublishAsync(
                sessionId,
                $"fallback_reason:{Bound(reasonCode)}",
                cancellationToken);
        }
    }

    private async Task PublishOutcomeAsync(
        Guid sessionId,
        FinancialMetricsFileOutcome outcome,
        MutableDiagnostics diagnostics,
        Stopwatch total,
        IReadOnlyList<string> reasonCodes,
        CancellationToken cancellationToken)
    {
        var immutable = diagnostics.ToImmutable(total, reasonCodes);
        var reasons = immutable.ReasonCodes.Count == 0
            ? "none"
            : string.Join(",", immutable.ReasonCodes.Select(Bound));
        await PublishAsync(
            sessionId,
            $"outcome:{outcome}; reasons:{Bound(reasons)}",
            cancellationToken);
    }

    private Task PublishAsync(
        Guid sessionId,
        string message,
        CancellationToken cancellationToken)
    {
        return _activityPublisher.PublishAsync(
            new Orchestration.Application.Activity.ActivityEvent(
                sessionId,
                EventType,
                AgentName,
                Bound(message),
                DateTimeOffset.UtcNow),
            cancellationToken);
    }

    private static MemoryStream CreatePdfStream(byte[] pdfBytes)
    {
        return new MemoryStream(pdfBytes, writable: false);
    }

    private static StructuredFinancialMetricsProvenanceInput CreateProvenance(
        StructuredFinancialMetricsPdfIngestionRequest request,
        string ingestionMethod)
    {
        return new StructuredFinancialMetricsProvenanceInput(
            ingestionMethod,
            request.OriginalFileName,
            request.FileSizeBytes,
            request.ContentHash);
    }

    private static IReadOnlyList<FinancialMetricCandidate>
        CreateDeterministicCandidates(StructuredFinancialMetricsInput input)
    {
        return input.Metrics
            .Select(metric => new FinancialMetricCandidate(
                Guid.NewGuid(),
                metric.Name,
                metric.Period,
                metric.Value,
                metric.Currency,
                metric.Unit,
                FinancialMetricCandidateSourceKinds.Reported,
                metric.Confidence ?? 1m,
                metric.SourcePage,
                metric.Source ?? "deterministic_pdf_parser",
                "deterministic_pdf_parser",
                FinancialMetricCandidateReviewStates.Explicit,
                null))
            .ToArray();
    }

    private static StructuredFinancialMetricsInput EmptyInput(
        StructuredFinancialMetricsPdfIngestionRequest request)
    {
        return new StructuredFinancialMetricsInput(
            request.DocumentId,
            request.Company,
            request.Currency,
            request.Unit,
            []);
    }

    private static IReadOnlyList<string> GetMissingFields(
        StructuredFinancialMetricsInput input)
    {
        var fields = new List<string>();

        if (string.IsNullOrWhiteSpace(input.Company))
        {
            fields.Add("company");
        }

        if (string.IsNullOrWhiteSpace(input.Currency))
        {
            fields.Add("currency");
        }

        if (string.IsNullOrWhiteSpace(input.Unit))
        {
            fields.Add("unit");
        }

        if (input.Metrics.Count == 0)
        {
            fields.Add("metrics");
        }

        return fields;
    }

    private static bool IsWarning(FinancialMetricsValidationIssue issue)
    {
        return string.Equals(
            issue.Severity,
            "Warning",
            StringComparison.OrdinalIgnoreCase);
    }

    private static CancellationTokenSource CreateTimeoutTokenSource(
        CancellationToken cancellationToken,
        int timeoutSeconds)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);

        if (timeoutSeconds <= 0)
        {
            timeout.Cancel();
        }
        else
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        }

        return timeout;
    }

    private static int GetMaxSourcePage(
        StructuredFinancialMetricsPdfExtractionOptions options)
    {
        return Math.Max(1, options.MaxPages);
    }

    private static bool IsAutoAcceptMode(FinancialMetricsExtractionOptions options)
    {
        return string.Equals(options.Mode, "AutoAccept", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsShadowMode(FinancialMetricsExtractionOptions options)
    {
        return string.Equals(options.Mode, "Shadow", StringComparison.OrdinalIgnoreCase);
    }

    private static string Bound(string value)
    {
        return value.Length <= 200 ? value : value[..200];
    }

    private sealed record SemanticFallbackResult(
        FinancialDocumentExtractionResult? Result,
        string? FailureReason);

    private sealed class MutableDiagnostics
    {
        public bool NativeTextAvailable { get; set; }
        public bool OcrAttempted { get; set; }
        public bool OcrSucceeded { get; set; }
        public bool MarkItDownAttempted { get; set; }
        public bool MarkItDownSucceeded { get; set; }
        public bool SemanticAttempted { get; set; }
        public bool SemanticSucceeded { get; set; }
        public int? PageCount { get; set; }
        public int? MarkdownCharacterCount { get; set; }
        public int? CandidateCount { get; set; }
        public int? ConflictCount { get; set; }

        public FinancialMetricsExtractionDiagnostics ToImmutable(
            Stopwatch total,
            IReadOnlyList<string> reasonCodes)
        {
            return new FinancialMetricsExtractionDiagnostics
            {
                NativeTextAvailable = NativeTextAvailable,
                OcrAttempted = OcrAttempted,
                OcrSucceeded = OcrSucceeded,
                MarkItDownAttempted = MarkItDownAttempted,
                MarkItDownSucceeded = MarkItDownSucceeded,
                SemanticAttempted = SemanticAttempted,
                SemanticSucceeded = SemanticSucceeded,
                PageCount = PageCount,
                MarkdownCharacterCount = MarkdownCharacterCount,
                CandidateCount = CandidateCount,
                ConflictCount = ConflictCount,
                TotalDurationMilliseconds = total.ElapsedMilliseconds,
                ReasonCodes = reasonCodes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            };
        }
    }
}
