import { useEffect, useState } from "react";
import * as signalR from "@microsoft/signalr";
import "./App.css";

type ActivityEvent = {
  sessionId: string;
  type: string;
  agent: string;
  message: string;
  timestamp: string;
};

type AnalysisSessionResponse = {
  id: string;
  status: string;
  createdAt?: string;
  updatedAt?: string;
  currentAgent?: string | null;
  contextJson?: string;
};

type AnalysisSessionSummary = {
  id: string;
  status: string;
  currentAgent?: string | null;
  createdAt: string;
  updatedAt: string;
  completedAt?: string | null;
};

type AnomalyEvidenceItem = {
  metric: string;
  value: number;
  threshold: number;
  interpretation: string;
};

type ComplianceEvidenceItem = {
  regulation: string;
  section: string;
  finding: string;
  source: string;
};

type ComplianceContext = {
  riskDetected: boolean;
  riskLevel: string;
  engine?: string;
  summary: string;
  evidence: ComplianceEvidenceItem[];
  warnings?: string[];
};

type PlannerContext = {
  engine?: string;
  summary: string;
  recommendedActions: string[];
  riskFactors: string[];
  limitations: string[];
  usedLlm?: boolean;
  usedFallback?: boolean;
  provider?: string | null;
  model?: string | null;
  failureReason?: string | null;
};

type ToolCallArguments = Record<string, string>;

type ProposedToolCallContext = {
  toolName: string;
  arguments: ToolCallArguments;
  reason: string;
};

type ApprovedToolCallContext = {
  toolName: string;
  arguments: ToolCallArguments;
  reason: string;
};

type RejectedToolCallContext = {
  toolName: string;
  reason: string;
};

type ExecutedToolCallContext = {
  toolName: string;
  status?: string;
  succeeded: boolean;
  summary: string;
  engine: string;
  error?: string | null;
};

type ToolPlanContext = {
  proposedCalls: ProposedToolCallContext[];
  approvedCalls: ApprovedToolCallContext[];
  rejectedCalls: RejectedToolCallContext[];
  executedCalls: ExecutedToolCallContext[];
};

type FinancialRatioContext = {
  name: string;
  period: string;
  value: number;
  unit?: string;
  formula?: string;
  source?: string;
  inputMetrics?: string[];
  sourcePage?: number | null;
  confidence?: number;
  interpretation?: string;
};

type FinancialComparisonContext = {
  metricName: string;
  fromPeriod: string;
  toPeriod: string;
  fromValue: number;
  toValue: number;
  absoluteChange: number;
  percentageChange?: number | null;
  unit?: string;
  interpretation: string;
};

type FinancialRiskSignalContext = {
  code: string;
  category: string;
  severity: string;
  metric: string;
  period: string;
  value?: number | null;
  threshold?: number | null;
  explanation: string;
  sourcePage?: number | null;
  confidence?: number;
};

type FinancialRiskEvidenceContext = {
  code: string;
  title: string;
  severity: string;
  message: string;
  metric?: string | null;
  period?: string | null;
  value?: number | null;
  threshold?: number | null;
  engine: string;
  sourceDocumentId?: string | null;
  sourcePage?: number | null;
  confidence?: number;
};

type FinancialAnalysisContext = {
  engine: string;
  documentId: string;
  company?: string | null;
  ratios: FinancialRatioContext[];
  comparisons: FinancialComparisonContext[];
  riskSignals: FinancialRiskSignalContext[];
  riskEvidence: FinancialRiskEvidenceContext[];
  warnings: string[];
  limitations: string[];
  metricsInputSource?: string | null;
  metricsProvenance?: StructuredFinancialMetricsProvenanceContext | null;
};

type StructuredFinancialMetricInput = {
  name: string;
  period: string;
  value: number | null;
  unit?: string | null;
  currency?: string | null;
  source?: string | null;
  sourcePage?: number | null;
  confidence?: number | null;
};

type StructuredFinancialMetricsInput = {
  documentId: string;
  company?: string | null;
  currency?: string | null;
  unit?: string | null;
  metrics: StructuredFinancialMetricInput[];
};

type StructuredFinancialMetricsCsvInput = {
  documentId: string;
  company?: string | null;
  currency?: string | null;
  unit?: string | null;
  csv: string;
};

type StructuredFinancialMetricsFileMetadata = {
  documentId?: string | null;
  company?: string | null;
  currency?: string | null;
  unit?: string | null;
};

type FinancialMetricsValidationIssue = {
  code: string;
  message: string;
  metricName?: string | null;
  period?: string | null;
  severity: string;
};

type StructuredFinancialMetricContext = {
  name: string;
  period: string;
  value: number;
  unit: string;
  statement?: string;
  source?: string | null;
  currency?: string | null;
  sourcePage?: number | null;
  confidence?: number | null;
};

type StructuredFinancialMetricsProvenanceContext = {
  ingestionMethod: string;
  originalFileName?: string | null;
  fileSizeBytes?: number | null;
  contentHash?: string | null;
  metricCount: number;
  warningCount: number;
};

type StructuredFinancialMetricsContext = {
  documentId: string;
  company?: string | null;
  currency?: string | null;
  unit?: string | null;
  metrics: StructuredFinancialMetricContext[];
  validationWarnings: FinancialMetricsValidationIssue[];
  uploadedAt: string;
  provenance?: StructuredFinancialMetricsProvenanceContext | null;
};

type SaveFinancialMetricsResponse = {
  sessionId: string;
  isValid: boolean;
  context?: StructuredFinancialMetricsContext | null;
  errors: FinancialMetricsValidationIssue[];
  warnings: FinancialMetricsValidationIssue[];
};

type GetFinancialMetricsResponse = {
  sessionId: string;
  context?: StructuredFinancialMetricsContext | null;
};

type FileUploadErrorResponse = {
  error?: string;
};

type AnalysisSessionStartPreflightIssue = {
  code: string;
  message: string;
  severity: string;
};

type AnalysisSessionStartPreflightResult = {
  canStart: boolean;
  errors: AnalysisSessionStartPreflightIssue[];
  warnings: AnalysisSessionStartPreflightIssue[];
};

type AnalysisContext = {
  summary?: string;
  planner?: PlannerContext;
  toolPlan?: ToolPlanContext;
  anomaly?: {
    detected: boolean;
    severity: string;
    engine?: string;
    category: string;
    summary: string;
    evidence: AnomalyEvidenceItem[];
    recommendation: string;
  };
  financialAnalysis?: FinancialAnalysisContext | null;
  compliance?: ComplianceContext;
};

const apiBaseUrl = import.meta.env.VITE_API_URL ?? "http://localhost:5148";
const maxStructuredMetricsFileSizeBytes = 1_048_576;
const allowedStructuredMetricsFileExtensions = [".json", ".csv"];
const sampleJsonTemplateUrl =
  "/templates/structured-financial-metrics-sample.json";
const sampleCsvTemplateUrl =
  "/templates/structured-financial-metrics-sample.csv";

const jsonMetricsTemplate = JSON.stringify(
  {
    documentId: "manual-json-input",
    company: "Manual Test Co",
    currency: "USD",
    unit: "USD_thousand",
    metrics: [
      {
        name: "Revenue",
        period: "2024A",
        value: 1647768,
        source: "manual_upload",
        sourcePage: 18,
        confidence: 0.9,
      },
      {
        name: "Gross Profit",
        period: "2024A",
        value: 924000,
        source: "manual_upload",
        sourcePage: 18,
        confidence: 0.85,
      },
    ],
  },
  null,
  2
);

const csvMetricsTemplate = [
  "name,period,value,unit,currency,source,sourcePage,confidence",
  "Revenue,2024A,1647768,USD_thousand,USD,manual_upload,18,0.9",
  "Gross Profit,2024A,924000,USD_thousand,USD,manual_upload,18,0.85",
].join("\n");

function parseAnomalyContext(contextJson?: string): AnalysisContext | null {
  if (!contextJson) {
    return null;
  }

  try {
    return JSON.parse(contextJson) as AnalysisContext;
  } catch {
    return null;
  }
}

function StatusBadge({ status }: { status: string }) {
  return <span className={`statusBadge status-${status}`}>{status}</span>;
}

function PlannerPanel({ planner }: { planner?: PlannerContext }) {
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

function PlannerList({ title, items }: { title: string; items: string[] }) {
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

function ToolPlanAuditPanel({ toolPlan }: { toolPlan?: ToolPlanContext }) {
  const hasToolPlan =
    toolPlan &&
    (toolPlan.proposedCalls.length > 0 ||
      toolPlan.approvedCalls.length > 0 ||
      toolPlan.rejectedCalls.length > 0 ||
      toolPlan.executedCalls.length > 0);

  if (!hasToolPlan) {
    return null;
  }

  return (
    <section className="toolPlanPanel">
      <div className="toolPlanHeader">
        <div>
          <p className="toolPlanEyebrow">Tool plan audit</p>
          <h2>Controlled tool calling trail</h2>
          <p>Proposed, validated, rejected, and execution-policy decisions.</p>
        </div>
      </div>

      <div className="toolPlanGrid">
        <ToolCallGroup
          title="Proposed"
          tone="neutral"
          calls={toolPlan.proposedCalls.map((call) => ({
            toolName: call.toolName,
            detail: call.reason,
          }))}
        />
        <ToolCallGroup
          title="Approved"
          tone="success"
          calls={toolPlan.approvedCalls.map((call) => ({
            toolName: call.toolName,
            detail: call.reason,
          }))}
        />
        <ToolCallGroup
          title="Rejected"
          tone="warning"
          calls={toolPlan.rejectedCalls.map((call) => ({
            toolName: call.toolName,
            detail: call.reason,
          }))}
        />
        <ToolCallGroup
          title="Execution audit"
          tone="neutral"
          calls={toolPlan.executedCalls.map((call) => ({
            toolName: call.toolName,
            detail: call.error ?? call.summary,
            meta: `${call.status ?? (call.succeeded ? "Executed" : "Failed")} - ${call.engine}`,
            status: call.status ?? (call.succeeded ? "Executed" : "Failed"),
            statusTone: getToolExecutionTone(call.status, call.succeeded),
          }))}
        />
      </div>
    </section>
  );
}

function ToolCallGroup({
  title,
  tone,
  calls,
}: {
  title: string;
  tone: "neutral" | "success" | "warning" | "danger";
  calls: {
    toolName: string;
    detail: string;
    meta?: string;
    status?: string;
    statusTone?: "neutral" | "success" | "warning" | "danger";
  }[];
}) {
  if (calls.length === 0) {
    return null;
  }

  return (
    <div className={`toolCallGroup toolCallGroup-${tone}`}>
      <strong>{title}</strong>

      <div className="toolCallList">
        {calls.map((call, index) => (
          <article className="toolCallCard" key={`${call.toolName}-${index}`}>
            <span>{call.toolName}</span>
            {call.status && (
              <em className={`toolCallStatus toolCallStatus-${call.statusTone ?? "neutral"}`}>
                {call.status}
              </em>
            )}
            {call.meta && <small>{call.meta}</small>}
            <p>{call.detail}</p>
          </article>
        ))}
      </div>
    </div>
  );
}

function getToolExecutionTone(
  status?: string,
  succeeded?: boolean
): "neutral" | "success" | "warning" | "danger" {
  if (status === "Failed" || succeeded === false) {
    return "danger";
  }

  if (status === "Executed") {
    return "success";
  }

  if (status === "SkippedDisabled") {
    return "warning";
  }

  return "neutral";
}

function EvidencePanel({ anomaly }: { anomaly: AnalysisContext["anomaly"] }) {
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
              Analysis engine: <strong>{anomaly.engine}</strong>
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

function FinancialRiskEvidencePanel({
  financialAnalysis,
}: {
  financialAnalysis?: FinancialAnalysisContext | null;
}) {
  if (!financialAnalysis) {
    return null;
  }

  const visibleEvidence = financialAnalysis.riskEvidence.slice(0, 6);
  const visibleSignals = financialAnalysis.riskSignals.slice(0, 6);
  const visibleRatios = financialAnalysis.ratios.slice(0, 6);
  const metricsInputSource = financialAnalysis.metricsInputSource ?? "unknown";
  const isFixtureFallback = metricsInputSource === "fixture_fallback";
  const hasNoMetrics = metricsInputSource === "none";
  const requiresSessionMetrics = financialAnalysis.warnings.some((warning) =>
    warning.includes("required for this mode")
  );

  return (
    <section className="financialRiskPanel">
      <div className="financialRiskHeader">
        <div>
          <p className="financialRiskEyebrow">Financial risk evidence</p>
          <h2>{financialAnalysis.company ?? "Structured financial metrics"}</h2>
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

function StructuredFinancialMetricsPanel({
  sessionId,
  metricsContext,
  isLoading,
  isSaving,
  saveResult,
  saveError,
  onSaveJson,
  onSaveCsv,
  onUploadFile,
}: {
  sessionId?: string;
  metricsContext?: StructuredFinancialMetricsContext | null;
  isLoading: boolean;
  isSaving: boolean;
  saveResult?: SaveFinancialMetricsResponse | null;
  saveError?: string | null;
  onSaveJson: (input: StructuredFinancialMetricsInput) => Promise<void>;
  onSaveCsv: (input: StructuredFinancialMetricsCsvInput) => Promise<void>;
  onUploadFile: (
    file: File,
    metadata: StructuredFinancialMetricsFileMetadata
  ) => Promise<void>;
}) {
  const [mode, setMode] = useState<"json" | "csv" | "file">("json");
  const [jsonText, setJsonText] = useState(jsonMetricsTemplate);
  const [csvDocumentId, setCsvDocumentId] = useState("manual-csv-input");
  const [csvCompany, setCsvCompany] = useState("Manual Test Co");
  const [csvCurrency, setCsvCurrency] = useState("USD");
  const [csvUnit, setCsvUnit] = useState("USD_thousand");
  const [csvText, setCsvText] = useState(csvMetricsTemplate);
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [fileDocumentId, setFileDocumentId] = useState("manual-file-input");
  const [fileCompany, setFileCompany] = useState("Manual Test Co");
  const [fileCurrency, setFileCurrency] = useState("USD");
  const [fileUnit, setFileUnit] = useState("USD_thousand");
  const [inputError, setInputError] = useState<string | null>(null);

  async function handleSaveJson() {
    setInputError(null);

    try {
      const parsed = JSON.parse(jsonText) as StructuredFinancialMetricsInput;
      await onSaveJson(parsed);
    } catch (error) {
      console.error(error);
      setInputError("Invalid JSON format.");
    }
  }

  async function handleSaveCsv() {
    setInputError(null);

    await onSaveCsv({
      documentId: csvDocumentId,
      company: csvCompany,
      currency: csvCurrency,
      unit: csvUnit,
      csv: csvText,
    });
  }

  async function loadSampleJson() {
    setInputError(null);

    try {
      const response = await fetch(sampleJsonTemplateUrl);

      if (!response.ok) {
        throw new Error("Sample JSON template could not be loaded.");
      }

      setJsonText(await response.text());
      setMode("json");
    } catch (error) {
      console.error(error);
      setInputError("Sample JSON template could not be loaded.");
    }
  }

  async function loadSampleCsv() {
    setInputError(null);

    try {
      const response = await fetch(sampleCsvTemplateUrl);

      if (!response.ok) {
        throw new Error("Sample CSV template could not be loaded.");
      }

      setCsvText(await response.text());
      setCsvDocumentId("sample-csv-metrics");
      setCsvCompany("Sample Energy Co");
      setCsvCurrency("USD");
      setCsvUnit("USD_thousand");
      setMode("csv");
    } catch (error) {
      console.error(error);
      setInputError("Sample CSV template could not be loaded.");
    }
  }

  function handleFileSelected(file: File | null) {
    setInputError(null);
    setSelectedFile(file);
  }

  async function handleUploadFile() {
    setInputError(null);

    if (!selectedFile) {
      setInputError("Select a JSON or CSV metrics file.");
      return;
    }

    const extension = getFileExtension(selectedFile.name);

    if (!allowedStructuredMetricsFileExtensions.includes(extension)) {
      setInputError("Only .json and .csv files are supported.");
      return;
    }

    if (selectedFile.size > maxStructuredMetricsFileSizeBytes) {
      setInputError("Only files up to 1 MB are supported.");
      return;
    }

    if (extension === ".csv" && fileDocumentId.trim().length === 0) {
      setInputError("DocumentId is required for CSV uploads.");
      return;
    }

    await onUploadFile(selectedFile, {
      documentId: fileDocumentId,
      company: fileCompany,
      currency: fileCurrency,
      unit: fileUnit,
    });
  }

  return (
    <section className="structuredMetricsPanel">
      <div className="structuredMetricsHeader">
        <div>
          <p className="structuredMetricsEyebrow">Structured metrics input</p>
          <h2>Attach financial metrics</h2>
          <p>
            Structured validation checks required fields, normalization and
            basic quality issues. It does not verify accounting correctness.
          </p>
        </div>

        <div className="structuredMetricsState">
          {!sessionId ? (
            <span>Create or load a session before attaching structured financial metrics.</span>
          ) : isLoading ? (
            <span>Loading structured metrics...</span>
          ) : metricsContext ? (
            <>
              <strong>Structured metrics attached</strong>
              <span>Document: {metricsContext.documentId}</span>
              <span>Company: {metricsContext.company ?? "-"}</span>
              <span>
                Source:{" "}
                {formatIngestionMethod(
                  metricsContext.provenance?.ingestionMethod
                )}
              </span>
              {metricsContext.provenance?.originalFileName && (
                <span>File: {metricsContext.provenance.originalFileName}</span>
              )}
              <span>
                Metrics:{" "}
                {metricsContext.provenance?.metricCount ??
                  metricsContext.metrics.length}
              </span>
              <span>
                Warnings:{" "}
                {metricsContext.provenance?.warningCount ??
                  metricsContext.validationWarnings.length}
              </span>
              <span>Uploaded: {new Date(metricsContext.uploadedAt).toLocaleString()}</span>
            </>
          ) : (
            <span>No structured financial metrics attached to this session.</span>
          )}
        </div>
      </div>

      <div className="metricsTemplateBox">
        <div>
          <strong>Templates</strong>
          <p>
            Use these samples to match the expected structured financial
            metrics format.
          </p>
        </div>

        <div className="templateActions">
          <a href={sampleJsonTemplateUrl} download>
            Download sample JSON
          </a>
          <button onClick={loadSampleJson} type="button">
            Load sample JSON
          </button>
          <a href={sampleCsvTemplateUrl} download>
            Download sample CSV
          </a>
          <button onClick={loadSampleCsv} type="button">
            Load sample CSV
          </button>
        </div>
      </div>

      <div className="metricsModeToggle" role="tablist" aria-label="Metrics input mode">
        <button
          className={mode === "json" ? "active" : ""}
          onClick={() => setMode("json")}
          type="button"
        >
          JSON
        </button>
        <button
          className={mode === "csv" ? "active" : ""}
          onClick={() => setMode("csv")}
          type="button"
        >
          CSV
        </button>
        <button
          className={mode === "file" ? "active" : ""}
          onClick={() => setMode("file")}
          type="button"
        >
          File Upload
        </button>
      </div>

      {mode === "json" ? (
        <div className="metricsEditor">
          <label>
            JSON metrics
            <textarea
              value={jsonText}
              onChange={(event) => setJsonText(event.target.value)}
              spellCheck={false}
            />
          </label>

          <button
            onClick={handleSaveJson}
            disabled={!sessionId || isSaving}
            type="button"
          >
            {isSaving ? "Saving..." : "Save JSON Metrics"}
          </button>
        </div>
      ) : mode === "csv" ? (
        <div className="metricsEditor">
          <div className="csvMetaGrid">
            <label>
              DocumentId
              <input
                value={csvDocumentId}
                onChange={(event) => setCsvDocumentId(event.target.value)}
              />
            </label>
            <label>
              Company
              <input
                value={csvCompany}
                onChange={(event) => setCsvCompany(event.target.value)}
              />
            </label>
            <label>
              Currency
              <input
                value={csvCurrency}
                onChange={(event) => setCsvCurrency(event.target.value)}
              />
            </label>
            <label>
              Unit
              <input
                value={csvUnit}
                onChange={(event) => setCsvUnit(event.target.value)}
              />
            </label>
          </div>

          <label>
            CSV metrics
            <textarea
              value={csvText}
              onChange={(event) => setCsvText(event.target.value)}
              spellCheck={false}
            />
          </label>

          <button
            onClick={handleSaveCsv}
            disabled={!sessionId || isSaving}
            type="button"
          >
            {isSaving ? "Saving..." : "Save CSV Metrics"}
          </button>
        </div>
      ) : (
        <div className="metricsEditor">
          <div className="fileUploadBox">
            <label>
              JSON or CSV file
              <input
                type="file"
                accept=".json,.csv"
                disabled={!sessionId || isSaving}
                onChange={(event) =>
                  handleFileSelected(event.currentTarget.files?.[0] ?? null)
                }
              />
            </label>

            <p>Only .json and .csv files up to 1 MB are supported.</p>

            {selectedFile && (
              <div className="selectedFileSummary">
                <strong>Selected file</strong>
                <span>{selectedFile.name}</span>
                <span>{formatFileSize(selectedFile.size)}</span>
              </div>
            )}
          </div>

          <div className="csvMetaGrid">
            <label>
              DocumentId
              <input
                value={fileDocumentId}
                disabled={!sessionId || isSaving}
                onChange={(event) => setFileDocumentId(event.target.value)}
              />
            </label>
            <label>
              Company
              <input
                value={fileCompany}
                disabled={!sessionId || isSaving}
                onChange={(event) => setFileCompany(event.target.value)}
              />
            </label>
            <label>
              Currency
              <input
                value={fileCurrency}
                disabled={!sessionId || isSaving}
                onChange={(event) => setFileCurrency(event.target.value)}
              />
            </label>
            <label>
              Unit
              <input
                value={fileUnit}
                disabled={!sessionId || isSaving}
                onChange={(event) => setFileUnit(event.target.value)}
              />
            </label>
          </div>

          <button
            onClick={handleUploadFile}
            disabled={!sessionId || isSaving || !selectedFile}
            type="button"
          >
            {isSaving ? "Uploading..." : "Upload File"}
          </button>
        </div>
      )}

      {(inputError || saveError) && (
        <div className="metricsResult metricsResult-danger">
          {inputError ?? saveError}
        </div>
      )}

      {saveResult && (
        <div
          className={`metricsResult ${
            saveResult.isValid ? "metricsResult-success" : "metricsResult-danger"
          }`}
        >
          <strong>
            {saveResult.isValid
              ? mode === "file"
                ? "File metrics saved successfully"
                : "Saved successfully"
              : "Metrics were not persisted."}
          </strong>

          <IssueList title="Errors" issues={saveResult.errors} />
          <IssueList title="Warnings" issues={saveResult.warnings} />
        </div>
      )}
    </section>
  );
}

function IssueList({
  title,
  issues,
}: {
  title: string;
  issues: FinancialMetricsValidationIssue[];
}) {
  if (issues.length === 0) {
    return null;
  }

  return (
    <div className="issueList">
      <span>{title}</span>
      <ul>
        {issues.map((issue, index) => (
          <li key={`${issue.code}-${index}`}>
            <strong>[{issue.severity}] {issue.code}</strong> - {issue.message}
            {(issue.metricName || issue.period) && (
              <em>
                {issue.metricName ? ` Metric: ${issue.metricName}` : ""}
                {issue.period ? ` Period: ${issue.period}` : ""}
              </em>
            )}
          </li>
        ))}
      </ul>
    </div>
  );
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

function formatFileSize(size: number) {
  if (size < 1024) {
    return `${size} B`;
  }

  if (size < 1024 * 1024) {
    return `${(size / 1024).toFixed(1)} KB`;
  }

  return `${(size / (1024 * 1024)).toFixed(2)} MB`;
}

function getFileExtension(fileName: string) {
  const dotIndex = fileName.lastIndexOf(".");

  return dotIndex >= 0
    ? fileName.slice(dotIndex).toLowerCase()
    : "";
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

function formatPercent(value?: number | null) {
  if (value === null || value === undefined) {
    return "-";
  }

  return `${Math.round(value * 100)}%`;
}

function CompliancePanel({
  compliance,
}: {
  compliance?: ComplianceContext;
}) {
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

function getEventTone(type: string) {
  if (type.includes("tool_call_rejected")) {
    return "event-warning";
  }

  if (type.includes("tool_call_skipped")) {
    return "event-info";
  }

  if (type.includes("tool_call_executed")) {
    return "event-success";
  }

  if (
    type.includes("tool_plan_proposed") ||
    type.includes("tool_plan_validated") ||
    type.includes("structured_financial_metrics_attached")
  ) {
    return "event-info";
  }

  if (
    type.includes("planner_reasoning_fallback_used") ||
    type.includes("tool_execution_fallback_used") ||
    type.includes("analysis_start_blocked") ||
    type.includes("financial_metrics_fixture_fallback_used") ||
    type.includes("financial_metrics_required_missing")
  ) {
    return "event-warning";
  }

  if (
    type.includes("planner_reasoning_completed") ||
    type.includes("llm_reasoning")
  ) {
    return "event-info";
  }

  if (type.includes("anomaly")) {
    return "event-danger";
  }

  if (type.includes("human_approval")) {
    return "event-warning";
  }

  if (type.includes("rejected") || type.includes("failed")) {
    return "event-danger";
  }

  if (type.includes("completed") || type.includes("approved")) {
    return "event-success";
  }

  return "event-neutral";
}

async function readJsonOrNull(response: Response) {
  try {
    return (await response.json()) as unknown;
  } catch {
    return null;
  }
}

function isStartPreflightResult(
  value: unknown
): value is AnalysisSessionStartPreflightResult {
  if (!value || typeof value !== "object") {
    return false;
  }

  const candidate = value as Partial<AnalysisSessionStartPreflightResult>;

  return (
    typeof candidate.canStart === "boolean" &&
    Array.isArray(candidate.errors) &&
    Array.isArray(candidate.warnings)
  );
}

function getStartPreflightErrorMessage(
  preflight: AnalysisSessionStartPreflightResult
) {
  const missingMetricsIssue = preflight.errors.find(
    (issue) => issue.code === "STRUCTURED_FINANCIAL_METRICS_REQUIRED"
  );

  if (missingMetricsIssue) {
    return "Structured financial metrics are required before starting this analysis. Attach JSON/CSV metrics and try again.";
  }

  return (
    preflight.errors[0]?.message ??
    "The analysis session could not be started."
  );
}

function getConflictErrorMessage(value: unknown) {
  if (!value || typeof value !== "object") {
    return null;
  }

  const candidate = value as { error?: unknown };

  return typeof candidate.error === "string" ? candidate.error : null;
}

async function getStartPreflight(
  sessionId: string
): Promise<AnalysisSessionStartPreflightResult> {
  const response = await fetch(
    `${apiBaseUrl}/api/analysis-sessions/${sessionId}/start-preflight`
  );

  if (!response.ok) {
    throw new Error("Failed to check start readiness.");
  }

  return (await response.json()) as AnalysisSessionStartPreflightResult;
}

function StartReadinessPanel({
  sessionId,
  preflight,
  isChecking,
  error,
}: {
  sessionId?: string;
  preflight: AnalysisSessionStartPreflightResult | null;
  isChecking: boolean;
  error: string | null;
}) {
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
          <p>Attach JSON/CSV structured metrics before starting this analysis.</p>
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

function App() {
  const [connectionStatus, setConnectionStatus] = useState("Disconnected");
  const [session, setSession] = useState<AnalysisSessionResponse | null>(null);
  const [events, setEvents] = useState<ActivityEvent[]>([]);
  const [isCreating, setIsCreating] = useState(false);
  const [isStarting, setIsStarting] = useState(false);
  const [decisionReason, setDecisionReason] = useState("");
  const [isSubmittingDecision, setIsSubmittingDecision] = useState(false);
  const [savedSessions, setSavedSessions] = useState<AnalysisSessionSummary[]>(
    []
  );
  const [selectedSessionId, setSelectedSessionId] = useState("");
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [structuredMetrics, setStructuredMetrics] =
    useState<StructuredFinancialMetricsContext | null>(null);
  const [isLoadingStructuredMetrics, setIsLoadingStructuredMetrics] =
    useState(false);
  const [isSavingStructuredMetrics, setIsSavingStructuredMetrics] =
    useState(false);
  const [metricsSaveResult, setMetricsSaveResult] =
    useState<SaveFinancialMetricsResponse | null>(null);
  const [metricsSaveError, setMetricsSaveError] = useState<string | null>(null);
  const [startPreflight, setStartPreflight] =
    useState<AnalysisSessionStartPreflightResult | null>(null);
  const [isCheckingStartPreflight, setIsCheckingStartPreflight] =
    useState(false);
  const [startPreflightError, setStartPreflightError] = useState<string | null>(
    null
  );

  useEffect(() => {
    let isDisposed = false;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${apiBaseUrl}/hubs/activity`)
      .withAutomaticReconnect()
      .build();

    const handleActivityEvent = (event: ActivityEvent) => {
      setEvents((currentEvents) => {
        const alreadyExists = currentEvents.some(
          (currentEvent) =>
            currentEvent.sessionId === event.sessionId &&
            currentEvent.type === event.type &&
            currentEvent.agent === event.agent &&
            currentEvent.message === event.message &&
            currentEvent.timestamp === event.timestamp
        );

        if (alreadyExists) {
          return currentEvents;
        }

        return [event, ...currentEvents];
      });
    };

    connection.on("activityEventReceived", handleActivityEvent);

    connection.onreconnecting(() => {
      if (!isDisposed) {
        setConnectionStatus("Reconnecting");
      }
    });

    connection.onreconnected(() => {
      if (!isDisposed) {
        setConnectionStatus("Connected");
      }
    });

    connection.onclose(() => {
      if (!isDisposed) {
        setConnectionStatus("Disconnected");
      }
    });

    connection
      .start()
      .then(() => {
        if (!isDisposed) {
          setConnectionStatus("Connected");
        }
      })
      .catch((error) => {
        if (isDisposed) {
          return;
        }

        console.error("SignalR connection failed:", error);
        setConnectionStatus("Failed");
      });

    return () => {
      isDisposed = true;
      connection.off("activityEventReceived", handleActivityEvent);
      void connection.stop();
    };
  }, []);

  useEffect(() => {
    loadSavedSessions().catch((error) => {
      console.error("Failed to load saved sessions:", error);
    });
  }, []);

  useEffect(() => {
    setMetricsSaveResult(null);
    setMetricsSaveError(null);

    if (!session?.id) {
      setStructuredMetrics(null);
      setStartPreflight(null);
      setStartPreflightError(null);
      setIsCheckingStartPreflight(false);
      return;
    }

    setStartPreflight(null);
    setStartPreflightError(null);

    loadStructuredFinancialMetrics(session.id).catch((error) => {
      console.error("Failed to load structured financial metrics:", error);
      setStructuredMetrics(null);
    });

    refreshStartPreflight(session.id).catch((error) => {
      console.error("Failed to load start preflight:", error);
    });
  }, [session?.id]);

  async function loadSessionEvents(sessionId: string) {
    const response = await fetch(
      `${apiBaseUrl}/api/analysis-sessions/${sessionId}/events`
    );

    if (!response.ok) {
      throw new Error("Failed to load session events.");
    }

    const historicalEvents = (await response.json()) as ActivityEvent[];

    setEvents(historicalEvents);
  }

  async function loadSavedSessions() {
    const response = await fetch(`${apiBaseUrl}/api/analysis-sessions`);

    if (!response.ok) {
      throw new Error("Failed to load saved analysis sessions.");
    }

    const sessions = (await response.json()) as AnalysisSessionSummary[];

    setSavedSessions(sessions);

    if (sessions.length > 0 && !selectedSessionId) {
      setSelectedSessionId(sessions[0].id);
    }
  }

  async function loadSessionDetails(sessionId: string) {
    const response = await fetch(`${apiBaseUrl}/api/analysis-sessions/${sessionId}`);

    if (!response.ok) {
      throw new Error("Failed to load analysis session.");
    }

    return (await response.json()) as AnalysisSessionResponse;
  }

  async function loadStructuredFinancialMetrics(sessionId: string) {
    setIsLoadingStructuredMetrics(true);

    try {
      const response = await fetch(
        `${apiBaseUrl}/api/analysis-sessions/${sessionId}/financial-metrics`
      );

      if (!response.ok) {
        throw new Error("Failed to load structured financial metrics.");
      }

      const payload = (await response.json()) as GetFinancialMetricsResponse;
      setStructuredMetrics(payload.context ?? null);
    } finally {
      setIsLoadingStructuredMetrics(false);
    }
  }

  async function refreshStartPreflight(sessionId: string) {
    setIsCheckingStartPreflight(true);
    setStartPreflightError(null);

    try {
      const preflight = await getStartPreflight(sessionId);
      setStartPreflight(preflight);
    } catch (error) {
      console.error(error);
      setStartPreflight(null);
      setStartPreflightError(
        "Could not check start readiness. Backend preflight will still run when starting."
      );
    } finally {
      setIsCheckingStartPreflight(false);
    }
  }

  async function createSession() {
    setIsCreating(true);
    setErrorMessage(null);

    try {
      const response = await fetch(`${apiBaseUrl}/api/analysis-sessions`, {
        method: "POST",
      });

      if (!response.ok) {
        throw new Error("Failed to create analysis session.");
      }

      const createdSession =
        (await response.json()) as AnalysisSessionResponse;

      setSession(createdSession);
      setEvents([]);
      setStructuredMetrics(null);
      setMetricsSaveResult(null);
      setMetricsSaveError(null);
      setSelectedSessionId(createdSession.id);

      await loadSavedSessions();
      await refreshStartPreflight(createdSession.id);
    } catch (error) {
      console.error(error);
      setErrorMessage("Could not create the analysis session.");
    } finally {
      setIsCreating(false);
    }
  }

  async function startSession() {
    if (!session) {
      return;
    }

    setIsStarting(true);
    setErrorMessage(null);

    try {
      const response = await fetch(
        `${apiBaseUrl}/api/analysis-sessions/${session.id}/start`,
        {
          method: "POST",
        }
      );

      if (!response.ok) {
        if (response.status === 409) {
          const conflictPayload = await readJsonOrNull(response);

          if (isStartPreflightResult(conflictPayload)) {
            setStartPreflight(conflictPayload);
            setErrorMessage(getStartPreflightErrorMessage(conflictPayload));
            await loadSessionEvents(session.id);
            return;
          }

          setErrorMessage(
            getConflictErrorMessage(conflictPayload) ??
              "Could not start the analysis session."
          );
          return;
        }

        throw new Error("Failed to start analysis session.");
      }

      const updatedSession =
        (await response.json()) as AnalysisSessionResponse;

      setSession((currentSession) => ({
        ...currentSession,
        ...updatedSession,
      }));

      await loadSessionEvents(updatedSession.id);

      await loadSavedSessions();
      await refreshStartPreflight(updatedSession.id);
    } catch (error) {
      console.error(error);
      setErrorMessage("Could not start the analysis session.");
    } finally {
      setIsStarting(false);
    }
  }

  async function submitHumanDecision(decision: "approve" | "reject") {
    if (!session) {
      return;
    }

    setIsSubmittingDecision(true);
    setErrorMessage(null);

    try {
      const response = await fetch(
        `${apiBaseUrl}/api/analysis-sessions/${session.id}/${decision}`,
        {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
          },
          body: JSON.stringify({
            reason:
              decisionReason.trim().length > 0
                ? decisionReason
                : decision === "approve"
                  ? "Approved by human auditor."
                  : "Rejected by human auditor.",
          }),
        }
      );

      if (!response.ok) {
        throw new Error(`Failed to ${decision} analysis session.`);
      }

      const updatedSession =
        (await response.json()) as AnalysisSessionResponse;

      setSession((currentSession) => ({
        ...currentSession,
        ...updatedSession,
      }));

      await loadSessionEvents(updatedSession.id);

      await loadSavedSessions();

      setDecisionReason("");
    } catch (error) {
      console.error(error);
      setErrorMessage(`Could not ${decision} the analysis session.`);
    } finally {
      setIsSubmittingDecision(false);
    }
  }

  async function loadExistingSession(sessionId?: string) {
    const idToLoad = sessionId ?? selectedSessionId;

    if (!idToLoad) {
      return;
    }

    setErrorMessage(null);

    try {
      const loadedSession = await loadSessionDetails(idToLoad);

      setSession(loadedSession);

      await loadSessionEvents(loadedSession.id);
      await refreshStartPreflight(loadedSession.id);
    } catch (error) {
      console.error(error);
      setErrorMessage("Could not load the analysis session.");
    }
  }

  async function saveJsonMetrics(input: StructuredFinancialMetricsInput) {
    if (!session) {
      return;
    }

    await saveMetrics(
      `${apiBaseUrl}/api/analysis-sessions/${session.id}/financial-metrics`,
      input
    );
  }

  async function saveCsvMetrics(input: StructuredFinancialMetricsCsvInput) {
    if (!session) {
      return;
    }

    await saveMetrics(
      `${apiBaseUrl}/api/analysis-sessions/${session.id}/financial-metrics/csv`,
      input
    );
  }

  async function uploadFinancialMetricsFile(
    file: File,
    metadata: StructuredFinancialMetricsFileMetadata
  ) {
    if (!session) {
      return;
    }

    setIsSavingStructuredMetrics(true);
    setMetricsSaveError(null);
    setMetricsSaveResult(null);

    try {
      const formData = new FormData();
      formData.append("file", file);

      if (metadata.documentId?.trim()) {
        formData.append("documentId", metadata.documentId.trim());
      }

      if (metadata.company?.trim()) {
        formData.append("company", metadata.company.trim());
      }

      if (metadata.currency?.trim()) {
        formData.append("currency", metadata.currency.trim());
      }

      if (metadata.unit?.trim()) {
        formData.append("unit", metadata.unit.trim());
      }

      const response = await fetch(
        `${apiBaseUrl}/api/analysis-sessions/${session.id}/financial-metrics/file`,
        {
          method: "POST",
          body: formData,
        }
      );

      if (!response.ok) {
        const uploadError = (await response
          .json()
          .catch(() => ({}))) as FileUploadErrorResponse;

        throw new Error(
          uploadError.error ??
            "File upload failed. Please check the file format and try again."
        );
      }

      const result = (await response.json()) as SaveFinancialMetricsResponse;
      setMetricsSaveResult(result);

      if (result.isValid) {
        await loadStructuredFinancialMetrics(session.id);
        setSession(await loadSessionDetails(session.id));
      }

      await refreshStartPreflight(session.id);
    } catch (error) {
      console.error(error);
      setMetricsSaveError(
        error instanceof Error
          ? error.message
          : "File upload failed. Please check the file format and try again."
      );
    } finally {
      setIsSavingStructuredMetrics(false);
    }
  }

  async function saveMetrics(
    url: string,
    payload: StructuredFinancialMetricsInput | StructuredFinancialMetricsCsvInput
  ) {
    if (!session) {
      return;
    }

    setIsSavingStructuredMetrics(true);
    setMetricsSaveError(null);
    setMetricsSaveResult(null);

    try {
      const response = await fetch(url, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
        },
        body: JSON.stringify(payload),
      });

      if (!response.ok) {
        throw new Error("Failed to save structured financial metrics.");
      }

      const result = (await response.json()) as SaveFinancialMetricsResponse;
      setMetricsSaveResult(result);

      if (result.isValid) {
        await loadStructuredFinancialMetrics(session.id);
        setSession(await loadSessionDetails(session.id));
      }

      await refreshStartPreflight(session.id);
    } catch (error) {
      console.error(error);
      setMetricsSaveError("Could not save structured financial metrics.");
    } finally {
      setIsSavingStructuredMetrics(false);
    }
  }

  const latestEvent = events[0];
  const analysisContext = parseAnomalyContext(session?.contextJson);
  const planner = analysisContext?.planner;
  const toolPlan = analysisContext?.toolPlan;
  const anomaly = analysisContext?.anomaly;
  const financialAnalysis = analysisContext?.financialAnalysis;
  const hasFinancialAnalysis = Boolean(financialAnalysis);
  const compliance = analysisContext?.compliance;
  const isStartBlockedByPreflight = startPreflight?.canStart === false;

  return (
    <main className="page">
      <section className="shell">
        <header className="header">
          <div>
            <p className="eyebrow">LLM-Ready Orchestration Platform</p>
            <h1>Financial Analysis Control Room</h1>
            <p className="subtitle">
              Real-time activity feed for supervised agent workflows.
            </p>
          </div>

          <div className={`connectionBadge ${connectionStatus.toLowerCase()}`}>
            SignalR: <strong>{connectionStatus}</strong>
          </div>
        </header>

        <section className="actions" aria-label="Session actions">
          <button onClick={createSession} disabled={isCreating}>
            {isCreating ? "Creating..." : "Create Analysis Session"}
          </button>

          <button
            onClick={startSession}
            disabled={!session || isStarting || isStartBlockedByPreflight}
            title={
              isStartBlockedByPreflight
                ? "Start blocked by preflight"
                : undefined
            }
          >
            {isStarting
              ? "Starting..."
              : isStartBlockedByPreflight
                ? "Start blocked by preflight"
                : "Start Session"}
          </button>
        </section>

        <StartReadinessPanel
          sessionId={session?.id}
          preflight={startPreflight}
          isChecking={isCheckingStartPreflight}
          error={startPreflightError}
        />

        <section className="loadSessionPanel">
          <label>
            Review previous analysis
            <div className="loadSessionRow">
              <select
                value={selectedSessionId}
                onChange={(event) => setSelectedSessionId(event.target.value)}
              >
                {savedSessions.length === 0 ? (
                  <option value="">No saved sessions yet</option>
                ) : (
                  savedSessions.map((savedSession) => (
                    <option key={savedSession.id} value={savedSession.id}>
                      {new Date(savedSession.createdAt).toLocaleString()} ·{" "}
                      {savedSession.status} · {savedSession.id.slice(0, 8)}
                    </option>
                  ))
                )}
              </select>

              <button
                onClick={() => loadExistingSession()}
                disabled={!selectedSessionId}
              >
                Load Session
              </button>

              <button onClick={loadSavedSessions}>Refresh</button>
            </div>
          </label>
        </section>

        {errorMessage && <p className="errorMessage">{errorMessage}</p>}

        <section className="dashboardGrid">
          {session && (
            <section className="sessionCard">
              <div className="sessionHeader">
                <div>
                  <span className="label">Current session</span>
                  <code>{session.id}</code>
                </div>

                <StatusBadge status={session.status} />
              </div>

              <div className="sessionGrid">
                <div>
                  <span>Current agent</span>
                  <strong>{session.currentAgent ?? "None"}</strong>
                </div>

                <div>
                  <span>Created</span>
                  <strong>
                    {session.createdAt
                      ? new Date(session.createdAt).toLocaleString()
                      : "-"}
                  </strong>
                </div>

                <div>
                  <span>Updated</span>
                  <strong>
                    {session.updatedAt
                      ? new Date(session.updatedAt).toLocaleString()
                      : "-"}
                  </strong>
                </div>
              </div>
            </section>
          )}

          <StructuredFinancialMetricsPanel
            sessionId={session?.id}
            metricsContext={structuredMetrics}
            isLoading={isLoadingStructuredMetrics}
            isSaving={isSavingStructuredMetrics}
            saveResult={metricsSaveResult}
            saveError={metricsSaveError}
            onSaveJson={saveJsonMetrics}
            onSaveCsv={saveCsvMetrics}
            onUploadFile={uploadFinancialMetricsFile}
          />

          <PlannerPanel planner={planner} />
          <ToolPlanAuditPanel toolPlan={toolPlan} />
          {!hasFinancialAnalysis && <EvidencePanel anomaly={anomaly} />}
          {hasFinancialAnalysis && (
            <FinancialRiskEvidencePanel financialAnalysis={financialAnalysis} />
          )}
          <CompliancePanel compliance={compliance} />

          {session?.status === "Completed" && (
            <div className="finalDecision finalDecisionSuccess">
              Human auditor approved this workflow. The analysis was completed.
            </div>
          )}

          {session?.status === "Failed" && (
            <div className="finalDecision finalDecisionDanger">
              Human auditor rejected this workflow. The analysis was stopped.
            </div>
          )}

          {session?.status === "AwaitingHumanApproval" && (
            <section className="approvalPanel">
              <div>
                <p className="approvalEyebrow">Human intervention required</p>
                <h2>High-severity anomaly detected</h2>
                <p>
                  The supervised workflow has been paused. A human auditor must
                  review the evidence before the system can continue or
                  terminate the analysis.
                </p>
              </div>

              <label className="reasonField">
                Auditor reason
                <textarea
                  value={decisionReason}
                  onChange={(event) => setDecisionReason(event.target.value)}
                  placeholder="Example: Anomaly above threshold, reject for manual investigation."
                />
              </label>

              <div className="approvalActions">
                <button
                  className="approveButton"
                  onClick={() => submitHumanDecision("approve")}
                  disabled={isSubmittingDecision}
                >
                  Approve
                </button>

                <button
                  className="rejectButton"
                  onClick={() => submitHumanDecision("reject")}
                  disabled={isSubmittingDecision}
                >
                  Reject
                </button>
              </div>
            </section>
          )}

          <section className="activityPanel">
            <div className="panelHeader">
              <h2>Activity Feed</h2>
              <span>{events.length} events</span>
            </div>

            {latestEvent && (
              <div className="latestEventSummary">
                <span>Latest event</span>
                <strong>{latestEvent.agent}</strong>
                <p>{latestEvent.message}</p>
              </div>
            )}

            {events.length === 0 ? (
              <p className="emptyState">
                No activity yet. Create and start a session.
              </p>
            ) : (
              <div className="eventList">
                {events.map((event, index) => (
                  <article
                    className={`eventCard ${getEventTone(event.type)}`}
                    key={`${event.timestamp}-${index}`}
                  >
                    <div className="eventMeta">
                      <span>{event.agent}</span>
                      <time>{new Date(event.timestamp).toLocaleTimeString()}</time>
                    </div>

                    <h3>{event.type}</h3>
                    <p>{event.message}</p>
                  </article>
                ))}
              </div>
            )}
          </section>
        </section>
      </section>
    </main>
  );
}

export default App;
