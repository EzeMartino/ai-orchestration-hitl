import type {
  FinancialMetricCandidate,
  FinancialMetricsExtractionDraft,
  StructuredFinancialMetricInput,
  StructuredFinancialMetricsInput,
} from "../types/domain.types";

const blockingReviewStates = new Set(["inferred", "conflict", "missing"]);
const metadataFieldNames = new Set(["company", "currency", "unit"]);
const selectedReviewStates = new Set(["explicit", "accepted", "human_corrected"]);

export type ReviewDraftValidationResult = {
  canConfirm: boolean;
  blockingCandidateIds: string[];
  missingFields: string[];
};

export type ReviewDraftValidationOptions = {
  metricAdditionCount?: number;
};

export type FinancialMetricCandidateEdit = Pick<
  FinancialMetricCandidate,
  "value" | "currency" | "unit"
>;

export function mapCandidateToStructuredMetric(
  candidate: FinancialMetricCandidate,
): StructuredFinancialMetricInput {
  const source =
    candidate.sourceKind === "human_corrected"
      ? "human_corrected"
      : candidate.extractionStrategy === "deterministic_pdf_parser"
        ? candidate.evidence || null
        : candidate.extractionStrategy || candidate.evidence || null;

  return {
    name: candidate.name,
    period: candidate.period,
    value: candidate.value ?? null,
    unit: candidate.unit ?? null,
    currency: candidate.currency ?? null,
    source,
    sourcePage: candidate.sourcePage ?? null,
    confidence: candidate.sourceKind === "human_corrected" ? 1 : candidate.confidence,
  };
}

export function isCurrentSessionRequest(
  requestSessionId: string,
  requestGeneration: number,
  currentSessionId: string | null,
  currentGeneration: number,
) {
  return (
    requestSessionId === currentSessionId &&
    requestGeneration === currentGeneration
  );
}

export function shouldLoadFinancialMetricsReview(
  requestSessionId: string,
  activeSessionId: string | null,
  uploadSessionId: string | null,
) {
  return (
    requestSessionId === activeSessionId &&
    requestSessionId !== uploadSessionId
  );
}

export function parseFiniteMetricValue(valueText: string): number | null | undefined {
  if (valueText.trim().length === 0) {
    return null;
  }

  const value = Number(valueText);

  return Number.isFinite(value) ? value : undefined;
}

function normalizeMetadataFieldName(fieldName: string) {
  return fieldName.trim().toLowerCase();
}

function setProposedMetadataValue(
  input: StructuredFinancialMetricsInput,
  fieldName: string,
  value: string | null,
): StructuredFinancialMetricsInput {
  const normalized = normalizeMetadataFieldName(fieldName);
  const nextValue = value && value.trim().length > 0 ? value.trim() : null;

  switch (normalized) {
    case "company":
      return { ...input, company: nextValue };
    case "currency":
      return { ...input, currency: nextValue };
    case "unit":
      return { ...input, unit: nextValue };
    default:
      return input;
  }
}

function computeMissingFields(input: StructuredFinancialMetricsInput) {
  const missing: string[] = [];

  if (!input.company?.trim()) {
    missing.push("company");
  }

  if (!input.currency?.trim()) {
    missing.push("currency");
  }

  if (!input.unit?.trim()) {
    missing.push("unit");
  }

  if (!input.metrics?.length) {
    missing.push("metrics");
  }

  return missing;
}

function computeDraftMissingFields(
  draft: FinancialMetricsExtractionDraft,
  options: ReviewDraftValidationOptions,
) {
  const missing = new Set(
    computeMissingFields(draft.payload.proposedInput).map(normalizeMetadataFieldName)
  );
  const input = draft.payload.proposedInput;

  if (input.company?.trim()) {
    missing.delete("company");
  }

  if (input.currency?.trim()) {
    missing.delete("currency");
  }

  if (input.unit?.trim()) {
    missing.delete("unit");
  }

  if (
    (options.metricAdditionCount ?? 0) > 0 ||
    draft.payload.candidates.some((candidate) =>
      selectedReviewStates.has(candidate.reviewState)
    )
  ) {
    missing.delete("metrics");
  } else {
    missing.add("metrics");
  }

  return [...missing].filter((fieldName) => fieldName.length > 0);
}

export function validateReviewDraft(
  draft: FinancialMetricsExtractionDraft,
  options: ReviewDraftValidationOptions = {},
): ReviewDraftValidationResult {
  const blockingCandidateIds = [
    ...draft.payload.candidates,
    ...(draft.payload.metadataCandidates ?? []),
  ]
    .filter((candidate) => blockingReviewStates.has(candidate.reviewState))
    .map((candidate) => candidate.id);
  const missingFields = computeDraftMissingFields(draft, options);

  return {
    canConfirm: blockingCandidateIds.length === 0 && missingFields.length === 0,
    blockingCandidateIds,
    missingFields,
  };
}

export function applyCandidateEdit(
  draft: FinancialMetricsExtractionDraft,
  candidateId: string,
  edit: FinancialMetricCandidateEdit,
): FinancialMetricsExtractionDraft {
  return {
    ...draft,
    payload: {
      ...draft.payload,
      candidates: draft.payload.candidates.map((candidate) =>
        candidate.id === candidateId
          ? {
              ...candidate,
              ...edit,
              sourceKind: "human_corrected",
              reviewState: "human_corrected",
            }
          : candidate,
      ),
    },
  };
}

export function applyMetadataCandidateEdit(
  draft: FinancialMetricsExtractionDraft,
  candidateId: string,
  value: string,
): FinancialMetricsExtractionDraft {
  const edited = (draft.payload.metadataCandidates ?? []).find(
    (candidate) => candidate.id === candidateId,
  );
  const proposedInput =
    edited && metadataFieldNames.has(normalizeMetadataFieldName(edited.fieldName))
      ? setProposedMetadataValue(draft.payload.proposedInput, edited.fieldName, value)
      : draft.payload.proposedInput;

  return {
    ...draft,
    payload: {
      ...draft.payload,
      proposedInput,
      missingFields: computeMissingFields(proposedInput),
      metadataCandidates: (draft.payload.metadataCandidates ?? []).map((candidate) =>
        candidate.id === candidateId
          ? {
              ...candidate,
              value,
              sourceKind: "human_corrected",
              reviewState: "human_corrected",
            }
          : candidate,
      ),
    },
  };
}

export function applyProposedMetadataEdit(
  draft: FinancialMetricsExtractionDraft,
  fieldName: string,
  value: string,
): FinancialMetricsExtractionDraft {
  const proposedInput = setProposedMetadataValue(
    draft.payload.proposedInput,
    fieldName,
    value,
  );

  return {
    ...draft,
    payload: {
      ...draft.payload,
      proposedInput,
      missingFields: computeMissingFields(proposedInput),
    },
  };
}

export function applyMetadataCandidateRejection(
  draft: FinancialMetricsExtractionDraft,
  candidateId: string,
): FinancialMetricsExtractionDraft {
  const rejected = (draft.payload.metadataCandidates ?? []).find(
    (candidate) => candidate.id === candidateId,
  );
  const proposedInput = rejected
    ? setProposedMetadataValue(draft.payload.proposedInput, rejected.fieldName, null)
    : draft.payload.proposedInput;

  return {
    ...draft,
    payload: {
      ...draft.payload,
      proposedInput,
      missingFields: computeMissingFields(proposedInput),
      metadataCandidates: (draft.payload.metadataCandidates ?? []).map((candidate) =>
        candidate.id === candidateId
          ? {
              ...candidate,
              reviewState: "rejected",
            }
          : candidate,
      ),
    },
  };
}
