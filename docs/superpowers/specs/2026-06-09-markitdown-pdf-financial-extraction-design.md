# MarkItDown PDF Financial Extraction Design

## Context

The application already accepts structured financial metrics through JSON, CSV,
and PDF uploads. JSON and CSV are mapped directly into
`StructuredFinancialMetricsInput`. PDF ingestion currently tries native text
extraction with PdfPig, falls back to local Poppler/Tesseract OCR, and applies a
deterministic alias-and-row parser.

That deterministic path works for simple statement layouts but loses data when
PDF text is column-wrapped, tables are visually complex, labels differ from the
known aliases, or report-level metadata such as currency and units is separated
from the metric rows.

The new capability adds a semantic enrichment fallback built around
[Microsoft MarkItDown](https://github.com/microsoft/markitdown). MarkItDown
normalizes PDF content into Markdown that preserves useful document structure.
A dedicated financial-document extraction agent maps that Markdown into the
existing structured metrics contract. Deterministic validation and human review
remain authoritative.

## Goals

- Preserve the current deterministic PDF parser as the fast first strategy.
- Use MarkItDown when deterministic extraction is incomplete or low-confidence.
- Continue supporting image-only PDFs with local OCR.
- Extract metrics, periods, company, currency, and units from richer document
  context.
- Reuse the existing LLM provider, model, and API key configuration used by the
  PlannerAgent.
- Require human review for inferred, conflicting, or otherwise ambiguous data.
- Preserve field-level evidence, confidence, provenance, and human corrections.
- Persist approved data through the existing
  `StructuredFinancialMetricsSessionService`.
- Keep JSON and CSV behavior unchanged.

## Non-Goals

- Replacing JSON or CSV ingestion.
- Replacing the deterministic PDF parser.
- Using the `markitdown-ocr` plugin, hosted OCR, Azure Document Intelligence, or
  Azure Content Understanding.
- Allowing the extraction agent to make operational or accounting decisions.
- Letting the PlannerAgent parse financial documents directly.
- Persisting the source PDF or complete generated Markdown long-term.
- Automatically accepting values that are inferred without explicit evidence.
- Introducing a general-purpose document-management system.

## Selected Approach

Use a hybrid fallback pipeline:

```text
PDF upload
  |
  +--> Existing deterministic extraction
  |      |
  |      +--> Sufficient, valid, explicit result
  |      |      -> persist through existing session service
  |      |
  |      +--> Incomplete, low-confidence, or conflicting result
  |             -> semantic enrichment fallback
  |
  +--> Semantic enrichment fallback
         |
         +--> searchable PDF: MarkItDown directly
         |
         +--> scanned PDF: local OCR -> searchable PDF -> MarkItDown
         |
         +--> FinancialDocumentExtractionAgent
         |
         +--> deterministic reconciliation and validation
                |
                +--> explicit, high-confidence, conflict-free
                |      -> persist through existing session service
                |
                +--> inferred, ambiguous, or conflicting
                       -> persistent review draft
                       -> human correction/approval
                       -> persist through existing session service
```

The PlannerAgent continues to coordinate the overall workflow but does not own
document conversion or field extraction. Those responsibilities belong to
dedicated, testable services.

## Component Boundaries

### Extraction Completeness Evaluator

Add an Application-layer service that evaluates the deterministic extraction
result and decides whether semantic enrichment is required.

It returns a decision plus explicit reason codes. The fallback is triggered when
one or more of these conditions apply:

- no valid metrics were extracted;
- metric coverage is below a configurable threshold;
- required ratio inputs are missing;
- company, currency, unit, or periods are missing;
- the deterministic parser reports low-confidence or structural warnings;
- duplicate candidates disagree;
- extracted values fail deterministic consistency checks.

This decision is deterministic and must not call an LLM.

### Local Searchable PDF OCR

Add an Infrastructure adapter behind an Application contract that turns a
scanned PDF into a temporary searchable PDF.

The implementation reuses local Poppler/Tesseract dependencies: Poppler renders
each page, Tesseract emits one searchable PDF per page, and a pinned Python PDF
library combines those pages into one temporary PDF. Tool-specific details
remain inside Infrastructure.

The generated searchable PDF is transient and deleted after conversion. The
existing text-only OCR path remains available as a safe degradation path if
searchable-PDF generation fails.

### MarkItDown Converter

Add an Application contract such as:

```csharp
public interface IFinancialDocumentMarkdownConverter
{
    Task<FinancialDocumentMarkdownResult> ConvertPdfAsync(
        Stream pdf,
        CancellationToken cancellationToken);
}
```

The Infrastructure implementation calls a small Python module through the
existing CSnakes environment. The initial dependency is pinned to
`markitdown[pdf]==0.1.6`; upgrades require the same conversion and regression
test suite.

Requirements:

- pin an exact tested MarkItDown version;
- call `convert_stream()` with a binary stream and explicit PDF stream metadata;
- disable plugins;
- do not configure an LLM client in MarkItDown;
- do not accept paths, URLs, or remote resources from uploaded content;
- bound conversion by file size, page count, timeout, and output length;
- return normalized Markdown plus conversion diagnostics;
- never persist full Markdown in `AnalysisSession.ContextJson`.

### Financial Document Extraction Agent

Add a dedicated `FinancialDocumentExtractionAgent` behind an Application
interface. Its Infrastructure implementation uses Semantic Kernel and the same
LLM provider/model/secret configuration as PlannerAgent. It must not require a
new API key.

The agent receives:

- bounded Markdown chunks;
- deterministic metric candidates;
- upload metadata supplied by the user;
- the supported metric schema and aliases;
- the required JSON output schema;
- instructions that document content is untrusted data, not executable
  instructions.

The agent has no tool-calling capability. It returns strict structured JSON and
cannot persist data directly.

Large Markdown is split at page or heading boundaries. Each chunk produces
field candidates, which are merged deterministically before final validation.

### Candidate Reconciler

Add a deterministic service that combines parser and agent candidates.

Rules:

- identical values with compatible metadata are merged;
- non-overlapping candidates are combined;
- a conflict is never auto-resolved solely by comparing confidence scores;
- explicit document evidence outranks an unsupported inference;
- user-supplied upload metadata is retained unless the document provides
  contradictory explicit evidence, in which case review is required;
- duplicate metric-period candidates retain the strongest evidence while
  recording the alternatives;
- all inferred fields require review;
- unresolved conflicts require review.

### Existing Session Service

`StructuredFinancialMetricsSessionService` remains the only service that
validates and persists active structured metrics. Neither MarkItDown nor the
extraction agent writes to the analysis session.

## Candidate and Evidence Model

Every extracted field or metric candidate records:

- field or metric name;
- period;
- proposed value;
- currency;
- unit;
- source kind;
- source page when available;
- short evidence excerpt;
- confidence;
- extraction strategy;
- review state;
- optional inference explanation.

Recommended source kinds:

- `reported`: explicitly present in the source document;
- `inferred`: derived from context but not stated explicitly;
- `computed`: calculated deterministically from approved metrics;
- `human_corrected`: changed or supplied during review.

Recommended review states:

- `explicit`;
- `inferred`;
- `conflict`;
- `missing`;
- `accepted`;
- `rejected`;
- `human_corrected`.

Confidence is advisory. It cannot override conflict or inference rules.

Evidence excerpts must be short, bounded, and tied to a source page or Markdown
section when possible. The system must not present an inferred value as quoted
document evidence.

## Automatic Acceptance Rules

The PDF result can be persisted automatically only when all these conditions
hold:

- every persisted value is supported by explicit evidence;
- confidence meets the configured automatic-acceptance threshold;
- no parser/agent conflict remains;
- required metadata is present;
- deterministic structural and financial validation passes;
- no candidate is marked `inferred`;
- no review-blocking warning remains.

Examples:

- `USD in millions` in a report heading is explicit evidence for currency and
  unit.
- A year in a table header is explicit evidence for the corresponding period.
- A `$` symbol with no report-level currency declaration is ambiguous and must
  be reviewed.
- Inferring USD only because the company is based in the United States is not
  automatically acceptable.

## Human Review Workflow

PDF upload returns one of three outcomes:

- `accepted`: active metrics were persisted successfully;
- `review_required`: a review draft was created and active metrics were not
  changed;
- `failed`: no draft or active metrics were changed.

Review drafts are stored separately from `AnalysisSession.ContextJson`, using a
dedicated persistence model such as `FinancialMetricsExtractionDraft` with a
JSONB candidate payload.

The draft stores:

- session and user ownership;
- status;
- original file metadata and content hash;
- deterministic and semantic extraction diagnostics;
- original candidates;
- conflicts and missing fields;
- evidence excerpts;
- reviewed values;
- per-field review decisions;
- reviewer identity and timestamps.

The full PDF and complete Markdown are transient and are not stored by this
feature.

The review UI presents an editable table with:

- metric and period;
- value;
- currency and unit;
- source and source page;
- confidence;
- evidence;
- review status.

Only uncertain fields need prominent attention. The user may accept, edit,
reject, or add candidates. A human edit changes the source to
`human_corrected` and retains the original candidate for auditability.

`Confirm and save` runs deterministic validation again. Only a valid reviewed
input is passed to `StructuredFinancialMetricsSessionService`.

An active `review_required` draft blocks `Start Session`, even when older active
metrics exist. The user must approve or explicitly discard the pending draft.

## API Behavior

Keep the existing file-upload route:

```text
POST /api/analysis-sessions/{id}/financial-metrics/file
```

Extend its response compatibly with:

- `outcome`;
- optional `reviewDraft`;
- existing context, errors, and warnings.

JSON and CSV return `outcome = accepted` and preserve existing behavior.

Add review endpoints:

```text
GET  /api/analysis-sessions/{id}/financial-metrics/review
PUT  /api/analysis-sessions/{id}/financial-metrics/review/{draftId}
POST /api/analysis-sessions/{id}/financial-metrics/review/{draftId}/confirm
POST /api/analysis-sessions/{id}/financial-metrics/review/{draftId}/discard
```

Every endpoint enforces current-user ownership through the session. Confirm and
discard operations are idempotent. A confirmed or discarded draft cannot be
edited again.

## Frontend Behavior

The existing File Upload mode remains the entry point.

For PDF uploads:

- show conversion/extraction progress;
- display `accepted`, `review_required`, or `failed`;
- open the review editor when review is required;
- show evidence and confidence without overwhelming the primary metric table;
- visually distinguish explicit, inferred, conflicting, and corrected values;
- disable confirmation while validation errors remain;
- allow the user to discard the draft;
- refresh session readiness after confirm/discard.

JSON and CSV screens remain unchanged.

## Failure Handling

- Deterministic extraction succeeds sufficiently: skip MarkItDown and the LLM.
- Searchable-PDF generation fails: use existing OCR text result where possible,
  report degraded extraction, and require review.
- MarkItDown is unavailable or times out: retain deterministic candidates and
  return either `accepted` or `review_required` according to deterministic
  validation.
- Agent is disabled, unavailable, times out, or returns invalid JSON: retain
  deterministic candidates and create a review draft when needed.
- Reconciliation fails: return `review_required`; never choose silently.
- OCR produces no useful text: return `failed` with an actionable error.
- Validation fails after human edits: keep the draft editable and return field
  errors.
- Any extraction failure leaves previously active metrics unchanged.

Semantic enrichment failure must not fail the entire application process or
analysis session.

## Security and Resource Controls

Uploaded documents are untrusted.

- Validate PDF signature, extension, MIME type, and existing 20 MB limit.
- Keep the configured maximum page count.
- Use MarkItDown's narrow binary stream API only.
- Disable MarkItDown plugins and remote-resource conversion.
- Never pass a user-controlled path or URL to MarkItDown.
- Run OCR and conversion in bounded temporary directories.
- Delete temporary files in `finally` paths.
- Bound Markdown characters, chunk count, agent tokens, execution time, and
  retries.
- Treat Markdown instructions as document text. The extraction prompt must
  explicitly reject prompt injection and tool-use requests found in content.
- Give the extraction agent no tools, network access, or persistence access.
- Validate the agent response against a strict schema before reconciliation.
- Avoid logging complete document contents, Markdown, or sensitive evidence.

## Configuration

Add an options section for semantic PDF enrichment:

- enabled flag;
- MarkItDown package/version expectation;
- conversion timeout;
- maximum Markdown characters;
- maximum chunks;
- semantic extraction timeout;
- automatic-acceptance confidence threshold;
- deterministic coverage threshold;
- required metadata fields;
- searchable-PDF OCR enabled flag;
- temporary file limits;
- maximum evidence excerpt length.

Reuse existing LLM configuration and secrets. Semantic extraction has its own
enablement flag but does not introduce a second API key.

## Observability

Emit structured events and traces for:

- deterministic extraction completed;
- semantic fallback triggered, including reason codes;
- searchable-PDF OCR used;
- MarkItDown conversion completed/failed;
- extraction agent completed/failed;
- review draft created;
- review confirmed/discarded;
- active metrics persisted.

Record durations, page count, Markdown size, candidate counts, conflict counts,
and outcome. Do not log full Markdown or raw financial document content.

## Testing Strategy

### Unit Tests

- completeness evaluator triggers only for configured conditions;
- MarkItDown adapter uses binary streams and plugins disabled;
- searchable-PDF OCR adapter cleans temporary artifacts;
- agent response schema rejects malformed or unsupported fields;
- reconciler merges matches and preserves conflicts;
- inferred fields always require review;
- explicit high-confidence fields can be auto-accepted;
- human corrections preserve original candidates;
- active metrics are unchanged while a draft is pending.

### Integration Tests

- searchable PDF takes MarkItDown fallback when deterministic coverage is low;
- scanned PDF takes local OCR, then MarkItDown;
- deterministic success skips MarkItDown and the agent;
- MarkItDown timeout degrades safely;
- agent timeout or invalid JSON degrades safely;
- pending draft blocks session start;
- confirm persists through `StructuredFinancialMetricsSessionService`;
- discard leaves previous active metrics unchanged;
- user isolation prevents access to another user's draft.

### Representative Document Cases

- native-text annual report;
- image-only report;
- hybrid PDF with text and scanned pages;
- multi-page statements;
- wrapped or split table columns;
- report-level currency and unit declarations;
- conflicting values in summary and detailed tables;
- missing currency;
- reported ratios plus base metrics;
- prompt-injection text embedded in the document.

### End-to-End Verification

1. Register or sign in.
2. Create a session.
3. Upload a PDF that triggers semantic fallback.
4. Verify the review draft and evidence.
5. Correct an inferred or conflicting field.
6. Confirm the draft.
7. Verify active structured metrics and provenance.
8. Start the session.
9. Verify DataAgent ratios preserve `reported`, `computed`, and
   `human_corrected` provenance as appropriate.

Existing backend, Python, frontend, and browser verification remain part of the
proof set.

## Rollout

Introduce semantic enrichment behind a feature flag.

1. Shadow mode: run MarkItDown and the agent, record diagnostics, but retain the
   deterministic result.
2. Review-only mode: create review drafts but never auto-accept semantic
   candidates.
3. Controlled auto-acceptance: enable only for explicit, high-confidence,
   conflict-free results.

This staged rollout provides real extraction-quality data before semantic
results can alter active financial metrics.

## Decision Summary

- Hybrid deterministic-first architecture selected.
- Microsoft MarkItDown is the Markdown normalizer.
- Existing local OCR remains mandatory for scanned PDFs.
- `markitdown-ocr` and cloud document services are excluded.
- A dedicated extraction agent owns semantic mapping; PlannerAgent only
  coordinates.
- The agent reuses PlannerAgent LLM configuration and API key.
- Inferred or conflicting data always requires human review.
- Pending review data is stored separately and never contaminates active
  structured metrics.
- Existing session validation and persistence remain authoritative.
