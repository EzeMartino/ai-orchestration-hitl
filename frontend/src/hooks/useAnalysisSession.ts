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
  ConfirmFinancialMetricsExtractionDraftRequest,
  FinancialReportSummary,
} from "../types/domain.types";
import * as api from "../services/api";
import {
  isCurrentSessionRequest,
  shouldLoadFinancialMetricsReview,
} from "../utils/financialMetricsReview";

function isSameActivityEvent(left: ActivityEvent, right: ActivityEvent) {
  return left.sessionId === right.sessionId &&
    left.type === right.type &&
    left.agent === right.agent &&
    left.message === right.message &&
    left.timestamp === right.timestamp;
}

export function useAnalysisSession() {
  const isMounted = useRef(true);
  const activeSessionId = useRef<string | null>(null);
  const sessionGeneration = useRef(0);
  const workflowGeneration = useRef(0);
  const sessionDetailsGeneration = useRef(0);
  const sessionEventsGeneration = useRef(0);
  const savedSessionsGeneration = useRef(0);
  const structuredMetricsGeneration = useRef(0);
  const startPreflightGeneration = useRef(0);
  const sessionStartGeneration = useRef(0);
  const decisionGeneration = useRef(0);
  const metricsSaveGeneration = useRef(0);
  const financialMetricsReviewSaveGeneration = useRef(0);
  const financialMetricsReviewGeneration = useRef(0);
  const financialMetricsUploadSessionId = useRef<string | null>(null);
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
  const [financialReportSummary, setFinancialReportSummary] =
    useState<FinancialReportSummary | null>(null);
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

  const isCurrentGeneration = (generation: number) =>
    isMounted.current && sessionGeneration.current === generation;

  const isCurrentSession = (sessionId: string, generation: number) =>
    isMounted.current && isCurrentSessionRequest(
      sessionId,
      generation,
      activeSessionId.current,
      sessionGeneration.current,
    );

  const beginSessionChange = (sessionId: string | null) => {
    const generation = sessionGeneration.current + 1;
    sessionGeneration.current = generation;
    workflowGeneration.current += 1;
    sessionDetailsGeneration.current += 1;
    activeSessionId.current = sessionId;
    sessionEventsGeneration.current += 1;
    structuredMetricsGeneration.current += 1;
    startPreflightGeneration.current += 1;
    sessionStartGeneration.current += 1;
    decisionGeneration.current += 1;
    metricsSaveGeneration.current += 1;
    financialMetricsReviewSaveGeneration.current += 1;
    financialMetricsReviewGeneration.current += 1;
    financialMetricsUploadSessionId.current = null;
    setSession(null);
    setEvents([]);
    setIsCreating(false);
    setIsStarting(false);
    setDecisionReason("");
    setIsSubmittingDecision(false);
    setErrorMessage(null);
    setStructuredMetrics(null);
    setFinancialReportSummary(null);
    setIsLoadingStructuredMetrics(false);
    setIsSavingStructuredMetrics(false);
    setMetricsSaveResult(null);
    setMetricsSaveError(null);
    setFinancialMetricsReview(null);
    setFinancialMetricsReviewError(null);
    setIsLoadingFinancialMetricsReview(false);
    setIsSavingFinancialMetricsReview(false);
    setStartPreflight(null);
    setStartPreflightError(null);
    setIsCheckingStartPreflight(false);
    return generation;
  };

  // Helper to add activity events from SignalR
  const addActivityEvent = useCallback((event: ActivityEvent) => {
    setEvents((currentEvents) => {
      if (event.sessionId !== activeSessionId.current) {
        return currentEvents;
      }

      const alreadyExists = currentEvents.some((currentEvent) =>
        isSameActivityEvent(currentEvent, event)
      );

      if (alreadyExists) {
        return currentEvents;
      }

      return [event, ...currentEvents];
    });
  }, []);

  const loadSessionEvents = async (
    sessionId: string,
    requestSessionGeneration = sessionGeneration.current,
  ) => {
    if (!isCurrentSession(sessionId, requestSessionGeneration)) {
      return;
    }
    const requestGeneration = sessionEventsGeneration.current + 1;
    sessionEventsGeneration.current = requestGeneration;
    try {
      const historicalEvents = await api.loadSessionEvents(sessionId);
      if (
        isCurrentSession(sessionId, requestSessionGeneration) &&
        sessionEventsGeneration.current === requestGeneration
      ) {
        const sessionEvents = historicalEvents.filter((event) => event.sessionId === sessionId);
        setEvents((currentEvents) => [
          ...currentEvents,
          ...sessionEvents.filter((historicalEvent) =>
            !currentEvents.some((currentEvent) =>
              isSameActivityEvent(currentEvent, historicalEvent)
            )
          ),
        ].sort((left, right) => Date.parse(right.timestamp) - Date.parse(left.timestamp)));
      }
    } catch (error) {
      console.error(error);
    }
  };

  const loadSavedSessions = async () => {
    const requestGeneration = savedSessionsGeneration.current + 1;
    savedSessionsGeneration.current = requestGeneration;
    try {
      const sessions = await api.loadSavedSessions();
      if (isMounted.current && savedSessionsGeneration.current === requestGeneration) {
        setSavedSessions(sessions);
        setSelectedSessionId((current) => current || sessions[0]?.id || "");
      }
    } catch (error) {
      console.error(error);
      if (isMounted.current && savedSessionsGeneration.current === requestGeneration) {
        throw error;
      }
    }
  };

  const loadStructuredFinancialMetrics = async (
    sessionId: string,
    requestSessionGeneration = sessionGeneration.current,
  ) => {
    if (!isCurrentSession(sessionId, requestSessionGeneration)) {
      return;
    }
    const requestGeneration = structuredMetricsGeneration.current + 1;
    structuredMetricsGeneration.current = requestGeneration;
    setIsLoadingStructuredMetrics(true);
    try {
      const payload = await api.loadStructuredFinancialMetrics(sessionId);
      if (
        isCurrentSession(sessionId, requestSessionGeneration) &&
        structuredMetricsGeneration.current === requestGeneration
      ) {
        setStructuredMetrics(payload.context ?? null);
        setFinancialReportSummary(payload.reportSummary ?? null);
      }
    } catch (error) {
      if (
        isCurrentSession(sessionId, requestSessionGeneration) &&
        structuredMetricsGeneration.current === requestGeneration
      ) {
        throw error;
      }
    } finally {
      if (
        isCurrentSession(sessionId, requestSessionGeneration) &&
        structuredMetricsGeneration.current === requestGeneration
      ) {
        setIsLoadingStructuredMetrics(false);
      }
    }
  };

  const loadCurrentSessionDetails = async (
    sessionId: string,
    requestSessionGeneration: number,
  ) => {
    if (!isCurrentSession(sessionId, requestSessionGeneration)) {
      return null;
    }
    const requestGeneration = sessionDetailsGeneration.current + 1;
    sessionDetailsGeneration.current = requestGeneration;
    try {
      const details = await api.loadSessionDetails(sessionId);
      if (
        isCurrentSession(sessionId, requestSessionGeneration) &&
        sessionDetailsGeneration.current === requestGeneration
      ) {
        return details;
      }
    } catch (error) {
      if (
        isCurrentSession(sessionId, requestSessionGeneration) &&
        sessionDetailsGeneration.current === requestGeneration
      ) {
        throw error;
      }
    }
    return null;
  };

  const refreshStartPreflight = async (
    sessionId: string,
    requestSessionGeneration = sessionGeneration.current,
  ) => {
    if (!isCurrentSession(sessionId, requestSessionGeneration)) {
      return;
    }
    const requestGeneration = startPreflightGeneration.current + 1;
    startPreflightGeneration.current = requestGeneration;
    setIsCheckingStartPreflight(true);
    setStartPreflightError(null);
    try {
      const preflight = await api.getStartPreflight(sessionId);
      if (
        isCurrentSession(sessionId, requestSessionGeneration) &&
        startPreflightGeneration.current === requestGeneration
      ) {
        setStartPreflight(preflight);
      }
    } catch (error) {
      console.error(error);
      if (
        isCurrentSession(sessionId, requestSessionGeneration) &&
        startPreflightGeneration.current === requestGeneration
      ) {
        setStartPreflight(null);
        setStartPreflightError(
          "No se pudo verificar la preparación de inicio. La validación previa del servidor se ejecutará al iniciar de todos modos."
        );
      }
    } finally {
      if (
        isCurrentSession(sessionId, requestSessionGeneration) &&
        startPreflightGeneration.current === requestGeneration
      ) {
        setIsCheckingStartPreflight(false);
      }
    }
  };

  const loadFinancialMetricsReview = async (
    sessionId: string,
    requestSessionGeneration = sessionGeneration.current,
  ) => {
    if (!shouldLoadFinancialMetricsReview(
      sessionId,
      activeSessionId.current,
      financialMetricsUploadSessionId.current,
    ) || !isCurrentSession(sessionId, requestSessionGeneration)) {
      return null;
    }
    const requestGeneration = financialMetricsReviewGeneration.current + 1;
    financialMetricsReviewGeneration.current = requestGeneration;
    setIsLoadingFinancialMetricsReview(true);
    setFinancialMetricsReviewError(null);
    try {
      const draft = await api.getFinancialMetricsReview(sessionId);
      if (isCurrentSession(sessionId, requestSessionGeneration) && isCurrentSessionRequest(
          sessionId,
          requestGeneration,
          activeSessionId.current,
          financialMetricsReviewGeneration.current,
        )) {
        setFinancialMetricsReview(draft);
        return draft;
      }
      return null;
    } catch (error) {
      console.error(error);
      if (isCurrentSession(sessionId, requestSessionGeneration) && isCurrentSessionRequest(
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
      if (isCurrentSession(sessionId, requestSessionGeneration) && isCurrentSessionRequest(
          sessionId,
          requestGeneration,
          activeSessionId.current,
          financialMetricsReviewGeneration.current,
        )) {
        setIsLoadingFinancialMetricsReview(false);
      }
    }
  };

  const refreshAfterFinancialMetricsReview = async (
    sessionId: string,
    requestSessionGeneration: number,
  ) => {
    await loadStructuredFinancialMetrics(sessionId, requestSessionGeneration);
    if (!isCurrentSession(sessionId, requestSessionGeneration)) {
      return;
    }
    const updated = await loadCurrentSessionDetails(sessionId, requestSessionGeneration);
    if (!updated) {
      return;
    }
    setSession(updated);
    await refreshStartPreflight(sessionId, requestSessionGeneration);
  };

  const createSession = async () => {
    const previousSession = session;
    const requestSessionGeneration = beginSessionChange(null);
    const requestWorkflowGeneration = workflowGeneration.current;
    const isCurrentCreate = () =>
      isCurrentGeneration(requestSessionGeneration) &&
      workflowGeneration.current === requestWorkflowGeneration;
    setIsCreating(true);
    try {
      const createdSession = await api.createSession();
      if (!isCurrentCreate()) {
        return;
      }
      activeSessionId.current = createdSession.id;
      setSession(createdSession);
      setSelectedSessionId(createdSession.id);

      await loadSavedSessions();
      if (isCurrentSession(createdSession.id, requestSessionGeneration)) {
        await refreshStartPreflight(createdSession.id, requestSessionGeneration);
      }
    } catch (error) {
      console.error(error);
      if (isCurrentCreate()) {
        if (previousSession) {
          beginSessionChange(previousSession.id);
          setSession(previousSession);
        }
        setErrorMessage("No se pudo crear la sesión de análisis.");
      }
    } finally {
      if (isCurrentCreate()) {
        setIsCreating(false);
      }
    }
  };

  const startSession = async () => {
    if (!session) return;
    const requestSessionId = session.id;
    const requestSessionGeneration = sessionGeneration.current;
    const requestGeneration = sessionStartGeneration.current + 1;
    sessionStartGeneration.current = requestGeneration;
    const requestWorkflowGeneration = workflowGeneration.current + 1;
    workflowGeneration.current = requestWorkflowGeneration;
    const isCurrentStartOperation = () =>
      isCurrentSession(requestSessionId, requestSessionGeneration) &&
      sessionStartGeneration.current === requestGeneration;
    const isCurrentStart = () =>
      isCurrentStartOperation() &&
      workflowGeneration.current === requestWorkflowGeneration;
    setIsStarting(true);
    setErrorMessage(null);
    try {
      const response = await api.startSession(requestSessionId);
      if (!isCurrentStart()) {
        return;
      }
      if (!response.ok) {
        if (response.status === 409) {
          try {
            const conflictPayload = await response.json();
            if (!isCurrentStart()) {
              return;
            }
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
              await loadSessionEvents(requestSessionId, requestSessionGeneration);
              return;
            }

            setErrorMessage(
              conflictPayload.error && typeof conflictPayload.error === "string"
                ? conflictPayload.error
                : "No se pudo iniciar la sesión de análisis."
            );
            return;
          } catch {
            if (isCurrentStart()) {
              setErrorMessage("No se pudo iniciar la sesión de análisis.");
            }
            return;
          }
        }
        throw new Error("No se pudo iniciar la sesión de análisis.");
      }

      const updatedSession = await response.json() as AnalysisSessionResponse;
      if (!isCurrentStart()) {
        return;
      }
      setSession((currentSession) => ({
        ...(currentSession?.id === requestSessionId ? currentSession : session),
        ...updatedSession,
      }));

      await loadSessionEvents(requestSessionId, requestSessionGeneration);
      if (isCurrentStart()) {
        await loadSavedSessions();
      }
      if (isCurrentStart()) {
        await refreshStartPreflight(requestSessionId, requestSessionGeneration);
      }
    } catch (error) {
      console.error(error);
      if (isCurrentStart()) {
        setErrorMessage("No se pudo iniciar la sesión de análisis.");
      }
    } finally {
      if (isCurrentStartOperation()) {
        setIsStarting(false);
      }
    }
  };

  const submitHumanDecision = async (decision: "approve" | "reject") => {
    if (!session) return;
    const requestSessionId = session.id;
    const requestSessionGeneration = sessionGeneration.current;
    const requestGeneration = decisionGeneration.current + 1;
    decisionGeneration.current = requestGeneration;
    const requestWorkflowGeneration = workflowGeneration.current + 1;
    workflowGeneration.current = requestWorkflowGeneration;
    const isCurrentDecisionOperation = () =>
      isCurrentSession(requestSessionId, requestSessionGeneration) &&
      decisionGeneration.current === requestGeneration;
    const isCurrentDecision = () =>
      isCurrentDecisionOperation() &&
      workflowGeneration.current === requestWorkflowGeneration;
    setIsSubmittingDecision(true);
    setErrorMessage(null);
    try {
      const fallbackReason =
        decision === "approve"
          ? "Aprobado por el auditor humano."
          : "Rechazado por el auditor humano.";
      const reason = decisionReason.trim().length > 0 ? decisionReason : fallbackReason;

      const updatedSession = await api.submitHumanDecision(requestSessionId, decision, reason);
      if (!isCurrentDecision()) {
        return;
      }
      setSession((currentSession) => ({
        ...(currentSession?.id === requestSessionId ? currentSession : session),
        ...updatedSession,
      }));

      await loadSessionEvents(requestSessionId, requestSessionGeneration);
      if (isCurrentDecision()) {
        await loadSavedSessions();
      }
      if (isCurrentDecision()) {
        setDecisionReason("");
      }
    } catch (error) {
      console.error(error);
      if (isCurrentDecision()) {
        setErrorMessage(`No se pudo ${decision === "approve" ? "aprobar" : "rechazar"} la sesión de análisis.`);
      }
    } finally {
      if (isCurrentDecisionOperation()) {
        setIsSubmittingDecision(false);
      }
    }
  };

  const loadExistingSession = async (sessionId?: string) => {
    const idToLoad = sessionId ?? selectedSessionId;
    if (!idToLoad) return;
    const previousSession = session;
    const requestSessionGeneration = beginSessionChange(idToLoad);
    const requestWorkflowGeneration = workflowGeneration.current;
    const isCurrentLoad = () =>
      isCurrentSession(idToLoad, requestSessionGeneration) &&
      workflowGeneration.current === requestWorkflowGeneration;
    try {
      const loadedSession = await loadCurrentSessionDetails(idToLoad, requestSessionGeneration);
      if (!loadedSession || !isCurrentLoad()) {
        return;
      }
      activeSessionId.current = loadedSession.id;
      setSession(loadedSession);
      await loadSessionEvents(loadedSession.id, requestSessionGeneration);
      if (isCurrentSession(loadedSession.id, requestSessionGeneration)) {
        await refreshStartPreflight(loadedSession.id, requestSessionGeneration);
      }
    } catch (error) {
      console.error(error);
      if (isCurrentLoad()) {
        if (previousSession) {
          beginSessionChange(previousSession.id);
          setSession(previousSession);
        }
        setErrorMessage("No se pudo cargar la sesión de análisis.");
      }
    }
  };

  const saveStructuredMetrics = async <TInput,>(
    input: TInput,
    save: (sessionId: string, input: TInput) => Promise<SaveFinancialMetricsResponse>,
  ) => {
    if (!session) return;
    const saveSessionId = session.id;
    const requestSessionGeneration = sessionGeneration.current;
    const requestGeneration = metricsSaveGeneration.current + 1;
    metricsSaveGeneration.current = requestGeneration;
    const isCurrentSave = () =>
      isCurrentSession(saveSessionId, requestSessionGeneration) &&
      metricsSaveGeneration.current === requestGeneration;
    setIsSavingStructuredMetrics(true);
    setMetricsSaveError(null);
    setMetricsSaveResult(null);
    try {
      const result = await save(saveSessionId, input);
      if (!isCurrentSave()) {
        return;
      }
      setMetricsSaveResult(result);
      if (result.isValid) {
        setFinancialMetricsReview(null);
        await loadStructuredFinancialMetrics(saveSessionId, requestSessionGeneration);
        if (!isCurrentSave()) {
          return;
        }
        const updated = await loadCurrentSessionDetails(saveSessionId, requestSessionGeneration);
        if (!updated || !isCurrentSave()) {
          return;
        }
        setSession(updated);
      }
      if (isCurrentSave()) {
        await refreshStartPreflight(saveSessionId, requestSessionGeneration);
      }
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

  const saveJsonMetrics = (input: StructuredFinancialMetricsInput) =>
    saveStructuredMetrics(input, api.saveJsonMetrics);

  const saveCsvMetrics = (input: StructuredFinancialMetricsCsvInput) =>
    saveStructuredMetrics(input, api.saveCsvMetrics);

  const uploadFinancialMetricsFile = async (
    file: File,
    metadata: StructuredFinancialMetricsFileMetadata
  ) => {
    if (!session) return;
    const uploadSessionId = session.id;
    const requestSessionGeneration = sessionGeneration.current;
    const requestGeneration = metricsSaveGeneration.current + 1;
    metricsSaveGeneration.current = requestGeneration;
    const isCurrentUpload = () =>
      isCurrentSession(uploadSessionId, requestSessionGeneration) &&
      metricsSaveGeneration.current === requestGeneration;
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
        await loadStructuredFinancialMetrics(uploadSessionId, requestSessionGeneration);
        if (!isCurrentUpload()) {
          return;
        }
        const updated = await loadCurrentSessionDetails(uploadSessionId, requestSessionGeneration);
        if (!updated || !isCurrentUpload()) {
          return;
        }
        setSession(updated);
      } else if (outcome === "failed") {
        setFinancialMetricsReview(null);
      }
      if (isCurrentUpload()) {
        await refreshStartPreflight(uploadSessionId, requestSessionGeneration);
      }
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
    const requestSessionGeneration = sessionGeneration.current;
    const requestGeneration = financialMetricsReviewSaveGeneration.current + 1;
    financialMetricsReviewSaveGeneration.current = requestGeneration;
    const isCurrentSave = () =>
      isCurrentSession(reviewSessionId, requestSessionGeneration) &&
      financialMetricsReviewSaveGeneration.current === requestGeneration;
    setIsSavingFinancialMetricsReview(true);
    setFinancialMetricsReviewError(null);
    try {
      const draft = await api.updateFinancialMetricsReview(reviewSessionId, draftId, request);
      if (isCurrentSave()) {
        setFinancialMetricsReview(draft);
        await refreshStartPreflight(reviewSessionId, requestSessionGeneration);
        return draft;
      }
      return null;
    } catch (error) {
      console.error(error);
      if (isCurrentSave()) {
        setFinancialMetricsReviewError(
          error instanceof Error
            ? error.message
            : "No se pudieron guardar los cambios del borrador de revisión."
        );
      }
      return null;
    } finally {
      if (isCurrentSave()) {
        setIsSavingFinancialMetricsReview(false);
      }
    }
  };

  const confirmFinancialMetricsReview = async (
    draftId: string,
    request: ConfirmFinancialMetricsExtractionDraftRequest,
  ) => {
    if (!session) return;
    const reviewSessionId = session.id;
    const requestSessionGeneration = sessionGeneration.current;
    const requestGeneration = financialMetricsReviewSaveGeneration.current + 1;
    financialMetricsReviewSaveGeneration.current = requestGeneration;
    const isCurrentSave = () =>
      isCurrentSession(reviewSessionId, requestSessionGeneration) &&
      financialMetricsReviewSaveGeneration.current === requestGeneration;
    setIsSavingFinancialMetricsReview(true);
    setFinancialMetricsReviewError(null);
    try {
      await api.confirmFinancialMetricsReview(reviewSessionId, draftId, request);
      if (isCurrentSave()) {
        setFinancialMetricsReview(null);
        setMetricsSaveResult(null);
        await refreshAfterFinancialMetricsReview(reviewSessionId, requestSessionGeneration);
      }
    } catch (error) {
      console.error(error);
      if (isCurrentSave()) {
        setFinancialMetricsReviewError(
          error instanceof Error
            ? error.message
            : "No se pudo confirmar el borrador de revisión."
        );
      }
    } finally {
      if (isCurrentSave()) {
        setIsSavingFinancialMetricsReview(false);
      }
    }
  };

  const discardFinancialMetricsReview = async (draftId: string) => {
    if (!session) return;
    const reviewSessionId = session.id;
    const requestSessionGeneration = sessionGeneration.current;
    const requestGeneration = financialMetricsReviewSaveGeneration.current + 1;
    financialMetricsReviewSaveGeneration.current = requestGeneration;
    const isCurrentSave = () =>
      isCurrentSession(reviewSessionId, requestSessionGeneration) &&
      financialMetricsReviewSaveGeneration.current === requestGeneration;
    setIsSavingFinancialMetricsReview(true);
    setFinancialMetricsReviewError(null);
    try {
      await api.discardFinancialMetricsReview(reviewSessionId, draftId);
      if (isCurrentSave()) {
        setFinancialMetricsReview(null);
        setMetricsSaveResult(null);
        await refreshAfterFinancialMetricsReview(reviewSessionId, requestSessionGeneration);
      }
    } catch (error) {
      console.error(error);
      if (isCurrentSave()) {
        setFinancialMetricsReviewError(
          error instanceof Error
            ? error.message
            : "No se pudo descartar el borrador de revisión."
        );
      }
    } finally {
      if (isCurrentSave()) {
        setIsSavingFinancialMetricsReview(false);
      }
    }
  };

  useEffect(() => {
    isMounted.current = true;
    return () => {
      isMounted.current = false;
      sessionGeneration.current += 1;
    };
  }, []);

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
    const requestSessionGeneration = sessionGeneration.current;
    setStartPreflight(null);
    setStartPreflightError(null);

    loadSessionEvents(session.id, requestSessionGeneration).catch((error) => {
      console.error("Failed to load session events:", error);
    });

    loadStructuredFinancialMetrics(session.id, requestSessionGeneration).catch((error) => {
      console.error("Failed to load structured financial metrics:", error);
      if (isCurrentSession(session.id, requestSessionGeneration)) {
        setStructuredMetrics(null);
        setFinancialReportSummary(null);
      }
    });

    loadFinancialMetricsReview(session.id, requestSessionGeneration).catch((error) => {
      console.error("Failed to load financial metrics review:", error);
    });

    refreshStartPreflight(session.id, requestSessionGeneration).catch((error) => {
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
    financialReportSummary,
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
