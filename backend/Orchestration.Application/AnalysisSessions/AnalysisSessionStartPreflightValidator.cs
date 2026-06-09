using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Domain.AnalysisSessions;

namespace Orchestration.Application.AnalysisSessions;

public sealed class AnalysisSessionStartPreflightValidator
    : IAnalysisSessionStartPreflightValidator
{
    public const string StructuredFinancialMetricsRequiredCode =
        "STRUCTURED_FINANCIAL_METRICS_REQUIRED";

    public const string StructuredFinancialMetricsRequiredMessage =
        "Se requieren métricas financieras estructuradas para este modo, pero no se adjuntaron a esta sesión.";

    private const string ContextPropertyName = "structuredFinancialMetrics";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

    private readonly DataAgentOptions _options;

    public AnalysisSessionStartPreflightValidator(
        IOptions<DataAgentOptions> options)
    {
        _options = options.Value;
    }

    public Task<AnalysisSessionStartPreflightResult> ValidateAsync(
        AnalysisSession session,
        CancellationToken cancellationToken)
    {
        if (!_options.FinancialAnalysisToolsEnabled ||
            !_options.RequireSessionFinancialMetrics)
        {
            return Task.FromResult(AnalysisSessionStartPreflightResult.Allowed);
        }

        if (HasStructuredFinancialMetrics(session.ContextJson))
        {
            return Task.FromResult(AnalysisSessionStartPreflightResult.Allowed);
        }

        return Task.FromResult(new AnalysisSessionStartPreflightResult(
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
        ));
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
