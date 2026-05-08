using System.ComponentModel;
using Microsoft.SemanticKernel;
using Orchestration.Application.Agents.Legal.Regulations;
using Orchestration.Application.Agents.Shared;

namespace Orchestration.Infrastructure.Agents.Legal;

public sealed class LegalCompliancePlugin
{
    private readonly IRegulatoryKnowledgeSource _regulatoryKnowledgeSource;

    public LegalCompliancePlugin(
        IRegulatoryKnowledgeSource regulatoryKnowledgeSource)
    {
        _regulatoryKnowledgeSource = regulatoryKnowledgeSource;
    }

    [KernelFunction("review_financial_compliance")]
    [Description("Reviews a financial report summary against compliance rules.")]
    public async Task<LegalCompliancePluginResult> ReviewFinancialComplianceAsync(
        [Description("Name of the financial report.")]
        string reportName,
        [Description("Total amount of all transactions in the report.")]
        double totalAmount,
        [Description("Number of transactions in the report.")]
        int transactionCount,
        CancellationToken cancellationToken = default)
    {
        var report = new FinancialReportContext(
            SessionId: Guid.Empty,
            ReportName: reportName,
            TotalAmount: Convert.ToDecimal(totalAmount),
            TransactionCount: transactionCount,
            SubmittedAt: DateTimeOffset.UtcNow
        );

        var review = await _regulatoryKnowledgeSource.ReviewAsync(
            report,
            cancellationToken
        );

        return new LegalCompliancePluginResult(
            HasComplianceRisk: review.HasComplianceRisk,
            RiskLevel: review.RiskLevel,
            Summary: review.Summary,
            Engine: $"Semantic Kernel + {review.SourceEngine}",
            Evidence: review.Findings
                .Select(x => new LegalComplianceEvidenceResult(
                    x.Regulation,
                    x.Section,
                    x.Finding,
                    x.Source
                ))
                .ToList()
        );
    }
}