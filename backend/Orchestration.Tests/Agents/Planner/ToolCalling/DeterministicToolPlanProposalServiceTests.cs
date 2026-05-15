using FluentAssertions;
using Orchestration.Application.Agents.Planner.ToolCalling;

namespace Orchestration.Tests.Agents.Planner.ToolCalling;

public class DeterministicToolPlanProposalServiceTests
{
    [Fact]
    public async Task ProposeAsync_Should_return_empty_plan_when_tool_calling_is_disabled()
    {
        var service = new DeterministicToolPlanProposalService(
            new ToolCallingOptions
            {
                Enabled = false
            }
        );

        var result = await service.ProposeAsync(
            CreateInput(),
            CancellationToken.None
        );

        result.ProposedCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task ProposeAsync_Should_return_safe_proposed_calls_when_tool_calling_is_enabled()
    {
        var sessionId = Guid.NewGuid();
        var service = new DeterministicToolPlanProposalService(
            new ToolCallingOptions
            {
                Enabled = true
            }
        );

        var result = await service.ProposeAsync(
            CreateInput(sessionId),
            CancellationToken.None
        );

        result.ProposedCalls.Should().HaveCount(2);

        var dataCall = result.ProposedCalls[0];
        dataCall.ToolName.Should().Be("data.analyze_transactions");
        dataCall.Arguments["sessionId"].Should().Be(sessionId.ToString());
        dataCall.Arguments["reportName"].Should().Be("financial-report");
        dataCall.Arguments["totalAmount"].Should().Be("125000.50");
        dataCall.Arguments["transactionCount"].Should().Be("42");
        dataCall.Arguments["submittedAt"].Should().NotBeNullOrWhiteSpace();
        dataCall.Reason.Should().Be("Analizar senales cuantitativas del reporte financiero para detectar anomalias.");

        var legalCall = result.ProposedCalls[1];
        legalCall.ToolName.Should().Be("legal.search_cnv_regulation");
        legalCall.Arguments["query"].Should().Be("agentes");
        legalCall.Arguments["area"].Should().Be("Agentes");
        legalCall.Arguments["limit"].Should().Be("5");
        legalCall.Arguments["requiresReview"].Should().Be("true");
        legalCall.Reason.Should().Be("Recuperar evidencia regulatoria CNV citada relacionada con agentes regulados.");
    }

    private static ToolPlanProposalInput CreateInput(
        Guid? sessionId = null)
    {
        return new ToolPlanProposalInput(
            SessionId: sessionId ?? Guid.NewGuid(),
            ReportName: "financial-report",
            TotalAmount: 125000.50m,
            TransactionCount: 42,
            PlannerSummary: "Planner reviewed evidence.",
            RiskFactors: ["High data severity."],
            Limitations: ["Human approval required."]
        );
    }
}
