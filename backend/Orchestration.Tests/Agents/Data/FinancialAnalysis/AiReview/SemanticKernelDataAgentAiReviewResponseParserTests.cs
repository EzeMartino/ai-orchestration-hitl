using FluentAssertions;
using Orchestration.Application.Agents.Data.FinancialAnalysis.AiReview;

namespace Orchestration.Tests.Agents.Data.FinancialAnalysis.AiReview;

public sealed class SemanticKernelDataAgentAiReviewResponseParserTests
{
    [Fact]
    public void Parse_Should_parse_pure_json()
    {
        var parser = new SemanticKernelDataAgentAiReviewResponseParser();

        var result = parser.Parse(CreateValidJson());

        result.Succeeded.Should().BeTrue();
        result.Response.Should().NotBeNull();
        result.Response!.Summary.Should().Be("Structured evidence requires review.");
        result.Response.KeyFindings.Should().ContainSingle();
        result.Response.DataQualityNotes.Should().ContainSingle();
        result.Response.Limitations.Should().Contain("Advisory only.");
    }

    [Fact]
    public void Parse_Should_parse_fenced_json()
    {
        var parser = new SemanticKernelDataAgentAiReviewResponseParser();
        var content = $"""
```json
{CreateValidJson()}
```
""";

        var result = parser.Parse(content);

        result.Succeeded.Should().BeTrue();
        result.Response!.Summary.Should().Be("Structured evidence requires review.");
    }

    [Fact]
    public void Parse_Should_parse_embedded_json()
    {
        var parser = new SemanticKernelDataAgentAiReviewResponseParser();
        var content = $"""
Here is the JSON:
{CreateValidJson()}
Done.
""";

        var result = parser.Parse(content);

        result.Succeeded.Should().BeTrue();
        result.Response!.RiskInterpretation.Should().Be("Human review should focus on liquidity.");
    }

    [Fact]
    public void Parse_Should_reject_invalid_json()
    {
        var parser = new SemanticKernelDataAgentAiReviewResponseParser();

        var result = parser.Parse("not-json");

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Be(SemanticKernelDataAgentAiReviewResponseParser.InvalidJson);
    }

    [Fact]
    public void Parse_Should_reject_missing_summary()
    {
        var parser = new SemanticKernelDataAgentAiReviewResponseParser();

        var result = parser.Parse("""
{
  "keyFindings": [],
  "riskInterpretation": "Review evidence.",
  "dataQualityNotes": [],
  "limitations": []
}
""");

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Be(SemanticKernelDataAgentAiReviewResponseParser.SchemaValidationFailed);
    }

    [Fact]
    public void Parse_Should_reject_missing_risk_interpretation()
    {
        var parser = new SemanticKernelDataAgentAiReviewResponseParser();

        var result = parser.Parse("""
{
  "summary": "Review evidence.",
  "keyFindings": [],
  "dataQualityNotes": [],
  "limitations": []
}
""");

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Be(SemanticKernelDataAgentAiReviewResponseParser.SchemaValidationFailed);
    }

    [Fact]
    public void Parse_Should_handle_null_lists_as_empty_lists()
    {
        var parser = new SemanticKernelDataAgentAiReviewResponseParser();

        var result = parser.Parse("""
{
  "summary": "Review evidence.",
  "keyFindings": null,
  "riskInterpretation": "Review deterministic evidence.",
  "dataQualityNotes": null,
  "limitations": null
}
""");

        result.Succeeded.Should().BeTrue();
        result.Response!.KeyFindings.Should().BeEmpty();
        result.Response.DataQualityNotes.Should().BeEmpty();
        result.Response.Limitations.Should().BeEmpty();
    }

    private static string CreateValidJson()
    {
        return """
{
  "summary": "Structured evidence requires review.",
  "keyFindings": [
    {
      "title": "Liquidity pressure",
      "description": "Current ratio is below threshold.",
      "severity": "High",
      "relatedMetrics": ["current_ratio"]
    }
  ],
  "riskInterpretation": "Human review should focus on liquidity.",
  "dataQualityNotes": [
    {
      "message": "Metrics were manually attached.",
      "severity": "Info",
      "relatedFields": ["metricsInputSource"]
    }
  ],
  "limitations": ["Advisory only."]
}
""";
    }
}
