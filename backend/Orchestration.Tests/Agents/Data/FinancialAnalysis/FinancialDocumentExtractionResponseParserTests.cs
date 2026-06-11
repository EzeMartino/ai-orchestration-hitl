using System.Text.Json.Nodes;
using FluentAssertions;
using Orchestration.Application.Agents.Data.FinancialAnalysis.Extraction;

namespace Orchestration.Tests.Agents.Data.FinancialAnalysis;

public sealed class FinancialDocumentExtractionResponseParserTests
{
    private const int MaxEvidenceExcerptCharacters = 50;

    private readonly FinancialDocumentExtractionResponseParser _parser = new();

    [Fact]
    public void Parse_Should_map_valid_json_to_candidates()
    {
        var result = Parse(CreateValidJson());

        result.Succeeded.Should().BeTrue();
        result.FailureReason.Should().BeNull();
        result.Result.Should().NotBeNull();
        result.Result!.Company.Should().BeEquivalentTo(
            new
            {
                FieldName = "company",
                Value = "Example Energy",
                SourceKind = FinancialMetricCandidateSourceKinds.Reported,
                Confidence = 0.98m,
                SourcePage = 1,
                Evidence = "Example Energy Annual Report",
                ExtractionStrategy = "semantic_markdown_agent",
                ReviewState = FinancialMetricCandidateReviewStates.Explicit,
                InferenceExplanation = (string?)null
            },
            options => options.ExcludingMissingMembers());
        result.Result.Currency.FieldName.Should().Be("currency");
        result.Result.Unit.FieldName.Should().Be("unit");
        result.Result.Metrics.Should().ContainSingle();

        var metric = result.Result.Metrics.Single();
        metric.Id.Should().NotBeEmpty();
        metric.Name.Should().Be("revenue");
        metric.Period.Should().Be("2024A");
        metric.Value.Should().Be(100m);
        metric.Currency.Should().Be("USD");
        metric.Unit.Should().Be("USD_million");
        metric.SourceKind.Should().Be(FinancialMetricCandidateSourceKinds.Reported);
        metric.Confidence.Should().Be(0.97m);
        metric.SourcePage.Should().Be(12);
        metric.Evidence.Should().Be("| Revenue | 100 |");
        metric.ExtractionStrategy.Should().Be("semantic_markdown_agent");
        metric.ReviewState.Should().Be(FinancialMetricCandidateReviewStates.Explicit);
        metric.InferenceExplanation.Should().BeNull();
    }

    [Theory]
    [InlineData("Ventas netas", "revenue")]
    [InlineData("Ganancia bruta", "gross_profit")]
    [InlineData("Operating Income", "operating_income")]
    [InlineData("quick_ratio", "quick_ratio")]
    [InlineData("capex_to_revenue", "capex_to_revenue")]
    public void Parse_Should_normalize_supported_aliases_and_workflow_metrics(
        string suppliedName,
        string expectedName)
    {
        var json = Mutate(root =>
        {
            Metric(root)["name"] = suppliedName;
            Metric(root)["period"] = "FY2024e";
        });

        var result = Parse(json);

        result.Succeeded.Should().BeTrue();
        result.Result!.Metrics.Single().Name.Should().Be(expectedName);
        result.Result.Metrics.Single().Period.Should().Be("2024E");
    }

    [Fact]
    public void Parse_Should_map_inferred_candidates_to_inferred_review_state()
    {
        var json = Mutate(root =>
        {
            Company(root)["sourceKind"] = "inferred";
            Company(root)["evidence"] = "";
            Company(root)["inferenceExplanation"] = "The report title identifies the issuer.";
            Metric(root)["sourceKind"] = "inferred";
            Metric(root)["evidence"] = "";
            Metric(root)["inferenceExplanation"] = "The column heading indicates the fiscal year.";
        });

        var result = Parse(json);

        result.Succeeded.Should().BeTrue();
        result.Result!.Company.ReviewState.Should()
            .Be(FinancialMetricCandidateReviewStates.Inferred);
        result.Result.Metrics.Single().ReviewState.Should()
            .Be(FinancialMetricCandidateReviewStates.Inferred);
    }

    [Fact]
    public void Parse_Should_reject_unknown_metric_names()
    {
        var result = Parse(Mutate(root => Metric(root)["name"] = "invented_metric"));

        AssertSchemaFailure(result);
    }

    [Theory]
    [InlineData("company", -0.01)]
    [InlineData("company", 1.01)]
    [InlineData("metric", -0.01)]
    [InlineData("metric", 1.01)]
    public void Parse_Should_reject_confidence_outside_zero_to_one(
        string target,
        double confidence)
    {
        var json = Mutate(root =>
        {
            Candidate(root, target)["confidence"] = confidence;
        });

        AssertSchemaFailure(Parse(json));
    }

    [Theory]
    [InlineData("company")]
    [InlineData("metric")]
    public void Parse_Should_reject_evidence_longer_than_configured_limit(string target)
    {
        var json = Mutate(root =>
        {
            Candidate(root, target)["evidence"] =
                new string('x', MaxEvidenceExcerptCharacters + 1);
        });

        AssertSchemaFailure(Parse(json));
    }

    [Fact]
    public void Parse_Should_clamp_evidence_limit_to_one()
    {
        var json = Mutate(root =>
        {
            Company(root)["evidence"] = "x";
            Currency(root)["evidence"] = "x";
            Unit(root)["evidence"] = "x";
            Metric(root)["evidence"] = "x";
        });

        _parser.Parse(json, 0).Succeeded.Should().BeTrue();

        var tooLong = Mutate(root =>
        {
            Company(root)["evidence"] = "x";
            Currency(root)["evidence"] = "x";
            Unit(root)["evidence"] = "x";
            Metric(root)["evidence"] = "xx";
        });

        AssertSchemaFailure(_parser.Parse(tooLong, 0));
    }

    [Theory]
    [InlineData("company", "")]
    [InlineData("company", "   ")]
    [InlineData("metric", "")]
    [InlineData("metric", "   ")]
    public void Parse_Should_reject_reported_candidate_without_nonblank_evidence(
        string target,
        string evidence)
    {
        var json = Mutate(root =>
        {
            Candidate(root, target)["evidence"] = evidence;
        });

        AssertSchemaFailure(Parse(json));
    }

    [Theory]
    [InlineData("company", null)]
    [InlineData("company", " ")]
    [InlineData("metric", null)]
    [InlineData("metric", " ")]
    public void Parse_Should_reject_inferred_candidate_without_nonblank_explanation(
        string target,
        string? explanation)
    {
        var json = Mutate(root =>
        {
            var candidate = Candidate(root, target);
            candidate["sourceKind"] = "inferred";
            candidate["inferenceExplanation"] = explanation;
        });

        AssertSchemaFailure(Parse(json));
    }

    [Theory]
    [InlineData("2024Q1")]
    [InlineData("Q1 2024")]
    [InlineData("2024F")]
    [InlineData("24A")]
    [InlineData("annual")]
    public void Parse_Should_reject_invalid_periods(string period)
    {
        var result = Parse(Mutate(root => Metric(root)["period"] = period));

        AssertSchemaFailure(result);
    }

    [Theory]
    [InlineData("root")]
    [InlineData("document")]
    [InlineData("company")]
    [InlineData("metric")]
    public void Parse_Should_reject_extra_properties(string target)
    {
        var json = Mutate(root =>
        {
            Candidate(root, target)["unexpected"] = true;
        });

        AssertSchemaFailure(Parse(json));
    }

    [Theory]
    [InlineData("fence")]
    [InlineData("prefix")]
    [InlineData("suffix")]
    public void Parse_Should_reject_markdown_or_non_json_wrappers(string wrapper)
    {
        var valid = CreateValidJson();
        var content = wrapper switch
        {
            "fence" => $"```json\n{valid}\n```",
            "prefix" => $"Here is the result:\n{valid}",
            "suffix" => $"{valid}\nDone.",
            _ => throw new ArgumentOutOfRangeException(nameof(wrapper))
        };

        AssertSchemaFailure(Parse(content));
    }

    [Fact]
    public void Parse_Should_reject_duplicate_root_properties()
    {
        var json = CreateValidJson().Replace(
            "\"document\": {",
            "\"document\": null,\n  \"document\": {",
            StringComparison.Ordinal);

        AssertSchemaFailure(Parse(json));
    }

    [Fact]
    public void Parse_Should_reject_duplicate_document_properties()
    {
        var json = CreateValidJson().Replace(
            "\"company\": {",
            "\"company\": null,\n    \"company\": {",
            StringComparison.Ordinal);

        AssertSchemaFailure(Parse(json));
    }

    [Fact]
    public void Parse_Should_reject_duplicate_metadata_properties()
    {
        var json = CreateValidJson().Replace(
            "\"value\": \"Example Energy\",",
            "\"value\": \"Other Energy\",\n      \"value\": \"Example Energy\",",
            StringComparison.Ordinal);

        AssertSchemaFailure(Parse(json));
    }

    [Fact]
    public void Parse_Should_reject_duplicate_metric_properties()
    {
        var json = CreateValidJson().Replace(
            "\"name\": \"revenue\",",
            "\"name\": \"gross_profit\",\n      \"name\": \"revenue\",",
            StringComparison.Ordinal);

        AssertSchemaFailure(Parse(json));
    }

    [Theory]
    [InlineData("document")]
    [InlineData("metrics")]
    public void Parse_Should_reject_missing_required_root_fields(string propertyName)
    {
        var json = Mutate(root => root.Remove(propertyName));

        AssertSchemaFailure(Parse(json));
    }

    [Theory]
    [InlineData("company")]
    [InlineData("currency")]
    [InlineData("unit")]
    public void Parse_Should_reject_missing_required_document_fields(string propertyName)
    {
        var json = Mutate(root => Document(root).Remove(propertyName));

        AssertSchemaFailure(Parse(json));
    }

    [Theory]
    [InlineData("value")]
    [InlineData("sourceKind")]
    [InlineData("confidence")]
    [InlineData("sourcePage")]
    [InlineData("evidence")]
    public void Parse_Should_reject_missing_required_metadata_fields(string propertyName)
    {
        var json = Mutate(root => Company(root).Remove(propertyName));

        AssertSchemaFailure(Parse(json));
    }

    [Theory]
    [InlineData("name")]
    [InlineData("period")]
    [InlineData("value")]
    [InlineData("currency")]
    [InlineData("unit")]
    [InlineData("sourceKind")]
    [InlineData("confidence")]
    [InlineData("sourcePage")]
    [InlineData("evidence")]
    [InlineData("inferenceExplanation")]
    public void Parse_Should_reject_missing_required_metric_fields(string propertyName)
    {
        var json = Mutate(root => Metric(root).Remove(propertyName));

        AssertSchemaFailure(Parse(json));
    }

    [Theory]
    [InlineData("root")]
    [InlineData("document")]
    [InlineData("metrics")]
    [InlineData("metadata")]
    [InlineData("metric")]
    public void Parse_Should_reject_wrong_container_json_kinds(string target)
    {
        var json = target switch
        {
            "root" => "[]",
            _ => Mutate(root =>
            {
                switch (target)
                {
                    case "document":
                        root["document"] = "not-an-object";
                        break;
                    case "metrics":
                        root["metrics"] = new JsonObject();
                        break;
                    case "metadata":
                        Document(root)["company"] = "not-an-object";
                        break;
                    case "metric":
                        root["metrics"] = new JsonArray("not-an-object");
                        break;
                }
            })
        };

        AssertSchemaFailure(Parse(json));
    }

    [Theory]
    [InlineData("value")]
    [InlineData("sourceKind")]
    [InlineData("confidence")]
    [InlineData("sourcePage")]
    [InlineData("evidence")]
    [InlineData("inferenceExplanation")]
    public void Parse_Should_reject_wrong_metadata_field_json_kinds(string propertyName)
    {
        var json = Mutate(root =>
        {
            var company = Company(root);
            company["inferenceExplanation"] = null;
            company[propertyName] = propertyName switch
            {
                "value" or "sourceKind" or "evidence" => 123,
                "confidence" => "high",
                "sourcePage" => "one",
                "inferenceExplanation" => 123,
                _ => throw new ArgumentOutOfRangeException(nameof(propertyName))
            };
        });

        AssertSchemaFailure(Parse(json));
    }

    [Theory]
    [InlineData("name")]
    [InlineData("period")]
    [InlineData("value")]
    [InlineData("currency")]
    [InlineData("unit")]
    [InlineData("sourceKind")]
    [InlineData("confidence")]
    [InlineData("sourcePage")]
    [InlineData("evidence")]
    [InlineData("inferenceExplanation")]
    public void Parse_Should_reject_wrong_metric_field_json_kinds(string propertyName)
    {
        var json = Mutate(root =>
        {
            Metric(root)[propertyName] = propertyName switch
            {
                "name" or "period" or "currency" or "unit" or
                    "sourceKind" or "evidence" => 123,
                "value" => "one hundred",
                "confidence" => "high",
                "sourcePage" => "twelve",
                "inferenceExplanation" => 123,
                _ => throw new ArgumentOutOfRangeException(nameof(propertyName))
            };
        });

        AssertSchemaFailure(Parse(json));
    }

    [Theory]
    [InlineData("computed")]
    [InlineData("human_corrected")]
    [InlineData("unknown")]
    public void Parse_Should_reject_untrusted_source_kinds(string sourceKind)
    {
        var metadataJson = Mutate(root => Company(root)["sourceKind"] = sourceKind);
        var metricJson = Mutate(root => Metric(root)["sourceKind"] = sourceKind);

        AssertSchemaFailure(Parse(metadataJson));
        AssertSchemaFailure(Parse(metricJson));
    }

    [Theory]
    [InlineData("company", 0)]
    [InlineData("company", -1)]
    [InlineData("metric", 0)]
    [InlineData("metric", -1)]
    public void Parse_Should_reject_source_page_less_than_one(string target, int page)
    {
        var json = Mutate(root => Candidate(root, target)["sourcePage"] = page);

        AssertSchemaFailure(Parse(json));
    }

    [Theory]
    [InlineData("company")]
    [InlineData("metric")]
    public void Parse_Should_reject_fractional_source_page(string target)
    {
        var json = Mutate(root => Candidate(root, target)["sourcePage"] = 1.5);

        AssertSchemaFailure(Parse(json));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_Should_reject_blank_metadata_value(string value)
    {
        var json = Mutate(root => Company(root)["value"] = value);

        AssertSchemaFailure(Parse(json));
    }

    [Fact]
    public void Parse_Should_reject_non_numeric_metric_value()
    {
        var nullResult = Parse(Mutate(root => Metric(root)["value"] = null));
        var stringResult = Parse(Mutate(root => Metric(root)["value"] = "100"));

        AssertSchemaFailure(nullResult);
        AssertSchemaFailure(stringResult);
    }

    [Fact]
    public void Parse_Should_reject_empty_metrics_list()
    {
        var json = Mutate(root => root["metrics"] = new JsonArray());

        AssertSchemaFailure(Parse(json));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-json")]
    [InlineData("{} {}")]
    [InlineData("{")]
    public void Parse_Should_return_only_schema_failure_for_malformed_content(string content)
    {
        AssertSchemaFailure(Parse(content));
    }

    private FinancialDocumentExtractionParseResult Parse(string content)
    {
        return _parser.Parse(content, MaxEvidenceExcerptCharacters);
    }

    private static void AssertSchemaFailure(FinancialDocumentExtractionParseResult result)
    {
        result.Succeeded.Should().BeFalse();
        result.Result.Should().BeNull();
        result.FailureReason.Should().Be(
            FinancialDocumentExtractionResponseParser.SchemaValidationFailed);
    }

    private static JsonObject Candidate(JsonObject root, string target)
    {
        return target switch
        {
            "root" => root,
            "document" => Document(root),
            "company" => Company(root),
            "metric" => Metric(root),
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
    }

    private static JsonObject Document(JsonObject root)
    {
        return root["document"]!.AsObject();
    }

    private static JsonObject Company(JsonObject root)
    {
        return Document(root)["company"]!.AsObject();
    }

    private static JsonObject Currency(JsonObject root)
    {
        return Document(root)["currency"]!.AsObject();
    }

    private static JsonObject Unit(JsonObject root)
    {
        return Document(root)["unit"]!.AsObject();
    }

    private static JsonObject Metric(JsonObject root)
    {
        return root["metrics"]!.AsArray()[0]!.AsObject();
    }

    private static string Mutate(Action<JsonObject> mutation)
    {
        var root = JsonNode.Parse(CreateValidJson())!.AsObject();
        mutation(root);
        return root.ToJsonString();
    }

    private static string CreateValidJson()
    {
        return """
{
  "document": {
    "company": {
      "value": "Example Energy",
      "sourceKind": "reported",
      "confidence": 0.98,
      "sourcePage": 1,
      "evidence": "Example Energy Annual Report"
    },
    "currency": {
      "value": "USD",
      "sourceKind": "reported",
      "confidence": 0.99,
      "sourcePage": 12,
      "evidence": "Amounts in USD millions"
    },
    "unit": {
      "value": "USD_million",
      "sourceKind": "reported",
      "confidence": 0.99,
      "sourcePage": 12,
      "evidence": "Amounts in USD millions"
    }
  },
  "metrics": [
    {
      "name": "revenue",
      "period": "2024A",
      "value": 100.0,
      "currency": "USD",
      "unit": "USD_million",
      "sourceKind": "reported",
      "confidence": 0.97,
      "sourcePage": 12,
      "evidence": "| Revenue | 100 |",
      "inferenceExplanation": null
    }
  ]
}
""";
    }
}
