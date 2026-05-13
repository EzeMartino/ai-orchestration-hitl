using Orchestration.Application.Agents.Legal.Regulations;
using Orchestration.Application.Agents.Shared;

namespace Orchestration.Infrastructure.Agents.Legal.Regulations;

public sealed class MockRegulatoryKnowledgeSource : IRegulatoryKnowledgeSource
{
    public Task<RegulatoryReviewResult> ReviewAsync(
        FinancialReportContext report,
        CancellationToken cancellationToken)
    {
        var hasComplianceRisk =
            report.TotalAmount >= 100000m ||
            report.TransactionCount >= 40;

        var riskLevel = hasComplianceRisk
            ? "Medium"
            : "Low";

        var summary = hasComplianceRisk
            ? "The anomaly may require compliance review before operational action is taken."
            : "No significant compliance risk detected for the submitted financial report.";

        IReadOnlyList<RegulatoryFinding> findings = hasComplianceRisk
            ?
            [
                new RegulatoryFinding(
                    Regulation: "Internal AML Policy",
                    Section: "Transaction Monitoring",
                    Finding: "High-risk transaction patterns require human review before account-level action.",
                    Source: "Mock regulatory knowledge source"
                ),
                new RegulatoryFinding(
                    Regulation: "Operational Risk Control",
                    Section: "Human Approval Safeguards",
                    Finding: "Automated systems must pause before irreversible operational actions when anomaly severity is elevated.",
                    Source: "Mock regulatory knowledge source"
                )
            ]
            :
            [
                new RegulatoryFinding(
                    Regulation: "Internal AML Policy",
                    Section: "Transaction Monitoring",
                    Finding: "No escalation threshold was reached.",
                    Source: "Mock regulatory knowledge source"
                )
            ];

        var result = new RegulatoryReviewResult(
            HasComplianceRisk: hasComplianceRisk,
            RiskLevel: riskLevel,
            Summary: summary,
            SourceEngine: "Mock Regulatory Knowledge Source",
            Findings: findings,
            Warnings:
            [
                "Mock regulatory knowledge source. Do not use for real legal decisions."
            ]
        );

        return Task.FromResult(result);
    }
}
