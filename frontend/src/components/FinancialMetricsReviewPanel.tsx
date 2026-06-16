import { Fragment, useEffect, useMemo, useState } from "react";
import type {
  FinancialDocumentMetadataCandidate,
  FinancialMetricCandidateConflict,
  FinancialMetricCandidate,
  FinancialMetricCandidateReviewDecision,
  FinancialMetricCandidateReviewState,
  FinancialMetricsExtractionDraft,
  StructuredFinancialMetricInput,
  UpdateFinancialMetricsExtractionDraftRequest,
} from "../types/domain.types";
import {
  applyCandidateEdit,
  validateReviewDraft,
} from "../utils/financialMetricsReview";

interface FinancialMetricsReviewPanelProps {
  draft?: FinancialMetricsExtractionDraft | null;
  isLoading: boolean;
  isSaving: boolean;
  error?: string | null;
  onUpdate: (
    draftId: string,
    request: UpdateFinancialMetricsExtractionDraftRequest
  ) => Promise<FinancialMetricsExtractionDraft | null>;
  onConfirm: (draftId: string) => Promise<void>;
  onDiscard: (draftId: string) => Promise<void>;
}

type ReviewStateCounts = {
  conflict: number;
  missing: number;
  inferred: number;
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

function countReviewStates(candidates: FinancialMetricCandidate[]): ReviewStateCounts {
  return candidates.reduce(
    (counts, candidate) => ({
      conflict: counts.conflict + (candidate.reviewState === "conflict" ? 1 : 0),
      missing: counts.missing + (candidate.reviewState === "missing" ? 1 : 0),
      inferred: counts.inferred + (candidate.reviewState === "inferred" ? 1 : 0),
    }),
    { conflict: 0, missing: 0, inferred: 0 }
  );
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

function candidateToMetric(candidate: FinancialMetricCandidate): StructuredFinancialMetricInput {
  return {
    name: candidate.name,
    period: candidate.period,
    value: candidate.value ?? null,
    unit: candidate.unit ?? null,
    currency: candidate.currency ?? null,
    source:
      candidate.sourceKind === "human_corrected"
        ? "human_corrected"
        : candidate.extractionStrategy || candidate.evidence || null,
    sourcePage: candidate.sourcePage ?? null,
    confidence:
      candidate.sourceKind === "human_corrected" ? 1 : candidate.confidence,
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

function createUpdateRequest(
  draft: FinancialMetricsExtractionDraft
): UpdateFinancialMetricsExtractionDraftRequest {
  const selectedCandidates = selectedMetricCandidates(draft.payload.candidates);
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
      metrics: selectedCandidates.map(candidateToMetric),
    },
    metricAdditions: [],
    metadataAdditions: [],
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
}: FinancialMetricsReviewPanelProps) {
  const [workingDraft, setWorkingDraft] = useState<FinancialMetricsExtractionDraft | null>(
    draft ?? null
  );

  useEffect(() => {
    setWorkingDraft(draft ?? null);
  }, [draft]);

  const counts = useMemo(
    () => countReviewStates(workingDraft?.payload.candidates ?? []),
    [workingDraft]
  );
  const validation = workingDraft
    ? validateReviewDraft(workingDraft)
    : { canConfirm: false, blockingCandidateIds: [] };
  const hasUnresolvedConflicts = (workingDraft?.payload.conflicts ?? [])
    .some((conflict) => workingDraft && !isConflictResolvedLocally(conflict, workingDraft));
  const canConfirm = validation.canConfirm && !hasUnresolvedConflicts;
  const dirty = isDraftDirty(draft ?? null, workingDraft);

  if (isLoading && !workingDraft) {
    return (
      <section className="financialMetricsReviewPanel" aria-live="polite">
        <p className="financialMetricsReviewEyebrow">Revisión de extracción PDF</p>
        <strong>Cargando borrador de revisión...</strong>
      </section>
    );
  }

  if (!workingDraft) {
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

  function handleValueEdit(candidateId: string, valueText: string) {
    if (!workingDraft) {
      return;
    }

    const value = valueText.trim().length === 0 ? null : Number(valueText);
    setWorkingDraft(applyCandidateEdit(workingDraft, candidateId, { value }));
  }

  async function handleSave() {
    if (!workingDraft) {
      return null;
    }

    const updated = await onUpdate(workingDraft.id, createUpdateRequest(workingDraft));
    if (updated) {
      setWorkingDraft(updated);
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

    await onConfirm(saved.id);
  }

  const blockingReason = !validation.canConfirm
    ? `${validation.blockingCandidateIds.length} candidato(s) requieren decisión.`
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
        <span>Estado: {workingDraft.status}</span>
      </div>

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
