# PDF Financial Metrics Ingestion Design

## Context

The application currently accepts structured financial metrics through JSON paste, CSV paste, and `.json` / `.csv` file uploads. Uploaded data is normalized into `StructuredFinancialMetricsInput`, validated by the existing session service, persisted into the analysis session context, and later consumed by `DataAgentFinancialAnalysisWorkflow`.

The new capability adds automatic ingestion for financial analysis PDFs, including scanned PDFs that require local OCR. The final analysis flow should still depend on structured metrics, so PDF ingestion is a preprocessing step that converts PDF content into the same structured metrics contract used by JSON and CSV.

## Goals

- Allow users to upload `.pdf` financial analysis reports from the existing file upload path.
- Extract structured metrics automatically from native-text PDFs.
- Fall back to local OCR for image-only or low-text PDFs.
- Persist extracted metrics in the existing session context when validation passes.
- Preserve provenance: original file name, size, content hash, ingestion method, metric count, warning count, source pages, and confidence.
- Return clear validation errors and warnings when extraction is incomplete, unsupported, or low confidence.

## Non-Goals

- No external OCR, hosted document AI, or cloud LLM dependency.
- No autonomous accounting judgment. The system extracts and validates evidence for agent analysis; it does not certify correctness.
- No replacement of existing JSON/CSV ingestion paths.
- No long-term document storage in this phase. The persisted artifact remains the structured metrics context plus provenance.

## Recommended Architecture

Add an Application-layer contract:

```csharp
public interface IStructuredFinancialMetricsPdfExtractor
{
    Task<StructuredFinancialMetricsPdfExtractionResult> ExtractAsync(
        Stream pdf,
        StructuredFinancialMetricsPdfExtractionRequest request,
        CancellationToken cancellationToken);
}
```

The Infrastructure implementation performs PDF text extraction, OCR fallback, and deterministic metric parsing. The API controller remains the orchestration point for file uploads but delegates PDF-specific work to this extractor. The existing `StructuredFinancialMetricsSessionService` remains the single persistence and validation path.

This keeps PDF processing isolated from the controller, avoids changing the data agent workflow, and preserves the current architecture boundary where agents consume structured financial metrics from session context.

## Upload Flow

1. Frontend accepts `.json`, `.csv`, and `.pdf` in the existing File Upload tab.
2. API validates extension and size through `StructuredFinancialMetricsFileUploadOptions`.
3. `.json` and `.csv` keep their current behavior.
4. `.pdf` uses `IStructuredFinancialMetricsPdfExtractor`.
5. Extractor first attempts native PDF text extraction.
6. If extracted text is missing or below a configured threshold, extractor renders pages locally and runs local OCR.
7. Extractor maps recognized financial line items into `StructuredFinancialMetricsInput`.
8. API applies metadata fallback from the upload form for `documentId`, `company`, `currency`, and `unit` when the extractor cannot infer them.
9. API saves through `StructuredFinancialMetricsSessionService` with `ingestionMethod = "pdf_file"`.
10. Response uses the existing `SaveFinancialMetricsFileResponse` shape with `fileType = "pdf"`.

## Local OCR Strategy

OCR is local and configurable. The extractor should support:

- Native text extraction for searchable PDFs.
- Local page rendering for scanned PDFs.
- Local OCR execution per rendered page.
- Configurable page limit, DPI, minimum text length, OCR timeout, and minimum confidence.

If OCR dependencies are missing, the API returns a clear `BadRequest` response such as `PDF OCR dependencies are not configured.` If OCR runs but extraction confidence is low, the result should be invalid or valid-with-warnings depending on whether enough required metrics were found.

The first implementation can use installed local binaries or libraries behind one Infrastructure service. The contract should not leak tool-specific details into Application or API code.

## Metric Extraction Rules

The parser should be deterministic and conservative:

- Recognize common financial metric names and aliases such as revenue, gross profit, EBITDA, EBIT, net income, free cash flow, total debt, net debt, capex, current assets, current liabilities, cash, inventory, equity, interest expense, and shares.
- Detect periods from nearby headers or labels such as `2024A`, `2025E`, `FY2024`, `Q4 2025`, or plain year columns.
- Detect units and currency from report-level hints and local row text.
- Store `source = "pdf_extraction"` or `source = "pdf_ocr"` depending on the extraction path.
- Store `sourcePage` when page attribution is available.
- Store confidence per metric.
- Deduplicate repeated metric-period pairs by preferring higher confidence and more complete metadata.

The parser should not invent values. Missing required fields should surface through the existing validation result instead of being silently guessed.

## Error Handling

Use existing upload response conventions:

- Unsupported extension: existing unsupported extension error.
- Empty PDF or unreadable PDF: `Invalid PDF file.`
- OCR dependency missing: `PDF OCR dependencies are not configured.`
- No metrics extracted: valid HTTP response with `isValid = false`, no persisted context, and validation errors.
- Partial extraction: persisted only if the existing structured metrics validation passes; warnings describe low confidence, OCR fallback, missing optional metadata, or dropped duplicate rows.

## Frontend Changes

- Change accepted file extensions to `.json,.csv,.pdf`.
- Update validation copy from JSON/CSV to JSON/CSV/PDF.
- Add `pdf_file` to ingestion method labels.
- Keep one File Upload mode; no new workflow screen is needed.
- Adjust file size copy to match backend configuration.
- Keep metadata fields visible because PDF extraction may need document/company/currency/unit fallback.

## Configuration

Extend `StructuredFinancialMetricsFileUpload` with `.pdf` and a PDF-appropriate size limit. Add a separate PDF extraction options section for:

- Native text minimum length.
- Maximum pages.
- OCR DPI.
- OCR timeout.
- Minimum metric confidence.
- Local OCR/rendering command paths when needed.

Defaults should work for small demo reports and fail clearly when OCR binaries are not available.

## Testing

Backend tests:

- `.pdf` upload calls the extractor and persists valid extracted metrics.
- Scanned PDF path is represented by a fake extractor result with OCR warnings and `pdf_ocr` metric sources.
- Invalid PDF extraction result returns `isValid = false` and does not persist context.
- Missing OCR dependency returns a clear upload error.
- Existing JSON and CSV upload tests remain unchanged.

Extractor tests:

- Native text path extracts known metrics from sample text.
- OCR fallback triggers when native text is below threshold.
- Metric aliases, periods, currency, unit, source page, and confidence are mapped correctly.
- Duplicate metric-period rows prefer the higher-confidence candidate.

Frontend tests or build checks:

- File input accepts `.pdf`.
- Client-side extension validation includes `.pdf`.
- `pdf_file` provenance label renders as `PDF file`.

Verification:

- `dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj`
- `dotnet build backend/Orchestration.slnx`
- `npm run build` from `frontend`

## Rollout Notes

The first implementation should be conservative: extract enough common financial statement metrics to feed the current ratios, comparisons, and risk-signal workflow. More sophisticated table reconstruction can be added later behind the same extractor interface without changing controller or agent contracts.
