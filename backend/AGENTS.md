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

## References
- `../README.md`: architecture, local run, LLM safety model, screenshots.
- `Orchestration.Api/Program.cs`: runtime registrations and CORS.
- `Orchestration.AppHost/AppHost.cs`: Aspire resource graph and env wiring.
- `Orchestration.Application/AnalysisSessions/AnalysisOrchestratorService.cs`: workflow orchestration.
- `Orchestration.Application/Agents/Planner`: planner, reasoning, tool plan contracts.
- `../tools/CnvRegulation.McpServer/README.md`: MCP server commands and storage behavior.
