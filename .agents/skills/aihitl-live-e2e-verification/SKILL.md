---
name: aihitl-live-e2e-verification
description: Use when ai-orchestration-hitl changes require proof through Aspire, the live API and browser, SignalR, financial file upload or review, or persisted analysis-session context.
---

# AIHITL Live E2E Verification

HTTP success alone is not end-to-end proof. Prove one isolated run across resources, API, browser, SignalR, and persistence.

## Workflow

1. Treat AppHost as shared. Before browser work, inspect Aspire readiness/logs, record endpoints, and wait for dependencies, migration, API, and frontend steady. Do not trust old tabs or restart healthy resources.
2. Use Aspire's API URL, but open `http://localhost:5173`; configured CORS rejects `127.0.0.1`. Inspect Network and Console in a fresh browser context.
3. Create a unique disposable account/session and synthetic input. Record session ID and input filename/hash. Never reuse seeded data or expose secrets.
4. Exercise the relevant flow. Capture URL, method, status, decisive fields, and UI state. Reload `GET /api/analysis-sessions/{id}` and parse persisted `contextJson`; response/UI memory does not prove persistence.
5. If the flow should emit activity, prove `/hubs/activity` connected and capture an `activityEventReceived` whose session ID/type matches the Activity Feed and `GET /api/analysis-sessions/{id}/events`. Otherwise record SignalR/event proof as N/A; never create unrelated mutation merely to emit an event. Always record relevant console errors or none.
6. Recheck after reload. Classify a startup error transient only when contemporaneous readiness/log evidence causally identifies an unready dependency and the same action succeeds after it becomes ready. Stabilization plus retry alone is insufficient; otherwise classify it unresolved/product flake.

## Decision table

| Change | Required live proof |
| --- | --- |
| API/session | Authenticated response + reloaded session/context |
| UI | API truth + visible state after reload |
| Activity/event | If emission is expected: SignalR + feed + persisted event; otherwise N/A |
| PDF review | Upload + stored draft + UI/server gates + unchanged active context |

For PDF review, require every gate: upload returns `outcome: "review_required"`; `reviewDraft.payload.candidates` is nonempty; `GET .../financial-metrics/review` returns that draft; the editor renders those candidates; preflight returns `canStart: false` with `FINANCIAL_METRICS_REVIEW_REQUIRED`; Start is disabled; `POST .../start` returns `409` without agent execution; reloaded `contextJson` excludes unconfirmed candidates from active `structuredFinancialMetrics`. **NEVER call `/confirm` or click confirmation unless explicitly requested.**

## Stop and report

Stop when endpoint identity, auth isolation, authority, or evidence is unclear/disagrees, or proof needs review confirmation/out-of-scope changes.

Report resources/endpoints; disposable identity, session ID, and input; API/UI; SignalR/event or N/A and console; persisted context/event; startup classification evidence; cleanup; verdict/gaps.

Clean only run-created account/session/draft/input via authorized supported operations. If unavailable, report identifiers and leave isolated. Never purge databases or alter unrelated sessions.

## Common mistakes

| Mistake | Correction |
| --- | --- |
| HTTP `200` means complete | Prove all applicable layers |
| Old `127.0.0.1` tab | Reopen `localhost:5173` |
| Retry succeeds after startup | Require causal dependency evidence |
| No activity expected | Record N/A; never manufacture an event |
| Review draft confirmed for convenience | Leave it pending unless explicitly requested |
