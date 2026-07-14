---
name: aihitl-cnv-mcp-quality-gate
description: Use when ai-orchestration-hitl work involves the CNV MCP server, cnv_regulation PostgreSQL or pgvector state, ingestion, embeddings, full-text or hybrid search, or regulatory analysis quality.
---

# AIHITL CNV MCP Quality Gate

## Safety gate

Work from `tools/CnvRegulation.McpServer`. Start read-only. Before mutation, inspect configured storage and connection presence, embedding provider, and observed current database schema, corpus, and embedding state. Never print secrets: connection strings, API keys, user-secrets, or environment values.

### Decision / quick reference

| Observed state | Decision |
|---|---|
| In-memory | No database mutation; use unit/in-memory checks. |
| Persistent local PostgreSQL | Confirm ownership and mutation authority; preserve existing state. |
| Shared PostgreSQL | Require explicit target and mutation authority. |
| Disposable Docker | Use only when explicitly designated and authorized; never assume a database is disposable. |

Never reset a database or replace a corpus unless expressly requested.

## Ordered workflow

1. Build and run unit tests: `dotnet build`, then `dotnet test`.
2. Only when observed schema is stale/missing and migration is authorized:
   `dotnet run --project src/CnvRegulation.McpServer -- migrate-db`
3. Only when observed corpus is stale/missing and ingestion is authorized:
   `dotnet run --project src/CnvRegulation.McpServer -- ingest --source-directory data/sources --storage postgres`
4. Run PostgreSQL integration tests only when opted in against the authorized target. Supply its connection through an existing secret-safe channel; never echo it.
   ```powershell
   $env:CNV_REGULATION_RUN_INTEGRATION_TESTS = "true"
   dotnet test
   ```
5. Establish the full-text search baseline:
   `dotnet run --project src/CnvRegulation.McpServer -- validate-search-quality --storage postgres --queries data/search-quality/cnv.search-quality.json --mode full_text`
6. Validate analysis quality:
   `dotnet run --project src/CnvRegulation.McpServer -- validate-analysis-quality --storage postgres --cases data/analysis-quality/cnv.analysis-quality.json`

## Embeddings and comparison

Any paid OpenAI operation requires explicit authorization unless the user already requested it. First run the bounded smoke:

```powershell
$env:DOTNET_ENVIRONMENT = "Development"
dotnet run --project src/CnvRegulation.McpServer -- generate-embeddings --storage postgres --provider OpenAI --limit 25 --only-missing --batch-size 8 --delay-ms 250 --failed-report data/embedding-reports/failed-embeddings.json
```

Inspect failures and cost, then obtain separate authorization for the full run. Run the same resume-safe command without the cap:

```powershell
dotnet run --project src/CnvRegulation.McpServer -- generate-embeddings --storage postgres --provider OpenAI --only-missing --batch-size 8 --delay-ms 250 --failed-report data/embedding-reports/failed-embeddings.json
```

If authorized provider/state supports hybrid, write comparison evidence:

`dotnet run --project src/CnvRegulation.McpServer -- validate-search-quality --storage postgres --compare-modes full_text,hybrid --queries data/search-quality/cnv.search-quality.json --output-report data/search-quality/reports/fulltext-vs-hybrid.review.md`

Keep `full_text` default until a human completes legal relevance review of that report. A 14/14 mechanical pass alone never promotes hybrid. Citations are retrieval evidence, not proof of legal applicability or compliance risk.

## Stop conditions and common mistakes

| Stop or mistake | Response |
|---|---|
| Storage, ownership, authority, or current state is unclear | Stop and ask; do not default to Docker or mutate. |
| Paid-operation authorization is absent | Stop before OpenAI use. Smoke authorization does not cover the full run. |
| A command/output may reveal secrets | Redact or stop; never print the value. |
| A gate fails or comparison lacks human review | Report evidence; do not promote hybrid or claim regulatory safety. |
