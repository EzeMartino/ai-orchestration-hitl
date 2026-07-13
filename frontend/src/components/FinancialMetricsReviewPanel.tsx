import { Fragment, useEffect, useMemo, useState } from "react";
import type {
  FinancialDocumentMetadataCandidate,
  FinancialMetricCandidateConflict,
  FinancialMetricCandidate,
  FinancialMetricCandidateAddition,
  FinancialMetricCandidateReviewDecision,
  FinancialMetricCandidateReviewState,
  FinancialMetricsExtractionDraft,
  StructuredFinancialMetricInput,
  StructuredFinancialMetricsInput,
  UpdateFinancialMetricsExtractionDraftRequest,
  ConfirmFinancialMetricsExtractionDraftRequest,
} from "../types/domain.types";
import {
  applyCandidateEdit,
  applyMetadataCandidateEdit,
  applyMetadataCandidateRejection,
  applyProposedMetadataEdit,
  applyReportSummaryToDraft,
  getDraftReportSummaryForm,
  mapCandidateToStructuredMetric,
  parseFiniteMetricValue,
  validateReviewDraft,
} from "../utils/financialMetricsReview";
import {
  mapFinancialReportSummaryErrorsByField,
  validateFinancialReportSummary,
} from "../utils/financialReportSummary";
import type { FinancialReportSummaryFormState } from "../utils/financialReportSummary";

interface FinancialMetricsReviewPanelProps {
  draft?: FinancialMetricsExtractionDraft | null;
  isLoading: boolean;
  isSaving: boolean;
  error?: string | null;
  onUpdate: (
    draftId: string,
    request: UpdateFinancialMetricsExtractionDraftRequest
  ) => Promise<FinancialMetricsExtractionDraft | null>;
  onConfirm: (
    draftId: string,
    request: ConfirmFinancialMetricsExtractionDraftRequest,
  ) => Promise<void>;
  onDiscard: (draftId: string) => Promise<void>;
  onRetry: () => void;
}

type ReviewStateCounts = {
  conflict: number;
  missing: number;
  inferred: number;
};

type MetadataFieldName = "company" | "currency" | "unit";

const metadataFieldNames: MetadataFieldName[] = ["company", "currency", "unit"];

type DraftMetricAddition = FinancialMetricCandidateAddition & {
  localId: string;
};

type MetricAdditionForm = {
  name: string;
  period: string;
  value: string;
  currency: string;
  unit: string;
};

const resolvedReviewStates = new Set([
  "explicit",
  "accepted",
  "rejected",
  "human_corrected",
]);

const selectedReviewStates = new Set([
  "explicit",
  "accepted",
  "human_corrected",
]);

function normalizeMetadataFieldName(fieldName: string) {
  return fieldName.trim().toLowerCase();
}

function formatMetadataFieldName(fieldName: string) {
  switch (normalizeMetadataFieldName(fieldName)) {
    case "company":
      return "CompaÃ±Ã­a";
    case "currency":
      return "Moneda";
    case "unit":
      return "Unidad";
    default:
      return fieldName;
  }
}

function getProposedMetadataValue(
  input: StructuredFinancialMetricsInput,
  fieldName: MetadataFieldName
) {
  switch (fieldName) {
    case "company":
      return input.company ?? "";
    case "currency":
      return input.currency ?? "";
    case "unit":
      return input.unit ?? "";
    default:
      return "";
  }
}

function countReviewStates(
  draft?: FinancialMetricsExtractionDraft | null
): ReviewStateCounts {
  const candidates = [
    ...(draft?.payload.candidates ?? []),
    ...(draft?.payload.metadataCandidates ?? []),
  ];
  const counts = candidates.reduce(
    (counts, candidate) => ({
      conflict: counts.conflict + (candidate.reviewState === "conflict" ? 1 : 0),
      missing: counts.missing + (candidate.reviewState === "missing" ? 1 : 0),
      inferred: counts.inferred + (candidate.reviewState === "inferred" ? 1 : 0),
    }),
    { conflict: 0, missing: 0, inferred: 0 }
  );

  return {
    ...counts,
    missing: counts.missing + (draft?.payload.missingFields.length ?? 0),
  };
}

function formatCandidateValue(value?: number | null) {
  return value === null || value === undefined ? "" : String(value);
}

function formatConfidence(confidence?: number | null) {
  if (confidence === null || confidence === undefined) {
    return "-";
  }

  return `${Math.round(confidence * 100)}%`;
}

function formatReviewState(state: FinancialMetricCandidateReviewState) {
  switch (state) {
    case "explicit":
      return "Explícita";
    case "inferred":
      return "Inferida";
    case "conflict":
      return "Conflicto";
    case "missing":
      return "Faltante";
    case "accepted":
      return "Aceptada";
    case "rejected":
      return "Rechazada";
    case "human_corrected":
      return "Corrección humana";
    default:
      return state;
  }
}

function toReviewDecision(
  state: FinancialMetricCandidateReviewState
): FinancialMetricCandidateReviewDecision | null {
  switch (state) {
    case "explicit":
    case "accepted":
      return "accepted";
    case "rejected":
      return "rejected";
    case "human_corrected":
      return "human_corrected";
    default:
      return null;
  }
}

function additionToMetric(addition: DraftMetricAddition): StructuredFinancialMetricInput {
  return {
    name: addition.name,
    period: addition.period,
    value: addition.value ?? null,
    currency: addition.currency ?? null,
    unit: addition.unit ?? null,
    source: "human_corrected",
    sourcePage: null,
    confidence: 1,
  };
}

function selectedMetricCandidates(candidates: FinancialMetricCandidate[]) {
  return candidates.filter((candidate) =>
    candidate.reviewState === "explicit" ||
    candidate.reviewState === "accepted" ||
    candidate.reviewState === "human_corrected"
  );
}

function createCandidateUpdate(candidate: FinancialMetricCandidate) {
  const decision = toReviewDecision(candidate.reviewState);

  if (!decision) {
    return null;
  }

  return {
    candidateId: candidate.id,
    decision,
    value: decision === "human_corrected" ? candidate.value ?? null : null,
    currency: decision === "human_corrected" ? candidate.currency ?? null : null,
    unit: decision === "human_corrected" ? candidate.unit ?? null : null,
    metadataValue: null,
  };
}

function createMetadataUpdate(candidate: FinancialDocumentMetadataCandidate) {
  const decision = toReviewDecision(candidate.reviewState);

  if (!decision) {
    return null;
  }

  return {
    candidateId: candidate.id,
    decision,
    value: null,
    currency: null,
    unit: null,
    metadataValue: decision === "human_corrected" ? candidate.value : null,
  };
}

function createMetadataAdditions(draft: FinancialMetricsExtractionDraft) {
  const existingFields = new Set(
    (draft.payload.metadataCandidates ?? []).map((candidate) =>
      normalizeMetadataFieldName(candidate.fieldName)
    )
  );

  return metadataFieldNames
    .filter((fieldName) => !existingFields.has(fieldName))
    .map((fieldName) => {
      const value = getProposedMetadataValue(draft.payload.proposedInput, fieldName);

      return value.trim().length > 0
        ? {
            fieldName,
            value: value.trim(),
          }
        : null;
    })
    .filter(
      (addition): addition is { fieldName: MetadataFieldName; value: string } =>
        Boolean(addition)
    );
}

function createUpdateRequest(
  draft: FinancialMetricsExtractionDraft,
  metricAdditions: DraftMetricAddition[]
): UpdateFinancialMetricsExtractionDraftRequest {
  const selectedCandidates = selectedMetricCandidates(draft.payload.candidates);
  const normalizedMetricAdditions = metricAdditions.map((addition) => ({
    name: addition.name.trim(),
    period: addition.period.trim(),
    value: addition.value ?? null,
    currency: addition.currency?.trim() || null,
    unit: addition.unit?.trim() || null,
  }));
  const metricUpdates = draft.payload.candidates
    .map(createCandidateUpdate)
    .filter((update): update is NonNullable<typeof update> => Boolean(update));
  const metadataUpdates = (draft.payload.metadataCandidates ?? [])
    .map(createMetadataUpdate)
    .filter((update): update is NonNullable<typeof update> => Boolean(update));

  return {
    candidates: [...metricUpdates, ...metadataUpdates],
    proposedInput: {
      ...draft.payload.proposedInput,
      metrics: [
        ...selectedCandidates.map(mapCandidateToStructuredMetric),
        ...metricAdditions.map(additionToMetric),
      ],
    },
    metricAdditions: normalizedMetricAdditions,
    metadataAdditions: createMetadataAdditions(draft),
  };
}

function isConflictResolvedLocally(
  conflict: FinancialMetricCandidateConflict,
  draft: FinancialMetricsExtractionDraft
) {
  const candidatesById = new Map(
    draft.payload.candidates.map((candidate) => [candidate.id, candidate])
  );
  const metadataById = new Map(
    (draft.payload.metadataCandidates ?? []).map((candidate) => [candidate.id, candidate])
  );
  const involved = [
    ...conflict.metricCandidates
      .map((candidate) => candidatesById.get(candidate.id))
      .filter((candidate): candidate is FinancialMetricCandidate => Boolean(candidate)),
    ...conflict.metadataCandidates
      .map((candidate) => metadataById.get(candidate.id))
      .filter((candidate): candidate is FinancialDocumentMetadataCandidate => Boolean(candidate)),
  ];

  if (involved.length === 0) {
    return conflict.isResolved;
  }

  return (
    involved.every((candidate) => resolvedReviewStates.has(candidate.reviewState)) &&
    involved.filter((candidate) => selectedReviewStates.has(candidate.reviewState)).length === 1
  );
}

function isDraftDirty(
  initialDraft: FinancialMetricsExtractionDraft | null,
  currentDraft: FinancialMetricsExtractionDraft | null
) {
  if (!initialDraft || !currentDraft) {
    return false;
  }

  return JSON.stringify(initialDraft.payload) !== JSON.stringify(currentDraft.payload);
}

export function FinancialMetricsReviewPanel({
  draft,
  isLoading,
  isSaving,
  error,
  onUpdate,
  onConfirm,
  onDiscard,
  onRetry,
}: FinancialMetricsReviewPanelProps) {
  const [workingDraft, setWorkingDraft] = useState<FinancialMetricsExtractionDraft | null>(
    draft ?? null
  );
  const [metricAdditions, setMetricAdditions] = useState<DraftMetricAddition[]>([]);
  const [metricAdditionForm, setMetricAdditionForm] = useState<MetricAdditionForm>({
    name: "",
    period: "",
    value: "",
    currency: "",
    unit: "",
  });
  const [reportSummary, setReportSummary] = useState<FinancialReportSummaryFormState>(
    getDraftReportSummaryForm(draft),
  );

  useEffect(() => {
    setWorkingDraft(draft ?? null);
    setReportSummary(getDraftReportSummaryForm(draft));
    setMetricAdditions([]);
    setMetricAdditionForm({
      name: "",
      period: "",
      value: "",
      currency: draft?.payload.proposedInput.currency ?? "",
      unit: draft?.payload.proposedInput.unit ?? "",
    });
  }, [draft]);

  const counts = useMemo(
    () => countReviewStates(workingDraft),
    [workingDraft]
  );
  const validation = workingDraft
    ? validateReviewDraft(workingDraft, {
        metricAdditionCount: metricAdditions.length,
        reportSummary: validateFinancialReportSummary(reportSummary),
      })
    : { canConfirm: false, blockingCandidateIds: [], missingFields: [], reportSummaryIssues: [] };
  const hasUnresolvedConflicts = (workingDraft?.payload.conflicts ?? [])
    .some((conflict) => workingDraft && !isConflictResolvedLocally(conflict, workingDraft));
  const canConfirm = validation.canConfirm && !hasUnresolvedConflicts;
  const summaryErrorMessages = mapFinancialReportSummaryErrorsByField(
    validation.reportSummaryIssues,
  );
  const dirty =
    isDraftDirty(draft ?? null, workingDraft) ||
    metricAdditions.length > 0 ||
    JSON.stringify(getDraftReportSummaryForm(draft)) !== JSON.stringify(reportSummary);

  if (isLoading && !workingDraft) {
    return (
      <section className="financialMetricsReviewPanel" aria-live="polite">
        <p className="financialMetricsReviewEyebrow">Revisión de extracción PDF</p>
        <strong>Cargando borrador de revisión...</strong>
      </section>
    );
  }

  if (!workingDraft) {
    if (error) {
      return (
        <section className="financialMetricsReviewPanel" aria-live="polite">
          <p className="financialMetricsReviewEyebrow">RevisiÃ³n de extracciÃ³n PDF</p>
          <div className="financialMetricsReviewNotice financialMetricsReviewNotice-danger" role="alert">
            {error}
          </div>
          <button type="button" onClick={onRetry}>
            Reintentar
          </button>
        </section>
      );
    }

    return null;
  }

  function updateCandidate(
    candidateId: string,
    updater: (candidate: FinancialMetricCandidate) => FinancialMetricCandidate
  ) {
    setWorkingDraft((current) => {
      if (!current) {
        return current;
      }

      return {
        ...current,
        payload: {
          ...current.payload,
          candidates: current.payload.candidates.map((candidate) =>
            candidate.id === candidateId ? updater(candidate) : candidate
          ),
        },
      };
    });
  }

  function updateMetadataCandidate(
    candidateId: string,
    updater: (
      candidate: FinancialDocumentMetadataCandidate
    ) => FinancialDocumentMetadataCandidate,
    syncProposedInput = false
  ) {
    setWorkingDraft((current) => {
      if (!current) {
        return current;
      }

      const original = (current.payload.metadataCandidates ?? []).find(
        (candidate) => candidate.id === candidateId
      );

      if (!original) {
        return current;
      }

      const updated = updater(original);
      const nextDraft = {
        ...current,
        payload: {
          ...current.payload,
          metadataCandidates: (current.payload.metadataCandidates ?? []).map(
            (candidate) => candidate.id === candidateId ? updated : candidate
          ),
        },
      };

      return syncProposedInput
        ? applyProposedMetadataEdit(nextDraft, updated.fieldName, updated.value)
        : nextDraft;
    });
  }

  function handleProposedMetadataChange(fieldName: MetadataFieldName, value: string) {
    setWorkingDraft((current) => {
      if (!current) {
        return current;
      }

      const matchingCandidates = (current.payload.metadataCandidates ?? []).filter(
        (candidate) =>
          normalizeMetadataFieldName(candidate.fieldName) === fieldName
      );
      const unresolvedCandidate = matchingCandidates.find(
        (candidate) => !resolvedReviewStates.has(candidate.reviewState)
      );
      const editableCandidate = unresolvedCandidate ?? matchingCandidates[0];

      return editableCandidate
        ? applyMetadataCandidateEdit(current, editableCandidate.id, value)
        : applyProposedMetadataEdit(current, fieldName, value);
    });

    if (fieldName === "currency" || fieldName === "unit") {
      setMetricAdditionForm((current) => ({
        ...current,
        [fieldName]: current[fieldName].trim().length === 0 ? value : current[fieldName],
      }));
    }
  }

  function handleMetricAdditionFormChange(
    fieldName: keyof MetricAdditionForm,
    value: string
  ) {
    setMetricAdditionForm((current) => ({
      ...current,
      [fieldName]: value,
    }));
  }

  function handleAddMetric() {
    const value = Number(metricAdditionForm.value);

    if (
      metricAdditionForm.name.trim().length === 0 ||
      metricAdditionForm.period.trim().length === 0 ||
      metricAdditionForm.value.trim().length === 0 ||
      !Number.isFinite(value)
    ) {
      return;
    }

    setMetricAdditions((current) => [
      ...current,
      {
        localId:
          globalThis.crypto?.randomUUID?.() ??
          `metric-addition-${Date.now()}-${current.length + 1}`,
        name: metricAdditionForm.name.trim(),
        period: metricAdditionForm.period.trim(),
        value,
        currency: metricAdditionForm.currency.trim() || null,
        unit: metricAdditionForm.unit.trim() || null,
      },
    ]);
    setMetricAdditionForm((current) => ({
      name: "",
      period: "",
      value: "",
      currency: current.currency,
      unit: current.unit,
    }));
  }

  function handleRemoveMetricAddition(localId: string) {
    setMetricAdditions((current) =>
      current.filter((addition) => addition.localId !== localId)
    );
  }

  function handleValueEdit(candidateId: string, valueText: string) {
    if (!workingDraft) {
      return;
    }

    const value = parseFiniteMetricValue(valueText);

    if (value === undefined) {
      return;
    }

    setWorkingDraft(applyCandidateEdit(workingDraft, candidateId, { value }));
  }

  async function handleSave() {
    if (!workingDraft) {
      return null;
    }

    const summaryValidation = validateFinancialReportSummary(reportSummary);
    if (!summaryValidation.isValid || !summaryValidation.value) {
      return null;
    }
    const draftWithSummary = applyReportSummaryToDraft(
      workingDraft,
      summaryValidation.value,
    );
    const updated = await onUpdate(
      workingDraft.id,
      createUpdateRequest(draftWithSummary, metricAdditions)
    );
    if (updated) {
      setWorkingDraft(updated);
      setMetricAdditions([]);
    }

    return updated;
  }

  async function handleConfirm() {
    if (!workingDraft || !canConfirm) {
      return;
    }

    const saved = await handleSave();
    if (!saved) {
      return;
    }

    const summaryValidation = validateFinancialReportSummary(reportSummary);
    if (!summaryValidation.isValid || !summaryValidation.value) {
      return;
    }

    await onConfirm(saved.id, { reportSummary: summaryValidation.value });
  }

  const blockingMessages = [
    validation.blockingCandidateIds.length > 0
      ? `${validation.blockingCandidateIds.length} candidato(s) requieren decisión.`
      : null,
    validation.missingFields.length > 0
      ? `Campos faltantes: ${validation.missingFields.join(", ")}.`
      : null,
    validation.reportSummaryIssues.length > 0
      ? `Resumen del informe: ${validation.reportSummaryIssues.map((issue) => issue.code).join(", ")}.`
      : null,
  ].filter(Boolean);
  const blockingReason = blockingMessages.length > 0
    ? blockingMessages.join(" ")
    : hasUnresolvedConflicts
        ? "Hay conflictos pendientes de resolución."
        : undefined;

  return (
    <section className="financialMetricsReviewPanel" aria-labelledby="financialMetricsReviewTitle">
      <div className="financialMetricsReviewHeader">
        <div>
          <p className="financialMetricsReviewEyebrow">Revisión de extracción PDF</p>
          <h2 id="financialMetricsReviewTitle">Validar métricas detectadas</h2>
          <p>
            Revise candidatos semánticos antes de guardar las métricas activas de la sesión.
          </p>
        </div>

        <dl className="financialMetricsReviewSummary" aria-label="Resumen de revisión">
          <div>
            <dt>Conflictos</dt>
            <dd>{counts.conflict}</dd>
          </div>
          <div>
            <dt>Faltantes</dt>
            <dd>{counts.missing}</dd>
          </div>
          <div>
            <dt>Inferidas</dt>
            <dd>{counts.inferred}</dd>
          </div>
        </dl>
      </div>

      {(error || blockingReason) && (
        <div className={`financialMetricsReviewNotice ${error ? "financialMetricsReviewNotice-danger" : ""}`}>
          {error ?? blockingReason}
        </div>
      )}

      <div className="financialMetricsReviewMeta">
        <span>Archivo: {workingDraft.originalFileName}</span>
        <span>Candidatos: {workingDraft.payload.candidates.length}</span>
        <span>Metadatos: {workingDraft.payload.metadataCandidates?.length ?? 0}</span>
        <span>Estado: {workingDraft.status}</span>
      </div>

      <fieldset className="financialReportSummaryForm financialReportSummaryReview">
        <legend>Resumen del informe confirmado</legend>
        {workingDraft.payload.schemaVersion === 1 && (
          <p role="note">
            Este borrador v1 no contiene un resumen confiable. Complete los cuatro campos antes de confirmar.
          </p>
        )}
        <div className="financialReportSummaryGrid">
          <label>
            Nombre del informe
            <input
              value={reportSummary.reportName}
              onChange={(event) => setReportSummary((current) => ({ ...current, reportName: event.target.value }))}
              aria-invalid={Boolean(summaryErrorMessages.reportName)}
              aria-describedby={summaryErrorMessages.reportName ? "review-financial-report-name-error" : undefined}
              disabled={isSaving || isLoading}
            />
            {summaryErrorMessages.reportName && <span id="review-financial-report-name-error" className="fieldError">{summaryErrorMessages.reportName}</span>}
          </label>
          <label>
            Importe total
            <input
              type="number"
              min="0"
              step="any"
              value={reportSummary.totalAmount}
              onChange={(event) => setReportSummary((current) => ({ ...current, totalAmount: event.target.value }))}
              aria-invalid={Boolean(summaryErrorMessages.totalAmount)}
              aria-describedby={summaryErrorMessages.totalAmount ? "review-financial-report-total-error" : undefined}
              disabled={isSaving || isLoading}
            />
            {summaryErrorMessages.totalAmount && <span id="review-financial-report-total-error" className="fieldError">{summaryErrorMessages.totalAmount}</span>}
          </label>
          <label>
            Cantidad de transacciones
            <input
              type="number"
              min="0"
              step="1"
              value={reportSummary.transactionCount}
              onChange={(event) => setReportSummary((current) => ({ ...current, transactionCount: event.target.value }))}
              aria-invalid={Boolean(summaryErrorMessages.transactionCount)}
              aria-describedby={summaryErrorMessages.transactionCount ? "review-financial-report-count-error" : undefined}
              disabled={isSaving || isLoading}
            />
            {summaryErrorMessages.transactionCount && <span id="review-financial-report-count-error" className="fieldError">{summaryErrorMessages.transactionCount}</span>}
          </label>
          <label>
            Fecha de envío
            <input
              type="datetime-local"
              value={reportSummary.submittedAt}
              onChange={(event) => setReportSummary((current) => ({ ...current, submittedAt: event.target.value }))}
              aria-invalid={Boolean(summaryErrorMessages.submittedAt)}
              aria-describedby={summaryErrorMessages.submittedAt ? "review-financial-report-date-error" : undefined}
              disabled={isSaving || isLoading}
            />
            {summaryErrorMessages.submittedAt && <span id="review-financial-report-date-error" className="fieldError">{summaryErrorMessages.submittedAt}</span>}
          </label>
        </div>
      </fieldset>

      <div className="financialMetricsReviewDocumentFields">
        <h3>Metadatos del documento</h3>
        <div className="financialMetricsReviewDocumentGrid">
          <label>
            DocumentId
            <input value={workingDraft.payload.proposedInput.documentId} readOnly />
          </label>
          {metadataFieldNames.map((fieldName) => (
            <label key={fieldName}>
              {formatMetadataFieldName(fieldName)}
              <input
                value={getProposedMetadataValue(
                  workingDraft.payload.proposedInput,
                  fieldName
                )}
                onChange={(event) =>
                  handleProposedMetadataChange(fieldName, event.target.value)
                }
                disabled={isSaving}
              />
            </label>
          ))}
        </div>
      </div>

      <div className="financialMetricsReviewMetricAdditions">
        <h3>Agregar métrica manual</h3>
        <div className="metricAdditionGrid">
          <label>
            Métrica
            <input
              value={metricAdditionForm.name}
              onChange={(event) =>
                handleMetricAdditionFormChange("name", event.target.value)
              }
              disabled={isSaving}
              placeholder="Revenue"
            />
          </label>
          <label>
            Periodo
            <input
              value={metricAdditionForm.period}
              onChange={(event) =>
                handleMetricAdditionFormChange("period", event.target.value)
              }
              disabled={isSaving}
              placeholder="2025A"
            />
          </label>
          <label>
            Valor
            <input
              type="number"
              value={metricAdditionForm.value}
              onChange={(event) =>
                handleMetricAdditionFormChange("value", event.target.value)
              }
              disabled={isSaving}
            />
          </label>
          <label>
            Moneda
            <input
              value={metricAdditionForm.currency}
              onChange={(event) =>
                handleMetricAdditionFormChange("currency", event.target.value)
              }
              disabled={isSaving}
            />
          </label>
          <label>
            Unidad
            <input
              value={metricAdditionForm.unit}
              onChange={(event) =>
                handleMetricAdditionFormChange("unit", event.target.value)
              }
              disabled={isSaving}
            />
          </label>
          <button
            type="button"
            onClick={handleAddMetric}
            disabled={
              isSaving ||
              metricAdditionForm.name.trim().length === 0 ||
              metricAdditionForm.period.trim().length === 0 ||
              metricAdditionForm.value.trim().length === 0 ||
              !Number.isFinite(Number(metricAdditionForm.value))
            }
          >
            Agregar métrica
          </button>
        </div>

        {metricAdditions.length > 0 && (
          <ul className="metricAdditionList" aria-label="Métricas manuales pendientes">
            {metricAdditions.map((addition) => (
              <li key={addition.localId}>
                <span>
                  <strong>{addition.name}</strong> {addition.period}: {addition.value}
                  {addition.currency ? ` ${addition.currency}` : ""}
                  {addition.unit ? ` (${addition.unit})` : ""}
                </span>
                <button
                  type="button"
                  className="metricAdditionRemove"
                  onClick={() => handleRemoveMetricAddition(addition.localId)}
                  aria-label={`Quitar ${addition.name} ${addition.period}`}
                  disabled={isSaving}
                >
                  Quitar
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>

      {(workingDraft.payload.metadataCandidates?.length ?? 0) > 0 && (
        <div className="financialMetricsReviewTableWrap metadataReviewTableWrap">
          <table className="financialMetricsReviewTable metadataReviewTable">
            <caption>Candidatos de metadatos del PDF</caption>
            <thead>
              <tr>
                <th scope="col">Campo</th>
                <th scope="col">Valor</th>
                <th scope="col">Fuente/Pág.</th>
                <th scope="col">Conf.</th>
                <th scope="col">Estado</th>
                <th scope="col">Acciones</th>
              </tr>
            </thead>
            <tbody>
              {(workingDraft.payload.metadataCandidates ?? []).map((candidate) => (
                <Fragment key={candidate.id}>
                  <tr>
                    <th scope="row">{formatMetadataFieldName(candidate.fieldName)}</th>
                    <td>
                      <label className="srOnly" htmlFor={`metadata-value-${candidate.id}`}>
                        Valor de {formatMetadataFieldName(candidate.fieldName)}
                      </label>
                      <input
                        id={`metadata-value-${candidate.id}`}
                        value={candidate.value ?? ""}
                        onChange={(event) =>
                          setWorkingDraft((current) =>
                            current
                              ? applyMetadataCandidateEdit(
                                  current,
                                  candidate.id,
                                  event.target.value
                                )
                              : current
                          )
                        }
                        disabled={isSaving}
                      />
                    </td>
                    <td>
                      <span>{candidate.sourceKind}</span>
                      <small>{candidate.sourcePage ? `Pág. ${candidate.sourcePage}` : "Sin pág."}</small>
                    </td>
                    <td>{formatConfidence(candidate.confidence)}</td>
                    <td>
                      <span className={`reviewStatePill reviewState-${candidate.reviewState}`}>
                        {formatReviewState(candidate.reviewState)}
                      </span>
                    </td>
                    <td>
                      <div className="candidateActions">
                        <button
                          type="button"
                          onClick={() =>
                            updateMetadataCandidate(
                              candidate.id,
                              (current) => ({
                                ...current,
                                reviewState: "accepted",
                              }),
                              true
                            )
                          }
                          aria-label={`Aceptar ${formatMetadataFieldName(candidate.fieldName)}`}
                          disabled={isSaving}
                        >
                          Aceptar
                        </button>
                        <button
                          type="button"
                          className="candidateRejectButton"
                          onClick={() =>
                            setWorkingDraft((current) =>
                              current
                                ? applyMetadataCandidateRejection(current, candidate.id)
                                : current
                            )
                          }
                          aria-label={`Rechazar ${formatMetadataFieldName(candidate.fieldName)}`}
                          disabled={isSaving}
                        >
                          Rechazar
                        </button>
                      </div>
                    </td>
                  </tr>
                  <tr className="candidateEvidenceRow">
                    <td colSpan={6}>
                      <details>
                        <summary>Evidencia y explicación</summary>
                        <p>{candidate.evidence || "Sin evidencia textual."}</p>
                        {candidate.inferenceExplanation && (
                          <small>{candidate.inferenceExplanation}</small>
                        )}
                      </details>
                    </td>
                  </tr>
                </Fragment>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <div className="financialMetricsReviewTableWrap">
        <table className="financialMetricsReviewTable">
          <caption>Candidatos de métricas financieras extraídas del PDF</caption>
          <thead>
            <tr>
              <th scope="col">Métrica</th>
              <th scope="col">Periodo</th>
              <th scope="col">Valor</th>
              <th scope="col">Moneda</th>
              <th scope="col">Unidad</th>
              <th scope="col">Fuente/Pág.</th>
              <th scope="col">Conf.</th>
              <th scope="col">Estado</th>
              <th scope="col">Acciones</th>
            </tr>
          </thead>
          <tbody>
            {workingDraft.payload.candidates.map((candidate) => (
              <Fragment key={candidate.id}>
                <tr>
                  <th scope="row">{candidate.name}</th>
                  <td>{candidate.period}</td>
                  <td>
                    <label className="srOnly" htmlFor={`candidate-value-${candidate.id}`}>
                      Valor de {candidate.name} {candidate.period}
                    </label>
                    <input
                      id={`candidate-value-${candidate.id}`}
                      type="number"
                      value={formatCandidateValue(candidate.value)}
                      onChange={(event) =>
                        handleValueEdit(candidate.id, event.target.value)
                      }
                      disabled={isSaving}
                    />
                  </td>
                  <td>
                    <label className="srOnly" htmlFor={`candidate-currency-${candidate.id}`}>
                      Moneda de {candidate.name} {candidate.period}
                    </label>
                    <input
                      id={`candidate-currency-${candidate.id}`}
                      value={candidate.currency ?? ""}
                      onChange={(event) =>
                        setWorkingDraft((current) =>
                          current
                            ? applyCandidateEdit(current, candidate.id, {
                                currency: event.target.value,
                              })
                            : current
                        )
                      }
                      disabled={isSaving}
                    />
                  </td>
                  <td>
                    <label className="srOnly" htmlFor={`candidate-unit-${candidate.id}`}>
                      Unidad de {candidate.name} {candidate.period}
                    </label>
                    <input
                      id={`candidate-unit-${candidate.id}`}
                      value={candidate.unit ?? ""}
                      onChange={(event) =>
                        setWorkingDraft((current) =>
                          current
                            ? applyCandidateEdit(current, candidate.id, {
                                unit: event.target.value,
                              })
                            : current
                        )
                      }
                      disabled={isSaving}
                    />
                  </td>
                  <td>
                    <span>{candidate.sourceKind}</span>
                    <small>{candidate.sourcePage ? `Pág. ${candidate.sourcePage}` : "Sin pág."}</small>
                  </td>
                  <td>{formatConfidence(candidate.confidence)}</td>
                  <td>
                    <span className={`reviewStatePill reviewState-${candidate.reviewState}`}>
                      {formatReviewState(candidate.reviewState)}
                    </span>
                  </td>
                  <td>
                    <div className="candidateActions">
                      <button
                        type="button"
                        onClick={() =>
                          updateCandidate(candidate.id, (current) => ({
                            ...current,
                            reviewState: "accepted",
                          }))
                        }
                        aria-label={`Aceptar ${candidate.name} ${candidate.period}`}
                        disabled={isSaving}
                      >
                        Aceptar
                      </button>
                      <button
                        type="button"
                        className="candidateRejectButton"
                        onClick={() =>
                          updateCandidate(candidate.id, (current) => ({
                            ...current,
                            reviewState: "rejected",
                          }))
                        }
                        aria-label={`Rechazar ${candidate.name} ${candidate.period}`}
                        disabled={isSaving}
                      >
                        Rechazar
                      </button>
                    </div>
                  </td>
                </tr>
                <tr className="candidateEvidenceRow">
                  <td colSpan={9}>
                    <details>
                      <summary>Evidencia y explicación</summary>
                      <p>{candidate.evidence || "Sin evidencia textual."}</p>
                      {candidate.inferenceExplanation && (
                        <small>{candidate.inferenceExplanation}</small>
                      )}
                    </details>
                  </td>
                </tr>
              </Fragment>
            ))}
          </tbody>
        </table>
      </div>

      <div className="financialMetricsReviewActions">
        <button type="button" onClick={handleSave} disabled={!dirty || isSaving}>
          {isSaving ? "Guardando..." : "Guardar cambios"}
        </button>
        <button
          type="button"
          onClick={handleConfirm}
          disabled={!canConfirm || isSaving}
          title={blockingReason}
        >
          {isSaving ? "Guardando..." : "Confirmar y guardar"}
        </button>
        <button
          type="button"
          className="financialMetricsReviewDiscard"
          onClick={() => onDiscard(workingDraft.id)}
          disabled={isSaving}
        >
          Descartar borrador
        </button>
      </div>
    </section>
  );
}

export default FinancialMetricsReviewPanel;
