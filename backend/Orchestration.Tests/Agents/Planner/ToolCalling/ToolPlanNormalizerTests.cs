using FluentAssertions;
using Orchestration.Application.Agents.Planner.ToolCalling;

namespace Orchestration.Tests.Agents.Planner.ToolCalling;

public class ToolPlanNormalizerTests
{
    [Fact]
    public void Normalize_Should_remove_duplicate_same_tool_and_same_arguments()
    {
        var plan = new ToolPlan(
            [
                CreateCall(
                    "data.analyze_transactions",
                    new Dictionary<string, string>
                    {
                        ["sessionId"] = "session-1",
                        ["reportName"] = "report"
                    },
                    "First reason."
                ),
                CreateCall(
                    "data.analyze_transactions",
                    new Dictionary<string, string>
                    {
                        ["reportName"] = "report",
                        ["sessionId"] = "session-1"
                    },
                    "Duplicate reason."
                )
            ]
        );

        var result = new ToolPlanNormalizer().Normalize(plan);

        result.ProposedCalls.Should().ContainSingle();
        result.ProposedCalls[0].Reason.Should().Be("First reason.");
    }

    [Fact]
    public void Normalize_Should_preserve_calls_with_different_arguments()
    {
        var plan = new ToolPlan(
            [
                CreateCall(
                    "legal.search_cnv_regulation",
                    new Dictionary<string, string>
                    {
                        ["query"] = "agentes"
                    }
                ),
                CreateCall(
                    "legal.search_cnv_regulation",
                    new Dictionary<string, string>
                    {
                        ["query"] = "custodia"
                    }
                )
            ]
        );

        var result = new ToolPlanNormalizer().Normalize(plan);

        result.ProposedCalls.Should().HaveCount(2);
    }

    [Fact]
    public void Normalize_Should_trim_tool_names_reasons_and_arguments()
    {
        var plan = new ToolPlan(
            [
                CreateCall(
                    " data.analyze_transactions ",
                    new Dictionary<string, string>
                    {
                        [" sessionId "] = " session-1 ",
                        [" reportName "] = " report "
                    },
                    " Analyze data. "
                )
            ]
        );

        var result = new ToolPlanNormalizer().Normalize(plan);

        result.ProposedCalls.Should().ContainSingle();
        result.ProposedCalls[0].ToolName.Should().Be("data.analyze_transactions");
        result.ProposedCalls[0].Reason.Should().Be("Analyze data.");
        result.ProposedCalls[0].Arguments["sessionId"].Should().Be("session-1");
        result.ProposedCalls[0].Arguments["reportName"].Should().Be("report");
    }

    [Fact]
    public void Normalize_Should_remove_empty_tool_names()
    {
        var plan = new ToolPlan(
            [
                CreateCall(" "),
                CreateCall("legal.search_cnv_regulation")
            ]
        );

        var result = new ToolPlanNormalizer().Normalize(plan);

        result.ProposedCalls.Should().ContainSingle();
        result.ProposedCalls[0].ToolName.Should().Be("legal.search_cnv_regulation");
    }

    [Fact]
    public void Normalize_Should_preserve_first_occurrence()
    {
        var plan = new ToolPlan(
            [
                CreateCall("data.analyze_transactions", reason: "First reason."),
                CreateCall("data.analyze_transactions", reason: "Second reason.")
            ]
        );

        var result = new ToolPlanNormalizer().Normalize(plan);

        result.ProposedCalls.Should().ContainSingle();
        result.ProposedCalls[0].Reason.Should().Be("First reason.");
    }

    [Fact]
    public void Normalize_Should_normalize_null_reason_safely()
    {
        var plan = new ToolPlan(
            [
                CreateCall("data.analyze_transactions", reason: null!)
            ]
        );

        var result = new ToolPlanNormalizer().Normalize(plan);

        result.ProposedCalls.Should().ContainSingle();
        result.ProposedCalls[0].Reason.Should().BeEmpty();
    }

    [Fact]
    public void Normalize_Should_remove_duplicate_argument_free_legal_calls()
    {
        var plan = new ToolPlan(
            [
                CreateCall(
                    "legal.search_cnv_regulation",
                    new Dictionary<string, string>(),
                    "First legal reason."
                ),
                CreateCall(
                    "legal.search_cnv_regulation",
                    new Dictionary<string, string>(),
                    "Duplicate legal reason."
                )
            ]
        );

        var result = new ToolPlanNormalizer().Normalize(plan);

        result.ProposedCalls.Should().ContainSingle();
        result.ProposedCalls[0].Reason.Should().Be("First legal reason.");
        result.ProposedCalls[0].Arguments.Should().BeEmpty();
    }

    [Fact]
    public void Normalize_Should_preserve_proposal_provenance()
    {
        var plan = new ToolPlan(
            ProposedCalls: [CreateCall("legal.search_cnv_regulation")],
            ProposalSource: ToolPlanProposalSource.DeterministicFallback,
            ProposalFallbackReason: ToolPlanProposalFallbackReason.LlmResponseInvalid
        );

        var result = new ToolPlanNormalizer().Normalize(plan);

        result.ProposalSource.Should().Be(ToolPlanProposalSource.DeterministicFallback);
        result.ProposalFallbackReason.Should().Be(ToolPlanProposalFallbackReason.LlmResponseInvalid);
    }

    [Fact]
    public void Normalize_Should_leave_legacy_plan_provenance_null()
    {
        var plan = new ToolPlan([CreateCall("legal.search_cnv_regulation")]);

        var result = new ToolPlanNormalizer().Normalize(plan);

        result.ProposalSource.Should().BeNull();
        result.ProposalFallbackReason.Should().BeNull();
    }

    private static ProposedToolCall CreateCall(
        string toolName,
        IReadOnlyDictionary<string, string>? arguments = null,
        string reason = "Reason.")
    {
        return new ProposedToolCall(
            ToolName: toolName,
            Arguments: arguments ?? new Dictionary<string, string>(),
            Reason: reason
        );
    }
}
