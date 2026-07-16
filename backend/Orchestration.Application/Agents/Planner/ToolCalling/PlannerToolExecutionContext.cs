using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Legal.Cnv;
using Orchestration.Application.Agents.Shared;

namespace Orchestration.Application.Agents.Planner.ToolCalling;

public sealed record PlannerToolExecutionContext(
    FinancialReportContext Report,
    DataAgentResult DataResult,
    LegalDataEvidenceContext DataEvidence
);
