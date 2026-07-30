# CNV Search-Hit Enrichment Design

**Issue:** [#8 — Verify search hits with CNV document and article retrieval](https://github.com/EzeMartino/ai-orchestration-hitl/issues/8)

**Date:** 2026-07-21

**Status:** Approved design

## Problem

The Legal workflow currently consumes the snippets and citations returned by
`search_cnv_regulation`. The CNV MCP server also exposes read-only
`get_cnv_document` and `get_cnv_article` tools, but the orchestration-side MCP
client cannot call them. A search hit can therefore reach legal review without
the surrounding document, canonical article text, or an explicit record of
whether the hit was verified against the source repository.

The existing document and article services also synthesize mock placeholder
content when a requested item is missing. Placeholder text must not be treated
as canonical regulatory evidence.

## Goals

- Enrich approved search hits through typed document and article retrieval.
- Keep selection deterministic, read-only, sequential, bounded, cancellable,
  and auditable.
- Preserve the original search snippet/citation separately from canonical
  document/article context.
- Fail closed on identity conflicts and degrade missing, timed-out, or malformed
  retrieval to explicit limitations.
- Propagate enrichment through direct Legal, plan-driven Legal, persisted
  session context, and the operator UI.
- Preserve issue #7 semantics: retrieval or enrichment does not establish legal
  applicability, non-compliance, or compliance risk.

## Non-goals

- No LLM- or Planner-selected enrichment candidates.
- No legal applicability decision, risk promotion, or violation finding.
- No retries, background enrichment, corpus mutation, ingestion, migrations,
  embedding generation, or search-mode changes.
- No attempt to display or persist unbounded regulatory documents.
- No replacement of original snippets or citations with canonical content.

## Approved Decisions

- Approval is automatic and deterministic, not a separate human gate.
- A candidate requires a finite score of at least `0.40`, a non-empty
  `DocumentId`, and at least one citation.
- At most two candidates are enriched across the whole Legal review.
- Each candidate can issue at most one document call and one article call.
- Calls execute sequentially. No automatic retry is performed.
- Conflicts fail closed: both versions remain auditable, canonical content is
  excluded from legal review, and human review remains required.
- Partial enrichment is retained when one canonical lookup succeeds and the
  other lookup fails.
- Enrichment is persisted and rendered in the Legal compliance panel.
- Missing document/article data is explicit; the server no longer fabricates
  mock fallback content for these tools.

## Architecture

The flow is:

```text
search_cnv_regulation
  -> aggregate and deduplicate all search results
  -> deterministic global candidate selection
  -> typed sequential document/article enrichment
  -> evidence assessment and legal review
  -> Legal/Planner propagation
  -> persisted context and operator UI
```

### Typed MCP client

`ICnvRegulationMcpClient` gains:

```csharp
Task<CnvRegulationDocumentResponse> GetDocumentAsync(
    CnvRegulationDocumentRequest request,
    CancellationToken cancellationToken);

Task<CnvRegulationArticleResponse> GetArticleAsync(
    CnvRegulationArticleRequest request,
    CancellationToken cancellationToken);
```

The orchestration project defines transport DTOs rather than referencing the
MCP server projects. `CnvRegulationStdioMcpClient` factors the common tool-call
path so search, document, and article calls share:

- connection setup and the existing single-client lock;
- linked caller cancellation and `ToolCallTimeoutSeconds`;
- error extraction, malformed structured-content handling, telemetry, and
  transport reset behavior;
- typed structured-content deserialization with JSON-text fallback.

Tool names and argument shapes are fixed:

- `get_cnv_document`: `documentId`;
- `get_cnv_article`: `article`, optional `title`, `chapter`, and `section`.

### Deterministic enricher

A dedicated `CnvRegulatoryHitEnricher` owns selection, verification, bounds,
conflict detection, and audit construction. `McpRegulatoryKnowledgeSource`
invokes it only after all configured search queries finish, which makes the
two-hit budget global rather than per query.

Transport remains in the client. Legal policy remains in the enricher. The
Planner never decides which hit to enrich.

## MCP Server Contract Hardening

Document response:

```csharp
public sealed class GetRegulationDocumentResponse
{
    public required bool Found { get; init; }
    public RegulationDocument? Document { get; init; }
    public required IReadOnlyList<RegulationCitation> Citations { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}
```

Article response:

```csharp
public sealed class GetRegulationArticleResponse
{
    public required bool Found { get; init; }
    public string? Text { get; init; }
    public RegulationCitation? Citation { get; init; }
    public double Confidence { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}
```

For a missing document, `Found=false`, `Document=null`, `Citations=[]`, and an
explicit warning. For a missing article, `Found=false`, `Text=null`,
`Citation=null`, `Confidence=0`, and an explicit warning. `Found=true` requires
the corresponding non-null content fields.
The in-memory services stop using `MockRegulationData` as a missing-item
fallback. Existing mock data can remain for explicit test fixtures, but it is
not returned in response to an unknown document or article request.

Tool descriptions are updated to describe repository-backed regulatory data,
not mock content.

## Candidate Selection

Selection operates on the aggregate of every successful search query.

### Eligibility

A search result is eligible when:

1. `Score` is finite and `Score >= 0.40`;
2. `DocumentId` is non-empty after trimming;
3. at least one non-null citation exists.

Eligibility reuses the issue #7 relevance boundary but does not change the
existing relevance classification.

### Primary citation

All original citations remain in top-level retrieval evidence. For enrichment,
one primary citation is selected deterministically:

1. citations with an article locator;
2. citations with a section locator;
3. citations with a chapter locator;
4. remaining citations;
5. then ordinal ordering by normalized source, title, locator, and URL.

An article call occurs only when the primary citation or search result provides
a non-empty article locator. Document-only enrichment remains valid.

### Deduplication and ranking

The candidate key is:

```text
normalized DocumentId + normalized primary locator
```

Duplicate candidates retain the highest score and record every contributing
query index in the audit. Candidates sort by:

1. score descending;
2. normalized `DocumentId` ordinal;
3. normalized locator ordinal;
4. normalized `ChunkId` ordinal.

The first two candidates are selected. A per-run document cache prevents a
second document call when selected candidates reference the same document.
Each selected article locator can produce at most one article call.

## Application Contracts

New Application-layer records keep original and canonical evidence separate.
The contract names and serialized shape below are fixed by this design.

```csharp
public sealed record RegulatoryEvidenceEnrichment(
    string EnrichmentId,
    string DocumentId,
    string? ChunkId,
    int Rank,
    double Score,
    RegulatoryOriginalEvidence Original,
    RegulatoryCanonicalDocument? Document,
    RegulatoryCanonicalArticle? Article,
    string Status,
    IReadOnlyList<string> Limitations);
```

`RegulatoryOriginalEvidence` contains the original snippet and a snapshot of
the selected original citation. `RegulatoryCanonicalDocument` contains stable
document identity, source metadata, dates, status, review flag, bounded text,
original text length, and a truncation flag. `RegulatoryCanonicalArticle`
contains the returned citation, bounded text, confidence, original text length,
and a truncation flag.

Stable status codes are:

- `Verified`: every attempted canonical lookup succeeded and identities agree;
- `Partial`: at least one canonical lookup succeeded and no conflict exists;
- `Conflict`: a stable identity or locator conflicts;
- `Unavailable`: no usable canonical context was obtained.

Codes remain internal English identifiers. UI labels are Spanish.

## Bounds

`CnvRegulationMcpOptions` gains:

```csharp
public int MaxEnrichedHits { get; init; } = 2;
public int MaxDocumentContextCharacters { get; init; } = 12_000;
public int MaxArticleContextCharacters { get; init; } = 6_000;
```

Options validation fails startup when configuration falls outside these safe
bounds:

- enriched hits: `0..2`;
- document context: `0..12_000` characters;
- article context: `0..6_000` characters.

`0` disables that enrichment dimension without changing search behavior.
Truncation preserves the original length and emits a limitation code/message.
The full unbounded text is neither persisted nor sent to an LLM.

Maximum uncached work per review is two document calls plus two article calls.

## Verification and Conflict Rules

Identity comparisons trim values, compare case-insensitively, normalize Unicode
diacritics, collapse whitespace, and ignore punctuation in article labels.

### Document conflict

A document response is conflicting when:

- returned document ID differs from the requested `DocumentId` after
  normalization;
- source differs when both original and canonical source are present; or
- resolution number differs when both are present.

A changed title, URL, date, or status is not a conflict. Original and canonical
values are retained separately.

### Article conflict

An article response is conflicting when:

- returned article locator differs from the requested locator after
  normalization;
- source differs when both values are present; or
- resolution number differs when both values are present.

### Malformed response

These shapes are malformed:

- document `Found=true` with null document, blank document ID, or blank text;
- article `Found=true` with null citation, blank text, or blank article locator;
- null collections declared as required by the typed contract.

Malformed content is not accepted as canonical context.

## Failure, Cancellation, and Partial Results

- Caller cancellation always propagates and stops the review immediately.
- A tool-call timeout is degraded only when the caller token was not canceled.
- Timeout, missing, malformed, and ordinary retrieval failures produce explicit
  per-stage audit status and Spanish limitation text.
- No automatic retry occurs.
- Document success plus article failure produces `Partial` and retains document
  context.
- Document failure plus a consistent article success produces `Partial` and
  retains article context.
- A document identity conflict stops further article retrieval for that
  candidate and produces `Conflict`.
- Conflict content is persisted for audit but excluded from review input.
- Original search findings and citations survive every enrichment failure.

Warnings must say that canonical context could not be verified or was only
partially verified. They must not say that risk is absent, applicability is
established, or a violation occurred.

## Legal Review Integration

`LegalAnalysisReviewInput` gains a trailing optional enrichment collection.
Only `Verified` and `Partial` enrichments contribute canonical context. Review
services receive original evidence and canonical context as separate fields.

Deterministic review:

- keeps the existing relevance and severity policy;
- prefers a verified canonical article citation for review references, then a
  verified canonical document citation, while original evidence stays available
  in the enrichment record;
- appends enrichment limitations without turning them into legal findings.

Semantic review:

- presents original and canonical evidence in separate prompt sections;
- states that document verification does not establish applicability,
  non-compliance, risk, or legal advice;
- allowlists canonical references from non-conflicting enrichments;
- preserves the existing output sanitizer and severity bounds.

Enrichment does not change `HasComplianceRisk=false`,
`RiskLevel=NotEstablished`, relevance, applicability, or issue #7 approval
semantics. Relevant evidence already requires human review; conflicts and
limitations provide additional reasons, not a new automatic risk decision.

## Audit and Observability

`LegalQueryStrategyAudit` gains a trailing optional `Enrichments` collection.
Each `LegalCnvEnrichmentAudit` records:

- enrichment ID, rank, candidate key, score, and contributing query indices;
- whether document/article calls were selected, attempted, or served from the
  per-run cache;
- stage status: `NotApplicable`, `NotAttempted`, `Succeeded`, `Missing`,
  `TimedOut`, `Malformed`, `Failed`, or `Conflict`;
- original text length and a separate truncation flag for each successful
  document/article stage;
- final enrichment status and machine-readable limitation codes.

Audit records do not duplicate canonical text. Logs include IDs, tool names,
rank, status, and counts, but never full regulatory text. One Activity Feed
event summarizes selected, verified, partial, conflict, and unavailable counts;
persisted audit remains the source of truth.

## Propagation and Multi-call Aggregation

The following records gain a trailing optional enrichment collection to retain
source compatibility:

- `RegulatoryReviewResult`;
- `LegalCompliancePluginResult`;
- `LegalAgentResult`.

The Legal plugin and `SemanticKernelLegalAgent` propagate the collection without
reclassification. `ToolExecutionResultMapper` aggregates enrichments from every
successful Legal call, deduplicates by `EnrichmentId`, and merges the richer
outcome deterministically. Conflicting duplicates fail closed to `Conflict`.
Malformed selected Legal payloads retain the existing fail-closed behavior.

## Persistence

`AnalysisOrchestratorService.BuildAnalysisContext` adds:

```text
compliance.evidenceEnrichments[]
```

Each item includes original evidence, bounded canonical document/article
context, status, truncation metadata, and limitations. The existing
`compliance.queryStrategy` contains the enrichment audit. Old sessions without
the field remain valid.

## Operator UI

`ComplianceContext` gains an optional nullable enrichment collection. The
existing Legal panel renders a section titled **Verificación de contexto
regulatorio** when enrichment data exists.

For each item it shows:

- rank and Spanish status label: Verificado, Parcial, Conflicto, or No
  disponible;
- original snippet and citation;
- canonical document/article metadata and bounded text inside an accessible,
  collapsed `<details>` element;
- truncation notice and limitations.

The section states:

> La verificación documental aporta contexto regulatorio, pero no determina
> aplicabilidad, incumplimiento ni asesoramiento legal.

React escapes all text. No raw HTML is rendered. Duplicate limitation strings
use stable index-qualified keys. Missing enrichment data renders no new section.

## Testing Strategy

### MCP server

- document found and document missing;
- article found and article missing;
- no placeholder/mock fallback for unknown identifiers;
- tool metadata remains read-only, idempotent, non-destructive, and structured.

### Typed stdio client

- valid document and article structured responses;
- `Found=false` responses;
- malformed JSON and malformed typed shapes;
- tool errors;
- internal timeout/reset behavior;
- caller cancellation propagation;
- exact tool names and arguments.

### Selector and enricher

- global ordering across multiple search queries;
- non-finite and below-threshold exclusion;
- primary-citation selection;
- candidate deduplication and contributing query indices;
- maximum two candidates and maximum four calls;
- document-cache reuse;
- verified, document-only partial, article-only partial, unavailable, and
  conflict outcomes;
- document ID, source, resolution, and article-locator conflicts;
- truncation and zero-limit configuration;
- timeout, missing, malformed, ordinary failure, and caller cancellation.

### Legal integration

- direct MCP source preserves original evidence and adds canonical context;
- conflicts stay out of legal review;
- partial safe context reaches deterministic and semantic review;
- severity, applicability, compliance risk, and issue #7 behavior remain
  unchanged;
- plan-driven multi-call aggregation is order-independent and deduplicated;
- audit survives persistence.

### Persistence and frontend

- context JSON includes original and canonical fields separately;
- old/null enrichment remains compatible;
- formatter labels preserve unknown future values;
- component renders verified, partial, conflict, unavailable, truncated, and
  empty cases accessibly;
- all frontend tests and the production build pass.

### Verification gates

- focused backend Legal/Planner/context tests;
- full orchestration backend suite;
- full CNV MCP server unit/in-memory suite;
- frontend Node tests and production build;
- legal-language and unbounded-text scans;
- `git diff --check` and clean worktree.

PostgreSQL integration tests remain opt-in and are not enabled without a
separately authorized disposable database. No corpus, embedding, migration, or
ingestion operation is part of this issue.

## Acceptance Mapping

| Acceptance criterion | Design coverage |
| --- | --- |
| Approved top hits enriched through typed document/article retrieval | Deterministic selector, typed client methods, two-hit/four-call bound |
| Deterministic, read-only, bounded, cancellable, audited | Stable ordering, sequential MCP calls, safe options, cancellation rules, persisted audit |
| Canonical citation/context separate from original snippet | `RegulatoryEvidenceEnrichment` original/document/article fields and separate UI sections |
| Failure becomes explicit limitation, not legal conclusion | Missing/malformed/timeout/conflict states, Spanish limitations, unchanged risk/applicability |
| Valid, missing, conflicting, timed-out, malformed tests | Server, client, enricher, integration, persistence, and UI test matrix |
