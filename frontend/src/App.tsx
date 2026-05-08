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
};

type AnomalyContext = {
  summary?: string;
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

const apiBaseUrl = "https://localhost:7020";

function parseAnomalyContext(contextJson?: string): AnomalyContext | null {
  if (!contextJson) {
    return null;
  }

  try {
    return JSON.parse(contextJson) as AnomalyContext;
  } catch {
    return null;
  }
}

function StatusBadge({ status }: { status: string }) {
  return <span className={`statusBadge status-${status}`}>{status}</span>;
}

function EvidencePanel({ anomaly }: { anomaly: AnomalyContext["anomaly"] }) {
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
    </section>
  );
}

function getEventTone(type: string) {
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
  const anomalyContext = parseAnomalyContext(session?.contextJson);
  const anomaly = anomalyContext?.anomaly;
  const compliance = anomalyContext?.compliance;

  return (
    <main className="page">
      <section className="shell">
        <header className="header">
          <div>
            <p className="eyebrow">AI Orchestration Platform</p>
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
                  The autonomous workflow has been paused. A human auditor must
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
