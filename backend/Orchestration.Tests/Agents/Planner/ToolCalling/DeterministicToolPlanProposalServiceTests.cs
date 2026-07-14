using FluentAssertions;
using Orchestration.Application.Agents.Planner.ToolCalling;
using System.Globalization;

namespace Orchestration.Tests.Agents.Planner.ToolCalling;

public class DeterministicToolPlanProposalServiceTests
{
    private static readonly DateTimeOffset SubmittedAt =
        new(2024, 2, 3, 4, 5, 6, TimeSpan.Zero);

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
        dataCall.Arguments["submittedAt"].Should().Be(
            SubmittedAt.ToString("O", CultureInfo.InvariantCulture));
        dataCall.Reason.Should().Be("Analizar senales cuantitativas del reporte financiero para detectar anomalias.");

        var legalCall = result.ProposedCalls[1];
        legalCall.ToolName.Should().Be("legal.search_cnv_regulation");
        legalCall.Arguments.Should().BeEmpty();
        legalCall.Reason.Should().Be(
            "Autorizar una revisión regulatoria CNV derivada del análisis financiero completado.");
    }

    [Fact]
    public async Task ProposeAsync_Should_only_propose_catalog_tools_allowed_by_configuration()
    {
        var service = new DeterministicToolPlanProposalService(
            new ToolCallingOptions
            {
                Enabled = true,
                AllowedTools = [PlannerToolCatalog.SearchCnvRegulationName]
            }
        );

        var result = await service.ProposeAsync(
            CreateInput(),
            CancellationToken.None
        );

        result.ProposedCalls.Should().ContainSingle()
            .Which.ToolName.Should().Be(PlannerToolCatalog.SearchCnvRegulationName);
    }

    private static ToolPlanProposalInput CreateInput(
        Guid? sessionId = null)
    {
        return new ToolPlanProposalInput(
            SessionId: sessionId ?? Guid.NewGuid(),
            ReportName: "financial-report",
            TotalAmount: 125000.50m,
            TransactionCount: 42,
            SubmittedAt: SubmittedAt,
            PlannerSummary: "Planner reviewed evidence.",
            RiskFactors: ["High data severity."],
            Limitations: ["Human approval required."]
        );
    }
}
