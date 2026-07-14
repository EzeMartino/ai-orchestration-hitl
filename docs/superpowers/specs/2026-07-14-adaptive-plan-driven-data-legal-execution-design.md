# Adaptive Plan-Driven Data-to-Legal Execution Design

**Date:** 2026-07-14
**Status:** Design approved; written specification pending review
**Issue:** [#6 — Make plan-driven execution adaptive to Data evidence before legal retrieval](https://github.com/EzeMartino/ai-orchestration-hitl/issues/6)

## Context

The deterministic Planner path already executes `DataAgent` before
`LegalAgent` and passes the completed `FinancialAnalysisContext` through an
enriched `FinancialReportContext`. The Legal path then derives up to four CNV
queries from validated financial risk signals, aggregates cited evidence, and
records `LegalQueryStrategyAudit` metadata.

Plan-driven mode does not preserve that data flow. It proposes, validates, and
executes Data and Legal calls as one flat batch. The deterministic proposal
supplies the fixed Legal query `"agentes"`; Semantic Kernel can likewise
propose Legal arguments before Data evidence exists. Although the controlled
executor awaits calls sequentially, it does not transfer Data output into the
Legal call. The Legal handler calls the CNV MCP client directly, bypassing
`LegalAgent`, `FinancialAnalysisLegalCnvQueryStrategy`, Legal AI review, and
query-strategy audit metadata. Data and Legal results are mapped only after the
whole batch returns.

If either mapped result is missing, Planner currently reruns both deterministic
agents. That duplicates successful work, obscures provenance, and provides no
stage boundary for partial failure or cancellation.

## Goals

- Make plan-driven execution a deterministic Data-to-Legal pipeline.
- Ensure Legal query generation consumes the completed Data financial-analysis
  context before any CNV retrieval begins.
- Preserve the original normalized, validated, policy-approved tool plan.
- Treat one approved Legal call as authorization for one composite Legal review
  containing up to four deterministic CNV subqueries.
- Continue safely when Data evidence is degraded or unavailable.
- Identify every generic Legal fallback with durable, structured metadata.
- Preserve successful stage outputs when another stage fails.
- Give final Planner reasoning the exact final Data and Legal results.
- Preserve cancellation, workflow, approval, allowlist, and read-only-tool
  safety boundaries.

## Non-goals

- Adding a generic DAG, scheduler, retry engine, or resumable stage framework.
- Allowing an LLM to invent, rewrite, or select post-Data Legal queries.
- Deriving Legal risk from raw ratios or comparisons when validated risk
  signals are unavailable.
- Adding workflow-transition, approval, operational-finance, or legal-conclusion
  tools.
- Persisting tool output JSON, model prompts, chain-of-thought, uncited snippets,
  or complete financial-analysis payloads in audit metadata.
- Redesigning Shadow mode or the frontend.
- Retrying a failed stage automatically.

## Chosen Architecture

Planner remains the stage coordinator. No new generic stage-runner abstraction
or DAG contract is introduced.

Plan-driven execution follows this sequence:

```text
initial proposal
  -> normalize once
  -> validate once
  -> policy once
  -> publish proposal/validation audit
  -> execute approved Data stage
  -> map final Data result
  -> classify Data evidence state
  -> execute approved Legal stage with trusted runtime context
       -> derive 1-4 contextual queries, or one deterministic fallback query
       -> retrieve and aggregate cited CNV evidence
       -> record query strategy and per-query outcomes
  -> map final Legal result
  -> Planner reasoning from final Data + Legal outputs
  -> human-review decision
  -> Planner completion
```

The trusted catalog's `PlannerToolSatisfactionKind` determines stage
membership. Data always precedes Legal, regardless of LLM proposal order.
Unknown or prohibited tools remain rejected by the existing validator. The
coordinator never adds a new approved Planner call after validation.

The plan remains flat in persisted audit data. Stage ordering is an execution
rule derived from trusted catalog metadata, not an LLM-authored dependency
graph.

`ProposedCalls`, `ApprovedCalls`, and `RejectedCalls` preserve normalized plan
order. `ExecutedCalls` records actual canonical stage order, including skipped
or failed stage entries, so persisted execution audit cannot imply that Legal
ran before Data.

## Tool Proposal and Authorization

The initial plan continues to propose two capabilities when both are enabled:

- `data.analyze_transactions`
- `legal.search_cnv_regulation`

The Legal capability becomes a composite evidence-retrieval authorization. It
does not require a query, area, or other pre-Data search argument. Its catalog
description explains that deterministic runtime logic derives CNV queries from
completed Data evidence. Keeping the canonical tool name preserves existing
allowlists while removing the misleading pre-evidence query contract.

With no Legal arguments, normal plan deduplication collapses repeated Legal
proposals to one call. The original proposed, approved, and rejected call lists
remain immutable after validation. The one approved Legal call may execute up
to four internal queries, but those queries are suboperations recorded in
Legal audit metadata, not new Planner tool calls.

`ToolPlan` carries safe proposal provenance through normalization:

- `llm`
- `deterministic`
- `deterministic_fallback`

When Semantic Kernel output is invalid and the deterministic proposer supplies
the plan, a stable `proposalFallbackReason` records the failure class. Raw model
output and prompts are never recorded. `ToolPlanAuditResult` persists this
provenance alongside the unchanged call lists.

Allowed proposal fallback reasons are:

- `llm_response_invalid`
- `llm_request_failed`
- `llm_configuration_failed`

Cancellation is never classified as proposal fallback. If multiple failures
are observed inside one proposal attempt, the first terminal failure in call
order determines the single recorded reason.

## Runtime Execution Context

The controlled executor receives trusted runtime execution context in addition
to approved calls. The context contains the original `FinancialReportContext`,
the mapped Data result when available, and an explicit Data-evidence state. It
is constructed by Planner, never from LLM arguments.

Data execution remains unchanged: the approved Data call reconstructs the
report from validated canonical arguments and returns serialized
`DataAgentResult`.

After Data execution, Planner maps and classifies the result before invoking
the Legal stage. The Legal handler calls `ILegalAgent` with the original report
and the completed Data `FinancialAnalysisContext` when it is usable. It no
longer calls the CNV MCP client directly. `LegalAgent` therefore retains the
existing query strategy, multi-query retrieval, citation filtering,
deduplication, warnings, query audit, and Legal AI review.

The Legal review contract gains an explicit financial-analysis resolution mode
for this staged path. Plan-driven Legal execution uses `provided_only`: when
the completed Data stage supplies no usable context, Legal must not silently
load older session context from the database. Existing non-staged callers keep
their current `provided_or_persisted` behavior.

The executor serializes the aggregate `LegalAgentResult` as the Legal tool
output. `ToolExecutionResultMapper` deserializes that aggregate result rather
than reconstructing a reduced Legal result from the first raw MCP response.

## Data-Evidence Classification

Only `FinancialAnalysisContext.RiskSignals` can drive contextual Legal queries.
Planner and Legal use the following deterministic classification:

| Data condition | Legal behavior | Review behavior |
|---|---|---|
| Data succeeded; signals stage succeeded; one or more signals | Contextual queries | Existing risk rules apply |
| Data degraded; signals stage succeeded; one or more signals | Contextual queries from emitted signals only | Human review required |
| No financial-analysis context | Generic fallback | Human review required |
| Signals stage failed | Generic fallback | Human review required |
| No specific signals | Generic fallback | Human review required |
| Legacy or ambiguous execution metadata | Generic fallback | Human review required |
| Data tool failed or approved Data stage absent | Generic fallback | Human review required |
| Valid signals exist but none map to a supported Legal category | Generic fallback | Human review required |

Ratios, comparisons, summaries, warnings, and free text may explain audit
state, but cannot be promoted into a Legal risk signal. A degraded result can
drive contextual retrieval only when the signals stage itself succeeded and
emitted validated signals.

Classification uses one reason with this precedence:

1. Data tool failed.
2. Approved Data stage absent.
3. Ambiguous or legacy execution metadata.
4. Financial-analysis context missing.
5. Signals stage failed.
6. No specific signals.
7. Signals present but unsupported by the Legal mapping.
8. Contextual retrieval.

## Localized Failure Results

Stage failure no longer triggers a full deterministic rerun of both agents.
Each missing or failed stage receives an explicit safe aggregate result so
Planner reasoning and persistence remain structurally complete without
inventing evidence.

A failed or absent Data stage produces a `DataAgentResult` with:

- `HasAnomaly = false`
- `Severity = "Unknown"`
- safe failure summary and engine
- empty evidence
- no financial-analysis context
- `RequiresHumanReview = true`

A failed or absent Legal stage produces a `LegalAgentResult` with:

- `HasComplianceRisk = false`
- `RiskLevel = "Unknown"`
- safe failure summary and engine
- empty evidence
- a stable warning
- `RequiresHumanReview = true`

`LegalAgentResult` gains the backward-compatible optional
`RequiresHumanReview` field. Planner's final decision includes it together with
Data anomaly, Data review, and Legal compliance-risk signals. Uncertainty is
never represented as a fabricated anomaly or compliance finding.

An absent or rejected stage is never executed outside the approved call set.
Its safe result preserves the rejection boundary and forces human review.

## Legal Query Strategy and Audit

`FinancialAnalysisLegalCnvQueryStrategy` remains the only mapping from
financial risk signals to CNV queries. It retains high-severity ordering,
deduplication, and the four-query limit. When context is not usable, it emits
one deterministic general financial-reporting query.

The current warning/limitation-only query branch is removed. Warnings and
limitations remain audit context, but they cannot create or expand Legal
queries without a validated `FinancialRiskSignal`. If valid signals are present
but none map to a supported Legal category, the strategy emits the generic
fallback with reason `signals_unmapped`.

`LegalQueryStrategyAudit` becomes strongly descriptive and durable:

- `strategyVersion`
- `source`: `contextual` or `fallback`
- `fallbackReason`, when applicable
- Data tool status
- financial-analysis overall status, when available
- failed stage operation and allowlisted failure code, when available
- ordered query audit entries

Stable fallback reasons are:

- `data_tool_failed`
- `data_stage_absent`
- `financial_analysis_missing`
- `signals_stage_failed`
- `no_specific_signals`
- `signals_unmapped`
- `legacy_or_ambiguous_execution`

Each query audit entry contains:

- query index and total count
- deterministic query text
- regulation area
- derivation reason
- related signal names or codes
- execution status
- result count
- cited-evidence count

One failed CNV subquery does not discard successful cited evidence from other
subqueries. It adds a safe warning, records the failed query outcome, and forces
human review. Uncited results remain excluded from Legal evidence.

Legal aggregate outcomes are classified as follows:

| Condition | Aggregate result |
|---|---|
| At least one query completes, with or without cited results | Normal Legal result; cited count may be zero |
| Some queries fail and at least one completes | Partial Legal result; preserve successful citations and require review |
| Every query fails | Safe `Unknown` Legal result and review required |
| Legal agent throws or output cannot be mapped | Safe `Unknown` Legal result and review required |
| Queries complete but results are uncited | Normal no-cited-evidence result plus existing warning; do not invent risk |

## Activity Ordering

Plan-level audit events are published before execution. Per-call events are
published immediately after their stage result, producing this observable
order:

```text
tool_plan_proposed
tool_plan_validated
Data tool_call_executed | tool_call_failed
legal_cnv_queries_derived | legal_cnv_query_fallback_used
Legal tool_call_executed | tool_call_failed
planner_reasoning_completed | planner_reasoning_fallback_used
Planner agent_completed
```

Rejected-call events remain part of validation audit. Stage and query failures
use stable safe messages. Tool outputs and financial values are excluded from
activity messages.

The Legal derivation/fallback activity event is emitted only after the aggregate
Legal review completes. Cancellation during query retrieval therefore leaves
no Legal derivation, fallback, execution, reasoning, or completion event.

## Cancellation

`OperationCanceledException` always propagates. Cancellation does not create a
safe failure result, generic query fallback, deterministic retry, downstream
Legal call, Planner reasoning, approval decision, or completion event.

If cancellation occurs after Data completed, its already-published activity
event may remain as partial audit evidence. This feature does not add resumable
execution or persist a partial `ToolPlanAuditResult`; existing orchestrator
cancellation behavior remains authoritative.

## Persistence and Compatibility

`AnalysisOrchestratorService` continues to omit `ToolExecutionResult.OutputJson`
from `ContextJson`. It persists:

- proposal provenance and original tool-call audit
- final safe or successful Data result
- final safe or successful Legal result
- Legal query-strategy audit
- final Planner reasoning when execution was not cancelled

New record fields are optional/defaulted for backward JSON compatibility.
Existing internal IDs, canonical tool names, activity types, and workflow
statuses remain stable except for the new explicit Legal fallback activity
type. No frontend work is planned unless compile-time contract checks reveal a
typed consumer that must accept the additive audit fields.

Shadow mode keeps its current deterministic Data-to-Legal flow. It receives
regression coverage but no behavior redesign.

## Security and Safety Invariants

- LLM output remains advisory.
- Normalize, validate, and policy evaluation run once against the original
  plan.
- Only approved, allowlisted, read-only tools execute.
- LLM ordering cannot place Legal before Data.
- Internal Legal subqueries come only from deterministic strategy code.
- Internal subqueries cannot transition workflow, approve a session, move
  money, or issue a legal conclusion.
- Missing, rejected, or failed stages force human review rather than bypassing
  policy.
- No raw tool output, prompt, chain-of-thought, uncited snippet, or full
  financial-analysis JSON is persisted as audit metadata.

## Tests

### Planner and proposal services

- Data completes before approved Legal stage execution begins, even when the LLM
  returns Legal first.
- The Legal stage receives the exact mapped Data financial-analysis context.
- Planner reasoning receives the exact final Data and Legal aggregate results.
- Original proposed, approved, and rejected calls remain unchanged after
  staging.
- Invalid LLM output records `deterministic_fallback` and a stable safe reason.
- Rejected or missing stages are not executed and create safe review-required
  results.
- Workflow, approval, operational, and legal-conclusion calls remain rejected.

### Data classification and Legal execution

- Succeeded Data with signals produces contextual queries.
- Degraded Data with a successful signals stage uses only emitted signals and
  requires review.
- Failed signals, missing context, empty signals, legacy metadata, Data tool
  failure, and absent Data stage each produce the expected generic fallback
  reason.
- Valid but unsupported signals produce `signals_unmapped`; warnings and
  limitations alone never add Legal queries.
- One approved Legal call produces one to four strategy queries while remaining
  one Planner call.
- Legal receives `provided_only` resolution mode and never reloads stale session
  context in plan-driven execution.
- Partial query failure preserves successful citations, audits each outcome,
  and requires review.
- Total Legal failure produces a safe review-required aggregate result.
- The real mapper preserves `LegalAgentResult.QueryStrategy`, Legal review, and
  `RequiresHumanReview`.

### Cancellation and activity

- Cancellation during Data prevents Legal and reasoning.
- Cancellation between stages prevents Legal and reasoning.
- Cancellation during Legal prevents reasoning and completion.
- `OperationCanceledException` is never converted to fallback.
- Activity events have exact stage order and cardinality.
- No post-cancellation completion or approval mutation occurs.

### Integration and end to end

- Real Planner, controlled executor, mapper, Data agent, Legal agent, and fake
  CNV MCP client prove the completed Data context drives Legal requests.
- Plan-driven session persistence contains final Data, Legal, reasoning, plan
  provenance, and query audit without `OutputJson`.
- Generic fallback reason and per-query results survive a session reload.
- A hostile/reordered plan cannot mutate workflow or approval state.
- Existing Shadow mode behavior remains unchanged.

### Regression commands

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~PlannerAgentTests|FullyQualifiedName~DeterministicToolPlanProposalServiceTests|FullyQualifiedName~SemanticKernelToolPlanProposalServiceTests|FullyQualifiedName~ToolPlanNormalizerTests|FullyQualifiedName~ToolPlanValidatorTests" --verbosity minimal
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~ControlledToolExecutorTests|FullyQualifiedName~ToolExecutionResultMapperTests|FullyQualifiedName~FinancialAnalysisLegalCnvQueryStrategyTests|FullyQualifiedName~McpRegulatoryKnowledgeSourceTests" --verbosity minimal
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~AnalysisOrchestratorContextTests|FullyQualifiedName~ProductionLikeWorkflowE2ETests" --verbosity minimal
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --verbosity minimal
dotnet build backend/Orchestration.slnx --no-restore --verbosity minimal
git diff --check
```

## Acceptance Mapping

| Issue criterion | Design coverage |
|---|---|
| Legal query generation receives completed Data context | Staged execution maps Data before invoking aggregate Legal review with trusted runtime context |
| Staged dependencies without workflow/approval mutation | Trusted catalog ordering, one validation/policy pass, immutable approved calls, read-only composite Legal subqueries |
| Generic fallback queries explicitly identified | Structured Legal strategy source, stable fallback reason, Data status, and per-query outcomes |
| Planner reasoning receives final Data and Legal outputs | Successful or explicit safe aggregate results are finalized before reasoning |
| Staged success | Planner, executor, mapper, integration, activity-order, and persistence coverage |
| Partial failure | Degraded Data, failed Data, partial Legal query failure, total Legal failure, preserved successful evidence |
| Cancellation | Propagated cancellation with no fallback or downstream stages |
| Deterministic fallback | Proposal provenance plus deterministic generic Legal strategy and durable reason codes |
