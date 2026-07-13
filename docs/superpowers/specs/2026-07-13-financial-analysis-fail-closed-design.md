# Fail-Closed Financial Analysis Execution Design

**Issue:** [#4 — Prevent financial-analysis execution failures from being classified as Low risk](https://github.com/EzeMartino/ai-orchestration-hitl/issues/4)

**Status:** Approved design; pending written-spec review

## Problem

`CSnakesFinancialAnalysisService` catches every non-cancellation exception raised while invoking or mapping the four Python financial-analysis operations. It returns empty collections plus warnings. The risk-signal and evidence-summary fallbacks additionally return `HasRiskSignals = false` and `RiskLevel = "Low"`.

`DataAgentFinancialAnalysisWorkflow` continues with those empty outputs and derives `HasAnomaly` and severity only from returned risk signals. An empty failed response is therefore indistinguishable from a successful analysis that found no risk signals. Planner only gates human approval on `DataAgentResult.HasAnomaly` or LegalAgent compliance risk, so a technical failure can complete without review.

The same ambiguity applies to parseable but structurally invalid Python responses: missing arrays become empty collections and missing or invalid numeric values can become zero.

## Goals

- Distinguish technical execution status from calculated business risk.
- Preserve valid partial outputs when one Python stage fails.
- Force human review for every failed, degraded, or unknown required financial-analysis execution.
- Keep a successful zero-signal result as a legitimate Low-risk result.
- Persist stage-level execution evidence in session context.
- Emit structured, session-correlated logs with safe failure codes and duration.
- Keep user-facing warnings safe and free of exception or payload details.
- Preserve source and JSON compatibility through additive contracts.

## Non-goals

- Changing financial formulas, ratios, threshold profiles, or Python risk rules.
- Adding automatic retries, circuit breakers, or timeout infrastructure.
- Replacing CSnakes or the current Python module.
- Redesigning the financial-risk panel.
- Reclassifying historical completed sessions or changing their workflow state.
- Making synchronous Python calls interruptible after invocation has started.

## Chosen Policy

The workflow uses a fail-closed, preserve-partial-results policy:

1. Execute every stage that can still produce useful evidence.
2. Preserve outputs from successful stages.
3. Record technical failure separately from business-risk classification.
4. Require human review when any required stage is failed, degraded, or unknown.
5. Never represent a technical failure as a successful Low-risk result.

## Execution Contracts

Add a shared execution model in the Application layer:

```csharp
public enum FinancialAnalysisExecutionStatus
{
    LegacyUnknown = 0,
    Succeeded,
    Degraded,
    Failed
}

public sealed record FinancialAnalysisStageExecution(
    string Operation,
    FinancialAnalysisExecutionStatus Status,
    long DurationMilliseconds,
    string? FailureCode = null);

public sealed record FinancialAnalysisExecution(
    FinancialAnalysisExecutionStatus OverallStatus,
    IReadOnlyList<FinancialAnalysisStageExecution> Stages);
```

Supported operation names are stable lowercase values:

- `ratios`
- `comparisons`
- `signals`
- `summary`

An individual stage emits `Succeeded`, `Failed`, or `LegacyUnknown`. `Degraded` is an aggregate workflow state, not a single-stage result.

Application code uses the enum. JSON contracts and persisted context serialize it as stable lowercase strings: `legacy_unknown`, `succeeded`, `degraded`, and `failed`. Deserialization maps a missing value to `LegacyUnknown`; unrecognized future values also fail closed as `LegacyUnknown` rather than throwing or assuming success.

Each of the four existing response records gains a non-positional `Execution` init property with an operation-specific `LegacyUnknown` default. Existing constructors and fakes therefore continue to compile. New service responses must always set an explicit `Succeeded` or `Failed` value.

Stable safe failure codes are:

- `PYTHON_INVOCATION_FAILED`: the Python call raised a non-cancellation exception.
- `PYTHON_RESPONSE_INVALID`: returned JSON was malformed or did not satisfy the required response shape.
- `FINANCIAL_ANALYSIS_UNEXPECTED_FAILURE`: a defensive workflow boundary caught an otherwise unclassified failure.

Failure codes are operator-safe identifiers. Exception messages are never copied into response DTOs, persisted context, activity-event messages, or frontend state.

## Business Risk Versus Technical Status

`FinancialAnalysisToolResult.RiskLevel` remains the business-risk value. Its supported values become `Low`, `Medium`, `High`, and `Unknown`.

- A successful signal execution with zero signals produces `RiskLevel = "Low"`.
- A failed signal execution produces `RiskLevel = "Unknown"`.
- A successful signal execution after an upstream stage failure may still calculate a risk level, but the aggregate execution remains `Degraded` and requires review.

`DataAgentResult.HasAnomaly` continues to represent business anomaly evidence only. Add an optional trailing `RequiresHumanReview` property so technical incompleteness does not need to masquerade as a detected anomaly. Planner approval becomes:

```text
dataResult.HasAnomaly
OR dataResult.RequiresHumanReview
OR legalResult.HasComplianceRisk
```

When signal execution fails, the DataAgent result uses `Severity = "Unknown"`, `HasAnomaly = false`, and `RequiresHumanReview = true`. Consumers must use the explicit review flag and execution metadata rather than interpreting `HasAnomaly = false` as a completed clean assessment.

## Stage Execution Flow

The workflow runs stages in the existing order:

1. Ratios
2. Period comparisons
3. Risk signals
4. Quantitative summary

Ratios and comparisons are independent and both run. Risk signals run with all valid metrics and any successful ratio or comparison outputs. Summary runs with the valid inputs available after signal execution so partial quantitative evidence is retained.

Aggregate rules are deterministic:

- All four stages succeeded: `Succeeded`.
- Signal execution failed: `Failed`; calculated risk is `Unknown`.
- Signals succeeded but ratios, comparisons, or summary failed: `Degraded`; preserve the calculated risk and valid evidence.
- Any stage has `LegacyUnknown` during a new execution: fail closed as `Degraded` unless a signal failure already makes the result `Failed`.

Warnings returned by a successful Python operation are domain or data-quality warnings. Warning presence alone does not change technical execution status. This preserves the current valid case where missing optional inputs produce warnings without manufacturing an anomaly.

If signal execution fails, the AI review is recorded as not run with safe reason `financial_analysis_execution_failed`. For a degraded result with successful signals, AI review may run over the preserved evidence, but its input includes the safe execution warning and its limitations must state that the analysis was incomplete.

## Python Response Validation

Invocation and response mapping are separate failure boundaries:

- Invocation exceptions map to `PYTHON_INVOCATION_FAILED`.
- Malformed JSON, missing required top-level arrays/objects, wrong JSON kinds, or invalid required values map to `PYTHON_RESPONSE_INVALID`.

Valid empty arrays remain valid. The mapper must distinguish an explicitly returned empty `signals` array from a missing or incorrectly typed `signals` property. Cancellation is checked before invocation and cancellation exceptions continue to propagate instead of becoming failed responses.

## Session Correlation and Logging

Each request record gains an optional trailing `SessionId` used only for execution correlation and excluded from the JSON sent to Python. `DataAgentFinancialAnalysisWorkflow` supplies `report.SessionId` for all four calls.

`CSnakesFinancialAnalysisService` receives `ILogger<CSnakesFinancialAnalysisService>` and measures each operation with `Stopwatch.GetTimestamp()` and `Stopwatch.GetElapsedTime(...)`.

Every completion log contains structured fields:

- `SessionId`
- `Operation`
- `DurationMilliseconds`
- `ExecutionStatus`
- `FailureCode`

Failure logs include the exception for internal diagnostics but never log serialized requests, Python responses, metric values, document content, or user-facing exception text.

The workflow publishes one safe `financial_analysis_execution_degraded` or `financial_analysis_execution_failed` activity event per run. Existing `ActivityEvent.SessionId` provides session correlation. The event identifies affected operation names and stable failure codes without exception details.

## Persistence and Orchestration

`FinancialAnalysisContext` gains a non-positional `Execution` property with a `LegacyUnknown` default. `AnalysisOrchestratorService` explicitly maps this property into `contextJson.financialAnalysis`; adding the record property alone is not sufficient because the orchestrator builds an anonymous persisted object.

The anomaly context adds two explicit fields:

```json
{
  "anomaly": {
    "detected": false,
    "assessmentStatus": "inconclusive",
    "requiresHumanReview": true
  }
}
```

`assessmentStatus` is `completed` only when the signal assessment succeeded and `inconclusive` when it did not. These fields let operators distinguish:

- a calculated anomaly,
- a completed clean assessment,
- an inconclusive technical assessment.

Planner transitions to the existing `AwaitingHumanApproval` state whenever `RequiresHumanReview` is true. No new session state is introduced.

`ConfigurableDataAgent` must preserve fail-closed semantics across its legacy fallback. If an unexpected structured-workflow exception causes fallback to a healthy legacy result, the combined result remains review-required and records a safe unexpected-failure code. A fallback cannot erase the primary execution failure.

## Compatibility

- New response and context fields are additive and use defaults, preserving existing positional constructors.
- Old JSON without execution metadata deserializes as `LegacyUnknown`, never as `Succeeded`.
- Historical completed sessions are not reopened or mutated. The UI displays their missing metadata neutrally.
- New executions containing `LegacyUnknown` fail closed and require review.
- Unknown future JSON fields remain tolerated by existing serializer behavior.
- `AnalysisSessionDto` continues exposing the same `ContextJson` string; no API envelope change is required.
- No EF Core migration is required because execution metadata is stored inside session JSON.

## Frontend Behavior

Extend `FinancialAnalysisContext` with optional execution metadata. The existing financial-risk panel adds a compact status banner:

- `Succeeded`: no extra warning banner.
- `Degraded`: “Análisis incompleto — revisión humana requerida.”
- `Failed`: “No se pudo completar la evaluación de riesgo — revisión humana requerida.”
- `LegacyUnknown`: neutral historical message stating that execution metadata is unavailable.

The banner lists affected operations using safe labels and failure codes. It never renders exception text. A degraded result may still show valid ratios, comparisons, signals, evidence, and calculated risk, but the banner prevents the result from being presented as a successful Low-risk assessment.

## Testing Strategy

Implementation follows test-driven development.

### Adapter tests

- Each of the four operations returns explicit `Succeeded` metadata on valid responses.
- Each operation maps an invocation exception to `Failed` plus `PYTHON_INVOCATION_FAILED`.
- Malformed JSON and structurally invalid JSON map to `PYTHON_RESPONSE_INVALID`.
- Explicit empty arrays remain successful.
- Pre-cancelled tokens propagate cancellation.
- Duration and structured log fields are populated.
- Logs and returned warnings do not expose exception text or financial payloads.

### Workflow tests

- Independent ratio, comparison, signal, and summary failures produce the specified aggregate status.
- Successful outputs survive unrelated stage failures.
- Signal failure returns risk and severity `Unknown` and forces review.
- Successful zero-signal analysis remains `Succeeded`, Low, and does not require review.
- Domain warnings without technical failure do not force review.
- Degraded AI review contains an incomplete-analysis limitation; failed signal analysis skips AI review.
- Healthy legacy fallback cannot erase a primary structured-workflow failure.

### Planner and orchestration tests

- `RequiresHumanReview = true` forces Planner approval even without anomaly or legal risk.
- A degraded or failed execution transitions to `AwaitingHumanApproval`.
- Session context round-trips overall and stage execution metadata.
- Persisted anomaly data distinguishes calculated, clean, and inconclusive outcomes.
- Old context JSON without execution metadata remains readable.

### Frontend tests

- Render succeeded, degraded, failed, and legacy-unknown states.
- Show valid partial evidence under degraded status.
- Never present degraded or failed execution as successful Low risk.
- Render only safe failure codes and labels.

## Acceptance Mapping

- Technical failure cannot become successful Low risk: failed signals use `Unknown`; aggregate status is explicit.
- Execution status is separate from calculated risk: stage and aggregate execution records are independent of `RiskLevel`.
- Failed or partial required stages force review: `RequiresHumanReview` participates in Planner gating.
- User-facing warnings are safe: stable codes and curated messages only.
- Structured logs are complete: operation, duration, session ID, status, and failure code.
- Failure coverage is complete: ratios, comparisons, signals, and summary each receive adapter and workflow tests.
