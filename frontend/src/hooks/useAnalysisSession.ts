import { useState, useEffect, useCallback, useRef } from "react";
import type {
  ActivityEvent,
  AnalysisSessionResponse,
  AnalysisSessionSummary,
  AnalysisSessionStartPreflightResult,
  FinancialMetricsExtractionDraft,
  StructuredFinancialMetricsContext,
  SaveFinancialMetricsResponse,
  StructuredFinancialMetricsInput,
  StructuredFinancialMetricsCsvInput,
  StructuredFinancialMetricsFileMetadata,
  UpdateFinancialMetricsExtractionDraftRequest,
} from "../types/domain.types";
import * as api from "../services/api";
import {
  isCurrentSessionRequest,
  shouldLoadFinancialMetricsReview,
} from "../utils/financialMetricsReview";

export function useAnalysisSession() {
  const activeSessionId = useRef<string | null>(null);
  const financialMetricsReviewGeneration = useRef(0);
  const financialMetricsUploadSessionId = useRef<string | null>(null);
  const financialMetricsUploadGeneration = useRef(0);
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
  const [financialMetricsReview, setFinancialMetricsReview] =
    useState<FinancialMetricsExtractionDraft | null>(null);
  const [isLoadingFinancialMetricsReview, setIsLoadingFinancialMetricsReview] = useState(false);
  const [isSavingFinancialMetricsReview, setIsSavingFinancialMetricsReview] = useState(false);
  const [financialMetricsReviewError, setFinancialMetricsReviewError] = useState<string | null>(null);
  const [startPreflight, setStartPreflight] = useState<AnalysisSessionStartPreflightResult | null>(null);
  const [isCheckingStartPreflight, setIsCheckingStartPreflight] = useState(false);
  const [startPreflightError, setStartPreflightError] = useState<string | null>(null);

  const invalidateFinancialMetricsReviewWork = () => {
    activeSessionId.current = null;
    financialMetricsReviewGeneration.current += 1;
    financialMetricsUploadSessionId.current = null;
    financialMetricsUploadGeneration.current += 1;
    setFinancialMetricsReview(null);
    setFinancialMetricsReviewError(null);
    setIsLoadingFinancialMetricsReview(false);
    setIsSavingFinancialMetricsReview(false);
    setIsLoadingStructuredMetrics(false);
    setIsSavingStructuredMetrics(false);
    setIsCheckingStartPreflight(false);
  };

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
    if (activeSessionId.current !== sessionId) {
      return;
    }
    setIsLoadingStructuredMetrics(true);
    try {
      const payload = await api.loadStructuredFinancialMetrics(sessionId);
      if (activeSessionId.current === sessionId) {
        setStructuredMetrics(payload.context ?? null);
      }
    } finally {
      if (activeSessionId.current === sessionId) {
        setIsLoadingStructuredMetrics(false);
      }
    }
  };

  const refreshStartPreflight = async (sessionId: string) => {
    if (activeSessionId.current !== sessionId) {
      return;
    }
    setIsCheckingStartPreflight(true);
    setStartPreflightError(null);
    try {
      const preflight = await api.getStartPreflight(sessionId);
      if (activeSessionId.current === sessionId) {
        setStartPreflight(preflight);
      }
    } catch (error) {
      console.error(error);
      if (activeSessionId.current === sessionId) {
        setStartPreflight(null);
        setStartPreflightError(
          "No se pudo verificar la preparación de inicio. La validación previa del servidor se ejecutará al iniciar de todos modos."
        );
      }
    } finally {
      if (activeSessionId.current === sessionId) {
        setIsCheckingStartPreflight(false);
      }
    }
  };

  const loadFinancialMetricsReview = async (sessionId: string) => {
    if (!shouldLoadFinancialMetricsReview(
      sessionId,
      activeSessionId.current,
      financialMetricsUploadSessionId.current,
    )) {
      return null;
    }
    const requestGeneration = financialMetricsReviewGeneration.current + 1;
    financialMetricsReviewGeneration.current = requestGeneration;
    setIsLoadingFinancialMetricsReview(true);
    setFinancialMetricsReviewError(null);
    try {
      const draft = await api.getFinancialMetricsReview(sessionId);
      if (isCurrentSessionRequest(
        sessionId,
        requestGeneration,
        activeSessionId.current,
        financialMetricsReviewGeneration.current,
      )) {
        setFinancialMetricsReview(draft);
      }
      return draft;
    } catch (error) {
      console.error(error);
      if (isCurrentSessionRequest(
        sessionId,
        requestGeneration,
        activeSessionId.current,
        financialMetricsReviewGeneration.current,
      )) {
        setFinancialMetricsReview(null);
        setFinancialMetricsReviewError(
          error instanceof Error
            ? error.message
          : "No se pudo cargar el borrador de revisión de métricas financieras."
        );
      }
      return null;
    } finally {
      if (isCurrentSessionRequest(
        sessionId,
        requestGeneration,
        activeSessionId.current,
        financialMetricsReviewGeneration.current,
      )) {
        setIsLoadingFinancialMetricsReview(false);
      }
    }
  };

  const refreshAfterFinancialMetricsReview = async (sessionId: string) => {
    await loadStructuredFinancialMetrics(sessionId);
    if (activeSessionId.current !== sessionId) {
      return;
    }
    const updated = await api.loadSessionDetails(sessionId);
    if (activeSessionId.current !== sessionId) {
      return;
    }
    setSession(updated);
    await refreshStartPreflight(sessionId);
  };

  const createSession = async () => {
    const previousSessionId = activeSessionId.current;
    invalidateFinancialMetricsReviewWork();
    setIsCreating(true);
    setErrorMessage(null);
    try {
      const createdSession = await api.createSession();
      activeSessionId.current = createdSession.id;
      setSession(createdSession);
      setEvents([]);
      setStructuredMetrics(null);
      setFinancialMetricsReview(null);
      setMetricsSaveResult(null);
      setMetricsSaveError(null);
      setFinancialMetricsReviewError(null);
      setSelectedSessionId(createdSession.id);

      await loadSavedSessions();
      await refreshStartPreflight(createdSession.id);
    } catch (error) {
      console.error(error);
      setErrorMessage("No se pudo crear la sesión de análisis.");
    } finally {
      if (activeSessionId.current === null && previousSessionId) {
        activeSessionId.current = previousSessionId;
        void loadStructuredFinancialMetrics(previousSessionId);
        void loadFinancialMetricsReview(previousSessionId);
        void refreshStartPreflight(previousSessionId);
      }
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
                  ? "Se requieren métricas financieras estructuradas antes de iniciar este análisis. Adjunte métricas en formato JSON/CSV/PDF e intente nuevamente."
                  : conflictPayload.errors[0]?.message ?? "No se pudo iniciar la sesión de análisis."
              );
              await loadSessionEvents(session.id);
              return;
            }

            setErrorMessage(
              conflictPayload.error && typeof conflictPayload.error === "string"
                ? conflictPayload.error
                : "No se pudo iniciar la sesión de análisis."
            );
            return;
          } catch {
            setErrorMessage("No se pudo iniciar la sesión de análisis.");
            return;
          }
        }
        throw new Error("No se pudo iniciar la sesión de análisis.");
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
      setErrorMessage("No se pudo iniciar la sesión de análisis.");
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
          ? "Aprobado por el auditor humano."
          : "Rechazado por el auditor humano.";
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
      setErrorMessage(`No se pudo ${decision === "approve" ? "aprobar" : "rechazar"} la sesión de análisis.`);
    } finally {
      setIsSubmittingDecision(false);
    }
  };

  const loadExistingSession = async (sessionId?: string) => {
    const idToLoad = sessionId ?? selectedSessionId;
    if (!idToLoad) return;
    const previousSessionId = activeSessionId.current;
    invalidateFinancialMetricsReviewWork();
    setErrorMessage(null);
    try {
      const loadedSession = await api.loadSessionDetails(idToLoad);
      activeSessionId.current = loadedSession.id;
      setSession(loadedSession);
      await loadSessionEvents(loadedSession.id);
      await refreshStartPreflight(loadedSession.id);
    } catch (error) {
      console.error(error);
      setErrorMessage("No se pudo cargar la sesión de análisis.");
    } finally {
      if (activeSessionId.current === null && previousSessionId) {
        activeSessionId.current = previousSessionId;
        void loadStructuredFinancialMetrics(previousSessionId);
        void loadFinancialMetricsReview(previousSessionId);
        void refreshStartPreflight(previousSessionId);
      }
    }
  };

  const saveJsonMetrics = async (input: StructuredFinancialMetricsInput) => {
    if (!session) return;
    const saveSessionId = session.id;
    const isCurrentSave = () => activeSessionId.current === saveSessionId;
    setIsSavingStructuredMetrics(true);
    setMetricsSaveError(null);
    setMetricsSaveResult(null);
    try {
      const result = await api.saveJsonMetrics(saveSessionId, input);
      if (!isCurrentSave()) {
        return;
      }
      setMetricsSaveResult(result);
      if (result.isValid) {
        setFinancialMetricsReview(null);
        await loadStructuredFinancialMetrics(saveSessionId);
        if (!isCurrentSave()) {
          return;
        }
        const updated = await api.loadSessionDetails(saveSessionId);
        if (!isCurrentSave()) {
          return;
        }
        setSession(updated);
      }
      await refreshStartPreflight(saveSessionId);
    } catch (error) {
      console.error(error);
      if (isCurrentSave()) {
        setMetricsSaveError("No se pudieron guardar las métricas financieras estructuradas.");
      }
    } finally {
      if (isCurrentSave()) {
        setIsSavingStructuredMetrics(false);
      }
    }
  };

  const saveCsvMetrics = async (input: StructuredFinancialMetricsCsvInput) => {
    if (!session) return;
    const saveSessionId = session.id;
    const isCurrentSave = () => activeSessionId.current === saveSessionId;
    setIsSavingStructuredMetrics(true);
    setMetricsSaveError(null);
    setMetricsSaveResult(null);
    try {
      const result = await api.saveCsvMetrics(saveSessionId, input);
      if (!isCurrentSave()) {
        return;
      }
      setMetricsSaveResult(result);
      if (result.isValid) {
        setFinancialMetricsReview(null);
        await loadStructuredFinancialMetrics(saveSessionId);
        if (!isCurrentSave()) {
          return;
        }
        const updated = await api.loadSessionDetails(saveSessionId);
        if (!isCurrentSave()) {
          return;
        }
        setSession(updated);
      }
      await refreshStartPreflight(saveSessionId);
    } catch (error) {
      console.error(error);
      if (isCurrentSave()) {
        setMetricsSaveError("No se pudieron guardar las métricas financieras estructuradas.");
      }
    } finally {
      if (isCurrentSave()) {
        setIsSavingStructuredMetrics(false);
      }
    }
  };

  const uploadFinancialMetricsFile = async (
    file: File,
    metadata: StructuredFinancialMetricsFileMetadata
  ) => {
    if (!session) return;
    const uploadSessionId = session.id;
    const uploadGeneration = financialMetricsUploadGeneration.current + 1;
    financialMetricsUploadGeneration.current = uploadGeneration;
    const isCurrentUpload = () => isCurrentSessionRequest(
      uploadSessionId,
      uploadGeneration,
      activeSessionId.current,
      financialMetricsUploadGeneration.current,
    );
    financialMetricsReviewGeneration.current += 1;
    financialMetricsUploadSessionId.current = uploadSessionId;
    setIsLoadingFinancialMetricsReview(false);
    setIsSavingStructuredMetrics(true);
    setMetricsSaveError(null);
    setMetricsSaveResult(null);
    setFinancialMetricsReviewError(null);
    try {
      const result = await api.uploadFinancialMetricsFile(uploadSessionId, file, metadata);
      if (!isCurrentUpload()) {
        return;
      }
      setMetricsSaveResult(result);
      const outcome = result.outcome ?? (result.isValid ? "accepted" : "failed");

      if (outcome === "review_required") {
        setFinancialMetricsReview(result.reviewDraft ?? null);
      } else if (outcome === "accepted" && result.isValid) {
        setFinancialMetricsReview(null);
        await loadStructuredFinancialMetrics(uploadSessionId);
        if (!isCurrentUpload()) {
          return;
        }
        const updated = await api.loadSessionDetails(uploadSessionId);
        if (!isCurrentUpload()) {
          return;
        }
        setSession(updated);
      } else if (outcome === "failed") {
        setFinancialMetricsReview(null);
      }
      await refreshStartPreflight(uploadSessionId);
    } catch (error) {
      console.error(error);
      if (isCurrentUpload()) {
        setMetricsSaveError(
          error instanceof Error
            ? error.message
            : "La carga del archivo falló. Por favor, compruebe el formato del archivo e intente de nuevo."
        );
      }
    } finally {
      if (isCurrentUpload()) {
        financialMetricsUploadSessionId.current = null;
        setIsSavingStructuredMetrics(false);
      }
    }
  };

  const updateFinancialMetricsReview = async (
    draftId: string,
    request: UpdateFinancialMetricsExtractionDraftRequest
  ) => {
    if (!session) return null;
    const reviewSessionId = session.id;
    setIsSavingFinancialMetricsReview(true);
    setFinancialMetricsReviewError(null);
    try {
      const draft = await api.updateFinancialMetricsReview(reviewSessionId, draftId, request);
      if (activeSessionId.current === reviewSessionId) {
        setFinancialMetricsReview(draft);
        await refreshStartPreflight(reviewSessionId);
      }
      return draft;
    } catch (error) {
      console.error(error);
      if (activeSessionId.current === reviewSessionId) {
        setFinancialMetricsReviewError(
          error instanceof Error
            ? error.message
            : "No se pudieron guardar los cambios del borrador de revisión."
        );
      }
      return null;
    } finally {
      if (activeSessionId.current === reviewSessionId) {
        setIsSavingFinancialMetricsReview(false);
      }
    }
  };

  const confirmFinancialMetricsReview = async (draftId: string) => {
    if (!session) return;
    const reviewSessionId = session.id;
    setIsSavingFinancialMetricsReview(true);
    setFinancialMetricsReviewError(null);
    try {
      await api.confirmFinancialMetricsReview(reviewSessionId, draftId);
      if (activeSessionId.current === reviewSessionId) {
        setFinancialMetricsReview(null);
        setMetricsSaveResult(null);
        await refreshAfterFinancialMetricsReview(reviewSessionId);
      }
    } catch (error) {
      console.error(error);
      if (activeSessionId.current === reviewSessionId) {
        setFinancialMetricsReviewError(
          error instanceof Error
            ? error.message
            : "No se pudo confirmar el borrador de revisión."
        );
      }
    } finally {
      if (activeSessionId.current === reviewSessionId) {
        setIsSavingFinancialMetricsReview(false);
      }
    }
  };

  const discardFinancialMetricsReview = async (draftId: string) => {
    if (!session) return;
    const reviewSessionId = session.id;
    setIsSavingFinancialMetricsReview(true);
    setFinancialMetricsReviewError(null);
    try {
      await api.discardFinancialMetricsReview(reviewSessionId, draftId);
      if (activeSessionId.current === reviewSessionId) {
        setFinancialMetricsReview(null);
        setMetricsSaveResult(null);
        await refreshAfterFinancialMetricsReview(reviewSessionId);
      }
    } catch (error) {
      console.error(error);
      if (activeSessionId.current === reviewSessionId) {
        setFinancialMetricsReviewError(
          error instanceof Error
            ? error.message
            : "No se pudo descartar el borrador de revisión."
        );
      }
    } finally {
      if (activeSessionId.current === reviewSessionId) {
        setIsSavingFinancialMetricsReview(false);
      }
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
      activeSessionId.current = null;
      setStructuredMetrics(null);
      setFinancialMetricsReview(null);
      setFinancialMetricsReviewError(null);
      setIsLoadingFinancialMetricsReview(false);
      setIsSavingFinancialMetricsReview(false);
      setStartPreflight(null);
      setStartPreflightError(null);
      setIsCheckingStartPreflight(false);
      return;
    }

    activeSessionId.current = session.id;
    setStartPreflight(null);
    setStartPreflightError(null);

    loadStructuredFinancialMetrics(session.id).catch((error) => {
      console.error("Failed to load structured financial metrics:", error);
      if (activeSessionId.current === session.id) {
        setStructuredMetrics(null);
      }
    });

    loadFinancialMetricsReview(session.id).catch((error) => {
      console.error("Failed to load financial metrics review:", error);
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
    financialMetricsReview,
    isLoadingFinancialMetricsReview,
    isSavingFinancialMetricsReview,
    financialMetricsReviewError,
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
    loadFinancialMetricsReview,
    updateFinancialMetricsReview,
    confirmFinancialMetricsReview,
    discardFinancialMetricsReview,
    refreshStartPreflight,
  };
}
