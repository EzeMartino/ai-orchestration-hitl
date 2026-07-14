using System.Text;

namespace Orchestration.Application.Agents.Planner.ToolCalling;

public sealed class ToolPlanNormalizer : IToolPlanNormalizer
{
    public ToolPlan Normalize(
        ToolPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedCalls = new List<ProposedToolCall>();

        foreach (var proposedCall in plan.ProposedCalls)
        {
            var toolName = proposedCall.ToolName?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(toolName))
            {
                continue;
            }

            var arguments = NormalizeArguments(proposedCall.Arguments);
            var deduplicationKey = CreateDeduplicationKey(toolName, arguments);

            if (!seenKeys.Add(deduplicationKey))
            {
                continue;
            }

            normalizedCalls.Add(new ProposedToolCall(
                ToolName: toolName,
                Arguments: arguments,
                Reason: proposedCall.Reason?.Trim() ?? string.Empty
            ));
        }

        return plan with { ProposedCalls = normalizedCalls };
    }

    private static IReadOnlyDictionary<string, string> NormalizeArguments(
        IReadOnlyDictionary<string, string>? arguments)
    {
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (arguments is null)
        {
            return normalized;
        }

        foreach (var argument in arguments)
        {
            var key = argument.Key?.Trim();

            if (key is null || normalized.ContainsKey(key))
            {
                continue;
            }

            normalized[key] = argument.Value?.Trim() ?? string.Empty;
        }

        return normalized;
    }

    private static string CreateDeduplicationKey(
        string toolName,
        IReadOnlyDictionary<string, string> arguments)
    {
        var builder = new StringBuilder(toolName.Trim());

        foreach (var argument in arguments.OrderBy(argument => argument.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder
                .Append('|')
                .Append(argument.Key.Trim())
                .Append('=')
                .Append(argument.Value.Trim());
        }

        return builder.ToString();
    }
}
