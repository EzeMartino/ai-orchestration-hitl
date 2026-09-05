# Ponytail corrections design

The user approved implementing the corrections in the 2026-09-05 ponytail report on a new branch. The report is the design input; this document records implementation decisions for that approved scope.

## Behavior

1. Reject incompatible monetary units/currencies when deriving ratios or comparing periods. Preserve supported input conventions; normalize percent-valued reported ratios to fractions. Missing operands remain unknown; quick-ratio sums require all operands. Zero confidence remains zero.
2. Describe the legacy anomaly calculation as fixed-threshold heuristics. Do not claim z-scores, temporal velocity, or statistical significance without corresponding inputs.
3. Protect session status with optimistic concurrency. State and the decision/start event must persist atomically; only the winning transition may be audited. Notify SignalR after persistence, and isolate transport failures from committed workflow state.
4. Record a safe failed state when an in-process execution error prevents completion. A conflict must not fail the execution owned by another request. Process-crash recovery is outside this change.
5. Use neutral final-status copy. Never infer human approval/rejection from Completed/Failed alone. Ignore stale asynchronous responses and events from other sessions, including their dependent refreshes and flags.
6. Remove fabricated fixed confidence from persisted financial projections and its UI display when unavailable.
7. Reuse JSON/CSV save coordination. Keep internal tool execution results typed instead of serializing and re-parsing them. Preserve legacy JSON decoding only at compatibility boundaries, all financial/legal safeguards, audit projection, tool allowlist and deny-by-default behavior.
8. Call the legacy CSnakes implementation directly; remove the deterministic Semantic Kernel wrapper if no other production caller needs it. Keep Semantic Kernel for actual LLM-backed features.

## Constraints

- Branch: `codex/ponytail-corrections`; current checkout. Preserve existing backend/AGENTS.md and frontend/AGENTS.md edits and exclude them from commits.
- Keep public HTTP and stored evidence contracts backward compatible. Confidence may be null when unavailable; historic snapshots remain readable.
- No new production dependencies, broad architecture rewrite, financial advice, or weakening of HITL/legal evidence safeguards.
- No new comments or docblocks in backend/frontend code. Preserve unrelated existing comments.
- Test defects before fixing them. Reuse Python unittest, Node tests and xUnit. Test PostgreSQL concurrency when a local server is available, with explicit skipping when unavailable.
- Keep commits local. No push, PR, deployment or merge requested.

## Alternatives and decision

A full workflow engine and a universal financial-unit converter add unnecessary scope. Merely hiding warnings or changing UI copy would leave the calculation and persistence defects. Use narrow corrections at the existing calculation, transition and response boundaries, then remove the identified internal duplication.

## Acceptance

The reported numerical examples are rejected or yield correctly normalized evidence; simultaneous start/decision attempts have one winner; committed events match committed transitions; injected runtime failure leaves a terminal failed session; session B remains selected when an older response for A arrives; foreign-session events are ignored. Current Python/frontend/backend suites and frontend build pass. Controlled tool execution preserves existing security and evidence behavior without its internal JSON round trip.
