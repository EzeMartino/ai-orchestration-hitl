---
name: aihitl-agent-tool-audit
description: Use when reviewing ai-orchestration-hitl Planner, Legal, or Data tools, registrations, schemas, LLM proposal eligibility, automatic execution, HITL approval, ownership, safety, or capability gaps.
---

# AIHITL Agent Tool Audit

## Core rule

Reviews/diagnoses default read-only; explicit no-change binds. Change the repository only for an explicit implementation request within its scope. Each tool/fallback remains unverified pending its record.

## Workflow

1. Fix scope and mode. With a declared budget, record start, absolute deadline, and absolute evidence cutoff before tracing. Otherwise use scope completion.
2. Use `codegraph_context`, then `codegraph_search`, `codegraph_callers`, and `codegraph_impact`; read source afterward. Literal-search only config keys, canonical names, prompts, logs, or test text CodeGraph cannot model.
3. Record each canonical tool; split native/deterministic paths, MCP tools/transports, substitutes, degraded paths, and fallbacks when reachability differs.
4. Prefix evidence `Confirmed`, `Absent`, `Unknown`, `N/A`, or `Inference`, then `path:line`. `Absent` names scope; `N/A`/`Inference` explains why. Repeat paths; no global evidence list.

## Per-tool record

Repeat this vertical record. Keep every field and its evidence separate.

| Field | Evidence |
|---|---|
| Agent / canonical tool | |
| Purpose | |
| Mode / fallback | |
| Definition | |
| Input schema | |
| Output schema | |
| DI registration | |
| Plugin registration | |
| Catalog / allowlist | |
| LLM proposal visibility | |
| Execution mode / reachability | |
| Executor wiring / policy | |
| HITL approval | |
| Owner | |
| Safety | |
| Implementation | |
| Focused tests | |
| Used / unused runtime status | |
| Availability | |
| Primary / secondary gap | |

Do not collapse definition, registration, proposal, execution, approval, owner, safety, implementation, or tests into a `wired` claim.

## Runtime status

Use exactly one: `runtime-observed` (runtime evidence), `statically reachable` (non-test source path), `test-only` (only tests reference it), `unreferenced in searched scope` (none in named scope), or `unknown` (insufficient evidence). A static caller never proves a tool was used; it supports at most `statically reachable`.

## Gap precedence

Evaluate in order; primary is the earliest causal gap. Add secondary gaps only when independently evidenced, never when merely downstream.

| Condition | Classification |
|---|---|
| No implementation or equivalent | Missing capability |
| Implementation exists; required connection is absent | Disconnected tool |
| Connection exists; rules are unsafe or inconsistent | Policy gap |
| Connected outcome cannot be audited | Observability gap |
| Existing behavior lacks focused coverage | Test gap |

Recommend changes only when requested and supported by record evidence. Label inference.

## Stop

With a budget, stop evidence collection at the recorded cutoff and deliver `Unknown` fields by the deadline. Without a deadline, stop at scoped completion; never invent a percentage cutoff.

## Common mistakes

- Treating a static caller as runtime use.
- Collapsing MCP tools, wrappers, and fallbacks.
- Calling a missing connection a policy gap.
- Recommending from `Unknown` or inference.
