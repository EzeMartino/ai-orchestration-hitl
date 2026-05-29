import { useState } from "react";
import type { DragEvent } from "react";
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

const maxStructuredMetricsFileSizeBytes = 20_971_520;
const allowedStructuredMetricsFileExtensions = [".json", ".csv", ".pdf"];
const sampleJsonTemplateUrl = "/templates/structured-financial-metrics-sample.json";
const sampleCsvTemplateUrl = "/templates/structured-financial-metrics-sample.csv";
const uploadRequiresSessionMessage =
  "No puede cargar los archivos si no ha creado o cargado una sesi\u00f3n.";
const saveRequiresSessionMessage =
  "No puede guardar las m\u00e9tricas si no ha creado o cargado una sesi\u00f3n.";

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
  const [isDraggingFile, setIsDraggingFile] = useState(false);

  async function handleSaveJson() {
    setInputError(null);
    try {
      const parsed = JSON.parse(jsonText) as StructuredFinancialMetricsInput;
      await onSaveJson(parsed);
    } catch (error) {
      console.error(error);
      setInputError("Formato JSON no válido.");
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
        throw new Error("No se pudo cargar la plantilla JSON de muestra.");
      }
      setJsonText(await response.text());
      setMode("json");
    } catch (error) {
      console.error(error);
      setInputError("No se pudo cargar la plantilla JSON de muestra.");
    }
  }

  async function loadSampleCsv() {
    setInputError(null);
    try {
      const response = await fetch(sampleCsvTemplateUrl);
      if (!response.ok) {
        throw new Error("No se pudo cargar la plantilla CSV de muestra.");
      }
      setCsvText(await response.text());
      setCsvDocumentId("sample-csv-metrics");
      setCsvCompany("Sample Energy Co");
      setCsvCurrency("USD");
      setCsvUnit("USD_thousand");
      setMode("csv");
    } catch (error) {
      console.error(error);
      setInputError("No se pudo cargar la plantilla CSV de muestra.");
    }
  }

  function handleFileSelected(file: File | null) {
    setInputError(null);
    setSelectedFile(file);
  }

  function handleFileDrop(event: DragEvent<HTMLDivElement>) {
    event.preventDefault();
    setIsDraggingFile(false);
    handleFileSelected(event.dataTransfer.files?.[0] ?? null);
  }

  async function handleUploadFile() {
    setInputError(null);
    if (!selectedFile) {
      setInputError("Seleccione un archivo de métricas JSON, CSV o PDF.");
      return;
    }

    const extension = getFileExtension(selectedFile.name);
    if (!allowedStructuredMetricsFileExtensions.includes(extension)) {
      setInputError("Solo se admiten archivos con extensión .json, .csv y .pdf.");
      return;
    }

    if (selectedFile.size > maxStructuredMetricsFileSizeBytes) {
      setInputError("Solo se admiten archivos de hasta 20 MB.");
      return;
    }

    if (extension === ".csv" && fileDocumentId.trim().length === 0) {
      setInputError("El DocumentId es obligatorio para la carga de CSV.");
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
          <p className="structuredMetricsEyebrow">Entrada de métricas estructuradas</p>
          <h2>Adjuntar métricas financieras</h2>
          <p>
            La validación estructurada comprueba los campos requeridos, la normalización y
            problemas básicos de calidad. No verifica la exactitud contable.
          </p>
        </div>

        <div className="structuredMetricsState">
          {!sessionId ? (
            <span>Cree o cargue una sesión antes de adjuntar métricas financieras estructuradas.</span>
          ) : isLoading ? (
            <span>Cargando métricas estructuradas...</span>
          ) : metricsContext ? (
            <>
              <strong>Métricas estructuradas adjuntas</strong>
              <span>Documento: {metricsContext.documentId}</span>
              <span>Compañía: {metricsContext.company ?? "-"}</span>
              <span>
                Origen:{" "}
                {formatIngestionMethod(
                  metricsContext.provenance?.ingestionMethod
                )}
              </span>
              {metricsContext.provenance?.originalFileName && (
                <span>Archivo: {metricsContext.provenance.originalFileName}</span>
              )}
              <span>
                Métricas:{" "}
                {metricsContext.provenance?.metricCount ??
                  metricsContext.metrics.length}
              </span>
              <span>
                Advertencias:{" "}
                {metricsContext.provenance?.warningCount ??
                  metricsContext.validationWarnings.length}
              </span>
              <span>Cargado: {new Date(metricsContext.uploadedAt).toLocaleString()}</span>
            </>
          ) : (
            <span>No hay métricas financieras estructuradas adjuntas a esta sesión.</span>
          )}
        </div>
      </div>

      <div className="metricsTemplateBox">
        <div>
          <strong>Plantillas</strong>
          <p>
            Utilice estas muestras para coincidir con el formato de métricas
            financieras estructuradas esperado.
          </p>
        </div>

        <div className="templateActions">
          <a href={sampleJsonTemplateUrl} download>
            Descargar JSON de muestra
          </a>
          <button onClick={loadSampleJson} type="button">
            Cargar JSON de muestra
          </button>
          <a href={sampleCsvTemplateUrl} download>
            Descargar CSV de muestra
          </a>
          <button onClick={loadSampleCsv} type="button">
            Cargar CSV de muestra
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
          Cargar Archivo
        </button>
      </div>

      {mode === "json" ? (
        <div className="metricsEditor">
          <label>
            Métricas en JSON
            <textarea
              value={jsonText}
              onChange={(event) => setJsonText(event.target.value)}
              spellCheck={false}
            />
          </label>

          <span
            className={!sessionId ? "sessionActionTooltip" : undefined}
            data-tooltip={!sessionId ? saveRequiresSessionMessage : undefined}
            tabIndex={!sessionId ? 0 : undefined}
            title={!sessionId ? saveRequiresSessionMessage : undefined}
          >
            <button
              onClick={handleSaveJson}
              disabled={!sessionId || isSaving}
              type="button"
            >
              {isSaving ? "Guardando..." : "Guardar Métricas JSON"}
            </button>
          </span>
        </div>
      ) : mode === "csv" ? (
        <div className="metricsEditor">
          <div className="csvMetaGrid">
            <label>
              Identificador de Documento (DocumentId)
              <input
                value={csvDocumentId}
                onChange={(event) => setCsvDocumentId(event.target.value)}
              />
            </label>
            <label>
              Compañía
              <input
                value={csvCompany}
                onChange={(event) => setCsvCompany(event.target.value)}
              />
            </label>
            <label>
              Moneda
              <input
                value={csvCurrency}
                onChange={(event) => setCsvCurrency(event.target.value)}
              />
            </label>
            <label>
              Unidad
              <input
                value={csvUnit}
                onChange={(event) => setCsvUnit(event.target.value)}
              />
            </label>
          </div>

          <label>
            Métricas en CSV
            <textarea
              value={csvText}
              onChange={(event) => setCsvText(event.target.value)}
              spellCheck={false}
            />
          </label>

          <span
            className={!sessionId ? "sessionActionTooltip" : undefined}
            data-tooltip={!sessionId ? saveRequiresSessionMessage : undefined}
            tabIndex={!sessionId ? 0 : undefined}
            title={!sessionId ? saveRequiresSessionMessage : undefined}
          >
            <button
              onClick={handleSaveCsv}
              disabled={!sessionId || isSaving}
              type="button"
            >
              {isSaving ? "Guardando..." : "Guardar Métricas CSV"}
            </button>
          </span>
        </div>
      ) : (
        <div className="metricsEditor">
          <div
            className={`fileUploadBox ${isDraggingFile ? "fileUploadBox-active" : ""}`}
            onDragEnter={(event) => {
              event.preventDefault();
              setIsDraggingFile(true);
            }}
            onDragOver={(event) => event.preventDefault()}
            onDragLeave={() => setIsDraggingFile(false)}
            onDrop={handleFileDrop}
          >
            <label>
              Archivo JSON, CSV o PDF
              <input
                type="file"
                accept=".json,.csv,.pdf"
                disabled={isSaving}
                onChange={(event) =>
                  handleFileSelected(event.currentTarget.files?.[0] ?? null)
                }
              />
            </label>

            <p>Solo se admiten archivos .json, .csv y .pdf de hasta 20 MB.</p>

            {selectedFile && (
              <div className="selectedFileSummary">
                <strong>Archivo seleccionado</strong>
                <span>{selectedFile.name}</span>
                <span>{formatFileSize(selectedFile.size)}</span>
              </div>
            )}
          </div>

          <div className="csvMetaGrid">
            <label>
              Identificador de Documento (DocumentId)
              <input
                value={fileDocumentId}
                disabled={!sessionId || isSaving}
                onChange={(event) => setFileDocumentId(event.target.value)}
              />
            </label>
            <label>
              Compañía
              <input
                value={fileCompany}
                disabled={!sessionId || isSaving}
                onChange={(event) => setFileCompany(event.target.value)}
              />
            </label>
            <label>
              Moneda
              <input
                value={fileCurrency}
                disabled={!sessionId || isSaving}
                onChange={(event) => setFileCurrency(event.target.value)}
              />
            </label>
            <label>
              Unidad
              <input
                value={fileUnit}
                disabled={!sessionId || isSaving}
                onChange={(event) => setFileUnit(event.target.value)}
              />
            </label>
          </div>

          <span
            className={!sessionId ? "sessionActionTooltip" : undefined}
            data-tooltip={!sessionId ? uploadRequiresSessionMessage : undefined}
            tabIndex={!sessionId ? 0 : undefined}
            title={!sessionId ? uploadRequiresSessionMessage : undefined}
          >
            <button
              onClick={handleUploadFile}
              disabled={!sessionId || isSaving || !selectedFile}
              type="button"
            >
              {isSaving ? "Subiendo..." : "Subir Archivo"}
            </button>
          </span>
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
                ? "Métricas del archivo guardadas con éxito"
                : "Guardadas con éxito"
              : "Las métricas no fueron persistidas."}
          </strong>

          <IssueList title="Errores" issues={saveResult.errors} />
          <IssueList title="Advertencias" issues={saveResult.warnings} />
        </div>
      )}
    </section>
  );
}

export default StructuredFinancialMetricsPanel;
