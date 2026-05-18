import { useEffect, useState } from "react";
import * as signalR from "@microsoft/signalr";
import "./App.css";

type ActivityEvent = {
  sessionId: string;
  type: string;
  agent: string;
  message: string;
  timestamp: string;
};

type AnalysisSessionResponse = {
  id: string;
  status: string;
  createdAt?: string;
  updatedAt?: string;
  currentAgent?: string | null;
  contextJson?: string;
};

type AnalysisSessionSummary = {
  id: string;
  status: string;
  currentAgent?: string | null;
  createdAt: string;
  updatedAt: string;
  completedAt?: string | null;
};

type AnomalyEvidenceItem = {
  metric: string;
  value: number;
  threshold: number;
  interpretation: string;
};

type ComplianceEvidenceItem = {
  regulation: string;
  section: string;
  finding: string;
  source: string;
};

type ComplianceContext = {
  riskDetected: boolean;
  riskLevel: string;
  engine?: string;
  summary: string;
  evidence: ComplianceEvidenceItem[];
  warnings?: string[];
};

type PlannerContext = {
  engine?: string;
  summary: string;
  recommendedActions: string[];
  riskFactors: string[];
  limitations: string[];
  usedLlm?: boolean;
  usedFallback?: boolean;
  provider?: string | null;
  model?: string | null;
  failureReason?: string | null;
};

type ToolCallArguments = Record<string, string>;

type ProposedToolCallContext = {
  toolName: string;
  arguments: ToolCallArguments;
  reason: string;
};

type ApprovedToolCallContext = {
  toolName: string;
  arguments: ToolCallArguments;
  reason: string;
};

type RejectedToolCallContext = {
  toolName: string;
  reason: string;
};

type ExecutedToolCallContext = {
  toolName: string;
  status?: string;
  succeeded: boolean;
  summary: string;
  engine: string;
  error?: string | null;
};

type ToolPlanContext = {
  proposedCalls: ProposedToolCallContext[];
  approvedCalls: ApprovedToolCallContext[];
  rejectedCalls: RejectedToolCallContext[];
  executedCalls: ExecutedToolCallContext[];
};

type AnalysisContext = {
  summary?: string;
  planner?: PlannerContext;
  toolPlan?: ToolPlanContext;
  anomaly?: {
    detected: boolean;
    severity: string;
    engine?: string;
    category: string;
    summary: string;
    evidence: AnomalyEvidenceItem[];
    recommendation: string;
  };
  compliance?: ComplianceContext;
};

const apiBaseUrl = import.meta.env.VITE_API_URL ?? "http://localhost:5148";

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

function StatusBadge({ status }: { status: string }) {
  return <span className={`statusBadge status-${status}`}>{status}</span>;
}

function PlannerPanel({ planner }: { planner?: PlannerContext }) {
  if (!planner) {
    return null;
  }

  const llmStatus = planner.usedLlm
    ? "LLM reasoning used"
    : planner.usedFallback
      ? "Deterministic fallback"
      : "Reasoning metadata unavailable";

  return (
    <section className="plannerPanel">
      <div className="plannerHeader">
        <div>
          <p className="plannerEyebrow">Planner review</p>
          <h2>Controlled reasoning summary</h2>
          <p>{planner.summary}</p>

          {planner.engine && (
            <div className="engineBadge">
              Planner engine: <strong>{planner.engine}</strong>
            </div>
          )}
        </div>
      </div>

      <div className="plannerMetaGrid">
        <div>
          <span>LLM status</span>
          <strong>{llmStatus}</strong>
        </div>

        <div>
          <span>Provider</span>
          <strong>{planner.provider ?? "None"}</strong>
        </div>

        <div>
          <span>Model</span>
          <strong>{planner.model ?? "None"}</strong>
        </div>
      </div>

      {planner.failureReason && (
        <div className="plannerFallbackWarning">
          <strong>LLM fallback used</strong>
          <p>{planner.failureReason}</p>
        </div>
      )}

      <div className="plannerGrid">
        <PlannerList
          title="Recommended actions"
          items={planner.recommendedActions}
        />
        <PlannerList title="Risk factors" items={planner.riskFactors} />
        <PlannerList title="Limitations" items={planner.limitations} />
      </div>
    </section>
  );
}

function PlannerList({ title, items }: { title: string; items: string[] }) {
  if (items.length === 0) {
    return null;
  }

  return (
    <div className="plannerList">
      <strong>{title}</strong>
      <ul>
        {items.map((item) => (
          <li key={item}>{item}</li>
        ))}
      </ul>
    </div>
  );
}

function ToolPlanAuditPanel({ toolPlan }: { toolPlan?: ToolPlanContext }) {
  const hasToolPlan =
    toolPlan &&
    (toolPlan.proposedCalls.length > 0 ||
      toolPlan.approvedCalls.length > 0 ||
      toolPlan.rejectedCalls.length > 0 ||
      toolPlan.executedCalls.length > 0);

  if (!hasToolPlan) {
    return null;
  }

  return (
    <section className="toolPlanPanel">
      <div className="toolPlanHeader">
        <div>
          <p className="toolPlanEyebrow">Tool plan audit</p>
          <h2>Controlled tool calling trail</h2>
          <p>Proposed, validated, rejected, and execution-policy decisions.</p>
        </div>
      </div>

      <div className="toolPlanGrid">
        <ToolCallGroup
          title="Proposed"
          tone="neutral"
          calls={toolPlan.proposedCalls.map((call) => ({
            toolName: call.toolName,
            detail: call.reason,
          }))}
        />
        <ToolCallGroup
          title="Approved"
          tone="success"
          calls={toolPlan.approvedCalls.map((call) => ({
            toolName: call.toolName,
            detail: call.reason,
          }))}
        />
        <ToolCallGroup
          title="Rejected"
          tone="warning"
          calls={toolPlan.rejectedCalls.map((call) => ({
            toolName: call.toolName,
            detail: call.reason,
          }))}
        />
        <ToolCallGroup
          title="Execution audit"
          tone="neutral"
          calls={toolPlan.executedCalls.map((call) => ({
            toolName: call.toolName,
            detail: call.error ?? call.summary,
            meta: `${call.status ?? (call.succeeded ? "Executed" : "Failed")} - ${call.engine}`,
            status: call.status ?? (call.succeeded ? "Executed" : "Failed"),
            statusTone: getToolExecutionTone(call.status, call.succeeded),
          }))}
        />
      </div>
    </section>
  );
}

function ToolCallGroup({
  title,
  tone,
  calls,
}: {
  title: string;
  tone: "neutral" | "success" | "warning" | "danger";
  calls: {
    toolName: string;
    detail: string;
    meta?: string;
    status?: string;
    statusTone?: "neutral" | "success" | "warning" | "danger";
  }[];
}) {
  if (calls.length === 0) {
    return null;
  }

  return (
    <div className={`toolCallGroup toolCallGroup-${tone}`}>
      <strong>{title}</strong>

      <div className="toolCallList">
        {calls.map((call, index) => (
          <article className="toolCallCard" key={`${call.toolName}-${index}`}>
            <span>{call.toolName}</span>
            {call.status && (
              <em className={`toolCallStatus toolCallStatus-${call.statusTone ?? "neutral"}`}>
                {call.status}
              </em>
            )}
            {call.meta && <small>{call.meta}</small>}
            <p>{call.detail}</p>
          </article>
        ))}
      </div>
    </div>
  );
}

function getToolExecutionTone(
  status?: string,
  succeeded?: boolean
): "neutral" | "success" | "warning" | "danger" {
  if (status === "Failed" || succeeded === false) {
    return "danger";
  }

  if (status === "Executed") {
    return "success";
  }

  if (status === "SkippedDisabled") {
    return "warning";
  }

  return "neutral";
}

function EvidencePanel({ anomaly }: { anomaly: AnalysisContext["anomaly"] }) {
  if (!anomaly) {
    return null;
  }

  return (
    <section className="evidencePanel">
      <div className="evidenceHeader">
        <div>
          <p className="evidenceEyebrow">Risk evidence</p>
          <h2>{anomaly.category}</h2>
          <p>{anomaly.summary}</p>

          {anomaly.engine && (
            <div className="engineBadge">
              Analysis engine: <strong>{anomaly.engine}</strong>
            </div>
          )}
        </div>

        <span className={`severityBadge severity-${anomaly.severity}`}>
          {anomaly.severity}
        </span>
      </div>

      <div className="evidenceGrid">
        {anomaly.evidence.map((item) => (
          <article className="metricCard" key={item.metric}>
            <span className="metricName">{item.metric}</span>

            <div className="metricValues">
              <strong>{item.value}</strong>
              <span>Threshold: {item.threshold}</span>
            </div>

            <p>{item.interpretation}</p>
          </article>
        ))}
      </div>

      <div className="recommendationBox">
        <strong>Recommendation</strong>
        <p>{anomaly.recommendation}</p>
      </div>
    </section>
  );
}

function CompliancePanel({
  compliance,
}: {
  compliance?: ComplianceContext;
}) {
  if (!compliance) {
    return null;
  }

  const reviewNotes = [
    "This is regulatory retrieval evidence, not legal advice.",
    ...(compliance.warnings ?? []),
  ];

  return (
    <section className="compliancePanel">
      <div className="complianceHeader">
        <div>
          <p className="complianceEyebrow">Compliance review</p>
          <h2>LegalAgent assessment</h2>
          <p>{compliance.summary}</p>

          {compliance.engine && (
            <div className="engineBadge">
              Compliance engine: <strong>{compliance.engine}</strong>
            </div>
          )}
        </div>

        <span className={`riskBadge risk-${compliance.riskLevel}`}>
          {compliance.riskLevel}
        </span>
      </div>

      <div className="complianceEvidenceList">
        {compliance.evidence.map((item, index) => (
          <article
            className="complianceEvidenceCard"
            key={`${item.regulation}-${item.section}-${index}`}
          >
            <div className="complianceEvidenceTop">
              <strong>{item.regulation}</strong>
              <span>{item.section}</span>
            </div>

            <p>{item.finding}</p>

            <div className="sourceLine">
              Source: <span>{item.source}</span>
            </div>
          </article>
        ))}
      </div>

      <div className="legalWarnings">
        <strong>Review notes</strong>
        <ul>
          {reviewNotes.map((warning) => (
            <li key={warning}>{warning}</li>
          ))}
        </ul>
      </div>
    </section>
  );
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
    type.includes("tool_plan_validated")
  ) {
    return "event-info";
  }

  if (
    type.includes("planner_reasoning_fallback_used") ||
    type.includes("tool_execution_fallback_used")
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
  const [connectionStatus, setConnectionStatus] = useState("Disconnected");
  const [session, setSession] = useState<AnalysisSessionResponse | null>(null);
  const [events, setEvents] = useState<ActivityEvent[]>([]);
  const [isCreating, setIsCreating] = useState(false);
  const [isStarting, setIsStarting] = useState(false);
  const [decisionReason, setDecisionReason] = useState("");
  const [isSubmittingDecision, setIsSubmittingDecision] = useState(false);
  const [savedSessions, setSavedSessions] = useState<AnalysisSessionSummary[]>(
    []
  );
  const [selectedSessionId, setSelectedSessionId] = useState("");
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  useEffect(() => {
    let isDisposed = false;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${apiBaseUrl}/hubs/activity`)
      .withAutomaticReconnect()
      .build();

    const handleActivityEvent = (event: ActivityEvent) => {
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
    };

    connection.on("activityEventReceived", handleActivityEvent);

    connection.onreconnecting(() => {
      if (!isDisposed) {
        setConnectionStatus("Reconnecting");
      }
    });

    connection.onreconnected(() => {
      if (!isDisposed) {
        setConnectionStatus("Connected");
      }
    });

    connection.onclose(() => {
      if (!isDisposed) {
        setConnectionStatus("Disconnected");
      }
    });

    connection
      .start()
      .then(() => {
        if (!isDisposed) {
          setConnectionStatus("Connected");
        }
      })
      .catch((error) => {
        if (isDisposed) {
          return;
        }

        console.error("SignalR connection failed:", error);
        setConnectionStatus("Failed");
      });

    return () => {
      isDisposed = true;
      connection.off("activityEventReceived", handleActivityEvent);
      void connection.stop();
    };
  }, []);

  useEffect(() => {
    loadSavedSessions().catch((error) => {
      console.error("Failed to load saved sessions:", error);
    });
  }, []);

  async function loadSessionEvents(sessionId: string) {
    const response = await fetch(
      `${apiBaseUrl}/api/analysis-sessions/${sessionId}/events`
    );

    if (!response.ok) {
      throw new Error("Failed to load session events.");
    }

    const historicalEvents = (await response.json()) as ActivityEvent[];

    setEvents(historicalEvents);
  }

  async function loadSavedSessions() {
    const response = await fetch(`${apiBaseUrl}/api/analysis-sessions`);

    if (!response.ok) {
      throw new Error("Failed to load saved analysis sessions.");
    }

    const sessions = (await response.json()) as AnalysisSessionSummary[];

    setSavedSessions(sessions);

    if (sessions.length > 0 && !selectedSessionId) {
      setSelectedSessionId(sessions[0].id);
    }
  }

  async function createSession() {
    setIsCreating(true);
    setErrorMessage(null);

    try {
      const response = await fetch(`${apiBaseUrl}/api/analysis-sessions`, {
        method: "POST",
      });

      if (!response.ok) {
        throw new Error("Failed to create analysis session.");
      }

      const createdSession =
        (await response.json()) as AnalysisSessionResponse;

      setSession(createdSession);
      setEvents([]);
      setSelectedSessionId(createdSession.id);

      await loadSavedSessions();
    } catch (error) {
      console.error(error);
      setErrorMessage("Could not create the analysis session.");
    } finally {
      setIsCreating(false);
    }
  }

  async function startSession() {
    if (!session) {
      return;
    }

    setIsStarting(true);
    setErrorMessage(null);

    try {
      const response = await fetch(
        `${apiBaseUrl}/api/analysis-sessions/${session.id}/start`,
        {
          method: "POST",
        }
      );

      if (!response.ok) {
        throw new Error("Failed to start analysis session.");
      }

      const updatedSession =
        (await response.json()) as AnalysisSessionResponse;

      setSession((currentSession) => ({
        ...currentSession,
        ...updatedSession,
      }));

      await loadSessionEvents(updatedSession.id);

      await loadSavedSessions();
    } catch (error) {
      console.error(error);
      setErrorMessage("Could not start the analysis session.");
    } finally {
      setIsStarting(false);
    }
  }

  async function submitHumanDecision(decision: "approve" | "reject") {
    if (!session) {
      return;
    }

    setIsSubmittingDecision(true);
    setErrorMessage(null);

    try {
      const response = await fetch(
        `${apiBaseUrl}/api/analysis-sessions/${session.id}/${decision}`,
        {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
          },
          body: JSON.stringify({
            reason:
              decisionReason.trim().length > 0
                ? decisionReason
                : decision === "approve"
                  ? "Approved by human auditor."
                  : "Rejected by human auditor.",
          }),
        }
      );

      if (!response.ok) {
        throw new Error(`Failed to ${decision} analysis session.`);
      }

      const updatedSession =
        (await response.json()) as AnalysisSessionResponse;

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
  }

  async function loadExistingSession(sessionId?: string) {
    const idToLoad = sessionId ?? selectedSessionId;

    if (!idToLoad) {
      return;
    }

    setErrorMessage(null);

    try {
      const sessionResponse = await fetch(
        `${apiBaseUrl}/api/analysis-sessions/${idToLoad}`
      );

      if (!sessionResponse.ok) {
        throw new Error("Failed to load analysis session.");
      }

      const loadedSession =
        (await sessionResponse.json()) as AnalysisSessionResponse;

      setSession(loadedSession);

      await loadSessionEvents(loadedSession.id);
    } catch (error) {
      console.error(error);
      setErrorMessage("Could not load the analysis session.");
    }
  }

  const latestEvent = events[0];
  const analysisContext = parseAnomalyContext(session?.contextJson);
  const planner = analysisContext?.planner;
  const toolPlan = analysisContext?.toolPlan;
  const anomaly = analysisContext?.anomaly;
  const compliance = analysisContext?.compliance;

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

          <button onClick={startSession} disabled={!session || isStarting}>
            {isStarting ? "Starting..." : "Start Session"}
          </button>
        </section>

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

          <PlannerPanel planner={planner} />
          <ToolPlanAuditPanel toolPlan={toolPlan} />
          <EvidencePanel anomaly={anomaly} />
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
