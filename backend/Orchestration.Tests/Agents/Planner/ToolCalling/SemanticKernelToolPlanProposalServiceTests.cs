using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Orchestration.Application.Agents.Planner.ToolCalling;
using Orchestration.Infrastructure.Agents.Planner.ToolCalling;
using System.Globalization;
using System.Text.Json;

namespace Orchestration.Tests.Agents.Planner.ToolCalling;

public class SemanticKernelToolPlanProposalServiceTests
{
    private static readonly Guid SessionId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DateTimeOffset SubmittedAt =
        new DateTimeOffset(2024, 2, 3, 4, 5, 6, TimeSpan.FromHours(-3))
            .AddTicks(1_234_567);

    private const string CanonicalizationFailureCode =
        "TOOL_PLAN_SUBMITTED_AT_CANONICALIZED";

    [Theory]
    [InlineData("{}", "missing", null, null)]
    [InlineData("{\"submittedAt\":\"2030-01-01T00:00:00Z\"}", "mismatch", "2030-01-01T00:00:00Z", null)]
    [InlineData("{\"submittedAt\":\"not-a-date\"}", "malformed", "not-a-date", null)]
    [InlineData("{\" SubmittedAt \":\"2030-01-01T00:00:00Z\"}", "mismatch", "2030-01-01T00:00:00Z", null)]
    [InlineData("{\"submittedAt\":\"2030-01-01T00:00:00Z\",\"SubmittedAt\":\"2031-01-01T00:00:00Z\"}", "duplicate", "2030-01-01T00:00:00Z", "2031-01-01T00:00:00Z")]
    public async Task ProposeAsync_Should_canonicalize_data_submitted_at_and_log_safe_reason(
        string argumentsJson,
        string expectedReason,
        string? firstUnsafeFragment,
        string? secondUnsafeFragment)
    {
        var logger = new CapturingLogger<SemanticKernelToolPlanProposalService>();
        var input = CreateInput();
        var service = CreateService(
            toolCallingEnabled: true,
            chatContent: CreatePlanJson(
                PlannerToolCatalog.AnalyzeTransactionsName,
                argumentsJson
            ),
            logger
        );

        var result = await service.ProposeAsync(input, CancellationToken.None);

        var dataCall = result.ProposedCalls.Should().ContainSingle().Subject;
        dataCall.Arguments.Keys
            .Where(IsSubmittedAtKey)
            .Should()
            .Equal("submittedAt");
        dataCall.Arguments["submittedAt"].Should().Be(FormatSubmittedAt(input));

        var entry = logger.Entries.Should().ContainSingle().Subject;
        AssertSafeCanonicalizationWarning(
            entry,
            input,
            expectedReason,
            firstUnsafeFragment,
            secondUnsafeFragment
        );
    }

    [Fact]
    public async Task ProposeAsync_Should_record_invalid_response_fallback()
    {
        var logger = new CapturingLogger<SemanticKernelToolPlanProposalService>();
        var input = CreateInput();
        var fallback = new DeterministicToolPlanProposalService(
            new ToolCallingOptions
            {
                Enabled = true
            }
        );
        var service = CreateService(
            toolCallingEnabled: true,
            chatContent: "not-json",
            logger
        );

        var result = await service.ProposeAsync(
            input,
            CancellationToken.None
        );
        var expected = await fallback.ProposeAsync(
            input,
            CancellationToken.None
        );

        result.ProposedCalls.Should().BeEquivalentTo(
            expected.ProposedCalls,
            options => options.WithStrictOrdering()
        );
        result.ProposalSource.Should().Be(ToolPlanProposalSource.DeterministicFallback);
        result.ProposalFallbackReason.Should().Be(
            ToolPlanProposalFallbackReason.LlmResponseInvalid);
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ProposeAsync_Should_record_request_failure_fallback()
    {
        var service = CreateService(
            toolCallingEnabled: true,
            new FakeChatCompletionService(new InvalidOperationException("sensitive request failure"))
        );

        var result = await service.ProposeAsync(
            CreateInput(),
            CancellationToken.None
        );

        result.ProposalSource.Should().Be(ToolPlanProposalSource.DeterministicFallback);
        result.ProposalFallbackReason.Should().Be(
            ToolPlanProposalFallbackReason.LlmRequestFailed);
    }

    [Fact]
    public async Task ProposeAsync_Should_record_configuration_failure_fallback()
    {
        var toolCallingOptions = new ToolCallingOptions
        {
            Enabled = true
        };
        var service = new SemanticKernelToolPlanProposalService(
            toolCallingOptions,
            new DeterministicToolPlanProposalService(toolCallingOptions),
            new SemanticKernelToolPlanResponseParser(),
            chatCompletionService: null,
            kernel: null,
            configurationFailure: ToolPlanProposalFallbackReason.LlmConfigurationFailed
        );

        var result = await service.ProposeAsync(
            CreateInput(),
            CancellationToken.None
        );

        result.ProposalSource.Should().Be(ToolPlanProposalSource.DeterministicFallback);
        result.ProposalFallbackReason.Should().Be(
            ToolPlanProposalFallbackReason.LlmConfigurationFailed);
    }

    [Fact]
    public async Task ProposeAsync_Should_always_propagate_operation_cancellation()
    {
        var service = CreateService(
            toolCallingEnabled: true,
            new FakeChatCompletionService(new OperationCanceledException("request canceled"))
        );

        Func<Task> act = () => service.ProposeAsync(
            CreateInput(),
            CancellationToken.None
        );

        await act.Should().ThrowAsync<OperationCanceledException>();
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
      "arguments": {},
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
        result.ProposedCalls[0].Arguments.Should().BeEmpty();
        result.ProposedCalls[0].Reason.Should().Be("Recuperar evidencia CNV citada.");
        result.ProposalSource.Should().Be(ToolPlanProposalSource.Llm);
        result.ProposalFallbackReason.Should().BeNull();
        chatCompletionService.LastChatHistory
            .Should()
            .NotBeNull();
        chatCompletionService.LastChatHistory!
            .Select(message => message.Content)
            .Should()
            .Contain(message => message != null && message.Contains("Write the reason field in Spanish."))
            .And
            .Contain(message => message != null && message.Contains("Do not exceed maxToolCalls."));
        var userMessage = chatCompletionService.LastChatHistory!
            .Single(message => message.Role == AuthorRole.User)
            .Content!;
        using var payload = JsonDocument.Parse(userMessage);
        payload.RootElement.GetProperty("submittedAt").GetString()
            .Should().Be(FormatSubmittedAt(CreateInput()));
    }

    [Fact]
    public async Task ProposeAsync_Should_leave_exact_canonical_data_argument_unchanged_without_logging()
    {
        var logger = new CapturingLogger<SemanticKernelToolPlanProposalService>();
        var input = CreateInput();
        var expectedArguments = new Dictionary<string, string>
        {
            ["submittedAt"] = FormatSubmittedAt(input),
            ["reportName"] = "llm-report",
            ["totalAmount"] = "999.50"
        };
        var service = CreateService(
            toolCallingEnabled: true,
            chatContent: CreatePlanJson(
                PlannerToolCatalog.AnalyzeTransactionsName,
                JsonSerializer.Serialize(expectedArguments)
            ),
            logger
        );

        var result = await service.ProposeAsync(input, CancellationToken.None);

        result.ProposedCalls.Should().ContainSingle();
        result.ProposedCalls[0].Arguments.Should().Equal(expectedArguments);
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ProposeAsync_Should_canonicalize_each_data_call_using_trimmed_case_insensitive_tool_match()
    {
        var logger = new CapturingLogger<SemanticKernelToolPlanProposalService>();
        var input = CreateInput();
        var service = CreateService(
            toolCallingEnabled: true,
            chatContent: """
{
  "proposedCalls": [
    {
      "toolName": " DATA.ANALYZE_TRANSACTIONS ",
      "arguments": { "marker": "first" },
      "reason": "Primera llamada."
    },
    {
      "toolName": "data.analyze_transactions",
      "arguments": { "submittedAt": "not-a-date", "marker": "second" },
      "reason": "Segunda llamada."
    }
  ]
}
""",
            logger
        );

        var result = await service.ProposeAsync(input, CancellationToken.None);

        result.ProposedCalls.Should().HaveCount(2);
        result.ProposedCalls[0].ToolName.Should().Be(" DATA.ANALYZE_TRANSACTIONS ");
        result.ProposedCalls[0].Arguments["marker"].Should().Be("first");
        result.ProposedCalls[1].Arguments["marker"].Should().Be("second");
        result.ProposedCalls.Should().OnlyContain(call =>
            call.Arguments.Count(pair => IsSubmittedAtKey(pair.Key)) == 1 &&
            call.Arguments["submittedAt"] == FormatSubmittedAt(input));

        logger.Entries.Should().HaveCount(2);
        AssertSafeCanonicalizationWarning(logger.Entries[0], input, "missing");
        AssertSafeCanonicalizationWarning(logger.Entries[1], input, "malformed", "not-a-date");
    }

    [Fact]
    public async Task ProposeAsync_Should_leave_legal_only_plan_identical_without_logging()
    {
        var logger = new CapturingLogger<SemanticKernelToolPlanProposalService>();
        var input = CreateInput();
        var service = CreateService(
            toolCallingEnabled: true,
            chatContent: """
{
  "proposedCalls": [
    {
      "toolName": "legal.search_cnv_regulation",
      "arguments": {},
      "reason": "Buscar regulación."
    }
  ]
}
""",
            logger
        );

        var result = await service.ProposeAsync(input, CancellationToken.None);

        var legalCall = result.ProposedCalls.Should().ContainSingle().Subject;
        legalCall.Should().BeEquivalentTo(new ProposedToolCall(
            PlannerToolCatalog.SearchCnvRegulationName,
            new Dictionary<string, string>(),
            "Buscar regulación."
        ));
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ProposeAsync_Should_serialize_only_allowlisted_catalog_definitions()
    {
        var chatCompletionService = new FakeChatCompletionService(
            """
            { "proposedCalls": [] }
            """
        );
        var options = new ToolCallingOptions
        {
            Enabled = true,
            AllowedTools = [PlannerToolCatalog.SearchCnvRegulationName]
        };
        var service = new SemanticKernelToolPlanProposalService(
            options,
            new DeterministicToolPlanProposalService(options),
            new SemanticKernelToolPlanResponseParser(),
            chatCompletionService
        );

        await service.ProposeAsync(CreateInput(), CancellationToken.None);

        var userMessage = chatCompletionService.LastChatHistory!
            .Single(message => message.Role == AuthorRole.User)
            .Content!;
        using var payload = JsonDocument.Parse(userMessage);
        var availableTools = payload.RootElement.GetProperty("availableTools");

        availableTools.GetArrayLength().Should().Be(1);
        availableTools[0].GetProperty("name").GetString()
            .Should().Be(PlannerToolCatalog.SearchCnvRegulationName);
        availableTools[0].GetProperty("arguments").GetArrayLength()
            .Should().Be(0);
    }

    private static SemanticKernelToolPlanProposalService CreateService(
        bool toolCallingEnabled,
        string chatContent,
        ILogger<SemanticKernelToolPlanProposalService>? logger = null)
    {
        return CreateService(
            toolCallingEnabled,
            new FakeChatCompletionService(chatContent),
            logger
        );
    }

    private static SemanticKernelToolPlanProposalService CreateService(
        bool toolCallingEnabled,
        FakeChatCompletionService chatCompletionService,
        ILogger<SemanticKernelToolPlanProposalService>? logger = null)
    {
        var toolCallingOptions = new ToolCallingOptions
        {
            Enabled = toolCallingEnabled
        };

        return new SemanticKernelToolPlanProposalService(
            toolCallingOptions,
            new DeterministicToolPlanProposalService(toolCallingOptions),
            new SemanticKernelToolPlanResponseParser(),
            chatCompletionService,
            logger
        );
    }

    private static string CreatePlanJson(
        string toolName,
        string argumentsJson)
    {
        return $$"""
{
  "proposedCalls": [
    {
      "toolName": {{JsonSerializer.Serialize(toolName)}},
      "arguments": {{argumentsJson}},
      "reason": "Analizar transacciones."
    }
  ]
}
""";
    }

    private static bool IsSubmittedAtKey(
        string key)
    {
        return string.Equals(
            key.Trim(),
            "submittedAt",
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static string FormatSubmittedAt(
        ToolPlanProposalInput input)
    {
        return input.SubmittedAt.ToString("O", CultureInfo.InvariantCulture);
    }

    private static void AssertSafeCanonicalizationWarning(
        CapturedLogEntry entry,
        ToolPlanProposalInput input,
        string expectedReason,
        params string?[] unsafeFragments)
    {
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Properties.Keys.Should().BeEquivalentTo(
            ["SessionId", "ToolName", "FailureCode", "Reason"]
        );
        entry.Properties["SessionId"].Should().Be(input.SessionId);
        entry.Properties["ToolName"].Should().Be(PlannerToolCatalog.AnalyzeTransactionsName);
        entry.Properties["FailureCode"].Should().Be(CanonicalizationFailureCode);
        entry.Properties["Reason"].Should().Be(expectedReason);

        var structuredProperties = string.Join(" ", entry.Properties.Values);
        var forbiddenFragments = new[]
        {
            input.ReportName,
            input.TotalAmount.ToString(CultureInfo.InvariantCulture),
            input.TransactionCount.ToString(CultureInfo.InvariantCulture),
            input.PlannerSummary,
            input.RiskFactors[0],
            input.Limitations[0]
        }
            .Concat(unsafeFragments.OfType<string>());

        foreach (var forbiddenFragment in forbiddenFragments)
        {
            entry.Message.Should().NotContain(forbiddenFragment);
            structuredProperties.Should().NotContain(forbiddenFragment);
        }
    }

    private static ToolPlanProposalInput CreateInput()
    {
        return new ToolPlanProposalInput(
            SessionId: SessionId,
            ReportName: "financial-report",
            TotalAmount: 125000m,
            TransactionCount: 42,
            SubmittedAt: SubmittedAt,
            PlannerSummary: "Planner reviewed collected evidence.",
            RiskFactors: ["High data severity."],
            Limitations: ["Human approval required."]
        );
    }

    private sealed class FakeChatCompletionService : IChatCompletionService
    {
        private readonly string? _content;
        private readonly Exception? _exception;

        public FakeChatCompletionService(
            string content)
        {
            _content = content;
        }

        public FakeChatCompletionService(
            Exception exception)
        {
            _exception = exception;
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

            if (_exception is not null)
            {
                return Task.FromException<IReadOnlyList<ChatMessageContent>>(_exception);
            }

            IReadOnlyList<ChatMessageContent> response =
            [
                new ChatMessageContent(
                    AuthorRole.Assistant,
                    _content ?? string.Empty
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

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<CapturedLogEntry> _entries = [];

        public IReadOnlyList<CapturedLogEntry> Entries => _entries;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values
                    .Where(pair => pair.Key != "{OriginalFormat}")
                    .ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();

            _entries.Add(new CapturedLogEntry(
                logLevel,
                formatter(state, exception),
                properties
            ));
        }
    }

    private sealed record CapturedLogEntry(
        LogLevel Level,
        string Message,
        IReadOnlyDictionary<string, object?> Properties
    );

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
