using System.Text.Json.Nodes;
using FluentAssertions;
using Orchestration.Application.Agents.Data.FinancialAnalysis.Extraction;

namespace Orchestration.Tests.Agents.Data.FinancialAnalysis;

public sealed class FinancialDocumentExtractionResponseParserTests
{
    private const int MaxEvidenceExcerptCharacters = 50;
    private const int MaxSourcePage = 100;

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
        result.Result.Currency!.FieldName.Should().Be("currency");
        result.Result.Unit!.FieldName.Should().Be("unit");
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
        result.Result!.Company!.ReviewState.Should()
            .Be(FinancialMetricCandidateReviewStates.Inferred);
        result.Result.Metrics.Single().ReviewState.Should()
            .Be(FinancialMetricCandidateReviewStates.Inferred);
    }

    [Fact]
    public void Parse_Should_accept_metadata_only_result()
    {
        var json = Mutate(root => root["metrics"] = new JsonArray());

        var result = Parse(json);

        result.Succeeded.Should().BeTrue();
        result.Result!.Company.Should().NotBeNull();
        result.Result.Currency.Should().NotBeNull();
        result.Result.Unit.Should().NotBeNull();
        result.Result.Metrics.Should().BeEmpty();
    }

    [Fact]
    public void Parse_Should_accept_metrics_only_result_with_empty_document()
    {
        var json = Mutate(root => root["document"] = new JsonObject());

        var result = Parse(json);

        result.Succeeded.Should().BeTrue();
        result.Result!.Company.Should().BeNull();
        result.Result.Currency.Should().BeNull();
        result.Result.Unit.Should().BeNull();
        result.Result.Metrics.Should().ContainSingle();
    }

    [Fact]
    public void Parse_Should_accept_null_metric_currency_and_unit()
    {
        var json = Mutate(root =>
        {
            Metric(root)["currency"] = null;
            Metric(root)["unit"] = null;
        });

        var result = Parse(json);

        result.Succeeded.Should().BeTrue();
        result.Result!.Metrics.Single().Currency.Should().BeNull();
        result.Result.Metrics.Single().Unit.Should().BeNull();
    }

    [Fact]
    public void ExtractionRequest_Should_carry_max_source_page()
    {
        var request = new FinancialDocumentExtractionRequest(
            Markdown: "# Financial report",
            MaxEvidenceExcerptCharacters: 50,
            MaxMarkdownChunks: 12,
            MaxSourcePage: 250);

        request.MaxSourcePage.Should().Be(250);
    }

    [Fact]
    public void Parse_Should_accept_source_page_at_upper_bound()
    {
        var json = Mutate(root =>
        {
            Company(root)["sourcePage"] = MaxSourcePage;
            Currency(root)["sourcePage"] = MaxSourcePage;
            Unit(root)["sourcePage"] = MaxSourcePage;
            Metric(root)["sourcePage"] = MaxSourcePage;
        });

        Parse(json).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Parse_Should_accept_null_source_pages()
    {
        var json = Mutate(root =>
        {
            Company(root)["sourcePage"] = null;
            Currency(root)["sourcePage"] = null;
            Unit(root)["sourcePage"] = null;
            Metric(root)["sourcePage"] = null;
        });

        Parse(json).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("company")]
    [InlineData("metric")]
    public void Parse_Should_reject_source_page_above_upper_bound(string target)
    {
        var json = Mutate(root =>
            Candidate(root, target)["sourcePage"] = MaxSourcePage + 1);

        AssertSchemaFailure(Parse(json));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Parse_Should_reject_invalid_max_source_page(int maxSourcePage)
    {
        AssertSchemaFailure(_parser.Parse(
            CreateValidJson(),
            MaxEvidenceExcerptCharacters,
            maxSourcePage));
    }

    [Fact]
    public void Parser_Should_expose_resource_limits()
    {
        FinancialDocumentExtractionResponseParser.MaxResponseCharacters.Should()
            .Be(1_000_000);
        FinancialDocumentExtractionResponseParser.MaxMetricCandidates.Should().Be(500);
        FinancialDocumentExtractionResponseParser.MaxJsonDepth.Should().Be(8);
    }

    [Fact]
    public void Parse_Should_accept_response_at_character_limit()
    {
        var content = CreateValidJson().PadRight(
            FinancialDocumentExtractionResponseParser.MaxResponseCharacters);

        Parse(content).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Parse_Should_reject_response_over_character_limit()
    {
        var content = CreateValidJson().PadRight(
            FinancialDocumentExtractionResponseParser.MaxResponseCharacters + 1);

        AssertSchemaFailure(Parse(content));
    }

    [Fact]
    public void Parse_Should_accept_maximum_metric_candidate_count()
    {
        var result = Parse(CreateJsonWithMetricCount(
            FinancialDocumentExtractionResponseParser.MaxMetricCandidates));

        result.Succeeded.Should().BeTrue();
        result.Result!.Metrics.Should().HaveCount(500);
    }

    [Fact]
    public void Parse_Should_reject_metric_candidate_count_over_limit()
    {
        var result = Parse(CreateJsonWithMetricCount(
            FinancialDocumentExtractionResponseParser.MaxMetricCandidates + 1));

        AssertSchemaFailure(result);
    }

    [Fact]
    public void Parse_Should_reject_json_deeper_than_maximum_depth()
    {
        var content = """
{
  "document": {
    "company": {
      "value": {"a":{"b":{"c":{"d":{"e":{"f":"Example Energy"}}}}}},
      "sourceKind": "reported",
      "confidence": 0.98,
      "sourcePage": 1,
      "evidence": "Example Energy Annual Report"
    }
  },
  "metrics": []
}
""";

        AssertSchemaFailure(Parse(content));
    }

    [Fact]
    public void Parse_Should_accept_decoded_strings_at_configured_bounds()
    {
        var json = Mutate(root =>
        {
            Company(root)["value"] = new string('c', 500);
            Metric(root)["currency"] = new string('c', 32);
            Metric(root)["unit"] = new string('u', 128);
            Metric(root)["sourceKind"] = "inferred";
            Metric(root)["evidence"] = "";
            Metric(root)["inferenceExplanation"] =
                new string('i', MaxEvidenceExcerptCharacters);
        });

        Parse(json).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("metadata")]
    [InlineData("currency")]
    [InlineData("unit")]
    [InlineData("inference")]
    public void Parse_Should_reject_decoded_strings_over_configured_bounds(string target)
    {
        var json = Mutate(root =>
        {
            switch (target)
            {
                case "metadata":
                    Company(root)["value"] = new string('c', 501);
                    break;
                case "currency":
                    Metric(root)["currency"] = new string('c', 33);
                    break;
                case "unit":
                    Metric(root)["unit"] = new string('u', 129);
                    break;
                case "inference":
                    Metric(root)["sourceKind"] = "inferred";
                    Metric(root)["evidence"] = "";
                    Metric(root)["inferenceExplanation"] =
                        new string('i', MaxEvidenceExcerptCharacters + 1);
                    break;
            }
        });

        AssertSchemaFailure(Parse(json));
    }

    [Fact]
    public void Parse_Should_apply_evidence_limit_after_trimming()
    {
        var json = Mutate(root =>
        {
            Company(root)["evidence"] =
                $" \t{new string('e', MaxEvidenceExcerptCharacters)}\r\n ";
            Currency(root)["evidence"] = "currency";
            Unit(root)["evidence"] = "unit";
            Metric(root)["evidence"] =
                $" \t{new string('e', MaxEvidenceExcerptCharacters)}\r\n ";
        });

        var result = Parse(json);

        result.Succeeded.Should().BeTrue();
        result.Result!.Company!.Evidence.Should()
            .HaveLength(MaxEvidenceExcerptCharacters);
        result.Result.Metrics.Single().Evidence.Should()
            .HaveLength(MaxEvidenceExcerptCharacters);
    }

    [Theory]
    [InlineData("1e1000")]
    [InlineData("-1e1000")]
    public void Parse_Should_reject_metric_values_outside_decimal_range(string value)
    {
        var json = CreateValidJson().Replace(
            "\"value\": 100.0",
            $"\"value\": {value}",
            StringComparison.Ordinal);

        AssertSchemaFailure(Parse(json));
    }

    public static IEnumerable<object[]> UnsafeDecodedStringCases()
    {
        var targets = new[]
        {
            "metadata",
            "name",
            "period",
            "currency",
            "unit",
            "evidence",
            "inference",
            "sourceKind"
        };

        foreach (var target in targets)
        {
            yield return [target, 0];
            yield return [target, 1];
        }
    }

    [Theory]
    [MemberData(nameof(UnsafeDecodedStringCases))]
    public void Parse_Should_reject_unsafe_decoded_control_characters(
        string target,
        int characterCode)
    {
        var unsafeCharacter = ((char)characterCode).ToString();
        var json = Mutate(root =>
        {
            switch (target)
            {
                case "metadata":
                    Company(root)["value"] = $"Example{unsafeCharacter} Energy";
                    break;
                case "name":
                    Metric(root)["name"] = $"revenue{unsafeCharacter}";
                    break;
                case "period":
                    Metric(root)["period"] = $"2024A{unsafeCharacter}";
                    break;
                case "currency":
                    Metric(root)["currency"] = $"USD{unsafeCharacter}";
                    break;
                case "unit":
                    Metric(root)["unit"] = $"USD{unsafeCharacter}_million";
                    break;
                case "evidence":
                    Metric(root)["evidence"] = $"Revenue{unsafeCharacter}100";
                    break;
                case "inference":
                    Metric(root)["sourceKind"] = "inferred";
                    Metric(root)["evidence"] = "";
                    Metric(root)["inferenceExplanation"] =
                        $"Derived{unsafeCharacter}from context";
                    break;
                case "sourceKind":
                    Metric(root)["sourceKind"] = $"reported{unsafeCharacter}";
                    break;
            }
        });

        AssertSchemaFailure(Parse(json));
    }

    [Fact]
    public void Parse_Should_allow_cr_lf_and_tab_in_free_text()
    {
        var json = Mutate(root =>
        {
            Company(root)["value"] = "Example\tEnergy";
            Company(root)["evidence"] = "Example\r\n\tEnergy";
            Metric(root)["sourceKind"] = "inferred";
            Metric(root)["currency"] = "U\tSD";
            Metric(root)["unit"] = "USD\r\nmillion";
            Metric(root)["evidence"] = "";
            Metric(root)["inferenceExplanation"] = "Derived\r\n\tfrom context";
        });

        Parse(json).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Parse_Should_reject_unpaired_utf16_surrogate_when_exposed()
    {
        var json = CreateValidJson().Replace(
            "\"currency\": \"USD\",",
            "\"currency\": \"\\uD800\",",
            StringComparison.Ordinal);

        AssertSchemaFailure(Parse(json));
    }

    [Fact]
    public void Parse_Should_reject_unpaired_utf16_surrogate_in_property_name()
    {
        var json = CreateValidJson().Replace(
            "\"document\": {",
            "\"\\uD800\": null,\n  \"document\": {",
            StringComparison.Ordinal);

        AssertSchemaFailure(Parse(json));
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

        _parser.Parse(json, 0, MaxSourcePage).Succeeded.Should().BeTrue();

        var tooLong = Mutate(root =>
        {
            Company(root)["evidence"] = "x";
            Currency(root)["evidence"] = "x";
            Unit(root)["evidence"] = "x";
            Metric(root)["evidence"] = "xx";
        });

        AssertSchemaFailure(_parser.Parse(tooLong, 0, MaxSourcePage));
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
    public void Parse_Should_reject_fully_empty_result()
    {
        var json = Mutate(root =>
        {
            root["document"] = new JsonObject();
            root["metrics"] = new JsonArray();
        });

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
        return _parser.Parse(
            content,
            MaxEvidenceExcerptCharacters,
            MaxSourcePage);
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

    private static string CreateJsonWithMetricCount(int count)
    {
        var root = JsonNode.Parse(CreateValidJson())!.AsObject();
        var template = Metric(root).DeepClone();
        var metrics = new JsonArray();

        for (var index = 0; index < count; index++)
        {
            metrics.Add(template.DeepClone());
        }

        root["metrics"] = metrics;
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
