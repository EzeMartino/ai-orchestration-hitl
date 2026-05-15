using FluentAssertions;
using Orchestration.Infrastructure.Agents.Planner.ToolCalling;

namespace Orchestration.Tests.Agents.Planner.ToolCalling;

public class SemanticKernelToolPlanResponseParserTests
{
    [Fact]
    public void ParseOrFallback_Should_parse_valid_tool_plan_json()
    {
        const string content = """
{
  "proposedCalls": [
    {
      "toolName": "data.analyze_transactions",
      "arguments": {
        "sessionId": "test-session",
        "reportName": "financial-report"
      },
      "reason": "Analizar senales del reporte financiero."
    }
  ]
}
""";

        var parser = new SemanticKernelToolPlanResponseParser();

        var result = parser.ParseOrFallback(content);

        result.ProposedCalls.Should().ContainSingle();
        result.ProposedCalls[0].ToolName.Should().Be("data.analyze_transactions");
        result.ProposedCalls[0].Arguments["sessionId"].Should().Be("test-session");
        result.ProposedCalls[0].Arguments["reportName"].Should().Be("financial-report");
        result.ProposedCalls[0].Reason.Should().Be("Analizar senales del reporte financiero.");
    }

    [Fact]
    public void ParseOrFallback_Should_return_empty_plan_for_invalid_json()
    {
        var parser = new SemanticKernelToolPlanResponseParser();

        var result = parser.ParseOrFallback("not-json");

        result.ProposedCalls.Should().BeEmpty();
    }

    [Fact]
    public void ParseOrFallback_Should_handle_missing_proposed_calls()
    {
        var parser = new SemanticKernelToolPlanResponseParser();

        var result = parser.ParseOrFallback("{}");

        result.ProposedCalls.Should().BeEmpty();
    }

    [Fact]
    public void ParseOrFallback_Should_handle_null_arguments()
    {
        const string content = """
{
  "proposedCalls": [
    {
      "toolName": "legal.search_cnv_regulation",
      "arguments": null,
      "reason": null
    }
  ]
}
""";

        var parser = new SemanticKernelToolPlanResponseParser();

        var result = parser.ParseOrFallback(content);

        result.ProposedCalls.Should().ContainSingle();
        result.ProposedCalls[0].ToolName.Should().Be("legal.search_cnv_regulation");
        result.ProposedCalls[0].Arguments.Should().BeEmpty();
        result.ProposedCalls[0].Reason.Should().Be("El LLM propuso esta herramienta de solo lectura.");
    }

    [Fact]
    public void ParseOrFallback_Should_skip_empty_tool_name()
    {
        const string content = """
{
  "proposedCalls": [
    {
      "toolName": "",
      "arguments": {
        "query": "agentes"
      },
      "reason": "Missing tool name."
    },
    {
      "toolName": "legal.search_cnv_regulation",
      "arguments": {
        "query": "agentes"
      },
      "reason": "Retrieve CNV evidence."
    }
  ]
}
""";

        var parser = new SemanticKernelToolPlanResponseParser();

        var result = parser.ParseOrFallback(content);

        result.ProposedCalls.Should().ContainSingle();
        result.ProposedCalls[0].ToolName.Should().Be("legal.search_cnv_regulation");
    }
}
