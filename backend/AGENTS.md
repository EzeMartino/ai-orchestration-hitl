# Backend Agent Guide

## Repo Map
- `Orchestration.slnx`: backend solution entrypoint.
- `Orchestration.Api`: ASP.NET Core API, controllers, SignalR `ActivityHub`, DI/runtime wiring.
- `Orchestration.AppHost`: Aspire host for PostgreSQL/pgvector, API, CNV MCP migration/ingestion, frontend.
- `Orchestration.Application`: workflow state machine, agent contracts, planner/tool-calling logic, DTOs.
- `Orchestration.Domain`: `AnalysisSession`, statuses, triggers, activity models.
- `Orchestration.Infrastructure`: EF Core, Semantic Kernel, CSnakes, MCP stdio, PostgreSQL integrations.
- `Orchestration.Tests`: xUnit coverage for state machine, agents, LLM fallback, MCP mapping, tool plans.
- `../tools/CnvRegulation.McpServer`: isolated CNV regulation MCP server used by the LegalAgent.
- `../python-agents/data_agent`: Python anomaly detection code loaded through CSnakes.

## Run
- Restore: `dotnet restore Orchestration.slnx`
- Build: `dotnet build Orchestration.slnx`
- Run full stack: `dotnet run --project Orchestration.AppHost`
- Test: `dotnet test Orchestration.slnx`
- Lint: no separate backend lint command; use build warnings plus tests.
- Optional LLM reasoning uses `Llm__Enabled`, `Llm__Provider`, `Llm__Model`, `Llm__ApiKey`, `Llm__ServiceId`.
- Keep `Llm__Enabled=false` as the no-key default; never commit API keys or local secrets.

## Engineering Guardrails
- LLM reasoning is advisory only. Workflow control stays deterministic; human approval remains mandatory.
- Do not let LLM/tool-calling code approve, reject, complete, fail, or bypass `AnalysisSessionStateMachine`.
- Keep layer boundaries: Application owns contracts/rules; Infrastructure owns SK/CSnakes/MCP/EF implementations; API owns HTTP/SignalR/DI.
- Preserve auditability: activity events, `ContextJson`, planner reasoning metadata, tool plan audit, and human decisions must remain inspectable.
- LegalAgent retrieves cited CNV evidence only; it does not make legal conclusions. Treat uncited results as warnings, not proof.
- Prefer Aspire-injected config/env vars over hardcoded URLs, DB strings, model names, or secret values.
- If Aspire/CNV fails, inspect bootstrap, migration, Postgres, MCP child-process args, and logs before changing endpoint behavior.
- Keep generated artifacts out of commits, especially `bin/`, `obj/`, `.vs/`, local settings, CNV reports, and downloaded data.
- Add or update focused tests when touching state transitions, planner reasoning, tool validation, MCP mapping, or persisted DTO shape.

## Development Insights
- When adding a runtime flag, propagate it through `Orchestration.AppHost/AppHost.cs` as well as API config. Missing AppHost env wiring has broken production-like demos before.
- Production-like mode is:
  `DataAgent__FinancialAnalysisToolsEnabled=true`,
  `DataAgent__UseFixtureMetricsFallback=false`,
  `DataAgent__RequireSessionFinancialMetrics=true`.
  In this mode, missing session metrics should fail start preflight, not fall back silently.
- `DataAgent__AiReviewEnabled`, `LegalAgent__AiReviewEnabled`, `ToolCalling__Enabled`, `ToolCalling__ExecutionMode`, and financial-tool flags must be checked at the API process that AppHost launches, not only in the parent shell.
- Structured metrics flow is append-only/auditable: validate, normalize, persist under `ContextJson.structuredFinancialMetrics`, preserve prior blocks, never start analysis automatically, never store raw upload content.
- DataAgent quantitative evidence comes from CSnakes/Python/Pandas. DataAgent AI review only interprets existing ratios, comparisons, risk signals, evidence, warnings, and limitations.
- Fixture fallback is demo-only. If it is used, `financialAnalysis.metricsInputSource` and UI/warnings must make that visible.
- `financialAnalysis` should record `metricsInputSource`, provenance, threshold profile, thresholds used, explainable risk signals, and optional `aiReview` without overwriting `structuredFinancialMetrics`.
- Risk signals should carry explainability fields when available: metric, period, value, threshold code/operator/value, reason, severity, and evidence.
- LegalAgent query/audit text must distinguish financial-signal-derived queries from general fallback queries. Do not claim queries came from risk signals when source is fallback.
- Persistible warnings should be safe. Log technical MCP/provider exception details, but avoid storing raw `ex.Message` if it may include paths, URLs, transport internals, or secrets.
- Publish human decision/activity events only after the state transition is validated. Invalid second decisions must return conflict without contaminating the audit trail.
- Controlled Tool Calling is deny-by-default. Shadow mode audits/skips execution; PlanDriven mode executes only allowlisted read-only tools through `ControlledToolExecutor`; do not persist raw `OutputJson`.

## References
- `../README.md`: architecture, local run, LLM safety model, screenshots.
- `Orchestration.Api/Program.cs`: runtime registrations and CORS.
- `Orchestration.AppHost/AppHost.cs`: Aspire resource graph and env wiring.
- `Orchestration.Application/AnalysisSessions/AnalysisOrchestratorService.cs`: workflow orchestration.
- `Orchestration.Application/Agents/Planner`: planner, reasoning, tool plan contracts.
- `../tools/CnvRegulation.McpServer/README.md`: MCP server commands and storage behavior.
