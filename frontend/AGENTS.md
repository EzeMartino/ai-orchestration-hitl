# Frontend Agent Guide

## Repo Map
- `package.json`: Vite React scripts and dependencies.
- `src/main.tsx`: React root with `StrictMode`.
- `src/App.tsx`: audit console, API calls, SignalR client, session state, evidence panels, HITL actions.
- `src/App.css`: all UI styling for the control room.
- `index.html`: Vite entrypoint.
- `dist/` and `node_modules/`: generated/local only; do not commit.

## Run
- Install: `npm install`
- Dev server: `npm run dev`
- Build/type-check: `npm run build`
- Preview build: `npm run preview`
- Lint: no dedicated lint script; `npm run build` runs `tsc` and Vite.
- AppHost also starts this app and injects `VITE_API_URL`; standalone fallback is `http://localhost:5148`.
- Prefer `http://localhost:5173` for browser/API testing because backend CORS is configured for that origin.

## Engineering Guardrails
- Keep this as an operational audit console, not a marketing/landing page.
- Preserve HITL flow: create/load session, start analysis, review evidence, approve/reject with a reason.
- Keep evidence visible after `Completed` or `Failed`; do not hide anomaly, compliance, planner, or tool-plan audit data.
- Mirror backend DTOs carefully: `AnalysisSessionResponse`, saved-session summaries, events, and parsed `ContextJson`.
- `ContextJson` parsing must stay defensive; malformed JSON should not break the UI.
- SignalR must clean up safely under React `StrictMode`; avoid duplicate active connections.
- Prefer `VITE_API_URL` for API base URL and keep any localhost value as local fallback only.
- If backend errors appear as generic UI failures, call the API directly and inspect the response before changing frontend state logic.
- Maintain concise, scan-friendly status hierarchy: current session, planner review, tool audit, evidence, activity timeline, human decision.
- Keep UI changes compatible with `npm run build`; no new package unless it clearly improves the app.

## Development Insights
- Use `http://localhost:5173` for local UI validation. `127.0.0.1` can miss the configured CORS surface.
- AppHost injects `VITE_API_URL`; standalone Vite uses the local fallback. When behavior differs, inspect the generated frontend env and API endpoint first.
- Start readiness is a UX guardrail, not the security boundary. Backend `POST /api/analysis-sessions/{id}/start` preflight remains authoritative.
- Disable `Start Session` only when preflight was loaded successfully and `canStart=false`. If readiness fetch fails, allow start and let backend return the real result.
- Structured metrics UI supports JSON paste, CSV paste, JSON/CSV upload, and sample templates. Saving metrics must refresh metrics context and start preflight, but must not start analysis.
- In production-like mode, a new session without metrics should show `STRUCTURED_FINANCIAL_METRICS_REQUIRED` and `Start blocked by preflight`.
- Once `financialAnalysis` exists, show `Financial Risk Evidence` and hide legacy `Risk Evidence` to avoid duplicate DataAgent output.
- Financial panels should show metrics source/provenance, threshold profile, thresholds used, explainable risk signals, warnings, limitations, and DataAgent AI Review metadata.
- Show fixture fallback and missing-required-metrics warnings prominently. Never imply fixture data is session-specific.
- LegalAgent UI should frame outputs as possible regulatory review areas, keep citations visible, and avoid legal conclusion/recommendation language.
- Reloaded `Completed` and `Failed` sessions must restore evidence panels and Activity Feed. Do not gate evidence visibility on active/in-progress states.
- For browser smoke tests on this Windows machine, Chrome may be blocked by local policy; Playwright with `--channel=msedge` worked for final validation.

## References
- `../README.md`: product framing, safety model, run commands, screenshot set.
- `../docs/screenshots`: expected audit-console surfaces.
- `../backend/Orchestration.Api/Controllers/AnalysisSessionController.cs`: HTTP contract.
- `../backend/Orchestration.Api/Hubs/ActivityHub.cs`: SignalR endpoint.
- `../backend/Orchestration.Application/AnalysisSessions/AnalysisOrchestratorService.cs`: session DTO/context behavior.
