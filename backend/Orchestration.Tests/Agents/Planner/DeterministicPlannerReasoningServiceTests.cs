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
        result.Limitations.Should().Contain("No LLM reasoning was used.");
        result.Limitations.Should().Contain("This is not legal, financial, or investment advice.");
        result.RecommendedActions.Should().Contain("Approve or reject the session based on human judgment.");
    }
}
