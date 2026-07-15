---
name: aihitl-agent-tool-audit
description: Use when reviewing ai-orchestration-hitl Planner, Legal, or Data tools, registrations, schemas, LLM proposal eligibility, automatic execution, HITL approval, ownership, safety, or capability gaps.
---

# AIHITL Agent Tool Audit

## Core rule

Audit read-only. Do not edit product files, registrations, configuration, or tests. Treat each tool and fallback as unverified until its row proves every applicable stage.

## Workflow

1. Fix the requested agents, tools, and time budget. Reserve 25% for synthesis.
2. Use `codegraph_context`, then `codegraph_search`, `codegraph_callers`, and `codegraph_impact`. Read focused source afterward. Use literal search only for config keys, canonical names, prompts, logs, or test text CodeGraph cannot model.
3. Trace each tool separately through definition/schema, DI, Semantic Kernel plugin registration, `PlannerToolCatalog`/allowlist, LLM proposal visibility, `ControlledToolExecutor` policy, HITL approval, implementation, and focused tests. Mark non-applicable stages.
4. Record owner and safety constraints. Separate native/deterministic implementations, MCP tools, transports, substitutes, degraded paths, and fallbacks when availability differs.
5. Prefix every evidence cell with `Confirmed`, `Absent`, `Unknown`, `N/A`, or `Inference`, then repository `path:line`. For `Absent`, name searched scope; for `N/A` or `Inference`, explain why. Never use a global evidence list instead.

## Inventory template

| Agent / canonical tool | Mode / fallback | Definition + schema evidence | DI evidence | Plugin evidence | Catalog / allowlist evidence | LLM proposal evidence | Automatic execution / executor policy evidence | HITL evidence | Owner / safety evidence | Implementation evidence | Test evidence | Availability | Gap |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|

Keep paths in every row, including repeated paths. Do not merge registration, proposal, execution, or approval into one `wired` claim.

## Decision table

| Evidence pattern | Availability | Gap classification |
|---|---|---|
| All applicable stages confirmed | Available, or conditional when flags/HITL apply | None |
| Implementation exists but a required wiring stage is absent | Unavailable | Disconnected tool |
| Requested outcome has no implementation or equivalent | Unavailable | Missing capability |
| Wiring exists but proposal, executor, approval, ownership, or safety rules are missing or inconsistent | Blocked/unsafe | Policy gap |
| Runtime path or outcome cannot be audited | Unverified | Observability gap |
| Behavior lacks focused coverage | Preserve code-derived availability; reduce confidence | Test gap |

Apply multiple classifications only when each has evidence. Recommend changes only when requested and supported by row evidence. Label inference.

## Stop

At 75% of the time budget, stop discovery. Deliver a partial matrix on time with unresolved cells `Unknown`; never overrun for one more trace. Otherwise stop when every scoped row is auditable and each gap follows the decision table.

## Common mistakes

- Calling a class definition `available` without registrations and execution policy.
- Treating an MCP server tool, client wrapper, and deterministic fallback as one capability.
- Inferring LLM eligibility from implementation or automatic execution from proposal visibility.
- Listing implementation/tests globally instead of per tool row.
- Turning absence of evidence into a recommendation or confirmed gap.
