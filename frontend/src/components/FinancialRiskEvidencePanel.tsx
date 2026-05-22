import { useState } from "react";
import type { FinancialAnalysisContext } from "../types/domain.types";

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
      return "JSON paste";
    case "csv_paste":
      return "CSV paste";
    case "json_file":
      return "JSON file";
    case "csv_file":
      return "CSV file";
    default:
      return "Unknown";
  }
}

function formatMetricsInputSource(inputSource?: string | null) {
  switch (inputSource) {
    case "session_context":
      return "Session context";
    case "fixture_fallback":
      return "Fixture fallback";
    case "none":
      return "None";
    case "unknown":
      return "Unknown";
    default:
      return "Unknown";
  }
}

function formatAiReviewStatus(
  aiReview: FinancialAnalysisContext["aiReview"]
) {
  if (!aiReview) {
    return "AI review not available";
  }

  if (aiReview.usedLlm) {
    return "LLM used";
  }

  if (aiReview.failureReason?.includes("not") || aiReview.summary.includes("not executed")) {
    return "Not run";
  }

  return "Deterministic fallback";
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
  const requiresSessionMetrics = financialAnalysis.warnings.some((warning) =>
    warning.includes("required for this mode")
  );

  return (
    <section className="financialRiskPanel">
      <div className="financialRiskHeader">
        <div>
          <p className="financialRiskEyebrow">Financial risk evidence</p>
          <h2 style={{ display: "flex", alignItems: "center", gap: "0.75rem", flexWrap: "wrap" }}>
            {financialAnalysis.company ?? "Structured financial metrics"}
            {financialAnalysis.thresholdProfile && (
              <span className="profileBadge" style={{ fontWeight: "normal" }}>
                Profile: <strong>{financialAnalysis.thresholdProfile}</strong>
              </span>
            )}
          </h2>
          <p>
            Quantitative evidence generated from structured financial metrics.
          </p>

          <div className="engineBadge">
            Financial engine: <strong>{financialAnalysis.engine}</strong>
          </div>
          <div className="engineBadge">
            Metrics source:{" "}
            <strong>
              {formatMetricsInputSource(financialAnalysis.metricsInputSource)}
            </strong>
          </div>
          {financialAnalysis.metricsProvenance && (
            <div className="engineBadge">
              Ingestion:{" "}
              <strong>
                {formatIngestionMethod(
                  financialAnalysis.metricsProvenance.ingestionMethod
                )}
              </strong>
              {financialAnalysis.metricsProvenance.originalFileName
                ? ` - File: ${financialAnalysis.metricsProvenance.originalFileName}`
                : ""}
            </div>
          )}
        </div>

        <div className="documentBadge">
          <span>Document</span>
          <strong>{financialAnalysis.documentId}</strong>
        </div>
      </div>

      {isFixtureFallback && (
        <div className="financialSourceWarning">
          Fixture fallback metrics were used. Attach structured metrics to
          analyze session-specific data.
        </div>
      )}

      {hasNoMetrics && (
        <div className="financialSourceWarning">
          {requiresSessionMetrics
            ? "Structured financial metrics are required for this mode but were not attached to this session. Attach JSON/CSV metrics before starting the analysis."
            : "No structured financial metrics were available."}
        </div>
      )}

      {aiReview && (
        <div className="financialAiReview">
          <div className="financialAiReviewHeader">
            <div>
              <strong>DataAgent AI Review</strong>
              <p>Advisory interpretation of deterministic financial evidence.</p>
            </div>
            <span className="aiReviewStatus">{formatAiReviewStatus(aiReview)}</span>
          </div>

          <dl className="aiReviewMeta">
            <div>
              <dt>LLM status</dt>
              <dd>{formatAiReviewStatus(aiReview)}</dd>
            </div>
            <div>
              <dt>Provider</dt>
              <dd>{aiReview.provider ?? "-"}</dd>
            </div>
            <div>
              <dt>Model</dt>
              <dd>{aiReview.model ?? "-"}</dd>
            </div>
            <div>
              <dt>Failure reason</dt>
              <dd>{aiReview.failureReason ?? "-"}</dd>
            </div>
          </dl>

          {aiReview.failureReason && (
            <div className="financialSourceWarning">
              AI review used a safe fallback. Reason: {aiReview.failureReason}.
            </div>
          )}

          <div className="aiReviewTextBlock">
            <strong>Summary</strong>
            <p>{aiReview.summary}</p>
          </div>

          <div className="aiReviewTextBlock">
            <strong>Risk interpretation</strong>
            <p>{aiReview.riskInterpretation}</p>
          </div>

          {aiReview.keyFindings.length > 0 && (
            <div className="financialSection">
              <strong>Key findings</strong>
              <div className="financialSignalList">
                {aiReview.keyFindings.map((finding, index) => (
                  <article
                    className="financialSignalCard"
                    key={`${finding.title}-${index}`}
                  >
                    <span className={`severityPill severity-${finding.severity}`}>
                      {finding.severity}
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
                  <strong>Data quality notes</strong>
                  <ul>
                    {aiReview.dataQualityNotes.map((note, index) => (
                      <li key={`${note.message}-${index}`}>
                        [{note.severity}] {note.message}
                      </li>
                    ))}
                  </ul>
                </div>
              )}

              {aiReview.limitations.length > 0 && (
                <div>
                  <strong>AI review limitations</strong>
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
                  {item.severity}
                </span>
                <strong>{formatSignalTitle(item.title)}</strong>
              </div>

              <p>{item.message}</p>

              <dl className="financialEvidenceMeta">
                <div>
                  <dt>Metric</dt>
                  <dd>{item.metric ?? "-"}</dd>
                </div>
                <div>
                  <dt>Period</dt>
                  <dd>{item.period ?? "-"}</dd>
                </div>
                <div>
                  <dt>Value</dt>
                  <dd>{formatNumber(item.value)}</dd>
                </div>
                <div>
                  <dt>Threshold</dt>
                  <dd>{formatNumber(item.threshold)}</dd>
                </div>
                <div>
                  <dt>Confidence</dt>
                  <dd>{formatPercent(item.confidence)}</dd>
                </div>
                <div>
                  <dt>Source page</dt>
                  <dd>{item.sourcePage ?? "-"}</dd>
                </div>
              </dl>
            </article>
          ))}
        </div>
      )}

      {visibleSignals.length > 0 && (
        <div className="financialSection">
          <strong>Risk signals</strong>
          <div className="financialSignalList">
            {visibleSignals.map((signal, index) => (
              <article
                className="financialSignalCard"
                key={`${signal.code}-${signal.metric}-${signal.period}-${index}`}
              >
                <span className={`severityPill severity-${signal.severity}`}>
                  {signal.severity}
                </span>
                <div>
                  <strong>{formatSignalTitle(signal.code)}</strong>
                  <p>{signal.explanation}</p>
                  <small>
                    {signal.metric} · {signal.period} · Value{" "}
                    {formatNumber(signal.value)} · Threshold{" "}
                    {formatNumber(signal.threshold)}
                  </small>
                </div>
              </article>
            ))}
          </div>
        </div>
      )}

      <div className="disclaimerBox">
        <strong>Heuristic Guardrail</strong>
        Risk thresholds are heuristic review criteria. They are not investment advice and do not confirm accounting correctness.
      </div>

      {financialAnalysis.thresholdsUsed && financialAnalysis.thresholdsUsed.length > 0 && (
        <div className="thresholdsCollapse">
          <div
            className="thresholdsCollapseHeader"
            onClick={() => setThresholdsExpanded(!thresholdsExpanded)}
            style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}
          >
            <span>Applied Thresholds ({financialAnalysis.thresholdsUsed.length})</span>
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
                        {t.severity}
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
          <strong>Key ratios</strong>
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
              <strong>Warnings</strong>
              <ul>
                {financialAnalysis.warnings.map((warning) => (
                  <li key={warning}>{warning}</li>
                ))}
              </ul>
            </div>
          )}

          {financialAnalysis.limitations.length > 0 && (
            <div>
              <strong>Limitations</strong>
              <ul>
                {financialAnalysis.limitations.map((limitation) => (
                  <li key={limitation}>{limitation}</li>
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
