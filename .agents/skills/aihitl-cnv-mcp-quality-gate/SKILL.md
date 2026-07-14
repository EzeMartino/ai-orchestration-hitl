---
name: aihitl-cnv-mcp-quality-gate
description: Use when ai-orchestration-hitl work involves the CNV MCP server, cnv_regulation PostgreSQL or pgvector state, ingestion, embeddings, full-text or hybrid search, or regulatory analysis quality.
---

# AIHITL CNV MCP Quality Gate

## Safety and read-only preflight

From `tools/CnvRegulation.McpServer`, inspect storage/connection presence, provider, schema, corpus, and embeddings first. Never print secrets or secret-bearing environment values.

| Observed state | Decision |
|---|---|
| In-memory | Unit checks; no database mutation. |
| Persistent local/shared PostgreSQL | Confirm owner/target/authority; preserve state. |
| Disposable Docker | Only explicitly authorized; never assume disposability. |

Never reset a database or replace a corpus unless requested. Preflight:

```powershell
dotnet run --project src/CnvRegulation.McpServer -- inspect-coverage --storage postgres
dotnet run --project src/CnvRegulation.McpServer -- generate-embeddings --storage postgres --provider fake --limit 25 --dry-run --only-missing
```

Require coverage totals, dry-run scanned/missing/eligible/already-embedded counts, and `Dry run only. No embeddings were generated or persisted.` Connection/schema failure: request migration authority. Empty/stale coverage: request ingestion authority. Neither permits mutation.

## Ordered gate

1. Clear integration opt-in for baseline; verify/restore:
   ```powershell
   $priorOptIn=$env:CNV_REGULATION_RUN_INTEGRATION_TESTS
   try {
       Remove-Item Env:CNV_REGULATION_RUN_INTEGRATION_TESTS -ErrorAction SilentlyContinue
       if(Test-Path Env:CNV_REGULATION_RUN_INTEGRATION_TESTS){throw "Integration opt-in remains set."}
       dotnet build
       dotnet test
   } finally { [Environment]::SetEnvironmentVariable("CNV_REGULATION_RUN_INTEGRATION_TESTS",$priorOptIn) }
   ```
2. If schema is stale/missing and authorized: `dotnet run --project src/CnvRegulation.McpServer -- migrate-db`
3. If corpus is stale/missing and authorized: `dotnet run --project src/CnvRegulation.McpServer -- ingest --source-directory data/sources --storage postgres`
4. Integration: dedicated disposable database only, never persistent local/shared corpus. Load `$dedicatedDisposableConnectionString` secret-safely; scope/restore:
   ```powershell
   $priorOptIn=$env:CNV_REGULATION_RUN_INTEGRATION_TESTS; $priorDb=$env:CNV_REGULATION_DB_CONNECTION_STRING
   try {
       $env:CNV_REGULATION_RUN_INTEGRATION_TESTS="true"
       $env:CNV_REGULATION_DB_CONNECTION_STRING=$dedicatedDisposableConnectionString
       dotnet test
   } finally {
       [Environment]::SetEnvironmentVariable("CNV_REGULATION_RUN_INTEGRATION_TESTS",$priorOptIn)
       [Environment]::SetEnvironmentVariable("CNV_REGULATION_DB_CONNECTION_STRING",$priorDb)
   }
   ```
5. Full-text quality: `dotnet run --project src/CnvRegulation.McpServer -- validate-search-quality --storage postgres --queries data/search-quality/cnv.search-quality.json --mode full_text`
6. Analysis quality: `dotnet run --project src/CnvRegulation.McpServer -- validate-analysis-quality --storage postgres --cases data/analysis-quality/cnv.analysis-quality.json`

## Paid embeddings and hybrid

Every paid OpenAI operation needs explicit authorization unless the user already requested it. Run bounded smoke first:

```powershell
$priorEnvironment=$env:DOTNET_ENVIRONMENT
try {
    $env:DOTNET_ENVIRONMENT="Development"
    dotnet run --project src/CnvRegulation.McpServer -- generate-embeddings --storage postgres --provider OpenAI --limit 25 --only-missing --batch-size 8 --delay-ms 250 --failed-report data/embedding-reports/failed-embeddings.json
} finally { [Environment]::SetEnvironmentVariable("DOTNET_ENVIRONMENT",$priorEnvironment) }
```

Inspect failures/cost; obtain separate full-generation authorization, then run:

`dotnet run --project src/CnvRegulation.McpServer -- generate-embeddings --storage postgres --provider OpenAI --only-missing --batch-size 8 --delay-ms 250 --failed-report data/embedding-reports/failed-embeddings.json`

Hybrid comparison creates paid OpenAI query embeddings; authorize explicitly. Confirm corpus dimensions `1536`; stop on mismatch. Scope/restore:

```powershell
$priorProvider=$env:Embeddings__Provider; $priorModel=$env:Embeddings__Model; $priorDimensions=$env:Embeddings__Dimensions
try {
    $env:Embeddings__Provider="OpenAI"; $env:Embeddings__Model="text-embedding-3-small"; $env:Embeddings__Dimensions="1536"
    dotnet run --project src/CnvRegulation.McpServer -- validate-search-quality --storage postgres --compare-modes full_text,hybrid --queries data/search-quality/cnv.search-quality.json --output-report data/search-quality/reports/fulltext-vs-hybrid.review.md
} finally {
    [Environment]::SetEnvironmentVariable("Embeddings__Provider",$priorProvider)
    [Environment]::SetEnvironmentVariable("Embeddings__Model",$priorModel)
    [Environment]::SetEnvironmentVariable("Embeddings__Dimensions",$priorDimensions)
}
```

Keep `full_text` default pending human legal-relevance review of comparison evidence. A 14/14 mechanical pass alone never promotes hybrid. Citations are retrieval evidence, not proof of legal applicability or compliance risk.

## Stop conditions / common mistakes

| Condition | Response |
|---|---|
| State, authority, dimensions, or dedicated test DB unclear | Stop and ask; do not mutate/default to Docker. |
| Paid authorization absent | Stop before OpenAI; smoke authorization does not cover full generation/comparison. |
| Gate fails or report lacks human review | Report evidence; never claim regulatory safety. |
