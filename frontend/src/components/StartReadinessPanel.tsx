import type { AnalysisSessionStartPreflightResult } from "../types/domain.types";

interface StartReadinessPanelProps {
  sessionId?: string;
  preflight: AnalysisSessionStartPreflightResult | null;
  isChecking: boolean;
  error: string | null;
}

export function StartReadinessPanel({
  sessionId,
  preflight,
  isChecking,
  error,
}: StartReadinessPanelProps) {
  if (!sessionId) {
    return (
      <section className="startReadinessPanel startReadiness-neutral">
        <div>
          <span>Preparación de inicio</span>
          <strong>Verificar preparación de inicio.</strong>
        </div>
      </section>
    );
  }

  if (isChecking && !preflight) {
    return (
      <section className="startReadinessPanel startReadiness-neutral">
        <div>
          <span>Preparación de inicio</span>
          <strong>Comprobando preparación...</strong>
        </div>
      </section>
    );
  }

  if (error && !preflight) {
    return (
      <section className="startReadinessPanel startReadiness-warning">
        <div>
          <span>Preparación de inicio</span>
          <strong>No se pudo verificar la preparación de inicio.</strong>
          <p>La validación previa del servidor se ejecutará al iniciar de todos modos.</p>
        </div>
      </section>
    );
  }

  if (!preflight) {
    return (
      <section className="startReadinessPanel startReadiness-neutral">
        <div>
          <span>Preparación de inicio</span>
          <strong>La preparación aún no ha sido verificada.</strong>
        </div>
      </section>
    );
  }

  const toneClass = preflight.canStart
    ? "startReadiness-success"
    : "startReadiness-danger";
  const title = preflight.canStart
    ? "Listo para iniciar el análisis."
    : "El análisis aún no puede iniciar.";

  return (
    <section className={`startReadinessPanel ${toneClass}`}>
      <div>
        <span>Preparación de inicio</span>
        <strong>{title}</strong>
        {!preflight.canStart && (
          <p>Adjunte métricas estructuradas en formato JSON/CSV/PDF antes de iniciar este análisis.</p>
        )}
      </div>

      {(preflight.errors.length > 0 || preflight.warnings.length > 0) && (
        <div className="preflightIssueList">
          {[...preflight.errors, ...preflight.warnings].map((issue) => (
            <div key={`${issue.severity}-${issue.code}-${issue.message}`}>
              <b>[{issue.severity}]</b> {issue.code} - {issue.message}
            </div>
          ))}
        </div>
      )}
    </section>
  );
}

export default StartReadinessPanel;
