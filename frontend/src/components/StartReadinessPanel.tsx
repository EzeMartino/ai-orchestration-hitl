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
          <span>Start readiness</span>
          <strong>Create or load a session to check start readiness.</strong>
        </div>
      </section>
    );
  }

  if (isChecking && !preflight) {
    return (
      <section className="startReadinessPanel startReadiness-neutral">
        <div>
          <span>Start readiness</span>
          <strong>Checking readiness...</strong>
        </div>
      </section>
    );
  }

  if (error && !preflight) {
    return (
      <section className="startReadinessPanel startReadiness-warning">
        <div>
          <span>Start readiness</span>
          <strong>Could not check start readiness.</strong>
          <p>Backend preflight will still run when starting.</p>
        </div>
      </section>
    );
  }

  if (!preflight) {
    return (
      <section className="startReadinessPanel startReadiness-neutral">
        <div>
          <span>Start readiness</span>
          <strong>Readiness has not been checked yet.</strong>
        </div>
      </section>
    );
  }

  const toneClass = preflight.canStart
    ? "startReadiness-success"
    : "startReadiness-danger";
  const title = preflight.canStart
    ? "Ready to start analysis."
    : "Analysis cannot start yet.";

  return (
    <section className={`startReadinessPanel ${toneClass}`}>
      <div>
        <span>Start readiness</span>
        <strong>{title}</strong>
        {!preflight.canStart && (
          <p>Attach JSON/CSV/PDF structured metrics before starting this analysis.</p>
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
