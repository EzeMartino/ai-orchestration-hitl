using FluentAssertions;
using Orchestration.Application.Agents.Planner.ToolCalling;

namespace Orchestration.Tests.Agents.Planner.ToolCalling;

public class ToolPlanValidatorTests
{
    [Fact]
    public void ToolCallingOptions_Should_default_to_shadow_execution_mode()
    {
        var options = new ToolCallingOptions();

        options.ExecutionMode.Should().Be(ToolCallingExecutionMode.Shadow);
    }

    [Theory]
    [InlineData("data.analyze_transactions")]
    [InlineData("legal.search_cnv_regulation")]
    public void Validate_Should_approve_allowlisted_tools(
        string toolName)
    {
        var validator = new ToolPlanValidator();

        var result = validator.Validate(
            CreatePlan(CreateCall(toolName))
        );

        result.IsValid.Should().BeTrue();
        result.ApprovedCalls.Should().ContainSingle()
            .Which.ToolName.Should().Be(toolName);
        result.RejectedCalls.Should().BeEmpty();
    }

    [Fact]
    public void Validate_Should_reject_unknown_tools()
    {
        var validator = new ToolPlanValidator();

        var result = validator.Validate(
            CreatePlan(CreateCall("system.execute_command"))
        );

        result.IsValid.Should().BeFalse();
        result.ApprovedCalls.Should().BeEmpty();
        result.RejectedCalls.Should().ContainSingle().Which.Should().Be(
            new RejectedToolCall(
                "system.execute_command",
                "Tool is not allowlisted."
            )
        );
    }

    [Fact]
    public void Validate_Should_reject_tools_not_in_configured_allowlist()
    {
        var validator = new ToolPlanValidator(
            new ToolCallingOptions
            {
                AllowedTools = ["data.analyze_transactions"]
            }
        );

        var result = validator.Validate(
            CreatePlan(CreateCall("legal.search_cnv_regulation"))
        );

        result.IsValid.Should().BeFalse();
        result.ApprovedCalls.Should().BeEmpty();
        result.RejectedCalls.Should().ContainSingle().Which.Should().Be(
            new RejectedToolCall(
                "legal.search_cnv_regulation",
                "Tool is not allowlisted."
            )
        );
    }

    [Theory]
    [InlineData("workflow.complete", "Workflow transition tools are not allowed.")]
    [InlineData("workflow.transition", "Workflow transition tools are not allowed.")]
    [InlineData("approval.approve_session", "Human approval tools cannot be called by LLM.")]
    [InlineData("approval.reject_session", "Human approval tools cannot be called by LLM.")]
    [InlineData("money.move", "Operational financial tools are not allowed.")]
    [InlineData("account.freeze", "Operational financial tools are not allowed.")]
    [InlineData("transaction.block", "Operational financial tools are not allowed.")]
    [InlineData("legal.determine_violation", "Legal conclusion tools are not allowed.")]
    [InlineData("legal.issue_advice", "Legal conclusion tools are not allowed.")]
    public void Validate_Should_reject_prohibited_tools(
        string toolName,
        string expectedReason)
    {
        var validator = new ToolPlanValidator();

        var result = validator.Validate(
            CreatePlan(CreateCall(toolName))
        );

        result.IsValid.Should().BeFalse();
        result.ApprovedCalls.Should().BeEmpty();
        result.RejectedCalls.Should().ContainSingle().Which.Should().Be(
            new RejectedToolCall(toolName, expectedReason)
        );
    }

    [Fact]
    public void Validate_Should_cap_max_tool_calls()
    {
        var validator = new ToolPlanValidator(
            new ToolCallingOptions
            {
                MaxToolCalls = 1
            }
        );

        var result = validator.Validate(
            CreatePlan(
                CreateCall("data.analyze_transactions"),
                CreateCall("legal.search_cnv_regulation")
            )
        );

        result.IsValid.Should().BeFalse();
        result.ApprovedCalls.Should().ContainSingle()
            .Which.ToolName.Should().Be("data.analyze_transactions");
        result.RejectedCalls.Should().ContainSingle().Which.Should().Be(
            new RejectedToolCall(
                "legal.search_cnv_regulation",
                "Maximum tool call count exceeded."
            )
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_Should_reject_empty_tool_name(
        string? toolName)
    {
        var validator = new ToolPlanValidator();

        var result = validator.Validate(
            CreatePlan(CreateCall(toolName))
        );

        result.IsValid.Should().BeFalse();
        result.ApprovedCalls.Should().BeEmpty();
        result.RejectedCalls.Should().ContainSingle().Which.Reason.Should().Be(
            "Tool is not allowlisted."
        );
    }

    [Fact]
    public void Validate_Should_preserve_rejected_reasons()
    {
        var validator = new ToolPlanValidator();

        var result = validator.Validate(
            CreatePlan(
                CreateCall("workflow.complete"),
                CreateCall("approval.reject_session"),
                CreateCall("money.move")
            )
        );

        result.IsValid.Should().BeFalse();
        result.RejectedCalls.Should().Equal(
            new RejectedToolCall(
                "workflow.complete",
                "Workflow transition tools are not allowed."
            ),
            new RejectedToolCall(
                "approval.reject_session",
                "Human approval tools cannot be called by LLM."
            ),
            new RejectedToolCall(
                "money.move",
                "Operational financial tools are not allowed."
            )
        );
    }

    private static ToolPlan CreatePlan(
        params ProposedToolCall[] calls)
    {
        return new ToolPlan(calls);
    }

    private static ProposedToolCall CreateCall(
        string? toolName)
    {
        return new ProposedToolCall(
            ToolName: toolName!,
            Arguments: new Dictionary<string, string>
            {
                ["sessionId"] = Guid.NewGuid().ToString()
            },
            Reason: "Planner requested read-only analysis."
        );
    }
}
