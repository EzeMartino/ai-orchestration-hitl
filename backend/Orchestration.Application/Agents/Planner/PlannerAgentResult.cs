using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Planner.Reasoning;

namespace Orchestration.Application.Agents.Planner;

public sealed record PlannerAgentResult(
    bool RequiresHumanApproval,
    string Summary,
    DataAgentResult DataResult,
    LegalAgentResult LegalResult,
    PlannerReasoningResult ReasoningResult
);
