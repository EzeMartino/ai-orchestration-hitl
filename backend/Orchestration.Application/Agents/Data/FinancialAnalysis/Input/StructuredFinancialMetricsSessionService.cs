using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Orchestration.Application.Activity;
using Orchestration.Application.Persistence;

namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed class StructuredFinancialMetricsSessionService
    : IStructuredFinancialMetricsSessionService
{
    private const string ContextPropertyName = "structuredFinancialMetrics";

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
                Warnings: validationResult.Warnings
            );
        }

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

        session.SetContext(MergeContextJson(session.ContextJson, context));

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _activityPublisher.PublishAsync(
            new ActivityEvent(
                session.Id,
                "structured_financial_metrics_attached",
                "DataAgent",
                $"Structured financial metrics attached: {metrics.Count} metrics from {provenance.IngestionMethod}.",
                DateTimeOffset.UtcNow
            ),
            cancellationToken
        );

        return new FinancialMetricsSessionSaveResult(
            SessionId: request.SessionId,
            IsValid: true,
            Context: context,
            Errors: validationResult.Errors,
            Warnings: validationResult.Warnings
        );
    }

    public async Task<StructuredFinancialMetricsContext?> GetAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var contextJson = await _dbContext.AnalysisSessions
            .Where(x => x.Id == sessionId)
            .Select(x => x.ContextJson)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(contextJson))
        {
            return null;
        }

        try
        {
            var root = JsonNode.Parse(contextJson) as JsonObject;

            return root?[ContextPropertyName]?.Deserialize<StructuredFinancialMetricsContext>(
                JsonOptions
            );
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string MergeContextJson(
        string? existingContextJson,
        StructuredFinancialMetricsContext context)
    {
        var root = ParseRoot(existingContextJson);
        root[ContextPropertyName] = JsonSerializer.SerializeToNode(context, JsonOptions);

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
            "csv_file"
            ? ingestionMethod
            : "unknown";
    }
}
