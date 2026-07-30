using Microsoft.SemanticKernel;
using System.Text.Json;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Shared;

namespace Orchestration.Infrastructure.Agents.Legal;

public sealed class SemanticKernelLegalAgent(LegalCompliancePlugin plugin) : ILegalAgent
{
    private const string PluginName = "LegalCompliance";
    private const string FunctionName = "review_financial_compliance";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Kernel _kernel = BuildKernel(plugin);

    private static Kernel BuildKernel(LegalCompliancePlugin plugin)
    {
        var builder = Kernel.CreateBuilder();
        var kernel = builder.Build();
        kernel.Plugins.AddFromObject(plugin, PluginName);
        return kernel;
    }

    public Task<LegalAgentResult> ReviewAsync(
        FinancialReportContext report,
        CancellationToken cancellationToken)
    {
        return ReviewAsync(
            report,
            LegalReviewContext.Default,
            cancellationToken
        );
    }

    public async Task<LegalAgentResult> ReviewAsync(
        FinancialReportContext report,
        LegalReviewContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var arguments = new KernelArguments
        {
            ["reportName"] = report.ReportName,
            ["totalAmount"] = Convert.ToDouble(report.TotalAmount),
            ["transactionCount"] = report.TransactionCount,
            ["sessionId"] = report.SessionId.ToString(),
            ["financialAnalysisJson"] = report.FinancialAnalysis is null
                ? null
                : JsonSerializer.Serialize(report.FinancialAnalysis, JsonOptions),
            ["allowPersistedFinancialAnalysisFallback"] =
                context.ResolutionMode ==
                    FinancialAnalysisResolutionMode.ProvidedOrPersisted,
            ["dataEvidenceJson"] = context.DataEvidence is null
                ? null
                : JsonSerializer.Serialize(context.DataEvidence, JsonOptions)
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
            Warnings: pluginResult.Warnings,
            QueryStrategy: pluginResult.QueryStrategy,
            LegalReview: pluginResult.LegalReview,
            RequiresHumanReview: pluginResult.RequiresHumanReview,
            EvidenceAssessment: pluginResult.EvidenceAssessment,
            EvidenceEnrichments: pluginResult.EvidenceEnrichments
        );
    }
}
