import assert from "node:assert/strict";
import test from "node:test";
import { getFinancialAnalysisExecutionBanner } from "../src/utils/financialAnalysisExecution.ts";

test("successful execution has no banner", () => {
  assert.equal(
    getFinancialAnalysisExecutionBanner({ overallStatus: "succeeded", stages: [] }),
    null,
  );
});

test("successful status with null stages fails closed as neutral", () => {
  const banner = getFinancialAnalysisExecutionBanner({
    overallStatus: "succeeded",
    stages: null,
  });

  assert.equal(banner?.tone, "neutral");
});

test("successful status with a contradictory failed stage fails closed", () => {
  const banner = getFinancialAnalysisExecutionBanner({
    overallStatus: "succeeded",
    stages: [{ operation: "signals", status: "failed" }],
  });

  assert.equal(banner?.tone, "neutral");
  assert.deepEqual(banner?.affectedStages, []);
});

test("successful complete unique stage metadata has no banner", () => {
  assert.equal(
    getFinancialAnalysisExecutionBanner({
      overallStatus: "succeeded",
      stages: [
        { operation: "ratios", status: "succeeded" },
        { operation: "comparisons", status: "succeeded" },
        { operation: "signals", status: "succeeded" },
        { operation: "summary", status: "succeeded" },
      ],
    }),
    null,
  );
});

test("successful nonempty stage metadata must be exact and well formed", () => {
  const cases = [
    [{ operation: "ratios", status: "succeeded" }],
    [
      { operation: "ratios", status: "succeeded" },
      { operation: "comparisons", status: "succeeded" },
      { operation: "signals", status: "succeeded" },
      { operation: "unknown", status: "succeeded" },
    ],
    [
      { operation: "ratios", status: "succeeded" },
      { operation: "comparisons", status: "succeeded" },
      { operation: "signals", status: "succeeded" },
      null,
    ],
    [
      { operation: "ratios", status: "succeeded" },
      { operation: "comparisons", status: "succeeded" },
      { operation: "signals", status: "succeeded" },
      { operation: "signals", status: "succeeded" },
    ],
  ];

  for (const stages of cases) {
    const banner = getFinancialAnalysisExecutionBanner({
      overallStatus: "succeeded",
      stages,
    } as never);

    assert.equal(banner?.tone, "neutral");
    assert.deepEqual(banner?.affectedStages, []);
  }
});

test("degraded execution requires review and keeps only safe stage labels", () => {
  const banner = getFinancialAnalysisExecutionBanner({
    overallStatus: "degraded",
    stages: [
      {
        operation: "ratios",
        status: "failed",
        durationMilliseconds: 12,
        failureCode: "PYTHON_INVOCATION_FAILED",
      },
    ],
  });

  assert.equal(banner?.tone, "warning");
  assert.equal(
    banner?.title,
    "Análisis incompleto — revisión humana requerida.",
  );
  assert.deepEqual(banner?.affectedStages, [
    "Ratios financieros — PYTHON_INVOCATION_FAILED",
  ]);
});

test("unknown failure codes are not rendered", () => {
  const banner = getFinancialAnalysisExecutionBanner({
    overallStatus: "failed",
    stages: [
      {
        operation: "signals",
        status: "failed",
        durationMilliseconds: 1,
        failureCode: "raw exception from provider",
      },
    ],
  });

  assert.deepEqual(banner?.affectedStages, ["Señales de riesgo"]);
  assert.doesNotMatch(
    JSON.stringify(banner),
    /raw exception|durationMilliseconds/i,
  );
});

test("missing or unknown execution is neutral historical metadata", () => {
  const missingBanner = getFinancialAnalysisExecutionBanner(undefined);
  const unknownBanner = getFinancialAnalysisExecutionBanner({
    overallStatus: "future_status",
    stages: [],
  });

  assert.equal(missingBanner?.tone, "neutral");
  assert.equal(unknownBanner?.tone, "neutral");
  assert.equal(
    missingBanner?.title,
    "No hay metadatos de ejecución disponibles para este análisis histórico.",
  );
  assert.equal(unknownBanner?.title, missingBanner?.title);
});

test("failed execution uses danger tone", () => {
  const banner = getFinancialAnalysisExecutionBanner({
    overallStatus: "failed",
    stages: [
      {
        operation: "summary",
        status: "failed",
        failureCode: "PYTHON_RESPONSE_INVALID",
      },
    ],
  });

  assert.equal(banner?.tone, "danger");
  assert.equal(
    banner?.title,
    "No se pudo completar la evaluación de riesgo — revisión humana requerida.",
  );
  assert.deepEqual(banner?.affectedStages, [
    "Resumen de evidencia — PYTHON_RESPONSE_INVALID",
  ]);
});

test("unknown operations and non-failed stages are excluded", () => {
  const banner = getFinancialAnalysisExecutionBanner({
    overallStatus: "degraded",
    stages: [
      {
        operation: "provider.internal_exception",
        status: "failed",
        failureCode: "PYTHON_INVOCATION_FAILED",
      },
      {
        operation: "__proto__",
        status: "failed",
        failureCode: "PYTHON_RESPONSE_INVALID",
      },
      {
        operation: "constructor",
        status: "failed",
        failureCode: "PYTHON_INVOCATION_FAILED",
      },
      {
        operation: "comparisons",
        status: "succeeded",
        failureCode: "PYTHON_RESPONSE_INVALID",
      },
    ],
  });

  assert.deepEqual(banner?.affectedStages, []);
  assert.doesNotMatch(JSON.stringify(banner), /provider|internal|comparaciones/i);
});

test("malformed stage collections do not break historical evidence rendering", () => {
  const banner = getFinancialAnalysisExecutionBanner({
    overallStatus: "degraded",
    stages: "raw provider payload",
  } as never);

  assert.equal(banner?.tone, "warning");
  assert.deepEqual(banner?.affectedStages, []);
});

test("duplicate failed operations produce one affected stage label", () => {
  const banner = getFinancialAnalysisExecutionBanner({
    overallStatus: "degraded",
    stages: [
      {
        operation: "ratios",
        status: "failed",
        failureCode: "PYTHON_INVOCATION_FAILED",
      },
      {
        operation: "ratios",
        status: "failed",
        failureCode: "PYTHON_RESPONSE_INVALID",
      },
    ],
  });

  assert.deepEqual(banner?.affectedStages, [
    "Ratios financieros — PYTHON_INVOCATION_FAILED",
  ]);
});
