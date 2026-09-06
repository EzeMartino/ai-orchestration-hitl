using System.Text.Json.Serialization;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Legal;

namespace Orchestration.Application.Agents.Planner.ToolCalling;

public sealed record ToolExecutionResult(
    string ToolName,
    ToolExecutionStatus Status,
    bool Succeeded,
    string Summary,
    string Engine,
    string OutputJson,
    string? Error
)
{
    [JsonIgnore]
    public DataAgentResult? TypedDataResult { get; init; }

    [JsonIgnore]
    public LegalAgentResult? TypedLegalResult { get; init; }
}
