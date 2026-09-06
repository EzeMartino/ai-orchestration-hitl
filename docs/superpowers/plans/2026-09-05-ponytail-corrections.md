# Ponytail Corrections Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Correct the eleven report defects and three identified simplifications on a dedicated branch.

**Architecture:** Keep existing HTTP contracts and deterministic workflow. Fix numerical validity at Python inputs, session consistency at persistence, UI identity at asynchronous boundaries, and preserve typed outputs inside the Planner.

**Tech Stack:** .NET 10, EF Core/Npgsql, Python unittest, React 19, TypeScript, Node test runner.

**Spec:** `docs/superpowers/specs/2026-09-05-ponytail-corrections-design.md`

## Global Constraints

- Work on `codex/ponytail-corrections`; do not commit or overwrite pre-existing AGENTS.md edits.
- No new production dependencies or broad refactoring. Preserve HTTP, evidence, legacy snapshots and controlled-execution safeguards.
- No new backend/frontend comments or docblocks.
- Tests must first demonstrate the defect, then pass. Keep unrelated modifications out of each task.
- Implementers do not spawn agents, commit, switch branches or run overlapping backend builds; the controller coordinates reviews and commits.

### Task 1: Financial arithmetic and truthful legacy indicators

**Files:** `python-agents/data_agent/financial_analysis.py`, `python-agents/data_agent/anomaly_detection.py`, `python-agents/tests/test_financial_analysis.py`; add a small anomaly test file if needed.

**Interfaces:** Existing JSON-in/JSON-out functions and anomaly tuple stay unchanged. Other tasks consume these responses unchanged. Keep legacy metric identifiers if renaming would break external callers; descriptions must state the actual heuristic.

- [x] Write tests using existing request fixtures and explicit expected outputs. Cases: current assets 2 millions / liabilities 500000 units; incompatible currencies; same scale/currency still computes; comparison across periods with incompatible units; EBITDA margin 25% to 24%; quick ratio with missing investments/receivables; explicit zero operands; confidence zero.

```python
assert invalid_ratios["ratios"] == []
assert invalid_ratios["warnings"]
assert normalized_margin["value"] == 0.25
assert not any(s["code"] == "MARGIN_COMPRESSION" for s in normalized_signals["signals"])
assert complete_quick_ratio["value"] == 0.2
assert zero_confidence_ratio["confidence"] == 0
```

- [x] Run new focused unittest cases and record the expected failures.
- [x] Implement compatibility checks where operands are chosen; normalize reported percent ratios once; require every sum operand; remove boolean fallback replacing zero confidence. Keep existing valid same-unit fixtures working. Do not build currency conversion.
- [x] Correct legacy descriptions to fixed amount/count heuristics without claiming historical/time-window evidence; cover observable output with a focused test.
- [x] Run `python-agents/data_agent/.venv/Scripts/python.exe -m unittest discover -s python-agents/tests -q`. Report changes, red/green proof and any remaining concerns.

### Task 2: Session-aware frontend and truthful presentation

**Files:** `frontend/src/hooks/useAnalysisSession.ts`, `frontend/src/App.tsx`, `frontend/src/components/FinancialRiskEvidencePanel.tsx`, relevant `frontend/tests/*.test.ts`; no package changes without demonstrating necessity.

**Interfaces:** APIs retain current signatures. Task 4 will persist null financial confidence; UI must render absent confidence honestly while still accepting historic numeric values. Keep React lifecycle behavior valid under StrictMode.

- [x] Add behavioral regression checks for the real hook/state path with deferred responses. Exercise A then B with B resolving first; start/decision/metrics responses from A after selecting B; other-session SignalR events; dependent event/preflight refreshes; old finally/error handlers must not overwrite B.

```typescript
assert.equal(currentSession.id, "B");
assert.ok(events.every(event => event.sessionId === "B"));
```

- [x] Run failing checks using the existing Node test infrastructure. Reuse dependencies; a small test harness around real code is preferable to a new state library.
- [x] Reuse/extend the existing session-generation guard for all async operations and refreshes. Filter live events using current session identity. Keep loaded session, metrics, readiness and busy/error state coherent.
- [x] Use neutral Completed/Failed copy; remove unsupported numeric confidence when null. Do not infer a decision from status alone.
- [x] Consolidate JSON/CSV save coordination and reuse existing refresh logic while preserving their distinct API calls and errors.
- [x] Run `npm test --prefix frontend` and `npm run build --prefix frontend`. Report changed files and red/green proof.

### Task 3: Typed internal execution and direct legacy DataAgent

**Files:** `backend/Orchestration.Application/Agents/Planner/ToolCalling/ToolExecutionResult.cs`, related mapping contract, `backend/Orchestration.Infrastructure/Agents/Planner/ToolCalling/ControlledToolExecutor.cs`, `ToolExecutionResultMapper.cs`, related Planner tests, legacy DataAgent classes/tests, `backend/Orchestration.Api/Program.cs` registrations only.

**Interfaces:** Keep ToolExecutionResult audit fields and HTTP diagnostic compatibility. Add typed internal Data/Legal payloads without exposing them in persisted audit or diagnostic JSON. Existing `OutputJson` inputs may still enter compatibility mapping in tests/diagnostics. Preserve all existing mapping validation, normalization and legal aggregation rules; typed results must not bypass semantic safeguards. Do not touch AnalysisOrchestratorService, DbContext, publisher or controller (Task 4 owns those).

- [x] Establish tests for real executor -> mapper Data and Legal results preserving evidence/RequiresHumanReview, and typed payloads remaining absent from serialization. Keep malformed legacy JSON tests.

```csharp
result.OutputJson.Should().Be("{}");
mapped.RequiresHumanReview.Should().BeTrue();
serializedAudit.Should().NotContain("typedDataResult");
```

- [x] Observe expected failures before changing execution output. Use explicit typed fields (JsonIgnore) or equivalently small typed contract, not a new generic dispatch framework.
- [x] Route internal mapping directly from typed results; retain raw JSON parsing at compatibility entry only. Factor shared semantic normalization so it is not duplicated or lost; keep required audit metadata and legal relevance boundaries.
- [x] Trace callers of SemanticKernelDataAgent/PythonAnomalyDetectionPlugin. Replace the fixed wrapper route with direct CSnakes registration preserving ILegacyDataAgent; remove genuinely unused wrappers/results/tests for the deleted indirection. Keep actual LLM registrations and financial-result contracts.
- [x] Coordinate backend build slot with controller; run focused executor/mapper/Planner/Data tests. Report removed/retained responsibilities and proof. Do not commit independently.

### Task 4: Atomic transitions, failure recovery and honest persistence

**Files:** `backend/Orchestration.Application/AnalysisSessions/AnalysisOrchestratorService.cs`, `backend/Orchestration.Infrastructure/Persistence/OrchestrationDbContext.cs`, EF model snapshot as required, `backend/Orchestration.Api/Hubs/SignalRActivityPublisher.cs`, `backend/Orchestration.Tests/AnalysisSessions/*`, publisher tests; README truthfulness corrections.

**Interfaces:** Preserve public orchestrator and publisher signatures where possible. Existing publisher saves the shared DbContext: stage the transition before its decision event, so a single SaveChanges commits state and event. Status becomes a concurrency token. Task 3 owns Program.cs and Planner/tool mapping; Task 2 expects nullable financial confidence.

- [x] Add regression tests with two preloaded contexts: only one start and one human decision can win. Assert losing attempt records no decision event. Inject planner failure and notification failure and inspect persisted state/events, not only mock call counts.

```csharp
await losingDecision.Should().ThrowAsync<InvalidOperationException>();
persistedEvents.Count(e => e.Type == "human_decision_received").Should().Be(1);
failedSession.Status.Should().Be(AnalysisSessionStatus.Failed);
```

- [x] Run tests red. Add PostgreSQL proof with an isolated disposable test database when local service is available; otherwise report explicit skip, never fake relational coverage.
- [x] Add Status concurrency token; translate conflicts to existing HTTP conflict flow and clear rejected tracked changes. Stage state before publication so existing publisher saves transition/event atomically. Prevent SignalR transport failure from undoing/aborting a committed workflow.
- [x] Catch execution failures after a successful start claim, persist safe Fail state and audit; a losing start conflict must not fail the winner. Preserve cancellation semantics and do not store raw exception messages. No process-crash recovery mechanism.
- [x] Persist null for unsupported financial confidence fields; add snapshot regression. Adjust README mandatory-approval/statistics claims to match actual contract without changing HITL policy.
- [x] Run focused tests, then full backend/Python/frontend suites and frontend build. Run independent task/whole-branch review, resolve findings, verify diff and commit only authorized files.


## Execution results — 2026-09-06

All four tasks completed and independently reviewed. The additional review findings in revenue/capex dimension handling and frontend history ordering were reproduced, corrected and verified. No important review findings remain open.

- Backend: `dotnet test backend/Orchestration.slnx --no-restore --verbosity quiet` with `ORCHESTRATION_TEST_POSTGRES` configured: **1876 passed, 0 failed, 0 skipped**. PostgreSQL concurrency and transaction rollback ran against an isolated disposable server.
- Python: `python-agents/data_agent/.venv/Scripts/python.exe -m unittest discover -s python-agents/tests -q`: **47 passed**.
- Frontend: `npm test --prefix frontend`: **57 passed**. `npm run build --prefix frontend`: **passed**.
- Commits: `7ba418c` backend consistency and typed execution; `7d4f229` financial validity; `3514c35` frontend session isolation and activity history.

Hook regression tests execute the actual hook with deferred requests and a small hook dispatcher; they emulate effect cleanup/restart, rather than prove a browser's full React lifecycle. The confidence test renders the actual component through React SSR. No live LLM/CNV deployment or process-crash recovery is claimed.

The pre-existing Microsoft.OpenApi NU1903 warning remains outside the validated report corrections. Differently written monetary dimension labels are rejected rather than converted or inferred. Existing user edits in both AGENTS.md files remain excluded. Delivery stays on the requested local branch; no merge, push or PR was requested.
