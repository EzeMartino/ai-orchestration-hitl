import type { ComplianceContext } from "../types/domain.types";

interface CompliancePanelProps {
  compliance?: ComplianceContext;
}

export function CompliancePanel({ compliance }: CompliancePanelProps) {
  if (!compliance) {
    return null;
  }

  const reviewNotes = [
    "Esta es evidencia de recuperación regulatoria, no constituye asesoramiento legal.",
    ...(compliance.warnings ?? []),
  ];

  const legalReview = compliance.legalReview;

  return (
    <section className="compliancePanel">
      <div className="complianceHeader">
        <div>
          <p className="complianceEyebrow">Revisión de cumplimiento normativo</p>
          <h2>Evaluación de LegalAgent</h2>
          <p>{compliance.summary}</p>

          {compliance.engine && (
            <div className="engineBadge">
              Motor de cumplimiento: <strong>{compliance.engine}</strong>
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
              Origen: <span>{item.source}</span>
            </div>
          </article>
        ))}
      </div>

      {legalReview && (
        <div className="financialAiReview" style={{ marginTop: "1.5rem" }}>
          <div className="financialAiReviewHeader">
            <div>
              <h3 style={{ margin: 0, color: "#0f2747", fontSize: "1.1rem" }}>
                Revisión de IA de LegalAgent
              </h3>
              <p style={{ margin: "0.35rem 0 0", color: "#475569" }}>
                {legalReview.reviewSummary}
              </p>
            </div>
            <span className="aiReviewStatus">
              {legalReview.usedLlm ? "Revisión de IA" : "Respaldo determinista"}
            </span>
          </div>

          <dl className="aiReviewMeta">
            <div>
              <dt>Método de análisis</dt>
              <dd>{legalReview.usedLlm ? "Semantic Kernel (LLM)" : "Respaldo basado en reglas"}</dd>
            </div>
            <div>
              <dt>Proveedor del modelo</dt>
              <dd>{legalReview.provider || "N/A"}</dd>
            </div>
            <div>
              <dt>Nombre del modelo</dt>
              <dd>{legalReview.model || "N/A"}</dd>
            </div>
          </dl>

          {legalReview.possibleRegulatoryReviewAreas && legalReview.possibleRegulatoryReviewAreas.length > 0 && (
            <div className="aiReviewTextBlock" style={{ marginTop: "1.2rem" }}>
              <strong style={{ display: "block", marginBottom: "0.5rem", color: "#0f2747" }}>
                Posibles áreas de revisión regulatoria
              </strong>
              <div style={{ display: "grid", gap: "0.85rem" }}>
                {legalReview.possibleRegulatoryReviewAreas.map((area, index) => (
                  <div
                    key={index}
                    style={{
                      background: "white",
                      border: "1px solid #cbd5e1",
                      borderRadius: "12px",
                      padding: "0.85rem 1rem",
                      boxShadow: "0 1px 3px rgba(0,0,0,0.05)"
                    }}
                  >
                    <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                      <strong style={{ color: "#0f2747" }}>{area.title}</strong>
                      <span className={`severityPill`} style={{
                        background: area.severity === "High" ? "#fee2e2" : area.severity === "Medium" ? "#fef3c7" : "#ecfdf5",
                        color: area.severity === "High" ? "#991b1b" : area.severity === "Medium" ? "#92400e" : "#065f46",
                        borderColor: area.severity === "High" ? "#fca5a5" : area.severity === "Medium" ? "#fcd34d" : "#6ee7b7",
                        borderWidth: "1px",
                        borderStyle: "solid"
                      }}>
                        {area.severity === "High" ? "Alta" : area.severity === "Medium" ? "Media" : area.severity === "Low" ? "Baja" : area.severity}
                      </span>
                    </div>
                    <p style={{ margin: "0.5rem 0", color: "#334155", fontSize: "0.9rem" }}>
                      {area.description}
                    </p>
                    {area.relatedFinancialSignals && area.relatedFinancialSignals.length > 0 && (
                      <div style={{ fontSize: "0.8rem", color: "#64748b", marginTop: "0.45rem" }}>
                        Señales relacionadas:{" "}
                        {area.relatedFinancialSignals.map((sig, sIdx) => (
                          <strong key={sIdx} style={{ color: "#334155", background: "#f1f5f9", padding: "0.15rem 0.35rem", borderRadius: "4px", marginRight: "0.25rem", display: "inline-block" }}>
                            {sig}
                          </strong>
                        ))}
                      </div>
                    )}
                    {area.evidenceCitations && area.evidenceCitations.length > 0 && (
                      <div style={{ fontSize: "0.8rem", color: "#64748b", marginTop: "0.45rem" }}>
                        Citas normativas:{" "}
                        {area.evidenceCitations.map((cit, cIdx) => (
                          <span key={cIdx} style={{ color: "#0f766e", fontWeight: 700, marginRight: "0.5rem" }}>
                            § {cit}
                          </span>
                        ))}
                      </div>
                    )}
                  </div>
                ))}
              </div>
            </div>
          )}

          {legalReview.evidenceReferences && legalReview.evidenceReferences.filter(e => e.citation && e.citation.trim() !== "").length > 0 && (
            <div className="aiReviewTextBlock" style={{ marginTop: "1.2rem" }}>
              <strong style={{ display: "block", marginBottom: "0.5rem", color: "#0f2747" }}>
                Evidencia Citada y Documentos de Respaldo
              </strong>
              <div style={{ display: "grid", gap: "0.75rem" }}>
                {legalReview.evidenceReferences
                  .filter(e => e.citation && e.citation.trim() !== "")
                  .map((ev, index) => (
                    <article
                      className="complianceEvidenceCard"
                      key={index}
                      style={{ borderLeftColor: "#0f766e", background: "#f0fdfa", padding: "0.85rem" }}
                    >
                      <div className="complianceEvidenceTop">
                        <strong style={{ color: "#0f2747" }}>{ev.title}</strong>
                        <span style={{ color: "#0f766e", fontSize: "0.8rem", fontWeight: 800 }}>{ev.source}</span>
                      </div>
                      {ev.citation && (
                        <p style={{ margin: "0.45rem 0", color: "#0f766e", fontWeight: "700", fontSize: "0.85rem" }}>
                          Cita: {ev.citation}
                        </p>
                      )}
                      {ev.snippet && (
                        <p style={{ margin: "0.45rem 0", color: "#334155", fontStyle: "italic", fontSize: "0.85rem", background: "white", padding: "0.5rem", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
                          "{ev.snippet}"
                        </p>
                      )}
                      <div className="sourceLine" style={{ fontSize: "0.8rem", marginTop: "0.45rem" }}>
                        {ev.url ? (
                          <a href={ev.url} target="_blank" rel="noopener noreferrer" style={{ color: "#0f766e", textDecoration: "underline", fontWeight: 700 }}>
                            Ver documento de origen oficial
                          </a>
                        ) : (
                          <span>Enlace de origen no disponible</span>
                        )}
                        {ev.score !== undefined && ev.score !== null && (
                          <span style={{ marginLeft: "1rem", color: "#64748b" }}>
                            Puntuación de relevancia: <strong>{(ev.score * 100).toFixed(0)}%</strong>
                          </span>
                        )}
                      </div>
                    </article>
                  ))}
              </div>
            </div>
          )}

          {((legalReview.warnings && legalReview.warnings.length > 0) || (legalReview.limitations && legalReview.limitations.length > 0)) && (
            <div className="legalWarnings" style={{ marginTop: "1.2rem", background: "#fef2f2", borderColor: "#fecaca", color: "#991b1b" }}>
              <strong>Advertencias y Limitaciones de la Revisión de IA</strong>
              <ul style={{ margin: 0, paddingLeft: "1.2rem" }}>
                {legalReview.warnings.map((warning, wIdx) => (
                  <li key={`w-${wIdx}`} style={{ marginBottom: "0.25rem" }}>{warning}</li>
                ))}
                {legalReview.limitations.map((limit, lIdx) => (
                  <li key={`l-${lIdx}`} style={{ marginBottom: "0.25rem" }}>{limit}</li>
                ))}
              </ul>
            </div>
          )}

          <div
            style={{
              marginTop: "1.2rem",
              background: "#eff6ff",
              border: "1px solid #bfdbfe",
              borderRadius: "12px",
              padding: "0.85rem 1rem",
              color: "#1e3a8a",
              fontSize: "0.85rem",
              lineHeight: "1.4"
            }}
          >
            <strong style={{ display: "block", marginBottom: "0.25rem", color: "#1e3a8a" }}>
              Descargo de Responsabilidad (Carácter Consultivo)
            </strong>
            Revisión de carácter consultivo del análisis financiero frente a la evidencia CNV/Infoleg provista. Esto no constituye asesoramiento legal. No se proporciona ninguna conclusión legal o regulatoria definitiva.
          </div>
        </div>
      )}

      <div className="legalWarnings">
        <strong>Notas de revisión</strong>
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
