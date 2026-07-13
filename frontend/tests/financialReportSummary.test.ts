import assert from "node:assert/strict";
import test from "node:test";
import {
  canEditFinancialReportSummary,
  emptyFinancialReportSummaryForm,
  mapFinancialReportSummaryErrorsByField,
  validateFinancialReportSummary,
} from "../src/utils/financialReportSummary.ts";

test("valid summary trims name, accepts zero values, and normalizes ISO time", () => {
  const result = validateFinancialReportSummary({
    reportName: "  Q2 report  ",
    totalAmount: "0",
    transactionCount: "0",
    submittedAt: "2026-07-12T18:30",
  });

  assert.equal(result.isValid, true);
  assert.deepEqual(result.value, {
    reportName: "Q2 report",
    totalAmount: 0,
    transactionCount: 0,
    submittedAt: new Date("2026-07-12T18:30").toISOString(),
  });
  assert.deepEqual(result.issues, []);
});

test("missing fields return backend-compatible required codes", () => {
  const result = validateFinancialReportSummary(emptyFinancialReportSummaryForm());

  assert.equal(result.isValid, false);
  assert.deepEqual(result.issues.map((issue) => issue.code), [
    "REPORT_NAME_REQUIRED",
    "TOTAL_AMOUNT_REQUIRED",
    "TRANSACTION_COUNT_REQUIRED",
    "SUBMITTED_AT_REQUIRED",
  ]);
});

test("negative, non-finite, fractional count, and invalid date return exact invalid codes", () => {
  const negative = validateFinancialReportSummary({
    reportName: "Report",
    totalAmount: "-0.01",
    transactionCount: "-1",
    submittedAt: "not-a-date",
  });
  const nonFinite = validateFinancialReportSummary({
    reportName: "Report",
    totalAmount: "NaN",
    transactionCount: "1.5",
    submittedAt: "2026-07-12T18:30",
  });

  assert.deepEqual(negative.issues.map((issue) => issue.code), [
    "TOTAL_AMOUNT_INVALID",
    "TRANSACTION_COUNT_INVALID",
    "SUBMITTED_AT_INVALID",
  ]);
  assert.deepEqual(nonFinite.issues.map((issue) => issue.code), [
    "TOTAL_AMOUNT_INVALID",
    "TRANSACTION_COUNT_INVALID",
  ]);
});

test("summary editing is locked without a session, while loading, or while saving", () => {
  assert.equal(canEditFinancialReportSummary(undefined, false, false), false);
  assert.equal(canEditFinancialReportSummary("session-1", false, true), false);
  assert.equal(canEditFinancialReportSummary("session-1", true, false), false);
  assert.equal(canEditFinancialReportSummary("session-1", false, false), true);
});

test("summary issues map to human-readable messages by field", () => {
  const validation = validateFinancialReportSummary(emptyFinancialReportSummaryForm());

  assert.deepEqual(mapFinancialReportSummaryErrorsByField(validation.issues), {
    reportName: "El nombre del informe es obligatorio.",
    totalAmount: "El importe total es obligatorio.",
    transactionCount: "La cantidad de transacciones es obligatoria.",
    submittedAt: "La fecha de envío es obligatoria.",
  });
});
