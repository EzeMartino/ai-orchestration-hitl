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
            ? "La anomalía puede requerir revisión de cumplimiento antes de tomar una acción operativa."
            : "No se detectó un riesgo de cumplimiento significativo para el informe financiero enviado.";

        IReadOnlyList<RegulatoryFinding> findings = hasComplianceRisk
            ?
            [
                new RegulatoryFinding(
                    Regulation: "Internal AML Policy",
                    Section: "Transaction Monitoring",
                    Finding: "Los patrones transaccionales de alto riesgo requieren revisión humana antes de una acción a nivel de cuenta.",
                    Source: "Fuente regulatoria simulada"
                ),
                new RegulatoryFinding(
                    Regulation: "Operational Risk Control",
                    Section: "Human Approval Safeguards",
                    Finding: "Los sistemas automatizados deben detenerse antes de acciones operativas irreversibles cuando la severidad de la anomalía es elevada.",
                    Source: "Fuente regulatoria simulada"
                )
            ]
            :
            [
                new RegulatoryFinding(
                    Regulation: "Internal AML Policy",
                    Section: "Transaction Monitoring",
                    Finding: "No se alcanzó ningún umbral de escalamiento.",
                    Source: "Fuente regulatoria simulada"
                )
            ];

        var result = new RegulatoryReviewResult(
            HasComplianceRisk: hasComplianceRisk,
            RiskLevel: riskLevel,
            Summary: summary,
            SourceEngine: "Fuente regulatoria simulada",
            Findings: findings,
            Warnings:
            [
                "Fuente regulatoria simulada. No usar para decisiones legales reales."
            ]
        );

        return Task.FromResult(result);
    }
}
