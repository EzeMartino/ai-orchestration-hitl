using Orchestration.Application.Agents.Legal.Regulations;
using Orchestration.Application.Agents.Shared;

namespace Orchestration.Infrastructure.Agents.Legal.Regulations;

/// <summary>
/// Fails closed when the CNV MCP service is not configured for a non-development environment.
/// </summary>
public sealed class UnavailableRegulatoryKnowledgeSource : IRegulatoryKnowledgeSource
{
    public Task<RegulatoryReviewResult> ReviewAsync(
        FinancialReportContext report,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new RegulatoryReviewResult(
            HasComplianceRisk: false,
            RiskLevel: "NotEstablished",
            Summary: "La recuperación documental CNV no está disponible. No se estableció riesgo de cumplimiento y se requiere revisión legal humana.",
            SourceEngine: "CNV MCP unavailable",
            Findings: [],
            Warnings: ["La fuente documental CNV no está disponible; se requiere revisión legal humana."],
            RequiresHumanReview: true,
            EvidenceAssessment: new RegulatoryEvidenceAssessment(
                EvidenceFound: false,
                Relevance: "NotEstablished",
                Applicability: "NotEstablished",
                EvidenceQuality: "Unavailable",
                Severity: "NotEstablished",
                RequiresHumanReview: true,
                Reasons: ["CNV MCP unavailable."])));
    }
}
