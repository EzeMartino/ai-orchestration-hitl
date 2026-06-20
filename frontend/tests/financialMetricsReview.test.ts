import assert from "node:assert/strict";
import test from "node:test";
import {
  applyCandidateEdit,
  applyMetadataCandidateEdit,
  validateReviewDraft,
} from "../src/utils/financialMetricsReview.ts";

function createDraft(candidateOverrides = [], overrides = {}) {
  return {
    id: "draft-1",
    sessionId: "session-1",
    status: "PendingReview",
    originalFileName: "metrics.pdf",
    fileSizeBytes: 2048,
    contentHash: "hash-1",
    createdAt: "2026-06-16T00:00:00Z",
    updatedAt: "2026-06-16T00:00:00Z",
    completedAt: null,
    payload: {
      schemaVersion: 1,
      proposedInput: {
        documentId: "metrics-pdf",
        company: "Demo Co",
        currency: "USD",
        unit: overrides.proposedUnit ?? "USD_thousand",
        metrics: [],
      },
      candidates: candidateOverrides.map((candidate, index) => ({
        id: `candidate-${index + 1}`,
        name: "Revenue",
        period: "2025A",
        value: 1200,
        currency: "USD",
        unit: "USD_thousand",
        sourceKind: "reported",
        confidence: 0.92,
        sourcePage: 12,
        evidence: "Revenue 2025A 1,200",
        extractionStrategy: "semantic",
        reviewState: "accepted",
        inferenceExplanation: null,
        ...candidate,
      })),
      conflicts: [],
      missingFields: overrides.missingFields ?? [],
      fallbackReasons: [],
      validationIssues: [],
      diagnostics: {
        nativeTextAvailable: true,
        ocrAttempted: false,
        ocrSucceeded: false,
        markItDownAttempted: true,
        markItDownSucceeded: true,
        semanticAttempted: true,
        semanticSucceeded: true,
        pageCount: 20,
        markdownCharacterCount: 5000,
        candidateCount: candidateOverrides.length,
        conflictCount: 0,
        nativeTextDurationMilliseconds: 10,
        ocrDurationMilliseconds: null,
        markItDownDurationMilliseconds: 30,
        semanticDurationMilliseconds: 40,
        totalDurationMilliseconds: 80,
        reasonCodes: [],
      },
      metadataCandidates: overrides.metadataCandidates ?? [],
    },
  };
}

test("validateReviewDraft blocks confirmation when inferred or conflict candidates remain and preserves blocking id order", () => {
  const draft = createDraft([
    { id: "accepted-first", reviewState: "accepted" },
    { id: "inferred-second", reviewState: "inferred" },
    { id: "conflict-third", reviewState: "conflict" },
    { id: "missing-fourth", reviewState: "missing" },
    { id: "rejected-fifth", reviewState: "rejected" },
  ]);

  const result = validateReviewDraft(draft);

  assert.equal(result.canConfirm, false);
  assert.deepEqual(result.blockingCandidateIds, [
    "inferred-second",
    "conflict-third",
    "missing-fourth",
  ]);
});

test("applyCandidateEdit marks edited candidates as human corrected", () => {
  const draft = createDraft([
    {
      id: "candidate-to-edit",
      value: 1200,
      currency: "USD",
      unit: "USD_thousand",
      sourceKind: "reported",
      reviewState: "accepted",
    },
  ]);

  const updated = applyCandidateEdit(draft, "candidate-to-edit", {
    value: 1300,
    currency: "EUR",
    unit: "EUR_million",
  });

  assert.equal(updated.payload.candidates[0].value, 1300);
  assert.equal(updated.payload.candidates[0].currency, "EUR");
  assert.equal(updated.payload.candidates[0].unit, "EUR_million");
  assert.equal(updated.payload.candidates[0].sourceKind, "human_corrected");
  assert.equal(updated.payload.candidates[0].reviewState, "human_corrected");
});

test("validateReviewDraft blocks confirmation when metadata candidates or missing fields remain unresolved", () => {
  const draft = createDraft([{ id: "accepted-metric", reviewState: "accepted" }], {
    metadataCandidates: [
      {
        id: "metadata-currency",
        fieldName: "currency",
        value: "USD",
        sourceKind: "inferred",
        confidence: 0.7,
        sourcePage: 1,
        evidence: "$ symbol",
        extractionStrategy: "semantic",
        reviewState: "inferred",
        inferenceExplanation: "Currency inferred from symbol.",
      },
    ],
    proposedUnit: "",
    missingFields: ["unit"],
  });

  const result = validateReviewDraft(draft);

  assert.equal(result.canConfirm, false);
  assert.deepEqual(result.blockingCandidateIds, ["metadata-currency"]);
  assert.deepEqual(result.missingFields, ["unit"]);
});

test("applyMetadataCandidateEdit marks edited metadata as human corrected and updates proposed input", () => {
  const draft = createDraft([], {
    metadataCandidates: [
      {
        id: "metadata-currency",
        fieldName: "currency",
        value: "USD",
        sourceKind: "inferred",
        confidence: 0.7,
        sourcePage: 1,
        evidence: "$ symbol",
        extractionStrategy: "semantic",
        reviewState: "inferred",
        inferenceExplanation: "Currency inferred from symbol.",
      },
    ],
  });

  const updated = applyMetadataCandidateEdit(draft, "metadata-currency", "ARS");

  assert.equal(updated.payload.metadataCandidates[0].value, "ARS");
  assert.equal(updated.payload.metadataCandidates[0].sourceKind, "human_corrected");
  assert.equal(updated.payload.metadataCandidates[0].reviewState, "human_corrected");
  assert.equal(updated.payload.proposedInput.currency, "ARS");
});
