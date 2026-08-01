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
