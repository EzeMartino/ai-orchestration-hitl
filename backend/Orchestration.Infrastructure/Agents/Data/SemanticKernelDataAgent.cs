using Microsoft.SemanticKernel;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Shared;

namespace Orchestration.Infrastructure.Agents.Data;

public sealed class SemanticKernelDataAgent(PythonAnomalyDetectionPlugin plugin) : ILegacyDataAgent
{
    private const string PluginName = "PythonAnomalyDetection";
    private const string FunctionName = "analyze_financial_transactions";

    private readonly Kernel _kernel = BuildKernel(plugin);

    private static Kernel BuildKernel(PythonAnomalyDetectionPlugin plugin)
    {
        var kernel = new Kernel();
        kernel.Plugins.AddFromObject(plugin, PluginName);
        return kernel;
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
