import type { ComplianceContext } from "../types/domain.types";
import {
  formatLegalAssessmentValue,
  formatLegalRiskLevel,
} from "../utils/legalEvidenceAssessment";
import {
  formatRegulatoryEnrichmentStatus,
  formatRegulatoryEnrichmentSummary,
  getSafeRegulatoryEvidenceUrl,
  normalizeRegulatoryEvidenceEnrichments,
} from "../utils/regulatoryEvidenceEnrichment";

interface CompliancePanelProps {
  compliance?: ComplianceContext;
}

const longRegulatoryContentStyle = {
  overflowWrap: "anywhere",
  whiteSpace: "pre-wrap",
} as const;

export function CompliancePanel({ compliance }: CompliancePanelProps) {
  if (!compliance) {
    return null;
  }

  const reviewNotes = [
    "Esta es evidencia de recuperación regulatoria, no constituye asesoramiento legal.",
    ...(compliance.warnings ?? []),
  ];

  const legalReview = compliance.legalReview;
  const evidenceAssessment = compliance.evidenceAssessment;
  const evidenceEnrichments = normalizeRegulatoryEvidenceEnrichments(compliance.evidenceEnrichments);

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
          {formatLegalRiskLevel(compliance.riskLevel)}
        </span>
      </div>

      {evidenceAssessment && (
        <section
          className="legalEvidenceAssessment"
          aria-labelledby="legal-evidence-assessment-title"
        >
          <p className="legalEvidenceAssessmentEyebrow">
            Estado de la evidencia recuperada
          </p>
          <h3 id="legal-evidence-assessment-title">
            Recuperación distinta de evaluación de cumplimiento
          </h3>
          <p className="legalEvidenceAssessmentDisclaimer">
            La evidencia encontrada puede indicar un área posible de revisión; no determina aplicabilidad, incumplimiento ni asesoramiento legal.
          </p>

          <dl className="legalEvidenceAssessmentGrid">
            <div>
              <dt>Evidencia encontrada</dt>
              <dd>{evidenceAssessment.evidenceFound ? "Sí" : "No"}</dd>
            </div>
            <div>
              <dt>Relevancia</dt>
              <dd>{formatLegalAssessmentValue(evidenceAssessment.relevance)}</dd>
            </div>
            <div>
              <dt>Aplicabilidad</dt>
              <dd>{formatLegalAssessmentValue(evidenceAssessment.applicability)}</dd>
            </div>
            <div>
              <dt>Calidad</dt>
              <dd>{formatLegalAssessmentValue(evidenceAssessment.evidenceQuality)}</dd>
            </div>
            <div>
              <dt>Severidad</dt>
              <dd>{formatLegalAssessmentValue(evidenceAssessment.severity)}</dd>
            </div>
            <div>
              <dt>Revisión humana</dt>
              <dd>
                {evidenceAssessment.requiresHumanReview
                  ? "Requerida"
                  : "No requerida por la recuperación"}
              </dd>
            </div>
          </dl>

          {evidenceAssessment.reasons.length > 0 && (
            <div className="legalEvidenceAssessmentReasons">
              <strong>Razones de la evaluación</strong>
              <ul>
                {evidenceAssessment.reasons.map((reason, index) => (
                  <li key={`${index}-${reason}`}>{reason}</li>
                ))}
              </ul>
            </div>
          )}
        </section>
      )}

      {evidenceEnrichments.length > 0 && (
        <section aria-labelledby="regulatory-context-verification-title">
          <h3 id="regulatory-context-verification-title">
            Verificación de contexto regulatorio
          </h3>
          <p>
            La verificación documental aporta contexto regulatorio, pero no determina aplicabilidad, incumplimiento ni asesoramiento legal.
          </p>

          {evidenceEnrichments.map((item) => {
            const originalCitationUrl = getSafeRegulatoryEvidenceUrl(
              item.original.citation.url,
            );
            const canonicalDocumentUrl = getSafeRegulatoryEvidenceUrl(
              item.document?.url,
            );
            const canonicalArticleUrl = getSafeRegulatoryEvidenceUrl(
              item.article?.citation.url,
            );

            return (
              <article
                key={item.enrichmentId}
                style={longRegulatoryContentStyle}
              >
                <h4>
                  Resultado {item.rank}:{" "}
                  {formatRegulatoryEnrichmentStatus(item.status)}
                </h4>
                <p>
                  <b>Fragmento original:</b> {item.original.snippet}
                </p>
                <div>
                  <b>Cita original:</b>
                  <dl>
                    <div>
                      <dt>Título</dt>
                      <dd>{item.original.citation.title}</dd>
                    </div>
                    <div>
                      <dt>Fuente</dt>
                      <dd>{item.original.citation.source}</dd>
                    </div>
                    {item.original.citation.documentType && (
                      <div>
                        <dt>Tipo de documento</dt>
                        <dd>{item.original.citation.documentType}</dd>
                      </div>
                    )}
                    {item.original.citation.resolutionNumber && (
                      <div>
                        <dt>Número de resolución</dt>
                        <dd>{item.original.citation.resolutionNumber}</dd>
                      </div>
                    )}
                    {item.original.citation.chapter && (
                      <div>
                        <dt>Capítulo</dt>
                        <dd>{item.original.citation.chapter}</dd>
                      </div>
                    )}
                    {item.original.citation.section && (
                      <div>
                        <dt>Sección</dt>
                        <dd>{item.original.citation.section}</dd>
                      </div>
                    )}
                    {item.original.citation.article && (
                      <div>
                        <dt>Artículo</dt>
                        <dd>{item.original.citation.article}</dd>
                      </div>
                    )}
                    {item.original.citation.publicationDate && (
                      <div>
                        <dt>Fecha de publicación</dt>
                        <dd>{item.original.citation.publicationDate}</dd>
                      </div>
                    )}
                  </dl>
                  {item.original.citation.quotedText && (
                    <p>
                      <b>Texto citado original:</b>{" "}
                      {item.original.citation.quotedText}
                    </p>
                  )}
                  {originalCitationUrl && (
                    <p>
                      <a
                        href={originalCitationUrl}
                        target="_blank"
                        rel="noopener noreferrer"
                      >
                        Abrir cita original: {item.original.citation.title}
                      </a>
                    </p>
                  )}
                </div>

                {(item.document || item.article) && (
                  <details>
                    <summary>{formatRegulatoryEnrichmentSummary(item.status)}</summary>

                    {item.document && (
                      <section>
                        <h5>Documento canónico: {item.document.title}</h5>
                        <dl>
                          <div>
                            <dt>Identificador</dt>
                            <dd>{item.document.id}</dd>
                          </div>
                          <div>
                            <dt>Fuente</dt>
                            <dd>{item.document.source}</dd>
                          </div>
                          <div>
                            <dt>Tipo de documento</dt>
                            <dd>{item.document.documentType}</dd>
                          </div>
                          {item.document.resolutionNumber && (
                            <div>
                              <dt>Número de resolución</dt>
                              <dd>{item.document.resolutionNumber}</dd>
                            </div>
                          )}
                          {item.document.publicationDate && (
                            <div>
                              <dt>Fecha de publicación</dt>
                              <dd>{item.document.publicationDate}</dd>
                            </div>
                          )}
                          {item.document.effectiveDate && (
                            <div>
                              <dt>Fecha de vigencia</dt>
                              <dd>{item.document.effectiveDate}</dd>
                            </div>
                          )}
                          <div>
                            <dt>Estado documental</dt>
                            <dd>{item.document.status}</dd>
                          </div>
                          <div>
                            <dt>Revisión indicada por la fuente</dt>
                            <dd>
                              {item.document.requiresReview ? "Sí" : "No"}
                            </dd>
                          </div>
                          {item.document.retrievedAt && (
                            <div>
                              <dt>Fecha de recuperación</dt>
                              <dd>{item.document.retrievedAt}</dd>
                            </div>
                          )}
                          <div>
                            <dt>Longitud original</dt>
                            <dd>
                              {item.document.originalTextLength} caracteres
                            </dd>
                          </div>
                        </dl>

                        {Object.keys(item.document.metadata).length > 0 && (
                          <div>
                            <b>Metadatos del documento:</b>
                            <dl>
                              {Object.entries(item.document.metadata).map(
                                ([name, value]) => (
                                  <div
                                    key={`${item.enrichmentId}-metadata-${name}`}
                                  >
                                    <dt>{name}</dt>
                                    <dd>{value}</dd>
                                  </div>
                                ),
                              )}
                            </dl>
                          </div>
                        )}

                        <p>{item.document.text}</p>

                        {canonicalDocumentUrl && (
                          <p>
                            <a
                              href={canonicalDocumentUrl}
                              target="_blank"
                              rel="noopener noreferrer"
                            >
                              Abrir documento canónico: {item.document.title}
                            </a>
                          </p>
                        )}

                        {item.document.citations.length > 0 && (
                          <div>
                            <b>Citas del documento canónico:</b>
                            <ul>
                              {item.document.citations.map(
                                (citation, index) => {
                                  const citationUrl =
                                    getSafeRegulatoryEvidenceUrl(citation.url);

                                  return (
                                    <li
                                      key={`${item.enrichmentId}-document-citation-${index}`}
                                    >
                                      <dl>
                                        <div>
                                          <dt>Título</dt>
                                          <dd>{citation.title}</dd>
                                        </div>
                                        <div>
                                          <dt>Fuente</dt>
                                          <dd>{citation.source}</dd>
                                        </div>
                                        {citation.documentType && (
                                          <div>
                                            <dt>Tipo de documento</dt>
                                            <dd>{citation.documentType}</dd>
                                          </div>
                                        )}
                                        {citation.resolutionNumber && (
                                          <div>
                                            <dt>Número de resolución</dt>
                                            <dd>{citation.resolutionNumber}</dd>
                                          </div>
                                        )}
                                        {citation.chapter && (
                                          <div>
                                            <dt>Capítulo</dt>
                                            <dd>{citation.chapter}</dd>
                                          </div>
                                        )}
                                        {citation.section && (
                                          <div>
                                            <dt>Sección</dt>
                                            <dd>{citation.section}</dd>
                                          </div>
                                        )}
                                        {citation.article && (
                                          <div>
                                            <dt>Artículo</dt>
                                            <dd>{citation.article}</dd>
                                          </div>
                                        )}
                                        {citation.publicationDate && (
                                          <div>
                                            <dt>Fecha de publicación</dt>
                                            <dd>{citation.publicationDate}</dd>
                                          </div>
                                        )}
                                      </dl>
                                      {citation.quotedText && (
                                        <p>
                                          <b>Texto citado canónico:</b>{" "}
                                          {citation.quotedText}
                                        </p>
                                      )}
                                      {citationUrl && (
                                        <p>
                                          <a
                                            href={citationUrl}
                                            target="_blank"
                                            rel="noopener noreferrer"
                                          >
                                            Abrir cita canónica:{" "}
                                            {citation.title}
                                          </a>
                                        </p>
                                      )}
                                    </li>
                                  );
                                },
                              )}
                            </ul>
                          </div>
                        )}
                      </section>
                    )}

                    {item.article && (
                      <section>
                        <h5>
                          Artículo canónico: {item.article.citation.title}
                          {item.article.citation.article
                            ? ` — ${item.article.citation.article}`
                            : ""}
                        </h5>
                        <dl>
                          <div>
                            <dt>Fuente</dt>
                            <dd>{item.article.citation.source}</dd>
                          </div>
                          {item.article.citation.documentType && (
                            <div>
                              <dt>Tipo de documento</dt>
                              <dd>{item.article.citation.documentType}</dd>
                            </div>
                          )}
                          {item.article.citation.resolutionNumber && (
                            <div>
                              <dt>Número de resolución</dt>
                              <dd>{item.article.citation.resolutionNumber}</dd>
                            </div>
                          )}
                          {item.article.citation.chapter && (
                            <div>
                              <dt>Capítulo</dt>
                              <dd>{item.article.citation.chapter}</dd>
                            </div>
                          )}
                          {item.article.citation.section && (
                            <div>
                              <dt>Sección</dt>
                              <dd>{item.article.citation.section}</dd>
                            </div>
                          )}
                          {item.article.citation.article && (
                            <div>
                              <dt>Artículo</dt>
                              <dd>{item.article.citation.article}</dd>
                            </div>
                          )}
                          {item.article.citation.publicationDate && (
                            <div>
                              <dt>Fecha de publicación</dt>
                              <dd>{item.article.citation.publicationDate}</dd>
                            </div>
                          )}
                          <div>
                            <dt>Confianza de recuperación</dt>
                            <dd>
                              {(item.article.confidence * 100).toFixed(0)}%
                            </dd>
                          </div>
                          <div>
                            <dt>Longitud original</dt>
                            <dd>
                              {item.article.originalTextLength} caracteres
                            </dd>
                          </div>
                        </dl>
                        {item.article.citation.quotedText && (
                          <p>
                            <b>Texto citado canónico:</b>{" "}
                            {item.article.citation.quotedText}
                          </p>
                        )}
                        <p>{item.article.text}</p>
                        {canonicalArticleUrl && (
                          <p>
                            <a
                              href={canonicalArticleUrl}
                              target="_blank"
                              rel="noopener noreferrer"
                            >
                              Abrir artículo canónico:{" "}
                              {item.article.citation.title}
                              {item.article.citation.article
                                ? ` — ${item.article.citation.article}`
                                : ""}
                            </a>
                          </p>
                        )}
                      </section>
                    )}
                  </details>
                )}

                {(item.document?.isTruncated ||
                  item.article?.isTruncated) && (
                  <p>
                    El contexto mostrado fue truncado al límite seguro configurado.
                  </p>
                )}

                {item.limitations.length > 0 && (
                  <div>
                    <b>Limitaciones de la verificación documental:</b>
                    <ul>
                      {item.limitations.map((limitation, index) => (
                        <li
                          key={`${item.enrichmentId}-limitation-${index}`}
                        >
                          {limitation}
                        </li>
                      ))}
                    </ul>
                  </div>
                )}
              </article>
            );
          })}
        </section>
      )}

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
