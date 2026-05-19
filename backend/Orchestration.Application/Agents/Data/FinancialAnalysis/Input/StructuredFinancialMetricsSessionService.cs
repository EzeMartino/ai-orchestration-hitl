using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
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

    public StructuredFinancialMetricsSessionService(
        IOrchestrationDbContext dbContext,
        IStructuredFinancialMetricsValidator validator,
        IFinancialMetricInputMapper mapper)
    {
        _dbContext = dbContext;
        _validator = validator;
        _mapper = mapper;
    }

    public async Task<FinancialMetricsSessionSaveResult?> SaveAsync(
        Guid sessionId,
        StructuredFinancialMetricsInput input,
        CancellationToken cancellationToken)
    {
        var session = await _dbContext.AnalysisSessions
            .FirstOrDefaultAsync(x => x.Id == sessionId, cancellationToken);

        if (session is null)
        {
            return null;
        }

        var validationResult = _validator.Validate(input);

        if (!validationResult.IsValid)
        {
            return new FinancialMetricsSessionSaveResult(
                SessionId: sessionId,
                IsValid: false,
                Context: null,
                Errors: validationResult.Errors,
                Warnings: validationResult.Warnings
            );
        }

        var context = new StructuredFinancialMetricsContext(
            DocumentId: input.DocumentId.Trim(),
            Company: NormalizeOptional(input.Company),
            Currency: NormalizeOptional(input.Currency),
            Unit: NormalizeOptional(input.Unit),
            Metrics: _mapper.MapToFinancialMetrics(validationResult),
            ValidationWarnings: validationResult.Warnings,
            UploadedAt: DateTimeOffset.UtcNow
        );

        session.SetContext(MergeContextJson(session.ContextJson, context));

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new FinancialMetricsSessionSaveResult(
            SessionId: sessionId,
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
}
