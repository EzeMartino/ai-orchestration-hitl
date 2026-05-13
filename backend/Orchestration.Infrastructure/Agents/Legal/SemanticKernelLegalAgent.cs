using Microsoft.SemanticKernel;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Shared;

namespace Orchestration.Infrastructure.Agents.Legal;

public sealed class SemanticKernelLegalAgent : ILegalAgent
{
    private const string PluginName = "LegalCompliance";
    private const string FunctionName = "review_financial_compliance";

    private readonly Kernel _kernel;

    public SemanticKernelLegalAgent(LegalCompliancePlugin plugin)
    {
        var builder = Kernel.CreateBuilder();

        _kernel = builder.Build();

        _kernel.Plugins.AddFromObject(
            plugin,
            PluginName
        );
    }

    public async Task<LegalAgentResult> ReviewAsync(
        FinancialReportContext report,
        CancellationToken cancellationToken)
    {
        var arguments = new KernelArguments
        {
            ["reportName"] = report.ReportName,
            ["totalAmount"] = Convert.ToDouble(report.TotalAmount),
            ["transactionCount"] = report.TransactionCount
        };

        var pluginResult = await _kernel.InvokeAsync<LegalCompliancePluginResult>(
            PluginName,
            FunctionName,
            arguments,
            cancellationToken
        );

        return pluginResult is null
           ? throw new InvalidOperationException(
               $"{PluginName}.{FunctionName} returned no result."
           )
           : new LegalAgentResult(
            HasComplianceRisk: pluginResult.HasComplianceRisk,
            RiskLevel: pluginResult.RiskLevel,
            Summary: pluginResult.Summary,
            Engine: pluginResult.Engine,
            Evidence: pluginResult.Evidence
                .Select(x => new LegalEvidence(
                    x.Regulation,
                    x.Section,
                    x.Finding,
                    x.Source
                ))
                .ToList(),
            Warnings: pluginResult.Warnings
        );
    }
}
