using FluentAssertions;
using Orchestration.Application.Agents.Planner.ToolCalling;

namespace Orchestration.Tests.Agents.Planner.ToolCalling;

public class ToolExecutionPolicyTests
{
    [Fact]
    public void Decide_Should_mark_data_tool_skipped_when_data_analysis_already_completed()
    {
        var policy = new ToolExecutionPolicy();

        var result = policy.Decide(
            [CreateCall("data.analyze_transactions")],
            new ToolExecutionPolicyContext(
                DataAnalysisAlreadyCompleted: true,
                LegalReviewAlreadyCompleted: false,
                DynamicExecutionEnabled: true
            )
        );

        var decision = result.Should().ContainSingle().Subject;

        decision.Status.Should().Be(ToolExecutionStatus.SkippedAlreadySatisfied);
        decision.Reason.Should().Be("DataAgent already executed during the deterministic workflow.");
    }

    [Fact]
    public void Decide_Should_mark_legal_tool_skipped_when_legal_review_already_completed()
    {
        var policy = new ToolExecutionPolicy();

        var result = policy.Decide(
            [CreateCall("legal.search_cnv_regulation")],
            new ToolExecutionPolicyContext(
                DataAnalysisAlreadyCompleted: false,
                LegalReviewAlreadyCompleted: true,
                DynamicExecutionEnabled: true
            )
        );

        var decision = result.Should().ContainSingle().Subject;

        decision.Status.Should().Be(ToolExecutionStatus.SkippedAlreadySatisfied);
        decision.Reason.Should().Be("LegalAgent already executed during the deterministic workflow.");
    }

    [Fact]
    public void Decide_Should_mark_call_skipped_disabled_when_dynamic_execution_is_disabled()
    {
        var policy = new ToolExecutionPolicy();

        var result = policy.Decide(
            [CreateCall("legal.search_cnv_regulation")],
            new ToolExecutionPolicyContext(
                DataAnalysisAlreadyCompleted: false,
                LegalReviewAlreadyCompleted: false,
                DynamicExecutionEnabled: false
            )
        );

        var decision = result.Should().ContainSingle().Subject;

        decision.Status.Should().Be(ToolExecutionStatus.SkippedDisabled);
        decision.Reason.Should().Be("Dynamic tool execution is disabled.");
    }

    [Fact]
    public void Decide_Should_mark_call_executed_when_dynamic_execution_is_enabled_and_not_already_satisfied()
    {
        var policy = new ToolExecutionPolicy();

        var result = policy.Decide(
            [CreateCall("legal.search_cnv_regulation")],
            new ToolExecutionPolicyContext(
                DataAnalysisAlreadyCompleted: false,
                LegalReviewAlreadyCompleted: false,
                DynamicExecutionEnabled: true
            )
        );

        var decision = result.Should().ContainSingle().Subject;

        decision.Status.Should().Be(ToolExecutionStatus.Executed);
        decision.Reason.Should().Be("Tool is approved for controlled execution.");
    }

    [Fact]
    public void Decide_Should_preserve_call_in_decision()
    {
        var call = CreateCall("data.analyze_transactions");
        var policy = new ToolExecutionPolicy();

        var result = policy.Decide(
            [call],
            new ToolExecutionPolicyContext(
                DataAnalysisAlreadyCompleted: true,
                LegalReviewAlreadyCompleted: false,
                DynamicExecutionEnabled: false
            )
        );

        result.Should().ContainSingle().Which.Call.Should().Be(call);
    }

    private static ApprovedToolCall CreateCall(
        string toolName)
    {
        return new ApprovedToolCall(
            ToolName: toolName,
            Arguments: new Dictionary<string, string>(),
            Reason: "Approved read-only tool call."
        );
    }
}
