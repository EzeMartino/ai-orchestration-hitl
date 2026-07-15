---
name: aihitl-cnv-mcp-quality-gate
description: Use when ai-orchestration-hitl work involves the CNV MCP server, cnv_regulation PostgreSQL or pgvector state, ingestion, embeddings, full-text or hybrid search, or regulatory analysis quality.
---

# AIHITL CNV MCP Quality Gate

## Safety and target binding

From `tools/CnvRegulation.McpServer`, inspect storage, target identity, schema, corpus, and embeddings first. Never print a DSN, password, API key, or secret-bearing environment value.

| Observed state | Decision |
|---|---|
| In-memory | Unit checks; no database mutation. |
| Persistent local/shared PostgreSQL | Confirm owner/target/authority; preserve state. |
| Disposable Docker | Only explicitly authorized; never assume disposability. |

Never reset a database or replace a corpus unless requested. The current CLI has no database-identity or embedding-distribution command. `inspect-coverage` reports coverage only; it does **not** prove provider, connection identity, `embedding_model`, or vector dimensions. With `--source-directory` it ingests, so omit that option during read-only inspection.

For PostgreSQL, load `$authorizedConnectionString` from an approved secret source without echoing it. This executable guard uses the existing PostgreSQL `psql` runtime to fingerprint the connected server/database/user; it never prints connection material:

```powershell
if ([string]::IsNullOrWhiteSpace($authorizedConnectionString)) { throw "Approved CNV connection is required." }
if (-not (Get-Command psql -ErrorAction SilentlyContinue)) {
    throw "The CNV CLI exposes no identity/embedding-state command; psql is required for this preflight. Stop."
}

function Get-DbPart($builder, [string[]]$names, $defaultValue = $null) {
    foreach ($name in $names) {
        if ($builder.ContainsKey($name) -and -not [string]::IsNullOrWhiteSpace([string]$builder[$name])) {
            return [string]$builder[$name]
        }
    }
    return $defaultValue
}

function Get-CnvDbEnvironment([string]$connectionString) {
    $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
    $builder.ConnectionString = $connectionString
    $hostName = Get-DbPart $builder @("Host", "Server")
    $database = Get-DbPart $builder @("Database", "Initial Catalog")
    $userName = Get-DbPart $builder @("Username", "User ID", "UserId")
    if ([string]::IsNullOrWhiteSpace($hostName) -or [string]::IsNullOrWhiteSpace($database) -or [string]::IsNullOrWhiteSpace($userName)) {
        throw "Authorized DSN must explicitly identify host, database, and user."
    }
    $sslMode = (Get-DbPart $builder @("SSL Mode", "SslMode") "Prefer").Replace(" ", "").ToLowerInvariant()
    $sslMode = switch ($sslMode) {
        "verifyca" { "verify-ca" }
        "verifyfull" { "verify-full" }
        default { $sslMode }
    }
    return @{
        PGHOST = $hostName
        PGPORT = Get-DbPart $builder @("Port") "5432"
        PGDATABASE = $database
        PGUSER = $userName
        PGPASSWORD = Get-DbPart $builder @("Password")
        PGSSLMODE = $sslMode
    }
}

function Invoke-WithCnvDbEnvironment([scriptblock]$action) {
    $dbEnvironment = Get-CnvDbEnvironment $authorizedConnectionString
    $names = @("CNV_REGULATION_DB_CONNECTION_STRING") + @($dbEnvironment.Keys)
    $prior = @{}
    foreach ($name in $names) { $prior[$name] = [Environment]::GetEnvironmentVariable($name) }
    try {
        [Environment]::SetEnvironmentVariable("CNV_REGULATION_DB_CONNECTION_STRING", $authorizedConnectionString)
        foreach ($entry in $dbEnvironment.GetEnumerator()) {
            [Environment]::SetEnvironmentVariable([string]$entry.Key, $entry.Value)
        }
        & $action
    } finally {
        foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $prior[$name]) }
    }
}

function Get-ConnectedCnvDbFingerprint {
    $identityLines = @(& psql -XAtqw -v ON_ERROR_STOP=1 -c "SELECT concat_ws('|',current_database(),current_user,coalesce(inet_server_addr()::text,'local'),coalesce(inet_server_port()::text,'local'))")
    if ($LASTEXITCODE -ne 0 -or $identityLines.Count -ne 1) { throw "Could not establish one CNV database identity." }
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes($identityLines[0].Trim())
        return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace("-", "").ToLowerInvariant()
    } finally { $sha.Dispose() }
}

$observedDbFingerprint = Invoke-WithCnvDbEnvironment { Get-ConnectedCnvDbFingerprint }
Write-Output "CNV DB fingerprint (SHA-256): $observedDbFingerprint"

function Invoke-WithAuthorizedCnvDb([scriptblock]$action) {
    if ([string]::IsNullOrWhiteSpace($authorizedDbFingerprint)) { throw "Approved CNV DB fingerprint is required." }
    $guarded = {
        $actualDbFingerprint = Get-ConnectedCnvDbFingerprint
        if ($actualDbFingerprint -ne $authorizedDbFingerprint) { throw "CNV DB fingerprint differs from authorization. Stop." }
        & $action
        if ($LASTEXITCODE -ne 0) { throw "Authorized CNV command failed with exit code $LASTEXITCODE." }
    }.GetNewClosure()
    Invoke-WithCnvDbEnvironment $guarded
}
```

Stop until a human authorization names the displayed fingerprint and operation scope; set `$authorizedDbFingerprint` from that approved record, never automatically from `$observedDbFingerprint`. The helper maps standard host/port/database/user/password/SSL fields. If the DSN needs client certificates, service/pass files, multi-host failover, integrated authentication, or other unmapped behavior, stop and use a separately approved secret-safe DBA preflight. Passwordless DSNs may use an already approved `.pgpass` because `psql -w` never prompts.

Then run read-only evidence against the bound target:

```powershell
Invoke-WithAuthorizedCnvDb { dotnet run --project src/CnvRegulation.McpServer -- inspect-coverage --storage postgres }
Invoke-WithAuthorizedCnvDb { dotnet run --project src/CnvRegulation.McpServer -- generate-embeddings --storage postgres --provider fake --dimensions 1536 --limit 25 --dry-run --only-missing }
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
2. If schema is stale/missing and the exact fingerprint is authorized: `Invoke-WithAuthorizedCnvDb { dotnet run --project src/CnvRegulation.McpServer -- migrate-db }`
3. If corpus is stale/missing and the exact fingerprint is authorized: `Invoke-WithAuthorizedCnvDb { dotnet run --project src/CnvRegulation.McpServer -- ingest --source-directory data/sources --storage postgres }`
4. Integration: dedicated disposable database only, never persistent local/shared corpus. Load its connection and separately approved fingerprint secret-safely; bind, set, and restore both target and opt-in:
   ```powershell
   $priorAuthorizedConnection=$authorizedConnectionString; $priorAuthorizedFingerprint=$authorizedDbFingerprint
   $priorOptIn=$env:CNV_REGULATION_RUN_INTEGRATION_TESTS
   try {
       $authorizedConnectionString=$dedicatedDisposableConnectionString
       $authorizedDbFingerprint=$dedicatedDisposableDbFingerprint
       Invoke-WithAuthorizedCnvDb {
           $env:CNV_REGULATION_RUN_INTEGRATION_TESTS="true"
           dotnet test
       }
   } finally {
       [Environment]::SetEnvironmentVariable("CNV_REGULATION_RUN_INTEGRATION_TESTS",$priorOptIn)
       $authorizedConnectionString=$priorAuthorizedConnection
       $authorizedDbFingerprint=$priorAuthorizedFingerprint
   }
   ```
5. Full-text quality: `Invoke-WithAuthorizedCnvDb { dotnet run --project src/CnvRegulation.McpServer -- validate-search-quality --storage postgres --queries data/search-quality/cnv.search-quality.json --mode full_text }`
6. Analysis quality: `Invoke-WithAuthorizedCnvDb { dotnet run --project src/CnvRegulation.McpServer -- validate-analysis-quality --storage postgres --cases data/analysis-quality/cnv.analysis-quality.json }`

## Paid embeddings and hybrid

Every paid OpenAI operation needs explicit authorization unless the user already requested it. Canonical state is provider `OpenAI`, model `text-embedding-3-small`, dimensions `1536`. Inspect stored model/dimension distribution immediately before generation or comparison:

```powershell
function Assert-CnvEmbeddingProfiles([ValidateSet("Generation", "Comparison")]$purpose) {
    $raw = @(& psql -XAtqw -F "|" -v ON_ERROR_STOP=1 -c "SELECT coalesce(embedding_model,'<missing>'),coalesce(vector_dims(embedding)::text,'<missing>'),count(*)::text FROM regulation_chunks WHERE embedding IS NOT NULL GROUP BY 1,2 ORDER BY 1,2")
    if ($LASTEXITCODE -ne 0) { throw "Could not inspect stored embedding profiles." }
    $profiles = @($raw | ForEach-Object {
        $parts = $_ -split "\|", 3
        if ($parts.Count -ne 3) { throw "Unexpected embedding-profile output." }
        [pscustomobject]@{ Model=$parts[0]; Dimensions=$parts[1]; Count=[int]$parts[2] }
    })
    $profiles | Format-Table Model, Dimensions, Count | Out-Host
    if ($profiles.Count -eq 0) {
        if ($purpose -eq "Comparison") { throw "No stored embeddings; comparison must stop." }
        return
    }
    if ($profiles.Count -ne 1 -or $profiles[0].Model -eq "<missing>" -or $profiles[0].Dimensions -eq "<missing>" -or
        $profiles[0].Model -ne "text-embedding-3-small" -or $profiles[0].Dimensions -ne "1536") {
        throw "Stored embeddings are missing, mixed, or noncanonical. Stop; rebuilding/replacing them needs separate authorization."
    }
}

function Invoke-WithCanonicalOpenAi([scriptblock]$action) {
    $values = @{
        DOTNET_ENVIRONMENT="Development"
        Embeddings__Provider="OpenAI"
        Embeddings__Model="text-embedding-3-small"
        Embeddings__Dimensions="1536"
    }
    $prior = @{}
    foreach ($name in $values.Keys) { $prior[$name]=[Environment]::GetEnvironmentVariable($name) }
    try {
        foreach ($entry in $values.GetEnumerator()) { [Environment]::SetEnvironmentVariable([string]$entry.Key,[string]$entry.Value) }
        & $action
        if ($LASTEXITCODE -ne 0) { throw "Canonical OpenAI command failed with exit code $LASTEXITCODE." }
    } finally {
        foreach ($name in $values.Keys) { [Environment]::SetEnvironmentVariable($name,$prior[$name]) }
    }
}
```

`DOTNET_ENVIRONMENT=Development` enables the documented user-secrets path. The wrapper does not change or print API-key variables; a separately approved `CNV_REGULATION_OPENAI_API_KEY` or `OPENAI_API_KEY` remains a supported alternative.

Run the authorized bounded smoke:

```powershell
Invoke-WithAuthorizedCnvDb {
    Assert-CnvEmbeddingProfiles Generation
    Invoke-WithCanonicalOpenAi {
        dotnet run --project src/CnvRegulation.McpServer -- generate-embeddings --storage postgres --provider OpenAI --model text-embedding-3-small --dimensions 1536 --limit 25 --only-missing --batch-size 8 --delay-ms 250 --failed-report data/embedding-reports/failed-embeddings.json
    }
}
```

Inspect failures/cost; obtain separate full-generation authorization, then run the same guard without the limit:

```powershell
Invoke-WithAuthorizedCnvDb {
    Assert-CnvEmbeddingProfiles Generation
    Invoke-WithCanonicalOpenAi {
        dotnet run --project src/CnvRegulation.McpServer -- generate-embeddings --storage postgres --provider OpenAI --model text-embedding-3-small --dimensions 1536 --only-missing --batch-size 8 --delay-ms 250 --failed-report data/embedding-reports/failed-embeddings.json
    }
}
```

Hybrid comparison creates paid OpenAI query embeddings; authorize it separately. Re-run the SQL profile gate, then a dry-run and require `Eligible chunks: 0`; missing eligible vectors, failed work, missing metadata, mixed models/dimensions, or noncanonical state stop comparison. Existing embeddings are always skipped by generation, so repair/rebuild is a separate mutation requiring explicit authorization.

```powershell
Invoke-WithAuthorizedCnvDb {
    Assert-CnvEmbeddingProfiles Comparison
    dotnet run --project src/CnvRegulation.McpServer -- generate-embeddings --storage postgres --provider fake --dimensions 1536 --dry-run --only-missing
}
Invoke-WithAuthorizedCnvDb {
    Assert-CnvEmbeddingProfiles Comparison
    Invoke-WithCanonicalOpenAi {
        dotnet run --project src/CnvRegulation.McpServer -- validate-search-quality --storage postgres --compare-modes full_text,hybrid --queries data/search-quality/cnv.search-quality.json --output-report data/search-quality/reports/fulltext-vs-hybrid.review.md
    }
}
```

`reviewDecision` contract values `unknown`, `full_text_better`, `equivalent`, and `needs_legal_review` retain `full_text`. Only a human `hybrid_better` decision permits further default-promotion consideration; it never changes the default automatically. Mechanical passes never promote hybrid. Citations are retrieval evidence, never legal-applicability/compliance-risk proof.

## Stop conditions / common mistakes

| Condition | Response |
|---|---|
| Target fingerprint, authority, schema, model distribution, dimensions, or dedicated test DB unclear | Stop and ask; do not mutate/default to Docker. |
| Paid authorization absent | Stop before OpenAI; smoke authorization does not cover full generation/comparison. |
| Stored embeddings missing metadata, mixed, mismatched, or need rebuild | Stop; keep `full_text`; obtain separate rebuild authorization. |
| Gate fails or review is not `hybrid_better` | Keep `full_text`; report evidence; never claim regulatory safety. |
