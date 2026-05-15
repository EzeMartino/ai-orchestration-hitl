using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Planner.Reasoning;
using Orchestration.Application.Agents.Planner.ToolCalling;

namespace Orchestration.Application.Agents.Planner;

public sealed record PlannerAgentResult(
    bool RequiresHumanApproval,
    string Summary,
    DataAgentResult DataResult,
    LegalAgentResult LegalResult,
    PlannerReasoningResult ReasoningResult,
    ToolPlanAuditResult ToolPlan
);
