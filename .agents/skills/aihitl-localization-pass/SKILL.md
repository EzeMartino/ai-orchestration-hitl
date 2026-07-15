---
name: aihitl-localization-pass
description: Use when ai-orchestration-hitl needs Spanish localization of user-visible warnings, errors, fallbacks, activity messages, financial-analysis output, or persisted copy across Python, backend, and frontend.
---

# AIHITL Localization Pass

Localize user-visible copy end to end. Preserve contracts; prove changed producers through consumers and focused tests.

## Contract vs copy

| Preserve exactly | Localize when user-visible |
|---|---|
| IDs, error codes, tool names, JSON keys | Warnings, errors, fallbacks, activity messages |
| Event types, protocol values, enum/status values | Summaries, evidence, labels, validation text |
| Log property names and structured fields | Planner/legal/tool rejection reasons surfaced to users |

Do not require zero English globally. Never run unbounded replacement. Classify English in contracts, logs, fixtures, prompts, citations, and technical terms instead of translating blindly.

## Workflow

1. Resolve the active worktree, inspect its diff, and fix scope.
2. Map scoped producers and consumers **before editing**. Use CodeGraph first (`codegraph_context`, then targeted search/callers/impact) for structural flow; use `rg` afterward for literal copy, historical variants, config, and tests. Map: producer -> mapper/contract -> frontend consumer/formatter -> exact-string tests. Inventory is incomplete until each applicable layer is classified.

| Layer | Required check |
|---|---|
| Python | Financial-analysis/anomaly summaries, evidence, warnings, failures, and focused tests |
| CSnakes/backend mapping | Python result mapping, services/plugins, API fallbacks/responses, activity output |
| Planner/legal/tools | Validation/execution errors, `RejectedToolCall.Reason`, legal/MCP messages and fallbacks |
| Frontend | API wrappers, render/fallback helpers, financial warnings, visible statuses and labels |
| Persisted history | Formatters that normalize legacy English warnings without rewriting stored data |

3. Translate only classified copy; preserve placeholders/interpolation. Change each producer with exact-string expectations. For persisted warnings, accept old English and current Spanish inputs while rendering Spanish.
4. Review the map. Confirm Python and CSnakes paths were not inferred from backend/frontend coverage.
5. Verify fresh, focused evidence:
   - run focused Python tests for every changed Python producer;
   - run filtered .NET tests for changed mappers/services/reasons;
   - run affected frontend Node tests and the frontend build;
   - run `git diff --check`, inspect the final diff, and scan changed user-visible surfaces for mixed language.

If .NET verification reports `MSB3026`, `MSB3027`, DLL locks, or copy retry noise, **REQUIRED SUB-SKILL:** use `$aihitl-temp-output-verification`; preserve the intended target/filter.

## Stop conditions and common mistakes

| Stop or mistake | Response |
|---|---|
| Unsure whether text is contract or copy | Stop; trace serialization and consumers before editing. |
| Inventory omits Python, CSnakes mapping, or their tests | Keep mapping; backend/frontend alone is incomplete. |
| Broad replacement or global zero-English goal | Narrow to classified user-visible surfaces. |
| IDs/codes/tool names/keys/status values changed | Revert contract changes; translate at render/message boundary. |
| Tests updated without their producer, or vice versa | Pair them, then rerun the focused slice. |
| Final scan finds mixed user-visible language | Trace each hit through producer, persistence, and formatter; fix or classify explicitly. |
