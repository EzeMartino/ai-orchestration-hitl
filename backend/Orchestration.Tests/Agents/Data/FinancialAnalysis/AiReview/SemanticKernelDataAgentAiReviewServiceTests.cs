using FluentAssertions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Data.FinancialAnalysis.AiReview;
using Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.AiReview;
using Orchestration.Infrastructure.Agents.Planner.Reasoning;

namespace Orchestration.Tests.Agents.Data.FinancialAnalysis.AiReview;

public sealed class SemanticKernelDataAgentAiReviewServiceTests
{
    [Fact]
    public async Task ReviewAsync_Should_return_llm_result_with_provider_metadata_when_response_is_valid()
    {
        var chat = new FakeChatCompletionService(CreateValidResponse());
        var service = CreateService(chat);

        var result = await service.ReviewAsync(
            CreateInput(),
            CancellationToken.None
        );

        result.UsedLlm.Should().BeTrue();
        result.UsedFallback.Should().BeFalse();
        result.Provider.Should().Be("OpenAI");
        result.Model.Should().Be("test-model");
        result.FailureReason.Should().BeNull();
        result.KeyFindings.Should().ContainSingle()
            .Which.RelatedMetrics.Should().BeEquivalentTo(["current_ratio"]);

        chat.LastExecutionSettings.Should()
            .BeOfType<OpenAIPromptExecutionSettings>()
            .Which.ResponseFormat.Should().NotBeNull();
        chat.LastChatHistory.Should().NotBeNull();
        chat.LastChatHistory!
            .Select(message => message.Content)
            .Should()
            .Contain(message => message != null && message.Contains("Return JSON only."))
            .And
            .Contain(message => message != null && message.Contains("\"metricsProvenance\""))
            .And
            .NotContain(message => message != null && message.Contains("contentHash"));
    }

    [Fact]
    public async Task ReviewAsync_Should_fallback_when_llm_is_disabled()
    {
        var chat = new FakeChatCompletionService(CreateValidResponse());
        var service = CreateService(
            chat,
            options: new LlmOptions
            {
                Enabled = false,
                Provider = "OpenAI",
                Model = "test-model",
                ApiKey = "not-used"
            }
        );

        var result = await service.ReviewAsync(
            CreateInput(),
            CancellationToken.None
        );

        result.UsedLlm.Should().BeFalse();
        result.UsedFallback.Should().BeTrue();
        result.Provider.Should().Be("OpenAI");
        result.Model.Should().Be("test-model");
        result.FailureReason.Should().Be("llm_disabled");
        chat.Calls.Should().Be(0);
    }

    [Fact]
    public async Task ReviewAsync_Should_fallback_when_response_is_invalid_json()
    {
        var service = CreateService(new FakeChatCompletionService("not-json"));

        var result = await service.ReviewAsync(
            CreateInput(),
            CancellationToken.None
        );

        result.UsedLlm.Should().BeFalse();
        result.UsedFallback.Should().BeTrue();
        result.FailureReason.Should().Be("invalid_json");
    }

    [Fact]
    public async Task ReviewAsync_Should_fallback_when_response_is_empty()
    {
        var service = CreateService(new FakeChatCompletionService(""));

        var result = await service.ReviewAsync(
            CreateInput(),
            CancellationToken.None
        );

        result.UsedLlm.Should().BeFalse();
        result.FailureReason.Should().Be("empty_response");
    }

    [Fact]
    public async Task ReviewAsync_Should_fallback_when_provider_throws()
    {
        var service = CreateService(new FakeChatCompletionService(
            content: "",
            throwOnCall: true
        ));

        var result = await service.ReviewAsync(
            CreateInput(),
            CancellationToken.None
        );

        result.UsedLlm.Should().BeFalse();
        result.UsedFallback.Should().BeTrue();
        result.FailureReason.Should().Be("provider_error");
        result.FailureReason.Should().NotContain("System.");
    }

    [Fact]
    public async Task ReviewAsync_Should_not_modify_input_risk_signals()
    {
        var riskSignal = CreateRiskSignal();
        var input = CreateInput(riskSignals: [riskSignal]);
        var service = CreateService(new FakeChatCompletionService(CreateValidResponse()));

        await service.ReviewAsync(
            input,
            CancellationToken.None
        );

        input.RiskSignals.Should().ContainSingle().Which.Should().Be(riskSignal);
    }

    [Fact]
    public async Task ReviewAsync_Should_remove_unknown_related_metrics()
    {
        var response = """
{
  "summary": "Review deterministic evidence.",
  "keyFindings": [
    {
      "title": "Liquidity pressure",
      "description": "Current ratio is below threshold.",
      "severity": "High",
      "relatedMetrics": ["current_ratio", "invented_metric"]
    }
  ],
  "riskInterpretation": "Human review should focus on the provided evidence.",
  "dataQualityNotes": [],
  "limitations": []
}
""";
        var service = CreateService(new FakeChatCompletionService(response));

        var result = await service.ReviewAsync(
            CreateInput(),
            CancellationToken.None
        );

        result.UsedLlm.Should().BeTrue();
        result.KeyFindings.Should().ContainSingle()
            .Which.RelatedMetrics.Should().BeEquivalentTo(["current_ratio"]);
        result.Limitations.Should().Contain("Unknown related metrics returned by the AI review were removed.");
    }

    [Fact]
    public async Task ReviewAsync_Should_fallback_when_output_contains_unsafe_language()
    {
        var response = """
{
  "summary": "Buy this position.",
  "keyFindings": [],
  "riskInterpretation": "Human review should focus on the provided evidence.",
  "dataQualityNotes": [],
  "limitations": []
}
""";
        var service = CreateService(new FakeChatCompletionService(response));

        var result = await service.ReviewAsync(
            CreateInput(),
            CancellationToken.None
        );

        result.UsedLlm.Should().BeFalse();
        result.UsedFallback.Should().BeTrue();
        result.FailureReason.Should().Be("unsafe_content");

        var output = string.Join(" ", EnumerateOutputStrings(result)).ToLowerInvariant();
        output.Should().NotContain("investment advice");
        output.Should().NotContain("buy");
        output.Should().NotContain("sell");
        output.Should().NotContain("illegal");
        output.Should().NotContain("guaranteed");
        output.Should().NotContain("accounting correctness confirmed");
    }

    private static SemanticKernelDataAgentAiReviewService CreateService(
        FakeChatCompletionService chatCompletionService,
        LlmOptions? options = null)
    {
        return new SemanticKernelDataAgentAiReviewService(
            options ?? new LlmOptions
            {
                Enabled = true,
                Provider = "OpenAI",
                Model = "test-model",
                ApiKey = "not-used"
            },
            new DeterministicDataAgentAiReviewService(),
            new SemanticKernelDataAgentAiReviewResponseParser(),
            chatCompletionService
        );
    }

    private static FinancialAnalysisAiReviewInput CreateInput(
        IReadOnlyList<FinancialRiskSignal>? riskSignals = null)
    {
        return new FinancialAnalysisAiReviewInput(
            SessionId: "11111111-1111-1111-1111-111111111111",
            DocumentId: "manual-json-input",
            Company: "Manual Test Co",
            MetricsInputSource: FinancialMetricsInputSources.SessionContext,
            MetricsProvenance: new StructuredFinancialMetricsProvenance(
                IngestionMethod: "json_file",
                OriginalFileName: "metrics.json",
                FileSizeBytes: 512,
                ContentHash: "secret-hash-not-for-llm",
                MetricCount: 12,
                WarningCount: 1
            ),
            Ratios:
            [
                new FinancialRatio(
                    Name: "current_ratio",
                    Period: "2025E",
                    Value: 0.67m,
                    Unit: "ratio",
                    Formula: "current_assets / current_liabilities",
                    Inputs: ["current_assets", "current_liabilities"],
                    Interpretation: "Liquidity is below threshold."
                )
            ],
            PeriodComparisons:
            [
                new FinancialPeriodComparison(
                    MetricName: "revenue",
                    FromPeriod: "2024A",
                    ToPeriod: "2025E",
                    FromValue: 100m,
                    ToValue: 82m,
                    AbsoluteChange: -18m,
                    PercentageChange: -0.18m,
                    Unit: "USD_thousand",
                    Interpretation: "Revenue declined."
                )
            ],
            RiskSignals: riskSignals ?? [CreateRiskSignal()],
            RiskEvidence: [CreateEvidence("current_ratio")],
            Warnings: ["Structured metrics are manually provided."],
            Limitations: ["No PDF extraction was performed."]
        );
    }

    private static FinancialRiskSignal CreateRiskSignal()
    {
        return new FinancialRiskSignal(
            Name: "LOW_CURRENT_RATIO",
            Severity: "High",
            Period: "2025E",
            Summary: "Current ratio is below threshold.",
            Evidence: [CreateEvidence("current_ratio")]
        );
    }

    private static RiskEvidenceItem CreateEvidence(string metricName)
    {
        return new RiskEvidenceItem(
            MetricName: metricName,
            Period: "2025E",
            Value: 0.67m,
            Threshold: 1.0m,
            Unit: "ratio",
            Interpretation: $"{metricName} crossed the configured review threshold."
        );
    }

    private static string CreateValidResponse()
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
  "limitations": ["Advisory interpretation only."]
}
""";
    }

    private static IEnumerable<string> EnumerateOutputStrings(FinancialAnalysisAiReviewResult result)
    {
        yield return result.Summary;
        yield return result.RiskInterpretation;

        foreach (var limitation in result.Limitations)
        {
            yield return limitation;
        }
    }

    private sealed class FakeChatCompletionService : IChatCompletionService
    {
        private readonly string _content;
        private readonly bool _throwOnCall;

        public FakeChatCompletionService(
            string content,
            bool throwOnCall = false)
        {
            _content = content;
            _throwOnCall = throwOnCall;
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
                throw new InvalidOperationException("provider stack trace should not leak");
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
