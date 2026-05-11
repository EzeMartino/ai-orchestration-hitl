# CNV Regulation MCP Server

MCP server for querying Argentine CNV regulatory material.

## Current status

MVP with mock fallback data, local `.txt`/`.html`/`.pdf` ingestion, curated source discovery/download, in-memory chunking, source inspection diagnostics, optional PostgreSQL persistence, and PostgreSQL full-text search.

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

## Ingest downloaded sources

Default ingestion uses in-memory storage:

```powershell
dotnet run --project src/CnvRegulation.McpServer -- ingest
```

You can pass a custom source directory:

```powershell
dotnet run --project src/CnvRegulation.McpServer -- ingest --source-directory data/sources
```

During ingestion, the server also performs a simple CNV-like legal structure pass. It detects title, chapter, section, and article markers, then stores article-level chunks for citation and search. PDF documents preserve extraction metadata such as `sourceFile`, `fileType`, `extractionMethod`, and `pageCount`.

## Inspect downloaded sources

Run local parsing and chunking diagnostics without starting the MCP server:

```powershell
dotnet run --project src/CnvRegulation.McpServer -- inspect-sources --source-directory data/sources
```

The report includes per-source metadata, file type, PDF page count when available, extracted text length, chunk count, detected legal structure markers, first detected articles, and warnings such as candidate source, requires review, unsupported file type, or missing article boundaries.

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

`search_cnv_regulation` supports these optional filters:

- `source`
- `documentType`
- `resolutionNumber`
- `status`
- `requiresReview`

PostgreSQL integration tests are skipped by default. To run them:

```powershell
$env:CNV_REGULATION_RUN_INTEGRATION_TESTS = "true"
$env:CNV_REGULATION_DB_CONNECTION_STRING = "Host=localhost;Port=5432;Database=cnv_regulation;Username=postgres;Password=postgres"
dotnet test
```

This phase does not add pgvector, embeddings, crawler/link-following, Semantic Kernel, or new app integration.

## Test

```powershell
dotnet test
```

## Project layout

- `src/CnvRegulation.Domain`: regulatory document, chunk, search result, and citation models.
- `src/CnvRegulation.Application`: request/response contracts plus document, chunking, repository, and service interfaces.
- `src/CnvRegulation.Infrastructure`: in-memory and PostgreSQL repositories, PostgreSQL full-text search, local ingestion, diagnostics, text/HTML/PDF parsers, chunker, legal structure detector, sidecar metadata reader, and mock service implementations.
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
