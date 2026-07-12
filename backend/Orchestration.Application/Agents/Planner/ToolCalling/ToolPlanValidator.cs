using System.Globalization;

namespace Orchestration.Application.Agents.Planner.ToolCalling;

public sealed class ToolPlanValidator : IToolPlanValidator
{
    private const string NotAllowlistedReason = "La herramienta no está permitida.";
    private const string WorkflowReason = "No se permiten herramientas de transición de workflow.";
    private const string ApprovalReason = "El LLM no puede llamar herramientas de aprobación humana.";
    private const string OperationalReason = "No se permiten herramientas financieras operativas.";
    private const string LegalConclusionReason = "No se permiten herramientas de conclusión legal.";
    private const string MaxToolCallsReason = "Se excedió la cantidad máxima de llamadas a herramientas.";
    private readonly HashSet<string> _allowedTools;
    private readonly int _maxToolCalls;

    public ToolPlanValidator(
        ToolCallingOptions? options = null)
    {
        var resolvedOptions = options ?? new ToolCallingOptions();

        _allowedTools = new HashSet<string>(
            resolvedOptions.AllowedTools
                .Where(tool => !string.IsNullOrWhiteSpace(tool)),
            StringComparer.OrdinalIgnoreCase
        );

        _maxToolCalls = Math.Max(0, resolvedOptions.MaxToolCalls);
    }

    public ToolValidationResult Validate(
        ToolPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var approvedCalls = new List<ApprovedToolCall>();
        var rejectedCalls = new List<RejectedToolCall>();

        for (var index = 0; index < plan.ProposedCalls.Count; index++)
        {
            var proposedCall = plan.ProposedCalls[index];
            var toolName = proposedCall.ToolName;

            if (index >= _maxToolCalls)
            {
                rejectedCalls.Add(new RejectedToolCall(toolName, MaxToolCallsReason));
                continue;
            }

            var rejectionReason = GetRejectionReason(proposedCall);

            if (rejectionReason is not null)
            {
                rejectedCalls.Add(new RejectedToolCall(toolName, rejectionReason));
                continue;
            }

            approvedCalls.Add(new ApprovedToolCall(
                toolName,
                proposedCall.Arguments,
                proposedCall.Reason
            ));
        }

        return new ToolValidationResult(
            IsValid: rejectedCalls.Count == 0,
            ApprovedCalls: approvedCalls,
            RejectedCalls: rejectedCalls
        );
    }

    private string? GetRejectionReason(
        ProposedToolCall proposedCall)
    {
        var toolName = proposedCall.ToolName;

        if (string.IsNullOrWhiteSpace(toolName))
        {
            return NotAllowlistedReason;
        }

        if (toolName.StartsWith("workflow.", StringComparison.OrdinalIgnoreCase))
        {
            return WorkflowReason;
        }

        if (toolName.StartsWith("approval.", StringComparison.OrdinalIgnoreCase))
        {
            return ApprovalReason;
        }

        if (toolName.StartsWith("money.", StringComparison.OrdinalIgnoreCase) ||
            toolName.StartsWith("account.", StringComparison.OrdinalIgnoreCase) ||
            toolName.StartsWith("transaction.", StringComparison.OrdinalIgnoreCase))
        {
            return OperationalReason;
        }

        if (string.Equals(toolName, "legal.determine_violation", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(toolName, "legal.issue_advice", StringComparison.OrdinalIgnoreCase))
        {
            return LegalConclusionReason;
        }

        var definition = PlannerToolCatalog.Find(toolName);

        if (definition is null || !_allowedTools.Contains(definition.Name))
        {
            return NotAllowlistedReason;
        }

        foreach (var argument in definition.Arguments.Where(argument => argument.Required))
        {
            if (!TryFindArgument(proposedCall.Arguments, argument.Name, out var value) ||
                string.IsNullOrWhiteSpace(value))
            {
                return $"Falta el argumento obligatorio: {argument.Name}.";
            }
        }

        foreach (var argument in proposedCall.Arguments)
        {
            var argumentDefinition = definition.Arguments.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, argument.Key, StringComparison.OrdinalIgnoreCase));

            if (argumentDefinition is null)
            {
                return $"Argumento no permitido: {argument.Key}.";
            }

            if (!string.IsNullOrWhiteSpace(argument.Value) &&
                !HasValidType(argument.Value, argumentDefinition.Type))
            {
                return $"Argumento con formato no válido: {argumentDefinition.Name}.";
            }
        }

        return null;
    }

    private static bool TryFindArgument(
        IReadOnlyDictionary<string, string> arguments,
        string key,
        out string value)
    {
        foreach (var argument in arguments)
        {
            if (string.Equals(argument.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = argument.Value;

                return true;
            }
        }

        value = "";

        return false;
    }

    private static bool HasValidType(
        string value,
        PlannerToolArgumentType type)
    {
        return type switch
        {
            PlannerToolArgumentType.String => true,
            PlannerToolArgumentType.Guid => Guid.TryParse(value, out _),
            PlannerToolArgumentType.Decimal => decimal.TryParse(
                value,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out _),
            PlannerToolArgumentType.Integer => int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out _),
            PlannerToolArgumentType.DateTimeOffset => DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out _),
            PlannerToolArgumentType.Boolean => bool.TryParse(value, out _),
            _ => false
        };
    }
}
