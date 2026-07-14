---
name: aihitl-temp-output-verification
description: Use when ai-orchestration-hitl builds or tests report MSB3026, MSB3027, locked Orchestration DLLs, or Python-backed tests stop resolving python-agents after changing OutputPath.
---

# AIHITL Temp Output Verification

## Overview

Separate locked-output noise from real build/test failures without disrupting the running app. Use the smallest proof that reproduces the original target.

## Workflow

1. Confirm the current directory is the `ai-orchestration-hitl` repository root. Record the exact failing solution, project, test filter, and command.
2. Classify the failure before changing processes. Continue only for `MSB3026`, `MSB3027`, copy retries, or locked `Orchestration.*.dll` output. Do not kill AppHost, Visual Studio, or API processes first.
3. Select the narrowest test filter or project build that proves the change. Create a unique output directory under `$env:TEMP` for this run.
4. Rerun the same target with `-p:OutputPath=<unique-temp-path> -m:1 -nr:false`.
5. For Python-backed tests, set `ORCHESTRATION_TEST_PYTHON_HOME` to the repository's `python-agents` source directory before rerunning.
6. Preserve AppHost unless teardown is explicitly required by the verification. If live-app proof matters, keep it running and smoke-test it separately.
7. Once lock/copy noise is gone, stop using this skill. Diagnose any remaining compile, test, configuration, or product failure normally.

## PowerShell example

```powershell
Set-Location C:\Repositories\ai-orchestration-hitl
$repo = (Get-Location).Path
if (-not (Test-Path (Join-Path $repo 'python-agents'))) { throw 'Run from the ai-orchestration-hitl repository root.' }
$env:ORCHESTRATION_TEST_PYTHON_HOME = Join-Path $repo 'python-agents'
$output = Join-Path $env:TEMP ("aihitl-test-bin-" + [guid]::NewGuid().ToString('N'))
dotnet test .\backend\Orchestration.Tests\Orchestration.Tests.csproj --filter 'FullyQualifiedName~CSnakesFinancialAnalysisServiceTests' "-p:OutputPath=$output" -m:1 -nr:false
```

## Quick reference

| Signal | Action |
|---|---|
| `MSB3026`, `MSB3027`, copy retry, locked DLL | Redirect to a unique temp `OutputPath` |
| Broad suite | Narrow to the smallest relevant filter first |
| Python source/fixture resolution fails after redirect | Set `ORCHESTRATION_TEST_PYTHON_HOME` to `<repo>\python-agents` |
| Running app needed for proof | Preserve AppHost; verify live behavior separately |
| Lock noise cleared | Leave this workflow and debug the remaining failure |

## Common mistakes and stop conditions

- Reusing a stale shared output directory: generate a unique path per run.
- Changing the target or filter while redirecting output: rerun the exact failing target first.
- Stopping processes before classifying the error: only teardown when explicitly required.
- Treating every failure as a lock: stop immediately if no lock/copy symptom exists.
- Continuing after lock errors disappear: remaining failures require normal diagnosis.
