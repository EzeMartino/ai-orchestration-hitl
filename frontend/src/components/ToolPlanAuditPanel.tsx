import type { ToolPlanContext } from "../types/domain.types";

interface ToolPlanAuditPanelProps {
  toolPlan?: ToolPlanContext;
}

export function ToolCallGroup({
  title,
  tone,
  calls,
}: {
  title: string;
  tone: "neutral" | "success" | "warning" | "danger";
  calls: {
    toolName: string;
    detail: string;
    meta?: string;
    status?: string;
    statusTone?: "neutral" | "success" | "warning" | "danger";
  }[];
}) {
  if (calls.length === 0) {
    return null;
  }

  return (
    <div className={`toolCallGroup toolCallGroup-${tone}`}>
      <strong>{title}</strong>

      <div className="toolCallList">
        {calls.map((call, index) => (
          <article className="toolCallCard" key={`${call.toolName}-${index}`}>
            <span>{call.toolName}</span>
            {call.status && (
              <em className={`toolCallStatus toolCallStatus-${call.statusTone ?? "neutral"}`}>
                {call.status}
              </em>
            )}
            {call.meta && <small>{call.meta}</small>}
            <p>{call.detail}</p>
          </article>
        ))}
      </div>
    </div>
  );
}

function getToolExecutionTone(
  status?: string,
  succeeded?: boolean
): "neutral" | "success" | "warning" | "danger" {
  if (status === "Failed" || status === "Fallida" || succeeded === false) {
    return "danger";
  }

  if (status === "Executed" || status === "Ejecutada") {
    return "success";
  }

  if (status === "SkippedDisabled") {
    return "warning";
  }

  return "neutral";
}

export function ToolPlanAuditPanel({ toolPlan }: ToolPlanAuditPanelProps) {
  const hasToolPlan =
    toolPlan &&
    (toolPlan.proposedCalls.length > 0 ||
      toolPlan.approvedCalls.length > 0 ||
      toolPlan.rejectedCalls.length > 0 ||
      toolPlan.executedCalls.length > 0);

  if (!hasToolPlan) {
    return null;
  }

  return (
    <section className="toolPlanPanel">
      <div className="toolPlanHeader">
        <div>
          <p className="toolPlanEyebrow">Auditoría del plan de herramientas</p>
          <h2>Seguimiento de llamadas a herramientas</h2>
          <p>Decisiones de herramientas propuestas, validadas, rechazadas y de políticas de ejecución.</p>
        </div>
      </div>

      <div className="toolPlanGrid">
        <ToolCallGroup
          title="Propuestas"
          tone="neutral"
          calls={toolPlan.proposedCalls.map((call) => ({
            toolName: call.toolName,
            detail: call.reason,
          }))}
        />
        <ToolCallGroup
          title="Aprobadas"
          tone="success"
          calls={toolPlan.approvedCalls.map((call) => ({
            toolName: call.toolName,
            detail: call.reason,
          }))}
        />
        <ToolCallGroup
          title="Rechazadas"
          tone="warning"
          calls={toolPlan.rejectedCalls.map((call) => ({
            toolName: call.toolName,
            detail: call.reason,
          }))}
        />
        <ToolCallGroup
          title="Auditoría de ejecución"
          tone="neutral"
          calls={toolPlan.executedCalls.map((call) => ({
            toolName: call.toolName,
            detail: call.error ?? call.summary,
            meta: `${call.status ?? (call.succeeded ? "Ejecutada" : "Fallida")} - ${call.engine}`,
            status: call.status ?? (call.succeeded ? "Ejecutada" : "Fallida"),
            statusTone: getToolExecutionTone(call.status, call.succeeded),
          }))}
        />
      </div>
    </section>
  );
}

export default ToolPlanAuditPanel;
