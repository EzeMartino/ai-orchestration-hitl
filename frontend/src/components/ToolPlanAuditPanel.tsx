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
  if (status === "Failed" || succeeded === false) {
    return "danger";
  }

  if (status === "Executed") {
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
          <p className="toolPlanEyebrow">Tool plan audit</p>
          <h2>Controlled tool calling trail</h2>
          <p>Proposed, validated, rejected, and execution-policy decisions.</p>
        </div>
      </div>

      <div className="toolPlanGrid">
        <ToolCallGroup
          title="Proposed"
          tone="neutral"
          calls={toolPlan.proposedCalls.map((call) => ({
            toolName: call.toolName,
            detail: call.reason,
          }))}
        />
        <ToolCallGroup
          title="Approved"
          tone="success"
          calls={toolPlan.approvedCalls.map((call) => ({
            toolName: call.toolName,
            detail: call.reason,
          }))}
        />
        <ToolCallGroup
          title="Rejected"
          tone="warning"
          calls={toolPlan.rejectedCalls.map((call) => ({
            toolName: call.toolName,
            detail: call.reason,
          }))}
        />
        <ToolCallGroup
          title="Execution audit"
          tone="neutral"
          calls={toolPlan.executedCalls.map((call) => ({
            toolName: call.toolName,
            detail: call.error ?? call.summary,
            meta: `${call.status ?? (call.succeeded ? "Executed" : "Failed")} - ${call.engine}`,
            status: call.status ?? (call.succeeded ? "Executed" : "Failed"),
            statusTone: getToolExecutionTone(call.status, call.succeeded),
          }))}
        />
      </div>
    </section>
  );
}

export default ToolPlanAuditPanel;
