using FluentAssertions;
using Orchestration.Application.Agents.Planner.Reasoning;

namespace Orchestration.Tests.Agents.Planner;

public class DeterministicPlannerReasoningServiceTests
{
    [Fact]
    public async Task GenerateReasoningAsync_Should_return_safe_limitations()
    {
        var service = new DeterministicPlannerReasoningService();

        var result = await service.GenerateReasoningAsync(
            new PlannerReasoningInput(
                SessionId: Guid.NewGuid(),
                ReportName: "financial-report",
                TotalAmount: 125000m,
                TransactionCount: 42,
                SubmittedAt: new DateTimeOffset(
                    2026, 7, 12, 18, 30, 0, TimeSpan.FromHours(-3)),
                DataSummary: "Anomaly detected.",
                DataSeverity: "High",
                DataEngine: "TestDataEngine",
                DataEvidence: ["Z-score above threshold."],
                LegalSummary: "Compliance review required.",
                LegalRiskLevel: "Medium",
                LegalEngine: "TestLegalEngine",
                LegalEvidence: ["Cited CNV evidence."],
                LegalWarnings: ["Human legal review required."]
            ),
            CancellationToken.None
        );

        result.Engine.Should().Be("Deterministic Planner Reasoning");
        result.UsedLlm.Should().BeFalse();
        result.UsedFallback.Should().BeTrue();
        result.Provider.Should().BeNull();
        result.Model.Should().BeNull();
        result.FailureReason.Should().BeNull();
        result.Limitations.Should().Contain("No se usó razonamiento LLM.");
        result.Limitations.Should().Contain("Esto no es asesoramiento legal, financiero ni de inversión.");
        result.Limitations.Should().Contain("Se requiere aprobación humana antes de completar workflows riesgosos.");
        result.RecommendedActions.Should().Contain("Aprobar o rechazar la sesión según el criterio humano.");
    }
}
