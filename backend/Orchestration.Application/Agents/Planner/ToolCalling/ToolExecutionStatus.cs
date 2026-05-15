using System.Text.Json.Serialization;

namespace Orchestration.Application.Agents.Planner.ToolCalling;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ToolExecutionStatus
{
    Executed,
    SkippedAlreadySatisfied,
    SkippedDisabled,
    Failed
}
