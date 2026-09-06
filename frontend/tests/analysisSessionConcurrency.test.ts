import assert from "node:assert/strict";
import { register } from "node:module";
import test from "node:test";
import { renderHook } from "./hookRuntime.mjs";

register("./hook-loader.mjs", import.meta.url);

const { useAnalysisSession } = await import("../src/hooks/useAnalysisSession.ts");

type Deferred<T> = {
  promise: Promise<T>;
  resolve(value: T): void;
  reject(error: Error): void;
};

function deferred<T>(): Deferred<T> {
  let resolve!: (value: T) => void;
  let reject!: (error: Error) => void;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, resolve, reject };
}

function session(id: string, status = "Pending") {
  return { id, status };
}

function event(sessionId: string, type = "updated") {
  return {
    sessionId,
    type,
    agent: "PlannerAgent",
    message: `${sessionId}-${type}`,
    timestamp: `2026-09-05T12:00:0${sessionId === "A" ? "1" : "2"}Z`,
  };
}

function preflight(sessionId: string) {
  return { canStart: true, errors: [], warnings: [], sessionId };
}

function metrics(sessionId: string) {
  return {
    context: {
      documentId: `document-${sessionId}`,
      metrics: [],
      validationWarnings: [],
      uploadedAt: "2026-09-05T12:00:00Z",
    },
    reportSummary: null,
  };
}

function api(overrides: Record<string, (...args: never[]) => unknown> = {}) {
  return {
    loadSessionEvents: async (sessionId: string) => [event(sessionId)],
    loadSavedSessions: async () => [],
    loadSessionDetails: async (sessionId: string) => session(sessionId),
    loadStructuredFinancialMetrics: async (sessionId: string) => metrics(sessionId),
    getFinancialMetricsReview: async () => null,
    updateFinancialMetricsReview: async () => null,
    confirmFinancialMetricsReview: async () => null,
    discardFinancialMetricsReview: async () => null,
    getStartPreflight: async (sessionId: string) => preflight(sessionId),
    createSession: async () => session("created"),
    startSession: async (sessionId: string) => Response.json(session(sessionId, "DataGathering")),
    submitHumanDecision: async (sessionId: string) => session(sessionId, "Completed"),
    saveJsonMetrics: async () => ({ isValid: false, errors: [], warnings: [] }),
    saveCsvMetrics: async () => ({ isValid: false, errors: [], warnings: [] }),
    uploadFinancialMetricsFile: async () => ({ isValid: false, errors: [], warnings: [] }),
    ...overrides,
  };
}

async function settle(harness: ReturnType<typeof renderHook>) {
  await harness.flush();
  await new Promise((resolve) => setImmediate(resolve));
  await harness.flush();
}

test("the newest session load wins and foreign live events are ignored", async () => {
  const detailsA = deferred<ReturnType<typeof session>>();
  const detailsB = deferred<ReturnType<typeof session>>();
  globalThis.__analysisSessionApi = api({
    loadSessionDetails: (sessionId: string) => sessionId === "A" ? detailsA.promise : detailsB.promise,
  });
  const harness = renderHook(useAnalysisSession);
  await settle(harness);

  const loadingA = harness.result.current.loadExistingSession("A");
  const loadingB = harness.result.current.loadExistingSession("B");
  detailsB.resolve(session("B"));
  await loadingB;
  await settle(harness);

  harness.result.current.addActivityEvent(event("A", "foreign"));
  harness.result.current.addActivityEvent(event("B", "live"));
  await settle(harness);
  detailsA.resolve(session("A"));
  await loadingA;
  await settle(harness);

  assert.equal(harness.result.current.session?.id, "B");
  assert.ok(harness.result.current.events.length > 0);
  assert.ok(harness.result.current.events.every((item) => item.sessionId === "B"));
  harness.unmount();
});

test("a history refresh preserves live events and newest-first ordering", async () => {
  const history = deferred<ReturnType<typeof event>[]>();
  globalThis.__analysisSessionApi = api({
    loadSessionEvents: () => history.promise,
  });
  const harness = renderHook(useAnalysisSession);
  await settle(harness);

  const loading = harness.result.current.loadExistingSession("A");
  await settle(harness);
  harness.result.current.addActivityEvent(event("A", "live"));
  await settle(harness);
  history.resolve([
    { ...event("A", "newer"), timestamp: "2026-09-05T12:01:00Z" },
    { ...event("A", "historical"), timestamp: "2026-09-05T12:00:00Z" },
  ]);
  await loading;
  await settle(harness);

  assert.deepEqual(
    harness.result.current.events.map((item) => item.type),
    ["newer", "live", "historical"],
  );
  harness.unmount();
});

test("a stale start failure cannot clear the current session busy or error state", async () => {
  const starts = { A: deferred<Response>(), B: deferred<Response>() };
  globalThis.__analysisSessionApi = api({
    startSession: (sessionId: "A" | "B") => starts[sessionId].promise,
  });
  const harness = renderHook(useAnalysisSession);
  await settle(harness);
  await harness.result.current.loadExistingSession("A");
  await settle(harness);

  const startingA = harness.result.current.startSession();
  await harness.result.current.loadExistingSession("B");
  await settle(harness);
  const startingB = harness.result.current.startSession();
  await settle(harness);
  const originalConsoleError = console.error;
  console.error = () => undefined;
  starts.A.reject(new Error("A failed late"));
  await startingA;
  console.error = originalConsoleError;
  await settle(harness);

  assert.equal(harness.result.current.session?.id, "B");
  assert.equal(harness.result.current.isStarting, true);
  assert.equal(harness.result.current.errorMessage, null);

  starts.B.resolve(Response.json(session("B", "DataGathering")));
  await startingB;
  await settle(harness);
  assert.equal(harness.result.current.session?.id, "B");
  assert.equal(harness.result.current.isStarting, false);
  harness.unmount();
});

test("a superseded same-session start still releases its own busy flag", async () => {
  const start = deferred<Response>();
  const decision = deferred<ReturnType<typeof session>>();
  globalThis.__analysisSessionApi = api({
    startSession: () => start.promise,
    submitHumanDecision: () => decision.promise,
  });
  const harness = renderHook(useAnalysisSession);
  await settle(harness);
  await harness.result.current.loadExistingSession("A");
  await settle(harness);

  const starting = harness.result.current.startSession();
  const deciding = harness.result.current.submitHumanDecision("approve");
  start.resolve(Response.json(session("A", "DataGathering")));
  await starting;
  await settle(harness);

  assert.equal(harness.result.current.session?.id, "A");
  assert.equal(harness.result.current.isStarting, false);
  assert.equal(harness.result.current.isSubmittingDecision, true);

  decision.resolve(session("A", "Completed"));
  await deciding;
  await settle(harness);
  harness.unmount();
});

test("a superseded same-session decision still releases its own busy flag", async () => {
  const start = deferred<Response>();
  const decision = deferred<ReturnType<typeof session>>();
  globalThis.__analysisSessionApi = api({
    startSession: () => start.promise,
    submitHumanDecision: () => decision.promise,
  });
  const harness = renderHook(useAnalysisSession);
  await settle(harness);
  await harness.result.current.loadExistingSession("A");
  await settle(harness);

  const deciding = harness.result.current.submitHumanDecision("approve");
  const starting = harness.result.current.startSession();
  decision.resolve(session("A", "Completed"));
  await deciding;
  await settle(harness);

  assert.equal(harness.result.current.session?.status, "Pending");
  assert.equal(harness.result.current.isSubmittingDecision, false);
  assert.equal(harness.result.current.isStarting, true);

  start.resolve(Response.json(session("A", "DataGathering")));
  await starting;
  await settle(harness);
  harness.unmount();
});

test("stale decision and metric save handlers cannot overwrite current flags or errors", async () => {
  const decisions = { A: deferred<ReturnType<typeof session>>(), B: deferred<ReturnType<typeof session>>() };
  const saves = {
    A: deferred<{ isValid: boolean; errors: never[]; warnings: never[] }>(),
    B: deferred<{ isValid: boolean; errors: never[]; warnings: never[] }>(),
  };
  globalThis.__analysisSessionApi = api({
    submitHumanDecision: (sessionId: "A" | "B") => decisions[sessionId].promise,
    saveJsonMetrics: (sessionId: "A" | "B") => saves[sessionId].promise,
  });
  const harness = renderHook(useAnalysisSession);
  await settle(harness);
  await harness.result.current.loadExistingSession("A");
  await settle(harness);

  const decidingA = harness.result.current.submitHumanDecision("approve");
  const savingA = harness.result.current.saveJsonMetrics({ documentId: "A", metrics: [] });
  await harness.result.current.loadExistingSession("B");
  await settle(harness);
  const decidingB = harness.result.current.submitHumanDecision("approve");
  const savingB = harness.result.current.saveJsonMetrics({ documentId: "B", metrics: [] });
  await settle(harness);

  const originalConsoleError = console.error;
  console.error = () => undefined;
  decisions.A.reject(new Error("A decision failed late"));
  saves.A.reject(new Error("A save failed late"));
  await Promise.all([decidingA, savingA]);
  console.error = originalConsoleError;
  await settle(harness);

  assert.equal(harness.result.current.session?.id, "B");
  assert.equal(harness.result.current.isSubmittingDecision, true);
  assert.equal(harness.result.current.isSavingStructuredMetrics, true);
  assert.equal(harness.result.current.errorMessage, null);
  assert.equal(harness.result.current.metricsSaveError, null);

  decisions.B.resolve(session("B", "Completed"));
  saves.B.resolve({ isValid: false, errors: [], warnings: [] });
  await Promise.all([decidingB, savingB]);
  await settle(harness);
  assert.equal(harness.result.current.session?.id, "B");
  assert.equal(harness.result.current.isSubmittingDecision, false);
  assert.equal(harness.result.current.isSavingStructuredMetrics, false);
  harness.unmount();
});

test("older same-session events metrics and preflight cannot replace a later reload", async () => {
  const oldEvents = deferred<ReturnType<typeof event>[]>();
  const oldMetrics = deferred<ReturnType<typeof metrics>>();
  const oldPreflight = deferred<ReturnType<typeof preflight>>();
  let aEventLoads = 0;
  let aMetricLoads = 0;
  let aPreflightLoads = 0;
  globalThis.__analysisSessionApi = api({
    loadSessionEvents: (sessionId: string) => {
      if (sessionId === "A" && aEventLoads++ === 0) return oldEvents.promise;
      return Promise.resolve([event(sessionId, "current")]);
    },
    loadStructuredFinancialMetrics: (sessionId: string) => {
      if (sessionId === "A" && aMetricLoads++ === 0) return oldMetrics.promise;
      return Promise.resolve(metrics(`${sessionId}-current`));
    },
    getStartPreflight: (sessionId: string) => {
      if (sessionId === "A" && aPreflightLoads++ === 0) return oldPreflight.promise;
      return Promise.resolve(preflight(`${sessionId}-current`));
    },
  });
  const harness = renderHook(useAnalysisSession);
  await settle(harness);

  const firstA = harness.result.current.loadExistingSession("A");
  await settle(harness);
  await harness.result.current.loadExistingSession("B");
  await settle(harness);
  await harness.result.current.loadExistingSession("A");
  await settle(harness);

  oldEvents.resolve([event("A", "old")]);
  oldMetrics.resolve(metrics("A-old"));
  oldPreflight.resolve(preflight("A-old"));
  await firstA;
  await settle(harness);

  assert.equal(harness.result.current.session?.id, "A");
  assert.deepEqual(harness.result.current.events.map((item) => item.type), ["current"]);
  assert.equal(harness.result.current.structuredMetrics?.documentId, "document-A-current");
  assert.equal(harness.result.current.startPreflight?.sessionId, "A-current");
  assert.equal(harness.result.current.isLoadingStructuredMetrics, false);
  assert.equal(harness.result.current.isCheckingStartPreflight, false);
  harness.unmount();
});
