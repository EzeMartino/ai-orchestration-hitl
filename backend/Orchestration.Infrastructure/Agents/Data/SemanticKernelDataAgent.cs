using Microsoft.SemanticKernel;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Shared;

namespace Orchestration.Infrastructure.Agents.Data;

public sealed class SemanticKernelDataAgent : IDataAgent
{
    private const string PluginName = "PythonAnomalyDetection";
    private const string FunctionName = "analyze_financial_transactions";

    private readonly Kernel _kernel;

    public SemanticKernelDataAgent(PythonAnomalyDetectionPlugin plugin)
    {
        _kernel = new Kernel();

        _kernel.Plugins.AddFromObject(
            plugin,
            PluginName
        );
    }

    public async Task<DataAgentResult> AnalyzeAsync(
        FinancialReportContext report,
        CancellationToken cancellationToken)
    {
        var arguments = new KernelArguments
        {
            ["totalAmount"] = Convert.ToDouble(report.TotalAmount),
            ["transactionCount"] = report.TransactionCount
        };

        var pluginResult = await _kernel.InvokeAsync<PythonAnomalyDetectionPluginResult>(
            PluginName,
            FunctionName,
            arguments,
            cancellationToken
        );

        return pluginResult is null
            ? throw new InvalidOperationException(
                $"{PluginName}.{FunctionName} returned no result."
            )
            : new DataAgentResult(
            HasAnomaly: pluginResult.HasAnomaly,
            Severity: pluginResult.Severity,
            Summary: pluginResult.Summary,
            Engine: pluginResult.Engine,
            Evidence: pluginResult.Evidence
                .Select(x => new AnomalyEvidence(
                    x.Metric,
                    x.Value,
                    x.Threshold,
                    x.Interpretation
                ))
                .ToList()
        );
    }
}
