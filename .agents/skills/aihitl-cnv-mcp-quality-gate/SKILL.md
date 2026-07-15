---
name: aihitl-cnv-mcp-quality-gate
description: Use when ai-orchestration-hitl work involves the CNV MCP server, cnv_regulation PostgreSQL or pgvector state, ingestion, embeddings, full-text or hybrid search, or regulatory analysis quality.
---

# AIHITL CNV MCP Quality Gate

Work in `tools/CnvRegulation.McpServer`. Never print DSN, password, API key, or secret environment values. Reset/corpus replacement/embedding rebuild/paid OpenAI/default changes need explicit authorization.

| Observed storage | Path |
|---|---|
| In-memory | Build/unit only; no DB mutation. |
| Persistent local/shared PostgreSQL | Authorize owner/target/operation; bind approved fingerprint; preserve state. |
| Disposable Docker | Designated/authorized only; never assume disposable. |

## Read-only preflight

Inspect before mutation:

1. Resolve effective non-secret storage/provider/model/dimensions from CLI, `DOTNET_ENVIRONMENT`, `Embeddings__*`, and `appsettings*.json`. Check connection presence only; never its value.
2. Load the approved connection secret-safely. Require `psql`; missing tooling/options means stop. Its target must equal the application's `CNV_REGULATION_DB_CONNECTION_STRING` target.
3. Run read-only checks; emit only fingerprint. Inspect pgvector/schema, profiles, corpus, coverage.

```powershell
$builder=[Data.Common.DbConnectionStringBuilder]::new(); $builder.set_ConnectionString($approvedConnectionString)
$allowed=@("Host","Server","Port","Database","Initial Catalog","Username","User ID","UserId","Password")
if (@($builder.Keys | Where-Object { $_ -notin $allowed }).Count) { throw "Connection options cannot be safely mapped. Stop." }
function DbValue([string[]]$names,$default=$null) { foreach($name in $names){if($builder.ContainsKey($name)){return [string]$builder[$name]}}; $default }
$pg=@{PGHOST=DbValue @("Host","Server"); PGPORT=DbValue @("Port") "5432"; PGDATABASE=DbValue @("Database","Initial Catalog"); PGUSER=DbValue @("Username","User ID","UserId"); PGPASSWORD=DbValue @("Password")}
if ($pg.Values | Where-Object {[string]::IsNullOrWhiteSpace($_)}) { throw "Connection cannot be mapped without ambient psql state. Stop." }
$names=@("CNV_REGULATION_DB_CONNECTION_STRING")+@($pg.Keys); $prior=@{}
foreach($name in $names){$prior[$name]=[Environment]::GetEnvironmentVariable($name)}
try {
    $env:CNV_REGULATION_DB_CONNECTION_STRING=$approvedConnectionString
    foreach($entry in $pg.GetEnumerator()){[Environment]::SetEnvironmentVariable($entry.Key,$entry.Value)}
    $identity=@(& psql -Xw -v ON_ERROR_STOP=1 -Atc "SELECT concat_ws('|',current_database(),current_user,coalesce(inet_server_addr()::text,'local'),coalesce(inet_server_port()::text,'local'))")
    if($LASTEXITCODE -ne 0 -or $identity.Count -ne 1){$identity=$null;throw "Database identity check failed. Stop."}
    $sha=[Security.Cryptography.SHA256]::Create()
    try{$fingerprint=([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($identity[0].Trim())))).Replace("-","").ToLowerInvariant()}
    finally{$identity=$null;$sha.Dispose()}
    if([string]::IsNullOrWhiteSpace($expectedFingerprint) -or $fingerprint -ne $expectedFingerprint){throw "Fingerprint is not separately authorized. Stop."}
    Write-Output "CNV DB fingerprint (SHA-256): $fingerprint"
    psql -Xw -v ON_ERROR_STOP=1 -c "SELECT extversion FROM pg_extension WHERE extname='vector'; SELECT column_name,data_type FROM information_schema.columns WHERE table_name='regulation_chunks' AND column_name IN ('embedding','embedding_model');"
    if($LASTEXITCODE -ne 0){throw "Schema check failed. Stop."}
    psql -Xw -v ON_ERROR_STOP=1 -c "SELECT coalesce(embedding_model,'missing'),coalesce(vector_dims(embedding)::text,'missing'),count(*) FROM regulation_chunks WHERE embedding IS NOT NULL GROUP BY 1,2 ORDER BY 1,2;"
    if($LASTEXITCODE -ne 0){throw "Embedding profile check failed. Stop."}
    dotnet run --project src/CnvRegulation.McpServer -- inspect-coverage --storage postgres
    dotnet run --project src/CnvRegulation.McpServer -- generate-embeddings --storage postgres --provider fake --dimensions 1536 --dry-run --only-missing
} finally { foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$prior[$name])} }
```

`inspect-coverage` proves coverage totals only—not connection identity, provider, model, or dimensions. Never add `--source-directory` to read-only inspection because that ingests.

## Authorization and ordered gate

For every persistent PostgreSQL command, reuse this scope after owner/target/operation authorization: map the same approved secret to PG/app variables, verify `$expectedFingerprint`, execute, then restore all variables. Never use ambient `psql` or reuse authorization across targets.

1. Save/clear/restore `CNV_REGULATION_RUN_INTEGRATION_TESTS`; run `dotnet build`, then `dotnet test`.
2. If observed state requires it and the mutation is authorized:
   - `dotnet run --project src/CnvRegulation.McpServer -- migrate-db`
   - `dotnet run --project src/CnvRegulation.McpServer -- ingest --source-directory data/sources --storage postgres`
3. Run opt-in integration tests only against a separately authorized dedicated disposable database. Bind its exact connection/fingerprint, set `CNV_REGULATION_RUN_INTEGRATION_TESTS=true`, run `dotnet test`, then restore both variables. Never target persistent local/shared corpus.
4. Run full-text and analysis gates:
   - `dotnet run --project src/CnvRegulation.McpServer -- validate-search-quality --storage postgres --queries data/search-quality/cnv.search-quality.json --mode full_text`
   - `dotnet run --project src/CnvRegulation.McpServer -- validate-analysis-quality --storage postgres --cases data/analysis-quality/cnv.analysis-quality.json`

## Paid embeddings and hybrid

Canonical state is `OpenAI` / `text-embedding-3-small` / `1536`. Before each generation/comparison, rerun stored-profile SQL. No existing embeddings permits separately authorized initial generation. Any stored null model/dimension, mixed profiles, or mismatch stops work; replacement/rebuild needs separate authorization. Before comparison, the dry-run must also show `Eligible chunks: 0`.

For smoke, full generation, and comparison, save/set/restore `DOTNET_ENVIRONMENT=Development` for user-secrets. Approved `CNV_REGULATION_OPENAI_API_KEY` or `OPENAI_API_KEY` also works. Never print keys. Smoke authorization excludes full generation/comparison.

Run the bounded smoke:

```powershell
dotnet run --project src/CnvRegulation.McpServer -- generate-embeddings --storage postgres --provider OpenAI --model text-embedding-3-small --dimensions 1536 --limit 25 --only-missing --batch-size 8 --delay-ms 250 --failed-report data/embedding-reports/failed-embeddings.json
```

Review failures and `Actual tokens`/spend; only then obtain separate full-run authorization:

```powershell
dotnet run --project src/CnvRegulation.McpServer -- generate-embeddings --storage postgres --provider OpenAI --model text-embedding-3-small --dimensions 1536 --only-missing --batch-size 8 --delay-ms 250 --failed-report data/embedding-reports/failed-embeddings.json
```

For comparison also save/set/restore `Embeddings__Provider=OpenAI`, `Embeddings__Model=text-embedding-3-small`, and `Embeddings__Dimensions=1536`:

```powershell
dotnet run --project src/CnvRegulation.McpServer -- validate-search-quality --storage postgres --compare-modes full_text,hybrid --queries data/search-quality/cnv.search-quality.json --output-report data/search-quality/reports/fulltext-vs-hybrid.review.md
```

| Human `reviewDecision` | Default decision |
|---|---|
| `unknown`, `full_text_better`, `equivalent`, `needs_legal_review` | Keep `full_text`. |
| `hybrid_better` | Permit further human promotion consideration; never automatic. |

Mechanical pass counts never promote hybrid. Citations are retrieval evidence, not proof of legal applicability or compliance risk.

## Stop conditions / mistakes

| Condition | Response |
|---|---|
| Target/config/fingerprint, schema, corpus, authority, or dedicated DB unclear | Stop; do not mutate or assume Docker is disposable. |
| `psql` unavailable or application/inspection targets cannot be proven identical | Stop; do not invent an identity command. |
| Stored embeddings missing metadata, mixed, mismatched, or needing rebuild | Stop; keep `full_text`; request separate rebuild authorization. |
| Paid authorization absent or gate/review fails | Stop; report evidence; never claim regulatory safety. |
