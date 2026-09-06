# Render Production Deployment Runbook

This runbook covers the first production deployment described by [`render.yaml`](../../render.yaml), the separate CNV corpus gate, required configuration, rollback, and operator handoff. It does not authorize a deployment, a secret change, or a persistent database mutation.

CNV, Infoleg, and canonical retrieval results are documentary evidence for human review. A retrieved or canonical match does not by itself establish legal applicability, a breach, compliance risk, or legal advice.

## First-deploy sequence

An authorized Render operator must perform these steps in order:

1. Create the Blueprint in Render from `render.yaml`.
2. Keep the API and both PostgreSQL databases in `oregon`.
3. Set `Cors__AllowedOrigins__0` to the final static-site HTTPS origin.
4. Set frontend `VITE_API_URL` to the final API HTTPS origin.
5. Allow the pre-deploy application and CNV schema migrations to complete, then confirm Render automatically starts the API and marks that initial deploy successful. The corpus may still be empty at this point.
6. Open an authorized SSH shell on the running instance for that exact successful API deploy. Use its inherited CNV database environment, confirm the target fingerprint, then stage the approved immutable corpus and search-query bundle as described below.
7. Run the one-time approved corpus ingestion from the staged source path.
8. Inspect corpus coverage and full-text search quality.
9. Redeploy or start the API with the required MCP configuration.
10. Verify health, login, two-user SignalR isolation, Data analysis, CNV retrieval, and restart token survival.

The initial successful deploy is a staging artifact, not production handoff: pre-deploy migrations are automatic, the API starts, and `/health` can pass with an empty corpus. The authenticated SSH gate then runs inside that deployed image with its inherited CNV connection environment. After the corpus gate passes, restart or redeploy the API and perform every final check in step 10.

Do not advance past a failed step. In particular, do not start ingestion until the operator has confirmed the Render environment, `ai-orchestration-hitl-cnv-db` resource, `oregon` region, and exact connection fingerprint without printing or copying the connection URI.

The frontend build validates `VITE_API_URL` before compiling. Its value must exist before the first static-site build and must be a valid absolute HTTP(S) URL such as `https://ai-orchestration-hitl-api.onrender.com`. A relative URL, another scheme, or an empty value blocks the prebuild. Changing it requires a new frontend build/deploy.

## CNV corpus gate

The API pre-deploy command automates the application and CNV **schema migrations only**. Corpus ingestion is deliberately excluded from startup and pre-deploy automation.

The final API image contains the MCP executable, but it does not contain a corpus bundle or the repository default `data/search-quality/cnv.search-quality.json`. Never rely on those inputs being present. Use an authorized immutable archive that contains both of these reviewed, approved, non-empty paths:

```text
cnv-gate/
  sources/
  search-quality/cnv.search-quality.json
```

An independent approver must record the archive's immutable artifact reference, expected SHA-256, and query-set approval in the change record. The query set must be reviewed and non-empty. A secret or pre-signed artifact URL must never be printed or copied into logs.

### Open an authenticated SSH gate session

Use Render's authenticated SSH access for the paid API service and connect to the running instance for the exact successful deploy identifier recorded in step 5. The SSH shell runs as the image's non-root runtime user and inherits that service instance's exact `CNV_REGULATION_DB_CONNECTION_STRING`; do not replace or re-enter the database URI.

The deployed image is verified to give that runtime user (UID `1654`, home `/home/app`) `/bin/bash` as its executable login shell and an owned `/home/app/.ssh` directory with mode `0700`, as required for this Render SSH gate. The image creates only the directory; it contains no SSH key material. Do not run the gate from `/bin/sh` or another shell.

Render one-off jobs snapshot base-service configuration and do not provide this runbook's required session-only environment overrides or writable mount. They are not used here. Using a one-off job would require a separately designed and approved solution with an independently packaged gate script and temporary service environment.

In the authenticated SSH Bash session, disable tracing/history, create only the known writable temporary path, and install cleanup before entering any session-only values. Enter the secret or pre-signed URL with `read -rsp`, which neither echoes it nor places the value in shell history. Enter the independently authorized digests from the separate change record; do not calculate them from values in this session.

```bash
if ! test -n "${BASH_VERSION:-}"; then
  echo "CNV gate setup failed: /bin/bash is required; stop and reconnect using the image login shell." >&2
  exit 1
fi
set -euo pipefail
set +x
set +o history
umask 077

if [[ -e /tmp/confirmed ]]; then
  echo "CNV gate setup failed: /tmp/confirmed already exists; stop and investigate." >&2
  exit 1
fi
mkdir -m 700 /tmp/confirmed

cleanup_cnv_gate() {
  rm -rf -- /tmp/confirmed
  unset CNV_GATE_BUNDLE_URL CNV_GATE_BUNDLE_SHA256 CNV_EXPECTED_TARGET_FINGERPRINT
}
trap cleanup_cnv_gate EXIT
trap 'exit 1' HUP INT TERM

read -rsp "Approved HTTPS artifact URL: " CNV_GATE_BUNDLE_URL
printf '\n'
read -rp "Authorized bundle SHA-256: " CNV_GATE_BUNDLE_SHA256
read -rp "Authorized CNV target fingerprint: " CNV_EXPECTED_TARGET_FINGERPRINT

if [[ "$CNV_GATE_BUNDLE_URL" != https://* ]]; then
  echo "CNV gate setup failed: artifact URL must use HTTPS." >&2
  exit 1
fi
if [[ ! "$CNV_GATE_BUNDLE_SHA256" =~ ^[[:xdigit:]]{64}$ ]]; then
  echo "CNV gate setup failed: bundle SHA-256 is invalid." >&2
  exit 1
fi
if [[ ! "$CNV_EXPECTED_TARGET_FINGERPRINT" =~ ^[[:xdigit:]]{64}$ ]]; then
  echo "CNV gate setup failed: target fingerprint is invalid." >&2
  exit 1
fi

CNV_GATE_BUNDLE_SHA256="${CNV_GATE_BUNDLE_SHA256,,}"
CNV_EXPECTED_TARGET_FINGERPRINT="${CNV_EXPECTED_TARGET_FINGERPRINT,,}"
export CNV_GATE_BUNDLE_URL CNV_GATE_BUNDLE_SHA256 CNV_EXPECTED_TARGET_FINGERPRINT
```

Any validation failure exits the shell through the trap, removes exactly `/tmp/confirmed`, and unsets the three session-only values without printing them. After successful ingestion and inspection, type `exit` to trigger the same cleanup before the required API redeploy/restart. Do not continue if the SSH session is not attached to the recorded deploy.

### Confirm the exact CNV target

An independent approver must derive and authorize `CNV_EXPECTED_TARGET_FINGERPRINT` from the intended Render database resource as lowercase `host:port/database`, SHA-256 hashed. Do not derive the expected value from the connection URI inside the same SSH session. After the guarded session setup above, run this check with the Python bundled in the deployed image:

```bash
/app/python/data_agent/.venv/bin/python - <<'PY' || exit 1
import hashlib
import hmac
import os
import sys
from urllib.parse import unquote, urlsplit


def fail(message: str) -> None:
    print(message, file=sys.stderr)
    raise SystemExit(1)


raw_uri = os.environ.get("CNV_REGULATION_DB_CONNECTION_STRING", "")
expected = os.environ.get("CNV_EXPECTED_TARGET_FINGERPRINT", "").strip().lower()
if not raw_uri:
    fail("CNV target fingerprint check failed: connection environment is missing.")
if len(expected) != 64 or any(character not in "0123456789abcdef" for character in expected):
    fail("CNV target fingerprint check failed: independently authorized fingerprint is missing or invalid.")

try:
    parsed = urlsplit(raw_uri)
    port = parsed.port or 5432
except ValueError:
    fail("CNV target fingerprint check failed: connection environment is invalid.")

host = (parsed.hostname or "").lower()
database = unquote(parsed.path.lstrip("/")).strip().lower()
if parsed.scheme.lower() not in {"postgres", "postgresql"} or not host or not database or "/" in database:
    fail("CNV target fingerprint check failed: connection environment is invalid.")

canonical_target = f"{host}:{port}/{database}"
actual = hashlib.sha256(canonical_target.encode("utf-8")).hexdigest()
print(actual)
if not hmac.compare_digest(actual, expected):
    fail("CNV target fingerprint mismatch; stop before ingestion.")
PY
```

The only target identifier written to standard output is the SHA-256 fingerprint; the URI, username, password, hostname, and database name are never printed. The check exits nonzero when the connection is invalid, the independently authorized expected fingerprint is absent/invalid, or `hmac.compare_digest` detects a mismatch. Stop if the expected fingerprint cannot be independently established and confirmed.

### Stage the immutable gate bundle

After the target check succeeds, download the approved archive to a temporary path, verify its SHA-256 **before** extraction, safely extract it, and reject empty sources or queries. This script never prints the artifact URL:

```bash
/app/python/data_agent/.venv/bin/python - <<'PY' || exit 1
import hashlib
import hmac
import json
import os
import shutil
import sys
import tarfile
import tempfile
from pathlib import Path
from urllib.parse import urlsplit
from urllib.request import HTTPRedirectHandler, Request, build_opener


def fail(message: str) -> None:
    print(message, file=sys.stderr)
    raise SystemExit(1)


class HttpsOnlyRedirectHandler(HTTPRedirectHandler):
    def redirect_request(self, request, file_pointer, code, message, headers, new_url):
        if urlsplit(new_url).scheme.lower() != "https":
            raise RuntimeError("Non-HTTPS artifact redirect rejected.")
        return super().redirect_request(request, file_pointer, code, message, headers, new_url)


artifact_url = os.environ.get("CNV_GATE_BUNDLE_URL", "")
expected = os.environ.get("CNV_GATE_BUNDLE_SHA256", "").strip().lower()
if urlsplit(artifact_url).scheme.lower() != "https":
    fail("CNV gate bundle staging failed: an HTTPS artifact reference is required.")
if len(expected) != 64 or any(character not in "0123456789abcdef" for character in expected):
    fail("CNV gate bundle staging failed: the authorized SHA-256 is missing or invalid.")

confirmed = Path("/tmp/confirmed")
if not confirmed.is_dir() or not os.access(confirmed, os.W_OK):
    fail("CNV gate bundle staging failed: /tmp/confirmed is not writable.")

destination = confirmed / "cnv-gate"
if destination.exists():
    fail("CNV gate bundle staging failed: destination is not empty.")

stage = Path(tempfile.mkdtemp(prefix=".cnv-gate-", dir=confirmed))
try:
    archive = stage / "bundle.tar.gz"
    digest = hashlib.sha256()
    try:
        request = Request(artifact_url, headers={"User-Agent": "cnv-gate-stager/1"})
        opener = build_opener(HttpsOnlyRedirectHandler())
        with opener.open(request, timeout=120) as response:
            if urlsplit(response.geturl()).scheme.lower() != "https":
                fail("CNV gate bundle staging failed: final artifact response is not HTTPS.")
            with archive.open("wb") as output:
                while block := response.read(1024 * 1024):
                    output.write(block)
                    digest.update(block)
    except Exception:
        fail("CNV gate bundle staging failed: download failed.")

    actual = digest.hexdigest()
    if not hmac.compare_digest(actual, expected):
        fail("CNV gate bundle staging failed: SHA-256 mismatch.")

    unpacked = stage / "unpacked"
    unpacked.mkdir()
    try:
        with tarfile.open(archive, "r:gz") as bundle:
            bundle.extractall(unpacked, filter="data")
    except (OSError, tarfile.TarError):
        fail("CNV gate bundle staging failed: archive is invalid.")

    bundle_root = unpacked / "cnv-gate"
    sources = bundle_root / "sources"
    queries_file = bundle_root / "search-quality" / "cnv.search-quality.json"
    source_files = [path for path in sources.rglob("*") if path.is_file()] if sources.is_dir() else []
    try:
        query_document = json.loads(queries_file.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        fail("CNV gate bundle staging failed: query set is missing or invalid.")

    queries = query_document.get("queries") if isinstance(query_document, dict) else None
    if not source_files or not isinstance(queries, list) or not queries:
        fail("CNV gate bundle staging failed: sources and reviewed queries must be non-empty.")

    bundle_root.rename(destination)
    print(f"Bundle SHA-256 verified: {actual}")
    print(f"Staged source files: {len(source_files)}")
    print(f"Staged reviewed queries: {len(queries)}")
finally:
    shutil.rmtree(stage, ignore_errors=True)
PY
```

Do not proceed unless the staged file counts are nonzero and the verified digest equals the independently authorized bundle digest. Staging does not authorize ingestion; the persistent mutation still requires the separately approved, fingerprint-confirmed target.

### Ingest and inspect

Run the commands exactly in this order from the same authenticated SSH session and inherited, fingerprint-confirmed connection environment:

```bash
/app/mcp/CnvRegulation.McpServer ingest --storage postgres --source-directory /tmp/confirmed/cnv-gate/sources || exit 1
/app/mcp/CnvRegulation.McpServer inspect-coverage --storage postgres || exit 1
/app/mcp/CnvRegulation.McpServer validate-search-quality --storage postgres --mode full_text --queries /tmp/confirmed/cnv-gate/search-quality/cnv.search-quality.json || exit 1
```

### Gate semantics

- Schema migration is safe to automate.
- Corpus ingestion is a separate persistent mutation. It requires explicit approval and confirmation of the target database immediately before execution.
- `/health` may be healthy with an empty corpus; it proves MCP transport and database execution, not corpus readiness or retrieval quality.
- Production handoff is blocked until the approved corpus reports non-empty coverage and the full-text search-quality report passes the acceptance rules below.
- Any corpus replacement, refresh, or re-ingestion after first deploy is another persistent mutation and requires the same approval and target confirmation.

`validate-search-quality` currently returns process exit code 0 even when individual report cases fail. Exit status alone is therefore insufficient. The formatter emits the exact summary labels `Queries:`, `Passed:`, and `Failed:`; warnings use `Warnings:`, failed cases use `[FAIL]`, and their failure explanations use `Reason:`. Acceptance requires all of the following:

- `Queries: N` where `N > 0`;
- `Failed: 0`;
- `Passed: N`, exactly equal to `Queries: N`;
- operator review of the complete report showing no `Warnings:`, `[FAIL]`, or `Reason:` lines.

If the coverage or report-content acceptance gate fails, run `exit 1` immediately. On success, record only the approved non-secret evidence and run `exit`. Both paths trigger the cleanup trap before the later API restart/redeploy.

The coverage and quality checks validate retrieval readiness, not a legal conclusion. Operators must preserve source citations and human review; retrieved evidence remains documentary only.

## Production configuration

The Blueprint declares most API values and Render database references. Before deployment, confirm every row below in the appropriate Render service. Values marked as a Render database URI must come from `fromDatabase`; do not paste them into Git or documentation.

| Service | Setting | Required production value or source |
| --- | --- | --- |
| API | `ASPNETCORE_ENVIRONMENT` | `Production` |
| API | `RenderProxy__Enabled` | `true` |
| API | `Cors__AllowedOrigins__0` | `https://ai-orchestration-hitl.onrender.com` (replace with the final static-site HTTPS origin if Render assigns a different origin) |
| API | `ConnectionStrings__orchestrationdb` | Render application database internal URI |
| API | `CNV_REGULATION_DB_CONNECTION_STRING` | Render CNV database internal URI |
| API | `Mcp__CnvRegulation__Required` | `true` |
| API | `Mcp__CnvRegulation__Enabled` | `true` |
| API | `Mcp__CnvRegulation__Command` | `/app/mcp/CnvRegulation.McpServer` |
| API | `Mcp__CnvRegulation__Args__0` | `--storage` |
| API | `Mcp__CnvRegulation__Args__1` | `postgres` |
| API | `ToolCalling__Enabled` | `true` |
| API | `ToolCalling__ExecutionMode` | `PlanDriven` |
| API | `ToolCalling__AllowedTools__0` | `data.analyze_transactions` |
| API | `ToolCalling__AllowedTools__1` | `legal.search_cnv_regulation` |
| API | `Llm__Enabled` | `false` |
| API | `DataAgent__AiReviewEnabled` | `false` |
| API | `DataAgent__FinancialAnalysisToolsEnabled` | `true` |
| API | `DataAgent__UsePythonFinancialAnalysis` | `true` |
| API | `DataAgent__UseFixtureMetricsFallback` | `false` |
| API | `DataAgent__RequireSessionFinancialMetrics` | `true` |
| API | `FinancialMetricsExtraction__MaxConcurrentConversions` | `1` |
| API | `StructuredFinancialMetricsPdfExtraction__TesseractLanguage` | `eng+spa` |
| API | `LegalAgent__AiReviewEnabled` | `false` |
| API | `IdentityBootstrap__Enabled` | `false` |
| Frontend | `VITE_API_URL` | `https://ai-orchestration-hitl-api.onrender.com` (replace with the final API HTTPS origin if Render assigns a different origin) |

### Provider and bootstrap semantics

`ToolCalling__AllowedTools` is an array. Provider configuration replaces the whole array; it does not merge environment entries with an inherited array. Production must therefore declare every allowed entry, including both `ToolCalling__AllowedTools__0` and `ToolCalling__AllowedTools__1`. Omitting either entry removes that tool from the effective production allowlist.

MCP source selection is bootstrap-scoped: dependency injection chooses the CNV MCP source when the API process starts. Changing `Mcp__CnvRegulation__Enabled`, `Mcp__CnvRegulation__Required`, its command, or its arguments on a running process does not switch the source. Apply the flags and restart or redeploy the API. With `Required=true`, startup must fail instead of silently continuing when the configured MCP is unavailable.

## Secrets and identity bootstrap

- Never put a database URI, account password, access token, or AI key in Git.
- Use `sync: false` for operator-supplied Render values and Render `fromDatabase` references for database connections.
- Identity bootstrap is optional and disabled in the production table. If an operator explicitly enables it, supply complete bootstrap user values only through Render secrets, confirm the target, create only the intended missing accounts, then disable it again. Do not record those values in logs or handoff notes.
- Treat frontend `VITE_API_URL` as public build-time configuration, not a secret; it still must be the intended absolute HTTPS API origin.

## Rollback

1. Identify the last known-compatible application image and the database schema version it supports.
2. Roll back the application image and schema only through compatible migrations. Do not deploy an older image against an incompatible newer schema or improvise destructive database changes.
3. Never roll back by automatically replacing the CNV corpus. Any corpus restore or replacement requires separate approval, a confirmed target, and new coverage and search-quality evidence.
4. Repeat the operator validation below after rollback before handoff.

## Operational constraints and logs

- Keep the API at one instance until a SignalR backplane exists. Multiple instances can break deterministic real-time delivery and user isolation without shared connection state.
- Inspect Render deploy and pre-deploy logs for migration, startup, MCP, and health failures.
- Do not dump the environment or copy secret-bearing output into logs, tickets, chat, or handoff notes. Record only redacted resource names, deploy identifiers, timestamps, exit status, and non-sensitive failure details.
- A passing transport health check does not waive the separate corpus gate or authenticated workflow checks.

## Operator-only Render validation

An authenticated Render CLI is not available in the local development environment. Therefore, no local command can prove a Render deployment or production database state. An authorized operator must perform deployment actions in Render and run the checks below against the final HTTPS origins. Except for the public health probe, use authenticated application sessions and never paste credentials or bearer tokens into logs or tickets.

1. **Health:** confirm the final API `/health` response is healthy after the API starts with the required MCP. Separately retain the successful non-empty coverage and full-text quality results; health alone is insufficient.
2. **Login:** sign in as two distinct, explicitly provisioned users. Keep `IdentityBootstrap__Enabled=false` during normal production operation.
3. **Two-user SignalR isolation:** open separate authenticated browser sessions, create or load activity for each user, and verify neither receives the other user's session events or data.
4. **Data analysis:** attach session-specific structured metrics, start analysis, and verify the Python financial-analysis result and audit activity are produced without fixture fallback.
5. **CNV retrieval:** exercise `legal.search_cnv_regulation` through the normal authenticated workflow and verify cited results, warnings, and review disclaimers. Treat retrieved and canonical content as documentary evidence only, never automatic applicability, breach, compliance determination, or legal advice.
6. **Restart token survival:** retain an authenticated session, perform an approved API restart from Render, then call a protected application route with the pre-restart session/token and verify it remains valid. Do not expose the token while recording evidence.

Record the Render deploy identifier, test timestamp, redacted account identifiers, and pass/fail outcome for each check. Block production handoff on any failure.
