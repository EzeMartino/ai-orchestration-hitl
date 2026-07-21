using FluentAssertions;
using Orchestration.Application.Agents.Legal.Regulations;
using Orchestration.Application.Agents.Shared;
using Orchestration.Infrastructure.Agents.Legal;

namespace Orchestration.Tests.Agents.Legal;

public class LegalCompliancePluginTests
{
    [Fact]
    public async Task ReviewFinancialComplianceAsync_Should_propagate_evidence_assessment_unchanged()
    {
        var evidenceAssessment = new RegulatoryEvidenceAssessment(
            EvidenceFound: true,
            Relevance: "Strong",
            Applicability: "Applicable",
            EvidenceQuality: "Weak",
            Severity: "Warning",
            RequiresHumanReview: true,
            Reasons: ["La evidencia requiere una decisión humana."]
        );
        var source = new StubRegulatoryKnowledgeSource(
            new RegulatoryReviewResult(
                HasComplianceRisk: false,
                RiskLevel: "NotEstablished",
                Summary: "La evidencia no permite establecer el riesgo.",
                SourceEngine: "Test regulatory source",
                Findings: [],
                Warnings: [],
                EvidenceAssessment: evidenceAssessment
            )
        );
        var plugin = new LegalCompliancePlugin(source);

        var result = await plugin.ReviewFinancialComplianceAsync(
            reportName: "legal-plugin-test-report",
            totalAmount: 100d,
            transactionCount: 1,
            cancellationToken: CancellationToken.None
        );

        result.EvidenceAssessment.Should().BeEquivalentTo(evidenceAssessment);
    }

    private sealed class StubRegulatoryKnowledgeSource(RegulatoryReviewResult result)
        : IRegulatoryKnowledgeSource
    {
        public Task<RegulatoryReviewResult> ReviewAsync(
            FinancialReportContext report,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(result);
        }
    }
}
