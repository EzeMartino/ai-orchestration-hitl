# PDF Financial Metrics Ingestion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `.pdf` financial metrics uploads with native PDF text extraction and local OCR fallback, then feed extracted metrics into the existing structured metrics session flow.

**Architecture:** Keep agents unchanged: PDFs become `StructuredFinancialMetricsInput` before persistence. Add Application contracts and deterministic parsing, Infrastructure adapters for PdfPig/native text plus local `pdftoppm` and `tesseract`, then wire the existing upload endpoint to `.pdf`.

**Tech Stack:** ASP.NET Core controllers, .NET 10, EF Core existing session context, `UglyToad.PdfPig` for native PDF text extraction, local Poppler `pdftoppm`, local Tesseract OCR, React/Vite frontend.

---

## File Structure

- Create `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/Input/IStructuredFinancialMetricsPdfExtractor.cs`: application boundary for PDF-to-structured-metrics extraction.
- Create `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/Input/StructuredFinancialMetricsPdfExtractionModels.cs`: request/result/page records used by parser and extractor.
- Create `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/Input/StructuredFinancialMetricsPdfExtractionOptions.cs`: configurable page/OCR thresholds.
- Create `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/Input/StructuredFinancialMetricsTextParser.cs`: deterministic financial metric parser over extracted page text.
- Create `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/Input/IStructuredFinancialMetricsTextParser.cs`: parser boundary for unit tests and extractor composition.
- Create `backend/Orchestration.Infrastructure/Agents/Data/FinancialAnalysis/Pdf/StructuredFinancialMetricsPdfExtractor.cs`: orchestrates native text, OCR fallback, parser, metadata fallback, and warnings.
- Create `backend/Orchestration.Infrastructure/Agents/Data/FinancialAnalysis/Pdf/IPdfTextExtractor.cs`: infrastructure boundary for native PDF text.
- Create `backend/Orchestration.Infrastructure/Agents/Data/FinancialAnalysis/Pdf/IOcrTextExtractor.cs`: infrastructure boundary for local OCR.
- Create `backend/Orchestration.Infrastructure/Agents/Data/FinancialAnalysis/Pdf/PdfPigTextExtractor.cs`: native PDF text adapter.
- Create `backend/Orchestration.Infrastructure/Agents/Data/FinancialAnalysis/Pdf/LocalOcrTextExtractor.cs`: `pdftoppm` + `tesseract` adapter.
- Modify `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/Input/StructuredFinancialMetricsSessionService.cs`: accept `pdf_file` provenance.
- Modify `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/Input/StructuredFinancialMetricsFileUploadOptions.cs`: add `.pdf` default and larger limit.
- Modify `backend/Orchestration.Api/Controllers/AnalysisSessionController.cs`: inject PDF extractor and handle `.pdf`.
- Modify `backend/Orchestration.Api/Program.cs`: configure PDF extraction options and register parser/extractor services.
- Modify `backend/Orchestration.Api/appsettings.json`: add PDF upload/options config.
- Modify `backend/Orchestration.Infrastructure/Orchestration.Infrastructure.csproj`: add `UglyToad.PdfPig`.
- Modify `backend/Orchestration.Tests/Api/AnalysisSessionFinancialMetricsControllerTests.cs`: controller PDF tests and fake extractor.
- Create `backend/Orchestration.Tests/Agents/Data/FinancialAnalysis/StructuredFinancialMetricsTextParserTests.cs`: deterministic parser tests.
- Create `backend/Orchestration.Tests/Agents/Data/FinancialAnalysis/StructuredFinancialMetricsPdfExtractorTests.cs`: extractor orchestration tests with fake text/OCR adapters.
- Modify `frontend/src/components/StructuredFinancialMetricsPanel.tsx`: allow `.pdf`, update copy and validation.
- Modify `frontend/src/components/FinancialRiskEvidencePanel.tsx`: label `pdf_file`.
- Modify `frontend/src/hooks/useAnalysisSession.ts`: start error text includes PDF.
- Modify `frontend/src/types/domain.types.ts`: no shape change required; keep type names.

---

### Task 1: Application Contracts And Provenance

**Files:**
- Create: `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/Input/IStructuredFinancialMetricsPdfExtractor.cs`
- Create: `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/Input/IStructuredFinancialMetricsTextParser.cs`
- Create: `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/Input/StructuredFinancialMetricsPdfExtractionModels.cs`
- Create: `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/Input/StructuredFinancialMetricsPdfExtractionOptions.cs`
- Modify: `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/Input/StructuredFinancialMetricsSessionService.cs`
- Modify: `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/Input/StructuredFinancialMetricsFileUploadOptions.cs`
- Test: `backend/Orchestration.Tests/Agents/Data/FinancialAnalysis/StructuredFinancialMetricsSessionServiceTests.cs`

- [ ] **Step 1: Write failing provenance test**

Append this test to `StructuredFinancialMetricsSessionServiceTests`:

```csharp
[Fact]
public async Task SaveAsync_Should_preserve_pdf_file_provenance()
{
    await using var dbContext = CreateDbContext();
    var session = AnalysisSession.Create();
    dbContext.AnalysisSessions.Add(session);
    await dbContext.SaveChangesAsync();
    var publisher = new FakeActivityEventPublisher();
    var service = CreateService(dbContext, publisher);

    var result = await service.SaveAsync(
        new SaveStructuredFinancialMetricsRequest(
            SessionId: session.Id,
            Input: CreateInput(),
            Provenance: new StructuredFinancialMetricsProvenanceInput(
                IngestionMethod: "pdf_file",
                OriginalFileName: "report.pdf",
                FileSizeBytes: 2048,
                ContentHash: "abc123"
            )
        ),
        CancellationToken.None
    );

    result.Should().NotBeNull();
    result!.IsValid.Should().BeTrue();
    result.Context.Should().NotBeNull();
    result.Context!.Provenance.Should().NotBeNull();
    result.Context.Provenance!.IngestionMethod.Should().Be("pdf_file");
    result.Context.Provenance.OriginalFileName.Should().Be("report.pdf");
    result.Context.Provenance.FileSizeBytes.Should().Be(2048);
    result.Context.Provenance.ContentHash.Should().Be("abc123");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --filter "SaveAsync_Should_preserve_pdf_file_provenance"
```

Expected: FAIL because `StructuredFinancialMetricsSessionService.NormalizeIngestionMethod` returns `unknown` for `pdf_file`.

- [ ] **Step 3: Add PDF contracts**

Create `IStructuredFinancialMetricsPdfExtractor.cs`:

```csharp
namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public interface IStructuredFinancialMetricsPdfExtractor
{
    Task<StructuredFinancialMetricsPdfExtractionResult> ExtractAsync(
        Stream pdf,
        StructuredFinancialMetricsPdfExtractionRequest request,
        CancellationToken cancellationToken);
}
```

Create `IStructuredFinancialMetricsTextParser.cs`:

```csharp
namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public interface IStructuredFinancialMetricsTextParser
{
    StructuredFinancialMetricsPdfExtractionResult Parse(
        StructuredFinancialMetricsTextParseRequest request);
}
```

Create `StructuredFinancialMetricsPdfExtractionModels.cs`:

```csharp
namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed record StructuredFinancialMetricsPdfExtractionRequest(
    string? DocumentId,
    string? Company,
    string? Currency,
    string? Unit,
    string? OriginalFileName
);

public sealed record StructuredFinancialMetricsTextParseRequest(
    string DocumentId,
    string? Company,
    string? Currency,
    string? Unit,
    IReadOnlyList<StructuredFinancialMetricsExtractedPage> Pages,
    string Source
);

public sealed record StructuredFinancialMetricsExtractedPage(
    int PageNumber,
    string Text,
    decimal? OcrConfidence = null
);

public sealed record StructuredFinancialMetricsPdfExtractionResult(
    bool IsValid,
    StructuredFinancialMetricsInput? Input,
    IReadOnlyList<FinancialMetricsValidationIssue> Errors,
    IReadOnlyList<FinancialMetricsValidationIssue> Warnings,
    bool UsedOcr
);
```

Create `StructuredFinancialMetricsPdfExtractionOptions.cs`:

```csharp
namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed class StructuredFinancialMetricsPdfExtractionOptions
{
    public const string SectionName = "StructuredFinancialMetricsPdfExtraction";

    public int NativeTextMinimumCharacters { get; init; } = 200;

    public int MaxPages { get; init; } = 20;

    public int OcrDpi { get; init; } = 200;

    public int OcrTimeoutSeconds { get; init; } = 60;

    public decimal MinimumMetricConfidence { get; init; } = 0.5m;

    public string PdfToPpmPath { get; init; } = "pdftoppm";

    public string TesseractPath { get; init; } = "tesseract";

    public string TesseractLanguage { get; init; } = "eng";
}
```

- [ ] **Step 4: Add `.pdf` default upload support**

Modify `StructuredFinancialMetricsFileUploadOptions.cs`:

```csharp
namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed class StructuredFinancialMetricsFileUploadOptions
{
    public const string SectionName = "StructuredFinancialMetricsFileUpload";

    public long MaxFileSizeBytes { get; init; } = 10_485_760;

    public string[] AllowedExtensions { get; init; } =
    [
        ".json",
        ".csv",
        ".pdf"
    ];
}
```

- [ ] **Step 5: Accept `pdf_file` provenance**

Modify `NormalizeIngestionMethod` in `StructuredFinancialMetricsSessionService.cs`:

```csharp
private static string NormalizeIngestionMethod(
    string? ingestionMethod)
{
    return ingestionMethod is
        "json_paste" or
        "csv_paste" or
        "json_file" or
        "csv_file" or
        "pdf_file"
        ? ingestionMethod
        : "unknown";
}
```

- [ ] **Step 6: Run focused test**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --filter "SaveAsync_Should_preserve_pdf_file_provenance"
```

Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add backend/Orchestration.Application/Agents/Data/FinancialAnalysis/Input backend/Orchestration.Tests/Agents/Data/FinancialAnalysis/StructuredFinancialMetricsSessionServiceTests.cs
git commit -m "Add PDF financial metrics contracts"
```

---

### Task 2: Deterministic Text-To-Metrics Parser

**Files:**
- Create: `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/Input/StructuredFinancialMetricsTextParser.cs`
- Test: `backend/Orchestration.Tests/Agents/Data/FinancialAnalysis/StructuredFinancialMetricsTextParserTests.cs`

- [ ] **Step 1: Write parser tests**

Create `StructuredFinancialMetricsTextParserTests.cs`:

```csharp
using FluentAssertions;
using Orchestration.Application.Agents.Data.FinancialAnalysis;

namespace Orchestration.Tests.Agents.Data.FinancialAnalysis;

public sealed class StructuredFinancialMetricsTextParserTests
{
    [Fact]
    public void Parse_Should_extract_known_metrics_with_periods_and_page_sources()
    {
        var parser = new StructuredFinancialMetricsTextParser();

        var result = parser.Parse(new StructuredFinancialMetricsTextParseRequest(
            DocumentId: "pdf-report",
            Company: "PDF Energy Co",
            Currency: "USD",
            Unit: "USD_thousand",
            Pages:
            [
                new StructuredFinancialMetricsExtractedPage(
                    3,
                    """
                    PDF Energy Co
                    Currency: USD
                    Unit: USD thousand
                    Metric 2024A 2025E
                    Revenue 1,647,768 1,820,000
                    Gross Profit 924,000 1,010,000
                    EBITDA 412,000 455,000
                    Net Debt 980,000 1,050,000
                    Free Cash Flow -55,000 15,000
                    """
                )
            ],
            Source: "pdf_extraction"
        ));

        result.IsValid.Should().BeTrue();
        result.Input.Should().NotBeNull();
        result.Input!.DocumentId.Should().Be("pdf-report");
        result.Input.Company.Should().Be("PDF Energy Co");
        result.Input.Metrics.Should().Contain(metric =>
            metric.Name == "revenue" &&
            metric.Period == "2024A" &&
            metric.Value == 1647768m &&
            metric.Source == "pdf_extraction" &&
            metric.SourcePage == 3 &&
            metric.Confidence == 0.8m
        );
        result.Input.Metrics.Should().Contain(metric =>
            metric.Name == "free_cash_flow" &&
            metric.Period == "2025E" &&
            metric.Value == 15000m
        );
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Parse_Should_return_invalid_result_when_no_metrics_are_found()
    {
        var parser = new StructuredFinancialMetricsTextParser();

        var result = parser.Parse(new StructuredFinancialMetricsTextParseRequest(
            DocumentId: "pdf-report",
            Company: null,
            Currency: null,
            Unit: null,
            Pages:
            [
                new StructuredFinancialMetricsExtractedPage(1, "Management discussion without numeric statements.")
            ],
            Source: "pdf_extraction"
        ));

        result.IsValid.Should().BeFalse();
        result.Input.Should().BeNull();
        result.Errors.Should().ContainSingle(issue =>
            issue.Code == "PDF_METRICS_NOT_FOUND" &&
            issue.Severity == "Error"
        );
    }

    [Fact]
    public void Parse_Should_mark_ocr_source_and_lower_confidence()
    {
        var parser = new StructuredFinancialMetricsTextParser();

        var result = parser.Parse(new StructuredFinancialMetricsTextParseRequest(
            DocumentId: "ocr-report",
            Company: "OCR Co",
            Currency: "USD",
            Unit: "USD_thousand",
            Pages:
            [
                new StructuredFinancialMetricsExtractedPage(
                    2,
                    """
                    Metric 2024A
                    Total Debt 700,000
                    Interest Expense 35,000
                    """,
                    OcrConfidence: 0.62m
                )
            ],
            Source: "pdf_ocr"
        ));

        result.IsValid.Should().BeTrue();
        result.UsedOcr.Should().BeTrue();
        result.Input!.Metrics.Should().Contain(metric =>
            metric.Name == "total_debt" &&
            metric.Source == "pdf_ocr" &&
            metric.Confidence == 0.62m
        );
        result.Warnings.Should().Contain(issue => issue.Code == "PDF_OCR_USED");
    }
}
```

- [ ] **Step 2: Run parser tests to verify they fail**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --filter "StructuredFinancialMetricsTextParserTests"
```

Expected: FAIL because `StructuredFinancialMetricsTextParser` does not exist.

- [ ] **Step 3: Implement parser**

Create `StructuredFinancialMetricsTextParser.cs`:

```csharp
using System.Globalization;
using System.Text.RegularExpressions;

namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed partial class StructuredFinancialMetricsTextParser
    : IStructuredFinancialMetricsTextParser
{
    private static readonly Dictionary<string, string> MetricAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Revenue"] = "revenue",
        ["Sales"] = "revenue",
        ["Gross Profit"] = "gross_profit",
        ["Operating Income"] = "operating_income",
        ["EBITDA"] = "ebitda",
        ["EBIT"] = "ebit",
        ["Net Income"] = "net_income",
        ["Current Assets"] = "current_assets",
        ["Current Liabilities"] = "current_liabilities",
        ["Cash"] = "cash",
        ["Short Term Investments"] = "short_term_investments",
        ["Receivables"] = "receivables",
        ["Total Debt"] = "total_debt",
        ["Net Debt"] = "net_debt",
        ["Equity"] = "equity",
        ["Free Cash Flow"] = "free_cash_flow",
        ["FCF"] = "free_cash_flow",
        ["Capex"] = "capex",
        ["Capital Expenditures"] = "capex",
        ["Interest Expense"] = "interest_expense"
    };

    public StructuredFinancialMetricsPdfExtractionResult Parse(
        StructuredFinancialMetricsTextParseRequest request)
    {
        var errors = new List<FinancialMetricsValidationIssue>();
        var warnings = new List<FinancialMetricsValidationIssue>();
        var metrics = new List<StructuredFinancialMetricInput>();
        var usedOcr = string.Equals(request.Source, "pdf_ocr", StringComparison.OrdinalIgnoreCase);

        if (usedOcr)
        {
            warnings.Add(Warning("PDF_OCR_USED", "Local OCR was used to extract financial metrics from the PDF."));
        }

        foreach (var page in request.Pages)
        {
            var lines = page.Text
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToArray();

            for (var index = 0; index < lines.Length; index++)
            {
                var periods = PeriodRegex()
                    .Matches(lines[index])
                    .Select(match => NormalizePeriod(match.Value))
                    .Where(period => !string.IsNullOrWhiteSpace(period))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

                if (periods.Length == 0)
                {
                    continue;
                }

                var upperBound = Math.Min(lines.Length, index + MetricAliases.Count + 8);
                for (var rowIndex = index + 1; rowIndex < upperBound; rowIndex++)
                {
                    AddMetricsFromLine(lines[rowIndex], periods, page, request, metrics);
                }
            }
        }

        var unique = metrics
            .GroupBy(metric => (metric.Name, metric.Period), StringTupleComparer.Instance)
            .Select(group => group
                .OrderByDescending(metric => metric.Confidence ?? 0m)
                .ThenBy(metric => metric.SourcePage ?? int.MaxValue)
                .First())
            .ToArray();

        if (unique.Length == 0)
        {
            errors.Add(Error("PDF_METRICS_NOT_FOUND", "No supported financial metrics were extracted from the PDF."));
            return new StructuredFinancialMetricsPdfExtractionResult(false, null, errors, warnings, usedOcr);
        }

        var input = new StructuredFinancialMetricsInput(
            DocumentId: request.DocumentId,
            Company: request.Company,
            Currency: request.Currency,
            Unit: request.Unit,
            Metrics: unique
        );

        return new StructuredFinancialMetricsPdfExtractionResult(true, input, errors, warnings, usedOcr);
    }

    private static void AddMetricsFromLine(
        string line,
        IReadOnlyList<string> periods,
        StructuredFinancialMetricsExtractedPage page,
        StructuredFinancialMetricsTextParseRequest request,
        List<StructuredFinancialMetricInput> metrics)
    {
        var alias = MetricAliases.Keys
            .OrderByDescending(key => key.Length)
            .FirstOrDefault(key => line.StartsWith(key, StringComparison.OrdinalIgnoreCase));

        if (alias is null)
        {
            return;
        }

        var values = NumberRegex()
            .Matches(line[alias.Length..])
            .Select(match => ParseNumber(match.Value))
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();

        var count = Math.Min(periods.Count, values.Length);
        for (var index = 0; index < count; index++)
        {
            metrics.Add(new StructuredFinancialMetricInput(
                Name: MetricAliases[alias],
                Period: periods[index],
                Value: values[index],
                Unit: request.Unit,
                Currency: request.Currency,
                Source: request.Source,
                SourcePage: page.PageNumber,
                Confidence: page.OcrConfidence ?? 0.8m
            ));
        }
    }

    private static decimal? ParseNumber(string value)
    {
        var normalized = value.Trim().Replace(",", "", StringComparison.Ordinal);
        var isParenthesizedNegative = normalized.StartsWith('(') && normalized.EndsWith(')');
        normalized = normalized.Trim('(', ')');

        if (!decimal.TryParse(normalized, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed))
        {
            return null;
        }

        return isParenthesizedNegative ? -parsed : parsed;
    }

    private static string NormalizePeriod(string value)
    {
        var trimmed = value.Trim().ToUpperInvariant();
        if (trimmed.StartsWith("FY", StringComparison.Ordinal))
        {
            return trimmed[2..] + "A";
        }

        return trimmed;
    }

    private static FinancialMetricsValidationIssue Error(string code, string message)
    {
        return new FinancialMetricsValidationIssue(code, message, null, null, "Error");
    }

    private static FinancialMetricsValidationIssue Warning(string code, string message)
    {
        return new FinancialMetricsValidationIssue(code, message, null, null, "Warning");
    }

    [GeneratedRegex(@"\b(?:FY)?20\d{2}[AE]?\b", RegexOptions.IgnoreCase)]
    private static partial Regex PeriodRegex();

    [GeneratedRegex(@"\(?-?\d{1,3}(?:,\d{3})*(?:\.\d+)?\)?|\(?-?\d+(?:\.\d+)?\)?")]
    private static partial Regex NumberRegex();

    private sealed class StringTupleComparer : IEqualityComparer<(string Name, string Period)>
    {
        public static readonly StringTupleComparer Instance = new();

        public bool Equals((string Name, string Period) x, (string Name, string Period) y)
        {
            return string.Equals(x.Name, y.Name, StringComparison.Ordinal) &&
                string.Equals(x.Period, y.Period, StringComparison.Ordinal);
        }

        public int GetHashCode((string Name, string Period) obj)
        {
            return HashCode.Combine(obj.Name, obj.Period);
        }
    }
}
```

- [ ] **Step 4: Run parser tests**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --filter "StructuredFinancialMetricsTextParserTests"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add backend/Orchestration.Application/Agents/Data/FinancialAnalysis/Input/StructuredFinancialMetricsTextParser.cs backend/Orchestration.Tests/Agents/Data/FinancialAnalysis/StructuredFinancialMetricsTextParserTests.cs
git commit -m "Add deterministic PDF metrics text parser"
```

---

### Task 3: PDF Extractor Orchestration

**Files:**
- Create: `backend/Orchestration.Infrastructure/Agents/Data/FinancialAnalysis/Pdf/IPdfTextExtractor.cs`
- Create: `backend/Orchestration.Infrastructure/Agents/Data/FinancialAnalysis/Pdf/IOcrTextExtractor.cs`
- Create: `backend/Orchestration.Infrastructure/Agents/Data/FinancialAnalysis/Pdf/StructuredFinancialMetricsPdfExtractor.cs`
- Test: `backend/Orchestration.Tests/Agents/Data/FinancialAnalysis/StructuredFinancialMetricsPdfExtractorTests.cs`

- [ ] **Step 1: Write extractor orchestration tests**

Create `StructuredFinancialMetricsPdfExtractorTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.Pdf;

namespace Orchestration.Tests.Agents.Data.FinancialAnalysis;

public sealed class StructuredFinancialMetricsPdfExtractorTests
{
    [Fact]
    public async Task ExtractAsync_Should_use_native_text_when_text_is_sufficient()
    {
        var native = new FakePdfTextExtractor
        {
            Pages =
            [
                new StructuredFinancialMetricsExtractedPage(
                    1,
                    "Metric 2024A\nRevenue 1,000\nEBITDA 250"
                )
            ]
        };
        var ocr = new FakeOcrTextExtractor();
        var extractor = CreateExtractor(native, ocr, minimumCharacters: 20);

        var result = await extractor.ExtractAsync(
            PdfBytes(),
            Request(),
            CancellationToken.None
        );

        result.IsValid.Should().BeTrue();
        result.UsedOcr.Should().BeFalse();
        ocr.CallCount.Should().Be(0);
        result.Input!.Metrics.Should().Contain(metric => metric.Name == "revenue");
    }

    [Fact]
    public async Task ExtractAsync_Should_fallback_to_ocr_when_native_text_is_too_short()
    {
        var native = new FakePdfTextExtractor
        {
            Pages = [new StructuredFinancialMetricsExtractedPage(1, "")]
        };
        var ocr = new FakeOcrTextExtractor
        {
            Pages =
            [
                new StructuredFinancialMetricsExtractedPage(
                    1,
                    "Metric 2024A\nTotal Debt 700,000\nInterest Expense 35,000",
                    OcrConfidence: 0.64m
                )
            ]
        };
        var extractor = CreateExtractor(native, ocr, minimumCharacters: 50);

        var result = await extractor.ExtractAsync(PdfBytes(), Request(), CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.UsedOcr.Should().BeTrue();
        ocr.CallCount.Should().Be(1);
        result.Warnings.Should().Contain(issue => issue.Code == "PDF_OCR_USED");
        result.Input!.Metrics.Should().Contain(metric =>
            metric.Source == "pdf_ocr" &&
            metric.Confidence == 0.64m
        );
    }

    [Fact]
    public async Task ExtractAsync_Should_return_dependency_error_when_ocr_is_not_configured()
    {
        var native = new FakePdfTextExtractor
        {
            Pages = [new StructuredFinancialMetricsExtractedPage(1, "")]
        };
        var ocr = new FakeOcrTextExtractor
        {
            Exception = new PdfOcrDependencyException("PDF OCR dependencies are not configured.")
        };
        var extractor = CreateExtractor(native, ocr, minimumCharacters: 50);

        var result = await extractor.ExtractAsync(PdfBytes(), Request(), CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Input.Should().BeNull();
        result.Errors.Should().ContainSingle(issue =>
            issue.Code == "PDF_OCR_NOT_CONFIGURED" &&
            issue.Message == "PDF OCR dependencies are not configured."
        );
    }

    private static StructuredFinancialMetricsPdfExtractor CreateExtractor(
        IPdfTextExtractor native,
        IOcrTextExtractor ocr,
        int minimumCharacters)
    {
        return new StructuredFinancialMetricsPdfExtractor(
            native,
            ocr,
            new StructuredFinancialMetricsTextParser(),
            Options.Create(new StructuredFinancialMetricsPdfExtractionOptions
            {
                NativeTextMinimumCharacters = minimumCharacters
            }),
            NullLogger<StructuredFinancialMetricsPdfExtractor>.Instance
        );
    }

    private static MemoryStream PdfBytes()
    {
        return new MemoryStream([0x25, 0x50, 0x44, 0x46]);
    }

    private static StructuredFinancialMetricsPdfExtractionRequest Request()
    {
        return new StructuredFinancialMetricsPdfExtractionRequest(
            DocumentId: "pdf-report",
            Company: "PDF Co",
            Currency: "USD",
            Unit: "USD_thousand",
            OriginalFileName: "report.pdf"
        );
    }

    private sealed class FakePdfTextExtractor : IPdfTextExtractor
    {
        public IReadOnlyList<StructuredFinancialMetricsExtractedPage> Pages { get; init; } = [];

        public Task<IReadOnlyList<StructuredFinancialMetricsExtractedPage>> ExtractTextAsync(
            Stream pdf,
            int maxPages,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Pages);
        }
    }

    private sealed class FakeOcrTextExtractor : IOcrTextExtractor
    {
        public int CallCount { get; private set; }
        public IReadOnlyList<StructuredFinancialMetricsExtractedPage> Pages { get; init; } = [];
        public Exception? Exception { get; init; }

        public Task<IReadOnlyList<StructuredFinancialMetricsExtractedPage>> ExtractTextAsync(
            Stream pdf,
            StructuredFinancialMetricsPdfExtractionOptions options,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult(Pages);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --filter "StructuredFinancialMetricsPdfExtractorTests"
```

Expected: FAIL because extractor and infrastructure interfaces do not exist.

- [ ] **Step 3: Add infrastructure interfaces and exception**

Create `IPdfTextExtractor.cs`:

```csharp
using Orchestration.Application.Agents.Data.FinancialAnalysis;

namespace Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.Pdf;

public interface IPdfTextExtractor
{
    Task<IReadOnlyList<StructuredFinancialMetricsExtractedPage>> ExtractTextAsync(
        Stream pdf,
        int maxPages,
        CancellationToken cancellationToken);
}
```

Create `IOcrTextExtractor.cs`:

```csharp
using Orchestration.Application.Agents.Data.FinancialAnalysis;

namespace Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.Pdf;

public interface IOcrTextExtractor
{
    Task<IReadOnlyList<StructuredFinancialMetricsExtractedPage>> ExtractTextAsync(
        Stream pdf,
        StructuredFinancialMetricsPdfExtractionOptions options,
        CancellationToken cancellationToken);
}

public sealed class PdfOcrDependencyException : Exception
{
    public PdfOcrDependencyException(string message)
        : base(message)
    {
    }
}
```

- [ ] **Step 4: Implement orchestration extractor**

Create `StructuredFinancialMetricsPdfExtractor.cs`:

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestration.Application.Agents.Data.FinancialAnalysis;

namespace Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.Pdf;

public sealed class StructuredFinancialMetricsPdfExtractor
    : IStructuredFinancialMetricsPdfExtractor
{
    private readonly IPdfTextExtractor _pdfTextExtractor;
    private readonly IOcrTextExtractor _ocrTextExtractor;
    private readonly IStructuredFinancialMetricsTextParser _textParser;
    private readonly StructuredFinancialMetricsPdfExtractionOptions _options;
    private readonly ILogger<StructuredFinancialMetricsPdfExtractor> _logger;

    public StructuredFinancialMetricsPdfExtractor(
        IPdfTextExtractor pdfTextExtractor,
        IOcrTextExtractor ocrTextExtractor,
        IStructuredFinancialMetricsTextParser textParser,
        IOptions<StructuredFinancialMetricsPdfExtractionOptions> options,
        ILogger<StructuredFinancialMetricsPdfExtractor> logger)
    {
        _pdfTextExtractor = pdfTextExtractor;
        _ocrTextExtractor = ocrTextExtractor;
        _textParser = textParser;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<StructuredFinancialMetricsPdfExtractionResult> ExtractAsync(
        Stream pdf,
        StructuredFinancialMetricsPdfExtractionRequest request,
        CancellationToken cancellationToken)
    {
        var seekable = await CopyToSeekableStreamAsync(pdf, cancellationToken);
        var nativePages = await _pdfTextExtractor.ExtractTextAsync(
            seekable,
            _options.MaxPages,
            cancellationToken
        );

        var nativeTextLength = nativePages.Sum(page => page.Text.Length);
        if (nativeTextLength >= _options.NativeTextMinimumCharacters)
        {
            return _textParser.Parse(BuildParseRequest(request, nativePages, "pdf_extraction"));
        }

        seekable.Position = 0;
        IReadOnlyList<StructuredFinancialMetricsExtractedPage> ocrPages;

        try
        {
            ocrPages = await _ocrTextExtractor.ExtractTextAsync(
                seekable,
                _options,
                cancellationToken
            );
        }
        catch (PdfOcrDependencyException ex)
        {
            _logger.LogWarning(ex, "PDF OCR dependencies are not configured.");
            return Failed(
                "PDF_OCR_NOT_CONFIGURED",
                "PDF OCR dependencies are not configured."
            );
        }

        return _textParser.Parse(BuildParseRequest(request, ocrPages, "pdf_ocr"));
    }

    private static StructuredFinancialMetricsTextParseRequest BuildParseRequest(
        StructuredFinancialMetricsPdfExtractionRequest request,
        IReadOnlyList<StructuredFinancialMetricsExtractedPage> pages,
        string source)
    {
        return new StructuredFinancialMetricsTextParseRequest(
            DocumentId: string.IsNullOrWhiteSpace(request.DocumentId)
                ? Path.GetFileNameWithoutExtension(request.OriginalFileName) ?? "pdf-report"
                : request.DocumentId.Trim(),
            Company: request.Company,
            Currency: request.Currency,
            Unit: request.Unit,
            Pages: pages,
            Source: source
        );
    }

    private static async Task<MemoryStream> CopyToSeekableStreamAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var copy = new MemoryStream();
        await stream.CopyToAsync(copy, cancellationToken);
        copy.Position = 0;
        return copy;
    }

    private static StructuredFinancialMetricsPdfExtractionResult Failed(
        string code,
        string message)
    {
        return new StructuredFinancialMetricsPdfExtractionResult(
            IsValid: false,
            Input: null,
            Errors:
            [
                new FinancialMetricsValidationIssue(
                    Code: code,
                    Message: message,
                    MetricName: null,
                    Period: null,
                    Severity: "Error"
                )
            ],
            Warnings: [],
            UsedOcr: true
        );
    }
}
```

- [ ] **Step 5: Run extractor tests**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --filter "StructuredFinancialMetricsPdfExtractorTests"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add backend/Orchestration.Infrastructure/Agents/Data/FinancialAnalysis/Pdf backend/Orchestration.Tests/Agents/Data/FinancialAnalysis/StructuredFinancialMetricsPdfExtractorTests.cs
git commit -m "Add PDF extraction orchestration"
```

---

### Task 4: Native PDF And Local OCR Adapters

**Files:**
- Modify: `backend/Orchestration.Infrastructure/Orchestration.Infrastructure.csproj`
- Create: `backend/Orchestration.Infrastructure/Agents/Data/FinancialAnalysis/Pdf/PdfPigTextExtractor.cs`
- Create: `backend/Orchestration.Infrastructure/Agents/Data/FinancialAnalysis/Pdf/LocalOcrTextExtractor.cs`

- [ ] **Step 1: Add PdfPig package**

Run:

```powershell
dotnet add backend/Orchestration.Infrastructure/Orchestration.Infrastructure.csproj package UglyToad.PdfPig --version 1.7.0-custom-5
```

Expected: package reference added to `Orchestration.Infrastructure.csproj`.

- [ ] **Step 2: Add native PDF text adapter**

Create `PdfPigTextExtractor.cs`:

```csharp
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using UglyToad.PdfPig;

namespace Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.Pdf;

public sealed class PdfPigTextExtractor : IPdfTextExtractor
{
    public Task<IReadOnlyList<StructuredFinancialMetricsExtractedPage>> ExtractTextAsync(
        Stream pdf,
        int maxPages,
        CancellationToken cancellationToken)
    {
        using var document = PdfDocument.Open(pdf);
        var pages = document.GetPages()
            .Take(maxPages)
            .Select(page => new StructuredFinancialMetricsExtractedPage(
                PageNumber: page.Number,
                Text: page.Text
            ))
            .ToArray();

        return Task.FromResult<IReadOnlyList<StructuredFinancialMetricsExtractedPage>>(pages);
    }
}
```

- [ ] **Step 3: Add local OCR adapter**

Create `LocalOcrTextExtractor.cs`:

```csharp
using System.Diagnostics;
using Orchestration.Application.Agents.Data.FinancialAnalysis;

namespace Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.Pdf;

public sealed class LocalOcrTextExtractor : IOcrTextExtractor
{
    public async Task<IReadOnlyList<StructuredFinancialMetricsExtractedPage>> ExtractTextAsync(
        Stream pdf,
        StructuredFinancialMetricsPdfExtractionOptions options,
        CancellationToken cancellationToken)
    {
        var workDirectory = Path.Combine(Path.GetTempPath(), "ai-orchestration-hitl-pdf-ocr", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDirectory);

        try
        {
            var inputPdf = Path.Combine(workDirectory, "input.pdf");
            await using (var file = File.Create(inputPdf))
            {
                pdf.Position = 0;
                await pdf.CopyToAsync(file, cancellationToken);
            }

            var imagePrefix = Path.Combine(workDirectory, "page");
            await RunProcessAsync(
                options.PdfToPpmPath,
                $"-r {options.OcrDpi} -png -f 1 -l {options.MaxPages} \"{inputPdf}\" \"{imagePrefix}\"",
                options.OcrTimeoutSeconds,
                cancellationToken
            );

            var images = Directory.GetFiles(workDirectory, "page-*.png")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (images.Length == 0)
            {
                throw new PdfOcrDependencyException("PDF OCR dependencies are not configured.");
            }

            var pages = new List<StructuredFinancialMetricsExtractedPage>();
            for (var index = 0; index < images.Length; index++)
            {
                var outputBase = Path.Combine(workDirectory, $"ocr-{index + 1}");
                await RunProcessAsync(
                    options.TesseractPath,
                    $"\"{images[index]}\" \"{outputBase}\" -l {options.TesseractLanguage}",
                    options.OcrTimeoutSeconds,
                    cancellationToken
                );

                var outputText = outputBase + ".txt";
                var text = File.Exists(outputText)
                    ? await File.ReadAllTextAsync(outputText, cancellationToken)
                    : "";

                pages.Add(new StructuredFinancialMetricsExtractedPage(
                    PageNumber: index + 1,
                    Text: text,
                    OcrConfidence: 0.6m
                ));
            }

            return pages;
        }
        finally
        {
            try
            {
                Directory.Delete(workDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static async Task RunProcessAsync(
        string fileName,
        string arguments,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is FileNotFoundException or System.ComponentModel.Win32Exception)
        {
            throw new PdfOcrDependencyException("PDF OCR dependencies are not configured.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            throw new PdfOcrDependencyException("PDF OCR processing timed out.");
        }

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            throw new PdfOcrDependencyException(
                string.IsNullOrWhiteSpace(error)
                    ? "PDF OCR dependencies are not configured."
                    : "PDF OCR processing failed."
            );
        }
    }
}
```

- [ ] **Step 4: Build infrastructure project**

Run:

```powershell
dotnet build backend/Orchestration.Infrastructure/Orchestration.Infrastructure.csproj
```

Expected: build succeeds.

- [ ] **Step 5: Commit**

```powershell
git add backend/Orchestration.Infrastructure/Orchestration.Infrastructure.csproj backend/Orchestration.Infrastructure/Agents/Data/FinancialAnalysis/Pdf/PdfPigTextExtractor.cs backend/Orchestration.Infrastructure/Agents/Data/FinancialAnalysis/Pdf/LocalOcrTextExtractor.cs
git commit -m "Add local PDF text and OCR adapters"
```

---

### Task 5: API Upload Integration

**Files:**
- Modify: `backend/Orchestration.Api/Controllers/AnalysisSessionController.cs`
- Modify: `backend/Orchestration.Api/Program.cs`
- Modify: `backend/Orchestration.Api/appsettings.json`
- Test: `backend/Orchestration.Tests/Api/AnalysisSessionFinancialMetricsControllerTests.cs`

- [ ] **Step 1: Write controller PDF tests**

Modify `AnalysisSessionFinancialMetricsControllerTests.cs`:

1. Add constructor dependency support in `CreateController`:

```csharp
private static AnalysisSessionsController CreateController(
    OrchestrationDbContext dbContext,
    StructuredFinancialMetricsFileUploadOptions? fileUploadOptions = null,
    FakeActivityEventPublisher? publisher = null,
    DataAgentOptions? dataAgentOptions = null,
    AnalysisOrchestratorService? orchestrator = null,
    IStructuredFinancialMetricsPdfExtractor? pdfExtractor = null)
{
    publisher ??= new FakeActivityEventPublisher();

    return new AnalysisSessionsController(
        dbContext,
        orchestrator: orchestrator!,
        new AnalysisSessionStartPreflightValidator(
            Options.Create(dataAgentOptions ?? new DataAgentOptions())
        ),
        publisher,
        StructuredFinancialMetricsSessionServiceTests.CreateService(dbContext, publisher),
        new StructuredFinancialMetricsCsvParser(),
        pdfExtractor ?? new FakeStructuredFinancialMetricsPdfExtractor(),
        Options.Create(fileUploadOptions ?? new StructuredFinancialMetricsFileUploadOptions())
    );
}
```

2. Add tests:

```csharp
[Fact]
public async Task SaveFinancialMetricsFile_Should_persist_valid_pdf_file()
{
    await using var dbContext = CreateDbContext();
    var session = AnalysisSession.Create();
    dbContext.AnalysisSessions.Add(session);
    await dbContext.SaveChangesAsync();
    var extractor = new FakeStructuredFinancialMetricsPdfExtractor
    {
        Result = PdfResult(
            new StructuredFinancialMetricsInput(
                DocumentId: "pdf-file-input",
                Company: "PDF File Co",
                Currency: "USD",
                Unit: "USD_thousand",
                Metrics:
                [
                    new StructuredFinancialMetricInput(
                        Name: "Revenue",
                        Period: "2024A",
                        Value: 1647768m,
                        Unit: "USD_thousand",
                        Currency: "USD",
                        Source: "pdf_extraction",
                        SourcePage: 18,
                        Confidence: 0.8m
                    )
                ]
            )
        )
    };
    var controller = CreateController(dbContext, pdfExtractor: extractor);

    var result = await controller.SaveFinancialMetricsFile(
        session.Id,
        CreateFileUploadRequest(
            "report.pdf",
            "%PDF-1.7",
            documentId: "form-document",
            company: "Form Co",
            currency: "USD",
            unit: "USD_thousand"
        ),
        CancellationToken.None
    );

    var response = result.Should().BeOfType<OkObjectResult>()
        .Which.Value.Should().BeOfType<SaveFinancialMetricsFileResponse>()
        .Subject;
    response.IsValid.Should().BeTrue();
    response.FileName.Should().Be("report.pdf");
    response.FileType.Should().Be("pdf");
    response.Context.Should().NotBeNull();
    response.Context!.DocumentId.Should().Be("pdf-file-input");
    response.Context.Provenance.Should().NotBeNull();
    response.Context.Provenance!.IngestionMethod.Should().Be("pdf_file");
    response.Context.Provenance.OriginalFileName.Should().Be("report.pdf");
    response.Context.Metrics.Should().ContainSingle(metric =>
        metric.Name == "revenue" &&
        metric.Source == "pdf_extraction" &&
        metric.SourcePage == 18
    );
    extractor.LastRequest.Should().NotBeNull();
    extractor.LastRequest!.DocumentId.Should().Be("form-document");
}

[Fact]
public async Task SaveFinancialMetricsFile_Should_return_invalid_result_for_pdf_without_metrics()
{
    await using var dbContext = CreateDbContext();
    var session = AnalysisSession.Create();
    dbContext.AnalysisSessions.Add(session);
    await dbContext.SaveChangesAsync();
    var extractor = new FakeStructuredFinancialMetricsPdfExtractor
    {
        Result = new StructuredFinancialMetricsPdfExtractionResult(
            IsValid: false,
            Input: null,
            Errors:
            [
                new FinancialMetricsValidationIssue(
                    Code: "PDF_METRICS_NOT_FOUND",
                    Message: "No supported financial metrics were extracted from the PDF.",
                    MetricName: null,
                    Period: null,
                    Severity: "Error"
                )
            ],
            Warnings: [],
            UsedOcr: false
        )
    };
    var controller = CreateController(dbContext, pdfExtractor: extractor);

    var result = await controller.SaveFinancialMetricsFile(
        session.Id,
        CreateFileUploadRequest("empty.pdf", "%PDF-1.7"),
        CancellationToken.None
    );

    var response = result.Should().BeOfType<OkObjectResult>()
        .Which.Value.Should().BeOfType<SaveFinancialMetricsFileResponse>()
        .Subject;
    response.IsValid.Should().BeFalse();
    response.Context.Should().BeNull();
    response.Errors.Should().ContainSingle(issue => issue.Code == "PDF_METRICS_NOT_FOUND");
    session.ContextJson.Should().Be("{}");
}

[Fact]
public async Task SaveFinancialMetricsFile_Should_return_bad_request_when_pdf_ocr_is_not_configured()
{
    await using var dbContext = CreateDbContext();
    var session = AnalysisSession.Create();
    dbContext.AnalysisSessions.Add(session);
    await dbContext.SaveChangesAsync();
    var extractor = new FakeStructuredFinancialMetricsPdfExtractor
    {
        Result = new StructuredFinancialMetricsPdfExtractionResult(
            IsValid: false,
            Input: null,
            Errors:
            [
                new FinancialMetricsValidationIssue(
                    Code: "PDF_OCR_NOT_CONFIGURED",
                    Message: "PDF OCR dependencies are not configured.",
                    MetricName: null,
                    Period: null,
                    Severity: "Error"
                )
            ],
            Warnings: [],
            UsedOcr: true
        )
    };
    var controller = CreateController(dbContext, pdfExtractor: extractor);

    var result = await controller.SaveFinancialMetricsFile(
        session.Id,
        CreateFileUploadRequest("scan.pdf", "%PDF-1.7"),
        CancellationToken.None
    );

    var response = result.Should().BeOfType<BadRequestObjectResult>()
        .Which.Value.Should().BeOfType<FileUploadErrorResponse>()
        .Subject;
    response.Error.Should().Be("PDF OCR dependencies are not configured.");
}
```

3. Add helper/fake near other private helpers:

```csharp
private static StructuredFinancialMetricsPdfExtractionResult PdfResult(
    StructuredFinancialMetricsInput input)
{
    return new StructuredFinancialMetricsPdfExtractionResult(
        IsValid: true,
        Input: input,
        Errors: [],
        Warnings: [],
        UsedOcr: false
    );
}

private sealed class FakeStructuredFinancialMetricsPdfExtractor
    : IStructuredFinancialMetricsPdfExtractor
{
    public StructuredFinancialMetricsPdfExtractionRequest? LastRequest { get; private set; }

    public StructuredFinancialMetricsPdfExtractionResult Result { get; init; } = PdfResult(
        new StructuredFinancialMetricsInput(
            DocumentId: "default-pdf",
            Company: "Default PDF Co",
            Currency: "USD",
            Unit: "USD_thousand",
            Metrics:
            [
                new StructuredFinancialMetricInput(
                    Name: "Revenue",
                    Period: "2024A",
                    Value: 100m,
                    Unit: "USD_thousand",
                    Currency: "USD",
                    Source: "pdf_extraction",
                    SourcePage: 1,
                    Confidence: 0.8m
                )
            ]
        )
    );

    public Task<StructuredFinancialMetricsPdfExtractionResult> ExtractAsync(
        Stream pdf,
        StructuredFinancialMetricsPdfExtractionRequest request,
        CancellationToken cancellationToken)
    {
        LastRequest = request;
        return Task.FromResult(Result);
    }
}
```

- [ ] **Step 2: Run controller tests to verify they fail**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --filter "SaveFinancialMetricsFile_Should_persist_valid_pdf_file|SaveFinancialMetricsFile_Should_return_invalid_result_for_pdf_without_metrics|SaveFinancialMetricsFile_Should_return_bad_request_when_pdf_ocr_is_not_configured"
```

Expected: FAIL because controller constructor and `.pdf` switch are not wired.

- [ ] **Step 3: Modify controller constructor and `.pdf` branch**

In `AnalysisSessionController.cs`, add constructor parameter:

```csharp
IStructuredFinancialMetricsCsvParser financialMetricsCsvParser,
IStructuredFinancialMetricsPdfExtractor financialMetricsPdfExtractor,
IOptions<StructuredFinancialMetricsFileUploadOptions> fileUploadOptions) : ControllerBase
```

Add field:

```csharp
private readonly IStructuredFinancialMetricsPdfExtractor _financialMetricsPdfExtractor = financialMetricsPdfExtractor;
```

Replace the body after `var extension = Path.GetExtension(file.FileName).ToLowerInvariant();` in `SaveFinancialMetricsFile` so binary PDFs do not pass through the UTF-8 text reader:

```csharp
if (extension == ".pdf")
{
    return await SavePdfFileAsync(id, request, cancellationToken);
}

var content = await ReadFileContentAsync(file, cancellationToken);

if (string.IsNullOrWhiteSpace(content))
{
    return BadRequest(new FileUploadErrorResponse("Uploaded file is empty."));
}

return extension switch
{
    ".json" => await SaveJsonFileAsync(id, request, content, cancellationToken),
    ".csv" => await SaveCsvFileAsync(id, request, content, cancellationToken),
    _ => BadRequest(new FileUploadErrorResponse("Unsupported file extension."))
};
```

The resulting method keeps validation first, computes the extension once, routes `.pdf` before reading text, and keeps existing `.json` / `.csv` behavior.

Add method:

```csharp
private async Task<IActionResult> SavePdfFileAsync(
    Guid id,
    StructuredFinancialMetricsFileUploadRequest request,
    CancellationToken cancellationToken)
{
    var sessionExists = await _dbContext.AnalysisSessions
        .AnyAsync(x => x.Id == id, cancellationToken);

    if (!sessionExists)
    {
        return NotFound();
    }

    await using var stream = request.File!.OpenReadStream();
    var extraction = await _financialMetricsPdfExtractor.ExtractAsync(
        stream,
        new StructuredFinancialMetricsPdfExtractionRequest(
            DocumentId: request.DocumentId,
            Company: request.Company,
            Currency: request.Currency,
            Unit: request.Unit,
            OriginalFileName: request.File.FileName
        ),
        cancellationToken
    );

    if (!extraction.IsValid || extraction.Input is null)
    {
        if (extraction.Errors.Any(error => error.Code == "PDF_OCR_NOT_CONFIGURED"))
        {
            return BadRequest(new FileUploadErrorResponse("PDF OCR dependencies are not configured."));
        }

        var invalidResult = new FinancialMetricsSessionSaveResult(
            SessionId: id,
            IsValid: false,
            Context: null,
            Errors: extraction.Errors,
            Warnings: extraction.Warnings
        );

        return Ok(CreateFileResponse(request.File, "pdf", invalidResult));
    }

    var result = await _financialMetricsSessionService.SaveAsync(
        new SaveStructuredFinancialMetricsRequest(
            SessionId: id,
            Input: ApplyFallbackMetadata(extraction.Input, request),
            Provenance: await CreateBinaryFileProvenanceAsync(
                request.File,
                "pdf_file",
                cancellationToken
            )
        ),
        cancellationToken
    );

    if (result is null)
    {
        return NotFound();
    }

    return Ok(CreateFileResponse(request.File, "pdf", result));
}
```

Add helper:

```csharp
private static async Task<StructuredFinancialMetricsProvenanceInput> CreateBinaryFileProvenanceAsync(
    IFormFile file,
    string ingestionMethod,
    CancellationToken cancellationToken)
{
    await using var stream = file.OpenReadStream();
    using var memory = new MemoryStream();
    await stream.CopyToAsync(memory, cancellationToken);

    return new StructuredFinancialMetricsProvenanceInput(
        IngestionMethod: ingestionMethod,
        OriginalFileName: Path.GetFileName(file.FileName),
        FileSizeBytes: file.Length,
        ContentHash: Convert.ToHexString(SHA256.HashData(memory.ToArray())).ToLowerInvariant()
    );
}
```

- [ ] **Step 4: Register services and config**

In `Program.cs`, add:

```csharp
builder.Services.Configure<StructuredFinancialMetricsPdfExtractionOptions>(
    builder.Configuration.GetSection(StructuredFinancialMetricsPdfExtractionOptions.SectionName)
);
builder.Services.AddScoped<IStructuredFinancialMetricsTextParser, StructuredFinancialMetricsTextParser>();
builder.Services.AddScoped<IPdfTextExtractor, PdfPigTextExtractor>();
builder.Services.AddScoped<IOcrTextExtractor, LocalOcrTextExtractor>();
builder.Services.AddScoped<IStructuredFinancialMetricsPdfExtractor, StructuredFinancialMetricsPdfExtractor>();
```

Add using if needed:

```csharp
using Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.Pdf;
```

In `appsettings.json`, update:

```json
"StructuredFinancialMetricsFileUpload": {
  "MaxFileSizeBytes": 10485760,
  "AllowedExtensions": [ ".json", ".csv", ".pdf" ]
},
"StructuredFinancialMetricsPdfExtraction": {
  "NativeTextMinimumCharacters": 200,
  "MaxPages": 20,
  "OcrDpi": 200,
  "OcrTimeoutSeconds": 60,
  "MinimumMetricConfidence": 0.5,
  "PdfToPpmPath": "pdftoppm",
  "TesseractPath": "tesseract",
  "TesseractLanguage": "eng"
},
```

- [ ] **Step 5: Run controller tests**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --filter "AnalysisSessionFinancialMetricsControllerTests"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add backend/Orchestration.Api/Controllers/AnalysisSessionController.cs backend/Orchestration.Api/Program.cs backend/Orchestration.Api/appsettings.json backend/Orchestration.Tests/Api/AnalysisSessionFinancialMetricsControllerTests.cs
git commit -m "Wire PDF financial metrics uploads"
```

---

### Task 6: Frontend PDF Upload Support

**Files:**
- Modify: `frontend/src/components/StructuredFinancialMetricsPanel.tsx`
- Modify: `frontend/src/components/FinancialRiskEvidencePanel.tsx`
- Modify: `frontend/src/components/StartReadinessPanel.tsx`
- Modify: `frontend/src/hooks/useAnalysisSession.ts`

- [ ] **Step 1: Update file upload constants and labels**

In `StructuredFinancialMetricsPanel.tsx`, change:

```tsx
const maxStructuredMetricsFileSizeBytes = 10_485_760;
const allowedStructuredMetricsFileExtensions = [".json", ".csv", ".pdf"];
```

Add `pdf_file` label:

```tsx
case "pdf_file":
  return "PDF file";
```

Update upload validation strings:

```tsx
setInputError("Select a JSON, CSV, or PDF metrics file.");
setInputError("Only .json, .csv, and .pdf files are supported.");
setInputError("Only files up to 10 MB are supported.");
```

Change file input label/copy:

```tsx
JSON, CSV, or PDF file
```

```tsx
accept=".json,.csv,.pdf"
```

```tsx
<p>Only .json, .csv, and .pdf files up to 10 MB are supported.</p>
```

- [ ] **Step 2: Update evidence and readiness copy**

In `FinancialRiskEvidencePanel.tsx`, add:

```tsx
case "pdf_file":
  return "PDF file";
```

Replace copy:

```tsx
"Structured financial metrics are required for this mode but were not attached to this session. Attach JSON/CSV/PDF metrics before starting the analysis."
```

In `StartReadinessPanel.tsx`, replace:

```tsx
<p>Attach JSON/CSV/PDF structured metrics before starting this analysis.</p>
```

In `useAnalysisSession.ts`, update conflict message:

```tsx
? "Structured financial metrics are required before starting this analysis. Attach JSON/CSV/PDF metrics and try again."
```

- [ ] **Step 3: Run frontend build**

Run:

```powershell
npm run build
```

Working dir: `frontend`

Expected: build succeeds.

- [ ] **Step 4: Commit**

```powershell
git add frontend/src/components/StructuredFinancialMetricsPanel.tsx frontend/src/components/FinancialRiskEvidencePanel.tsx frontend/src/components/StartReadinessPanel.tsx frontend/src/hooks/useAnalysisSession.ts
git commit -m "Allow PDF financial metrics uploads in frontend"
```

---

### Task 7: Full Verification And Documentation Touchups

**Files:**
- Modify: `docs/superpowers/specs/2026-05-27-pdf-financial-metrics-ingestion-design.md` only if implementation choices drift from approved design.

- [ ] **Step 1: Run backend targeted tests**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --filter "StructuredFinancialMetricsTextParserTests|StructuredFinancialMetricsPdfExtractorTests|AnalysisSessionFinancialMetricsControllerTests"
```

Expected: PASS.

- [ ] **Step 2: Run full backend tests**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj
```

Expected: PASS.

- [ ] **Step 3: Build backend solution**

Run:

```powershell
dotnet build backend/Orchestration.slnx
```

Expected: PASS.

- [ ] **Step 4: Build frontend**

Run:

```powershell
npm run build
```

Working dir: `frontend`

Expected: PASS.

- [ ] **Step 5: Inspect git status**

Run:

```powershell
git status --short
```

Expected: clean after task commits, or only intentional uncommitted docs if a final note was needed.

- [ ] **Step 6: Final commit if docs changed**

If Step 5 shows only intentional docs changes, run:

```powershell
git add docs/superpowers/specs/2026-05-27-pdf-financial-metrics-ingestion-design.md
git commit -m "Document PDF OCR implementation details"
```

Expected: commit succeeds.

---

## Dependency Notes

- `UglyToad.PdfPig` is used only for native text extraction and is isolated behind `IPdfTextExtractor`.
- OCR depends on local `pdftoppm` and `tesseract` binaries. The app fails clearly with `PDF OCR dependencies are not configured.` when those binaries are unavailable.
- The current implementation does not require Python or cloud OCR for PDF ingestion.

## Self-Review

- Spec coverage: PDF upload, native text extraction, local OCR fallback, existing session persistence, provenance, frontend labels, errors, and tests are covered.
- Scope check: one subsystem, PDF-to-structured-metrics ingestion, no agent workflow rewrite.
- Placeholder scan: no unresolved placeholder markers or unspecified "add handling" steps remain.
- Type consistency: `IStructuredFinancialMetricsPdfExtractor`, `StructuredFinancialMetricsPdfExtractionResult`, `pdf_file`, `pdf_extraction`, and `pdf_ocr` names are consistent across tasks.
