using System.Text.Json;
using Orchestration.Application.Agents.Planner.ToolCalling;

namespace Orchestration.Infrastructure.Agents.Planner.ToolCalling;

public sealed class SemanticKernelToolPlanResponseParser
{
    private const string FallbackReason = "LLM proposed this read-only tool call.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public ToolPlan ParseOrFallback(
        string? rawResponse)
    {
        return TryParse(rawResponse, out var plan)
            ? plan
            : new ToolPlan([]);
    }

    public bool TryParse(
        string? rawResponse,
        out ToolPlan plan)
    {
        plan = new ToolPlan([]);

        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return false;
        }

        var json = ExtractJson(rawResponse);

        try
        {
            var parsed = JsonSerializer.Deserialize<LlmToolPlanResponse>(
                json,
                JsonOptions
            );

            if (parsed is null || parsed.ProposedCalls is null)
            {
                return false;
            }

            plan = new ToolPlan(
                parsed.ProposedCalls
                    .Where(call => !string.IsNullOrWhiteSpace(call.ToolName))
                    .Select(call => new ProposedToolCall(
                        ToolName: call.ToolName,
                        Arguments: call.Arguments ?? new Dictionary<string, string>(),
                        Reason: string.IsNullOrWhiteSpace(call.Reason)
                            ? FallbackReason
                            : call.Reason
                    ))
                    .ToList()
            );

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string ExtractJson(
        string content)
    {
        var trimmed = content.Trim();

        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstLineEnd = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);

        if (firstLineEnd < 0 || lastFence <= firstLineEnd)
        {
            return trimmed;
        }

        return trimmed[(firstLineEnd + 1)..lastFence].Trim();
    }

    private sealed record LlmToolPlanResponse(
        IReadOnlyList<LlmProposedToolCall>? ProposedCalls
    );

    private sealed record LlmProposedToolCall(
        string ToolName,
        Dictionary<string, string>? Arguments,
        string? Reason
    );
}
