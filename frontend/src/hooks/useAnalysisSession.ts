import { useState, useEffect, useCallback } from "react";
import type {
  ActivityEvent,
  AnalysisSessionResponse,
  AnalysisSessionSummary,
  AnalysisSessionStartPreflightResult,
  StructuredFinancialMetricsContext,
  SaveFinancialMetricsResponse,
  StructuredFinancialMetricsInput,
  StructuredFinancialMetricsCsvInput,
  StructuredFinancialMetricsFileMetadata,
} from "../types/domain.types";
import * as api from "../services/api";

export function useAnalysisSession() {
  const [session, setSession] = useState<AnalysisSessionResponse | null>(null);
  const [events, setEvents] = useState<ActivityEvent[]>([]);
  const [isCreating, setIsCreating] = useState(false);
  const [isStarting, setIsStarting] = useState(false);
  const [decisionReason, setDecisionReason] = useState("");
  const [isSubmittingDecision, setIsSubmittingDecision] = useState(false);
  const [savedSessions, setSavedSessions] = useState<AnalysisSessionSummary[]>([]);
  const [selectedSessionId, setSelectedSessionId] = useState("");
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [structuredMetrics, setStructuredMetrics] = useState<StructuredFinancialMetricsContext | null>(null);
  const [isLoadingStructuredMetrics, setIsLoadingStructuredMetrics] = useState(false);
  const [isSavingStructuredMetrics, setIsSavingStructuredMetrics] = useState(false);
  const [metricsSaveResult, setMetricsSaveResult] = useState<SaveFinancialMetricsResponse | null>(null);
  const [metricsSaveError, setMetricsSaveError] = useState<string | null>(null);
  const [startPreflight, setStartPreflight] = useState<AnalysisSessionStartPreflightResult | null>(null);
  const [isCheckingStartPreflight, setIsCheckingStartPreflight] = useState(false);
  const [startPreflightError, setStartPreflightError] = useState<string | null>(null);

  // Helper to add activity events from SignalR
  const addActivityEvent = useCallback((event: ActivityEvent) => {
    setEvents((currentEvents) => {
      const alreadyExists = currentEvents.some(
        (currentEvent) =>
          currentEvent.sessionId === event.sessionId &&
          currentEvent.type === event.type &&
          currentEvent.agent === event.agent &&
          currentEvent.message === event.message &&
          currentEvent.timestamp === event.timestamp
      );

      if (alreadyExists) {
        return currentEvents;
      }

      return [event, ...currentEvents];
    });
  }, []);

  const loadSessionEvents = async (sessionId: string) => {
    try {
      const historicalEvents = await api.loadSessionEvents(sessionId);
      setEvents(historicalEvents);
    } catch (error) {
      console.error(error);
      throw error;
    }
  };

  const loadSavedSessions = async () => {
    try {
      const sessions = await api.loadSavedSessions();
      setSavedSessions(sessions);
      if (sessions.length > 0 && !selectedSessionId) {
        setSelectedSessionId(sessions[0].id);
      }
    } catch (error) {
      console.error(error);
      throw error;
    }
  };

  const loadStructuredFinancialMetrics = async (sessionId: string) => {
    setIsLoadingStructuredMetrics(true);
    try {
      const payload = await api.loadStructuredFinancialMetrics(sessionId);
      setStructuredMetrics(payload.context ?? null);
    } finally {
      setIsLoadingStructuredMetrics(false);
    }
  };

  const refreshStartPreflight = async (sessionId: string) => {
    setIsCheckingStartPreflight(true);
    setStartPreflightError(null);
    try {
      const preflight = await api.getStartPreflight(sessionId);
      setStartPreflight(preflight);
    } catch (error) {
      console.error(error);
      setStartPreflight(null);
      setStartPreflightError(
        "Could not check start readiness. Backend preflight will still run when starting."
      );
    } finally {
      setIsCheckingStartPreflight(false);
    }
  };

  const createSession = async () => {
    setIsCreating(true);
    setErrorMessage(null);
    try {
      const createdSession = await api.createSession();
      setSession(createdSession);
      setEvents([]);
      setStructuredMetrics(null);
      setMetricsSaveResult(null);
      setMetricsSaveError(null);
      setSelectedSessionId(createdSession.id);

      await loadSavedSessions();
      await refreshStartPreflight(createdSession.id);
    } catch (error) {
      console.error(error);
      setErrorMessage("Could not create the analysis session.");
    } finally {
      setIsCreating(false);
    }
  };

  const startSession = async () => {
    if (!session) return;
    setIsStarting(true);
    setErrorMessage(null);
    try {
      const response = await api.startSession(session.id);
      if (!response.ok) {
        if (response.status === 409) {
          try {
            const conflictPayload = await response.json();
            const isPreflight =
              conflictPayload &&
              typeof conflictPayload === "object" &&
              typeof conflictPayload.canStart === "boolean" &&
              Array.isArray(conflictPayload.errors) &&
              Array.isArray(conflictPayload.warnings);

            if (isPreflight) {
              setStartPreflight(conflictPayload);
              const missingMetrics = conflictPayload.errors.find(
                (issue: { code: string }) => issue.code === "STRUCTURED_FINANCIAL_METRICS_REQUIRED"
              );
              setErrorMessage(
                missingMetrics
                  ? "Structured financial metrics are required before starting this analysis. Attach JSON/CSV/PDF metrics and try again."
                  : conflictPayload.errors[0]?.message ?? "The analysis session could not be started."
              );
              await loadSessionEvents(session.id);
              return;
            }

            setErrorMessage(
              conflictPayload.error && typeof conflictPayload.error === "string"
                ? conflictPayload.error
                : "Could not start the analysis session."
            );
            return;
          } catch {
            setErrorMessage("Could not start the analysis session.");
            return;
          }
        }
        throw new Error("Failed to start analysis session.");
      }

      const updatedSession = await response.json() as AnalysisSessionResponse;
      setSession((currentSession) => ({
        ...currentSession,
        ...updatedSession,
      }));

      await loadSessionEvents(updatedSession.id);
      await loadSavedSessions();
      await refreshStartPreflight(updatedSession.id);
    } catch (error) {
      console.error(error);
      setErrorMessage("Could not start the analysis session.");
    } finally {
      setIsStarting(false);
    }
  };

  const submitHumanDecision = async (decision: "approve" | "reject") => {
    if (!session) return;
    setIsSubmittingDecision(true);
    setErrorMessage(null);
    try {
      const fallbackReason =
        decision === "approve"
          ? "Approved by human auditor."
          : "Rejected by human auditor.";
      const reason = decisionReason.trim().length > 0 ? decisionReason : fallbackReason;

      const updatedSession = await api.submitHumanDecision(session.id, decision, reason);
      setSession((currentSession) => ({
        ...currentSession,
        ...updatedSession,
      }));

      await loadSessionEvents(updatedSession.id);
      await loadSavedSessions();
      setDecisionReason("");
    } catch (error) {
      console.error(error);
      setErrorMessage(`Could not ${decision} the analysis session.`);
    } finally {
      setIsSubmittingDecision(false);
    }
  };

  const loadExistingSession = async (sessionId?: string) => {
    const idToLoad = sessionId ?? selectedSessionId;
    if (!idToLoad) return;
    setErrorMessage(null);
    try {
      const loadedSession = await api.loadSessionDetails(idToLoad);
      setSession(loadedSession);
      await loadSessionEvents(loadedSession.id);
      await refreshStartPreflight(loadedSession.id);
    } catch (error) {
      console.error(error);
      setErrorMessage("Could not load the analysis session.");
    }
  };

  const saveJsonMetrics = async (input: StructuredFinancialMetricsInput) => {
    if (!session) return;
    setIsSavingStructuredMetrics(true);
    setMetricsSaveError(null);
    setMetricsSaveResult(null);
    try {
      const result = await api.saveJsonMetrics(session.id, input);
      setMetricsSaveResult(result);
      if (result.isValid) {
        await loadStructuredFinancialMetrics(session.id);
        const updated = await api.loadSessionDetails(session.id);
        setSession(updated);
      }
      await refreshStartPreflight(session.id);
    } catch (error) {
      console.error(error);
      setMetricsSaveError("Could not save structured financial metrics.");
    } finally {
      setIsSavingStructuredMetrics(false);
    }
  };

  const saveCsvMetrics = async (input: StructuredFinancialMetricsCsvInput) => {
    if (!session) return;
    setIsSavingStructuredMetrics(true);
    setMetricsSaveError(null);
    setMetricsSaveResult(null);
    try {
      const result = await api.saveCsvMetrics(session.id, input);
      setMetricsSaveResult(result);
      if (result.isValid) {
        await loadStructuredFinancialMetrics(session.id);
        const updated = await api.loadSessionDetails(session.id);
        setSession(updated);
      }
      await refreshStartPreflight(session.id);
    } catch (error) {
      console.error(error);
      setMetricsSaveError("Could not save structured financial metrics.");
    } finally {
      setIsSavingStructuredMetrics(false);
    }
  };

  const uploadFinancialMetricsFile = async (
    file: File,
    metadata: StructuredFinancialMetricsFileMetadata
  ) => {
    if (!session) return;
    setIsSavingStructuredMetrics(true);
    setMetricsSaveError(null);
    setMetricsSaveResult(null);
    try {
      const result = await api.uploadFinancialMetricsFile(session.id, file, metadata);
      setMetricsSaveResult(result);
      if (result.isValid) {
        await loadStructuredFinancialMetrics(session.id);
        const updated = await api.loadSessionDetails(session.id);
        setSession(updated);
      }
      await refreshStartPreflight(session.id);
    } catch (error) {
      console.error(error);
      setMetricsSaveError(
        error instanceof Error
          ? error.message
          : "File upload failed. Please check the file format and try again."
      );
    } finally {
      setIsSavingStructuredMetrics(false);
    }
  };

  // Initial load
  useEffect(() => {
    loadSavedSessions().catch((error) => {
      console.error("Failed to load saved sessions:", error);
    });
  }, []);

  // Sync session details when session changes
  useEffect(() => {
    setMetricsSaveResult(null);
    setMetricsSaveError(null);

    if (!session?.id) {
      setStructuredMetrics(null);
      setStartPreflight(null);
      setStartPreflightError(null);
      setIsCheckingStartPreflight(false);
      return;
    }

    setStartPreflight(null);
    setStartPreflightError(null);

    loadStructuredFinancialMetrics(session.id).catch((error) => {
      console.error("Failed to load structured financial metrics:", error);
      setStructuredMetrics(null);
    });

    refreshStartPreflight(session.id).catch((error) => {
      console.error("Failed to load start preflight:", error);
    });
  }, [session?.id]);

  return {
    session,
    events,
    isCreating,
    isStarting,
    decisionReason,
    setDecisionReason,
    isSubmittingDecision,
    savedSessions,
    selectedSessionId,
    setSelectedSessionId,
    errorMessage,
    setErrorMessage,
    structuredMetrics,
    isLoadingStructuredMetrics,
    isSavingStructuredMetrics,
    metricsSaveResult,
    metricsSaveError,
    startPreflight,
    isCheckingStartPreflight,
    startPreflightError,
    addActivityEvent,
    createSession,
    startSession,
    submitHumanDecision,
    loadExistingSession,
    loadSavedSessions,
    saveJsonMetrics,
    saveCsvMetrics,
    uploadFinancialMetricsFile,
    refreshStartPreflight,
  };
}
