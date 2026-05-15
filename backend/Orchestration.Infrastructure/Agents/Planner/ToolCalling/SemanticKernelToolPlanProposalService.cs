using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Orchestration.Application.Agents.Planner.ToolCalling;
using Orchestration.Infrastructure.Agents.Planner.Reasoning;

namespace Orchestration.Infrastructure.Agents.Planner.ToolCalling;

public sealed class SemanticKernelToolPlanProposalService : IToolPlanProposalService
{
    private const string SystemPrompt = """
You are a tool planning assistant inside a human-supervised financial analysis workflow.

You may only propose read-only analysis or retrieval tools.

You must not approve, reject, complete, fail, transition workflow state, move funds, block accounts, freeze accounts, or provide legal conclusions.

Allowed tools:
- data.analyze_transactions
- legal.search_cnv_regulation

Propose at most 2 tool calls.
Prefer:
1. data.analyze_transactions
2. legal.search_cnv_regulation

Do not propose multiple legal.search_cnv_regulation calls unless there is a clearly different regulatory search intent.

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

For legal.search_cnv_regulation, prefer short Spanish CNV search queries such as "agentes", "fondos comunes", "custodia", "registro" or "regimen informativo". Do not generate long multi-term legal queries.

Write the reason field in Spanish.
Use concise Spanish suitable for an audit trail.
Return JSON only.
Do not include markdown.
Do not include code fences.

Return JSON only with this shape:
{
  "proposedCalls": [
    {
      "toolName": "data.analyze_transactions",
      "arguments": {
        "sessionId": "...",
        "reportName": "...",
        "totalAmount": "...",
        "transactionCount": "...",
        "submittedAt": "..."
      },
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
    private readonly DeterministicToolPlanProposalService _fallback;
    private readonly SemanticKernelToolPlanResponseParser _parser;
    private readonly Kernel? _kernel;
    private readonly IChatCompletionService _chatCompletionService;

    public SemanticKernelToolPlanProposalService(
        IOptions<LlmOptions> llmOptions,
        IOptions<ToolCallingOptions> toolCallingOptions,
        DeterministicToolPlanProposalService fallback,
        SemanticKernelToolPlanResponseParser parser)
    {
        _toolCallingOptions = toolCallingOptions.Value;
        _fallback = fallback;
        _parser = parser;

        var options = llmOptions.Value;
        var kernelBuilder = Kernel.CreateBuilder();
        kernelBuilder.AddOpenAIChatCompletion(
            modelId: options.Model,
            apiKey: options.ApiKey,
            serviceId: options.ServiceId
        );

        _kernel = kernelBuilder.Build();
        _chatCompletionService = _kernel.GetRequiredService<IChatCompletionService>(
            options.ServiceId
        );
    }

    internal SemanticKernelToolPlanProposalService(
        ToolCallingOptions toolCallingOptions,
        DeterministicToolPlanProposalService fallback,
        SemanticKernelToolPlanResponseParser parser,
        IChatCompletionService chatCompletionService)
    {
        _toolCallingOptions = toolCallingOptions;
        _fallback = fallback;
        _parser = parser;
        _chatCompletionService = chatCompletionService;
    }

    public async Task<ToolPlan> ProposeAsync(
        ToolPlanProposalInput input,
        CancellationToken cancellationToken)
    {
        if (!_toolCallingOptions.Enabled)
        {
            return new ToolPlan([]);
        }

        try
        {
            var history = new ChatHistory();
            history.AddSystemMessage(SystemPrompt);
            history.AddUserMessage(BuildUserPrompt(input));

            var response = await _chatCompletionService.GetChatMessageContentAsync(
                history,
                kernel: _kernel,
                cancellationToken: cancellationToken
            );

            return _parser.TryParse(response.Content, out var plan)
                ? plan
                : await CreateFallbackPlanAsync(input, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return await CreateFallbackPlanAsync(input, cancellationToken);
        }
    }

    private Task<ToolPlan> CreateFallbackPlanAsync(
        ToolPlanProposalInput input,
        CancellationToken cancellationToken)
    {
        return _fallback.ProposeAsync(
            input,
            cancellationToken
        );
    }

    private static string BuildUserPrompt(
        ToolPlanProposalInput input)
    {
        var payload = new
        {
            input.SessionId,
            input.ReportName,
            totalAmount = input.TotalAmount.ToString(CultureInfo.InvariantCulture),
            transactionCount = input.TransactionCount.ToString(CultureInfo.InvariantCulture),
            planner = new
            {
                summary = input.PlannerSummary,
                riskFactors = input.RiskFactors,
                limitations = input.Limitations
            },
            allowedTools = new[]
            {
                "data.analyze_transactions",
                "legal.search_cnv_regulation"
            },
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
}
