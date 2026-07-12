namespace Orchestration.Application.Agents.Planner.ToolCalling;

/// <summary>
/// Identifies the controlled executor handler for a planner tool.
/// </summary>
public enum PlannerToolHandler
{
    AnalyzeTransactions,
    SearchCnvRegulation
}

/// <summary>
/// Identifies the aggregate result produced by a planner tool.
/// </summary>
public enum PlannerToolResultKind
{
    DataAgent,
    LegalAgent
}

/// <summary>
/// Identifies the deterministic workflow stage that can satisfy a planner tool call.
/// </summary>
public enum PlannerToolSatisfactionKind
{
    DataAnalysis,
    LegalReview
}

/// <summary>
/// Identifies the serialized type expected for a planner tool argument.
/// </summary>
public enum PlannerToolArgumentType
{
    String,
    Guid,
    Decimal,
    Integer,
    DateTimeOffset,
    Boolean
}

/// <summary>
/// Describes one serialized planner tool argument.
/// </summary>
/// <param name="Name">Canonical argument name.</param>
/// <param name="Type">Expected serialized value type.</param>
/// <param name="Required">Whether the argument must be present and non-empty.</param>
/// <param name="Description">LLM-facing argument description.</param>
public sealed record PlannerToolArgumentDefinition(
    string Name,
    PlannerToolArgumentType Type,
    bool Required,
    string Description);

/// <summary>
/// Defines one read-only tool that the planner can safely propose and compose.
/// </summary>
/// <param name="Name">Canonical tool name.</param>
/// <param name="PromptDescription">LLM-facing description.</param>
/// <param name="Handler">Controlled execution handler.</param>
/// <param name="ResultKind">Aggregate result mapping strategy.</param>
/// <param name="SatisfactionKind">Deterministic workflow satisfaction policy.</param>
/// <param name="AuditActor">Actor name used in audit metadata.</param>
/// <param name="Arguments">Serialized argument schema.</param>
public sealed record PlannerToolDefinition(
    string Name,
    string PromptDescription,
    PlannerToolHandler Handler,
    PlannerToolResultKind ResultKind,
    PlannerToolSatisfactionKind SatisfactionKind,
    string AuditActor,
    IReadOnlyList<PlannerToolArgumentDefinition> Arguments);
