# Production-like Demo Script

This script demonstrates the production-like financial analysis workflow end to end.

The demo shows:

- session-attached structured metrics,
- start readiness preflight,
- DataAgent quantitative analysis through CSnakes + Python/Pandas,
- DataAgent AI review,
- LegalAgent CNV/Infoleg evidence retrieval,
- LegalAgent AI review,
- Planner review,
- human approval,
- reload/audit preservation.

## Safety Notes

The demo does not provide investment advice or legal advice.

LLM outputs are advisory. Python/Pandas computes the quantitative evidence. The backend State Machine controls workflow transitions. Human approval is mandatory before risky sessions complete.

If LLM credentials are unavailable, deterministic fallback remains safe and expected. The demo is still valid when `usedFallback=true`.

## Recommended Config

Use production-like mode:

```text
DataAgent__FinancialAnalysisToolsEnabled=true
DataAgent__UseFixtureMetricsFallback=false
DataAgent__RequireSessionFinancialMetrics=true
DataAgent__AiReviewEnabled=true
LegalAgent__AiReviewEnabled=true
Llm__Enabled=true
ToolCalling__Enabled=true
ToolCalling__ExecutionMode=Shadow
```

Fallback-friendly local demo:

```text
DataAgent__FinancialAnalysisToolsEnabled=true
DataAgent__UseFixtureMetricsFallback=false
DataAgent__RequireSessionFinancialMetrics=true
DataAgent__AiReviewEnabled=false
LegalAgent__AiReviewEnabled=false
Llm__Enabled=false
ToolCalling__Enabled=true
ToolCalling__ExecutionMode=Shadow
```

## Start Aspire

From the repository root:

```bash
cd backend
dotnet run --project Orchestration.AppHost
```

Expected result:

- Aspire starts the API, PostgreSQL resources, CNV migration task, and frontend resource.
- API health/logs show no startup failure.
- No analysis session is created automatically.

## Open Frontend

Open:

```text
http://localhost:5173
```

Use `localhost`, not `127.0.0.1`, because the default local CORS configuration targets localhost.

Expected result:

- Financial Analysis Control Room loads.
- Activity Feed panel is visible.
- No session is selected yet.

## Create Session

Click `Create Analysis Session`.

Expected result:

- A new session is created.
- Session status is `Pending`.
- Start readiness is checked.
- In production-like mode with no metrics, readiness shows that structured financial metrics are required.
- `Start Session` is blocked by preflight.

Reference screenshot:

![Start Readiness Blocked](screenshots/start-readiness-blocked.png)

## Attach Sample JSON Metrics

In `Structured metrics input`, use `Load sample JSON` or upload:

```text
frontend/public/templates/structured-financial-metrics-sample.json
```

Then click `Save JSON Metrics`.

Expected result:

- Metrics are validated and persisted to the current `AnalysisSession.ContextJson`.
- The panel shows `Structured metrics attached`.
- Document, company, metric count, upload time, source/provenance are visible.
- Activity Feed records `structured_financial_metrics_attached`.
- No analysis starts automatically.

Reference screenshots:

![Structured Metrics Input](screenshots/structured-metrics-input.png)

![Structured Metrics File Upload](screenshots/structured-metrics-file-upload.png)

## Check Start Readiness

After metrics are attached, confirm the readiness panel.

Expected result:

- Readiness says the session is ready to start.
- `Start Session` is enabled.
- Backend preflight would return `canStart=true`.

Reference screenshot:

![Start Readiness Ready](screenshots/start-readiness-ready.png)

## Start Session

Click `Start Session`.

Expected result:

- Session moves through `DataGathering`.
- PlannerAgent starts.
- DataAgent uses persisted session metrics, not fixture fallback.
- LegalAgent derives CNV/Infoleg queries from financial risk signals.
- Planner review completes.
- Status becomes `AwaitingHumanApproval`.
- Activity Feed contains agent, tool-plan, regulatory retrieval, and HITL events.

## Review Financial Risk Evidence

Open or scroll to `Financial Risk Evidence`.

Expected result:

- Engine is `Semantic Kernel + CSnakes + Python/Pandas`.
- Metrics source is `Session context`.
- Document and company come from the attached metrics.
- Fixture fallback warning is absent.
- A successful execution has no degraded or failed execution banner.
- Risk signals include metric, period, observed value, threshold, and reason.
- Threshold profile and thresholds used are visible.

Reference screenshot:

![Financial Risk Evidence from Session Context](screenshots/financial-risk-evidence-session-context.png)

### Fail-Closed Degraded Path Checks

There is no production runtime configuration switch that injects a failed financial-analysis stage. The degraded path is reproducible through the deterministic test fixture and frontend presenter tests; do not add failure injection to a demo deployment.

#### Backend Workflow and Persistence

Use the production-like E2E fixture to simulate a failed ratio stage while the signal stage succeeds with no business-risk evidence:

```bash
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~ProductionLikeWorkflow_DegradedFinancialAnalysis_ShouldPersistReviewStateAcrossApiReload" --verbosity minimal
```

This backend test verifies:

- The session pauses at `AwaitingHumanApproval` with `anomaly.detected=false`, an inconclusive assessment, and human review required.
- Persisted context and the API-reloaded context both retain `financialAnalysis.execution`, its `degraded` aggregate status, and all stage records.
- The failed ratio stage contains its safe operation, `failed` status, duration, and `PYTHON_INVOCATION_FAILED`; the successful signal stage remains explicit.
- Persisted execution metadata contains no exception text.

This test does not launch the dashboard or inspect the Activity Feed.

#### Activity Privacy

Run the focused workflow test that injects sensitive warning, metric, and exception-like details into a failed signal stage:

```bash
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~DataAgentFinancialAnalysisWorkflowTests.AnalyzeAsync_Should_fail_closed_when_signal_stage_fails" --verbosity minimal
```

Expected result:

- The Activity Feed event is correlated to the session and contains the safe operation and stable failure code.
- Sensitive adapter text, exception-like failure text, metric names, and metric values are excluded from the event message.
- Valid financial evidence displayed elsewhere is not hidden by this technical-message privacy rule.

#### Frontend Presenter

Run the execution-banner presenter tests:

```bash
node --test frontend/tests/financialAnalysisExecution.test.ts
```

Expected result:

- A complete `succeeded` execution has no warning banner.
- A `degraded` execution produces the human-review warning with allowlisted stage labels and stable failure codes.
- Unknown operations, raw failure text, and duration are not rendered as technical failure details.

For a live successful session, reload it, open `Financial Risk Evidence`, confirm no degraded/failed banner is present, and confirm valid metric evidence remains visible. A live degraded dashboard requires a test fixture or test host that returns the persisted degraded context; the standard demo configuration cannot inject that failure. Use the deterministic backend fixture and frontend presenter tests above for the reproducible degraded-path demonstration.

## Review DataAgent AI Review

In `Financial Risk Evidence`, review `DataAgent AI Review`.

Expected result:

- AI review status is visible.
- If LLM is configured and returns valid JSON, `usedLlm=true`.
- If LLM is disabled or fails safely, `usedFallback=true`.
- Summary, key findings, risk interpretation, data quality notes, and limitations are visible.
- Review text is advisory and does not recompute metrics.

## Review LegalAgent CNV/Infoleg Evidence

Open the compliance or legal evidence panel.

Expected result:

- CNV/Infoleg evidence is visible when cited results are available.
- Evidence without citations is not treated as strong legal support.
- Warnings/limitations are shown when evidence is weak, missing, or uncited.
- The LegalAgent does not declare legal violations.

Reference screenshot:

![Compliance Evidence](screenshots/legal-agent-mcp-evidence.png)

## Review LegalAgent AI Review

Review the LegalAgent AI review section.

Expected result:

- AI review metadata is visible.
- Possible regulatory review areas use only provided citations.
- No invented citations are shown.
- The output does not provide legal advice or declare violations.
- Deterministic fallback is visible if LLM is unavailable or unsafe.

## Review Planner Review

Open `Planner Review`.

Expected result:

- Planner reasoning summary is visible.
- `usedLlm`, `usedFallback`, provider, model, and failure reason metadata are visible when available.
- Controlled `toolPlan` audit is present when tool calling is enabled.
- Planner reasoning does not control State Machine transitions.

Reference screenshot:

![Planner Review](screenshots/planner-review-deterministic.png)

## Approve

In the human approval panel, enter a reason and click `Approve`.

Expected result:

- `human_decision_received` is recorded.
- Session moves to `Completed`.
- `analysis_completed` is recorded.
- Evidence remains in `ContextJson`.

Reference screenshot:

![Human Approval Gate](screenshots/human-approval-panel.png)

## Reload Previous Session

Use the saved sessions selector to reload the completed session.

Expected result:

- Status remains `Completed`.
- `structuredFinancialMetrics` is restored.
- `financialAnalysis` and `financialAnalysis.aiReview` are restored.
- `financialAnalysis.execution` and its stage records are restored.
- `compliance` and `compliance.legalReview` are restored.
- `planner` and `toolPlan` are restored.
- Activity Feed history is restored.

Reference screenshots:

![Completed Dashboard](screenshots/dashboard-session-completed.png)

![Saved Sessions](screenshots/saved-sessions-dropdown.png)

![Activity Feed](screenshots/activity-feed.png)

## Rejection Path Optional Check

For an alternate demo, create a second session, attach sample metrics, start it, and click `Reject`.

Expected result:

- Session moves to `Failed`.
- `analysis_rejected` is recorded.
- `analysis_completed` is not recorded.
- Evidence and Activity Feed remain available after reload.
- A second approve/reject decision is rejected safely.

## Validation Commands

Before presenting the demo, run:

```bash
python -m unittest discover -s python-agents\tests
```

```bash
cd backend
dotnet build
dotnet test
```

```bash
cd frontend
npm run build
```
