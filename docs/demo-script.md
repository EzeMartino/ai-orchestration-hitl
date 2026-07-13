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

### Fail-Closed Degraded Path Check

Use the deterministic production-like E2E scenario to simulate a failed ratio stage while the signal stage succeeds with no business-risk evidence:

```bash
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~ProductionLikeWorkflow_DegradedFinancialAnalysis_ShouldPersistReviewStateAcrossApiReload" --verbosity minimal
```

Expected result:

- Execution status is `degraded`, and the failed stage exposes only its operation and stable failure code.
- The session pauses at `AwaitingHumanApproval` even though no anomaly or legal risk was detected.
- The dashboard presents the incomplete-analysis banner and requires human review; retained valid evidence remains visible.
- Reloading the session preserves `financialAnalysis.execution`, including its aggregate status and stage records.
- No raw payload, metric value, or exception text appears in the dashboard or Activity Feed.

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
