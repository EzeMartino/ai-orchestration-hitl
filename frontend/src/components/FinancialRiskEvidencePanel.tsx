import { useState } from "react";
import type { FinancialAnalysisContext } from "../types/domain.types";
import {
  formatFinancialWarningText,
  groupFinancialWarnings,
} from "../utils/financialWarnings";

interface FinancialRiskEvidencePanelProps {
  financialAnalysis?: FinancialAnalysisContext | null;
}

function formatSignalTitle(value: string) {
  return value
    .replaceAll("_", " ")
    .replaceAll(".", " ")
    .replace(/\b\w/g, (character) => character.toUpperCase());
}

function formatNumber(value?: number | null) {
  if (value === null || value === undefined) {
    return "-";
  }

  return new Intl.NumberFormat(undefined, {
    maximumFractionDigits: 3,
  }).format(value);
}

function formatPercent(value?: number | null) {
  if (value === null || value === undefined) {
    return "-";
  }

  return `${Math.round(value * 100)}%`;
}

function formatIngestionMethod(ingestionMethod?: string | null) {
  switch (ingestionMethod) {
    case "json_paste":
      return "Pegado de JSON";
    case "csv_paste":
      return "Pegado de CSV";
    case "json_file":
      return "Archivo JSON";
    case "csv_file":
      return "Archivo CSV";
    case "pdf_file":
      return "Archivo PDF";
    default:
      return "Desconocido";
  }
}

function formatMetricsInputSource(inputSource?: string | null) {
  switch (inputSource) {
    case "session_context":
      return "Contexto de la sesión";
    case "fixture_fallback":
      return "Datos de prueba (respaldo)";
    case "none":
      return "Ninguno";
    case "unknown":
      return "Desconocido";
    default:
      return "Desconocido";
  }
}

function formatAiReviewStatus(
  aiReview: FinancialAnalysisContext["aiReview"]
) {
  if (!aiReview) {
    return "Revisión de IA no disponible";
  }

  if (aiReview.usedLlm) {
    return "LLM utilizado";
  }

  if (
    aiReview.failureReason?.includes("not") ||
    aiReview.summary.includes("not executed") ||
    aiReview.summary.includes("no se ejecutó")
  ) {
    return "No ejecutado";
  }

  return "Respaldo determinista";
}

function formatThreshold(signal: FinancialAnalysisContext["riskSignals"][number]) {
  if (signal.thresholdOperator && signal.thresholdValue != null) {
    return `${signal.thresholdOperator} ${formatNumber(signal.thresholdValue)}`;
  }

  return formatNumber(signal.threshold);
}

function formatMissingInput(value: string) {
  switch (value) {
    case "numerator":
      return "numerador";
    case "denominator":
      return "denominador";
    default:
      return value;
  }
}

function formatMissingInputs(values: string[]) {
  return values.map(formatMissingInput).join(" y ");
}

function isRequiredMetricsWarning(warning: string) {
  const normalized = warning.toLowerCase();

  return (
    normalized.includes("required for this mode") ||
    normalized.includes("se requieren métricas financieras estructuradas")
  );
}

export function FinancialRiskEvidencePanel({
  financialAnalysis,
}: FinancialRiskEvidencePanelProps) {
  if (!financialAnalysis) {
    return null;
  }

  const [thresholdsExpanded, setThresholdsExpanded] = useState(false);

  const visibleEvidence = financialAnalysis.riskEvidence.slice(0, 6);
  const visibleSignals = financialAnalysis.riskSignals.slice(0, 6);
  const visibleRatios = financialAnalysis.ratios.slice(0, 6);
  const metricsInputSource = financialAnalysis.metricsInputSource ?? "unknown";
  const isFixtureFallback = metricsInputSource === "fixture_fallback";
  const hasNoMetrics = metricsInputSource === "none";
  const aiReview = financialAnalysis.aiReview;
  const requiresSessionMetrics = financialAnalysis.warnings.some(isRequiredMetricsWarning);
  const groupedWarnings = groupFinancialWarnings(financialAnalysis.warnings);

  return (
    <section className="financialRiskPanel">
      <div className="financialRiskHeader">
        <div>
          <p className="financialRiskEyebrow">Evidencia de riesgo financiero</p>
          <h2 style={{ display: "flex", alignItems: "center", gap: "0.75rem", flexWrap: "wrap" }}>
            {financialAnalysis.company ?? "Métricas financieras estructuradas"}
            {financialAnalysis.thresholdProfile && (
              <span className="profileBadge" style={{ fontWeight: "normal" }}>
                Perfil: <strong>{financialAnalysis.thresholdProfile}</strong>
              </span>
            )}
          </h2>
          <p>
            Evidencia cuantitativa generada a partir de métricas financieras estructuradas.
          </p>

          <div className="engineBadge">
            Motor financiero: <strong>{financialAnalysis.engine}</strong>
          </div>
          <div className="engineBadge">
            Origen de las métricas:{" "}
            <strong>
              {formatMetricsInputSource(financialAnalysis.metricsInputSource)}
            </strong>
          </div>
          {financialAnalysis.metricsProvenance && (
            <div className="engineBadge">
              Ingesta:{" "}
              <strong>
                {formatIngestionMethod(
                  financialAnalysis.metricsProvenance.ingestionMethod
                )}
              </strong>
              {financialAnalysis.metricsProvenance.originalFileName
                ? ` - Archivo: ${financialAnalysis.metricsProvenance.originalFileName}`
                : ""}
            </div>
          )}
        </div>

        <div className="documentBadge">
          <span>Documento</span>
          <strong>{financialAnalysis.documentId}</strong>
        </div>
      </div>

      {isFixtureFallback && (
        <div className="financialSourceWarning">
          Se utilizaron métricas de prueba predefinidas (fixtures). Adjunte métricas estructuradas para analizar los datos específicos de la sesión.
        </div>
      )}

      {hasNoMetrics && (
        <div className="financialSourceWarning">
          {requiresSessionMetrics
            ? "Se requieren métricas financieras estructuradas para este modo, pero no se adjuntaron a la sesión. Adjunte métricas JSON/CSV/PDF antes de iniciar el análisis."
            : "Las métricas financieras estructuradas no estaban disponibles."}
        </div>
      )}

      {aiReview && (
        <div className="financialAiReview">
          <div className="financialAiReviewHeader">
            <div>
              <strong>Revisión de IA de DataAgent</strong>
              <p>Interpretación asesora de la evidencia financiera determinista.</p>
            </div>
            <span className="aiReviewStatus">{formatAiReviewStatus(aiReview)}</span>
          </div>

          <dl className="aiReviewMeta">
            <div>
              <dt>Estado del LLM</dt>
              <dd>{formatAiReviewStatus(aiReview)}</dd>
            </div>
            <div>
              <dt>Proveedor</dt>
              <dd>{aiReview.provider ?? "-"}</dd>
            </div>
            <div>
              <dt>Modelo</dt>
              <dd>{aiReview.model ?? "-"}</dd>
            </div>
            <div>
              <dt>Motivo de fallo</dt>
              <dd>{aiReview.failureReason ?? "-"}</dd>
            </div>
          </dl>

          {aiReview.failureReason && (
            <div className="financialSourceWarning">
              La revisión de IA utilizó un respaldo seguro. Motivo: {aiReview.failureReason}.
            </div>
          )}

          <div className="aiReviewTextBlock">
            <strong>Resumen</strong>
            <p>{aiReview.summary}</p>
          </div>

          <div className="aiReviewTextBlock">
            <strong>Interpretación de riesgos</strong>
            <p>{aiReview.riskInterpretation}</p>
          </div>

          {aiReview.keyFindings.length > 0 && (
            <div className="financialSection">
              <strong>Hallazgos clave</strong>
              <div className="financialSignalList">
                {aiReview.keyFindings.map((finding, index) => (
                  <article
                    className="financialSignalCard"
                    key={`${finding.title}-${index}`}
                  >
                    <span className={`severityPill severity-${finding.severity}`}>
                      {finding.severity === "High" ? "Alta" : finding.severity === "Medium" ? "Media" : finding.severity === "Low" ? "Baja" : finding.severity}
                    </span>
                    <div>
                      <strong>{finding.title}</strong>
                      <p>{finding.description}</p>
                      {finding.relatedMetrics.length > 0 && (
                        <small>{finding.relatedMetrics.join(", ")}</small>
                      )}
                    </div>
                  </article>
                ))}
              </div>
            </div>
          )}

          {(aiReview.dataQualityNotes.length > 0 ||
            aiReview.limitations.length > 0) && (
            <div className="financialReviewNotes">
              {aiReview.dataQualityNotes.length > 0 && (
                <div>
                  <strong>Notas de calidad de datos</strong>
                  <ul>
                    {aiReview.dataQualityNotes.map((note, index) => (
                      <li key={`${note.message}-${index}`}>
                        [{note.severity === "High" ? "Alta" : note.severity === "Medium" ? "Media" : note.severity === "Low" ? "Baja" : note.severity}] {note.message}
                      </li>
                    ))}
                  </ul>
                </div>
              )}

              {aiReview.limitations.length > 0 && (
                <div>
                  <strong>Limitaciones de la revisión de IA</strong>
                  <ul>
                    {aiReview.limitations.map((limitation) => (
                      <li key={limitation}>{limitation}</li>
                    ))}
                  </ul>
                </div>
              )}
            </div>
          )}
        </div>
      )}

      {visibleEvidence.length > 0 && (
        <div className="financialEvidenceGrid">
          {visibleEvidence.map((item, index) => (
            <article
              className="financialEvidenceCard"
              key={`${item.code}-${item.period ?? "period"}-${index}`}
            >
              <div className="financialEvidenceTop">
                <span className={`severityBadge severity-${item.severity}`}>
                  {item.severity === "High" ? "Alta" : item.severity === "Medium" ? "Media" : item.severity === "Low" ? "Baja" : item.severity}
                </span>
                <strong>{formatSignalTitle(item.title)}</strong>
              </div>

              <p>{item.message}</p>

              <dl className="financialEvidenceMeta">
                <div>
                  <dt>Métrica</dt>
                  <dd>{item.metric ?? "-"}</dd>
                </div>
                <div>
                  <dt>Período</dt>
                  <dd>{item.period ?? "-"}</dd>
                </div>
                <div>
                  <dt>Valor</dt>
                  <dd>{formatNumber(item.value)}</dd>
                </div>
                <div>
                  <dt>Umbral</dt>
                  <dd>{formatNumber(item.threshold)}</dd>
                </div>
                <div>
                  <dt>Confianza</dt>
                  <dd>{formatPercent(item.confidence)}</dd>
                </div>
                <div>
                  <dt>Página de origen</dt>
                  <dd>{item.sourcePage ?? "-"}</dd>
                </div>
              </dl>
            </article>
          ))}
        </div>
      )}

      {visibleSignals.length > 0 && (
        <div className="financialSection">
          <strong>Señales de riesgo</strong>
          <div className="financialSignalList">
            {visibleSignals.map((signal, index) => (
              <article
                className="financialSignalCard"
                key={`${signal.code}-${signal.metric}-${signal.period}-${index}`}
              >
                <span className={`severityPill severity-${signal.severity}`}>
                  {signal.severity === "High" ? "Alta" : signal.severity === "Medium" ? "Media" : signal.severity === "Low" ? "Baja" : signal.severity}
                </span>
                <div>
                  <strong>{formatSignalTitle(signal.code)}</strong>
                  <p>{signal.explanation}</p>
                  <dl className="financialEvidenceMeta">
                    <div>
                      <dt>Métrica</dt>
                      <dd>{signal.metric ?? "-"}</dd>
                    </div>
                    <div>
                      <dt>Período</dt>
                      <dd>{signal.period ?? "-"}</dd>
                    </div>
                    <div>
                      <dt>Valor observado</dt>
                      <dd>{formatNumber(signal.value)}</dd>
                    </div>
                    <div>
                      <dt>Umbral</dt>
                      <dd>{formatThreshold(signal)}</dd>
                    </div>
                  </dl>
                  {signal.reason && <small>Motivo: {signal.reason}</small>}
                </div>
              </article>
            ))}
          </div>
        </div>
      )}

      <div className="disclaimerBox">
        <strong>Línea de Defensa Heurística</strong>
        Los umbrales de riesgo son criterios de revisión heurísticos. No constituyen asesoramiento de inversión ni confirman la exactitud contable.
      </div>

      {financialAnalysis.thresholdsUsed && financialAnalysis.thresholdsUsed.length > 0 && (
        <div className="thresholdsCollapse">
          <div
            className="thresholdsCollapseHeader"
            onClick={() => setThresholdsExpanded(!thresholdsExpanded)}
            style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}
          >
            <span>Umbrales Aplicados ({financialAnalysis.thresholdsUsed.length})</span>
            <span style={{ transform: thresholdsExpanded ? "rotate(180deg)" : "rotate(0deg)", transition: "transform 0.2s ease", display: "inline-block" }}>
              ▼
            </span>
          </div>
          {thresholdsExpanded && (
            <div className="thresholdsCollapseContent">
              <div className="thresholdsGrid">
                {financialAnalysis.thresholdsUsed.map((t, idx) => (
                  <div className="thresholdCard" key={`${t.code}-${idx}`}>
                    <div className="thresholdCardHeader">
                      <span className="thresholdCardTitle">{formatSignalTitle(t.code)}</span>
                      <span className="thresholdValueBadge">
                        {t.metric} {t.operator} {formatNumber(t.value)}
                      </span>
                    </div>
                    <div className="thresholdCardBody">
                      {t.description}
                    </div>
                    <div className="thresholdCardMeta">
                      <span className={`severityBadge severity-${t.severity}`} style={{ fontSize: "0.65rem", padding: "0.1rem 0.35rem" }}>
                        {t.severity === "High" ? "Alta" : t.severity === "Medium" ? "Media" : t.severity === "Low" ? "Baja" : t.severity}
                      </span>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>
      )}

      {visibleRatios.length > 0 && (
        <div className="financialSection">
          <strong>Ratios clave</strong>
          <div className="ratioStrip">
            {visibleRatios.map((ratio) => (
              <article className="ratioTile" key={`${ratio.name}-${ratio.period}`}>
                <span>{formatSignalTitle(ratio.name)}</span>
                <strong>{formatNumber(ratio.value)}</strong>
                <small>
                  {ratio.period}
                  {ratio.unit ? ` · ${ratio.unit}` : ""}
                </small>
              </article>
            ))}
          </div>
        </div>
      )}

      {(financialAnalysis.warnings.length > 0 ||
        financialAnalysis.limitations.length > 0) && (
        <div className="financialReviewNotes">
          {financialAnalysis.warnings.length > 0 && (
            <div>
              <strong>Advertencias</strong>
              {groupedWarnings.periodGroups.length > 0 && (
                <div className="warningGroupList">
                  {groupedWarnings.periodGroups.map((group) => (
                    <details
                      className="warningGroup"
                      key={group.period}
                      open={groupedWarnings.periodGroups.length <= 2}
                    >
                      <summary>
                        <span>{group.period}</span>
                        <small>
                          Faltan {group.messageCount} insumos para{" "}
                          {group.items.length} ratios
                        </small>
                      </summary>
                      <ul>
                        {group.items.map((item) => (
                          <li key={`${group.period}-${item.ratioName}`}>
                            <div className="warningGroupItem">
                              <strong>{formatSignalTitle(item.ratioName)}</strong>
                              <span>
                                Falta {formatMissingInputs(item.missingInputs)}
                              </span>
                              <details>
                                <summary>Ver detalle técnico</summary>
                                <ul>
                                  {item.messages.map((message) => (
                                    <li key={message}>
                                      {formatFinancialWarningText(message)}
                                    </li>
                                  ))}
                                </ul>
                              </details>
                            </div>
                          </li>
                        ))}
                      </ul>
                    </details>
                  ))}
                </div>
              )}
              {groupedWarnings.ungrouped.length > 0 && (
                <ul>
                  {groupedWarnings.ungrouped.map((warning) => (
                    <li key={warning}>{formatFinancialWarningText(warning)}</li>
                  ))}
                </ul>
              )}
            </div>
          )}

          {financialAnalysis.limitations.length > 0 && (
            <div>
              <strong>Limitaciones</strong>
              <ul>
                {financialAnalysis.limitations.map((limitation) => (
                  <li key={limitation}>{formatFinancialWarningText(limitation)}</li>
                ))}
              </ul>
            </div>
          )}
        </div>
      )}
    </section>
  );
}

export default FinancialRiskEvidencePanel;
