using FluentAssertions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Orchestration.Application.Agents.Planner.Reasoning;
using Orchestration.Infrastructure.Agents.Planner.Reasoning;

namespace Orchestration.Tests.Agents.Planner;

public class SemanticKernelPlannerReasoningServiceTests
{
    [Fact]
    public void TryParseResponse_Should_parse_valid_json()
    {
        const string content = """
{
  "summary": "Human review is required.",
  "recommendedActions": ["Review anomaly evidence."],
  "riskFactors": ["High data severity."],
  "limitations": ["Not legal advice."]
}
""";

        var result = SemanticKernelPlannerReasoningService.TryParseResponse(
            content,
            "Semantic Kernel + OpenAI/test-model",
            "OpenAI",
            "test-model"
        );

        result.Should().NotBeNull();
        result!.Engine.Should().Be("Semantic Kernel + OpenAI/test-model");
        result.UsedLlm.Should().BeTrue();
        result.UsedFallback.Should().BeFalse();
        result.Provider.Should().Be("OpenAI");
        result.Model.Should().Be("test-model");
        result.FailureReason.Should().BeNull();
        result.Summary.Should().Be("Human review is required.");
        result.RecommendedActions.Should().Contain("Review anomaly evidence.");
        result.RiskFactors.Should().Contain("High data severity.");
        result.Limitations.Should().Contain("Not legal advice.");
    }

    [Fact]
    public void TryParseResponse_Should_parse_json_inside_code_fence()
    {
        const string content = """
```json
{
  "summary": "Human review is required.",
  "recommendedActions": ["Review anomaly evidence."],
  "riskFactors": ["High data severity."],
  "limitations": ["Not legal advice."]
}
```
""";

        var result = SemanticKernelPlannerReasoningService.TryParseResponse(
            content,
            "Semantic Kernel + OpenAI/test-model",
            "OpenAI",
            "test-model"
        );

        result.Should().NotBeNull();
        result!.UsedLlm.Should().BeTrue();
        result.UsedFallback.Should().BeFalse();
        result.Summary.Should().Be("Human review is required.");
    }

    [Fact]
    public void TryParseResponse_Should_parse_embedded_json()
    {
        const string content = """
Here is the structured response:
{
  "summary": "Human review is required.",
  "recommendedActions": ["Review anomaly evidence."],
  "riskFactors": ["High data severity."],
  "limitations": ["Not legal advice."]
}
No other action was taken.
""";

        var result = SemanticKernelPlannerReasoningService.TryParseResponse(
            content,
            "Semantic Kernel + OpenAI/test-model",
            "OpenAI",
            "test-model"
        );

        result.Should().NotBeNull();
        result!.UsedLlm.Should().BeTrue();
        result.UsedFallback.Should().BeFalse();
        result.Summary.Should().Be("Human review is required.");
    }

    [Fact]
    public void TryParseResponse_Should_return_null_for_invalid_json()
    {
        var result = SemanticKernelPlannerReasoningService.TryParseResponse(
            "not-json",
            "Semantic Kernel + OpenAI/test-model",
            "OpenAI",
            "test-model"
        );

        result.Should().BeNull();
    }

    [Fact]
    public void ApplyFallbackMetadata_Should_include_safe_failure_reason()
    {
        var fallback = new PlannerReasoningResult(
            Engine: "Deterministic Planner Reasoning",
            Summary: "Fallback summary.",
            RecommendedActions: ["Review evidence."],
            RiskFactors: ["Risk factor."],
            Limitations: ["No se usó razonamiento LLM."],
            UsedLlm: false,
            UsedFallback: true,
            Provider: null,
            Model: null,
            FailureReason: null
        );

        var result = SemanticKernelPlannerReasoningService.ApplyFallbackMetadata(
            fallback,
            new LlmOptions
            {
                Enabled = true,
                Provider = "OpenAI",
                Model = "test-model",
                ApiKey = "not-used"
            },
            SemanticKernelPlannerReasoningService.SanitizeFailure(new System.Text.Json.JsonException())
        );

        result.UsedLlm.Should().BeFalse();
        result.UsedFallback.Should().BeTrue();
        result.Provider.Should().Be("OpenAI");
        result.Model.Should().Be("test-model");
        result.FailureReason.Should().Be("LLM returned invalid JSON.");
        result.FailureReason.Should().NotContain("System.Text.Json");
    }

    [Fact]
    public async Task GenerateReasoningAsync_Should_not_fallback_when_llm_returns_valid_json()
    {
        var chatCompletionService = new FakeChatCompletionService(
            """
{
  "summary": "Se requiere revision humana.",
  "recommendedActions": ["Revisar evidencia de anomalias."],
  "riskFactors": ["Severidad alta en DataAgent."],
  "limitations": ["No es asesoramiento legal."]
}
"""
        );

        var service = new SemanticKernelPlannerReasoningService(
            new LlmOptions
            {
                Enabled = true,
                Provider = "OpenAI",
                Model = "test-model",
                ApiKey = "not-used"
            },
            new DeterministicPlannerReasoningService(),
            chatCompletionService
        );

        var result = await service.GenerateReasoningAsync(
            CreateInput(),
            CancellationToken.None
        );

        result.Engine.Should().Be("Semantic Kernel + OpenAI/test-model");
        result.UsedLlm.Should().BeTrue();
        result.UsedFallback.Should().BeFalse();
        result.FailureReason.Should().BeNull();
        result.Summary.Should().Be("Se requiere revision humana.");

        chatCompletionService.LastExecutionSettings
            .Should()
            .BeOfType<OpenAIPromptExecutionSettings>()
            .Which.ResponseFormat.Should().NotBeNull();

        chatCompletionService.LastChatHistory
            .Should()
            .NotBeNull();
        chatCompletionService.LastChatHistory!
            .Select(message => message.Content)
            .Should()
            .Contain(message => message != null && message.Contains("Respond in Spanish."));
    }

    private static PlannerReasoningInput CreateInput()
    {
        return new PlannerReasoningInput(
            SessionId: Guid.NewGuid(),
            ReportName: "financial-report",
            TotalAmount: 125000m,
            TransactionCount: 42,
            DataSummary: "Anomaly detected.",
            DataSeverity: "High",
            DataEngine: "TestDataEngine",
            DataEvidence: ["Z-score above threshold."],
            LegalSummary: "Compliance review required.",
            LegalRiskLevel: "Medium",
            LegalEngine: "TestLegalEngine",
            LegalEvidence: ["Cited CNV evidence."],
            LegalWarnings: ["Human legal review required."]
        );
    }

    private sealed class FakeChatCompletionService : IChatCompletionService
    {
        private readonly string _content;

        public FakeChatCompletionService(
            string content)
        {
            _content = content;
        }

        public IReadOnlyDictionary<string, object?> Attributes { get; } =
            new Dictionary<string, object?>();

        public PromptExecutionSettings? LastExecutionSettings { get; private set; }

        public ChatHistory? LastChatHistory { get; private set; }

        public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default)
        {
            LastChatHistory = chatHistory;
            LastExecutionSettings = executionSettings;

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
