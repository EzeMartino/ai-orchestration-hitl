import { useState } from "react";
import "./App.css";

// Types
import type { AnalysisContext } from "./types/domain.types";

// Components
import AuthPage from "./components/Auth/AuthPage";

// Hooks
import { useSignalRConnection } from "./hooks/useSignalRConnection";
import { useAnalysisSession } from "./hooks/useAnalysisSession";

// Components
import StatusBadge from "./components/StatusBadge";
import PlannerPanel from "./components/PlannerPanel";
import ToolPlanAuditPanel from "./components/ToolPlanAuditPanel";
import EvidencePanel from "./components/EvidencePanel";
import FinancialRiskEvidencePanel from "./components/FinancialRiskEvidencePanel";
import StructuredFinancialMetricsPanel from "./components/StructuredFinancialMetricsPanel";
import CompliancePanel from "./components/CompliancePanel";
import StartReadinessPanel from "./components/StartReadinessPanel";

function parseAnomalyContext(contextJson?: string): AnalysisContext | null {
  if (!contextJson) {
    return null;
  }

  try {
    return JSON.parse(contextJson) as AnalysisContext;
  } catch {
    return null;
  }
}

function getEventTone(type: string) {
  if (type.includes("tool_call_rejected")) {
    return "event-warning";
  }

  if (type.includes("tool_call_skipped")) {
    return "event-info";
  }

  if (type.includes("tool_call_executed")) {
    return "event-success";
  }

  if (
    type.includes("tool_plan_proposed") ||
    type.includes("tool_plan_validated") ||
    type.includes("structured_financial_metrics_attached")
  ) {
    return "event-info";
  }

  if (
    type.includes("planner_reasoning_fallback_used") ||
    type.includes("tool_execution_fallback_used") ||
    type.includes("analysis_start_blocked") ||
    type.includes("financial_metrics_fixture_fallback_used") ||
    type.includes("financial_metrics_required_missing")
  ) {
    return "event-warning";
  }

  if (
    type.includes("planner_reasoning_completed") ||
    type.includes("llm_reasoning")
  ) {
    return "event-info";
  }

  if (type.includes("anomaly")) {
    return "event-danger";
  }

  if (type.includes("human_approval")) {
    return "event-warning";
  }

  if (type.includes("rejected") || type.includes("failed")) {
    return "event-danger";
  }

  if (type.includes("completed") || type.includes("approved")) {
    return "event-success";
  }

  return "event-neutral";
}

function AuthenticatedApp({ token, onLogout }: { token: string; onLogout: () => void }) {
  const {
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
  } = useAnalysisSession();

  const connectionStatus = useSignalRConnection(addActivityEvent);

  const latestEvent = events[0];
  const analysisContext = parseAnomalyContext(session?.contextJson);
  const planner = analysisContext?.planner;
  const toolPlan = analysisContext?.toolPlan;
  const anomaly = analysisContext?.anomaly;
  const financialAnalysis = analysisContext?.financialAnalysis;
  const hasFinancialAnalysis = Boolean(financialAnalysis);
  const compliance = analysisContext?.compliance;
  const isStartBlockedByPreflight = startPreflight?.canStart === false;

  return (
    <main className="page">
      {/* Aurora background */}
      <div className="aurora-bg" aria-hidden="true">
        <div className="aurora-blob blob-1"></div>
        <div className="aurora-blob blob-2"></div>
        <div className="aurora-blob blob-3"></div>
      </div>
      <section className="shell">
        <header className="header">
          <div>
            <p className="eyebrow">Plataforma de Orquestación LLM</p>
            <h1>Sala de Control de Análisis Financiero</h1>
            <p className="subtitle">
              Feed de actividad en tiempo real para flujos de trabajo de agentes supervisados.
            </p>
          </div>

          <div className="headerRight">
            <div className="userProfileContainer">
              <div className="userAvatarBadge">
                {parseJwt(token)?.email?.charAt(0).toUpperCase() || "U"}
              </div>
              <div className="userProfileInfo">
                <span className="userProfileEmail">
                  {parseJwt(token)?.email || "Usuario"}
                </span>
                <button className="userLogoutBtn" onClick={onLogout} title="Cerrar sesión">
                  <svg
                    className="userLogoutIcon"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="2"
                    strokeLinecap="round"
                    strokeLinejoin="round"
                  >
                    <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" />
                    <polyline points="16 17 21 12 16 7" />
                    <line x1="21" y1="12" x2="9" y2="12" />
                  </svg>
                  <span>Cerrar sesión</span>
                </button>
              </div>
            </div>

            <div className={`connectionBadge ${connectionStatus.toLowerCase()}`}>
              Conexión en vivo (SignalR): <strong>{connectionStatus === "Connected" ? "Conectado" : connectionStatus === "Disconnected" ? "Desconectado" : connectionStatus === "Reconnecting" ? "Reconectando" : "Fallida"}</strong>
            </div>
          </div>
        </header>

        <section className="actions" aria-label="Session actions">
          <button onClick={createSession} disabled={isCreating}>
            {isCreating ? "Creando..." : "Crear Sesión de Análisis"}
          </button>

          <button
            onClick={startSession}
            disabled={!session || isStarting || isStartBlockedByPreflight}
            title={
              isStartBlockedByPreflight
                ? "Inicio bloqueado por validación previa"
                : undefined
            }
          >
            {isStarting
              ? "Iniciando..."
              : isStartBlockedByPreflight
                ? "Inicio bloqueado por validación previa"
                : "Iniciar Sesión"}
          </button>
        </section>

        <StartReadinessPanel
          sessionId={session?.id}
          preflight={startPreflight}
          isChecking={isCheckingStartPreflight}
          error={startPreflightError}
        />

        <section className="loadSessionPanel">
          <label>
            Revisar análisis anteriores
            <div className="loadSessionRow">
              <select
                value={selectedSessionId}
                onChange={(event) => setSelectedSessionId(event.target.value)}
              >
                {savedSessions.length === 0 ? (
                  <option value="">Aún no hay sesiones guardadas</option>
                ) : (
                  savedSessions.map((savedSession) => (
                    <option key={savedSession.id} value={savedSession.id}>
                      {new Date(savedSession.createdAt).toLocaleString()} ·{" "}
                      {savedSession.status} · {savedSession.id.slice(0, 8)}
                    </option>
                  ))
                )}
              </select>

              <button
                onClick={() => loadExistingSession()}
                disabled={!selectedSessionId}
              >
                Cargar Sesión
              </button>

              <button onClick={loadSavedSessions}>Actualizar</button>
            </div>
          </label>
        </section>

        {errorMessage && (
          <section className="startReadinessPanel startReadiness-danger">
            <div>
              <span>Error</span>
              <strong>{errorMessage}</strong>
            </div>
          </section>
        )}

        <section className="dashboardGrid">
          {session && (
            <section className="sessionCard">
              <div className="sessionHeader">
                <div>
                  <span className="label">Sesión actual</span>
                  <code>{session.id}</code>
                </div>

                <StatusBadge status={session.status} />
              </div>

              <div className="sessionGrid">
                <div>
                  <span>Agente actual</span>
                  <strong>{session.currentAgent ? (session.currentAgent === "PlannerAgent" ? "Agente Planificador" : session.currentAgent === "DataAgent" ? "Agente de Datos" : session.currentAgent === "LegalAgent" ? "Agente Legal" : session.currentAgent) : "Ninguno"}</strong>
                </div>

                <div>
                  <span>Creada</span>
                  <strong>
                    {session.createdAt
                      ? new Date(session.createdAt).toLocaleString()
                      : "-"}
                  </strong>
                </div>

                <div>
                  <span>Actualizada</span>
                  <strong>
                    {session.updatedAt
                      ? new Date(session.updatedAt).toLocaleString()
                      : "-"}
                  </strong>
                </div>
              </div>
            </section>
          )}

          <StructuredFinancialMetricsPanel
            sessionId={session?.id}
            metricsContext={structuredMetrics}
            isLoading={isLoadingStructuredMetrics}
            isSaving={isSavingStructuredMetrics}
            saveResult={metricsSaveResult}
            saveError={metricsSaveError}
            onSaveJson={saveJsonMetrics}
            onSaveCsv={saveCsvMetrics}
            onUploadFile={uploadFinancialMetricsFile}
          />

          <PlannerPanel planner={planner} />
          <ToolPlanAuditPanel toolPlan={toolPlan} />
          {!hasFinancialAnalysis && <EvidencePanel anomaly={anomaly} />}
          {hasFinancialAnalysis && (
            <FinancialRiskEvidencePanel financialAnalysis={financialAnalysis} />
          )}
          <CompliancePanel compliance={compliance} />

          {session?.status === "Completed" && (
            <div className="finalDecision finalDecisionSuccess">
              El auditor humano aprobó este flujo de trabajo. El análisis ha finalizado.
            </div>
          )}

          {session?.status === "Failed" && (
            <div className="finalDecision finalDecisionDanger">
              El auditor humano rechazó este flujo de trabajo. El análisis ha sido detenido.
            </div>
          )}

          {session?.status === "AwaitingHumanApproval" && (
            <section className="approvalPanel">
              <div>
                <p className="approvalEyebrow">Se requiere intervención humana</p>
                <h2>Anomalía de alta gravedad detectada</h2>
                <p>
                  El flujo de trabajo supervisado ha sido pausado. Un auditor humano debe
                  revisar la evidencia antes de que el sistema pueda continuar o
                  finalizar el análisis.
                </p>
              </div>

              <label className="reasonField">
                Comentario del auditor
                <textarea
                  value={decisionReason}
                  onChange={(event) => setDecisionReason(event.target.value)}
                  placeholder="Ejemplo: Anomalía por encima del umbral, rechazar para investigación manual."
                />
              </label>

              <div className="approvalActions">
                <button
                  className="approveButton"
                  onClick={() => submitHumanDecision("approve")}
                  disabled={isSubmittingDecision}
                >
                  Aprobar
                </button>

                <button
                  className="rejectButton"
                  onClick={() => submitHumanDecision("reject")}
                  disabled={isSubmittingDecision}
                >
                  Rechazar
                </button>
              </div>
            </section>
          )}

          <section className="activityPanel">
            <div className="panelHeader">
              <h2>Canal de Actividad</h2>
              <span>{events.length} eventos</span>
            </div>

            {latestEvent && (
              <div className="latestEventSummary">
                <span>Último evento</span>
                <strong>{latestEvent.agent}</strong>
                <p>{latestEvent.message}</p>
              </div>
            )}

            {events.length === 0 ? (
              <p className="emptyState">
                Sin actividad aún. Cree e inicie una sesión.
              </p>
            ) : (
              <div className="eventList">
                {events.map((event, index) => (
                  <article
                    className={`eventCard ${getEventTone(event.type)}`}
                    key={`${event.timestamp}-${index}`}
                  >
                    <div className="eventMeta">
                      <span>{event.agent}</span>
                      <time>{new Date(event.timestamp).toLocaleTimeString()}</time>
                    </div>

                    <h3>{event.type}</h3>
                    <p>{event.message}</p>
                  </article>
                ))}
              </div>
            )}
          </section>
        </section>
      </section>
    </main>
  );
}

function parseJwt(token: string) {
  try {
    const base64Url = token.split(".")[1];
    const base64 = base64Url.replace(/-/g, "+").replace(/_/g, "/");
    const jsonPayload = decodeURIComponent(
      atob(base64)
        .split("")
        .map((c) => "%" + ("00" + c.charCodeAt(0).toString(16)).slice(-2))
        .join("")
    );
    return JSON.parse(jsonPayload);
  } catch {
    return null;
  }
}

function App() {
  const [token, setToken] = useState<string | null>(localStorage.getItem("auth_token"));

  const handleLogout = () => {
    localStorage.removeItem("auth_token");
    setToken(null);
    window.location.reload();
  };

  if (!token) {
    return <AuthPage onLoginSuccess={(t) => setToken(t)} />;
  }

  return <AuthenticatedApp token={token} onLogout={handleLogout} />;
}

export default App;
