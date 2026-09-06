# Containerization Progress

## Environment Detection
- [x] .NET version detection (API: `net10.0`; MCP: `net8.0`, verified from both project files)
- [x] Linux distribution selection (Ubuntu Noble from valid `mcr.microsoft.com/dotnet/*:10.0` tags; required `10.0-bookworm-slim` tags return manifest exit 1)

## Prerequisites and Artifact Paths
- [x] API target: `backend/Orchestration.Api/Orchestration.Api.csproj` (`net10.0`)
- [x] MCP target: `tools/CnvRegulation.McpServer/src/CnvRegulation.McpServer/CnvRegulation.McpServer.csproj` (`net8.0`, `linux-x64`, self-contained)
- [x] Python home: `/app/python/data_agent`; virtual environment: `/app/python/data_agent/.venv`
- [x] MCP command: `/app/mcp/CnvRegulation.McpServer`
- [x] HTTP binding: `http://0.0.0.0:10000`
- [x] Runtime packages: `curl`, `poppler-utils`, `tesseract-ocr`, `tesseract-ocr-eng`, `tesseract-ocr-spa`

## Failing-First Image Inspection

Commands defined and run before creating the Dockerfile. Expected RED: `ai-orchestration-hitl:local` is absent.

```powershell
docker run --rm --entrypoint /bin/sh ai-orchestration-hitl:local -c "test -x /app/mcp/CnvRegulation.McpServer"
docker run --rm --entrypoint /bin/sh ai-orchestration-hitl:local -c "test -f /app/python/data_agent/requirements.lock"
docker run --rm --entrypoint /bin/sh ai-orchestration-hitl:local -c "tesseract --version && pdftoppm -v && python3.12 --version"
docker run --rm --entrypoint /bin/sh ai-orchestration-hitl:local -c "test $(id -u) -ne 0"
```

- [x] RED inspection evidence recorded

## Configuration Changes
- [x] Application configuration verification for environment variable support (container `ASPNETCORE_URLS` and `Python__Home` configured)
- [x] NuGet package source configuration (not applicable: no `NuGet.config` in repository root)

## Containerization
- [x] Dockerfile creation
- [x] .dockerignore file creation
- [x] Build stage created with `mcr.microsoft.com/dotnet/sdk:10.0` (Ubuntu Noble; valid MCR tag)
- [x] csproj file(s) copied for package restore
- [x] NuGet.config copied if applicable (not applicable: repository root has no `NuGet.config`)
- [x] Runtime stage created with `mcr.microsoft.com/dotnet/aspnet:10.0` (Ubuntu Noble; valid MCR tag)
- [x] Non-root user configuration (`USER $APP_UID`; all copied runtime artifacts owned by `$APP_UID`)
- [x] Dependency handling (runtime OCR/PDF packages, self-contained MCP, staged Python 3.12 and locked venv)
- [x] Health check configuration (`/alive`, curl, required timing)
- [x] Special requirements implementation (MCP single file; CSnakes cache at `/home/app/.config/CSnakes`; no SDK/source copy into final)

## Verification
- [x] Review containerization settings and make sure that all requirements are met
- [x] Docker build success
- [x] Final image content and metadata inspection
- [x] Final history/filesystem excludes SDK, build sources, and secrets

## Quality Follow-Up: Context and Python Cache
- [x] RED proof: host Python bytecode was copied into the final image
- [x] RED proof: harmless local settings probe reached `/app/appsettings.Local.json`
- [x] Exclude local settings, Python caches, bytecode, and local tooling from build context
- [x] Prune only proven CPython build/test/object artifacts from the staged cache
- [x] GREEN proof: excluded context artifacts, pruned cache paths, and runtime imports

## Evidence

- RED inspection before Dockerfile: all four commands exited `125`; `ai-orchestration-hitl:local` was absent and Docker's attempted pull was denied.
- Base-tag investigation after first build failure: `aspnet:10.0-bookworm-slim`, `aspnet:10.0.7-bookworm-slim`, `sdk:10.0-bookworm-slim`, and `sdk:10.0.7-bookworm-slim` returned manifest exit `1`; `aspnet:10.0` and `sdk:10.0` returned `0`. The valid ASP.NET tag reports Ubuntu 24.04.4 (Noble) and `APP_UID=1654`.
- Python-stage investigation after the next build failure: the exact `CSnakes.Stage 1.2.1` command exited `150` because it requires `Microsoft.NETCore.App 9.0.0`, while SDK 10 contains only 10.0.10. `sdk:9.0` manifest inspection exited `0` and its runtime list includes `Microsoft.NETCore.App 9.0.18`; only the Python staging base uses SDK 9.
- API-publish investigation after the subsequent build: four CSnakes generator inputs were absent from `/src/python-agents/data_agent` (`anomaly_detection.py`, `document_markdown.py`, `financial_analysis.py`, `searchable_pdf.py`). The build stage had copied only `backend/` and MCP source, so it now copies `python-agents/data_agent/` before API publish; no source is copied from the build stage into final.
- Python runtime investigation: the root-created staged venv resolved `python3` to `/root/.config/CSnakes/...`, while the required cache copy was `/home/app/.config/CSnakes`; it therefore failed `python3.12 --version` with exit `127` (`Permission denied`). SDK 9 confirms `APP_UID=1654` and `app` has home `/home/app`. Python staging now prepares and owns `/app/python` and `/home/app`, then runs `setup-python` as `$APP_UID` with `HOME=/home/app`; the cache and absolute venv link are born at the required final path, without exposing `/root`.
- Final `docker build --progress=plain -t ai-orchestration-hitl:local .`: exit `0`. Build emitted existing package warning `NU1903` for `Microsoft.OpenApi 2.4.1`, plus non-fatal CSnakes terminfo symlink warnings; API and MCP publish both completed successfully.
- Final metadata inspection: exit `0`; `1654 {"10000/tcp":{}} ["CMD-SHELL","curl --fail --silent http://127.0.0.1:10000/alive || exit 1"]`.
- Final required content commands: MCP executable exit `0`; requirements lock exit `0`; OCR/PDF/Python command exit `0` (`tesseract 5.3.4`, `pdftoppm 24.02.0`, `Python 3.12.9`); non-root UID command exit `0`.
- Final filesystem/history inspection: exit `0`; .NET runtime is 10.0.10, `/usr/share/dotnet/sdk`, `/src`, `/tools`, `/app/.env`, and `/app/python/data_agent/.env` are absent; app-owned `/home/app/.config/CSnakes` is present and `/root/.config/CSnakes` is absent. History shows only final runtime base, runtime packages, and copied API/MCP/Python artifacts.
- Quality RED before Dockerfile follow-up: four host source bytecode files were copied byte-for-byte into `/app/python/data_agent/__pycache__` (SHA-256 matched for `anomaly_detection`, `document_markdown`, `financial_analysis`, and `searchable_pdf`). A harmless ignored probe containing `TASK8_LOCAL_SETTINGS_MUST_NOT_ENTER_IMAGE` was published as `/app/appsettings.Local.json` in `ai-orchestration-hitl:task8-probe-red`. The unpruned CSnakes cache measured `90M` for `python/build`, `32M` for installed stdlib `python3.12/test`, and contained `253` `.o` files.
- Quality GREEN: `.dockerignore` excludes source bytecode/cache, local Python tooling, and local appsettings variants. With the harmless probe still present on the host, final-image scans found no probe marker, `appsettings.Local.json`, `appsettings.*.local.json`, `.env`, source `__pycache__`, or source `.pyc`; the probe was then removed before commit. Exact cache targets `python/build` and `install/lib/python3.12/test` are absent and the cache has zero `.o` files. `/app/python/data_agent/.venv/bin/python` and `python3.12` are executable; native/project imports (`ssl`, `sqlite3`, `numpy`, `onnxruntime`, `PIL`, `pypdf`, `financial_analysis`) pass with `PYTHONPATH=/app/python/data_agent`, and `pip check` reports no broken requirements. The import test uses `PYTHONDONTWRITEBYTECODE=1` so it does not create transient bytecode in its writable container layer.
- Measured final image size changed from `425225473` to `376993122` bytes (reduction `48232351` bytes, about 46.0 MiB). This is measured image reduction; cache proof is path-specific rather than an inaccurate blanket claim that no runtime build-related files exist.
- MCP cache correctness: restore now uses `-r linux-x64`; the rebuilt self-contained single-file publish uses `--no-restore` and completed successfully.

## Task 11: Production Isolation and Required MCP Readiness

### TDD RED

Tests were added before the integration packages. This exact command was run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "Category=ProductionIntegration" --verbosity normal
```

- Exit `1`; elapsed `9.92s`; zero tests ran because compilation stopped first.
- Expected failure: 8 `CS0234`/`CS0246` errors, exclusively for missing `Microsoft.AspNetCore.SignalR.Client`, `Testcontainers`, `HubConnection`, and `PostgreSqlContainer` symbols.
- Minimal dependency change: exact non-floating `Microsoft.AspNetCore.SignalR.Client` `10.0.7` and `Testcontainers.PostgreSql` `4.13.0`. Existing `Microsoft.AspNetCore.Mvc.Testing` remained `10.0.7`.
- Restore: `dotnet restore backend/Orchestration.Tests/Orchestration.Tests.csproj --verbosity minimal` exited `0` in `8.81s`. Existing transitive `Microsoft.OpenApi 2.4.1` `NU1903` remained visible.

### Integration GREEN

The mandated command was rerun exactly:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "Category=ProductionIntegration" --verbosity normal
```

- Initial exit `0`; 2 total, 2 passed, 0 failed; test time `21.0503s`; build time `23.59s`. The final run includes the added timeout/kill-tree proof and is recorded below.
- Post-self-review rerun of the same exact command, after moving invalid-command startup proof ahead of the CNV stop, also exited `0` with 0 build errors.
- Focused non-container regression filter covering existing ActivityHub authorization/authentication, SignalR publisher, MCP readiness, and MCP options tests: final run 70 total, 70 passed, 0 failed, 0 skipped in `677ms`.
- SignalR: Production `WebApplicationFactory`; real API registration/login for unique synthetic users A/B; opaque access tokens were neither decoded nor logged; real Long Polling `HubConnection` clients used the TestServer handler; user A created the session; production DI `IActivityEventPublisher` persisted and sent one matching `task11_isolation_probe`; A received exactly one; B received none in the bounded 2-second window; anonymous handshake failed with HTTP `401`; persisted event endpoint returned the event for A and `404` for B.
- Required MCP: a real current-platform self-contained MCP executable was published deterministically from the test assembly path; `migrate-db --storage postgres` targeted only the new disposable CNV database; Production API started with `Enabled=true`, `Required=true`, and the real command plus `--storage postgres`; empty migrated corpus returned exact `200 Healthy`; while that CNV target was still healthy, a separate factory with a guaranteed-missing command failed startup with `CNV MCP readiness probe failed.`; stopping the CNV container then produced bounded exact `503 Unhealthy`. No corpus ingestion, persistent target, volume, paid embedding, API key, DSN, password, or opaque token was used or logged.

Passing focused identity-capture runs:

- SignalR app PostgreSQL: ID `6cb7cdd588472bc8dadd7c4f6d942823ed672bdfc886268d8a48cfddc6a39951`; name `/aihitl-task11-app-signalr-10982ddfff564067ba25a6f6c5972f99`; run identity fingerprint `0e8b146744fd121e69d182604b34079c126941d444a8fcc1a5b0df42f54b8ef0`; deleted.
- Required-MCP app PostgreSQL: ID `49f15da776285cb7f82ae8286c6a97675019bafceb13bcd163f6b77911f1534b`; name `/aihitl-task11-app-mcp-c0718190c50b441a8ca106abae485e99`; run identity fingerprint `aab225025378dd167f2db504915cf429281564054b238c20f45490048016348a`; deleted.
- Required-MCP CNV PostgreSQL/pgvector: ID `40824246ffba34bece411ff9655557f8ad5c3cee47cccf51e67e3c51e36b85d2`; name `/aihitl-task11-cnv-c0718190c50b441a8ca106abae485e99`; run identity fingerprint `64493d829d76b49796778e06f83fa154e188ecf566fdad3550fe850aeea767ac`; explicitly stopped for the readiness transition, then deleted.

Harness debugging evidence:

- First MCP behavior run failed before API startup because the CNV schema begins with `CREATE EXTENSION IF NOT EXISTS vector`, while the harness initially used vanilla PostgreSQL. The CNV-only target was corrected to the official `pgvector/pgvector:pg17` image; the application target remains `postgres:17-alpine`.
- The first passing-path cleanup raced Windows file release for the self-contained MCP and hit a locked `clrjit.dll`. Cleanup validates the Task 11 temporary path and retries only `IOException`/`UnauthorizedAccessException` for a bounded 10 seconds. The caller now creates, validates, and records that directory before publish starts, so publish or executable-validation failures still reach cleanup. Cleanup attempts both containers and the directory even if an earlier disposal fails, then reports only a generic resource count.

Ambient-target safety TDD proof:

- Effective precedence is explicit: `RegulationDbOptions.Create` uses nonblank `CNV_REGULATION_DB_CONNECTION_STRING` before the lower configuration alias `RegulationDb__ConnectionString`.
- RED: the test wrapped the full run in a restoring outer scope that set the higher-precedence variable to a non-secret, unreachable loopback target on port 1. `dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~RequiredMcpReadinessTests" --verbosity minimal` exited `1`; 0 passed, 1 failed in `12s`, at `CNV disposable database migration failed with exit code -532462766.` This proved the migration child still honored the poison instead of its intended disposable target. RED app container ID `3f6e189cd59ad0fca06773b534bcd12c4186f6bfda7f4fa95950a1411ed91fbc`, run identity fingerprint `0892932d346803e8084fe7f6f7a5329366aa8228398943870a2ff900bacad4db`; RED CNV container ID `68fd496006c61a6e79a7677f4f24727f3813a8e6ecce1784eeeb2b9b6ea34387`, run identity fingerprint `48b3780367bcb6d7c018190a7058c34d50b1f87b918253133b5a62fd12a94ee0`; both deleted without schema migration.
- GREEN: the migration `ProcessStartInfo.Environment` now sets both connection names to the same disposable CNV connection. The nested API/MCP-child environment scope also saves, sets, and restores both names to that disposable connection. The outer poison stays active outside the nested scope, so deleting either exact override makes the regression fail without consulting or exposing any ambient value.
- GREEN focused MCP rerun before the timeout test: exit `0`; 1 passed, 0 failed in `24s`. The final required-MCP fixture run, including timeout proof, passed 2/2 in `19s`.
- No ambient connection was inspected, printed, queried, or mutated. All process environment variables were restored after the test; child-process output and connection strings remained undisclosed.

Process timeout and cleanup TDD proof:

- RED: `dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~RunProcessAsync_WhenTimeoutExpires" --verbosity minimal` exited `1` at compilation with `RequiredMcpReadinessTests.cs(138,17): error CS1501: Ninguna sobrecarga para el método 'RunProcessAsync' toma 3 argumentos`.
- GREEN: publish has a 2-minute timeout and CNV migration has a 30-second timeout. Timeout kills the entire process tree, waits for exit plus silent stdout/stderr drains, disposes the process, and throws `TimeoutException` without exposing child output. A cross-platform delayed-child marker test passed 1/1 in `5s`; it first confirmed the child command had started, then waited beyond the child's delay and confirmed the completion marker remained absent.
- Final exact ProductionIntegration command: exit `0`; 3 total, 3 passed, 0 failed; test time `28.0206s`; total build/test time `30.68s`; 0 build errors. Final focused non-container regressions: 70 passed, 0 failed, 0 skipped in `677ms`.
- Final residue check found 0 matching Task 11/smoke containers, 0 smoke networks, 0 CNV MCP processes, 0 Task 11 publish directories, 0 timeout markers, and no `.render-smoke.env`.

Quality-review follow-up:

- Discriminating mutation RED: after making a unique descendant `.cmd`/`.sh` script own both markers while its parent shell waited synchronously, temporarily replacing tree-kill with parent-only `process.Kill()` and running `dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~RunProcessAsync_WhenTimeoutExpires_KillsTheEntireProcessTree" --verbosity normal` exited `1`; 1 total, 1 failed in `12.0334s` with `Assert.False() Failure`, expected `False`, actual `True`. Tree-kill was restored immediately. The child script, started marker, and completion marker are all deleted in `finally`.
- Harness RED after restarting Docker Desktop: the two parallel PostgreSQL starts remained pending for more than 120 seconds even though both disposable containers logged ready, passed `pg_isready`, and exposed reachable ephemeral ports; the single-container SignalR Fact passed 1/1 in `15.8024s`. Starts are now explicit and sequential. The next run reached publish and produced all 236 files, but persistent build-server children retained redirected handles; the same direct publish passed with exit `0` in `2.369s`. Task 11 publish now uses `--disable-build-servers`.
- Final GREEN: the timeout Fact passed 1/1 in `6.4996s`; the required-MCP Fact passed 1/1 in `18.9177s`; the exact `Category=ProductionIntegration` command passed 3/3 in `29.7224s` (`32.13s` build/test). Final checks found 0 Task 11 containers, publish directories, timeout markers/scripts, MCP processes, or run-owned test processes; `git diff --check` exited `0` and added-line secret-pattern hits were `0`.
- Exact-event review RED/GREEN: the SignalR receipt now uses `Assert.Equal(expected, receivedByA)` for all `ActivityEvent` record fields. Publishing the temporary mutation `expected with { Agent = "Task11-mismatch" }` made the focused SignalR Fact exit `1`, 1/1 failed in `8.7001s`, with expected `Agent = Task11` and actual `Agent = Task11-mismatch`; the mutation was restored immediately. The same Fact then exited `0`, 1/1 passed in `10.4770s`; the exact `Category=ProductionIntegration` command exited `0`, 3/3 passed in `29.3839s` (`31.74s` build/test).
- Lifecycle deadline RED: the earlier two-container start exceeded 120 seconds and the nested publish left redirected output drains pending after its parent exited. After adding deterministic deadline/aggregation Facts first, `dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~AwaitTaskWithDeadlineAsync_WhenTaskNeverCompletes|FullyQualifiedName~ThrowPrimaryOrCleanupFailures_WhenBothExist_PreservesBoth" --verbosity minimal` exited `1` before test execution with two expected `CS0103` errors for the missing helpers. Container start/stop/dispose, health requests/polling, process exit/output drain, and cleanup now have explicit deadlines; all cleanup attempts run, and primary plus cleanup failures are preserved without process output, environment, or connection details.
- Normal-drain discriminating RED/GREEN: adding the focused never-completing-reader Fact before its helper made `dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~DrainProcessOutputAsync_WhenReaderNeverCompletes_ThrowsWithinDeadline" --verbosity minimal` exit `1` before test execution with the expected `CS0103` for `DrainProcessOutputAsync`. After wiring that bounded helper into the normal-success path, the same Fact passed 1/1 in `127ms`.
- Lifecycle deadline GREEN: the deterministic deadline/aggregation filter passed 2/2 in `124ms`; the process-tree Fact passed 1/1 in `5s`; SignalR passed 1/1 in `18s`; required MCP passed 1/1 in `41s`. The final exact `dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "Category=ProductionIntegration" --verbosity normal` command exited `0`, 6/6 passed in `29.0637s` (`31.36s` build/test).
- Post-gate checks: 0 Task 11 containers/networks/temp entries, CNV MCP processes, or testhost processes; no `.render-smoke.env`; `git diff --check` exited `0`; added-line secret-pattern hits were `0`; only the two scoped integration test files and this progress file changed.

### Built Image Smoke

Image: `ai-orchestration-hitl:local`, inspected image ID `sha256:aef4dff4c11136a07873425cecd69f156dadc899648e12745546b021005a4729`.

- New isolated network: ID `aa9d7027ee650ee31440e66e7159e4bec11d3d85ee91575e9288ed3dd73c51b2`; name `ai-hitl-smoke`.
- New app PostgreSQL: ID `6eb404df8640b1f068657a1795022dff6c9d8ed7ef244c39a8b817a35398c173`; name `ai-hitl-smoke-app-postgres`; run identity fingerprint `42400d50c34a956d727457eae866aeb7bb70d7d8c8d577cba1724c71b8e5d127`.
- New CNV PostgreSQL/pgvector: ID `33e783fb1099fa42a9c9d97ea9bd4ecf2fae006307dfaf05bd7afc2a6c327b67`; name `ai-hitl-smoke-cnv-postgres`; run identity fingerprint `8b09e2c29fdbadef7f7f741ae0bd18d773f8b7844e725bc51a9a6daede98acd3`.
- Application `--migrate-only`: exit `0`. CNV `/app/mcp/CnvRegulation.McpServer migrate-db --storage postgres`: exit `0`.
- Required-MCP API: ID `fdb3b18951d7bb7f43ba0d1b998e40651d2b60404a0236bb5a969ac680c4a912`; name `ai-orchestration-hitl-smoke`; `/alive` exact `200 Healthy`; `/health` exact `200 Healthy`.
- `.render-smoke.env` was ignored, never printed, and deleted in `finally`. Exact API, migration, app-DB, and CNV-DB containers plus the network were absent after cleanup. No volumes were created.

### Remaining Evidence and Gaps

- Docker was available; no production-integration acceptance item remains open.
- Existing warning: `Microsoft.OpenApi 2.4.1` reports `NU1903`; Task 11 did not modify that transitive dependency.
- A fresh-database API/WebApplicationFactory process emits a transient pre-migration `42P01` log for missing `DataProtectionKeys`; this was not emitted by the separate `--migrate-only` process. The hosted migration then applies the schema and all Task 11 acceptance checks pass. This production-code startup-order observation is recorded for follow-up and intentionally not changed within Task 11's test-only scope.

## Task 12: Complete Production Gate

Gate timestamp: `2026-08-08T11:30:30-03:00` (America/Buenos_Aires). Verified commit before this evidence update: `71ceec0f47718874fd5a3520ee543a3e5589ed9b`.

### Decision: CONDITIONAL GO

All executable local code, test, frontend, Docker-image, official Blueprint-schema, invariant, and branch gates below passed, including the final-review Bash correction and fresh image probes recorded below. Production handoff is not yet a **GO** because no Render resources were provisioned, no authenticated Render operator validation was run, and no independently approved immutable CNV corpus/query bundle was ingested or accepted against the intended production database. Empty-corpus technical readiness proves MCP transport/database execution only; it does not prove production retrieval coverage or legal applicability.

Final review temporarily invalidated the earlier local Docker gate: the prior image registered `/bin/sh` for the runtime user even though the documented CNV SSH gate requires Bash. The decision remained withheld during correction. A fresh rebuild, shell/runbook smoke, retained-content probes, and the focused production integration suite all passed; the local decision is therefore restored to **CONDITIONAL GO**, subject to the unchanged operator gates below.

No Render deployment, persistent CNV target, corpus ingestion, secret change, or production database mutation was performed by this gate.

### Restore and Release build

| Exact command | Exit | Evidence |
| --- | ---: | --- |
| `dotnet restore backend/Orchestration.slnx` | 0 | Restore completed; existing package advisories remained visible. |
| `dotnet restore tools/CnvRegulation.McpServer/CnvRegulation.McpServer.sln` | 0 | Restore completed. |
| `$env:ORCHESTRATION_TEST_PYTHON_HOME=(Resolve-Path 'python-agents/data_agent').Path; dotnet build backend/Orchestration.slnx --no-restore --configuration Release` | 0 | 0 errors, 29 warnings. |
| `dotnet build tools/CnvRegulation.McpServer/CnvRegulation.McpServer.sln --no-restore --configuration Release` | 0 | 0 errors, 0 warnings. |

The 29 backend build warnings are not hidden: 2 `NU1903` occurrences for transitive `Microsoft.OpenApi 2.4.1`; 11 `MessagePack 2.5.192` advisory occurrences in AppHost (9 `NU1902` moderate and 2 `NU1903` high); and 16 existing `CS0618` test-call warnings for obsolete `AnalysisSession.Create()`. They did not become build errors under current project policy, but the vulnerable dependencies remain remediation items.

### Test, Python, and frontend gates

| Exact command | Exit | Result |
| --- | ---: | --- |
| `$env:ORCHESTRATION_TEST_PYTHON_HOME=(Resolve-Path 'python-agents/data_agent').Path; dotnet test backend/Orchestration.slnx --no-build --configuration Release --verbosity minimal` | 0 | 1,866 passed, 0 failed, 0 skipped, 1,866 total; 36 s test duration. |
| `dotnet test tools/CnvRegulation.McpServer/tests/CnvRegulation.Application.Tests/CnvRegulation.Application.Tests.csproj --configuration Release --verbosity minimal` | 0 | 126 passed, 0 failed, 22 skipped, 148 total. |
| `dotnet test tools/CnvRegulation.McpServer/tests/CnvRegulation.McpServer.Tests/CnvRegulation.McpServer.Tests.csproj --configuration Release --verbosity minimal` | 0 | 20 passed, 0 failed, 0 skipped, 20 total. |
| `python-agents/data_agent/.venv/Scripts/python.exe -m unittest discover -s python-agents/tests -p "test_*.py"` | 0 | 31 passed in 0.694 s; the suite emitted its existing `EOF marker not found` diagnostic and still completed `OK`. |
| `npm --prefix frontend ci` | 0 | 40 packages installed/audited; npm reported 4 high-severity vulnerabilities. No automatic mutation (`npm audit fix`) was run. |
| `npm --prefix frontend test` | 0 | 55 passed, 0 failed/skipped. |
| `$env:VITE_API_URL='http://localhost:10000'; npm --prefix frontend run build` | 0 | TypeScript and Vite production build passed; the non-secret absolute local URL existed only in that process. |

The 22 named MCP Application skips are all methods in `PostgresRegulationRepositoryIntegrationTests`; their shared explicit opt-in reason is: set `CNV_REGULATION_RUN_INTEGRATION_TESTS=true` and a disposable `CNV_REGULATION_DB_CONNECTION_STRING`. They were not force-enabled because Task 11 already exercised the required real MCP path against newly created disposable PostgreSQL/pgvector databases, while this gate was forbidden from using an ambient or persistent CNV target.

Task 11's focused current-branch evidence remains part of this gate: exact `Category=ProductionIntegration` passed 6/6. Its Production `WebApplicationFactory` used two real users and two opaque-token SignalR clients; user A received the exact full event, user B received none during the bounded window, anonymous handshake returned `401`, and persisted ownership was enforced. The real required MCP migrated a disposable pgvector database, returned exact `200 Healthy` for an empty corpus, returned exact `503 Unhealthy` after that disposable target stopped, and failed startup for an invalid command. The full 1,866-test backend run above included those tests. Post-gate residue was 0 matching Task 11/smoke containers, networks, MCP/testhost processes, temp entries, and `.render-smoke.env` files.

### Docker and Blueprint artifact proof

| Exact command | Exit | Result |
| --- | ---: | --- |
| `docker build --progress=plain -t ai-orchestration-hitl:production-gate .` | 0 | Final-review rebuild passed; inspected ID `sha256:d96200ca94a05594a29ee94985159fd111a76ae5ffdce87e5ee0620cbc07c520`. |
| `render blueprints validate render.yaml` preflight through `Get-Command render` | 127 | Render CLI is absent; it was not installed or emulated. Authenticated CLI/operator validation remains pending. |
| `Invoke-WebRequest -Uri 'https://render.com/schema/render.yaml.json'` to a unique temporary path, followed by `python -c` using the already-installed `yaml` and `jsonschema` modules against `render.yaml` | 0 | Official Render JSON Schema downloaded over HTTPS; schema itself and Blueprint both validated; temp schema deleted in `finally`. |
| `docker image inspect ai-orchestration-hitl:production-gate --format "{{.Config.User}} {{json .Config.ExposedPorts}} {{json .Config.Healthcheck.Test}}"` | 0 | `1654 {"10000/tcp":{}} ["CMD-SHELL","curl --fail --silent http://127.0.0.1:10000/alive || exit 1"]`. |

#### Final-review Bash gate correction

- RED against prior image `sha256:8075f23b3e0dd0b99af869422e3c26eac8eb8c6ceaacc5a9af687c33bcee4be1`: `docker run --rm --entrypoint /usr/bin/getent ai-orchestration-hitl:production-gate passwd app` returned `app:x:1654:1654::/home/app:/bin/sh`; `/bin/sh -c 'set -o pipefail'` exited `2` with `Illegal option -o pipefail`. This disproved the earlier claim that the documented Bash gate was executable in the image.
- GREEN: the final stage now verifies that `app` exists and sets its login shell with `usermod --shell /bin/bash app` before `USER $APP_UID`. The exact rebuild exited `0` and produced image ID `sha256:d96200ca94a05594a29ee94985159fd111a76ae5ffdce87e5ee0620cbc07c520`.
- Image metadata remained `1654`, port `10000/tcp`, and the `/alive` healthcheck. `getent passwd app` now ends in `/bin/bash`; an explicit `/bin/bash -lc` login-shell probe confirmed `BASH_VERSION`, login-shell mode, and `set -euo pipefail` with exit `0`.
- The runbook now requires `/bin/bash` and begins with the portable assertion `test -n "${BASH_VERSION:-}"` before enabling `pipefail`. A safe `/bin/bash -lc` smoke executed the documented startup/cleanup and input-validation preamble with non-secret dummy session values, printed none of them, and exited `0`. The same guard invoked through `/bin/sh` exited `1` with the expected secret-safe Bash-required message before reaching `pipefail`.
- Retained image contract probes exited `0`: `/home/app/.ssh` remained owned by `1654:1654`, mode `0700`, and empty; API DLL, self-contained MCP executable, locked Python environment, `Python__Home`, OCR/PDF commands, and non-root UID remained present/correct (`tesseract 5.3.4`, `pdftoppm 24.02.0`, Python `3.12.9`).
- Focused regression: `$env:ORCHESTRATION_TEST_PYTHON_HOME=(Resolve-Path 'python-agents/data_agent').Path; dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "Category=ProductionIntegration" --verbosity normal` exited `0`; 6/6 passed in `29.9265s`. Existing `NU1903` and obsolete test-call warnings remained visible and unchanged.
- Runbook checks passed for 7 required headings, 11 production flags, 5 critical commands, the local `render.yaml` link, and 10 balanced code fences. Added-line secret-pattern hits were `0`; tracked runtime/deploy secret-pattern hits were `0` after excluding test/quality fixtures and applying a token boundary to avoid CSS `mask-*` false positives. Final residue was exactly 0 Task 11 containers, networks, temp entries, `.render-smoke.env` files, CNV MCP processes, and testhost processes. `git diff --check` exited `0`, and the changed-file set was exactly `Dockerfile`, `docs/deployment/render.md`, and `progress.md`.

Exact Render CLI preflight:

```powershell
$command = Get-Command render -ErrorAction SilentlyContinue; if ($null -eq $command) { Write-Output 'render-cli=absent'; exit 127 }; Write-Output ('render-cli=' + $command.Source); render blueprints validate render.yaml
```

Exact official-schema fallback (the unique temp path was removed in `finally`):

```powershell
$schemaPath = Join-Path ([System.IO.Path]::GetTempPath()) ('render-yaml-schema-' + [guid]::NewGuid().ToString('N') + '.json'); try { Invoke-WebRequest -Uri 'https://render.com/schema/render.yaml.json' -OutFile $schemaPath; python -c "import json,sys,yaml,jsonschema; schema=json.load(open(sys.argv[1], encoding='utf-8')); document=yaml.safe_load(open(sys.argv[2], encoding='utf-8')); jsonschema.Draft202012Validator.check_schema(schema); jsonschema.validate(document, schema); print('render-blueprint-official-schema=valid')" $schemaPath render.yaml } finally { if (Test-Path -LiteralPath $schemaPath) { Remove-Item -LiteralPath $schemaPath -Force } }
```

The fallback validates declared Blueprint structure only. It does not prove authenticated Render acceptance, provisioning, database references, deployed origins, or runtime service state.

### Production invariant scans

| Exact command | Exit | Interpretation |
| --- | ---: | --- |
| `rg -n "Password1!|admin@ezemartino\.com|user@ezemartino\.com" .` | 0 | At execution time, before this evidence table existed, matches were limited to four historical/design/plan documents, including Task 12's own literal scan command. The same four files match on `main`, and none differs in this branch. A final rerun also matches this progress row as evidence self-reference. This global command is therefore not represented as a zero-match scan. |
| `rg -n "Password1!|admin@ezemartino\.com|user@ezemartino\.com" backend frontend python-agents tools Dockerfile render.yaml README.md` | 1 | Expected ripgrep no-match exit: zero matches in runtime source, deploy artifacts, frontend, tooling, Python, or README. |
| `rg -n "Clients\.All|Clients\.Group" backend/Orchestration.Api` | 1 | Expected no-match exit: no broad SignalR publish path. |
| `rg -n "MockRegulatoryKnowledgeSource" backend/Orchestration.Api backend/Orchestration.Infrastructure` | 0 | Definition plus one registration; source inspection confirms registration only when environment is Development or Test, otherwise disabled production uses `UnavailableRegulatoryKnowledgeSource`. |
| `rg -n "UseSwagger|UseSwaggerUI|MapHealthChecks|UseForwardedHeaders" backend` | 0 | Swagger calls are inside `IsDevelopment`; `/health` and `/alive` map before environment-specific middleware; `UseForwardedHeaders` runs before HSTS/HTTPS when explicit Render proxy support is enabled. |
| `git diff --check` and `git status --short` before this evidence update | 0 | No whitespace errors; clean worktree. |

### Branch ancestry and final diff before evidence commit

| Exact command | Exit | Result |
| --- | ---: | --- |
| `git merge-base --is-ancestor main HEAD` | 0 | Branch contains local `main`. |
| `git rev-parse main` / `git rev-parse origin/main` | 0 | Both resolved to `0cf6d17d0392a0d356fb3450566838c98657bc45`. |
| `git log --oneline --decorate main..HEAD` | 0 | Six scoped commits through `71ceec0`; Tasks 7-11 map to endpoint/health, container, topology, runbook, SSH, and isolation/readiness work. |
| `git diff --stat main...HEAD` | 0 | 20 files; 2,683 insertions, 28 deletions before this Task 12 evidence update. |
| `git diff --check main...HEAD` | 0 | No branch whitespace errors. |

### Required operator actions before GO

1. Use an authenticated/current Render CLI or Render dashboard to validate and create the Blueprint from `render.yaml`; keep API plus both PostgreSQL databases in `oregon`, one API instance, and record the deploy identifier without secrets.
2. Confirm the final HTTPS origins: `Cors__AllowedOrigins__0` must be the static-site origin and frontend `VITE_API_URL` the API origin. Confirm database values come from Render `fromDatabase`, never pasted values.
3. Confirm required production flags before restart/redeploy: `Mcp__CnvRegulation__Enabled=true`, `Mcp__CnvRegulation__Required=true`, command `/app/mcp/CnvRegulation.McpServer`, args `--storage postgres`; `ToolCalling__Enabled=true`, `ExecutionMode=PlanDriven`, and the whole replacement array contains both `data.analyze_transactions` and `legal.search_cnv_regulation`. Keep `Llm__Enabled=false`, both AI-review flags false, fixture fallback false, and `IdentityBootstrap__Enabled=false` unless separately approved/configured. MCP selection is bootstrap-scoped, so flag changes require restart/redeploy.
4. Allow application and CNV schema migrations, then use authenticated Render SSH for the exact successful deploy. Independently approve the immutable, non-empty corpus/query bundle and expected target fingerprint; confirm exact CNV target without printing the URI; only then obtain separate authorization for the persistent one-time ingestion.
5. Require non-empty coverage plus passing full-text search-quality evidence. Treat all retrieved/canonical content as documentary evidence for human review, not applicability, breach, compliance risk, or legal advice.
6. Redeploy/restart, then verify final `/health`, two distinct user logins, cross-user SignalR isolation, Data analysis without fixture fallback, cited CNV retrieval with review disclaimers, and pre-restart token survival. Any failure changes this decision to **NO-GO**.

Open local remediation risks that do not invalidate the recorded test outcomes: transitive .NET package advisories (`Microsoft.OpenApi 2.4.1`, `MessagePack 2.5.192`), npm's 4 high-severity audit findings, 16 obsolete test-call warnings, and the Task 11 fresh-database transient pre-migration `DataProtectionKeys` `42P01` observation. They must be triaged explicitly; none is claimed clean or fixed by Task 12.
