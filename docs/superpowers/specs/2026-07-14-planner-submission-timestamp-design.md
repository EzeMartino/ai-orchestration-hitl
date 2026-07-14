# Planner Submission Timestamp Integrity Design

**Date:** 2026-07-14  
**Status:** Approved for implementation planning  
**Issue:** [#5 — Carry the real submission timestamp through tool planning and execution](https://github.com/EzeMartino/ai-orchestration-hitl/issues/5)

## Context

PR #18 introduced `FinancialReportContext.SubmittedAt`, added it to
`ToolPlanProposalInput`, passed it to deterministic and Semantic Kernel proposal
services, and removed the deterministic `DateTimeOffset.UtcNow` fallback. The
persisted report timestamp also reaches controlled Data-tool execution in the
deterministic path.

One integrity gap remains: `SemanticKernelToolPlanProposalService` includes the
trusted timestamp in the LLM prompt, but accepts the LLM's returned tool
arguments unchanged. An LLM can therefore omit, corrupt, or replace
`submittedAt` with another syntactically valid timestamp. Existing validation
checks parseability, not equality with persisted session evidence.

Missing or invalid source timestamps are already rejected upstream, but
`FinancialReportContextResolver` collapses timestamp-specific validation into
`FINANCIAL_REPORT_SUMMARY_INVALID`. That makes the failure less explicit in
preflight and audit events.

## Goals

- Treat persisted `FinancialReportContext.SubmittedAt` as the only trusted
  timestamp for controlled Data-tool calls.
- Ensure LLM and deterministic proposals produce the same exact timestamp,
  including original offset and round-trip precision.
- Never substitute execution time, session creation time, or current time.
- Surface missing or invalid persisted timestamps with stable, specific codes.
- Audit canonicalization and source-validation failures without recording
  untrusted timestamp values.
- Prove preservation from persisted context through planning and execution.

## Non-goals

- Canonicalizing `sessionId`, report name, amount, transaction count, or Legal
  tool arguments.
- Changing the tool approval policy, tool catalog, or plan deduplication rules.
- Changing how valid report timestamps are captured or edited in the frontend.
- Adding a generic trusted-argument framework before another field needs it.
- Replacing activity-event timestamps; those correctly represent event time.

## Trusted Data Flow

The source of truth remains:

```text
ContextJson.financialReport.submittedAt
  -> FinancialReportContextResolver
  -> FinancialReportContext.SubmittedAt
  -> PlannerAgent
  -> ToolPlanProposalInput.SubmittedAt
  -> proposal service
  -> approved tool plan
  -> ControlledToolExecutor
  -> DataAgent FinancialReportContext.SubmittedAt
```

`DateTimeOffset` is serialized with the invariant round-trip `"O"` format.
The original offset is preserved; equality by instant alone is insufficient for
the serialization assertions.

## LLM Proposal Canonicalization

`SemanticKernelToolPlanProposalService` will canonicalize parsed proposals
before normal tool validation and before the plan can be persisted or executed.

For every `data.analyze_transactions` call it will:

1. Inspect all argument keys equivalent to `submittedAt` after trimming and
   case-insensitive comparison.
2. Classify the untrusted arguments as exact, missing, mismatched, malformed,
   or duplicated.
3. Remove every equivalent key.
4. Insert one canonical `submittedAt` key whose value comes from
   `ToolPlanProposalInput.SubmittedAt.ToString("O", InvariantCulture)`.

This applies independently to every proposed Data call. Legal-only calls and
all unrelated arguments remain unchanged.

The deterministic proposal service keeps its existing direct construction from
the typed input. Invalid LLM output still uses the deterministic fallback, which
must preserve the same timestamp.

Canonicalization is preferred over rejecting a usable LLM plan because the
timestamp is trusted contextual evidence, not an LLM decision. It prevents an
invented value without turning a correct tool choice into a fallback.

## Validation and Audit Behavior

### Persisted source timestamp

The resolver will preserve timestamp-specific validation outcomes:

| Condition | Stable code | Behavior |
|---|---|---|
| Property absent or null | `SUBMITTED_AT_REQUIRED` | Block preflight and Planner |
| Default, malformed, or unsupported JSON value | `SUBMITTED_AT_INVALID` | Block preflight and Planner |

Other invalid report-summary fields retain the existing generic
`FINANCIAL_REPORT_SUMMARY_INVALID` result.

`AnalysisOrchestratorService` continues to publish
`financial_report_context_invalid`, but its message will reflect the specific
timestamp error returned by the resolver. No submitted timestamp value is
included in the event.

### LLM canonicalization

When the LLM timestamp is not already exact, the proposal service writes one
structured log entry with:

- `SessionId`
- canonical tool name
- `FailureCode = TOOL_PLAN_SUBMITTED_AT_CANONICALIZED`
- a stable reason: `missing`, `mismatch`, `malformed`, or `duplicate`

The log excludes the proposed value, prompt, report amounts, and raw LLM
payload. An exact timestamp requires no corrective log.

## Error Handling

- Missing persisted evidence fails before proposal generation.
- `DateTimeOffset.MinValue` remains invalid source evidence.
- Missing, malformed, or valid-but-different LLM values are replaced from the
  trusted input.
- Duplicate casing or whitespace variants are collapsed to one canonical key.
- Cancellation behavior remains unchanged.
- Parser failures continue through the existing deterministic fallback.
- No code path introduced by this change calls `DateTimeOffset.UtcNow`.

## Tests

### Proposal services

`SemanticKernelToolPlanProposalServiceTests` will cover exact, missing,
mismatched, malformed, case/whitespace-variant, and duplicate timestamps. It
will also cover multiple Data calls, Legal-only calls, safe structured logging,
and invalid-output fallback.

`DeterministicToolPlanProposalServiceTests` retains the exact invariant
round-trip assertion and verifies the same trusted input used by fallback.

### Source validation and audit

Resolver and preflight tests will distinguish required from invalid timestamps.
Orchestrator tests will verify Planner is not invoked, the session fails, and
the audit event contains only the stable specific message.

### End-to-end preservation

An integration test will start from persisted `ContextJson`, run the real LLM
proposal adapter with a fake chat response containing an invented timestamp,
approve and execute the Data call, and assert that `DataAgent` receives the
original timestamp and offset. Existing deterministic two-session coverage
continues to prove reload and session isolation.

### Regression commands

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~SemanticKernelToolPlanProposalServiceTests|FullyQualifiedName~DeterministicToolPlanProposalServiceTests|FullyQualifiedName~ControlledToolExecutorTests" --verbosity minimal
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~PlannerAgentTests|FullyQualifiedName~FinancialReportContextResolverTests|FullyQualifiedName~AnalysisSessionStartPreflightValidatorTests|FullyQualifiedName~AnalysisOrchestratorContextTests|FullyQualifiedName~ProductionLikeWorkflowE2ETests" --verbosity minimal
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --verbosity minimal
dotnet build backend/Orchestration.slnx --no-restore --verbosity minimal
git diff --check
```

## Acceptance Mapping

| Issue criterion | Design coverage |
|---|---|
| `SubmittedAt` is typed proposal input | Existing contract retained and asserted |
| LLM and deterministic proposals receive persisted timestamp | Prompt input plus post-parse canonicalization and deterministic direct construction |
| No production current-time fallback | Trusted input only; scan and tests |
| Missing timestamp explicit and audited | Specific resolver/preflight codes plus safe orchestrator event |
| End-to-end preservation | Persisted context through LLM plan and controlled Data execution |

