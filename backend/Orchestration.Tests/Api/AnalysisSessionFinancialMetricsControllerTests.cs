using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Orchestration.Api.Controllers;
using Orchestration.Application.Activity;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Planner;
using Orchestration.Application.Agents.Planner.Reasoning;
using Orchestration.Application.Agents.Planner.ToolCalling;
using Orchestration.Application.AnalysisSessions;
using Orchestration.Domain.AnalysisSessions;
using Orchestration.Infrastructure.Persistence;
using Orchestration.Tests.Agents;
using Orchestration.Tests.Agents.Data.FinancialAnalysis;

namespace Orchestration.Tests.Api;

public sealed class AnalysisSessionFinancialMetricsControllerTests
{
    [Fact]
    public async Task SaveFinancialMetrics_Should_return_ok_and_persist_context_for_valid_metrics()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var publisher = new FakeActivityEventPublisher();
        var controller = CreateController(dbContext, publisher: publisher);

        var result = await controller.SaveFinancialMetrics(
            session.Id,
            StructuredFinancialMetricsSessionServiceTests.CreateInput(),
            CancellationToken.None
        );

        var response = result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<SaveFinancialMetricsResponse>()
            .Subject;
        response.IsValid.Should().BeTrue();
        response.Context.Should().NotBeNull();
        response.Context!.Metrics.Should().ContainSingle(metric => metric.Name == "revenue");
        response.Context.Provenance.Should().NotBeNull();
        response.Context.Provenance!.IngestionMethod.Should().Be("json_paste");
        session.ContextJson.Should().Contain("structuredFinancialMetrics");
    }

    [Fact]
    public async Task SaveFinancialMetrics_Should_return_ok_with_invalid_result_for_invalid_financial_input()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var publisher = new FakeActivityEventPublisher();
        var controller = CreateController(dbContext, publisher: publisher);

        var result = await controller.SaveFinancialMetrics(
            session.Id,
            StructuredFinancialMetricsSessionServiceTests.CreateInput(documentId: ""),
            CancellationToken.None
        );

        var response = result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<SaveFinancialMetricsResponse>()
            .Subject;
        response.IsValid.Should().BeFalse();
        response.Context.Should().BeNull();
        response.Errors.Should().Contain(issue => issue.Code == "DOCUMENT_ID_REQUIRED");
        publisher.PublishedEvents.Should().BeEmpty();
        session.ContextJson.Should().Be("{}");
    }

    [Fact]
    public async Task SaveFinancialMetrics_Should_return_not_found_for_unknown_session()
    {
        await using var dbContext = CreateDbContext();
        var controller = CreateController(dbContext);

        var result = await controller.SaveFinancialMetrics(
            Guid.NewGuid(),
            StructuredFinancialMetricsSessionServiceTests.CreateInput(),
            CancellationToken.None
        );

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task SaveFinancialMetrics_Should_return_bad_request_for_null_body()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var controller = CreateController(dbContext);

        var result = await controller.SaveFinancialMetrics(
            session.Id,
            null!,
            CancellationToken.None
        );

        result.Should().BeOfType<BadRequestResult>();
    }

    [Fact]
    public async Task SaveFinancialMetricsCsv_Should_return_ok_and_persist_context_for_valid_csv()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var controller = CreateController(dbContext);

        var result = await controller.SaveFinancialMetricsCsv(
            session.Id,
            CreateCsvInput(),
            CancellationToken.None
        );

        var response = result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<SaveFinancialMetricsResponse>()
            .Subject;
        response.IsValid.Should().BeTrue();
        response.Context.Should().NotBeNull();
        response.Context!.DocumentId.Should().Be("manual-csv-input");
        response.Context.Company.Should().Be("Manual Test Co");
        response.Context.Provenance.Should().NotBeNull();
        response.Context.Provenance!.IngestionMethod.Should().Be("csv_paste");
        response.Context.Metrics.Should().Contain(metric =>
            metric.Name == "revenue" &&
            metric.Period == "2024A" &&
            metric.Value == 1647768m
        );
        session.ContextJson.Should().Contain("structuredFinancialMetrics");
    }

    [Fact]
    public async Task SaveFinancialMetricsCsv_Should_return_invalid_result_without_persisting_for_invalid_csv()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var controller = CreateController(dbContext);

        var result = await controller.SaveFinancialMetricsCsv(
            session.Id,
            CreateCsvInput(csv: """
                name,value
                Revenue,1647768
                """),
            CancellationToken.None
        );

        var response = result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<SaveFinancialMetricsResponse>()
            .Subject;
        response.IsValid.Should().BeFalse();
        response.Context.Should().BeNull();
        response.Errors.Should().Contain(issue => issue.Code == "CSV_REQUIRED_HEADER_MISSING");
        session.ContextJson.Should().Be("{}");
    }

    [Fact]
    public async Task SaveFinancialMetricsCsv_Should_return_not_found_for_unknown_session()
    {
        await using var dbContext = CreateDbContext();
        var controller = CreateController(dbContext);

        var result = await controller.SaveFinancialMetricsCsv(
            Guid.NewGuid(),
            CreateCsvInput(),
            CancellationToken.None
        );

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task SaveFinancialMetricsCsv_Should_return_bad_request_for_null_body()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var controller = CreateController(dbContext);

        var result = await controller.SaveFinancialMetricsCsv(
            session.Id,
            null!,
            CancellationToken.None
        );

        result.Should().BeOfType<BadRequestResult>();
    }

    [Fact]
    public async Task GetFinancialMetrics_Should_return_context_for_session()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var controller = CreateController(dbContext);
        await controller.SaveFinancialMetrics(
            session.Id,
            StructuredFinancialMetricsSessionServiceTests.CreateInput(),
            CancellationToken.None
        );

        var result = await controller.GetFinancialMetrics(
            session.Id,
            CancellationToken.None
        );

        var response = result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<GetFinancialMetricsResponse>()
            .Subject;
        response.SessionId.Should().Be(session.Id);
        response.Context.Should().NotBeNull();
        response.Context!.Metrics.Should().ContainSingle(metric => metric.Name == "revenue");
    }

    [Fact]
    public async Task GetFinancialMetrics_Should_return_not_found_for_unknown_session()
    {
        await using var dbContext = CreateDbContext();
        var controller = CreateController(dbContext);

        var result = await controller.GetFinancialMetrics(
            Guid.NewGuid(),
            CancellationToken.None
        );

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task SaveFinancialMetricsFile_Should_persist_valid_json_file()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var controller = CreateController(dbContext);
        const string content = """
            {
              "documentId": "json-file-input",
              "company": "JSON File Co",
              "currency": "USD",
              "unit": "USD_thousand",
              "metrics": [
                {
                  "name": "Revenue",
                  "period": "2024A",
                  "value": 1647768,
                  "unit": "USD_thousand",
                  "currency": "USD",
                  "source": "file_upload",
                  "sourcePage": 18,
                  "confidence": 0.9
                }
              ]
            }
            """;

        var result = await controller.SaveFinancialMetricsFile(
            session.Id,
            CreateFileUploadRequest(
                "metrics.json",
                content
            ),
            CancellationToken.None
        );

        var response = result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<SaveFinancialMetricsFileResponse>()
            .Subject;
        response.IsValid.Should().BeTrue();
        response.FileName.Should().Be("metrics.json");
        response.FileType.Should().Be("json");
        response.FileSizeBytes.Should().BeGreaterThan(0);
        response.Context.Should().NotBeNull();
        response.Context!.DocumentId.Should().Be("json-file-input");
        response.Context.Company.Should().Be("JSON File Co");
        response.Context.Provenance.Should().NotBeNull();
        response.Context.Provenance!.IngestionMethod.Should().Be("json_file");
        response.Context.Provenance.OriginalFileName.Should().Be("metrics.json");
        response.Context.Provenance.FileSizeBytes.Should().Be(response.FileSizeBytes);
        response.Context.Provenance.ContentHash.Should().Be(ComputeSha256(content));
        response.Context.Provenance.MetricCount.Should().Be(1);
        response.Context.Metrics.Should().ContainSingle(metric => metric.Name == "revenue");
        session.ContextJson.Should().Contain("structuredFinancialMetrics");
    }

    [Fact]
    public async Task SaveFinancialMetricsFile_Should_use_form_metadata_as_json_fallback()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var controller = CreateController(dbContext);

        var result = await controller.SaveFinancialMetricsFile(
            session.Id,
            CreateFileUploadRequest(
                "metrics.json",
                """
                {
                  "documentId": "",
                  "metrics": [
                    {
                      "name": "Revenue",
                      "period": "2024A",
                      "value": 1647768
                    }
                  ]
                }
                """,
                documentId: "form-json-document",
                company: "Form Metadata Co",
                currency: "USD",
                unit: "USD_thousand"
            ),
            CancellationToken.None
        );

        var response = result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<SaveFinancialMetricsFileResponse>()
            .Subject;
        response.IsValid.Should().BeTrue();
        response.Context.Should().NotBeNull();
        response.Context!.DocumentId.Should().Be("form-json-document");
        response.Context.Company.Should().Be("Form Metadata Co");
        response.Context.Currency.Should().Be("USD");
        response.Context.Unit.Should().Be("USD_thousand");
    }

    [Fact]
    public async Task SaveFinancialMetricsFile_Should_return_bad_request_for_invalid_json()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var controller = CreateController(dbContext);

        var result = await controller.SaveFinancialMetricsFile(
            session.Id,
            CreateFileUploadRequest("metrics.json", "{not-json"),
            CancellationToken.None
        );

        var response = result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().BeOfType<FileUploadErrorResponse>()
            .Subject;
        response.Error.Should().Be("Invalid JSON file.");
        session.ContextJson.Should().Be("{}");
    }

    [Fact]
    public async Task SaveFinancialMetricsFile_Should_return_invalid_result_for_invalid_financial_json()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var publisher = new FakeActivityEventPublisher();
        var controller = CreateController(dbContext, publisher: publisher);

        var result = await controller.SaveFinancialMetricsFile(
            session.Id,
            CreateFileUploadRequest(
                "metrics.json",
                """
                {
                  "documentId": "",
                  "metrics": [
                    {
                      "name": "",
                      "period": "2024A",
                      "value": null
                    }
                  ]
                }
                """
            ),
            CancellationToken.None
        );

        var response = result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<SaveFinancialMetricsFileResponse>()
            .Subject;
        response.IsValid.Should().BeFalse();
        response.Context.Should().BeNull();
        response.Errors.Should().Contain(issue => issue.Code == "DOCUMENT_ID_REQUIRED");
        response.Errors.Should().Contain(issue => issue.Code == "METRIC_NAME_REQUIRED");
        response.Errors.Should().Contain(issue => issue.Code == "METRIC_VALUE_REQUIRED");
        publisher.PublishedEvents.Should().BeEmpty();
        session.ContextJson.Should().Be("{}");
    }

    [Fact]
    public async Task SaveFinancialMetricsFile_Should_return_not_found_for_unknown_session()
    {
        await using var dbContext = CreateDbContext();
        var controller = CreateController(dbContext);

        var result = await controller.SaveFinancialMetricsFile(
            Guid.NewGuid(),
            CreateFileUploadRequest(
                "metrics.json",
                """
                {
                  "documentId": "json-file-input",
                  "metrics": [
                    {
                      "name": "Revenue",
                      "period": "2024A",
                      "value": 1647768
                    }
                  ]
                }
                """
            ),
            CancellationToken.None
        );

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task SaveFinancialMetricsFile_Should_persist_valid_pdf_file()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var pdfExtractor = new FakeStructuredFinancialMetricsPdfExtractor(
            PdfResult.Valid(new StructuredFinancialMetricsInput(
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
                        Confidence: 0.9m
                    )
                ]
            ))
        );
        var controller = CreateController(dbContext, pdfExtractor: pdfExtractor);

        var result = await controller.SaveFinancialMetricsFile(
            session.Id,
            CreateFileUploadRequest(
                "report.pdf",
                "%PDF test content",
                documentId: "form-pdf-document",
                company: "Form PDF Co",
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
        response.Context!.Provenance.Should().NotBeNull();
        response.Context.Provenance!.IngestionMethod.Should().Be("pdf_file");
        response.Context.Provenance.OriginalFileName.Should().Be("report.pdf");
        response.Context.Metrics.Should().ContainSingle(metric =>
            metric.Name == "revenue" &&
            metric.Source == "pdf_extraction" &&
            metric.SourcePage == 18
        );
        pdfExtractor.LastRequest.Should().NotBeNull();
        pdfExtractor.LastRequest!.DocumentId.Should().Be("form-pdf-document");
    }

    [Fact]
    public async Task SaveFinancialMetricsFile_Should_return_invalid_result_for_pdf_without_metrics()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var pdfExtractor = new FakeStructuredFinancialMetricsPdfExtractor(
            PdfResult.Invalid("PDF_METRICS_NOT_FOUND", "No financial metrics were found.")
        );
        var controller = CreateController(dbContext, pdfExtractor: pdfExtractor);

        var result = await controller.SaveFinancialMetricsFile(
            session.Id,
            CreateFileUploadRequest(
                "report.pdf",
                "%PDF test content",
                documentId: "form-pdf-document"
            ),
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
        var session = AnalysisSession.Create(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var pdfExtractor = new FakeStructuredFinancialMetricsPdfExtractor(
            PdfResult.Invalid("PDF_OCR_NOT_CONFIGURED", "OCR is not configured.")
        );
        var controller = CreateController(dbContext, pdfExtractor: pdfExtractor);

        var result = await controller.SaveFinancialMetricsFile(
            session.Id,
            CreateFileUploadRequest(
                "report.pdf",
                "%PDF test content",
                documentId: "form-pdf-document"
            ),
            CancellationToken.None
        );

        var response = result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().BeOfType<FileUploadErrorResponse>()
            .Subject;
        response.Error.Should().Be("PDF OCR dependencies are not configured.");
    }

    [Fact]
    public async Task SaveFinancialMetricsFile_Should_persist_valid_csv_file()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var controller = CreateController(dbContext);

        var result = await controller.SaveFinancialMetricsFile(
            session.Id,
            CreateFileUploadRequest(
                "metrics.csv",
                """
                name,period,value,unit,currency,source,sourcePage,confidence
                Revenue,2024A,1647768,USD_thousand,USD,file_upload,18,0.9
                """,
                documentId: "csv-file-input",
                company: "CSV File Co",
                currency: "USD",
                unit: "USD_thousand"
            ),
            CancellationToken.None
        );

        var response = result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<SaveFinancialMetricsFileResponse>()
            .Subject;
        response.IsValid.Should().BeTrue();
        response.FileName.Should().Be("metrics.csv");
        response.FileType.Should().Be("csv");
        response.Context.Should().NotBeNull();
        response.Context!.DocumentId.Should().Be("csv-file-input");
        response.Context.Company.Should().Be("CSV File Co");
        response.Context.Provenance.Should().NotBeNull();
        response.Context.Provenance!.IngestionMethod.Should().Be("csv_file");
        response.Context.Provenance.OriginalFileName.Should().Be("metrics.csv");
        response.Context.Provenance.FileSizeBytes.Should().Be(response.FileSizeBytes);
        response.Context.Provenance.ContentHash.Should().NotBeNullOrWhiteSpace();
        response.Context.Metrics.Should().ContainSingle(metric => metric.Name == "revenue");
        session.ContextJson.Should().Contain("structuredFinancialMetrics");
    }

    [Fact]
    public async Task SaveFinancialMetricsFile_Should_require_document_id_for_csv()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var controller = CreateController(dbContext);

        var result = await controller.SaveFinancialMetricsFile(
            session.Id,
            CreateFileUploadRequest(
                "metrics.csv",
                """
                name,period,value
                Revenue,2024A,1647768
                """
            ),
            CancellationToken.None
        );

        var response = result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().BeOfType<FileUploadErrorResponse>()
            .Subject;
        response.Error.Should().Be("DocumentId is required for CSV uploads.");
        session.ContextJson.Should().Be("{}");
    }

    [Fact]
    public async Task SaveFinancialMetricsFile_Should_return_invalid_result_for_invalid_csv_header()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var controller = CreateController(dbContext);

        var result = await controller.SaveFinancialMetricsFile(
            session.Id,
            CreateFileUploadRequest(
                "metrics.csv",
                """
                name,value
                Revenue,1647768
                """,
                documentId: "csv-file-input"
            ),
            CancellationToken.None
        );

        var response = result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<SaveFinancialMetricsFileResponse>()
            .Subject;
        response.IsValid.Should().BeFalse();
        response.Context.Should().BeNull();
        response.Errors.Should().Contain(issue => issue.Code == "CSV_REQUIRED_HEADER_MISSING");
        session.ContextJson.Should().Be("{}");
    }

    [Fact]
    public async Task SaveFinancialMetricsFile_Should_return_invalid_result_for_invalid_csv_decimal()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var controller = CreateController(dbContext);

        var result = await controller.SaveFinancialMetricsFile(
            session.Id,
            CreateFileUploadRequest(
                "metrics.csv",
                """
                name,period,value
                Revenue,2024A,not-a-number
                """,
                documentId: "csv-file-input"
            ),
            CancellationToken.None
        );

        var response = result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<SaveFinancialMetricsFileResponse>()
            .Subject;
        response.IsValid.Should().BeFalse();
        response.Errors.Should().Contain(issue => issue.Code == "CSV_INVALID_DECIMAL");
        session.ContextJson.Should().Be("{}");
    }

    [Fact]
    public async Task SaveFinancialMetricsFile_Should_return_bad_request_for_missing_file()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var controller = CreateController(dbContext);

        var result = await controller.SaveFinancialMetricsFile(
            session.Id,
            new StructuredFinancialMetricsFileUploadRequest(),
            CancellationToken.None
        );

        var response = result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().BeOfType<FileUploadErrorResponse>()
            .Subject;
        response.Error.Should().Be("Missing file.");
    }

    [Fact]
    public async Task SaveFinancialMetricsFile_Should_return_bad_request_for_empty_file()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var controller = CreateController(dbContext);

        var result = await controller.SaveFinancialMetricsFile(
            session.Id,
            CreateFileUploadRequest("metrics.json", ""),
            CancellationToken.None
        );

        var response = result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().BeOfType<FileUploadErrorResponse>()
            .Subject;
        response.Error.Should().Be("Empty file.");
    }

    [Fact]
    public async Task SaveFinancialMetricsFile_Should_return_bad_request_for_whitespace_file()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var controller = CreateController(dbContext);

        var result = await controller.SaveFinancialMetricsFile(
            session.Id,
            CreateFileUploadRequest("metrics.json", "   "),
            CancellationToken.None
        );

        var response = result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().BeOfType<FileUploadErrorResponse>()
            .Subject;
        response.Error.Should().Be("Uploaded file is empty.");
    }

    [Fact]
    public async Task SaveFinancialMetricsFile_Should_return_bad_request_for_unsupported_extension()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var controller = CreateController(dbContext);

        var result = await controller.SaveFinancialMetricsFile(
            session.Id,
            CreateFileUploadRequest("metrics.txt", "name,period,value"),
            CancellationToken.None
        );

        var response = result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().BeOfType<FileUploadErrorResponse>()
            .Subject;
        response.Error.Should().Be("Unsupported file extension.");
    }

    [Fact]
    public async Task SaveFinancialMetricsFile_Should_return_bad_request_for_file_that_exceeds_size_limit()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var controller = CreateController(
            dbContext,
            new StructuredFinancialMetricsFileUploadOptions
            {
                MaxFileSizeBytes = 5,
                AllowedExtensions = [".json", ".csv"]
            }
        );

        var result = await controller.SaveFinancialMetricsFile(
            session.Id,
            CreateFileUploadRequest("metrics.json", """{"documentId":"x"}"""),
            CancellationToken.None
        );

        var response = result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().BeOfType<FileUploadErrorResponse>()
            .Subject;
        response.Error.Should().Be("File exceeds maximum allowed size.");
    }

    [Fact]
    public async Task SaveFinancialMetricsFile_Should_accept_uppercase_extension_and_ignore_path_segments()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var controller = CreateController(dbContext);

        var result = await controller.SaveFinancialMetricsFile(
            session.Id,
            CreateFileUploadRequest(
                "C:\\temp\\METRICS.JSON",
                """
                {
                  "documentId": "uppercase-json-file",
                  "metrics": [
                    {
                      "name": "Revenue",
                      "period": "2024A",
                      "value": 1647768
                    }
                  ]
                }
                """,
                currency: "USD",
                unit: "USD_thousand"
            ),
            CancellationToken.None
        );

        var response = result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<SaveFinancialMetricsFileResponse>()
            .Subject;
        response.IsValid.Should().BeTrue();
        response.FileName.Should().Be("METRICS.JSON");
        response.Context.Should().NotBeNull();
        response.Context!.DocumentId.Should().Be("uppercase-json-file");
        response.Context.Provenance.Should().NotBeNull();
        response.Context.Provenance!.OriginalFileName.Should().Be("METRICS.JSON");
    }

    [Fact]
    public async Task GetStartPreflight_Should_return_blocked_result_when_required_metrics_are_missing()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var controller = CreateController(
            dbContext,
            dataAgentOptions: RequiredMetricsOptions()
        );

        var result = await controller.GetStartPreflight(
            session.Id,
            CancellationToken.None
        );

        var preflight = result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<AnalysisSessionStartPreflightResult>()
            .Subject;
        preflight.CanStart.Should().BeFalse();
        preflight.Errors.Should().ContainSingle(issue =>
            issue.Code == AnalysisSessionStartPreflightValidator
                .StructuredFinancialMetricsRequiredCode
        );
    }

    [Fact]
    public async Task StartSession_Should_return_conflict_when_required_metrics_are_missing()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var publisher = new FakeActivityEventPublisher();
        var planner = new FakePlannerAgent();
        var controller = CreateController(
            dbContext,
            publisher: publisher,
            dataAgentOptions: RequiredMetricsOptions(),
            orchestrator: CreateOrchestrator(dbContext, publisher, planner)
        );

        var result = await controller.StartSession(
            session.Id,
            CancellationToken.None
        );

        var preflight = result.Should().BeOfType<ConflictObjectResult>()
            .Which.Value.Should().BeOfType<AnalysisSessionStartPreflightResult>()
            .Subject;
        preflight.CanStart.Should().BeFalse();
        session.Status.Should().Be(AnalysisSessionStatus.Pending);
        session.CurrentAgent.Should().BeNull();
        planner.RunCalls.Should().Be(0);
        publisher.PublishedEvents.Should().ContainSingle(evt =>
            evt.Type == "analysis_start_blocked" &&
            evt.Agent == "Orchestrator"
        );
    }

    [Fact]
    public async Task StartSession_Should_start_when_required_metrics_are_attached()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var publisher = new FakeActivityEventPublisher();
        var planner = new FakePlannerAgent();
        var controller = CreateController(
            dbContext,
            publisher: publisher,
            dataAgentOptions: RequiredMetricsOptions(),
            orchestrator: CreateOrchestrator(dbContext, publisher, planner)
        );
        await controller.SaveFinancialMetrics(
            session.Id,
            StructuredFinancialMetricsSessionServiceTests.CreateInput(),
            CancellationToken.None
        );

        var result = await controller.StartSession(
            session.Id,
            CancellationToken.None
        );

        result.Should().BeOfType<OkObjectResult>();
        planner.RunCalls.Should().Be(1);
        session.Status.Should().Be(AnalysisSessionStatus.Completed);
    }

    [Fact]
    public async Task StartSession_Should_not_pass_request_cancellation_to_orchestrator_after_preflight()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var publisher = new FakeActivityEventPublisher();
        var planner = new FakePlannerAgent();
        var controller = CreateController(
            dbContext,
            publisher: publisher,
            dataAgentOptions: RequiredMetricsOptions(),
            orchestrator: CreateOrchestrator(dbContext, publisher, planner)
        );
        await controller.SaveFinancialMetrics(
            session.Id,
            StructuredFinancialMetricsSessionServiceTests.CreateInput(),
            CancellationToken.None
        );
        using var requestCancellation = new CancellationTokenSource();

        var result = await controller.StartSession(
            session.Id,
            requestCancellation.Token
        );

        result.Should().BeOfType<OkObjectResult>();
        planner.RunCalls.Should().Be(1);
        planner.LastCancellationTokenCanBeCanceled.Should().BeFalse();
        session.Status.Should().Be(AnalysisSessionStatus.Completed);
    }

    [Fact]
    public async Task StartSession_Should_allow_demo_mode_without_session_metrics()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var publisher = new FakeActivityEventPublisher();
        var planner = new FakePlannerAgent();
        var controller = CreateController(
            dbContext,
            publisher: publisher,
            dataAgentOptions: new DataAgentOptions
            {
                FinancialAnalysisToolsEnabled = true,
                UseFixtureMetricsFallback = true,
                RequireSessionFinancialMetrics = false
            },
            orchestrator: CreateOrchestrator(dbContext, publisher, planner)
        );

        var result = await controller.StartSession(
            session.Id,
            CancellationToken.None
        );

        result.Should().BeOfType<OkObjectResult>();
        planner.RunCalls.Should().Be(1);
        session.Status.Should().Be(AnalysisSessionStatus.Completed);
    }

    private static AnalysisSessionsController CreateController(
        OrchestrationDbContext dbContext,
        StructuredFinancialMetricsFileUploadOptions? fileUploadOptions = null,
        FakeActivityEventPublisher? publisher = null,
        DataAgentOptions? dataAgentOptions = null,
        AnalysisOrchestratorService? orchestrator = null,
        IStructuredFinancialMetricsPdfExtractor? pdfExtractor = null)
    {
        publisher ??= new FakeActivityEventPublisher();

        var controller = new AnalysisSessionsController(
            dbContext,
            orchestrator: orchestrator!,
            new AnalysisSessionStartPreflightValidator(
                Options.Create(dataAgentOptions ?? new DataAgentOptions())
            ),
            publisher,
            StructuredFinancialMetricsSessionServiceTests.CreateService(dbContext, publisher),
            new StructuredFinancialMetricsCsvParser(),
            pdfExtractor ?? new FakeStructuredFinancialMetricsPdfExtractor(PdfResult.Invalid("PDF_NOT_CONFIGURED", "PDF extractor was not configured.")),
            Options.Create(fileUploadOptions ?? new StructuredFinancialMetricsFileUploadOptions())
        );

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "00000000-0000-0000-0000-000000000001"),
            new(ClaimTypes.Name, "admin@ezemartino.com")
        };
        var identity = new ClaimsIdentity(claims, "TestAuthType");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };

        return controller;
    }

    private static AnalysisOrchestratorService CreateOrchestrator(
        OrchestrationDbContext dbContext,
        IActivityEventPublisher publisher,
        IPlannerAgent planner)
    {
        return new AnalysisOrchestratorService(
            dbContext,
            new AnalysisSessionWorkflowService(new AnalysisSessionStateMachine()),
            publisher,
            planner
        );
    }

    private static DataAgentOptions RequiredMetricsOptions()
    {
        return new DataAgentOptions
        {
            FinancialAnalysisToolsEnabled = true,
            UseFixtureMetricsFallback = false,
            RequireSessionFinancialMetrics = true
        };
    }

    private static OrchestrationDbContext CreateDbContext()
    {
        return StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
    }

    private static StructuredFinancialMetricsCsvInput CreateCsvInput(
        string csv = """
            name,period,value,unit,currency,source,sourcePage,confidence
            Revenue,2024A,1647768,USD_thousand,USD,manual_upload,18,0.9
            Gross Profit,2024A,924000,USD_thousand,USD,manual_upload,18,0.85
            """)
    {
        return new StructuredFinancialMetricsCsvInput(
            DocumentId: "manual-csv-input",
            Company: "Manual Test Co",
            Currency: "USD",
            Unit: "USD_thousand",
            Csv: csv
        );
    }

    private static StructuredFinancialMetricsFileUploadRequest CreateFileUploadRequest(
        string fileName,
        string content,
        string? documentId = null,
        string? company = null,
        string? currency = null,
        string? unit = null)
    {
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        var file = new FormFile(stream, 0, stream.Length, "file", fileName);

        return new StructuredFinancialMetricsFileUploadRequest
        {
            File = file,
            DocumentId = documentId,
            Company = company,
            Currency = currency,
            Unit = unit
        };
    }

    private static string ComputeSha256(
        string content)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed class FakePlannerAgent : IPlannerAgent
    {
        public int RunCalls { get; private set; }
        public bool? LastCancellationTokenCanBeCanceled { get; private set; }

        public Task<PlannerAgentResult> RunAsync(
            AnalysisSession session,
            CancellationToken cancellationToken)
        {
            RunCalls++;
            LastCancellationTokenCanBeCanceled = cancellationToken.CanBeCanceled;

            return Task.FromResult(new PlannerAgentResult(
                RequiresHumanApproval: false,
                Summary: "No human approval required.",
                DataResult: new DataAgentResult(
                    HasAnomaly: false,
                    Severity: "Low",
                    Summary: "No anomaly detected.",
                    Engine: "Test DataAgent",
                    Evidence: []
                ),
                LegalResult: new LegalAgentResult(
                    HasComplianceRisk: false,
                    RiskLevel: "Low",
                    Summary: "No compliance risk detected.",
                    Engine: "Test LegalAgent",
                    Evidence: [],
                    Warnings: []
                ),
                ReasoningResult: new PlannerReasoningResult(
                    Engine: "Test Planner",
                    Summary: "Planner completed.",
                    RecommendedActions: [],
                    RiskFactors: [],
                    Limitations: [],
                    UsedLlm: false,
                    UsedFallback: false,
                    Provider: null,
                    Model: null,
                    FailureReason: null
                ),
                ToolPlan: ToolPlanAuditResult.Empty
            ));
        }
    }

    private sealed class FakeStructuredFinancialMetricsPdfExtractor(
        StructuredFinancialMetricsPdfExtractionResult result) : IStructuredFinancialMetricsPdfExtractor
    {
        public StructuredFinancialMetricsPdfExtractionRequest? LastRequest { get; private set; }

        public Task<StructuredFinancialMetricsPdfExtractionResult> ExtractAsync(
            Stream pdf,
            StructuredFinancialMetricsPdfExtractionRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;

            return Task.FromResult(result);
        }
    }

    private static class PdfResult
    {
        public static StructuredFinancialMetricsPdfExtractionResult Valid(
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

        public static StructuredFinancialMetricsPdfExtractionResult Invalid(
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
                        Severity: "error"
                    )
                ],
                Warnings: [],
                UsedOcr: false
            );
        }
    }
}
