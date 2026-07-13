import assert from "node:assert/strict";
import test from "node:test";
import {
  applyCandidateEdit,
  applyMetadataCandidateEdit,
  applyMetadataCandidateRejection,
  applyReportSummaryToDraft,
  getDraftReportSummaryForm,
  isCurrentSessionRequest,
  isFinancialReviewInteractionDisabled,
  mapCandidateToStructuredMetric,
  parseFiniteMetricValue,
  shouldLoadFinancialMetricsReview,
  validateReviewDraft,
} from "../src/utils/financialMetricsReview.ts";
import { validateFinancialReportSummary } from "../src/utils/financialReportSummary.ts";

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

test("validateReviewDraft allows a pending metric addition to satisfy missing metrics", () => {
  const draft = createDraft([], {
    missingFields: ["metrics"],
  });

  const result = validateReviewDraft(draft, { metricAdditionCount: 1 });

  assert.equal(result.canConfirm, true);
  assert.deepEqual(result.missingFields, []);
});

test("validateReviewDraft ignores stale backend missing fields when current draft is complete", () => {
  const draft = createDraft([{ id: "accepted-metric", reviewState: "accepted" }], {
    missingFields: ["required_ratio_inputs_missing"],
  });

  const result = validateReviewDraft(draft);

  assert.equal(result.canConfirm, true);
  assert.deepEqual(result.missingFields, []);
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

test("applyMetadataCandidateRejection clears the proposed metadata value and keeps confirmation blocked", () => {
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
  });

  const updated = applyMetadataCandidateRejection(draft, "metadata-currency");
  const validation = validateReviewDraft(updated);

  assert.equal(updated.payload.metadataCandidates[0].reviewState, "rejected");
  assert.equal(updated.payload.proposedInput.currency, null);
  assert.deepEqual(validation.missingFields, ["currency"]);
  assert.equal(validation.canConfirm, false);
});

test("parseFiniteMetricValue rejects non-finite candidate edits", () => {
  assert.equal(parseFiniteMetricValue(""), null);
  assert.equal(parseFiniteMetricValue("123.45"), 123.45);
  assert.equal(parseFiniteMetricValue("1e999"), undefined);
  assert.equal(parseFiniteMetricValue("not-a-number"), undefined);
});

test("mapCandidateToStructuredMetric uses deterministic parser evidence as the source", () => {
  const metric = mapCandidateToStructuredMetric(createDraft([{
    extractionStrategy: "deterministic_pdf_parser",
    evidence: "Revenue 2025A 1,200",
    sourceKind: "reported",
  }]).payload.candidates[0]);

  assert.equal(metric.source, "Revenue 2025A 1,200");
});

test("mapCandidateToStructuredMetric uses semantic extraction strategy as the source", () => {
  const metric = mapCandidateToStructuredMetric(createDraft([{
    extractionStrategy: "semantic",
    evidence: "Revenue 2025A 1,200",
    sourceKind: "reported",
  }]).payload.candidates[0]);

  assert.equal(metric.source, "semantic");
});

test("isCurrentSessionRequest rejects completions from superseded sessions or generations", () => {
  assert.equal(isCurrentSessionRequest("session-a", 4, "session-a", 4), true);
  assert.equal(isCurrentSessionRequest("session-a", 4, "session-b", 4), false);
  assert.equal(isCurrentSessionRequest("session-a", 4, "session-a", 5), false);
});

test("shouldLoadFinancialMetricsReview blocks stale sessions and same-session uploads", () => {
  assert.equal(shouldLoadFinancialMetricsReview("session-a", "session-a", null), true);
  assert.equal(shouldLoadFinancialMetricsReview("session-a", "session-b", null), false);
  assert.equal(shouldLoadFinancialMetricsReview("session-a", "session-a", "session-a"), false);
});

test("financial review interactions are disabled while loading or saving", () => {
  assert.equal(isFinancialReviewInteractionDisabled(false, false), false);
  assert.equal(isFinancialReviewInteractionDisabled(true, false), true);
  assert.equal(isFinancialReviewInteractionDisabled(false, true), true);
  assert.equal(isFinancialReviewInteractionDisabled(true, true), true);
});

test("version 1 review drafts start with empty explicit report summary fields", () => {
  const draft = createDraft([{ reviewState: "accepted" }]);

  assert.deepEqual(getDraftReportSummaryForm(draft), {
    reportName: "",
    totalAmount: "",
    transactionCount: "",
    submittedAt: "",
  });
});

test("version 2 review drafts prepopulate exact proposed report summary", () => {
  const draft = createDraft([{ reviewState: "accepted" }]);
  draft.payload.schemaVersion = 2;
  draft.payload.proposedInput.reportSummary = {
    reportName: "Q2 report",
    totalAmount: 842350.75,
    transactionCount: 187,
    submittedAt: "2026-07-12T18:30:00.000Z",
  };

  const form = getDraftReportSummaryForm(draft);
  assert.equal(form.reportName, "Q2 report");
  assert.equal(form.totalAmount, "842350.75");
  assert.equal(form.transactionCount, "187");
  assert.equal(
    validateFinancialReportSummary(form).value?.submittedAt,
    "2026-07-12T18:30:00.000Z",
  );
});

test("report summary validation remains an independent confirmation gate", () => {
  const draft = createDraft([{ reviewState: "accepted" }]);
  const invalidSummary = validateFinancialReportSummary({
    reportName: "",
    totalAmount: "10",
    transactionCount: "1",
    submittedAt: "2026-07-12T18:30",
  });

  const result = validateReviewDraft(draft, { reportSummary: invalidSummary });

  assert.equal(result.canConfirm, false);
  assert.deepEqual(result.reportSummaryIssues.map((issue) => issue.code), [
    "REPORT_NAME_REQUIRED",
  ]);
});

test("corrected summary is copied into proposed input for update and confirmation", () => {
  const draft = createDraft([{ reviewState: "accepted" }]);
  const summary = {
    reportName: "corrected.pdf",
    totalAmount: 99.5,
    transactionCount: 4,
    submittedAt: "2026-07-12T18:30:00.000Z",
  };

  const updated = applyReportSummaryToDraft(draft, summary);

  assert.deepEqual(updated.payload.proposedInput.reportSummary, summary);
});
