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
                "La herramienta no está permitida."
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
                "La herramienta no está permitida."
            )
        );
    }

    [Fact]
    public void Validate_Should_reject_catalog_tool_with_missing_required_argument()
    {
        var validator = new ToolPlanValidator(
            new ToolCallingOptions
            {
                AllowedTools = [PlannerToolCatalog.SearchCnvRegulationName]
            }
        );

        var result = validator.Validate(
            CreatePlan(
                new ProposedToolCall(
                    PlannerToolCatalog.SearchCnvRegulationName,
                    new Dictionary<string, string>(),
                    "Planner requested cited evidence."))
        );

        result.IsValid.Should().BeFalse();
        result.ApprovedCalls.Should().BeEmpty();
        result.RejectedCalls.Should().ContainSingle().Which.Should().Be(
            new RejectedToolCall(
                PlannerToolCatalog.SearchCnvRegulationName,
                "Falta el argumento obligatorio: query."
            )
        );
    }

    [Theory]
    [InlineData("data.compute_financial_ratios")]
    [InlineData("data.compare_periods")]
    [InlineData("data.detect_financial_risk_signals")]
    [InlineData("data.summarize_quantitative_evidence")]
    public void Validate_Should_reject_removed_granular_tools_even_when_configured(
        string toolName)
    {
        var validator = new ToolPlanValidator(
            new ToolCallingOptions
            {
                AllowedTools = [toolName]
            }
        );

        var result = validator.Validate(
            CreatePlan(CreateFinancialCall(toolName))
        );

        result.IsValid.Should().BeFalse();
        result.ApprovedCalls.Should().BeEmpty();
        result.RejectedCalls.Should().ContainSingle().Which.Should().Be(
            new RejectedToolCall(
                toolName,
                "La herramienta no está permitida."
            )
        );
    }

    [Theory]
    [InlineData("workflow.complete", "No se permiten herramientas de transición de workflow.")]
    [InlineData("workflow.transition", "No se permiten herramientas de transición de workflow.")]
    [InlineData("approval.approve_session", "El LLM no puede llamar herramientas de aprobación humana.")]
    [InlineData("approval.reject_session", "El LLM no puede llamar herramientas de aprobación humana.")]
    [InlineData("money.move", "No se permiten herramientas financieras operativas.")]
    [InlineData("account.freeze", "No se permiten herramientas financieras operativas.")]
    [InlineData("transaction.block", "No se permiten herramientas financieras operativas.")]
    [InlineData("legal.determine_violation", "No se permiten herramientas de conclusión legal.")]
    [InlineData("legal.issue_advice", "No se permiten herramientas de conclusión legal.")]
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

    [Theory]
    [InlineData("unknown.tool")]
    [InlineData("workflow.complete")]
    [InlineData("approval.approve")]
    [InlineData("approval.reject")]
    [InlineData("money.transfer")]
    [InlineData("account.block")]
    [InlineData("legal.declare_violation")]
    [InlineData("data.delete_metrics")]
    public void Validate_Should_deny_unsafe_or_unknown_tools_by_default(
        string toolName)
    {
        var validator = new ToolPlanValidator();

        var result = validator.Validate(
            CreatePlan(CreateCall(toolName))
        );

        result.IsValid.Should().BeFalse();
        result.ApprovedCalls.Should().BeEmpty();
        result.RejectedCalls.Should().ContainSingle()
            .Which.ToolName.Should().Be(toolName);
        result.RejectedCalls.Single().Reason.Should().NotBeNullOrWhiteSpace();
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
                "Se excedió la cantidad máxima de llamadas a herramientas."
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
            "La herramienta no está permitida."
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
                "No se permiten herramientas de transición de workflow."
            ),
            new RejectedToolCall(
                "approval.reject_session",
                "El LLM no puede llamar herramientas de aprobación humana."
            ),
            new RejectedToolCall(
                "money.move",
                "No se permiten herramientas financieras operativas."
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
        var arguments = string.Equals(
            toolName,
            PlannerToolCatalog.AnalyzeTransactionsName,
            StringComparison.OrdinalIgnoreCase)
            ? new Dictionary<string, string>
            {
                ["sessionId"] = Guid.NewGuid().ToString(),
                ["reportName"] = "financial-report",
                ["totalAmount"] = "125000.50",
                ["transactionCount"] = "42",
                ["submittedAt"] = DateTimeOffset.UtcNow.ToString("O")
            }
            : string.Equals(
                toolName,
                PlannerToolCatalog.SearchCnvRegulationName,
                StringComparison.OrdinalIgnoreCase)
                ? new Dictionary<string, string>
                {
                    ["query"] = "agentes"
                }
                : new Dictionary<string, string>
                {
                    ["sessionId"] = Guid.NewGuid().ToString()
                };

        return new ProposedToolCall(
            ToolName: toolName!,
            Arguments: arguments,
            Reason: "Planner requested read-only analysis."
        );
    }

    private static ProposedToolCall CreateFinancialCall(
        string toolName)
    {
        return new ProposedToolCall(
            ToolName: toolName,
            Arguments: new Dictionary<string, string>
            {
                ["requestJson"] = "{\"metrics\":[]}"
            },
            Reason: "Planner requested read-only financial analysis."
        );
    }
}
