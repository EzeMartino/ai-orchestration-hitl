import assert from "node:assert/strict";
import test from "node:test";
import {
  formatLegalAssessmentValue,
  formatLegalRiskLevel,
} from "../src/utils/legalEvidenceAssessment.ts";

test("formats an unassessed legal risk while preserving other values", () => {
  assert.equal(formatLegalRiskLevel("NotEstablished"), "No determinado");
  assert.equal(formatLegalRiskLevel("Medium"), "Medium");
  assert.equal(formatLegalRiskLevel("Unexpected"), "Unexpected");
});

test("formats legal evidence assessment values", () => {
  const translations = new Map([
    ["None", "Ninguna"],
    ["Weak", "Débil"],
    ["Strong", "Fuerte"],
    ["NotEstablished", "No determinada"],
    ["Info", "Informativa"],
    ["Warning", "Revisión requerida"],
  ]);

  for (const [value, expected] of translations) {
    assert.equal(formatLegalAssessmentValue(value), expected);
  }
});

test("preserves unknown legal evidence assessment values", () => {
  assert.equal(formatLegalAssessmentValue("Unexpected"), "Unexpected");
});
