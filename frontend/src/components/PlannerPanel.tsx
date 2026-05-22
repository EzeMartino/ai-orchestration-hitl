import type { PlannerContext } from "../types/domain.types";

interface PlannerPanelProps {
  planner?: PlannerContext;
}

export function PlannerList({ title, items }: { title: string; items: string[] }) {
  if (items.length === 0) {
    return null;
  }

  return (
    <div className="plannerList">
      <strong>{title}</strong>
      <ul>
        {items.map((item) => (
          <li key={item}>{item}</li>
        ))}
      </ul>
    </div>
  );
}

export function PlannerPanel({ planner }: PlannerPanelProps) {
  if (!planner) {
    return null;
  }

  const llmStatus = planner.usedLlm
    ? "LLM reasoning used"
    : planner.usedFallback
      ? "Deterministic fallback"
      : "Reasoning metadata unavailable";

  return (
    <section className="plannerPanel">
      <div className="plannerHeader">
        <div>
          <p className="plannerEyebrow">Planner review</p>
          <h2>Controlled reasoning summary</h2>
          <p>{planner.summary}</p>

          {planner.engine && (
            <div className="engineBadge">
              Planner engine: <strong>{planner.engine}</strong>
            </div>
          )}
        </div>
      </div>

      <div className="plannerMetaGrid">
        <div>
          <span>LLM status</span>
          <strong>{llmStatus}</strong>
        </div>

        <div>
          <span>Provider</span>
          <strong>{planner.provider ?? "None"}</strong>
        </div>

        <div>
          <span>Model</span>
          <strong>{planner.model ?? "None"}</strong>
        </div>
      </div>

      {planner.failureReason && (
        <div className="plannerFallbackWarning">
          <strong>LLM fallback used</strong>
          <p>{planner.failureReason}</p>
        </div>
      )}

      <div className="plannerGrid">
        <PlannerList
          title="Recommended actions"
          items={planner.recommendedActions}
        />
        <PlannerList title="Risk factors" items={planner.riskFactors} />
        <PlannerList title="Limitations" items={planner.limitations} />
      </div>
    </section>
  );
}

export default PlannerPanel;
