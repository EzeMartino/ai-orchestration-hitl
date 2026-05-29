import type { AnalysisContext } from "../types/domain.types";

interface EvidencePanelProps {
  anomaly: AnalysisContext["anomaly"];
}

function formatAnalysisEngine(engine: string) {
  const normalizedEngine = engine.trim().toLowerCase();

  if (
    normalizedEngine === "semantic kernel + python/csnakes" ||
    normalizedEngine.includes("legacy fallback")
  ) {
    return "Deterministic fallback";
  }

  return engine;
}

export function EvidencePanel({ anomaly }: EvidencePanelProps) {
  if (!anomaly) {
    return null;
  }

  return (
    <section className="evidencePanel">
      <div className="evidenceHeader">
        <div>
          <p className="evidenceEyebrow">Risk evidence</p>
          <h2>{anomaly.category}</h2>
          <p>{anomaly.summary}</p>

          {anomaly.engine && (
            <div className="engineBadge">
              Analysis engine: <strong>{formatAnalysisEngine(anomaly.engine)}</strong>
            </div>
          )}
        </div>

        <span className={`severityBadge severity-${anomaly.severity}`}>
          {anomaly.severity}
        </span>
      </div>

      <div className="evidenceGrid">
        {anomaly.evidence.map((item) => (
          <article className="metricCard" key={item.metric}>
            <span className="metricName">{item.metric}</span>

            <div className="metricValues">
              <strong>{item.value}</strong>
              <span>Threshold: {item.threshold}</span>
            </div>

            <p>{item.interpretation}</p>
          </article>
        ))}
      </div>

      <div className="recommendationBox">
        <strong>Recommendation</strong>
        <p>{anomaly.recommendation}</p>
      </div>
    </section>
  );
}

export default EvidencePanel;
