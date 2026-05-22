import "./App.css";

// Types
import type { AnalysisContext } from "./types/domain.types";

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

function App() {
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
      <section className="shell">
        <header className="header">
          <div>
            <p className="eyebrow">LLM-Ready Orchestration Platform</p>
            <h1>Financial Analysis Control Room</h1>
            <p className="subtitle">
              Real-time activity feed for supervised agent workflows.
            </p>
          </div>

          <div className={`connectionBadge ${connectionStatus.toLowerCase()}`}>
            SignalR: <strong>{connectionStatus}</strong>
          </div>
        </header>

        <section className="actions" aria-label="Session actions">
          <button onClick={createSession} disabled={isCreating}>
            {isCreating ? "Creating..." : "Create Analysis Session"}
          </button>

          <button
            onClick={startSession}
            disabled={!session || isStarting || isStartBlockedByPreflight}
            title={
              isStartBlockedByPreflight
                ? "Start blocked by preflight"
                : undefined
            }
          >
            {isStarting
              ? "Starting..."
              : isStartBlockedByPreflight
                ? "Start blocked by preflight"
                : "Start Session"}
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
            Review previous analysis
            <div className="loadSessionRow">
              <select
                value={selectedSessionId}
                onChange={(event) => setSelectedSessionId(event.target.value)}
              >
                {savedSessions.length === 0 ? (
                  <option value="">No saved sessions yet</option>
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
                Load Session
              </button>

              <button onClick={loadSavedSessions}>Refresh</button>
            </div>
          </label>
        </section>

        {errorMessage && <p className="errorMessage">{errorMessage}</p>}

        <section className="dashboardGrid">
          {session && (
            <section className="sessionCard">
              <div className="sessionHeader">
                <div>
                  <span className="label">Current session</span>
                  <code>{session.id}</code>
                </div>

                <StatusBadge status={session.status} />
              </div>

              <div className="sessionGrid">
                <div>
                  <span>Current agent</span>
                  <strong>{session.currentAgent ?? "None"}</strong>
                </div>

                <div>
                  <span>Created</span>
                  <strong>
                    {session.createdAt
                      ? new Date(session.createdAt).toLocaleString()
                      : "-"}
                  </strong>
                </div>

                <div>
                  <span>Updated</span>
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
              Human auditor approved this workflow. The analysis was completed.
            </div>
          )}

          {session?.status === "Failed" && (
            <div className="finalDecision finalDecisionDanger">
              Human auditor rejected this workflow. The analysis was stopped.
            </div>
          )}

          {session?.status === "AwaitingHumanApproval" && (
            <section className="approvalPanel">
              <div>
                <p className="approvalEyebrow">Human intervention required</p>
                <h2>High-severity anomaly detected</h2>
                <p>
                  The supervised workflow has been paused. A human auditor must
                  review the evidence before the system can continue or
                  terminate the analysis.
                </p>
              </div>

              <label className="reasonField">
                Auditor reason
                <textarea
                  value={decisionReason}
                  onChange={(event) => setDecisionReason(event.target.value)}
                  placeholder="Example: Anomaly above threshold, reject for manual investigation."
                />
              </label>

              <div className="approvalActions">
                <button
                  className="approveButton"
                  onClick={() => submitHumanDecision("approve")}
                  disabled={isSubmittingDecision}
                >
                  Approve
                </button>

                <button
                  className="rejectButton"
                  onClick={() => submitHumanDecision("reject")}
                  disabled={isSubmittingDecision}
                >
                  Reject
                </button>
              </div>
            </section>
          )}

          <section className="activityPanel">
            <div className="panelHeader">
              <h2>Activity Feed</h2>
              <span>{events.length} events</span>
            </div>

            {latestEvent && (
              <div className="latestEventSummary">
                <span>Latest event</span>
                <strong>{latestEvent.agent}</strong>
                <p>{latestEvent.message}</p>
              </div>
            )}

            {events.length === 0 ? (
              <p className="emptyState">
                No activity yet. Create and start a session.
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

export default App;
