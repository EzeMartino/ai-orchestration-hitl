# CNV Regulation MCP Server

MCP server for querying Argentine CNV regulatory material.

## Current status

MVP with mock data only.

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

Local ingestion reads `.txt`, `.html`, and `.htm` files from `data/sources`. Each source file must have a sidecar metadata file with the same base name and `.metadata.json` suffix.

```powershell
dotnet run --project src/CnvRegulation.McpServer -- ingest
```

You can pass a custom source directory:

```powershell
dotnet run --project src/CnvRegulation.McpServer -- ingest --source-directory data/sources
```

During ingestion, the server also performs a simple CNV-like legal structure pass. It detects title, chapter, section, and article markers, then stores article-level chunks in memory for citation and search.

## Test

```powershell
dotnet test
```

## Project layout

- `src/CnvRegulation.Domain`: regulatory document, chunk, search result, and citation models.
- `src/CnvRegulation.Application`: request/response contracts plus document, chunking, repository, and service interfaces.
- `src/CnvRegulation.Infrastructure`: in-memory repository, local ingestion, parsers, chunker, legal structure detector, sidecar metadata reader, and mock service implementations.
- `src/CnvRegulation.McpServer`: stdio MCP host and tool definitions.
- `tests`: xUnit coverage for mock services, contracts, and MCP tool registration.

## Chunking MVP

The current chunker is intentionally simple and local-only. It detects article boundaries like:

```text
ARTÍCULO 1°.-
Artículo 1
ARTICULO 99.-
```

It also tracks current markers for:

```text
TÍTULO I
CAPÍTULO I
SECCIÓN I
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
  "status": "mock"
}
```

## Future phases

1. Local ingestion from CNV / Infoleg / Boletin Oficial files
2. PostgreSQL storage
3. Full-text search
4. pgvector hybrid search
5. Real citations
6. Integration with LegalAgent

## Design note

This server does not provide legal advice. It should search regulation, return fragments, cite sources, expose metadata, and warn when data is mock or may require validation.

No citation, no answer.
