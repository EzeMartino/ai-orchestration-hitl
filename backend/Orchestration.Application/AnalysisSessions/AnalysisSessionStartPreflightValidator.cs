using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Shared;
using Orchestration.Application.Persistence;
using Orchestration.Domain.AnalysisSessions;
using Orchestration.Domain.FinancialMetricsExtraction;

namespace Orchestration.Application.AnalysisSessions;

public sealed class AnalysisSessionStartPreflightValidator
    : IAnalysisSessionStartPreflightValidator
{
    public const string StructuredFinancialMetricsRequiredCode =
        "STRUCTURED_FINANCIAL_METRICS_REQUIRED";

    public const string StructuredFinancialMetricsRequiredMessage =
        "Se requieren métricas financieras estructuradas para este modo, pero no se adjuntaron a esta sesión.";

    public const string FinancialMetricsReviewRequiredCode =
        "FINANCIAL_METRICS_REVIEW_REQUIRED";

    public const string FinancialMetricsReviewRequiredMessage =
        "Financial metrics extracted from PDF require review before starting this session.";

    private const string ContextPropertyName = "structuredFinancialMetrics";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

    private readonly DataAgentOptions _options;
    private readonly IFinancialReportContextResolver _reportContextResolver;
    private readonly IOrchestrationDbContext? _dbContext;

    public AnalysisSessionStartPreflightValidator(
        IOptions<DataAgentOptions> options,
        IFinancialReportContextResolver reportContextResolver)
    {
        _options = options.Value;
        _reportContextResolver = reportContextResolver;
    }

    public AnalysisSessionStartPreflightValidator(
        IOptions<DataAgentOptions> options,
        IFinancialReportContextResolver reportContextResolver,
        IOrchestrationDbContext dbContext)
        : this(options, reportContextResolver)
    {
        _dbContext = dbContext;
    }

    public async Task<AnalysisSessionStartPreflightResult> ValidateAsync(
        AnalysisSession session,
        CancellationToken cancellationToken)
    {
        var hasPendingFinancialMetricsReview = _dbContext is not null &&
            await _dbContext.FinancialMetricsExtractionDrafts.AnyAsync(
                draft => draft.SessionId == session.Id
                    && draft.Status ==
                        FinancialMetricsExtractionDraftStatus.PendingReview,
                cancellationToken);

        if (hasPendingFinancialMetricsReview)
        {
            return new AnalysisSessionStartPreflightResult(
                CanStart: false,
                Errors:
                [
                    new AnalysisSessionStartPreflightIssue(
                        Code: FinancialMetricsReviewRequiredCode,
                        Message: FinancialMetricsReviewRequiredMessage,
                        Severity: "Error"
                    )
                ],
                Warnings: []
            );
        }

        var reportResolution = _reportContextResolver.Resolve(session);

        if (!reportResolution.IsValid)
        {
            return new AnalysisSessionStartPreflightResult(
                CanStart: false,
                Errors:
                [
                    new AnalysisSessionStartPreflightIssue(
                        Code: reportResolution.ErrorCode!,
                        Message: reportResolution.ErrorMessage!,
                        Severity: "Error")
                ],
                Warnings: []);
        }

        if (!_options.FinancialAnalysisToolsEnabled ||
            !_options.RequireSessionFinancialMetrics)
        {
            return AnalysisSessionStartPreflightResult.Allowed;
        }

        if (HasStructuredFinancialMetrics(session.ContextJson))
        {
            return AnalysisSessionStartPreflightResult.Allowed;
        }

        return new AnalysisSessionStartPreflightResult(
            CanStart: false,
            Errors:
            [
                new AnalysisSessionStartPreflightIssue(
                    Code: StructuredFinancialMetricsRequiredCode,
                    Message: StructuredFinancialMetricsRequiredMessage,
                    Severity: "Error"
                )
            ],
            Warnings: []
        );
    }

    private static bool HasStructuredFinancialMetrics(
        string? contextJson)
    {
        if (string.IsNullOrWhiteSpace(contextJson))
        {
            return false;
        }

        try
        {
            var root = JsonNode.Parse(contextJson);
            var context = root?[ContextPropertyName]
                ?.Deserialize<StructuredFinancialMetricsContext>(JsonOptions);

            return context?.Metrics.Count > 0;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }
}
