import { useState } from "react";
import type {
  StructuredFinancialMetricsContext,
  SaveFinancialMetricsResponse,
  StructuredFinancialMetricsInput,
  StructuredFinancialMetricsCsvInput,
  StructuredFinancialMetricsFileMetadata,
  FinancialMetricsValidationIssue,
} from "../types/domain.types";

interface StructuredFinancialMetricsPanelProps {
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
}

const maxStructuredMetricsFileSizeBytes = 10_485_760;
const allowedStructuredMetricsFileExtensions = [".json", ".csv", ".pdf"];
const sampleJsonTemplateUrl = "/templates/structured-financial-metrics-sample.json";
const sampleCsvTemplateUrl = "/templates/structured-financial-metrics-sample.csv";

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
  return dotIndex >= 0 ? fileName.slice(dotIndex).toLowerCase() : "";
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
    case "pdf_file":
      return "PDF file";
    default:
      return "Unknown";
  }
}

export function IssueList({
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

export function StructuredFinancialMetricsPanel({
  sessionId,
  metricsContext,
  isLoading,
  isSaving,
  saveResult,
  saveError,
  onSaveJson,
  onSaveCsv,
  onUploadFile,
}: StructuredFinancialMetricsPanelProps) {
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
      setInputError("Select a JSON, CSV, or PDF metrics file.");
      return;
    }

    const extension = getFileExtension(selectedFile.name);
    if (!allowedStructuredMetricsFileExtensions.includes(extension)) {
      setInputError("Only .json, .csv, and .pdf files are supported.");
      return;
    }

    if (selectedFile.size > maxStructuredMetricsFileSizeBytes) {
      setInputError("Only files up to 10 MB are supported.");
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
              JSON, CSV, or PDF file
              <input
                type="file"
                accept=".json,.csv,.pdf"
                disabled={!sessionId || isSaving}
                onChange={(event) =>
                  handleFileSelected(event.currentTarget.files?.[0] ?? null)
                }
              />
            </label>

            <p>Only .json, .csv, and .pdf files up to 10 MB are supported.</p>

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

export default StructuredFinancialMetricsPanel;
