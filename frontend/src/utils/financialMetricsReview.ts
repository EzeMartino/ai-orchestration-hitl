import type {
  FinancialMetricCandidate,
  FinancialMetricsExtractionDraft,
} from "../types/domain.types";

const blockingReviewStates = new Set(["inferred", "conflict", "missing"]);

export type ReviewDraftValidationResult = {
  canConfirm: boolean;
  blockingCandidateIds: string[];
};

export type FinancialMetricCandidateEdit = Pick<
  FinancialMetricCandidate,
  "value" | "currency" | "unit"
>;

export function validateReviewDraft(
  draft: FinancialMetricsExtractionDraft,
): ReviewDraftValidationResult {
  const blockingCandidateIds = draft.payload.candidates
    .filter((candidate) => blockingReviewStates.has(candidate.reviewState))
    .map((candidate) => candidate.id);

  return {
    canConfirm: blockingCandidateIds.length === 0,
    blockingCandidateIds,
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
