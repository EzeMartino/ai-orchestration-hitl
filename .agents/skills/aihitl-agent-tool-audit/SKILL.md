---
name: aihitl-agent-tool-audit
description: Use when reviewing ai-orchestration-hitl Planner, Legal, or Data tools, registrations, schemas, LLM proposal eligibility, automatic execution, HITL approval, ownership, safety, or capability gaps.
---

# AIHITL Agent Tool Audit

## Core rule

Reviews/diagnoses default read-only; explicit no-change binds. Change the repository only for an explicit implementation request within its scope. Each tool/fallback remains unverified pending its record.

## Workflow

1. Fix scope/mode. With a declared budget, record start/absolute deadline; set absolute cutoff timestamp = start + 75% of budget, reserving 25% for synthesis/delivery. Otherwise use scope completion.
2. Use `codegraph_context` → `codegraph_search` → `codegraph_callers` → `codegraph_impact` → source. Literal-search only config/names/prompts/logs/tests outside CodeGraph.
3. Record each canonical tool; split native/deterministic, MCP transports, substitutes, degraded paths, and fallbacks when reachability differs.
4. Evidence starts `Confirmed`, `Absent`, `Unknown`, `N/A`, or `Inference`, plus `path:line`. `Absent` names scope; `N/A`/`Inference` explains why. Repeat paths; no global list.

## Per-tool record

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

`wired` never substitutes for these separate lifecycle fields.

## Runtime status

Use exactly one: `runtime-observed` (runtime proof), `statically reachable` (non-test path), `test-only` (tests alone), `unreferenced in searched scope` (none in named scope), or `unknown` (insufficient evidence). A static caller never proves a tool was used; at most `statically reachable`.

## Availability

| Evidence | Availability |
|---|---|
| Required wiring/policy allows use under stated config | `Available/conditional` |
| Implementation/equivalent or required connection absent | `Unavailable` |
| Connected, but policy/approval safety prevents use | `Blocked/unsafe` |
| Evidence incomplete | `Unverified` |
| Only a test gap | Preserve code-derived availability; never change it automatically |

## Gap precedence

Evaluate in order; primary is earliest causal. Add secondary gaps only when independently evidenced, never merely downstream.

| Condition | Classification |
|---|---|
| No implementation or equivalent | Missing capability |
| Implementation exists; required connection is absent | Disconnected tool |
| Connection exists; rules are unsafe or inconsistent | Policy gap |
| Connected outcome cannot be audited | Observability gap |
| Existing behavior lacks focused coverage | Test gap |

Recommend changes only when requested and supported by record evidence. Label inference.

## Stop

With a budget, stop at cutoff and deliver `Unknown` by deadline. Without one, stop at scoped completion.

## Common mistakes

- Collapsing MCP tools, wrappers, and fallbacks.
- Recommending from `Unknown` or inference.
