using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Legal;

namespace Orchestration.Application.Agents.Planner.ToolCalling.Mapping;

public interface IToolExecutionResultMapper
{
    DataAgentResult? TryMapDataResult(
        IReadOnlyList<ToolExecutionResult> executedCalls);

    LegalAgentResult? TryMapLegalResult(
        IReadOnlyList<ToolExecutionResult> executedCalls);
}
