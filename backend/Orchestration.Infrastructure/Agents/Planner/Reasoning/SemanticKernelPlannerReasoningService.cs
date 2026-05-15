using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI.Chat;
using Orchestration.Application.Agents.Planner.Reasoning;

namespace Orchestration.Infrastructure.Agents.Planner.Reasoning;

public sealed class SemanticKernelPlannerReasoningService : IPlannerReasoningService
{
    private const string SystemPrompt = """
You are a planning assistant inside a human-supervised financial analysis workflow.

You do not approve, reject, block, freeze, move funds, or make operational decisions.

Your job is to summarize collected evidence from specialized tools:
- DataAgent: statistical anomaly evidence.
- LegalAgent: regulatory retrieval evidence.

You must be explicit about limitations.
You must not provide legal, financial, or investment advice.
You must not claim that a regulation was violated.
You may only say that evidence suggests human review is required.

Return valid JSON only.
Do not include markdown.
Do not include code fences.
Do not include explanatory text outside JSON.

Return this JSON shape:
{
  "summary": "string",
  "recommendedActions": ["string"],
  "riskFactors": ["string"],
  "limitations": ["string"]
}
""";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly LlmOptions _options;
    private readonly DeterministicPlannerReasoningService _fallback;
    private readonly Kernel? _kernel;
    private readonly IChatCompletionService _chatCompletionService;

    public SemanticKernelPlannerReasoningService(
        IOptions<LlmOptions> options,
        DeterministicPlannerReasoningService fallback)
    {
        _options = options.Value;
        _fallback = fallback;

        var kernelBuilder = Kernel.CreateBuilder();
        kernelBuilder.AddOpenAIChatCompletion(
            modelId: _options.Model,
            apiKey: _options.ApiKey,
            serviceId: _options.ServiceId
        );

        _kernel = kernelBuilder.Build();
        _chatCompletionService = _kernel.GetRequiredService<IChatCompletionService>(
            _options.ServiceId
        );
    }

    internal SemanticKernelPlannerReasoningService(
        LlmOptions options,
        DeterministicPlannerReasoningService fallback,
        IChatCompletionService chatCompletionService)
    {
        _options = options;
        _fallback = fallback;
        _chatCompletionService = chatCompletionService;
    }

    public async Task<PlannerReasoningResult> GenerateReasoningAsync(
        PlannerReasoningInput input,
        CancellationToken cancellationToken)
    {
        var engine = $"Semantic Kernel + {ProviderName}/{_options.Model}";

        try
        {
            var history = new ChatHistory();
            history.AddSystemMessage(SystemPrompt);
            history.AddUserMessage(BuildUserPrompt(input));

            var executionSettings = new OpenAIPromptExecutionSettings
            {
                ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
            };

            var response = await _chatCompletionService.GetChatMessageContentAsync(
                history,
                executionSettings,
                kernel: _kernel,
                cancellationToken: cancellationToken
            );

            return TryParseResponse(
                response.Content,
                engine,
                ProviderName,
                _options.Model
            ) ?? await CreateFallbackResultAsync(
                input,
                "LLM returned invalid JSON.",
                cancellationToken
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            return await CreateFallbackResultAsync(
                input,
                SanitizeFailure(ex),
                cancellationToken
            );
        }
        catch (Exception ex)
        {
            return await CreateFallbackResultAsync(
                input,
                SanitizeFailure(ex),
                cancellationToken
            );
        }
    }

    internal static PlannerReasoningResult? TryParseResponse(
        string? content,
        string engine,
        string provider,
        string model)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var json = ExtractJson(content);

        try
        {
            var parsed = JsonSerializer.Deserialize<PlannerReasoningResponse>(
                json,
                JsonOptions
            );

            if (parsed is null || string.IsNullOrWhiteSpace(parsed.Summary))
            {
                return null;
            }

            return new PlannerReasoningResult(
                Engine: engine,
                Summary: parsed.Summary,
                RecommendedActions: parsed.RecommendedActions.WhereNotBlank(),
                RiskFactors: parsed.RiskFactors.WhereNotBlank(),
                Limitations: parsed.Limitations.WhereNotBlank(),
                UsedLlm: true,
                UsedFallback: false,
                Provider: provider,
                Model: model,
                FailureReason: null
            );
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string SanitizeFailure(Exception ex)
    {
        return ex switch
        {
            JsonException => "LLM returned invalid JSON.",
            OperationCanceledException => "LLM request was canceled.",
            _ => "LLM reasoning failed and deterministic fallback was used."
        };
    }

    private async Task<PlannerReasoningResult> CreateFallbackResultAsync(
        PlannerReasoningInput input,
        string failureReason,
        CancellationToken cancellationToken)
    {
        var fallback = await _fallback.GenerateReasoningAsync(
            input,
            cancellationToken
        );

        return ApplyFallbackMetadata(
            fallback,
            _options,
            failureReason
        );
    }

    internal static PlannerReasoningResult ApplyFallbackMetadata(
        PlannerReasoningResult fallback,
        LlmOptions options,
        string failureReason)
    {
        return fallback with
        {
            UsedLlm = false,
            UsedFallback = true,
            Provider = NormalizeProvider(options.Provider),
            Model = options.Model,
            FailureReason = failureReason
        };
    }

    private string ProviderName =>
        NormalizeProvider(_options.Provider);

    private static string NormalizeProvider(string provider)
    {
        return string.Equals(provider, "OpenAI", StringComparison.OrdinalIgnoreCase)
            ? "OpenAI"
            : provider;
    }

    private static string ExtractJson(string content)
    {
        var trimmed = content.Trim();

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);

            if (firstLineEnd >= 0 && lastFence > firstLineEnd)
            {
                trimmed = trimmed[(firstLineEnd + 1)..lastFence].Trim();
            }
        }

        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            return trimmed;
        }

        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');

        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            return trimmed[firstBrace..(lastBrace + 1)].Trim();
        }

        return trimmed;
    }

    private static string BuildUserPrompt(PlannerReasoningInput input)
    {
        var payload = new
        {
            input.SessionId,
            input.ReportName,
            input.TotalAmount,
            input.TransactionCount,
            dataAgent = new
            {
                summary = input.DataSummary,
                severity = input.DataSeverity,
                engine = input.DataEngine,
                evidence = input.DataEvidence
            },
            legalAgent = new
            {
                summary = input.LegalSummary,
                riskLevel = input.LegalRiskLevel,
                engine = input.LegalEngine,
                evidence = input.LegalEvidence,
                warnings = input.LegalWarnings
            }
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private sealed record PlannerReasoningResponse(
        string Summary,
        IReadOnlyList<string>? RecommendedActions,
        IReadOnlyList<string>? RiskFactors,
        IReadOnlyList<string>? Limitations
    );
}

file static class PlannerReasoningResponseExtensions
{
    public static IReadOnlyList<string> WhereNotBlank(
        this IReadOnlyList<string>? values)
    {
        return values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList() ?? [];
    }
}
