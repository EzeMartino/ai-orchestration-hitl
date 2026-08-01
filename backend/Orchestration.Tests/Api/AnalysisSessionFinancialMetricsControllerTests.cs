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
using Orchestration.Application.Agents.Data.FinancialAnalysis.Extraction;
using Orchestration.Application.Agents.Shared;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Planner;
using Orchestration.Application.Agents.Planner.Reasoning;
using Orchestration.Application.Agents.Planner.ToolCalling;
using Orchestration.Application.AnalysisSessions;
using Orchestration.Domain.AnalysisSessions;
using Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.Pdf;
using Orchestration.Infrastructure.Persistence;
using Orchestration.Tests.Agents;
using Orchestration.Tests.Agents.Data.FinancialAnalysis;
using UglyToad.PdfPig.Core;

namespace Orchestration.Tests.Api;

public sealed class AnalysisSessionFinancialMetricsControllerTests
{
    private static FinancialReportSummaryInput ApiReportSummaryInput => new(
        "  balance-sheet-2025.pdf  ",
        842350.75m,
        187,
        new DateTimeOffset(2026, 7, 12, 18, 30, 0, TimeSpan.Zero));

    private static FinancialReportSummary ApiReportSummary => new(
        "balance-sheet-2025.pdf",
        842350.75m,
        187,
        new DateTimeOffset(2026, 7, 12, 18, 30, 0, TimeSpan.Zero));

    [Fact]
    public void AnalysisSessionsController_Should_expose_only_review_ingestion_constructor()
    {
        var constructor = typeof(AnalysisSessionsController)
            .GetConstructors()
            .Should()
            .ContainSingle()
            .Subject;
        var parameterTypes = constructor.GetParameters()
            .Select(parameter => parameter.ParameterType);

        parameterTypes.Should().Contain(typeof(IStructuredFinancialMetricsPdfIngestionService));
        parameterTypes.Should().Contain(typeof(IFinancialMetricsExtractionDraftService));
        parameterTypes.Should().NotContain(typeof(IStructuredFinancialMetricsPdfExtractor));
    }

    [Fact]
    public async Task SaveFinancialMetrics_Should_return_ok_and_persist_context_for_valid_metrics()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var publisher = new FakeActivityEventPublisher();
        var controller = CreateController(dbContext, publisher: publisher);

        var result = await controller.SaveFinancialMetrics(
            session.Id,
            StructuredFinancialMetricsSessionServiceTests.CreateInput() with
            {
                ReportSummary = ApiReportSummaryInput
            },
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
        response.ReportSummary.Should().Be(ApiReportSummary);
        session.ContextJson.Should().Contain("structuredFinancialMetrics");
    }

    [Fact]
    public async Task SaveFinancialMetrics_Should_return_ok_with_invalid_result_for_invalid_financial_input()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
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
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
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
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
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
        response.ReportSummary.Should().Be(ApiReportSummary);
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
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
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
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
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
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var controller = CreateController(dbContext);
        await controller.SaveFinancialMetrics(
            session.Id,
            StructuredFinancialMetricsSessionServiceTests.CreateInput() with
            {
                ReportSummary = ApiReportSummaryInput
            },
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
        response.ReportSummary.Should().Be(ApiReportSummary);
    }

    [Fact]
    public async Task GetFinancialMetrics_ShouldReadMetricsAndSummaryFromOneCombinedSnapshot()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var inner = StructuredFinancialMetricsSessionServiceTests.CreateService(dbContext);
        await inner.SaveAsync(
            session.Id,
            StructuredFinancialMetricsSessionServiceTests.CreateInput() with
            {
                ReportSummary = ApiReportSummaryInput
            },
            CancellationToken.None);
        var snapshotService = new CombinedSnapshotOnlySessionService(inner);
        var controller = CreateController(
            dbContext,
            financialMetricsSessionService: snapshotService);

        var result = await controller.GetFinancialMetrics(
            session.Id,
            CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<GetFinancialMetricsResponse>().Subject;
        snapshotService.CombinedGetCalls.Should().Be(1);
        response.Context.Should().NotBeNull();
        response.Context!.DocumentId.Should().Be("vista-energy-structured-input");
        response.ReportSummary.Should().Be(ApiReportSummary);
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
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var controller = CreateController(dbContext);
        const string content = """
            {
              "documentId": "json-file-input",
              "company": "JSON File Co",
              "currency": "USD",
              "unit": "USD_thousand",
              "reportSummary": {
                "reportName": "  balance-sheet-2025.pdf  ",
                "totalAmount": 842350.75,
                "transactionCount": 187,
                "submittedAt": "2026-07-12T18:30:00Z"
              },
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
        response.Outcome.Should().Be("accepted");
        response.ReviewDraft.Should().BeNull();
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
        response.ReportSummary.Should().Be(ApiReportSummary);
        session.ContextJson.Should().Contain("structuredFinancialMetrics");
    }

    [Fact]
    public async Task SaveFinancialMetricsFile_Malformed_json_summary_timestamp_Should_return_stable_invalid_issue()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var controller = CreateController(dbContext);

        var result = await controller.SaveFinancialMetricsFile(
            session.Id,
            CreateFileUploadRequest(
                "metrics.json",
                """
                {
                  "documentId": "json-file-input",
                  "reportSummary": {
                    "reportName": "report.json",
                    "totalAmount": 10,
                    "transactionCount": 1,
                    "submittedAt": "not-an-iso-timestamp"
                  },
                  "metrics": [{ "name": "Revenue", "period": "2024A", "value": 10 }]
                }
                """),
            CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<SaveFinancialMetricsFileResponse>()
            .Subject;
        response.IsValid.Should().BeFalse();
        response.Errors.Should().ContainSingle(issue =>
            issue.Code == "SUBMITTED_AT_INVALID");
        session.ContextJson.Should().Be("{}");
    }

    [Theory]
    [InlineData("{\"unexpected\":true}")]
    [InlineData("[1,2]")]
    public async Task SaveFinancialMetricsFile_Object_or_array_json_summary_timestamp_Should_return_stable_invalid_issue(
        string timestampJson)
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var controller = CreateController(dbContext);

        var result = await controller.SaveFinancialMetricsFile(
            session.Id,
            CreateFileUploadRequest(
                "metrics.json",
                $$"""
                {
                  "documentId": "json-file-input",
                  "reportSummary": {
                    "reportName": "report.json",
                    "totalAmount": 10,
                    "transactionCount": 1,
                    "submittedAt": {{timestampJson}}
                  },
                  "metrics": [{ "name": "Revenue", "period": "2024A", "value": 10 }]
                }
                """),
            CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<SaveFinancialMetricsFileResponse>()
            .Subject;
        response.IsValid.Should().BeFalse();
        response.Errors.Should().ContainSingle(issue =>
            issue.Code == "SUBMITTED_AT_INVALID");
        session.ContextJson.Should().Be("{}");
    }

    [Fact]
    public async Task SaveFinancialMetricsFile_Should_use_form_metadata_as_json_fallback()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
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
                  "reportSummary": {
                    "reportName": "Test financial report",
                    "totalAmount": 1250.50,
                    "transactionCount": 7,
                    "submittedAt": "2026-07-12T12:00:00Z"
                  },
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
        response.ReportSummary.Should().Be(new FinancialReportSummary(
            "Test financial report",
            1250.50m,
            7,
            new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task SaveFinancialMetricsFile_Should_return_bad_request_for_invalid_json()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
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
        response.Error.Should().Be("Archivo JSON no válido.");
        session.ContextJson.Should().Be("{}");
    }

    [Fact]
    public async Task SaveFinancialMetricsFile_Should_return_invalid_result_for_invalid_financial_json()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
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
    public async Task SaveFinancialMetricsFile_Should_return_accepted_for_pdf_when_ingestion_accepts()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var saveResult = CreateValidSaveResult(session.Id);
        var ingestion = new FakeStructuredFinancialMetricsPdfIngestionService(
            new StructuredFinancialMetricsPdfIngestionResult(
                FinancialMetricsFileOutcome.Accepted,
                saveResult,
                ReviewDraft: null,
                Errors: [],
                Warnings: []
            )
        );
        var controller = CreateController(dbContext, pdfIngestionService: ingestion);

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
        response.Outcome.Should().Be("accepted");
        response.ReviewDraft.Should().BeNull();
        response.Context.Should().NotBeNull();
        response.Context!.Provenance.Should().NotBeNull();
        response.Context.Provenance!.IngestionMethod.Should().Be("pdf_file");
        response.Context.Provenance.OriginalFileName.Should().Be("report.pdf");
        response.Context.Metrics.Should().ContainSingle(metric =>
            metric.Name == "revenue" &&
            metric.Source == "pdf_extraction" &&
            metric.SourcePage == 18
        );
        response.ReportSummary.Should().Be(ApiReportSummary);
        ingestion.LastRequest.Should().NotBeNull();
        ingestion.LastRequest!.SessionId.Should().Be(session.Id);
        ingestion.LastRequest.UserId.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        ingestion.LastRequest.DocumentId.Should().Be("form-pdf-document");
        ingestion.LastRequest.ReportSummary.Should().BeEquivalentTo(ApiReportSummaryInput);
    }

    [Fact]
    public async Task SaveFinancialMetricsFile_Should_return_review_required_for_pdf_when_ingestion_requires_review()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var draft = CreateDraft(session.Id);
        var ingestion = new FakeStructuredFinancialMetricsPdfIngestionService(
            new StructuredFinancialMetricsPdfIngestionResult(
                FinancialMetricsFileOutcome.ReviewRequired,
                SaveResult: null,
                draft,
                Errors:
                [
                    new FinancialMetricsValidationIssue(
                        "PDF_REVIEW_REQUIRED",
                        "Review required.",
                        MetricName: null,
                        Period: null,
                        Severity: "error"
                    )
                ],
                Warnings: []
            )
        );
        var controller = CreateController(dbContext, pdfIngestionService: ingestion);

        var result = await controller.SaveFinancialMetricsFile(
            session.Id,
            CreateFileUploadRequest("report.pdf", "%PDF test content", documentId: "form-pdf-document"),
            CancellationToken.None
        );

        var response = result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<SaveFinancialMetricsFileResponse>()
            .Subject;
        response.Outcome.Should().Be("review_required");
        response.IsValid.Should().BeFalse();
        response.Context.Should().BeNull();
        response.ReviewDraft.Should().Be(draft);
        response.Errors.Should().ContainSingle(issue => issue.Code == "PDF_REVIEW_REQUIRED");
        session.ContextJson.Should().Be("{}");
    }

    [Fact]
    public async Task SaveFinancialMetricsFile_Should_return_failed_for_pdf_when_ingestion_fails()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var ingestion = new FakeStructuredFinancialMetricsPdfIngestionService(
            new StructuredFinancialMetricsPdfIngestionResult(
                FinancialMetricsFileOutcome.Failed,
                SaveResult: null,
                ReviewDraft: null,
                Errors:
                [
                    new FinancialMetricsValidationIssue(
                        "PDF_INGESTION_FAILED",
                        "PDF ingestion failed.",
                        MetricName: null,
                        Period: null,
                        Severity: "error"
                    )
                ],
                Warnings: []
            )
        );
        var controller = CreateController(dbContext, pdfIngestionService: ingestion);

        var result = await controller.SaveFinancialMetricsFile(
            session.Id,
            CreateFileUploadRequest("report.pdf", "%PDF test content", documentId: "form-pdf-document"),
            CancellationToken.None
        );

        var response = result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<SaveFinancialMetricsFileResponse>()
            .Subject;
        response.Outcome.Should().Be("failed");
        response.IsValid.Should().BeFalse();
        response.Context.Should().BeNull();
        response.ReviewDraft.Should().BeNull();
        response.Errors.Should().ContainSingle(issue => issue.Code == "PDF_INGESTION_FAILED");
        session.ContextJson.Should().Be("{}");
    }

    [Fact]
    public async Task SaveFinancialMetricsFile_Should_return_bad_request_when_pdf_ingestion_rejects_invalid_file()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var ingestion = new FakeStructuredFinancialMetricsPdfIngestionService(
            exception: new PdfDocumentFormatException("PDF payload is corrupt.")
        );
        var controller = CreateController(dbContext, pdfIngestionService: ingestion);

        var result = await controller.SaveFinancialMetricsFile(
            session.Id,
            CreateFileUploadRequest(
                "report.pdf",
                "%PDF corrupt content",
                documentId: "form-pdf-document"
            ),
            CancellationToken.None
        );

        var response = result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().BeOfType<FileUploadErrorResponse>()
            .Subject;
        response.Error.Should().Be("Archivo PDF no válido.");
        session.ContextJson.Should().Be("{}");
    }

    [Fact]
    public async Task SaveFinancialMetricsFile_Should_pass_pdf_bytes_and_metadata_to_ingestion_service()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        byte[] pdfBytes = [0x25, 0x50, 0x44, 0x46, 0x00, 0x80, 0xff, 0x0a];
        var ingestion = new FakeStructuredFinancialMetricsPdfIngestionService(
            new StructuredFinancialMetricsPdfIngestionResult(
                FinancialMetricsFileOutcome.Accepted,
                CreateValidSaveResult(session.Id),
                ReviewDraft: null,
                Errors: [],
                Warnings: []
            )
        );
        var controller = CreateController(dbContext, pdfIngestionService: ingestion);

        var result = await controller.SaveFinancialMetricsFile(
            session.Id,
            CreateFileUploadRequest(
                "C:\\unsafe\\report.pdf",
                pdfBytes,
                documentId: "form-pdf-document",
                company: "Form PDF Co",
                currency: "USD",
                unit: "USD_thousand"
            ),
            CancellationToken.None
        );

        result.Should().BeOfType<OkObjectResult>();
        ingestion.LastRequest.Should().NotBeNull();
        ingestion.LastRequest!.PdfBytes.Should().Equal(pdfBytes);
        ingestion.LastRequest.OriginalFileName.Should().Be("report.pdf");
        ingestion.LastRequest.FileSizeBytes.Should().Be(pdfBytes.Length);
        ingestion.LastRequest.ContentHash.Should().Be(ComputeSha256(pdfBytes));
        ingestion.LastRequest.DocumentId.Should().Be("form-pdf-document");
        ingestion.LastRequest.Company.Should().Be("Form PDF Co");
        ingestion.LastRequest.Currency.Should().Be("USD");
        ingestion.LastRequest.Unit.Should().Be("USD_thousand");
    }

    [Fact]
    public async Task SaveFinancialMetricsFile_Should_return_review_required_for_pdf_without_metrics()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var ingestion = new FakeStructuredFinancialMetricsPdfIngestionService(
            new StructuredFinancialMetricsPdfIngestionResult(
                FinancialMetricsFileOutcome.ReviewRequired,
                SaveResult: null,
                CreateDraft(session.Id),
                Errors:
                [
                    new FinancialMetricsValidationIssue(
                        "PDF_METRICS_NOT_FOUND",
                        "No financial metrics were found.",
                        MetricName: null,
                        Period: null,
                        Severity: "error"
                    )
                ],
                Warnings: []
            )
        );
        var controller = CreateController(dbContext, pdfIngestionService: ingestion);

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
        response.Outcome.Should().Be("review_required");
        response.ReviewDraft.Should().NotBeNull();
        response.Errors.Should().ContainSingle(issue => issue.Code == "PDF_METRICS_NOT_FOUND");
        session.ContextJson.Should().Be("{}");
    }

    [Fact]
    public async Task SaveFinancialMetricsFile_Should_return_bad_request_when_pdf_ocr_is_not_configured()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var ingestion = new FakeStructuredFinancialMetricsPdfIngestionService(
            exception: new PdfOcrDependencyException("OCR is not configured.")
        );
        var controller = CreateController(dbContext, pdfIngestionService: ingestion);

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
        response.Error.Should().Be("Las dependencias de OCR para PDF no están configuradas.");
    }

    [Fact]
    public async Task SaveFinancialMetricsFile_Should_persist_valid_csv_file()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var csvParser = new CapturingStructuredFinancialMetricsCsvParser();
        var controller = CreateController(dbContext, csvParser: csvParser);

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
        response.Outcome.Should().Be("accepted");
        response.ReviewDraft.Should().BeNull();
        response.Context.Should().NotBeNull();
        response.Context!.DocumentId.Should().Be("csv-file-input");
        response.Context.Company.Should().Be("CSV File Co");
        response.Context.Provenance.Should().NotBeNull();
        response.Context.Provenance!.IngestionMethod.Should().Be("csv_file");
        response.Context.Provenance.OriginalFileName.Should().Be("metrics.csv");
        response.Context.Provenance.FileSizeBytes.Should().Be(response.FileSizeBytes);
        response.Context.Provenance.ContentHash.Should().NotBeNullOrWhiteSpace();
        response.Context.Metrics.Should().ContainSingle(metric => metric.Name == "revenue");
        response.ReportSummary.Should().Be(ApiReportSummary);
        csvParser.LastInput.Should().NotBeNull();
        csvParser.LastInput!.ReportSummary.Should().BeEquivalentTo(ApiReportSummaryInput);
        session.ContextJson.Should().Contain("structuredFinancialMetrics");
    }

    [Fact]
    public async Task SaveFinancialMetricsFile_Malformed_form_summary_timestamp_Should_return_stable_invalid_issue()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var controller = CreateController(dbContext);

        var result = await controller.SaveFinancialMetricsFile(
            session.Id,
            CreateFileUploadRequest(
                "metrics.csv",
                """
                name,period,value
                Revenue,2024A,10
                """,
                documentId: "csv-file-input",
                submittedAt: "not-an-iso-timestamp"),
            CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<SaveFinancialMetricsFileResponse>()
            .Subject;
        response.IsValid.Should().BeFalse();
        response.Errors.Should().ContainSingle(issue =>
            issue.Code == "SUBMITTED_AT_INVALID");
        session.ContextJson.Should().Be("{}");
    }

    [Fact]
    public async Task SaveFinancialMetricsFile_Should_require_document_id_for_csv()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
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
        response.Error.Should().Be("Se requiere el identificador de documento (DocumentId) para cargas de CSV.");
        session.ContextJson.Should().Be("{}");
    }

    [Fact]
    public async Task SaveFinancialMetricsFile_Should_return_invalid_result_for_invalid_csv_header()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
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
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
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
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
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
        response.Error.Should().Be("Falta el archivo.");
    }

    [Fact]
    public async Task SaveFinancialMetricsFile_Should_return_bad_request_for_empty_file()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
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
        response.Error.Should().Be("Archivo vacío.");
    }

    [Fact]
    public async Task SaveFinancialMetricsFile_Should_return_bad_request_for_whitespace_file()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
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
        response.Error.Should().Be("El archivo subido está vacío.");
    }

    [Fact]
    public async Task SaveFinancialMetricsFile_Should_return_bad_request_for_unsupported_extension()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
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
        response.Error.Should().Be("Extensión de archivo no soportada.");
    }

    [Fact]
    public async Task SaveFinancialMetricsFile_Should_return_bad_request_for_file_that_exceeds_size_limit()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
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
        response.Error.Should().Be("El archivo excede el tamaño máximo permitido.");
    }

    [Fact]
    public async Task SaveFinancialMetricsFile_Should_accept_uppercase_extension_and_ignore_path_segments()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
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
                  "reportSummary": {
                    "reportName": "Test financial report",
                    "totalAmount": 1250.50,
                    "transactionCount": 7,
                    "submittedAt": "2026-07-12T12:00:00Z"
                  },
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
        response.Outcome.Should().Be("accepted");
        response.ReviewDraft.Should().BeNull();
        response.Context.Should().NotBeNull();
        response.Context!.DocumentId.Should().Be("uppercase-json-file");
        response.Context.Provenance.Should().NotBeNull();
        response.Context.Provenance!.OriginalFileName.Should().Be("METRICS.JSON");
    }

    [Fact]
    public async Task GetFinancialMetricsReview_Should_return_pending_draft_for_current_user()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var draft = CreateDraft(session.Id);
        var draftService = new FakeFinancialMetricsExtractionDraftService
        {
            GetPendingResult = FinancialMetricsExtractionDraftServiceResult.Success(draft)
        };
        var controller = CreateController(dbContext, draftService: draftService);

        var result = await controller.GetFinancialMetricsReview(session.Id, CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().Be(draft);
        draftService.LastSessionId.Should().Be(session.Id);
        draftService.LastUserId.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    }

    [Fact]
    public async Task GetFinancialMetricsReview_Should_return_not_found_for_another_users_draft()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var draftService = new FakeFinancialMetricsExtractionDraftService
        {
            GetPendingResult = FinancialMetricsExtractionDraftServiceResult.NotFound("Draft not found.")
        };
        var controller = CreateController(dbContext, draftService: draftService);

        var result = await controller.GetFinancialMetricsReview(session.Id, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task UpdateFinancialMetricsReview_Should_use_route_session_and_current_user()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var draftId = Guid.Parse("00000000-0000-0000-0000-000000000099");
        var request = CreateDraftUpdateRequest(session.Id);
        var draft = CreateDraft(session.Id, draftId);
        var draftService = new FakeFinancialMetricsExtractionDraftService
        {
            UpdateResult = FinancialMetricsExtractionDraftServiceResult.Success(draft)
        };
        var controller = CreateController(dbContext, draftService: draftService);

        var result = await controller.UpdateFinancialMetricsReview(
            session.Id,
            draftId,
            request,
            CancellationToken.None
        );

        result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().Be(draft);
        draftService.LastDraftId.Should().Be(draftId);
        draftService.LastSessionId.Should().Be(session.Id);
        draftService.LastUserId.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        draftService.LastUpdateRequest.Should().BeSameAs(request);
    }

    [Fact]
    public async Task ConfirmFinancialMetricsReview_Should_return_ok_with_service_result()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        session.SetContext("""{"structuredFinancialMetrics":{"documentId":"existing","metrics":[]}}""");
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var draftId = Guid.Parse("00000000-0000-0000-0000-000000000099");
        var identity = FinancialMetricsExtractionDraftIdentityDto.FromDraft(
            CreateDraft(session.Id, draftId, status: "confirmed")
        );
        var draftService = new FakeFinancialMetricsExtractionDraftService
        {
            ConfirmResult = FinancialMetricsExtractionDraftServiceResult.Success(identity)
        };
        var controller = CreateController(dbContext, draftService: draftService);
        var confirmRequest = new ConfirmFinancialMetricsExtractionDraftRequest(
            ApiReportSummaryInput);

        var result = await controller.ConfirmFinancialMetricsReview(
            session.Id,
            draftId,
            confirmRequest,
            CancellationToken.None
        );

        result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().Be(identity);
        draftService.LastDraftId.Should().Be(draftId);
        draftService.LastSessionId.Should().Be(session.Id);
        draftService.LastUserId.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        draftService.LastConfirmRequest.Should().BeSameAs(confirmRequest);
    }

    [Fact]
    public async Task DiscardFinancialMetricsReview_Should_leave_existing_context_unchanged()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        session.SetContext("""{"structuredFinancialMetrics":{"documentId":"existing","metrics":[]}}""");
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var originalContext = session.ContextJson;
        var draftId = Guid.Parse("00000000-0000-0000-0000-000000000099");
        var identity = FinancialMetricsExtractionDraftIdentityDto.FromDraft(
            CreateDraft(session.Id, draftId, status: "discarded")
        );
        var draftService = new FakeFinancialMetricsExtractionDraftService
        {
            DiscardResult = FinancialMetricsExtractionDraftServiceResult.Success(identity)
        };
        var controller = CreateController(dbContext, draftService: draftService);

        var result = await controller.DiscardFinancialMetricsReview(
            session.Id,
            draftId,
            CancellationToken.None
        );

        result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().Be(identity);
        session.ContextJson.Should().Be(originalContext);
    }

    [Fact]
    public async Task Terminal_review_actions_Should_return_ok_when_service_reports_success()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var draftId = Guid.Parse("00000000-0000-0000-0000-000000000099");
        var confirmed = FinancialMetricsExtractionDraftIdentityDto.FromDraft(
            CreateDraft(session.Id, draftId, status: "confirmed")
        );
        var discarded = FinancialMetricsExtractionDraftIdentityDto.FromDraft(
            CreateDraft(session.Id, draftId, status: "discarded")
        );
        var draftService = new FakeFinancialMetricsExtractionDraftService
        {
            ConfirmResult = FinancialMetricsExtractionDraftServiceResult.Success(confirmed),
            DiscardResult = FinancialMetricsExtractionDraftServiceResult.Success(discarded)
        };
        var controller = CreateController(dbContext, draftService: draftService);

        var confirm = await controller.ConfirmFinancialMetricsReview(
            session.Id,
            draftId,
            new ConfirmFinancialMetricsExtractionDraftRequest(ApiReportSummaryInput),
            CancellationToken.None);
        var discard = await controller.DiscardFinancialMetricsReview(session.Id, draftId, CancellationToken.None);

        confirm.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().Be(confirmed);
        discard.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().Be(discarded);
    }

    [Fact]
    public async Task ConfirmFinancialMetricsReview_Should_return_conflict_with_validation_issues_when_confirmation_is_invalid()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var validationIssue = new FinancialMetricsValidationIssue(
            "METRIC_VALUE_REQUIRED",
            "Metric value is required.",
            MetricName: "revenue",
            Period: "2024A",
            Severity: "error"
        );
        var draftService = new FakeFinancialMetricsExtractionDraftService
        {
            ConfirmResult = FinancialMetricsExtractionDraftServiceResult.Invalid(
                "Draft cannot be confirmed.",
                [validationIssue]
            )
        };
        var controller = CreateController(dbContext, draftService: draftService);

        var result = await controller.ConfirmFinancialMetricsReview(
            session.Id,
            Guid.Parse("00000000-0000-0000-0000-000000000099"),
            new ConfirmFinancialMetricsExtractionDraftRequest(ApiReportSummaryInput),
            CancellationToken.None
        );

        var response = result.Should().BeOfType<ConflictObjectResult>()
            .Which.Value.Should().BeOfType<FinancialMetricsExtractionDraftErrorResponse>()
            .Subject;
        response.ValidationIssues.Should().ContainSingle(issue => issue.Code == "METRIC_VALUE_REQUIRED");
    }

    [Fact]
    public async Task GetStartPreflight_Should_return_blocked_result_when_required_metrics_are_missing()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        TestFinancialReport.SetPersistedContext(session);
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
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        TestFinancialReport.SetPersistedContext(session);
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
        var activityEvent = publisher.PublishedEvents.Should().ContainSingle().Subject;
        activityEvent.Type.Should().Be("analysis_start_blocked");
        activityEvent.Agent.Should().Be("Orchestrator");
        activityEvent.Message.Should().Be(
            "Inicio de análisis bloqueado: se requieren métricas financieras estructuradas pero no están presentes.");
    }

    [Theory]
    [InlineData("{\"financialReport\":{\"reportName\":\"report.pdf\",\"totalAmount\":1,\"transactionCount\":1}}", FinancialReportSummaryValidator.SubmittedAtRequiredCode, FinancialReportSummaryValidator.SubmittedAtRequiredMessage, null)]
    [InlineData("{\"financialReport\":{\"reportName\":\"report.pdf\",\"totalAmount\":1,\"transactionCount\":1,\"submittedAt\":\"private-raw-date\"}}", FinancialReportSummaryValidator.SubmittedAtInvalidCode, FinancialReportSummaryValidator.SubmittedAtInvalidMessage, "private-raw-date")]
    public async Task StartSession_InvalidSubmittedAt_ShouldReturnSpecificSafeConflict(
        string contextJson,
        string expectedCode,
        string expectedMessage,
        string? rawTimestamp)
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(
            Guid.Parse("11111111-1111-1111-1111-111111111111"));
        session.SetContext(contextJson);
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var publisher = new FakeActivityEventPublisher();
        var planner = new FakePlannerAgent();
        var controller = CreateController(
            dbContext,
            publisher: publisher,
            orchestrator: CreateOrchestrator(dbContext, publisher, planner));

        var result = await controller.StartSession(
            session.Id,
            CancellationToken.None);

        var preflight = result.Should().BeOfType<ConflictObjectResult>()
            .Which.Value.Should().BeOfType<AnalysisSessionStartPreflightResult>()
            .Subject;
        preflight.CanStart.Should().BeFalse();
        preflight.Errors.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Code = expectedCode,
            Message = expectedMessage,
            Severity = "Error"
        });
        session.Status.Should().Be(AnalysisSessionStatus.Pending);
        planner.RunCalls.Should().Be(0);
        var activityEvent = publisher.PublishedEvents.Should().ContainSingle().Subject;
        activityEvent.Type.Should().Be("analysis_start_blocked");
        activityEvent.Agent.Should().Be("Orchestrator");
        activityEvent.Message.Should().Be(expectedMessage);
        activityEvent.Message.Should().NotContain("submittedAt");
        if (rawTimestamp is not null)
        {
            activityEvent.Message.Should().NotContain(rawTimestamp);
        }
    }

    [Fact]
    public async Task StartSession_Should_start_when_required_metrics_are_attached()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
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
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
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
        var session = AnalysisSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        TestFinancialReport.SetPersistedContext(session);
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
        IStructuredFinancialMetricsPdfIngestionService? pdfIngestionService = null,
        IFinancialMetricsExtractionDraftService? draftService = null,
        IStructuredFinancialMetricsCsvParser? csvParser = null,
        IStructuredFinancialMetricsSessionService? financialMetricsSessionService = null)
    {
        publisher ??= new FakeActivityEventPublisher();

        var controller = new AnalysisSessionsController(
            dbContext,
            orchestrator: orchestrator!,
            new AnalysisSessionStartPreflightValidator(
                Options.Create(dataAgentOptions ?? new DataAgentOptions()),
                new FinancialReportContextResolver()
            ),
            publisher,
            financialMetricsSessionService ??
                StructuredFinancialMetricsSessionServiceTests.CreateService(dbContext, publisher),
            csvParser ?? new StructuredFinancialMetricsCsvParser(),
            pdfIngestionService ?? new FakeStructuredFinancialMetricsPdfIngestionService(
                new StructuredFinancialMetricsPdfIngestionResult(
                    FinancialMetricsFileOutcome.Failed,
                    SaveResult: null,
                    ReviewDraft: null,
                    Errors:
                    [
                        new FinancialMetricsValidationIssue(
                            "PDF_NOT_CONFIGURED",
                            "PDF ingestion was not configured.",
                            MetricName: null,
                            Period: null,
                            Severity: "error"
                        )
                    ],
                    Warnings: []
                )
            ),
            draftService ?? new FakeFinancialMetricsExtractionDraftService(),
            Options.Create(fileUploadOptions ?? new StructuredFinancialMetricsFileUploadOptions())
        );

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "11111111-1111-1111-1111-111111111111"),
            new(ClaimTypes.Name, "fixture-user@example.test")
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
            planner,
            new FinancialReportContextResolver()
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
            Csv: csv,
            ReportSummary: ApiReportSummaryInput
        );
    }

    private static StructuredFinancialMetricsFileUploadRequest CreateFileUploadRequest(
        string fileName,
        string content,
        string? documentId = null,
        string? company = null,
        string? currency = null,
        string? unit = null,
        string? submittedAt = null)
    {
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        return CreateFileUploadRequest(
            fileName, stream, documentId, company, currency, unit, submittedAt);
    }

    private static StructuredFinancialMetricsFileUploadRequest CreateFileUploadRequest(
        string fileName,
        byte[] content,
        string? documentId = null,
        string? company = null,
        string? currency = null,
        string? unit = null,
        string? submittedAt = null)
    {
        var stream = new MemoryStream(content);
        return CreateFileUploadRequest(
            fileName, stream, documentId, company, currency, unit, submittedAt);
    }

    private static StructuredFinancialMetricsFileUploadRequest CreateFileUploadRequest(
        string fileName,
        MemoryStream stream,
        string? documentId,
        string? company,
        string? currency,
        string? unit,
        string? submittedAt)
    {
        var file = new FormFile(stream, 0, stream.Length, "file", fileName);

        return new StructuredFinancialMetricsFileUploadRequest
        {
            File = file,
            DocumentId = documentId,
            Company = company,
            Currency = currency,
            Unit = unit,
            ReportName = ApiReportSummaryInput.ReportName,
            TotalAmount = ApiReportSummaryInput.TotalAmount,
            TransactionCount = ApiReportSummaryInput.TransactionCount,
            SubmittedAt = submittedAt ??
                ApiReportSummaryInput.SubmittedAt!.Value.ToString("O")
        };
    }

    private static string ComputeSha256(
        string content)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ComputeSha256(
        byte[] content)
    {
        var hash = SHA256.HashData(content);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static FinancialMetricsSessionSaveResult CreateValidSaveResult(
        Guid sessionId)
    {
        var input = new StructuredFinancialMetricsInput(
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
            ],
            ReportSummary: ApiReportSummaryInput
        );
        var context = new StructuredFinancialMetricsContext(
            DocumentId: input.DocumentId,
            Company: input.Company,
            Currency: input.Currency,
            Unit: input.Unit,
            Metrics:
            [
                new FinancialMetric(
                    Name: "revenue",
                    Period: "2024A",
                    Value: 1647768m,
                    Unit: "USD_thousand",
                    Statement: "income_statement",
                    Source: "pdf_extraction",
                    Currency: "USD",
                    SourcePage: 18,
                    Confidence: 0.9m
                )
            ],
            ValidationWarnings: [],
            UploadedAt: DateTimeOffset.UtcNow,
            Provenance: new StructuredFinancialMetricsProvenance(
                IngestionMethod: "pdf_file",
                OriginalFileName: "report.pdf",
                FileSizeBytes: 17,
                ContentHash: "hash",
                MetricCount: 1,
                WarningCount: 0
            )
        );

        return new FinancialMetricsSessionSaveResult(
            sessionId,
            IsValid: true,
            context,
            Errors: [],
            Warnings: [],
            ReportSummary: ApiReportSummary
        );
    }

    private static FinancialMetricsExtractionDraftDto CreateDraft(
        Guid sessionId,
        Guid? draftId = null,
        string status = "pending")
    {
        var now = DateTimeOffset.UtcNow;

        return new FinancialMetricsExtractionDraftDto(
            Id: draftId ?? Guid.Parse("00000000-0000-0000-0000-000000000050"),
            SessionId: sessionId,
            Status: status,
            OriginalFileName: "report.pdf",
            FileSizeBytes: 123,
            ContentHash: "hash",
            Payload: CreateDraftPayload(),
            CreatedAt: now,
            UpdatedAt: now,
            CompletedAt: status == "pending" ? null : now
        );
    }

    private static FinancialMetricsExtractionDraftPayload CreateDraftPayload()
    {
        return new FinancialMetricsExtractionDraftPayload(
            SchemaVersion: FinancialMetricsExtractionDraftPayload.CurrentSchemaVersion,
            ProposedInput: StructuredFinancialMetricsSessionServiceTests.CreateInput(),
            Candidates:
            [
                new FinancialMetricCandidate(
                    Id: Guid.Parse("00000000-0000-0000-0000-000000000060"),
                    Name: "Revenue",
                    Period: "2024A",
                    Value: 1647768m,
                    Currency: "USD",
                    Unit: "USD_thousand",
                    SourceKind: FinancialMetricCandidateSourceKinds.Reported,
                    Confidence: 0.9m,
                    SourcePage: 18,
                    Evidence: "Revenue 1,647,768",
                    ExtractionStrategy: "native_text",
                    ReviewState: FinancialMetricCandidateReviewStates.Accepted,
                    InferenceExplanation: null
                )
            ],
            Conflicts: [],
            MissingFields: [],
            FallbackReasons: [],
            ValidationIssues: [],
            Diagnostics: new FinancialMetricsExtractionDiagnostics()
        );
    }

    private static UpdateFinancialMetricsExtractionDraftRequest CreateDraftUpdateRequest(
        Guid sessionId)
    {
        var draft = CreateDraft(sessionId);
        var candidate = draft.Payload.Candidates[0];

        return new UpdateFinancialMetricsExtractionDraftRequest(
            Candidates:
            [
                new FinancialMetricCandidateReviewUpdate(
                    candidate.Id,
                    FinancialMetricCandidateReviewDecisions.Accepted,
                    Value: candidate.Value,
                    Currency: candidate.Currency,
                    Unit: candidate.Unit,
                    MetadataValue: null
                )
            ],
            ProposedInput: draft.Payload.ProposedInput
        );
    }

    private sealed class FakePlannerAgent : IPlannerAgent
    {
        public int RunCalls { get; private set; }
        public bool? LastCancellationTokenCanBeCanceled { get; private set; }

        public Task<PlannerAgentResult> RunAsync(
            FinancialReportContext report,
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

    private sealed class FakeStructuredFinancialMetricsPdfIngestionService(
        StructuredFinancialMetricsPdfIngestionResult? result = null,
        Exception? exception = null) : IStructuredFinancialMetricsPdfIngestionService
    {
        public StructuredFinancialMetricsPdfIngestionRequest? LastRequest { get; private set; }

        public Task<StructuredFinancialMetricsPdfIngestionResult> IngestAsync(
            StructuredFinancialMetricsPdfIngestionRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;

            if (exception is not null)
            {
                throw exception;
            }

            return Task.FromResult(result ?? new StructuredFinancialMetricsPdfIngestionResult(
                FinancialMetricsFileOutcome.Failed,
                SaveResult: null,
                ReviewDraft: null,
                Errors: [],
                Warnings: []
            ));
        }
    }

    private sealed class CapturingStructuredFinancialMetricsCsvParser
        : IStructuredFinancialMetricsCsvParser
    {
        private readonly StructuredFinancialMetricsCsvParser _inner = new();

        public StructuredFinancialMetricsCsvInput? LastInput { get; private set; }

        public StructuredFinancialMetricsCsvParseResult Parse(
            StructuredFinancialMetricsCsvInput input)
        {
            LastInput = input;
            return _inner.Parse(input);
        }
    }

    private sealed class CombinedSnapshotOnlySessionService(
        IStructuredFinancialMetricsSessionService inner)
        : IStructuredFinancialMetricsSessionService
    {
        public int CombinedGetCalls { get; private set; }

        public Task<FinancialMetricsSessionSaveResult?> StageAsync(
            SaveStructuredFinancialMetricsRequest request,
            CancellationToken cancellationToken) =>
            inner.StageAsync(request, cancellationToken);

        public Task<FinancialMetricsSessionSaveResult?> SaveAsync(
            Guid sessionId,
            StructuredFinancialMetricsInput input,
            CancellationToken cancellationToken) =>
            inner.SaveAsync(sessionId, input, cancellationToken);

        public Task<FinancialMetricsSessionSaveResult?> SaveAsync(
            SaveStructuredFinancialMetricsRequest request,
            CancellationToken cancellationToken) =>
            inner.SaveAsync(request, cancellationToken);

        public Task<StructuredFinancialMetricsContext?> GetAsync(
            Guid sessionId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Split metrics read must not be used.");

        public Task<FinancialReportSummary?> GetReportSummaryAsync(
            Guid sessionId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Split report read must not be used.");

        public Task<StructuredFinancialMetricsSessionContext> GetSessionContextAsync(
            Guid sessionId,
            CancellationToken cancellationToken)
        {
            CombinedGetCalls++;
            return inner.GetSessionContextAsync(sessionId, cancellationToken);
        }
    }

    private sealed class FakeFinancialMetricsExtractionDraftService
        : IFinancialMetricsExtractionDraftService
    {
        public FinancialMetricsExtractionDraftServiceResult GetPendingResult { get; init; } =
            FinancialMetricsExtractionDraftServiceResult.NotFound("Draft not found.");

        public FinancialMetricsExtractionDraftServiceResult UpdateResult { get; init; } =
            FinancialMetricsExtractionDraftServiceResult.NotFound("Draft not found.");

        public FinancialMetricsExtractionDraftServiceResult ConfirmResult { get; init; } =
            FinancialMetricsExtractionDraftServiceResult.NotFound("Draft not found.");

        public FinancialMetricsExtractionDraftServiceResult DiscardResult { get; init; } =
            FinancialMetricsExtractionDraftServiceResult.NotFound("Draft not found.");

        public Guid? LastDraftId { get; private set; }
        public Guid? LastSessionId { get; private set; }
        public Guid? LastUserId { get; private set; }
        public UpdateFinancialMetricsExtractionDraftRequest? LastUpdateRequest { get; private set; }
        public ConfirmFinancialMetricsExtractionDraftRequest? LastConfirmRequest { get; private set; }

        public Task<FinancialMetricsExtractionDraftServiceResult> CreateOrReplaceAsync(
            Guid sessionId,
            Guid userId,
            CreateFinancialMetricsExtractionDraftRequest request,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<FinancialMetricsExtractionDraftServiceResult> GetPendingAsync(
            Guid sessionId,
            Guid userId,
            CancellationToken cancellationToken)
        {
            LastSessionId = sessionId;
            LastUserId = userId;

            return Task.FromResult(GetPendingResult);
        }

        public Task<FinancialMetricsExtractionDraftServiceResult> UpdateAsync(
            Guid draftId,
            Guid sessionId,
            Guid userId,
            UpdateFinancialMetricsExtractionDraftRequest request,
            CancellationToken cancellationToken)
        {
            LastDraftId = draftId;
            LastSessionId = sessionId;
            LastUserId = userId;
            LastUpdateRequest = request;

            return Task.FromResult(UpdateResult);
        }

        public Task<FinancialMetricsExtractionDraftServiceResult> ConfirmAsync(
            Guid draftId,
            Guid sessionId,
            Guid userId,
            ConfirmFinancialMetricsExtractionDraftRequest request,
            CancellationToken cancellationToken)
        {
            LastDraftId = draftId;
            LastSessionId = sessionId;
            LastUserId = userId;
            LastConfirmRequest = request;

            return Task.FromResult(ConfirmResult);
        }

        public Task<FinancialMetricsExtractionDraftServiceResult> DiscardAsync(
            Guid draftId,
            Guid sessionId,
            Guid userId,
            CancellationToken cancellationToken)
        {
            LastDraftId = draftId;
            LastSessionId = sessionId;
            LastUserId = userId;

            return Task.FromResult(DiscardResult);
        }
    }
}
