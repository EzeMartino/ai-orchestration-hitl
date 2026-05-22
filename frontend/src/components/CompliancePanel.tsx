import type { ComplianceContext } from "../types/domain.types";

interface CompliancePanelProps {
  compliance?: ComplianceContext;
}

export function CompliancePanel({ compliance }: CompliancePanelProps) {
  if (!compliance) {
    return null;
  }

  const reviewNotes = [
    "This is regulatory retrieval evidence, not legal advice.",
    ...(compliance.warnings ?? []),
  ];

  return (
    <section className="compliancePanel">
      <div className="complianceHeader">
        <div>
          <p className="complianceEyebrow">Compliance review</p>
          <h2>LegalAgent assessment</h2>
          <p>{compliance.summary}</p>

          {compliance.engine && (
            <div className="engineBadge">
              Compliance engine: <strong>{compliance.engine}</strong>
            </div>
          )}
        </div>

        <span className={`riskBadge risk-${compliance.riskLevel}`}>
          {compliance.riskLevel}
        </span>
      </div>

      <div className="complianceEvidenceList">
        {compliance.evidence.map((item, index) => (
          <article
            className="complianceEvidenceCard"
            key={`${item.regulation}-${item.section}-${index}`}
          >
            <div className="complianceEvidenceTop">
              <strong>{item.regulation}</strong>
              <span>{item.section}</span>
            </div>

            <p>{item.finding}</p>

            <div className="sourceLine">
              Source: <span>{item.source}</span>
            </div>
          </article>
        ))}
      </div>

      <div className="legalWarnings">
        <strong>Review notes</strong>
        <ul>
          {reviewNotes.map((warning) => (
            <li key={warning}>{warning}</li>
          ))}
        </ul>
      </div>
    </section>
  );
}

export default CompliancePanel;
