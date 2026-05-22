using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Xunit;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Data.FinancialAnalysis.AiReview;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Legal.AiReview;
using Orchestration.Infrastructure.Agents.Legal.AiReview;
using Orchestration.Infrastructure.Agents.Planner.Reasoning;

namespace Orchestration.Tests.Agents.Legal.AiReview;

public sealed class SemanticKernelLegalAnalysisReviewServiceTests
{
    // ==========================================
    // Parser Tests
    // ==========================================

    [Fact]
    public void Parse_Should_parse_pure_json()
    {
        var parser = new SemanticKernelLegalAnalysisReviewResponseParser();
        var json = """
        {
          "reviewSummary": "Everything is consistent.",
          "possibleRegulatoryReviewAreas": [
            {
              "title": "Possible liquidity review area",
              "description": "Liquidity indicator is below 1.0",
              "severity": "Medium",
              "relatedFinancialSignals": ["LOW_CURRENT_RATIO"],
              "evidenceCitations": ["CNV Art. 42"]
            }
          ],
          "evidenceReferences": [
            {
              "source": "CNV",
              "title": "General Resolution 923",
              "url": "http://cnv.gob.ar/923",
              "citation": "CNV Art. 42",
              "snippet": "Compliance rules",
              "regulationArea": "Liquidity",
              "score": 0.95
            }
          ],
          "warnings": ["Warning A"],
          "limitations": ["Limitation B"]
        }
        """;

        var result = parser.Parse(json);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().NotBeNull();
        result.Response!.ReviewSummary.Should().Be("Everything is consistent.");
        result.Response.PossibleRegulatoryReviewAreas.Should().ContainSingle();
        result.Response.PossibleRegulatoryReviewAreas[0].Title.Should().Be("Possible liquidity review area");
        result.Response.EvidenceReferences.Should().ContainSingle();
        result.Response.EvidenceReferences[0].Citation.Should().Be("CNV Art. 42");
        result.Response.Warnings.Should().ContainSingle().Which.Should().Be("Warning A");
        result.Response.Limitations.Should().ContainSingle().Which.Should().Be("Limitation B");
    }

    [Fact]
    public void Parse_Should_parse_fenced_json()
    {
        var parser = new SemanticKernelLegalAnalysisReviewResponseParser();
        var content = """
        ```json
        {
          "reviewSummary": "Fenced summary",
          "possibleRegulatoryReviewAreas": [],
          "evidenceReferences": [],
          "warnings": [],
          "limitations": []
        }
        ```
        """;

        var result = parser.Parse(content);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().NotBeNull();
        result.Response!.ReviewSummary.Should().Be("Fenced summary");
    }

    [Fact]
    public void Parse_Should_fail_on_invalid_json()
    {
        var parser = new SemanticKernelLegalAnalysisReviewResponseParser();
        var result = parser.Parse("not-json");

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Be("invalid_json");
        result.Response.Should().BeNull();
    }

    [Fact]
    public void Parse_Should_fail_on_missing_review_summary()
    {
        var parser = new SemanticKernelLegalAnalysisReviewResponseParser();
        var json = """
        {
          "possibleRegulatoryReviewAreas": [],
          "evidenceReferences": [],
          "warnings": [],
          "limitations": []
        }
        """;

        var result = parser.Parse(json);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Be("schema_validation_failed");
    }

    [Fact]
    public void Parse_Should_handle_null_lists_as_empty()
    {
        var parser = new SemanticKernelLegalAnalysisReviewResponseParser();
        var json = """
        {
          "reviewSummary": "Test null arrays mapping",
          "possibleRegulatoryReviewAreas": null,
          "evidenceReferences": null,
          "warnings": null,
          "limitations": null
        }
        """;

        var result = parser.Parse(json);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().NotBeNull();
        result.Response!.PossibleRegulatoryReviewAreas.Should().BeEmpty();
        result.Response.EvidenceReferences.Should().BeEmpty();
        result.Response.Warnings.Should().BeEmpty();
        result.Response.Limitations.Should().BeEmpty();
    }

    // ==========================================
    // Service Tests
    // ==========================================

    [Fact]
    public async Task ReviewAsync_Should_return_llm_result_when_response_is_valid()
    {
        var validResponse = CreateValidResponse();
        var chat = new FakeChatCompletionService(validResponse);
        var service = CreateService(chat);

        var result = await service.ReviewAsync(CreateInput(), CancellationToken.None);

        result.UsedLlm.Should().BeTrue();
        result.UsedFallback.Should().BeFalse();
        result.Provider.Should().Be("OpenAI");
        result.Model.Should().Be("test-model");
        result.FailureReason.Should().BeNull();
        result.ReviewSummary.Should().Be("Valid review summary.");
        result.PossibleRegulatoryReviewAreas.Should().ContainSingle();
        result.PossibleRegulatoryReviewAreas[0].EvidenceCitations.Should().ContainSingle().Which.Should().Be("CNV Art. 42");
    }

    [Fact]
    public async Task ReviewAsync_Should_fallback_when_llm_is_disabled()
    {
        var chat = new FakeChatCompletionService(CreateValidResponse());
        var service = CreateService(
            chat,
            llmOptions: new LlmOptions { Enabled = false, Model = "test", ApiKey = "test", ServiceId = "test" },
            legalAgentOptions: new LegalAgentOptions { AiReviewEnabled = true }
        );

        var result = await service.ReviewAsync(CreateInput(), CancellationToken.None);

        result.UsedLlm.Should().BeFalse();
        result.UsedFallback.Should().BeTrue();
        result.FailureReason.Should().Be("llm_disabled");
        chat.Calls.Should().Be(0);
    }

    [Fact]
    public async Task ReviewAsync_Should_fallback_when_response_is_empty()
    {
        var chat = new FakeChatCompletionService("");
        var service = CreateService(chat);

        var result = await service.ReviewAsync(CreateInput(), CancellationToken.None);

        result.UsedLlm.Should().BeFalse();
        result.UsedFallback.Should().BeTrue();
        result.FailureReason.Should().Be("empty_response");
    }

    [Fact]
    public async Task ReviewAsync_Should_fallback_when_response_is_invalid_json()
    {
        var chat = new FakeChatCompletionService("invalid-json");
        var service = CreateService(chat);

        var result = await service.ReviewAsync(CreateInput(), CancellationToken.None);

        result.UsedLlm.Should().BeFalse();
        result.UsedFallback.Should().BeTrue();
        result.FailureReason.Should().Be("invalid_json");
    }

    [Fact]
    public async Task ReviewAsync_Should_fallback_when_provider_throws()
    {
        var chat = new FakeChatCompletionService("", throwOnCall: true);
        var service = CreateService(chat);

        var result = await service.ReviewAsync(CreateInput(), CancellationToken.None);

        result.UsedLlm.Should().BeFalse();
        result.UsedFallback.Should().BeTrue();
        result.FailureReason.Should().Be("provider_error");
    }

    [Fact]
    public async Task ReviewAsync_Should_fallback_when_provider_times_out()
    {
        var chat = new FakeChatCompletionService("", timeoutOnCall: true);
        var service = CreateService(chat);

        var result = await service.ReviewAsync(CreateInput(), CancellationToken.None);

        result.UsedLlm.Should().BeFalse();
        result.UsedFallback.Should().BeTrue();
        result.FailureReason.Should().Be("timeout");
    }

    [Fact]
    public async Task ReviewAsync_Should_propagate_caller_cancellation()
    {
        var chat = new FakeChatCompletionService(CreateValidResponse());
        var service = CreateService(chat);

        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var action = async () => await service.ReviewAsync(CreateInput(), cts.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ReviewAsync_Should_fallback_when_output_contains_forbidden_language()
    {
        var responseWithForbidden = """
        {
          "reviewSummary": "this is illegal under regulations",
          "possibleRegulatoryReviewAreas": [],
          "evidenceReferences": [],
          "warnings": [],
          "limitations": []
        }
        """;
        var chat = new FakeChatCompletionService(responseWithForbidden);
        var service = CreateService(chat);

        var result = await service.ReviewAsync(CreateInput(), CancellationToken.None);

        result.UsedLlm.Should().BeFalse();
        result.UsedFallback.Should().BeTrue();
        result.FailureReason.Should().Be("forbidden_language");
    }

    [Fact]
    public async Task ReviewAsync_Should_remove_invented_citations_and_related_areas()
    {
        var jsonWithInventedCitation = """
        {
          "reviewSummary": "Checking citations",
          "possibleRegulatoryReviewAreas": [
            {
              "title": "Possible liquidity area",
              "description": "Evidence citation description",
              "severity": "Medium",
              "relatedFinancialSignals": ["LOW_CURRENT_RATIO"],
              "evidenceCitations": ["CNV Art. 42", "INVENTED_CITATION_999"]
            },
            {
              "title": "Unsupported area",
              "description": "Area with no valid citations",
              "severity": "Low",
              "relatedFinancialSignals": [],
              "evidenceCitations": ["INVENTED_CITATION_999"]
            }
          ],
          "evidenceReferences": [],
          "warnings": [],
          "limitations": []
        }
        """;

        var chat = new FakeChatCompletionService(jsonWithInventedCitation);
        var service = CreateService(chat);

        var result = await service.ReviewAsync(CreateInput(), CancellationToken.None);

        result.UsedLlm.Should().BeTrue();
        result.PossibleRegulatoryReviewAreas.Should().ContainSingle();
        result.PossibleRegulatoryReviewAreas[0].Title.Should().Be("Possible liquidity area");
        result.PossibleRegulatoryReviewAreas[0].EvidenceCitations.Should().ContainSingle().Which.Should().Be("CNV Art. 42");
        result.Warnings.Should().Contain("Unknown citations or references returned by the AI review were removed.");
        result.Warnings.Should().Contain("Some possible regulatory review areas were removed because they had no valid evidence citations.");
    }

    [Fact]
    public async Task ReviewAsync_Should_filter_EvidenceReferences_to_allowed_citations()
    {
        var jsonWithInventedReference = """
        {
          "reviewSummary": "Check reference citations",
          "possibleRegulatoryReviewAreas": [
            {
              "title": "Liquidity area",
              "description": "Details",
              "severity": "Medium",
              "relatedFinancialSignals": [],
              "evidenceCitations": ["CNV Art. 42"]
            }
          ],
          "evidenceReferences": [
            {
              "source": "CNV",
              "title": "General Resolution 923",
              "url": "http://cnv.gob.ar/923",
              "citation": "CNV Art. 42",
              "snippet": "Snippet A",
              "regulationArea": "Liquidity",
              "score": 0.9
            },
            {
              "source": "CNV",
              "title": "Invented Doc",
              "url": null,
              "citation": "INVENTED_CITATION_999",
              "snippet": "Snippet B",
              "regulationArea": null,
              "score": 0.8
            }
          ],
          "warnings": [],
          "limitations": []
        }
        """;

        var chat = new FakeChatCompletionService(jsonWithInventedReference);
        var service = CreateService(chat);

        var result = await service.ReviewAsync(CreateInput(), CancellationToken.None);

        result.UsedLlm.Should().BeTrue();
        result.EvidenceReferences.Should().ContainSingle();
        result.EvidenceReferences[0].Citation.Should().Be("CNV Art. 42");
        result.Warnings.Should().Contain("Unknown citations or references returned by the AI review were removed.");
    }

    [Fact]
    public async Task ReviewAsync_Should_not_modify_input_financial_signals()
    {
        var chat = new FakeChatCompletionService(CreateValidResponse());
        var service = CreateService(chat);
        var signal = CreateSignal("LOW_CURRENT_RATIO", "High");
        var input = CreateInput(riskSignals: [signal]);

        await service.ReviewAsync(input, CancellationToken.None);

        input.FinancialRiskSignals.Should().ContainSingle().Which.Should().Be(signal);
    }

    // ==========================================
    // Dependency Injection Tests
    // ==========================================

    [Fact]
    public void DI_Should_resolve_DeterministicLegalAnalysisReviewService_when_AiReviewEnabled_is_false()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Llm:Enabled", "true" },
                { "Llm:Provider", "OpenAI" },
                { "Llm:Model", "gpt-4" },
                { "Llm:ApiKey", "secret" },
                { "Llm:ServiceId", "legal" },
                { "LegalAgent:AiReviewEnabled", "false" }
            })
            .Build();

        services.AddLogging();
        services.AddLegalAgentAiReview(config);

        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<ILegalAnalysisReviewService>();

        service.Should().BeOfType<DeterministicLegalAnalysisReviewService>();
    }

    [Fact]
    public void DI_Should_resolve_DeterministicLegalAnalysisReviewService_when_LlmEnabled_is_false()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Llm:Enabled", "false" },
                { "Llm:Provider", "OpenAI" },
                { "Llm:Model", "gpt-4" },
                { "Llm:ApiKey", "secret" },
                { "Llm:ServiceId", "legal" },
                { "LegalAgent:AiReviewEnabled", "true" }
            })
            .Build();

        services.AddLogging();
        services.AddLegalAgentAiReview(config);

        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<ILegalAnalysisReviewService>();

        service.Should().BeOfType<DeterministicLegalAnalysisReviewService>();
    }

    [Fact]
    public void DI_Should_resolve_SemanticKernelLegalAnalysisReviewService_when_both_are_true()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Llm:Enabled", "true" },
                { "Llm:Provider", "OpenAI" },
                { "Llm:Model", "gpt-4" },
                { "Llm:ApiKey", "secret" },
                { "Llm:ServiceId", "legal" },
                { "LegalAgent:AiReviewEnabled", "true" }
            })
            .Build();

        services.AddLogging();
        services.AddLegalAgentAiReview(config);

        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<ILegalAnalysisReviewService>();

        service.Should().BeOfType<SemanticKernelLegalAnalysisReviewService>();
    }

    [Fact]
    public void DI_Should_not_crash_DI_on_missing_or_incomplete_LLM_config()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Llm:Enabled", "true" },
                // Model, ApiKey, ServiceId are intentionally omitted or empty
                { "LegalAgent:AiReviewEnabled", "true" }
            })
            .Build();

        services.AddLogging();
        var action = () => services.AddLegalAgentAiReview(config);

        action.Should().NotThrow();

        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<ILegalAnalysisReviewService>();

        service.Should().BeOfType<SemanticKernelLegalAnalysisReviewService>();
    }

    // ==========================================
    // Test Helpers
    // ==========================================

    private static SemanticKernelLegalAnalysisReviewService CreateService(
        FakeChatCompletionService chat,
        LlmOptions? llmOptions = null,
        LegalAgentOptions? legalAgentOptions = null)
    {
        return new SemanticKernelLegalAnalysisReviewService(
            llmOptions ?? new LlmOptions
            {
                Enabled = true,
                Provider = "OpenAI",
                Model = "test-model",
                ApiKey = "not-used",
                ServiceId = "test"
            },
            legalAgentOptions ?? new LegalAgentOptions
            {
                AiReviewEnabled = true
            },
            new DeterministicLegalAnalysisReviewService(),
            new SemanticKernelLegalAnalysisReviewResponseParser(),
            chat
        );
    }

    private static LegalAnalysisReviewInput CreateInput(
        IReadOnlyList<FinancialRiskSignal>? riskSignals = null)
    {
        return new LegalAnalysisReviewInput(
            SessionId: "33333333-3333-3333-3333-333333333333",
            Company: "Test Co",
            DocumentId: "doc-123",
            MetricsInputSource: "session_context",
            MetricsProvenance: null,
            FinancialAiReview: null,
            FinancialRiskSignals: riskSignals ?? [CreateSignal("LOW_CURRENT_RATIO", "High")],
            FinancialRiskEvidence: Array.Empty<RiskEvidenceItem>(),
            FinancialWarnings: Array.Empty<string>(),
            FinancialLimitations: Array.Empty<string>(),
            CnvEvidence: [CreateEvidence("CNV Art. 42")]
        );
    }

    private static FinancialRiskSignal CreateSignal(string name, string severity)
    {
        return new FinancialRiskSignal(
            Name: name,
            Severity: severity,
            Period: "2024",
            Summary: "Signal warning",
            Evidence: [
                new RiskEvidenceItem(
                    MetricName: "current_ratio",
                    Period: "2024",
                    Value: 0.65m,
                    Threshold: 1.0m,
                    Unit: "ratio",
                    Interpretation: "Low current ratio"
                )
            ]
        );
    }

    private static LegalEvidenceReference CreateEvidence(string? citation)
    {
        return new LegalEvidenceReference(
            Source: "CNV",
            Title: "General Resolution 923",
            Url: "http://cnv.gob.ar/923",
            Citation: citation,
            Snippet: "Entities must maintain compliance.",
            RegulationArea: "Liquidity",
            Score: 0.88
        );
    }

    private static string CreateValidResponse()
    {
        return """
        {
          "reviewSummary": "Valid review summary.",
          "possibleRegulatoryReviewAreas": [
            {
              "title": "Possible liquidity review area",
              "description": "Low current ratio indicates liquidity pressure.",
              "severity": "High",
              "relatedFinancialSignals": ["LOW_CURRENT_RATIO"],
              "evidenceCitations": ["CNV Art. 42"]
            }
          ],
          "evidenceReferences": [
            {
              "source": "CNV",
              "title": "General Resolution 923",
              "url": "http://cnv.gob.ar/923",
              "citation": "CNV Art. 42",
              "snippet": "Maintain compliance.",
              "regulationArea": "Liquidity",
              "score": 0.9
            }
          ],
          "warnings": [],
          "limitations": []
        }
        """;
    }

    private sealed class FakeChatCompletionService : IChatCompletionService
    {
        private readonly string _content;
        private readonly bool _throwOnCall;
        private readonly bool _timeoutOnCall;

        public FakeChatCompletionService(
            string content,
            bool throwOnCall = false,
            bool timeoutOnCall = false)
        {
            _content = content;
            _throwOnCall = throwOnCall;
            _timeoutOnCall = timeoutOnCall;
        }

        public IReadOnlyDictionary<string, object?> Attributes { get; } =
            new Dictionary<string, object?>();

        public int Calls { get; private set; }

        public ChatHistory? LastChatHistory { get; private set; }

        public PromptExecutionSettings? LastExecutionSettings { get; private set; }

        public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastChatHistory = chatHistory;
            LastExecutionSettings = executionSettings;

            if (_throwOnCall)
            {
                throw new InvalidOperationException("mock provider exception");
            }

            if (_timeoutOnCall)
            {
                throw new OperationCanceledException("mock timeout exception");
            }

            IReadOnlyList<ChatMessageContent> response =
            [
                new ChatMessageContent(
                    AuthorRole.Assistant,
                    _content
                )
            ];

            return Task.FromResult(response);
        }

        public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
