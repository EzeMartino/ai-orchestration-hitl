using Orchestration.Application.Agents.Shared;

namespace Orchestration.Application.Agents.Legal;

public sealed class MockLegalAgent : ILegalAgent
{
    public Task<LegalAgentResult> ReviewAsync(
        FinancialReportContext report,
        CancellationToken cancellationToken)
    {
        var result = new LegalAgentResult(
            HasComplianceRisk: true,
            RiskLevel: "Medium",
            Summary: "The anomaly may require compliance review before operational action is taken.",
            Engine: "Mock Compliance Knowledge Base",
            Evidence:
            [
                new LegalEvidence(
                    Regulation: "Internal AML Policy",
                    Section: "Transaction Monitoring",
                    Finding: "High-risk transaction patterns require human review before account-level action.",
                    Source: "Mock regulatory knowledge base"
                )
            ],
            Warnings:
            [
                "Mock compliance knowledge base. Do not use for real legal decisions."
            ]
        );

        return Task.FromResult(result);
    }
}
