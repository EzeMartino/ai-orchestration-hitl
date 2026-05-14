# Financial Analysis Control Room

Human-in-the-loop orchestration platform for supervised financial anomaly review, combining deterministic workflow control, optional LLM-assisted planner reasoning, Python-based anomaly detection, MCP regulatory retrieval, real-time telemetry, and human approval gates.

This repository is best described as a **controlled LLM-assisted human-in-the-loop orchestration platform**. LLM reasoning is advisory, workflow control remains deterministic, and human approval remains mandatory.

## Current AI Status

This project now supports **optional controlled LLM-assisted planner reasoning**.

The LLM is used only to generate structured reasoning summaries for the `PlannerAgent`. It does not control workflow transitions, approve or reject sessions, execute tools autonomously, or make legal/financial decisions.

Current implementation:

- `PlannerAgent` coordinates the workflow in .NET.
- `PlannerAgent` can optionally use Semantic Kernel + OpenAI for advisory reasoning summaries.
- If LLM reasoning is disabled or fails, the system falls back to deterministic planner reasoning.
- `DataAgent` runs Python-based anomaly detection through Semantic Kernel + CSnakes.
- `LegalAgent` retrieves cited CNV regulatory evidence through Semantic Kernel + MCP.
- The workflow pauses for human approval when risk is detected.
- All activity is streamed in real time and persisted for audit review.

The architecture is intentionally designed so LLM-backed reasoning can assist the workflow without bypassing the state machine, HITL controls, MCP evidence retrieval, Python analytics, or audit trail.

## Controlled LLM Planner Reasoning

The `PlannerAgent` can optionally use an LLM through Semantic Kernel to generate structured reasoning summaries.

The LLM output is advisory only.

The LLM cannot:

- approve an analysis session,
- reject an analysis session,
- complete a workflow,
- fail a workflow,
- bypass human approval,
- directly execute tools,
- override the state machine.

If LLM reasoning is disabled or fails, the system uses deterministic planner reasoning. The state machine and HITL approval gates remain mandatory.

Planner reasoning metadata is persisted in `ContextJson`, including:

- engine,
- summary,
- recommended actions,
- risk factors,
- limitations,
- whether LLM reasoning was used,
- whether deterministic fallback was used,
- provider,
- model,
- safe fallback reason when applicable.

No API keys, stack traces, or secrets are persisted.

## What This Project Is

This is a human-supervised orchestration platform for financial analysis workflows.

It demonstrates how to combine:

- deterministic workflow orchestration,
- specialized agents,
- Python analytics,
- regulatory retrieval via MCP,
- human approval gates,
- real-time activity streaming,
- persisted audit history.

## What This Project Is Not Yet

This project does **not** currently include:

- autonomous LLM tool-calling,
- LLM-controlled workflow transitions,
- autonomous legal interpretation,
- natural language report understanding,
- automatic financial decision-making,
- production-grade regulatory advice.

The LegalAgent retrieves regulatory evidence. It does not provide legal conclusions.
The DataAgent detects statistical anomalies. It does not make operational decisions.
The human auditor remains responsible for approval or rejection.

## Architecture Overview

```text
React Dashboard
  | HTTP + SignalR
  v
ASP.NET Core API
  |
  |-- AnalysisSession State Machine
  |-- Human Approval Endpoints
  |-- SignalR ActivityHub
  |-- ActivityEvents Persistence
  |
  |-- PlannerAgent
        |
        |-- Planner Reasoning
        |     |-- Deterministic fallback
        |     |-- Semantic Kernel + OpenAI optional
        |
        |-- DataAgent
        |     |-- Semantic Kernel
        |     |-- PythonAnomalyDetectionPlugin
        |     |-- CSnakes
        |     |-- Python anomaly_detection.py
        |
        |-- LegalAgent
              |-- Semantic Kernel
              |-- LegalCompliancePlugin
              |-- MCP stdio client
              |-- CNV Regulation MCP Server
              |-- cnv_regulation PostgreSQL DB

PostgreSQL
  |-- orchestrationdb
  |-- cnv_regulation
```

## Agents

### PlannerAgent

Current implementation: deterministic .NET workflow coordinator with optional controlled LLM-assisted reasoning.

Responsibilities:

- starts the analysis workflow,
- delegates anomaly detection to the DataAgent,
- delegates regulatory evidence retrieval to the LegalAgent,
- combines agent results,
- generates a planner reasoning summary,
- decides whether human approval is required through deterministic rules,
- updates workflow state.

Current reasoning behavior:

```text
PlannerAgent
  -> IPlannerReasoningService
      -> DeterministicPlannerReasoningService
      OR
      -> SemanticKernelPlannerReasoningService
          -> Semantic Kernel
          -> OpenAI chat completion
          -> defensive JSON parsing
          -> deterministic fallback on failure
```

The `PlannerAgent` does not allow the LLM to approve, reject, complete, fail, or transition a workflow state.

### DataAgent

Current implementation: Semantic Kernel plugin layer over Python analytics.

Pipeline:

```text
DataAgent
  -> Semantic Kernel
  -> PythonAnomalyDetectionPlugin
  -> CSnakesDataAgent
  -> Python anomaly_detection.py
```

The DataAgent currently returns structured anomaly evidence, including metrics, thresholds, severity, and explanation.

### LegalAgent

Current implementation: Semantic Kernel plugin layer over MCP regulatory retrieval.

Pipeline:

```text
LegalAgent
  -> Semantic Kernel
  -> LegalCompliancePlugin
  -> McpRegulatoryKnowledgeSource
  -> CnvRegulationStdioMcpClient
  -> CnvRegulation.McpServer
  -> cnv_regulation PostgreSQL database
```

The LegalAgent retrieves cited CNV regulatory evidence and warnings. It does not make legal conclusions.

## Workflow States

The workflow is controlled by a strict state machine.

```text
Pending
  -> DataGathering
  -> AwaitingHumanApproval
  -> Completed

Pending
  -> DataGathering
  -> AwaitingHumanApproval
  -> Failed
```

Main states:

- `Pending`: analysis session was created.
- `DataGathering`: agents are collecting evidence.
- `AwaitingHumanApproval`: workflow is paused and requires human decision.
- `Completed`: human auditor approved the workflow.
- `Failed`: human auditor rejected the workflow or the workflow failed.

The system does not allow risky workflows to complete without explicit human approval.

## Human-in-the-Loop Safety Model

The system is intentionally designed to avoid fully autonomous operational decisions.

When the DataAgent or LegalAgent detects risk:

1. the workflow transitions to `AwaitingHumanApproval`,
2. evidence is persisted in PostgreSQL,
3. activity events are streamed to the React dashboard,
4. the approval panel is displayed,
5. a human auditor must approve or reject the session.

The system only moves to `Completed` after explicit approval.
If rejected, the session moves to `Failed`.

## Auditability

The platform persists:

- analysis sessions,
- current workflow state,
- planner reasoning summaries,
- LLM/fallback metadata,
- agent evidence,
- legal/regulatory findings,
- warnings and disclaimers,
- real-time activity events,
- human decisions.

The Activity Feed is not only streamed via SignalR; it is also stored in PostgreSQL and can be restored when loading previous sessions.

Planner reasoning events include:

- `planner_reasoning_completed`
- `planner_reasoning_fallback_used`

These events make it clear whether reasoning came from deterministic fallback or an LLM-assisted path.

## MCP Regulatory Retrieval

The LegalAgent integrates with a local MCP server for CNV regulatory retrieval.

Current MCP server:

```text
tools/CnvRegulation.McpServer
```

Transport:

```text
stdio
```

Primary tool:

```text
search_cnv_regulation
```

The LegalAgent only treats MCP results as evidence when they include citations.

Uncited results are ignored.

Warnings and disclaimers are propagated to the frontend to make clear that the system performs regulatory retrieval, not legal advice.

## Python Analytics via CSnakes

The DataAgent executes Python code from the .NET workflow using CSnakes.

Current flow:

```text
SemanticKernelDataAgent
  -> PythonAnomalyDetectionPlugin
  -> CSnakesDataAgent
  -> anomaly_detection.py
```

The Python analyzer returns structured anomaly evidence:

- anomaly flag,
- severity,
- summary,
- metric evidence,
- thresholds,
- interpretation.

The current implementation is deterministic and testable. It does not require an LLM.

## Screenshots

### Planner Review

![Planner Review](docs/screenshots/planner-review-deterministic.png)

### Completed Dashboard

![Completed Dashboard](docs/screenshots/dashboard-session-completed.png)

### Risk Evidence

![Risk Evidence](docs/screenshots/data-agent-evidence.png)

### Compliance Evidence

![Compliance Evidence](docs/screenshots/legal-agent-mcp-evidence.png)

### Human Approval Gate

![Human Approval Gate](docs/screenshots/human-approval-panel.png)

### Activity Feed

![Activity Feed](docs/screenshots/activity-feed.png)

### Saved Sessions

![Saved Sessions](docs/screenshots/saved-sessions-dropdown.png)

## Running Locally

### Backend

```bash
cd backend
dotnet run --project Orchestration.AppHost
```

Aspire starts:

- PostgreSQL with pgvector,
- orchestration database,
- CNV regulation database,
- API,
- CNV migration executable,
- optional CNV ingestion executable,
- React frontend.

### Enabling LLM Planner Reasoning

The system works without an LLM by default.

To enable controlled LLM planner reasoning, set:

```text
Llm__Enabled=true
Llm__Provider=OpenAI
Llm__Model=<model-name>
Llm__ApiKey=<api-key>
Llm__ServiceId=planner-reasoning
```

Do not commit API keys.

When `Llm__Enabled=false`, no API key or model is required.

When `Llm__Enabled=true`, missing model or API key fails early with a clear configuration error.

### CNV Regulation Ingestion

The CNV ingestion task is manual in Aspire.

Run it when:

- the CNV database is empty,
- the regulatory sources changed,
- parser/chunker logic changed,
- the PostgreSQL volume was deleted.

The ingestion task is intentionally not required for API startup.

### Frontend

```bash
cd frontend
npm install
npm run dev
```

The frontend receives the API URL through:

```text
VITE_API_URL
```

## Tests

Run backend tests:

```bash
cd backend
dotnet test
```

Current test coverage includes:

- state machine transitions,
- PlannerAgent delegation,
- deterministic planner reasoning,
- Semantic Kernel planner reasoning fallback,
- defensive LLM JSON parsing,
- LLM disabled configuration path,
- controlled planner reasoning metadata,
- LLM configuration validation,
- planner `ContextJson` metadata,
- CSnakes DataAgent integration,
- Semantic Kernel DataAgent wrapper,
- Semantic Kernel LegalAgent wrapper,
- MCP regulatory mapping,
- cited-result filtering,
- warning propagation.

Run frontend build:

```bash
cd frontend
npm run build
```

## Safety Notes

This project is not legal, financial, or investment advice.

The LegalAgent retrieves regulatory evidence from CNV-related sources. It does not determine legal compliance.

The DataAgent detects statistical anomalies. It does not block transactions, move money, freeze accounts, or make operational decisions.

The LLM does not make financial, legal, or operational decisions.

The LLM may generate reasoning summaries, but workflow transitions remain controlled by deterministic application logic.

Human approval is required before completing risky workflows.

This is intentional: the project demonstrates how higher-automation systems can be constrained by workflow state, evidence review, and human approval.

## Roadmap

### Next: Controlled Tool Calling

The next planned step is to allow the LLM to propose a tool execution plan while keeping workflow control deterministic.

Planned upgrades:

- allow the `PlannerAgent` LLM reasoning layer to propose tool usage,
- validate proposed tools against an allowlist,
- execute only approved deterministic tools,
- persist proposed vs executed tool calls,
- reject unsafe or unknown tool requests,
- keep HITL approval gates mandatory,
- preserve deterministic state transitions.

The LLM must not bypass the state machine or human approval flow.
