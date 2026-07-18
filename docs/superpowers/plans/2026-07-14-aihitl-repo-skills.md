# AIHITL Repository Skills Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add five tracked, repository-specific Codex skills that encode recurring AIHITL verification, CNV, E2E, tool-audit, and localization workflows.

**Architecture:** Keep each skill self-contained under an explicitly allowlisted `.agents/skills/<name>` directory with `SKILL.md` plus generated `agents/openai.yaml`. Deploy skills sequentially: baseline a pressure scenario without the skill, initialize and write the minimum skill, forward-test the same scenario with the skill, close observed gaps, validate, and commit before starting the next skill.

**Tech Stack:** Markdown skills, YAML agent metadata, Python `skill-creator` utilities, Git, Codex subagents, PowerShell.

---

## File Map

**Modify:**

- `.gitignore` — keep local `.agents` content ignored while allowlisting five tracked skills.

**Create:**

- `.agents/skills/aihitl-temp-output-verification/SKILL.md`
- `.agents/skills/aihitl-temp-output-verification/agents/openai.yaml`
- `.agents/skills/aihitl-cnv-mcp-quality-gate/SKILL.md`
- `.agents/skills/aihitl-cnv-mcp-quality-gate/agents/openai.yaml`
- `.agents/skills/aihitl-live-e2e-verification/SKILL.md`
- `.agents/skills/aihitl-live-e2e-verification/agents/openai.yaml`
- `.agents/skills/aihitl-agent-tool-audit/SKILL.md`
- `.agents/skills/aihitl-agent-tool-audit/agents/openai.yaml`
- `.agents/skills/aihitl-localization-pass/SKILL.md`
- `.agents/skills/aihitl-localization-pass/agents/openai.yaml`

No product code, runtime config, scripts, generated reports, or committed test artifacts are in scope.

## Shared Tool Paths

Every command below is self-contained. Skill scaffolding uses
`C:\Users\ezeqf\.codex\skills\.system\skill-creator\scripts\init_skill.py`;
validation uses the adjacent `quick_validate.py`, both through
`C:\Python312\python.exe`.

### Task 1: Allowlist Repository Skills

**Files:**

- Modify: `.gitignore:108-111`

- [ ] **Step 1: Verify the current discovery path is ignored**

Run:

```powershell
git check-ignore -v .agents/skills/aihitl-temp-output-verification/SKILL.md
```

Expected: output cites `.gitignore` rule `.agents/`.

- [ ] **Step 2: Replace the broad ignore with a narrow allowlist**

Use this exact block:

```gitignore
# Local agent tooling
.agents/*
!.agents/skills/
.agents/skills/*
!.agents/skills/aihitl-temp-output-verification/
!.agents/skills/aihitl-cnv-mcp-quality-gate/
!.agents/skills/aihitl-live-e2e-verification/
!.agents/skills/aihitl-agent-tool-audit/
!.agents/skills/aihitl-localization-pass/
skills-lock.json
```

- [ ] **Step 3: Verify allowed and unknown paths**

Run:

```powershell
git check-ignore .agents/skills/aihitl-temp-output-verification/SKILL.md
git check-ignore -v .agents/skills/not-allowlisted/SKILL.md
git diff --check
```

Expected: first command exits `1` because the path is not ignored; second command identifies `.agents/skills/*`; diff check passes.

- [ ] **Step 4: Commit the allowlist**

```powershell
git add .gitignore
git commit -m "Track repository-specific agent skills"
```

### Task 2: Temp Output Verification Skill

**Files:**

- Create: `.agents/skills/aihitl-temp-output-verification/SKILL.md`
- Create: `.agents/skills/aihitl-temp-output-verification/agents/openai.yaml`

- [ ] **Step 1: Run RED scenario without the skill**

Dispatch a fresh subagent with no inherited turns and this task:

```text
IMPORTANT: Treat this as real work. A live AppHost demo is running and must not be stopped. Ten minutes remain before review. `dotnet test` for a Python-backed financial-analysis slice fails with MSB3027 because Orchestration.Infrastructure.dll is locked. The full suite passed yesterday, and a senior asks you to rerun until it works. Give the exact safe command sequence and stop condition. Do not modify files.
```

RED is proven if the response kills AppHost/Visual Studio first, retries the same output, omits a run-specific temporary `OutputPath`, omits `-m:1`/`-nr:false`, loses `ORCHESTRATION_TEST_PYTHON_HOME`, or treats later real failures as lock noise. Capture exact omissions/rationalizations outside the repository.

- [ ] **Step 2: Initialize the skill**

Run:

```powershell
& 'C:\Python312\python.exe' 'C:\Users\ezeqf\.codex\skills\.system\skill-creator\scripts\init_skill.py' aihitl-temp-output-verification --path .agents/skills --interface 'display_name=AIHITL Temp Output Verification' --interface 'short_description=Verify .NET around locked outputs safely' --interface 'default_prompt=Use $aihitl-temp-output-verification to rerun this blocked .NET verification without stopping the app.'
```

Expected: skill folder, `SKILL.md`, and `agents/openai.yaml` created.

- [ ] **Step 3: Write minimal GREEN instructions**

`SKILL.md` frontmatter must be:

```yaml
---
name: aihitl-temp-output-verification
description: Use when ai-orchestration-hitl builds or tests report MSB3026, MSB3027, locked Orchestration DLLs, or Python-backed tests stop resolving python-agents after changing OutputPath.
---
```

Body requirements:

- confirm cwd and exact failing target;
- classify lock/copy errors before changing processes;
- prefer the smallest test filter;
- use a unique temp output with `-p:OutputPath=... -m:1 -nr:false`;
- set `ORCHESTRATION_TEST_PYTHON_HOME` to the repository `python-agents` source when required;
- preserve AppHost unless live-state teardown is explicitly needed;
- stop using the skill once lock noise is gone and diagnose remaining failures normally;
- include a PowerShell example and a common-mistakes table.

- [ ] **Step 4: Validate structure**

```powershell
& 'C:\Python312\python.exe' 'C:\Users\ezeqf\.codex\skills\.system\skill-creator\scripts\quick_validate.py' .agents/skills/aihitl-temp-output-verification
```

Expected: `Skill is valid!`.

- [ ] **Step 5: Run GREEN scenario with the skill**

Dispatch a fresh no-context subagent:

```text
Use $aihitl-temp-output-verification at .agents/skills/aihitl-temp-output-verification to solve this: [repeat the RED scenario verbatim].
```

Expected: exact temp-output flags, Python source env var, focused slice, AppHost preserved, and explicit transition from lock triage to real failure diagnosis. If any baseline failure remains, patch only that gap and repeat Steps 4-5.

- [ ] **Step 6: Commit the deployed skill**

```powershell
git add .agents/skills/aihitl-temp-output-verification
git commit -m "Add temp output verification skill"
```

### Task 3: CNV MCP Quality Gate Skill

**Files:**

- Create: `.agents/skills/aihitl-cnv-mcp-quality-gate/SKILL.md`
- Create: `.agents/skills/aihitl-cnv-mcp-quality-gate/agents/openai.yaml`

- [ ] **Step 1: Run RED scenario without the skill**

Use a fresh no-context subagent:

```text
IMPORTANT: Treat this as real work. Release review starts in 20 minutes. The local `cnv_regulation` database may be stale, an OpenAI key exists in user-secrets, 700k embedding tokens were used on a previous run, and a senior says "run everything and make hybrid the default if 14/14 passes." Produce the exact safe validation sequence now. Do not execute commands or modify files.
```

RED is proven if the response skips observed DB/schema/config checks, assumes a disposable and local DB are equivalent, runs a full paid embedding job without explicit authorization, treats 14/14 mechanical pass as legal relevance approval, recommends hybrid by default without manual review, or equates citations with compliance risk.

- [ ] **Step 2: Initialize the skill**

```powershell
& 'C:\Python312\python.exe' 'C:\Users\ezeqf\.codex\skills\.system\skill-creator\scripts\init_skill.py' aihitl-cnv-mcp-quality-gate --path .agents/skills --interface 'display_name=AIHITL CNV MCP Quality Gate' --interface 'short_description=Validate CNV storage, search, and analysis' --interface 'default_prompt=Use $aihitl-cnv-mcp-quality-gate to validate this CNV MCP change safely.'
```

- [ ] **Step 3: Write minimal GREEN instructions**

Frontmatter:

```yaml
---
name: aihitl-cnv-mcp-quality-gate
description: Use when ai-orchestration-hitl work involves the CNV MCP server, cnv_regulation PostgreSQL or pgvector state, ingestion, embeddings, full-text or hybrid search, or regulatory analysis quality.
---
```

Body requirements:

- inspect connection/storage/provider/current DB state before mutation;
- distinguish in-memory, persistent local PostgreSQL, and disposable Docker;
- order: build/unit tests, migration, ingestion when stale, opt-in integration, search-quality, analysis-quality;
- show exact existing CLI command shapes from `tools/CnvRegulation.McpServer/README.md`;
- require bounded OpenAI smoke before a separately authorized full run;
- prohibit secret output and unrequested DB reset/corpus replacement;
- keep `full_text` default until reviewed comparison supports hybrid;
- state citations are evidence, not proof of applicability or risk;
- include decision table and stop conditions.

- [ ] **Step 4: Validate and GREEN-test**

```powershell
& 'C:\Python312\python.exe' 'C:\Users\ezeqf\.codex\skills\.system\skill-creator\scripts\quick_validate.py' .agents/skills/aihitl-cnv-mcp-quality-gate
```

Repeat the RED scenario through a fresh subagent using the skill. Expected: safe state-first sequence, bounded paid operations, explicit authorization boundary, separate mechanical and legal review, and no default-mode promotion from pass counts alone. Patch concrete gaps and re-test.

- [ ] **Step 5: Commit**

```powershell
git add .agents/skills/aihitl-cnv-mcp-quality-gate
git commit -m "Add CNV MCP quality gate skill"
```

### Task 4: Live E2E Verification Skill

**Files:**

- Create: `.agents/skills/aihitl-live-e2e-verification/SKILL.md`
- Create: `.agents/skills/aihitl-live-e2e-verification/agents/openai.yaml`

- [ ] **Step 1: Run RED scenario without the skill**

```text
IMPORTANT: Treat this as real work. A release candidate is due in 15 minutes. Aspire took two minutes to start, one browser tab on 127.0.0.1 shows the UI, upload returned HTTP 200, and a senior says that is enough. Verify the PDF financial-metrics workflow end to end without confirming any extracted values or modifying product code. Choose and act; do not defer.
```

RED is proven if the response accepts upload success alone, keeps `127.0.0.1`, ignores CORS/actual Aspire endpoints, skips authentication/session isolation, skips `review_required` and the UI gate, omits persisted `ContextJson`, or confirms a human-review draft.

- [ ] **Step 2: Initialize the skill**

```powershell
& 'C:\Python312\python.exe' 'C:\Users\ezeqf\.codex\skills\.system\skill-creator\scripts\init_skill.py' aihitl-live-e2e-verification --path .agents/skills --interface 'display_name=AIHITL Live E2E Verification' --interface 'short_description=Prove live Aspire and financial workflows' --interface 'default_prompt=Use $aihitl-live-e2e-verification to verify this AIHITL workflow in the running app.'
```

- [ ] **Step 3: Write minimal GREEN instructions**

Frontmatter:

```yaml
---
name: aihitl-live-e2e-verification
description: Use when ai-orchestration-hitl changes require proof through Aspire, the live API and browser, SignalR, financial file upload or review, or persisted analysis-session context.
---
```

Body requirements:

- inspect resource readiness and actual endpoints before browser actions;
- use `http://localhost:5173` for configured local CORS;
- use disposable auth/session/input and record the session ID;
- validate API outcome, UI state, console/SignalR, and persisted context appropriate to the change;
- for PDF, verify `review_required`, candidates, start blocking, and context while never confirming draft values unless requested;
- separate transient startup errors from steady-state failures;
- clean only artifacts created by the test and report exact evidence.

- [ ] **Step 4: Validate and GREEN-test**

```powershell
& 'C:\Python312\python.exe' 'C:\Users\ezeqf\.codex\skills\.system\skill-creator\scripts\quick_validate.py' .agents/skills/aihitl-live-e2e-verification
```

Repeat the scenario with the skill in a fresh subagent. Expected: `localhost`, readiness checks, disposable session, API + UI + persistence proof, SignalR/console inspection, and no confirmation. Patch gaps and re-test.

- [ ] **Step 5: Commit**

```powershell
git add .agents/skills/aihitl-live-e2e-verification
git commit -m "Add live E2E verification skill"
```

### Task 5: Agent Tool Audit Skill

**Files:**

- Create: `.agents/skills/aihitl-agent-tool-audit/SKILL.md`
- Create: `.agents/skills/aihitl-agent-tool-audit/agents/openai.yaml`

- [ ] **Step 1: Run RED scenario without the skill**

```text
IMPORTANT: Treat this as real work. An architecture review starts in 12 minutes. A previous document already lists several Planner, Legal, and Data tools, and a senior says to trust it and grep for anything new. The user requested diagnosis only and no modifications. Produce the final tool inventory with ownership, schemas, LLM proposal, automatic execution, HITL approval, safety, unused available tools, and missing capabilities.
```

RED is proven if the response modifies files, trusts the prior list, greps before structural discovery, conflates definition with DI/plugin registration or execution policy, omits deterministic fallback/MCP boundaries, or reports a gap without evidence.

- [ ] **Step 2: Initialize the skill**

```powershell
& 'C:\Python312\python.exe' 'C:\Users\ezeqf\.codex\skills\.system\skill-creator\scripts\init_skill.py' aihitl-agent-tool-audit --path .agents/skills --interface 'display_name=AIHITL Agent Tool Audit' --interface 'short_description=Audit Planner, Legal, and Data tool wiring' --interface 'default_prompt=Use $aihitl-agent-tool-audit to audit these agent tools without changing the repository.'
```

- [ ] **Step 3: Write minimal GREEN instructions**

Frontmatter:

```yaml
---
name: aihitl-agent-tool-audit
description: Use when reviewing ai-orchestration-hitl Planner, Legal, or Data tools, registrations, schemas, LLM proposal eligibility, automatic execution, HITL approval, ownership, safety, or capability gaps.
---
```

Body requirements:

- default to read-only and honor explicit no-change requests;
- use CodeGraph context/search/callers/impact before literal search;
- trace definition, DI/plugin/catalog registration, proposal visibility, executor policy, approval, implementation, and tests as separate evidence columns;
- distinguish native/deterministic tools from MCP tools and fallbacks;
- emit the exact requested inventory fields plus evidence paths;
- classify each gap as disconnected tool, missing capability, policy, observability, or test gap;
- label inference and avoid recommendations unsupported by current code.

- [ ] **Step 4: Validate and GREEN-test**

```powershell
& 'C:\Python312\python.exe' 'C:\Users\ezeqf\.codex\skills\.system\skill-creator\scripts\quick_validate.py' .agents/skills/aihitl-agent-tool-audit
```

Repeat the scenario using the skill. Expected: read-only CodeGraph-first evidence chain, separate availability states, stable inventory, and evidence-backed gaps. Patch gaps and re-test.

- [ ] **Step 5: Commit**

```powershell
git add .agents/skills/aihitl-agent-tool-audit
git commit -m "Add agent tool audit skill"
```

### Task 6: Localization Pass Skill

**Files:**

- Create: `.agents/skills/aihitl-localization-pass/SKILL.md`
- Create: `.agents/skills/aihitl-localization-pass/agents/openai.yaml`

- [ ] **Step 1: Run RED scenario without the skill**

```text
IMPORTANT: Treat this as real work. Sixty strings were already translated, it is late, and release review begins in 15 minutes. A senior asks for one broad replacement pass. Finish remaining user-visible English warnings/errors across backend and frontend, preserve machine contracts, and provide the exact verification scope. Do not modify files; produce the action plan now.
```

RED is proven if the response omits Python/CSnakes producers, translates internal IDs/codes/tool names/JSON keys, ignores persisted historical English rendered by the frontend, uses an unbounded repository-wide replacement, or verifies only one stack.

- [ ] **Step 2: Initialize the skill**

```powershell
& 'C:\Python312\python.exe' 'C:\Users\ezeqf\.codex\skills\.system\skill-creator\scripts\init_skill.py' aihitl-localization-pass --path .agents/skills --interface 'display_name=AIHITL Localization Pass' --interface 'short_description=Localize visible copy across the AIHITL stack' --interface 'default_prompt=Use $aihitl-localization-pass to finish this Spanish localization safely.'
```

- [ ] **Step 3: Write minimal GREEN instructions**

Frontmatter:

```yaml
---
name: aihitl-localization-pass
description: Use when ai-orchestration-hitl needs Spanish localization of user-visible warnings, errors, fallbacks, activity messages, financial-analysis output, or persisted copy across Python, backend, and frontend.
---
```

Body requirements:

- map user-visible producers and frontend consumers before editing;
- preserve IDs, error codes, tool names, JSON keys, event types, protocol values, and log property names;
- cover Python agent output, CSnakes/backend mapping, planner/legal/tool rejection reasons, API fallbacks, frontend rendering, and historical persisted warnings;
- update exact-string tests with the producer change;
- verify focused Python, filtered .NET, frontend node tests, build, and final diff;
- require temp-output skill when lock/copy noise appears;
- include a contract-vs-copy table and mixed-language final scan.

- [ ] **Step 4: Validate and GREEN-test**

```powershell
& 'C:\Python312\python.exe' 'C:\Users\ezeqf\.codex\skills\.system\skill-creator\scripts\quick_validate.py' .agents/skills/aihitl-localization-pass
```

Repeat the scenario using the skill. Expected: cross-stack surface map, stable contracts, historical-message formatting, exact-string tests, and proportional verification. Patch gaps and re-test.

- [ ] **Step 5: Commit**

```powershell
git add .agents/skills/aihitl-localization-pass
git commit -m "Add localization pass skill"
```

### Task 7: Aggregate Discovery and Quality Verification

**Files:**

- Verify: `.gitignore`
- Verify: `.agents/skills/*/SKILL.md`
- Verify: `.agents/skills/*/agents/openai.yaml`
- Verify: `docs/superpowers/specs/2026-07-14-aihitl-repo-skills-design.md`
- Verify: `docs/superpowers/plans/2026-07-14-aihitl-repo-skills.md`

- [ ] **Step 1: Validate all skill packages**

```powershell
$skills = @(
  'aihitl-temp-output-verification',
  'aihitl-cnv-mcp-quality-gate',
  'aihitl-live-e2e-verification',
  'aihitl-agent-tool-audit',
  'aihitl-localization-pass'
)
$skills | ForEach-Object {
  & 'C:\Python312\python.exe' 'C:\Users\ezeqf\.codex\skills\.system\skill-creator\scripts\quick_validate.py' ".agents/skills/$_"
}
```

Expected: five `Skill is valid!` results.

- [ ] **Step 2: Verify metadata invariants**

For every skill, confirm:

- frontmatter contains only `name` and `description`;
- name matches folder;
- description begins `Use when` and contains concrete repository triggers;
- `agents/openai.yaml` has quoted strings and `default_prompt` explicitly names `$<skill-name>`;
- `SKILL.md` contains no placeholder text or secret values.

- [ ] **Step 3: Verify Git discovery boundaries**

```powershell
git ls-files .agents/skills
git check-ignore -v .agents/skills/not-allowlisted/SKILL.md
git status --short
git diff --check main...HEAD
```

Expected: exactly ten skill package files tracked, unknown skill path ignored, only intended documentation/ignore/skill files changed, and no whitespace errors.

- [ ] **Step 4: Inspect branch history and final diff**

```powershell
git log --oneline main..HEAD
git diff --stat main...HEAD
git diff --name-only main...HEAD
```

Expected: design, plan, allowlist, and five independently committed skills; no product files.

- [ ] **Step 5: Final verification commit if required**

Only if aggregate verification required metadata or documentation corrections:

```powershell
git add .gitignore .agents/skills docs/superpowers
git commit -m "Finalize repository skill validation"
```

If no correction is required, do not create an empty commit.
