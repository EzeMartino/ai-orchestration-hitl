# Persisted Financial Report Context Design

**Issue:** [#3 — Replace hardcoded financial report context with persisted session data](https://github.com/EzeMartino/ai-orchestration-hitl/issues/3)

**Status:** Approved for implementation planning

## Problem

`PlannerAgent.BuildReportContext` currently creates a synthetic report name, total amount, and transaction count. Only the session identifier and creation timestamp come from persisted state. Those invented values flow into DataAgent, LegalAgent, Planner reasoning, Semantic Kernel prompts, and controlled tool calls.

The ingestion APIs already persist structured financial metrics per session, but none of their JSON, CSV, or multipart contracts carries the report-level values required by `FinancialReportContext`. Missing data is therefore hidden instead of rejected.

## Goals

- Persist an explicit report summary for every analysis session.
- Build `FinancialReportContext` only from persisted session state.
- Propagate one resolved context unchanged through Planner, Data, Legal, reasoning, and tool calls.
- Reject missing or invalid report summaries with stable, safe error codes.
- Preserve session isolation and the existing JSON, CSV, and PDF ingestion workflows.
- Preserve the computed `FinancialAnalysisContext` enrichment performed after DataAgent finishes.

## Non-goals

- Deriving total amount or transaction count from financial metrics.
- Adding report-summary columns to `AnalysisSession`.
- Replacing structured financial metrics or their extraction pipeline.
- Changing risk thresholds or business-risk calculation.
- Supporting old PDF review drafts by inventing missing summary values.

## Persisted Contract

Add two records in the shared Application layer:

```csharp
public sealed record FinancialReportSummaryInput(
    string? ReportName,
    decimal? TotalAmount,
    int? TransactionCount,
    DateTimeOffset? SubmittedAt);

public sealed record FinancialReportSummary(
    string ReportName,
    decimal TotalAmount,
    int TransactionCount,
    DateTimeOffset SubmittedAt);
```

Nullable input values distinguish omitted JSON/form values from valid zero values. The normalized persisted record is non-nullable.

The session JSON gains a separate root property:

```json
{
  "financialReport": {
    "reportName": "balance-sheet-2025.pdf",
    "totalAmount": 842350.75,
    "transactionCount": 187,
    "submittedAt": "2026-07-12T18:30:00Z"
  },
  "structuredFinancialMetrics": {
    "documentId": "balance-sheet-2025",
    "metrics": []
  }
}
```

`financialReport` remains independent from calculated `financialAnalysis`. The orchestrator may append analysis results without overwriting either input block.

## Input Surfaces

Every existing ingestion mode carries the same explicit summary:

- JSON paste and JSON files add `reportSummary` to `StructuredFinancialMetricsInput`.
- CSV paste adds `reportSummary` to `StructuredFinancialMetricsCsvInput`.
- CSV and PDF multipart uploads add `ReportName`, nullable `TotalAmount`, nullable `TransactionCount`, and nullable `SubmittedAt` form fields.
- PDF ingestion requests and review-draft payloads carry the normalized summary.

New positional record parameters are optional trailing parameters so source compatibility is retained while validation is introduced. Existing clients and persisted sessions deserialize successfully but cannot start analysis until they provide a valid summary.

The frontend renders one shared report-summary form above the JSON/CSV/file mode selector. The same state is submitted for every mode. Returned context displays the persisted values so operators can verify provenance before starting the session.

## Validation

Input validation normalizes the report name and validates:

- `ReportName`: required after trimming.
- `TotalAmount`: required and greater than or equal to zero.
- `TransactionCount`: required and greater than or equal to zero.
- `SubmittedAt`: required, non-default, and valid ISO-8601 input at API boundaries.

Stable save-validation codes:

- `REPORT_SUMMARY_REQUIRED`
- `REPORT_NAME_REQUIRED`
- `TOTAL_AMOUNT_REQUIRED`
- `TOTAL_AMOUNT_INVALID`
- `TRANSACTION_COUNT_REQUIRED`
- `TRANSACTION_COUNT_INVALID`
- `SUBMITTED_AT_REQUIRED`
- `SUBMITTED_AT_INVALID`

Invalid saves follow the existing financial-metrics convention: return `IsValid=false`, include validation issues, and do not mutate persisted session context.

Start preflight uses the same resolver and returns:

- `FINANCIAL_REPORT_SUMMARY_REQUIRED` when the root block is absent.
- `FINANCIAL_REPORT_SUMMARY_INVALID` when JSON or normalized values are invalid.

The summary is required for every Planner run, independently of `DataAgent.RequireSessionFinancialMetrics`, because both legacy and structured analysis paths consume `FinancialReportContext`.

## Resolution and Execution Flow

Introduce one `IFinancialReportContextResolver` in the Application layer. It accepts an `AnalysisSession`, parses `ContextJson`, validates `financialReport`, and returns either a `FinancialReportContext` or a typed failure containing a safe code and message.

Flow:

1. An ingestion endpoint validates metrics plus report summary.
2. The session service persists `financialReport` and `structuredFinancialMetrics` in one `SetContext` update.
3. Start preflight resolves the report and blocks invalid sessions before workflow transition.
4. `AnalysisOrchestratorService` resolves again as a defense at the service boundary.
5. If resolution fails there, the orchestrator publishes a safe failure activity, marks the session failed, persists it, and does not invoke Planner.
6. The orchestrator passes the resolved `FinancialReportContext` explicitly to `IPlannerAgent.RunAsync`.
7. Planner passes the same context to DataAgent and then preserves its report fields when adding `dataResult.FinancialAnalysis` for LegalAgent.
8. Planner reasoning and tool-plan inputs copy fields from that context.

`PlannerAgent.BuildReportContext` is removed. Production Planner code contains no amount, count, report-name, or timestamp fallback.

## Tool-call Traceability

`ToolPlanProposalInput` adds `SubmittedAt`. Both proposal implementations use the persisted timestamp:

- Deterministic proposal serializes `input.SubmittedAt`; it no longer calls `DateTimeOffset.UtcNow` for the tool argument.
- Semantic Kernel prompt includes the same timestamp.
- `ControlledToolExecutor` reconstructs the report from those exact approved arguments.

This produces an exact chain from persisted JSON to proposal audit, approved arguments, executor input, and DataAgent input.

## PDF Review Drafts

Accepted PDF ingestion persists the summary together with accepted metrics.

Review-required ingestion stores the summary in `FinancialMetricsExtractionDraftPayload`. The payload schema version increases from 1 to 2. The confirmation request also carries an explicit summary, pre-populated from a version 2 draft, so the operator can correct report metadata before confirmation. Confirmation persists the reviewed metrics and confirmed summary atomically.

Version 1 drafts contain no trustworthy summary. Their confirmation UI starts with empty summary fields and requires the operator to supply them. Confirmation without those values returns `FINANCIAL_REPORT_SUMMARY_REQUIRED`. No synthetic upgrade is allowed. This is a JSON contract change only and requires no EF Core migration.

## Context Merge Rules

All context writers must preserve unrelated root properties:

- Saving metrics replaces only `financialReport` and `structuredFinancialMetrics` after full validation succeeds.
- Completing analysis replaces only calculated analysis properties.
- Invalid input leaves the entire prior context unchanged.
- Updates are scoped by session identifier and authenticated user at API boundaries.

## Error and Audit Behavior

User-facing errors describe missing or invalid fields without exposing raw JSON or exception details. Activity events include the session identifier through existing event correlation and use stable failure codes.

No agent runs when the persisted summary is missing or invalid. A technical contract failure must never be interpreted as a business-risk result.

## Testing Strategy

### Contract and persistence

- Accept real positive values and valid zero values.
- Reject missing, blank, negative, malformed, and default values.
- Round-trip exact values through JSON paste, CSV paste, JSON file, CSV file, accepted PDF, and reviewed PDF.
- Verify invalid input does not overwrite previously valid context.
- Verify schema-version-1 PDF drafts require explicit remediation.

### Execution

- Assert Planner receives the persisted report with no fallback.
- Capture DataAgent, LegalAgent, reasoning, semantic proposal, deterministic proposal, and controlled-executor inputs.
- Assert exact equality of session ID, report name, total amount, transaction count, and submitted timestamp across every consumer.
- Assert financial-analysis enrichment does not change report-summary fields.
- Assert missing/invalid context blocks preflight and the orchestrator's defense marks failure without invoking Planner.

### Isolation

- Persist distinct summaries for two sessions and assert no cross-session reads.
- Assert one authenticated user cannot read, write, or start another user's summary.
- Extend production-like workflow tests to verify final context retains the original report summary.

### Frontend

- Validate all shared summary fields before submission.
- Submit identical summary state through JSON, CSV, and file modes.
- Render backend validation/preflight failures.
- Reset or reload summary state when the selected session changes.

## Compatibility and Rollout

- Existing API payloads still deserialize because the new input member is optional, but validation rejects omission explicitly.
- Existing session JSON remains readable; start is blocked until a valid summary is saved.
- Existing draft rows remain readable; version 1 cannot be confirmed without explicit summary remediation.
- No database migration is required.
- README examples and frontend templates must include `reportSummary`.

## Acceptance Mapping

- No hardcoded defaults: `BuildReportContext` removed; resolver is the only construction path.
- Persisted values: all required fields originate from `ContextJson.financialReport`.
- Explicit missing-input state: save validation, preflight, and orchestrator defense use stable codes.
- Identical values: one resolved object feeds Planner, Data, Legal, reasoning, and tools.
- Coverage: contract, invalid input, propagation, PDF review, and session/user isolation tests.
