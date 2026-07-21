using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Orchestration.Application.Agents.Planner.ToolCalling;
using Orchestration.Infrastructure.Agents.Planner.Reasoning;

namespace Orchestration.Infrastructure.Agents.Planner.ToolCalling;

public sealed class SemanticKernelToolPlanProposalService : IToolPlanProposalService
{
    private const string SubmittedAtArgumentName = "submittedAt";
    private const string SubmittedAtCanonicalizedFailureCode =
        "TOOL_PLAN_SUBMITTED_AT_CANONICALIZED";

    private const string SystemPrompt = """
You are a tool planning assistant inside a human-supervised financial analysis workflow.

You may only propose read-only analysis or retrieval tools.

You must not approve, reject, complete, fail, transition workflow state, move funds, block accounts, freeze accounts, or provide legal conclusions.

Only propose tools listed in the availableTools catalog supplied by the user message.
Do not exceed maxToolCalls.
Do not invent tool names or arguments outside the supplied schemas.

Forbidden tools include:
- workflow.complete
- workflow.fail
- workflow.transition
- approval.approve_session
- approval.reject_session
- money.move
- account.freeze
- transaction.block
- legal.determine_violation
- legal.issue_advice
- system.execute_command
- database.raw_query

Write the reason field in Spanish.
Use concise Spanish suitable for an audit trail.
Return JSON only.
Do not include markdown.
Do not include code fences.

Return JSON only with this shape:
{
  "proposedCalls": [
    {
      "toolName": "...",
      "arguments": {},
      "reason": "..."
    }
  ]
}

If no tool is appropriate, return:
{
  "proposedCalls": []
}
""";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ToolCallingOptions _toolCallingOptions;
    private readonly Func<ToolPlanProposalInput, CancellationToken, Task<ToolPlan>> _fallbackProposal;
    private readonly SemanticKernelToolPlanResponseParser _parser;
    private readonly ILogger<SemanticKernelToolPlanProposalService> _logger;
    private readonly Kernel? _kernel;
    private readonly IChatCompletionService? _chatCompletionService;
    private readonly ToolPlanProposalFallbackReason? _configurationFailure;

    public SemanticKernelToolPlanProposalService(
        IOptions<LlmOptions> llmOptions,
        IOptions<ToolCallingOptions> toolCallingOptions,
        DeterministicToolPlanProposalService fallback,
        SemanticKernelToolPlanResponseParser parser,
        ILogger<SemanticKernelToolPlanProposalService>? logger = null)
    {
        _toolCallingOptions = toolCallingOptions.Value;
        _fallbackProposal = fallback.ProposeAsync;
        _parser = parser;
        _logger = logger ?? NullLogger<SemanticKernelToolPlanProposalService>.Instance;

        var initialization = InitializeClient(llmOptions.Value);
        _kernel = initialization.Kernel;
        _chatCompletionService = initialization.ChatCompletionService;
        _configurationFailure = initialization.Failure;
    }

    internal SemanticKernelToolPlanProposalService(
        ToolCallingOptions toolCallingOptions,
        DeterministicToolPlanProposalService fallback,
        SemanticKernelToolPlanResponseParser parser,
        IChatCompletionService? chatCompletionService,
        ILogger<SemanticKernelToolPlanProposalService>? logger = null,
        Kernel? kernel = null,
        ToolPlanProposalFallbackReason? configurationFailure = null,
        Func<ToolPlanProposalInput, CancellationToken, Task<ToolPlan>>? fallbackProposal = null)
    {
        _toolCallingOptions = toolCallingOptions;
        _fallbackProposal = fallbackProposal ?? fallback.ProposeAsync;
        _parser = parser;
        _chatCompletionService = chatCompletionService;
        _logger = logger ?? NullLogger<SemanticKernelToolPlanProposalService>.Instance;
        _kernel = kernel;
        _configurationFailure = configurationFailure ??
            (chatCompletionService is null
                ? ToolPlanProposalFallbackReason.LlmConfigurationFailed
                : null);
    }

    public async Task<ToolPlan> ProposeAsync(
        ToolPlanProposalInput input,
        CancellationToken cancellationToken)
    {
        if (!_toolCallingOptions.Enabled)
        {
            return new ToolPlan([]);
        }

        var configurationFailure = _configurationFailure;

        if (configurationFailure is not null ||
            _chatCompletionService is null)
        {
            return await CreateFallbackPlanAsync(
                input,
                configurationFailure ?? ToolPlanProposalFallbackReason.LlmConfigurationFailed,
                cancellationToken
            );
        }

        var history = new ChatHistory();
        history.AddSystemMessage(SystemPrompt);
        history.AddUserMessage(BuildUserPrompt(input, _toolCallingOptions));

        ChatMessageContent response;
        try
        {
            response = await _chatCompletionService.GetChatMessageContentAsync(
                history,
                kernel: _kernel,
                cancellationToken: cancellationToken
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return await CreateFallbackPlanAsync(
                input,
                ToolPlanProposalFallbackReason.LlmRequestFailed,
                cancellationToken
            );
        }

        return _parser.TryParse(response.Content, out var plan)
            ? CanonicalizeSubmittedAt(plan, input)
            : await CreateFallbackPlanAsync(
                input,
                ToolPlanProposalFallbackReason.LlmResponseInvalid,
                cancellationToken
            );
    }

    private async Task<ToolPlan> CreateFallbackPlanAsync(
        ToolPlanProposalInput input,
        ToolPlanProposalFallbackReason reason,
        CancellationToken cancellationToken)
    {
        var fallbackPlan = await _fallbackProposal(
            input,
            cancellationToken
        );

        return fallbackPlan with
        {
            ProposalSource = ToolPlanProposalSource.DeterministicFallback,
            ProposalFallbackReason = reason
        };
    }

    private static ClientInitialization InitializeClient(
        LlmOptions options)
    {
        try
        {
            var kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.AddOpenAIChatCompletion(
                modelId: options.Model,
                apiKey: options.ApiKey,
                serviceId: options.ServiceId
            );

            var kernel = kernelBuilder.Build();
            var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>(
                options.ServiceId
            );

            return new ClientInitialization(
                kernel,
                chatCompletionService,
                Failure: null
            );
        }
        catch
        {
            return new ClientInitialization(
                Kernel: null,
                ChatCompletionService: null,
                ToolPlanProposalFallbackReason.LlmConfigurationFailed
            );
        }
    }

    private ToolPlan CanonicalizeSubmittedAt(
        ToolPlan plan,
        ToolPlanProposalInput input)
    {
        ProposedToolCall[]? canonicalizedCalls = null;
        var canonicalValue = input.SubmittedAt.ToString("O", CultureInfo.InvariantCulture);

        for (var index = 0; index < plan.ProposedCalls.Count; index++)
        {
            var call = plan.ProposedCalls[index];
            if (!string.Equals(
                    call.ToolName.Trim(),
                    PlannerToolCatalog.AnalyzeTransactionsName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var matchingArguments = call.Arguments
                .Where(argument => string.Equals(
                    argument.Key.Trim(),
                    SubmittedAtArgumentName,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var reason = GetCanonicalizationReason(matchingArguments, canonicalValue);
            if (reason is null)
            {
                continue;
            }

            var canonicalArguments = call.Arguments
                .Where(argument => !string.Equals(
                    argument.Key.Trim(),
                    SubmittedAtArgumentName,
                    StringComparison.OrdinalIgnoreCase))
                .ToDictionary(argument => argument.Key, argument => argument.Value);
            canonicalArguments[SubmittedAtArgumentName] = canonicalValue;

            canonicalizedCalls ??= plan.ProposedCalls.ToArray();
            canonicalizedCalls[index] = call with
            {
                Arguments = canonicalArguments
            };

            _logger.LogWarning(
                "Canonicalized planner submission timestamp. {SessionId} {ToolName} {FailureCode} {Reason}",
                input.SessionId,
                PlannerToolCatalog.AnalyzeTransactionsName,
                SubmittedAtCanonicalizedFailureCode,
                reason
            );
        }

        return canonicalizedCalls is null
            ? plan
            : plan with { ProposedCalls = canonicalizedCalls };
    }

    private static string? GetCanonicalizationReason(
        IReadOnlyList<KeyValuePair<string, string>> matchingArguments,
        string canonicalValue)
    {
        if (matchingArguments.Count == 0)
        {
            return "missing";
        }

        if (matchingArguments.Count > 1)
        {
            return "duplicate";
        }

        var argument = matchingArguments[0];
        if (!DateTimeOffset.TryParse(
                argument.Value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out _))
        {
            return "malformed";
        }

        return string.Equals(argument.Key, SubmittedAtArgumentName, StringComparison.Ordinal) &&
               string.Equals(argument.Value, canonicalValue, StringComparison.Ordinal)
            ? null
            : "mismatch";
    }

    private static string BuildUserPrompt(
        ToolPlanProposalInput input,
        ToolCallingOptions options)
    {
        var payload = new
        {
            input.SessionId,
            input.ReportName,
            totalAmount = input.TotalAmount.ToString(CultureInfo.InvariantCulture),
            transactionCount = input.TransactionCount.ToString(CultureInfo.InvariantCulture),
            submittedAt = input.SubmittedAt.ToString("O", CultureInfo.InvariantCulture),
            planner = new
            {
                summary = input.PlannerSummary,
                riskFactors = input.RiskFactors,
                limitations = input.Limitations
            },
            maxToolCalls = Math.Max(0, options.MaxToolCalls),
            availableTools = PlannerToolCatalog.GetAllowed(options)
                .Select(tool => new
                {
                    name = tool.Name,
                    description = tool.PromptDescription,
                    arguments = tool.Arguments.Select(argument => new
                    {
                        name = argument.Name,
                        type = argument.Type.ToString(),
                        required = argument.Required,
                        description = argument.Description
                    })
                }),
            forbiddenTools = new[]
            {
                "workflow.complete",
                "workflow.fail",
                "workflow.transition",
                "approval.approve_session",
                "approval.reject_session",
                "money.move",
                "account.freeze",
                "transaction.block",
                "legal.determine_violation",
                "legal.issue_advice",
                "system.execute_command",
                "database.raw_query"
            }
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private sealed record ClientInitialization(
        Kernel? Kernel,
        IChatCompletionService? ChatCompletionService,
        ToolPlanProposalFallbackReason? Failure);
}
