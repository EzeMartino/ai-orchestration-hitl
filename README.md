# Financial Analysis Control Room

LLM-ready orchestration platform for supervised financial anomaly review, combining .NET workflow orchestration, Python-based anomaly detection, MCP regulatory retrieval, real-time telemetry, and human approval gates.

This repository is best described as an **LLM-Ready Human-in-the-Loop Orchestration Platform**. It is designed so LLM-backed reasoning can be added later, while the current system remains deterministic, auditable, and constrained by explicit workflow state.

## Current AI Status

This project is **LLM-ready**, but it does not currently connect to a Large Language Model.

The current implementation uses deterministic agents and tool-based orchestration:

- `PlannerAgent` coordinates the workflow in .NET.
- `DataAgent` runs Python-based anomaly detection through Semantic Kernel + CSnakes.
- `LegalAgent` retrieves cited CNV regulatory evidence through Semantic Kernel + MCP.
- The workflow pauses for human approval when risk is detected.
- All activity is streamed in real time and persisted for audit review.

The architecture is intentionally designed so LLM-backed reasoning can be added later without replacing the state machine, HITL controls, MCP integration, Python analytics, or audit trail.

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

- LLM-powered reasoning,
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

Current implementation: deterministic .NET workflow coordinator.

Responsibilities:

- starts the analysis workflow,
- delegates anomaly detection to the DataAgent,
- delegates regulatory evidence retrieval to the LegalAgent,
- combines agent results,
- decides whether human approval is required,
- updates workflow state.

Future LLM-ready role:

- upgrade to an LLM-backed planner capable of reasoning over financial report content and selecting tools dynamically.

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
- agent evidence,
- legal/regulatory findings,
- warnings and disclaimers,
- real-time activity events,
- human decisions.

The Activity Feed is not only streamed via SignalR; it is also stored in PostgreSQL and can be restored when loading previous sessions.

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

Human approval is required before completing risky workflows.

This is intentional: the project demonstrates how higher-automation systems can be constrained by workflow state, evidence review, and human approval.

## Roadmap

### Next: Connect an LLM

The next planned step is to connect an LLM through Semantic Kernel.

Planned upgrades:

- Add an LLM-backed PlannerAgent.
- Allow the PlannerAgent to reason over submitted financial report content.
- Enable controlled tool-calling for DataAgent and LegalAgent.
- Keep HITL approval gates mandatory.
- Preserve deterministic state transitions.
- Persist LLM reasoning summaries and tool calls as audit events.
- Add model/provider metadata to the engine badges.

Future engine example:

```text
Semantic Kernel + GPT-4.1 + Python/CSnakes
Semantic Kernel + GPT-4.1 + MCP CNV Regulation Server
```

The LLM must not bypass the state machine or human approval flow.
