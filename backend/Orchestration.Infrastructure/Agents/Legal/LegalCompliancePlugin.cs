using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace Orchestration.Infrastructure.Agents.Legal;

public sealed class LegalCompliancePlugin
{
    [KernelFunction("review_financial_compliance")]
    [Description("Reviews a financial report summary against compliance rules.")]
    public Task<LegalCompliancePluginResult> ReviewFinancialComplianceAsync(
        [Description("Name of the financial report.")]
        string reportName,
        [Description("Total amount of all transactions in the report.")]
        double totalAmount,
        [Description("Number of transactions in the report.")]
        int transactionCount,
        CancellationToken cancellationToken = default)
    {
        var hasComplianceRisk = totalAmount >= 100000 || transactionCount >= 40;

        var riskLevel = hasComplianceRisk
            ? "Medium"
            : "Low";

        var summary = hasComplianceRisk
            ? "The anomaly may require compliance review before operational action is taken."
            : "No significant compliance risk detected for the submitted financial report.";

        IReadOnlyList<LegalComplianceEvidenceResult> evidence = hasComplianceRisk
            ?
            [
                new LegalComplianceEvidenceResult(
                    Regulation: "Internal AML Policy",
                    Section: "Transaction Monitoring",
                    Finding: "High-risk transaction patterns require human review before account-level action.",
                    Source: "Semantic Kernel mock compliance knowledge base"
                ),
                new LegalComplianceEvidenceResult(
                    Regulation: "Operational Risk Control",
                    Section: "Human Approval Safeguards",
                    Finding: "Automated systems must pause before irreversible operational actions when anomaly severity is elevated.",
                    Source: "Semantic Kernel mock compliance knowledge base"
                )
            ]
            :
            [
                new LegalComplianceEvidenceResult(
                    Regulation: "Internal AML Policy",
                    Section: "Transaction Monitoring",
                    Finding: "No escalation threshold was reached.",
                    Source: "Semantic Kernel mock compliance knowledge base"
                )
            ];

        var result = new LegalCompliancePluginResult(
            HasComplianceRisk: hasComplianceRisk,
            RiskLevel: riskLevel,
            Summary: summary,
            Engine: "Semantic Kernel + Mock Compliance Knowledge Base",
            Evidence: evidence
        );

        return Task.FromResult(result);
    }
}