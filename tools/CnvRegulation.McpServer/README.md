# CNV Regulation MCP Server

MCP server for querying Argentine CNV regulatory material.

## Current status

MVP with mock fallback data, local `.txt`/`.html`/`.pdf` ingestion, curated source discovery/download, in-memory chunking, PDF text normalization, source/chunk quality diagnostics, optional PostgreSQL persistence, PostgreSQL full-text search, and optional pgvector semantic/hybrid search.

Do not use for real regulatory decisions.

## Tools

- `search_cnv_regulation`
- `get_cnv_document`
- `get_cnv_article`
- `get_recent_cnv_resolutions`
- `analyze_text_against_cnv`

## Run

```powershell
dotnet run --project src/CnvRegulation.McpServer
```

## Ingest local sources

Local ingestion reads `.txt`, `.html`, `.htm`, and `.pdf` files from `data/sources`. Each source file must have a sidecar metadata file with the same base name and `.metadata.json` suffix.

## Discover and download sources

Create the curated candidate manifest:

```powershell
dotnet run --project src/CnvRegulation.McpServer -- discover-sources
```

This writes:

```text
data/source-manifest/sources.manifest.json
```

Download candidate files from the manifest:

```powershell
dotnet run --project src/CnvRegulation.McpServer -- download-sources --manifest data/source-manifest/sources.manifest.json
```

Downloaded files and generated metadata sidecars are written to:

```text
data/sources/
```

All discovered/downloaded sources are marked as `status=candidate` and `requiresReview=true`, because regulatory sources may be outdated, partial, modified, consultation-only, or not currently in force.

PDF files can be downloaded and ingested as candidate sources. PDF parsing extracts text only through PdfPig; it does not render pages or perform OCR.

## Discover Infoleg links

Run controlled link discovery over already-downloaded Infoleg HTML files:

```powershell
dotnet run --project src/CnvRegulation.McpServer -- discover-infoleg-links --source-directory data/sources
```

This writes:

```text
data/source-manifest/infoleg.discovered.manifest.json
```

You can override the output manifest and per-source cap:

```powershell
dotnet run --project src/CnvRegulation.McpServer -- discover-infoleg-links --source-directory data/sources --output-manifest data/source-manifest/infoleg.discovered.manifest.json --max-links-per-source 20
```

The discovery is intentionally bounded. It only accepts `http`/`https` links on `servicios.infoleg.gob.ar` under `/infolegInternet/`, normalizes them to `https`, removes fragments, rejects `javascript:`/`mailto:`/external/path-traversal links, deduplicates canonical URLs, classifies links as `norma`, `texact`, `anexos`, `verNorma`, or `unknown`, and emits all entries as `status=candidate` with `requiresReview=true`.

The generated manifest can be downloaded with the existing command:

```powershell
dotnet run --project src/CnvRegulation.McpServer -- download-sources --manifest data/source-manifest/infoleg.discovered.manifest.json
```

Download filters unsupported discovered file types before any HTTP request. Only `.html`, `.htm`, `.pdf`, and `.txt` are allowed; `.css`, `.js`, image, font, icon, and similar assets are skipped.

## Ingest downloaded sources

Default ingestion uses in-memory storage:

```powershell
dotnet run --project src/CnvRegulation.McpServer -- ingest
```

You can pass a custom source directory:

```powershell
dotnet run --project src/CnvRegulation.McpServer -- ingest --source-directory data/sources
```

During ingestion, the server also performs a simple CNV-like legal structure pass. It detects title, chapter, section, and article markers, then stores article-level chunks for citation and search. PDF text is normalized before chunking to reduce common extraction artifacts such as excessive whitespace, numeric page-only lines, simple hyphenated line breaks, repeated header/footer lines, and unaccented legal markers. PDF documents preserve extraction metadata such as `sourceFile`, `fileType`, `extractionMethod`, and `pageCount`.

Ingestion also computes `contentHash` for chunks, marks duplicate chunks with `duplicate=true` and `duplicateOfChunkId`, and marks wrapper-like Infoleg HTML documents with `isWrapperCandidate=true` and `searchable=false`.

Search hides duplicate chunks and non-searchable wrapper documents by default. Use `includeDuplicates=true` or `includeNonSearchable=true` only when auditing the corpus itself. Search ranking adds source-priority bonuses: CNV official material first, then Infoleg `texact`, Infoleg `norma`, Infoleg `anexos`/`verNorma`, and finally unknown candidate sources.

## Inspect downloaded sources

Run local parsing and chunking diagnostics without starting the MCP server:

```powershell
dotnet run --project src/CnvRegulation.McpServer -- inspect-sources --source-directory data/sources
```

The report includes per-source metadata, file type, PDF page count when available, extracted text length, chunk count, detected legal structure markers, first detected articles, and warnings such as candidate source, requires review, unsupported file type, or missing article boundaries.

## Inspect chunk quality

Run chunk quality diagnostics over locally ingested sources:

```powershell
dotnet run --project src/CnvRegulation.McpServer -- inspect-chunks --source-directory data/sources
```

For PostgreSQL, inspect chunks already persisted in the database:

```powershell
dotnet run --project src/CnvRegulation.McpServer -- inspect-chunks --storage postgres
```

If `--source-directory` is also passed with PostgreSQL storage, sources are ingested first and then inspected.

The report includes:

- empty chunks
- very short chunks
- very long chunks
- chunks without article identifiers
- possible duplicate chunks
- suspicious repeated header/footer pollution
- average and median chunk length
- top quality warnings

## Inspect coverage

Run an aggregate coverage report over ingested sources:

```powershell
dotnet run --project src/CnvRegulation.McpServer -- inspect-coverage --source-directory data/sources
```

For PostgreSQL, inspect the persisted repository:

```powershell
dotnet run --project src/CnvRegulation.McpServer -- inspect-coverage --storage postgres
```

If `--source-directory` is passed, sources are ingested before the coverage report. The report includes document, searchable/non-searchable document, chunk, unique searchable chunk, article, source, resolution-number, duplicate URL, duplicate chunk, filtered source, unsupported file, zero-chunk document, possible wrapper, and top warning counts.

## Validate search quality

Run curated search-quality checks against the configured search service:

```powershell
dotnet run --project src/CnvRegulation.McpServer -- validate-search-quality --queries data/search-quality/cnv.search-quality.json
```

For PostgreSQL:

```powershell
dotnet run --project src/CnvRegulation.McpServer -- validate-search-quality --storage postgres --queries data/search-quality/cnv.search-quality.json --mode full_text
```

The report shows pass/fail per query, expanded search queries, result counts, top result source/title/article, score, citation presence, warnings, and expectation failure reasons. The curated JSON is intentionally simple and should be updated as the corpus improves.

## Explain search queries

Run targeted diagnostics for one failing query:

```powershell
dotnet run --project src/CnvRegulation.McpServer -- explain-search-query --storage postgres --query "hecho relevante"
```

The report shows the normalized query, expanded terms, generated search queries with raw result counts, term presence in indexed documents/chunks, duplicate and wrapper filtering effects, top partial matches, applied filters, warnings, and a short recommendation.

Useful filter flags:

- `--source`
- `--document-type`
- `--resolution-number`
- `--status`
- `--requires-review true|false`
- `--include-duplicates`
- `--include-non-searchable`

## PostgreSQL persistence

Default storage is in-memory. No database is required for local mock/dev mode:

```powershell
dotnet run --project src/CnvRegulation.McpServer -- ingest --source-directory data/sources
```

To use PostgreSQL, set a connection string through the environment or `appsettings.json`:

```powershell
$env:CNV_REGULATION_DB_CONNECTION_STRING = "Host=localhost;Port=5432;Database=cnv_regulation;Username=postgres;Password=postgres"
```

Create or update the schema:

```powershell
dotnet run --project src/CnvRegulation.McpServer -- migrate-db
```

Ingest local sources into PostgreSQL:

```powershell
dotnet run --project src/CnvRegulation.McpServer -- ingest --source-directory data/sources --storage postgres
```

PostgreSQL ingestion upserts `regulation_documents`, then replaces chunks for each document in `regulation_chunks`. Reingesting the same source should not duplicate chunks.

PostgreSQL search uses full-text indexes over `regulation_chunks.text` and `regulation_documents.text`. Chunk matches are returned first with ranking and citations. If no chunks match, the search falls back to document-level matches.

Search queries are normalized and expanded through a bounded alias dictionary before full-text search. The response preserves the original query in `query`; when expansion is applied, warnings include the expanded terms and search queries used. PostgreSQL executes each expanded query with `websearch_to_tsquery`, merges duplicate `chunkId` results, keeps the best score with a small multi-query bonus, and preserves citations.

Initial aliases include:

- `alyc`: agente/agentes de liquidacion y compensacion
- `alac`: agente de liquidacion y compensacion
- `fci`: fondo/fondos comunes de inversion
- `cnv`: comision nacional de valores
- `oferta publica`: oferta publica, regimen de oferta publica
- `hecho relevante`: hechos relevantes, informacion relevante, informaciones relevantes
- `informacion relevante`: hecho relevante, hechos relevantes, informaciones relevantes
- `fiduciario financiero`: fiduciarios financieros, fideicomiso financiero, fideicomisos financieros
- `emisora`: emisor, emisoras, entidad emisora, sociedad emisora, entidades emisoras, emisores
- `lavado`: prevencion de lavado, financiamiento del terrorismo, pld, uif
- `idoneidad`: examen de idoneidad, idoneos, personal idoneo
- `regimen informativo`: informacion periodica, deber de informar, informes

`search_cnv_regulation` supports these optional filters:

- `source`
- `documentType`
- `resolutionNumber`
- `status`
- `requiresReview`

## Semantic search with pgvector

Full-text search remains the default. Semantic and hybrid modes require PostgreSQL with the pgvector extension installed.

Schema migration enables pgvector and adds chunk embedding columns:

- `embedding vector(1536)`
- `embedding_model`
- `embedding_generated_at`

For the current corpus size, semantic search uses exact vector ordering instead of HNSW/IVFFlat:

```sql
ORDER BY embedding <=> @queryEmbedding
```

Generate deterministic local embeddings without an API key:

```powershell
dotnet run --project src/CnvRegulation.McpServer -- migrate-db
dotnet run --project src/CnvRegulation.McpServer -- generate-embeddings --storage postgres --provider fake
```

Safe generation options:

- `--limit 25`: cap generated embeddings for smoke tests.
- `--dry-run`: report eligible work without persisting embeddings.
- `--only-missing`: resume mode; skip chunks that already have embeddings.
- `--batch-size 32`: process generated embeddings in batches.
- `--delay-ms 250`: wait between batches for basic rate limiting.

Real OpenAI smoke test:

```powershell
$env:DOTNET_ENVIRONMENT = "Development"
dotnet run --project src/CnvRegulation.McpServer -- generate-embeddings --storage postgres --provider OpenAI --limit 25 --only-missing --batch-size 8 --delay-ms 250
```

Run hybrid validation:

```powershell
dotnet run --project src/CnvRegulation.McpServer -- validate-search-quality --storage postgres --mode hybrid --queries data/search-quality/cnv.search-quality.json
```

Compare full-text baseline against hybrid:

```powershell
$env:DOTNET_ENVIRONMENT = "Development"
$env:Embeddings__Provider = "OpenAI"
$env:Embeddings__Model = "text-embedding-3-small"
dotnet run --project src/CnvRegulation.McpServer -- validate-search-quality --storage postgres --compare-modes full_text,hybrid --queries data/search-quality/cnv.search-quality.json
```

`search_cnv_regulation` accepts `searchMode`:

- `full_text` (default)
- `semantic`
- `hybrid`

Hybrid mode merges existing full-text candidates with pgvector candidates and includes score diagnostics in result metadata. The score combines normalized full-text score, vector score, source priority, and multi-query bonus while preserving duplicate/wrapper filtering.

Configuration:

```json
{
  "Embeddings": {
    "Enabled": false,
    "Provider": "Fake",
    "Model": "text-embedding-3-small",
    "Dimensions": 1536
  }
}
```

The `OpenAI` provider is available behind config, user-secrets, or environment variables, but normal build/test never requires an API key. Use `CNV_REGULATION_OPENAI_API_KEY` or `OPENAI_API_KEY` only when explicitly generating real embeddings. Full-text search remains the default search mode.

PostgreSQL integration tests are skipped by default. To run them:

```powershell
$env:CNV_REGULATION_RUN_INTEGRATION_TESTS = "true"
$env:CNV_REGULATION_DB_CONNECTION_STRING = "Host=localhost;Port=5432;Database=cnv_regulation;Username=postgres;Password=postgres"
dotnet test
```

Use a PostgreSQL image with pgvector installed, such as `pgvector/pgvector:pg16`, when running the pgvector integration path.

This phase does not add advanced compliance analysis, unrestricted crawler changes, Semantic Kernel, or new app integration.

## Test

```powershell
dotnet test
```

## Project layout

- `src/CnvRegulation.Domain`: regulatory document, chunk, search result, and citation models.
- `src/CnvRegulation.Application`: request/response contracts plus document, chunking, repository, query expansion, and service interfaces.
- `src/CnvRegulation.Infrastructure`: in-memory and PostgreSQL repositories, PostgreSQL full-text/semantic/hybrid search, deterministic and OpenAI embedding generators, query alias expansion, local ingestion, diagnostics, controlled Infoleg link discovery, text/HTML/PDF parsers, PDF normalizer, chunk quality inspector, chunker, legal structure detector, sidecar metadata reader, and mock service implementations.
- `src/CnvRegulation.McpServer`: stdio MCP host, CLI commands, and tool definitions.
- `tests`: xUnit coverage for mock services, contracts, diagnostics, repository behavior, and MCP tool registration.

## Chunking MVP

The current chunker is intentionally simple and local-only. It detects article boundaries like:

```text
ARTICULO 1.-
Articulo 1
Art. 2.-
ARTICULO 99.-
```

It also tracks current markers for:

```text
TITULO I
CAPITULO I
SECCION I
```

When chunks exist, `search_cnv_regulation` returns chunk-level results first, including article-level citations. `get_cnv_article` also checks ingested chunks before using mock fallback data.

## Local source format

Example:

```text
data/sources/cnv-nt-2013.sample.txt
data/sources/cnv-nt-2013.sample.metadata.json
```

Metadata shape:

```json
{
  "id": "cnv-nt-2013-sample",
  "source": "CNV",
  "documentType": "Texto Ordenado",
  "resolutionNumber": "622/2013",
  "title": "Normas CNV N.T. 2013 - Sample",
  "publicationDate": "2013-09-09",
  "effectiveDate": "2013-09-09",
  "url": "https://www.cnv.gov.ar/",
  "status": "candidate",
  "requiresReview": true,
  "retrievedAt": "2026-05-08T00:00:00Z"
}
```

## Future phases

1. Improve local ingestion from CNV / Infoleg / Boletin Oficial files
2. pgvector hybrid search
3. Real citations
4. Integration with LegalAgent

## Design note

This server does not provide legal advice. It should search regulation, return fragments, cite sources, expose metadata, and warn when data is mock or may require validation.

No citation, no answer.
