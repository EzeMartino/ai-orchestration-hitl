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

## Test

```powershell
dotnet test
```

## Project layout

- `src/CnvRegulation.Domain`: regulatory document, search result, and citation models.
- `src/CnvRegulation.Application`: request/response contracts and service interfaces.
- `src/CnvRegulation.Infrastructure`: in-memory repository, local ingestion, parsers, sidecar metadata reader, and mock service implementations.
- `src/CnvRegulation.McpServer`: stdio MCP host and tool definitions.
- `tests`: xUnit coverage for mock services, contracts, and MCP tool registration.

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
