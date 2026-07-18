# Repository Skills Design

**Date:** 2026-07-14
**Status:** Ready for user review
**Scope:** Five Codex skills specific to `ai-orchestration-hitl`

## Context

Recent work repeatedly crosses ASP.NET Core, Python, React, Aspire, PostgreSQL,
the CNV MCP server, and live browser verification. The generic skills already
cover debugging, TDD, worktrees, code review, and branch completion. The
remaining friction is repository-specific knowledge that has repeatedly been
rediscovered: locked .NET outputs, CNV database preparation, live financial
workflow proof, agent-tool inventory rules, and cross-stack localization.

The repository currently ignores `.agents/` as local tooling. The main checkout
contains local skills there, but none are versioned. These five skills should be
discoverable from the repository and travel with every clone without committing
the rest of a developer's local skill catalog.

## Goals

- Encode the five recurring workflows as concise, discoverable skills.
- Keep commands and safety boundaries tied to this repository.
- Make each skill independently triggerable and usable.
- Version only the five repository skills, not all local agent tooling.
- Validate every skill with RED/GREEN forward tests and metadata checks.

## Non-goals

- Replacing generic debugging, TDD, planning, review, or GitHub skills.
- Adding product code, migrations, tests, or runtime dependencies.
- Automatically spending OpenAI tokens, resetting databases, confirming
  extracted financial metrics, or changing production-like data.
- Committing generated reports, test outputs, virtual environments, or local
  secrets.

## Placement Options

### A. Tracked repository skills under `.agents/skills` — selected

Change `.gitignore` from ignoring all of `.agents/` to ignoring its contents,
then explicitly allowlist only the five skill directories. Benefits: automatic
repository discovery, version control, reviewable changes, and no global
pollution. Local skills outside the allowlist remain ignored.

### B. Personal skills under `$CODEX_HOME/skills`

Immediately discoverable but machine-local, not reviewable with the repository,
and easy to drift from the code they describe.

### C. Tracked skills under a neutral tools directory

Keeps `.agents` ignored, but requires a separate installation or synchronization
step and can leave the installed copy stale.

Option A best matches repository-specific behavior and the isolated worktree
requested for this change.

## Skill Package Shape

Each skill will contain only:

```text
.agents/skills/<skill-name>/
  SKILL.md
  agents/openai.yaml
```

No scripts are planned initially. These workflows depend on live state and
require judgment around costs, database state, running processes, or user data.
Hard-coded automation would make unsafe assumptions. `SKILL.md` will stay below
500 words where practical; commands remain inline because each workflow has a
small, stable command surface.

`agents/openai.yaml` will provide a display name, short description, and default
prompt generated from the final skill. Only `name` and `description` will appear
in `SKILL.md` frontmatter.

## Skill Designs

### `aihitl-temp-output-verification`

Trigger on `MSB3026`, `MSB3027`, locked `Orchestration.*.dll`, or Python-backed
tests that stop resolving `python-agents` after a temporary `OutputPath` change.

Core behavior:

1. Confirm repository and failing target.
2. Separate lock/copy noise from real compilation or test failures.
3. Re-run the smallest relevant slice with a run-specific temp `OutputPath`,
   `-m:1`, and `-nr:false`.
4. Set `ORCHESTRATION_TEST_PYTHON_HOME` when Python-backed tests need it.
5. Preserve a running AppHost and use live proof when stopping it is unnecessary.

This promotes the already proven memory skill into versioned repository tooling.

### `aihitl-cnv-mcp-quality-gate`

Trigger on CNV MCP ingestion, migration, PostgreSQL/pgvector, embeddings,
full-text/hybrid search comparison, or analysis-quality validation work.

Core behavior:

1. Inspect current storage/config before mutating it.
2. Distinguish in-memory, local PostgreSQL, and disposable Docker validation.
3. Run migration and ingestion only when required by observed state.
4. Validate unit tests before opt-in PostgreSQL integration tests.
5. Run `validate-search-quality` and `validate-analysis-quality` with explicit
   storage/mode settings.
6. Treat OpenAI embeddings as a paid operation: start with a bounded smoke run,
   require explicit authorization for a full run, and never expose keys.
7. Keep `full_text` as the safe default unless reviewed evidence supports a
   hybrid change; retrieved citations alone never prove compliance risk.

The skill will reference the existing CLI commands rather than duplicate their
implementation.

### `aihitl-live-e2e-verification`

Trigger when backend and frontend behavior must be proved through Aspire, a
browser, SignalR, financial upload/review, or persisted session context.

Core behavior:

1. Confirm Docker/AppHost readiness and actual resource endpoints.
2. Use `http://localhost:5173`, not `127.0.0.1`, for configured CORS behavior.
3. Authenticate with disposable test data and create an isolated analysis
   session.
4. Exercise only the flow relevant to the change; use synthetic input.
5. For PDF ingestion, verify response outcome, review gate, UI rendering, and
   persisted `ContextJson`; do not confirm extracted metrics unless requested.
6. Inspect browser console, API response, and SignalR state before attributing a
   failure to frontend logic.
7. Report concrete identifiers and outcomes, then leave unrelated sessions and
   runtime state untouched.

### `aihitl-agent-tool-audit`

Trigger when reviewing Planner, Legal, or Data agent tools, registrations,
schemas, proposal/execution policy, HITL approval, ownership, or missing
capabilities.

Core behavior:

1. Stay read-only unless implementation is explicitly requested.
2. Use CodeGraph first for definitions, registrations, callers, and impact;
   use literal search only for prompt strings, IDs, and configuration.
3. Separate a tool that exists from one registered, exposed to the LLM,
   automatically executable, or approval-gated.
4. Trace deterministic fallbacks and MCP tools separately.
5. Return a stable inventory containing name, purpose, owner, input/output
   schemas, proposal/execution rules, approval, and safety notes.
6. Classify gaps as disconnected existing tools, missing capability, policy
   weakness, observability gap, or test gap.

### `aihitl-localization-pass`

Trigger on Spanish localization of warnings, errors, fallbacks, activity copy,
or persisted messages across Python, backend, and frontend.

Core behavior:

1. Identify user-visible surfaces before broad searching.
2. Preserve internal IDs, codes, tool names, JSON keys, and protocol values.
3. Update producer strings and frontend formatting for historical persisted
   English messages.
4. Patch exact-string assertions with the runtime copy.
5. Verify the smallest affected Python, .NET, and frontend slices; use the temp
   output skill if live binaries are locked.
6. Scan the final diff for accidental identifier translation or mixed-language
   output.

## Safety Boundaries

- Skills never broaden the user's requested mutation scope.
- Database reset, corpus replacement, paid embeddings, and external publication
  require explicit authorization unless already requested.
- Live E2E uses synthetic/disposable data and never silently confirms a human
  review decision.
- Tool audit defaults to read-only.
- Localization changes visible copy only; stable machine contracts remain
  unchanged.
- Existing dirty worktrees and unrelated changes are preserved.

## Validation Strategy

Skills will be created and deployed sequentially. For each skill:

1. Run a realistic baseline scenario without the skill and record the missing or
   unsafe behavior (RED).
2. Initialize the skill with the official `skill-creator` scripts.
3. Write the minimum instructions that address the observed gaps (GREEN).
4. Generate `agents/openai.yaml` from the completed skill.
5. Run `quick_validate.py`.
6. Repeat the scenario with the skill in a fresh subagent and inspect whether it
   follows the intended workflow.
7. Close concrete gaps and re-run validation before starting the next skill.

Forward tests will be read-only or use bounded local checks. They will not reset
databases, invoke paid full embedding runs, start long-lived app resources, or
modify product code.

## Repository Verification

After all five skills:

- validate all five skill folders;
- confirm every `openai.yaml` matches its `SKILL.md`;
- verify `.gitignore` exposes only the allowlisted directories;
- scan for placeholders, absolute secrets, and generated artifacts;
- run `git diff --check`;
- re-run targeted product baselines only if repository files outside agent
  tooling were changed unexpectedly.

## Acceptance Criteria

- Exactly five repository skill directories are tracked and discoverable.
- Other `.agents` content remains ignored.
- Every description begins with concrete `Use when...` triggers.
- Each skill documents stop conditions and safety boundaries.
- Every skill passes structural validation and a RED/GREEN forward test.
- No product source, runtime configuration, user data, or generated report is
  changed.
