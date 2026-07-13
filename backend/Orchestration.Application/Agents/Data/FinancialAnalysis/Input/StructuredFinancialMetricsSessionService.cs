using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Orchestration.Application.Activity;
using Orchestration.Application.Agents.Shared;
using Orchestration.Application.Persistence;

namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed class StructuredFinancialMetricsSessionService
    : IStructuredFinancialMetricsSessionService
{
    private const string FinancialReportContextPropertyName = "financialReport";
    private const string MetricsContextPropertyName = "structuredFinancialMetrics";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

    private readonly IOrchestrationDbContext _dbContext;
    private readonly IStructuredFinancialMetricsValidator _validator;
    private readonly IFinancialMetricInputMapper _mapper;
    private readonly IActivityEventPublisher _activityPublisher;

    public StructuredFinancialMetricsSessionService(
        IOrchestrationDbContext dbContext,
        IStructuredFinancialMetricsValidator validator,
        IFinancialMetricInputMapper mapper,
        IActivityEventPublisher activityPublisher)
    {
        _dbContext = dbContext;
        _validator = validator;
        _mapper = mapper;
        _activityPublisher = activityPublisher;
    }

    public async Task<FinancialMetricsSessionSaveResult?> SaveAsync(
        Guid sessionId,
        StructuredFinancialMetricsInput input,
        CancellationToken cancellationToken)
    {
        return await SaveAsync(
            new SaveStructuredFinancialMetricsRequest(
                SessionId: sessionId,
                Input: input,
                Provenance: null
            ),
            cancellationToken
        );
    }

    public async Task<FinancialMetricsSessionSaveResult?> SaveAsync(
        SaveStructuredFinancialMetricsRequest request,
        CancellationToken cancellationToken)
    {
        var staged = await StageAsync(request, cancellationToken);

        if (staged is null || !staged.IsValid || staged.Context is null)
        {
            return staged;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _activityPublisher.PublishAsync(
            new ActivityEvent(
                request.SessionId,
                "structured_financial_metrics_attached",
                "DataAgent",
                $"Métricas financieras estructuradas adjuntas: {staged.Context.Metrics.Count} métricas desde {staged.Context.Provenance!.IngestionMethod}.",
                DateTimeOffset.UtcNow
            ),
            cancellationToken
        );

        return staged;
    }

    public async Task<FinancialMetricsSessionSaveResult?> StageAsync(
        SaveStructuredFinancialMetricsRequest request,
        CancellationToken cancellationToken)
    {
        var session = await _dbContext.AnalysisSessions
            .FirstOrDefaultAsync(x => x.Id == request.SessionId, cancellationToken);

        if (session is null)
        {
            return null;
        }

        var validationResult = _validator.Validate(request.Input);

        if (!validationResult.IsValid)
        {
            return new FinancialMetricsSessionSaveResult(
                SessionId: request.SessionId,
                IsValid: false,
                Context: null,
                Errors: validationResult.Errors,
                Warnings: validationResult.Warnings,
                ReportSummary: null
            );
        }

        var reportSummary = validationResult.ReportSummary
            ?? throw new InvalidOperationException(
                "A valid financial metrics result must include a report summary.");

        var metrics = _mapper.MapToFinancialMetrics(validationResult);
        var provenance = BuildProvenance(
            request.Provenance,
            metrics.Count,
            validationResult.Warnings.Count
        );
        var context = new StructuredFinancialMetricsContext(
            DocumentId: request.Input.DocumentId.Trim(),
            Company: NormalizeOptional(request.Input.Company),
            Currency: NormalizeOptional(request.Input.Currency),
            Unit: NormalizeOptional(request.Input.Unit),
            Metrics: metrics,
            ValidationWarnings: validationResult.Warnings,
            UploadedAt: DateTimeOffset.UtcNow,
            Provenance: provenance
        );

        session.SetContext(MergeContextJson(
            session.ContextJson,
            reportSummary,
            context));

        return new FinancialMetricsSessionSaveResult(
            SessionId: request.SessionId,
            IsValid: true,
            Context: context,
            Errors: validationResult.Errors,
            Warnings: validationResult.Warnings,
            ReportSummary: reportSummary
        );
    }

    public async Task<StructuredFinancialMetricsContext?> GetAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var sessionContext = await GetSessionContextAsync(sessionId, cancellationToken);

        return sessionContext.Metrics;
    }

    public async Task<FinancialReportSummary?> GetReportSummaryAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var sessionContext = await GetSessionContextAsync(sessionId, cancellationToken);

        return sessionContext.ReportSummary;
    }

    public async Task<StructuredFinancialMetricsSessionContext> GetSessionContextAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var contextJson = await _dbContext.AnalysisSessions
            .Where(x => x.Id == sessionId)
            .Select(x => x.ContextJson)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(contextJson))
        {
            return EmptySessionContext();
        }

        try
        {
            var root = JsonNode.Parse(contextJson) as JsonObject;

            if (root is null)
            {
                return EmptySessionContext();
            }

            return new StructuredFinancialMetricsSessionContext(
                DeserializeMetrics(root[MetricsContextPropertyName]),
                DeserializeReportSummary(root[FinancialReportContextPropertyName]));
        }
        catch (JsonException)
        {
            return EmptySessionContext();
        }
    }

    private static StructuredFinancialMetricsContext? DeserializeMetrics(
        JsonNode? node)
    {
        try
        {
            return node?.Deserialize<StructuredFinancialMetricsContext>(JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static FinancialReportSummary? DeserializeReportSummary(
        JsonNode? node)
    {
        try
        {
            var input = node?.Deserialize<FinancialReportSummaryInput>(JsonOptions);
            var validation = FinancialReportSummaryValidator.Validate(input);

            return validation.IsValid
                ? validation.Summary
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static StructuredFinancialMetricsSessionContext EmptySessionContext()
    {
        return new StructuredFinancialMetricsSessionContext(null, null);
    }

    private static string MergeContextJson(
        string? existingContextJson,
        FinancialReportSummary reportSummary,
        StructuredFinancialMetricsContext context)
    {
        var root = ParseRoot(existingContextJson);
        root[FinancialReportContextPropertyName] =
            JsonSerializer.SerializeToNode(reportSummary, JsonOptions);
        root[MetricsContextPropertyName] =
            JsonSerializer.SerializeToNode(context, JsonOptions);

        return root.ToJsonString(JsonOptions);
    }

    private static JsonObject ParseRoot(
        string? existingContextJson)
    {
        if (string.IsNullOrWhiteSpace(existingContextJson))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(existingContextJson) as JsonObject ??
                new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }

    private static string? NormalizeOptional(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static StructuredFinancialMetricsProvenance BuildProvenance(
        StructuredFinancialMetricsProvenanceInput? input,
        int metricCount,
        int warningCount)
    {
        return new StructuredFinancialMetricsProvenance(
            IngestionMethod: NormalizeIngestionMethod(input?.IngestionMethod),
            OriginalFileName: NormalizeOptional(input?.OriginalFileName),
            FileSizeBytes: input?.FileSizeBytes,
            ContentHash: NormalizeOptional(input?.ContentHash),
            MetricCount: metricCount,
            WarningCount: warningCount
        );
    }

    private static string NormalizeIngestionMethod(
        string? ingestionMethod)
    {
        return ingestionMethod is
            "json_paste" or
            "csv_paste" or
            "json_file" or
            "csv_file" or
            "pdf_file" or
            "pdf_file_reviewed" or
            "pdf_file_semantic"
            ? ingestionMethod
            : "unknown";
    }
}
