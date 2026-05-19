using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Shared;

namespace Orchestration.Infrastructure.Agents.Data;

public sealed class ConfigurableDataAgent : IDataAgent
{
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
        if (!_options.FinancialAnalysisToolsEnabled ||
            !_options.UsePythonFinancialAnalysis)
        {
            return await _legacyDataAgent.AnalyzeAsync(report, cancellationToken);
        }

        try
        {
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
                "Financial analysis workflow failed for report {ReportName}.",
                report.ReportName
            );

            if (_options.UseLegacyAnomalyDetectionFallback)
            {
                var fallback = await _legacyDataAgent.AnalyzeAsync(
                    report,
                    cancellationToken
                );

                return fallback with
                {
                    Engine = $"{fallback.Engine} (legacy fallback)"
                };
            }

            return new DataAgentResult(
                HasAnomaly: true,
                Severity: "Medium",
                Summary: "Financial analysis could not be completed. Human review recommended.",
                Engine: "Financial Analysis Workflow",
                Evidence: []
            );
        }
    }
}
