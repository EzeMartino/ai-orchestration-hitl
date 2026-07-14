using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Shared;

namespace Orchestration.Infrastructure.Agents.Data;

public sealed class ConfigurableDataAgent : IDataAgent
{
    private const string UnexpectedFailureCode = "FINANCIAL_ANALYSIS_UNEXPECTED_FAILURE";
    private const string HumanReviewSummary =
        "No se pudo completar el análisis financiero estructurado; requiere revisión humana.";
    private readonly ILegacyDataAgent _legacyDataAgent;
    private readonly IDataAgentFinancialAnalysisWorkflow _financialAnalysisWorkflow;
    private readonly DataAgentOptions _options;
    private readonly ILogger<ConfigurableDataAgent> _logger;

    public ConfigurableDataAgent(
        ILegacyDataAgent legacyDataAgent,
        IDataAgentFinancialAnalysisWorkflow financialAnalysisWorkflow,
        IOptions<DataAgentOptions> options,
        ILogger<ConfigurableDataAgent> logger)
    {
        _legacyDataAgent = legacyDataAgent;
        _financialAnalysisWorkflow = financialAnalysisWorkflow;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<DataAgentResult> AnalyzeAsync(
        FinancialReportContext report,
        CancellationToken cancellationToken)
    {
        var financialWorkflowEnabled =
            _options.FinancialAnalysisToolsEnabled &&
            _options.UsePythonFinancialAnalysis;

        _logger.LogInformation(
            "DataAgent financial workflow enabled: {FinancialWorkflowEnabled}",
            financialWorkflowEnabled
        );

        if (!financialWorkflowEnabled)
        {
            _logger.LogInformation("Using legacy anomaly detection workflow.");

            return await _legacyDataAgent.AnalyzeAsync(report, cancellationToken);
        }

        try
        {
            _logger.LogInformation("Using financial analysis workflow.");

            return await _financialAnalysisWorkflow.AnalyzeAsync(
                report,
                cancellationToken
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Financial analysis workflow failed for session {SessionId}. FailureCode: {FailureCode}.",
                report.SessionId,
                UnexpectedFailureCode
            );

            if (_options.UseLegacyAnomalyDetectionFallback)
            {
                _logger.LogWarning("Financial workflow failed; using legacy fallback.");

                var fallback = await _legacyDataAgent.AnalyzeAsync(
                    report,
                    cancellationToken
                );

                return fallback with
                {
                    Summary = $"{fallback.Summary} {HumanReviewSummary}",
                    Engine = $"{fallback.Engine} (respaldo legacy)",
                    RequiresHumanReview = true
                };
            }

            return new DataAgentResult(
                HasAnomaly: false,
                Severity: "Unknown",
                Summary: HumanReviewSummary,
                Engine: "Financial Analysis Workflow",
                Evidence: [],
                RequiresHumanReview: true
            );
        }
    }
}
