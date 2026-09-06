const call = (name, args) => globalThis.__analysisSessionApi[name](...args);

export const loadSessionEvents = (...args) => call("loadSessionEvents", args);
export const loadSavedSessions = (...args) => call("loadSavedSessions", args);
export const loadSessionDetails = (...args) => call("loadSessionDetails", args);
export const loadStructuredFinancialMetrics = (...args) => call("loadStructuredFinancialMetrics", args);
export const getFinancialMetricsReview = (...args) => call("getFinancialMetricsReview", args);
export const updateFinancialMetricsReview = (...args) => call("updateFinancialMetricsReview", args);
export const confirmFinancialMetricsReview = (...args) => call("confirmFinancialMetricsReview", args);
export const discardFinancialMetricsReview = (...args) => call("discardFinancialMetricsReview", args);
export const getStartPreflight = (...args) => call("getStartPreflight", args);
export const createSession = (...args) => call("createSession", args);
export const startSession = (...args) => call("startSession", args);
export const submitHumanDecision = (...args) => call("submitHumanDecision", args);
export const saveJsonMetrics = (...args) => call("saveJsonMetrics", args);
export const saveCsvMetrics = (...args) => call("saveCsvMetrics", args);
export const uploadFinancialMetricsFile = (...args) => call("uploadFinancialMetricsFile", args);
