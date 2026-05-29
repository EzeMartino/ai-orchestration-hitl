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
    ? "Razonamiento LLM utilizado"
    : planner.usedFallback
      ? "Alternativa determinista"
      : "Metadatos de razonamiento no disponibles";

  return (
    <section className="plannerPanel">
      <div className="plannerHeader">
        <div>
          <p className="plannerEyebrow">Revisión del Planificador</p>
          <h2>Resumen de razonamiento controlado</h2>
          <p>{planner.summary}</p>

          {planner.engine && (
            <div className="engineBadge">
              Motor del planificador: <strong>{planner.engine}</strong>
            </div>
          )}
        </div>
      </div>

      <div className="plannerMetaGrid">
        <div>
          <span>Estado del LLM</span>
          <strong>{llmStatus}</strong>
        </div>

        <div>
          <span>Proveedor</span>
          <strong>{planner.provider ?? "None"}</strong>
        </div>

        <div>
          <span>Modelo</span>
          <strong>{planner.model ?? "None"}</strong>
        </div>
      </div>

      {planner.failureReason && (
        <div className="plannerFallbackWarning">
          <strong>Alternativa de LLM utilizada</strong>
          <p>{planner.failureReason}</p>
        </div>
      )}

      <div className="plannerGrid">
        <PlannerList
          title="Acciones recomendadas"
          items={planner.recommendedActions}
        />
        <PlannerList title="Factores de riesgo" items={planner.riskFactors} />
        <PlannerList title="Limitaciones" items={planner.limitations} />
      </div>
    </section>
  );
}

export default PlannerPanel;
