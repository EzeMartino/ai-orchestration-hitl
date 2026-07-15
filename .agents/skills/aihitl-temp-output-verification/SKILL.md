---
name: aihitl-temp-output-verification
description: Use when ai-orchestration-hitl builds or tests report MSB3026, MSB3027, locked Orchestration DLLs, or Python-backed tests stop resolving python-agents after changing OutputPath.
---

# AIHITL Temp Output Verification

## Overview

Separate locked-output or post-redirect Python path failures from real build/test failures without disrupting the running app.

## Workflow

1. Resolve the active checkout/worktree root with `git rev-parse --show-toplevel`; validate its `backend` and `python-agents` directories. Never hard-code the canonical checkout. Record the exact failing target, filter, and command.
2. Preserve the failing target/filter; never broaden them. Narrow only when task scope permits.
3. Select the applicable remediation:
   - **Lock/copy branch:** For `MSB3026`, `MSB3027`, copy retries, or locked `Orchestration.*.dll`, assign a unique `$outputPath` under `$env:TEMP`. Rerun the intended target with `"-p:OutputPath=$outputPath" -m:1 -nr:false`. Do not stop AppHost, Visual Studio, or API processes first.
   - **Python-resolution branch:** When a Python-backed test stops resolving `python-agents` after output redirection, temporarily set `ORCHESTRATION_TEST_PYTHON_HOME` to `Join-Path $repoRoot 'python-agents'`. Preserve its prior process value or unset state; after the rerun, restore it and propagate the native test exit code.
4. Preserve AppHost unless teardown is required; smoke-test it separately when live proof matters.
5. After remediation, leave this workflow. Diagnose remaining failures normally. If neither branch matches, do not use this skill.

## PowerShell example

Combined path for a Python-backed target that needs redirected output:

```powershell
$repoRoot = & git rev-parse --show-toplevel 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($repoRoot)) { throw 'Current directory is not inside a Git worktree.' }
$repoRoot = $repoRoot.Trim()
if (-not (Test-Path -LiteralPath (Join-Path $repoRoot 'backend') -PathType Container) -or -not (Test-Path -LiteralPath (Join-Path $repoRoot 'python-agents') -PathType Container)) { throw 'Current Git root is not ai-orchestration-hitl.' }
$pythonHomeWasSet = Test-Path Env:ORCHESTRATION_TEST_PYTHON_HOME
$previousPythonHome = $env:ORCHESTRATION_TEST_PYTHON_HOME
$outputPath = Join-Path $env:TEMP ("aihitl-test-bin-" + [guid]::NewGuid().ToString('N'))
$testExitCode = 1
Push-Location -LiteralPath $repoRoot
try {
    $env:ORCHESTRATION_TEST_PYTHON_HOME = Join-Path $repoRoot 'python-agents'
    & dotnet test .\backend\Orchestration.Tests\Orchestration.Tests.csproj --filter 'FullyQualifiedName~CSnakesFinancialAnalysisServiceTests' "-p:OutputPath=$outputPath" -m:1 -nr:false
    $testExitCode = $LASTEXITCODE
} finally {
    if ($pythonHomeWasSet) {
        $env:ORCHESTRATION_TEST_PYTHON_HOME = $previousPythonHome
    } else {
        Remove-Item Env:ORCHESTRATION_TEST_PYTHON_HOME -ErrorAction SilentlyContinue
    }
    Pop-Location
}
$global:LASTEXITCODE = $testExitCode
if ($testExitCode -ne 0) { throw "dotnet test failed with exit code $testExitCode." }
```

## Quick reference

| Entry condition | Remediation |
|---|---|
| `MSB3026`, `MSB3027`, copy retry, locked DLL | Unique temp output plus `-m:1 -nr:false`; preserve target/filter |
| Python resolution fails after redirect | Set source-root environment variable; rerun same target/filter once |
| Neither condition | Stop; diagnose normally |
| Running app needed for proof | Preserve AppHost; verify live behavior separately |

## Common mistakes and stop conditions

| Mistake | Correction or stop condition |
|---|---|
| Hard-coding the canonical checkout | Resolve and validate the active worktree root |
| Reusing stale output | Generate one unique `$outputPath` per run |
| Broadening target or filter | Preserve them; narrow only when task scope permits |
| Killing processes before classification | Preserve them unless teardown is explicitly required |
| Declaring a Python path failure real before remediation | Set the source root and rerun the same target once |
| Continuing after the applicable remediation | Stop this workflow; diagnose the remaining failure normally |
