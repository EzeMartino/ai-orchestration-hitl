import assert from "node:assert/strict";
import { register } from "node:module";
import test from "node:test";
import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";

register("./tsx-loader.mjs", import.meta.url);

const { FinancialRiskEvidencePanel } = await import(
  "../src/components/FinancialRiskEvidencePanel.tsx"
);

function financialAnalysis(confidence: number | null) {
  return {
    engine: "test-engine",
    documentId: "document-1",
    ratios: [],
    comparisons: [],
    riskSignals: [],
    riskEvidence: [{
      code: "LOW_LIQUIDITY",
      title: "Low liquidity",
      severity: "High",
      message: "Liquidity is below the review threshold.",
      metric: "current_ratio",
      period: "2026",
      value: 0.8,
      threshold: 1,
      engine: "test-engine",
      confidence,
    }],
    warnings: [],
    limitations: [],
  };
}

test("financial confidence is rendered only when the persisted value exists", () => {
  const missingConfidence = renderToStaticMarkup(
    createElement(FinancialRiskEvidencePanel, {
      financialAnalysis: financialAnalysis(null),
    }),
  );
  const numericConfidence = renderToStaticMarkup(
    createElement(FinancialRiskEvidencePanel, {
      financialAnalysis: financialAnalysis(0.42),
    }),
  );

  assert.doesNotMatch(missingConfidence, />Confianza</);
  assert.match(numericConfidence, />Confianza</);
  assert.match(numericConfidence, />42%</);
});
