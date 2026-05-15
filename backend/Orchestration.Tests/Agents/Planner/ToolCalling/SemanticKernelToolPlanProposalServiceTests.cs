using FluentAssertions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Orchestration.Application.Agents.Planner.ToolCalling;
using Orchestration.Infrastructure.Agents.Planner.ToolCalling;

namespace Orchestration.Tests.Agents.Planner.ToolCalling;

public class SemanticKernelToolPlanProposalServiceTests
{
    [Fact]
    public async Task ProposeAsync_Should_fallback_safely_when_llm_output_is_invalid()
    {
        var service = CreateService(
            toolCallingEnabled: true,
            chatContent: "not-json"
        );

        var result = await service.ProposeAsync(
            CreateInput(),
            CancellationToken.None
        );

        result.ProposedCalls.Should().HaveCount(2);
        result.ProposedCalls.Should().Contain(x => x.ToolName == "data.analyze_transactions");
        result.ProposedCalls.Should().Contain(x => x.ToolName == "legal.search_cnv_regulation");
    }

    [Fact]
    public async Task ProposeAsync_Should_return_empty_plan_when_tool_calling_is_disabled()
    {
        var service = CreateService(
            toolCallingEnabled: false,
            chatContent: """
{
  "proposedCalls": [
    {
      "toolName": "data.analyze_transactions",
      "arguments": {},
      "reason": "Analyze data."
    }
  ]
}
"""
        );

        var result = await service.ProposeAsync(
            CreateInput(),
            CancellationToken.None
        );

        result.ProposedCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task ProposeAsync_Should_return_llm_plan_when_output_is_valid()
    {
        var chatCompletionService = new FakeChatCompletionService(
            """
{
  "proposedCalls": [
    {
      "toolName": "legal.search_cnv_regulation",
      "arguments": {
        "query": "agentes",
        "limit": "5"
      },
      "reason": "Recuperar evidencia CNV citada."
    }
  ]
}
"""
        );
        var service = CreateService(
            toolCallingEnabled: true,
            chatCompletionService
        );

        var result = await service.ProposeAsync(
            CreateInput(),
            CancellationToken.None
        );

        result.ProposedCalls.Should().ContainSingle();
        result.ProposedCalls[0].ToolName.Should().Be("legal.search_cnv_regulation");
        result.ProposedCalls[0].Arguments["query"].Should().Be("agentes");
        result.ProposedCalls[0].Arguments["limit"].Should().Be("5");
        result.ProposedCalls[0].Reason.Should().Be("Recuperar evidencia CNV citada.");
        chatCompletionService.LastChatHistory
            .Should()
            .NotBeNull();
        chatCompletionService.LastChatHistory!
            .Select(message => message.Content)
            .Should()
            .Contain(message => message != null && message.Contains("Write the reason field in Spanish."))
            .And
            .Contain(message => message != null && message.Contains("Propose at most 2 tool calls."));
    }

    private static SemanticKernelToolPlanProposalService CreateService(
        bool toolCallingEnabled,
        string chatContent)
    {
        return CreateService(
            toolCallingEnabled,
            new FakeChatCompletionService(chatContent)
        );
    }

    private static SemanticKernelToolPlanProposalService CreateService(
        bool toolCallingEnabled,
        FakeChatCompletionService chatCompletionService)
    {
        var toolCallingOptions = new ToolCallingOptions
        {
            Enabled = toolCallingEnabled
        };

        return new SemanticKernelToolPlanProposalService(
            toolCallingOptions,
            new DeterministicToolPlanProposalService(toolCallingOptions),
            new SemanticKernelToolPlanResponseParser(),
            chatCompletionService
        );
    }

    private static ToolPlanProposalInput CreateInput()
    {
        return new ToolPlanProposalInput(
            SessionId: Guid.NewGuid(),
            ReportName: "financial-report",
            TotalAmount: 125000m,
            TransactionCount: 42,
            PlannerSummary: "Planner reviewed collected evidence.",
            RiskFactors: ["High data severity."],
            Limitations: ["Human approval required."]
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

        public ChatHistory? LastChatHistory { get; private set; }

        public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default)
        {
            LastChatHistory = chatHistory;

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
