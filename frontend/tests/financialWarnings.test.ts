import assert from "node:assert/strict";
import test from "node:test";
import {
  formatFinancialWarningText,
  groupFinancialWarnings,
} from "../src/utils/financialWarnings.ts";

test("groups missing ratio input warnings by period and ratio", () => {
  const result = groupFinancialWarnings([
    "Missing numerator input for gross_margin in 2025A.",
    "Missing denominator input for gross_margin in 2025A.",
    "Missing numerator inputs for quick_ratio in 2025A.",
    "Missing numerator input for ebitda_margin in 2026E.",
    "No period comparisons were produced from the provided metrics.",
  ]);

  assert.equal(result.periodGroups.length, 2);
  assert.equal(result.ungrouped.length, 1);
  assert.equal(result.periodGroups[0].period, "2025A");
  assert.equal(result.periodGroups[0].messageCount, 3);
  assert.deepEqual(
    result.periodGroups[0].items.map((item) => ({
      ratioName: item.ratioName,
      missingInputs: item.missingInputs,
      messages: item.messages,
    })),
    [
      {
        ratioName: "gross_margin",
        missingInputs: ["numerator", "denominator"],
        messages: [
          "Missing numerator input for gross_margin in 2025A.",
          "Missing denominator input for gross_margin in 2025A.",
        ],
      },
      {
        ratioName: "quick_ratio",
        missingInputs: ["numerator"],
        messages: ["Missing numerator inputs for quick_ratio in 2025A."],
      },
    ],
  );
  assert.equal(result.periodGroups[1].period, "2026E");
  assert.equal(result.periodGroups[1].items[0].ratioName, "ebitda_margin");
});

test("formats common backend financial warnings in Spanish", () => {
  assert.equal(
    formatFinancialWarningText(
      "No period comparisons were produced from the provided metrics.",
    ),
    "No se generaron comparaciones entre periodos a partir de las métricas provistas.",
  );
  assert.equal(
    formatFinancialWarningText("Zero denominator for ebitda_margin in 2024A."),
    "Denominador cero para ebitda_margin en 2024A.",
  );
});
