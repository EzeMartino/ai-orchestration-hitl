---
name: aihitl-live-e2e-verification
description: Use when ai-orchestration-hitl changes require proof through Aspire, the live API and browser, SignalR, financial file upload or review, or persisted analysis-session context.
---

# AIHITL Live E2E Verification

HTTP success alone is not end-to-end proof. Prove one isolated run across resources, API, browser, SignalR, and persistence.

## Workflow

1. Treat a running AppHost as shared state. Before browser actions, inspect Aspire readiness/logs and record endpoints. Wait for dependencies, migration, `orchestration-api`, and `frontend` to reach steady state. Do not trust an old tab or restart healthy resources.
2. Use Aspire's API URL, but open `http://localhost:5173`; configured CORS rejects `127.0.0.1`. Use a fresh browser context with Network and Console visible.
3. Register a unique disposable account, authenticate, create a new session, and use unique synthetic input. Record session ID and input filename/hash. Never reuse seeded accounts, sessions, or credentials; never expose secrets.
4. Exercise only the relevant flow. Capture URL, method, status, decisive response fields, and UI state. Reload `GET /api/analysis-sessions/{id}` and parse persisted `contextJson`; neither upload response nor UI memory proves persistence.
5. Prove SignalR: show `/hubs/activity` connected and capture an `activityEventReceived` event whose session ID/type matches the Activity Feed and `GET /api/analysis-sessions/{id}/events`. Record relevant console errors or state that none appeared during observation.
6. Recheck after reload. Call startup failures transient only when readiness/logs stabilize and the same action succeeds. Reproducible failures after steady state are product/config failures.

## Decision table

| Change | Required live proof |
| --- | --- |
| API/session | Authenticated response + reloaded session/context |
| UI | API truth + visible state after reload |
| Activity/event | SignalR event + feed + persisted event |
| PDF review | Upload + stored draft + UI/server gates + unchanged active context |

For PDF review, require every gate: upload returns `outcome: "review_required"`; `reviewDraft.payload.candidates` is nonempty; `GET .../financial-metrics/review` returns that draft; the editor renders those candidates; preflight returns `canStart: false` with `FINANCIAL_METRICS_REVIEW_REQUIRED`; Start is disabled; `POST .../start` returns `409` without agent execution; reloaded `contextJson` excludes unconfirmed candidates from active `structuredFinancialMetrics`. **NEVER call `/confirm` or click confirmation unless explicitly requested.**

## Stop and report

Stop if resource/endpoint identity, auth isolation, or authority is unclear; evidence layers disagree; or proof needs review confirmation or out-of-scope product changes.

Report exact resources/endpoints; disposable identity label, session ID, and input; API/UI evidence; SignalR event and console result; persisted context/event; transient/steady-state classification; cleanup; verdict and gaps.

Clean only this run's account, session, draft, and input through supported authorized operations. If deletion is unavailable, leave them isolated and report identifiers. Never purge databases or alter unrelated sessions.

## Common mistakes

| Mistake | Correction |
| --- | --- |
| HTTP `200` means complete | Prove all applicable layers |
| Old `127.0.0.1` tab | Reopen `localhost:5173` |
| Connected badge alone | Capture matching SignalR and persisted events |
| Review draft confirmed for convenience | Leave it pending unless explicitly requested |
| Broad cleanup | Touch only disposable artifacts |
