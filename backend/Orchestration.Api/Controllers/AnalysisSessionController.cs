using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Orchestration.Domain.AnalysisSessions;
using Orchestration.Application.Activity;
using Orchestration.Application.AnalysisSessions;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Data.FinancialAnalysis.Extraction;
using Orchestration.Application.Agents.Shared;
using Orchestration.Application.Persistence;
using Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.Pdf;
using UglyToad.PdfPig.Core;

namespace Orchestration.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/analysis-sessions")]
public class AnalysisSessionsController(
    IOrchestrationDbContext dbContext,
    AnalysisOrchestratorService orchestrator,
    IAnalysisSessionStartPreflightValidator startPreflightValidator,
    IActivityEventPublisher activityPublisher,
    IStructuredFinancialMetricsSessionService financialMetricsSessionService,
    IStructuredFinancialMetricsCsvParser financialMetricsCsvParser,
    IStructuredFinancialMetricsPdfIngestionService financialMetricsPdfIngestionService,
    IFinancialMetricsExtractionDraftService financialMetricsExtractionDraftService,
    IOptions<StructuredFinancialMetricsFileUploadOptions> fileUploadOptions) : ControllerBase
{
    private readonly IOrchestrationDbContext _dbContext = dbContext;
    private readonly AnalysisOrchestratorService _orchestrator = orchestrator;
    private readonly IAnalysisSessionStartPreflightValidator _startPreflightValidator = startPreflightValidator;
    private readonly IActivityEventPublisher _activityPublisher = activityPublisher;
    private readonly IStructuredFinancialMetricsSessionService _financialMetricsSessionService = financialMetricsSessionService;
    private readonly IStructuredFinancialMetricsCsvParser _financialMetricsCsvParser = financialMetricsCsvParser;
    private readonly IStructuredFinancialMetricsPdfIngestionService _financialMetricsPdfIngestionService = financialMetricsPdfIngestionService;
    private readonly IFinancialMetricsExtractionDraftService _financialMetricsExtractionDraftService = financialMetricsExtractionDraftService;
    private readonly StructuredFinancialMetricsFileUploadOptions _fileUploadOptions = fileUploadOptions.Value;

    private Guid CurrentUserId => Guid.Parse(
        User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? throw new InvalidOperationException("User ID claim is missing.")
    );

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

    [HttpGet]
    public async Task<IActionResult> GetSessions(CancellationToken cancellationToken)
    {
        var sessions = await _dbContext.AnalysisSessions
            .Where(x => x.UserId == CurrentUserId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id,
                Status = x.Status.ToString(),
                x.CurrentAgent,
                x.CreatedAt,
                x.UpdatedAt,
                x.CompletedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(sessions);
    }

    [HttpPost]
    public async Task<IActionResult> CreateSession(CancellationToken cancellationToken)
    {
        var session = AnalysisSession.Create(CurrentUserId);

        _dbContext.AnalysisSessions.Add(session);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(
            nameof(GetSession),
            new { id = session.Id },
            new
            {
                session.Id,
                Status = session.Status.ToString(),
                session.ContextJson,
                session.CurrentAgent,
                session.CreatedAt,
                session.UpdatedAt
            }
        );
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetSession(
        Guid id,
        CancellationToken cancellationToken)
    {
        var session = await _dbContext.AnalysisSessions
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == CurrentUserId, cancellationToken);

        if (session is null)
        {
            return NotFound();
        }

        return Ok(new
        {
            session.Id,
            Status = session.Status.ToString(),
            session.ContextJson,
            session.CurrentAgent,
            session.FailureReason,
            session.CreatedAt,
            session.UpdatedAt,
            session.CompletedAt
        });
    }

    [HttpPost("{id:guid}/start")]
    public async Task<IActionResult> StartSession(
        Guid id,
        CancellationToken cancellationToken)
    {
        var session = await _dbContext.AnalysisSessions
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == CurrentUserId, cancellationToken);

        if (session is null)
        {
            return NotFound();
        }

        var preflight = await _startPreflightValidator.ValidateAsync(
            session,
            cancellationToken
        );

        if (!preflight.CanStart)
        {
            await _activityPublisher.PublishAsync(
                new ActivityEvent(
                    id,
                    "analysis_start_blocked",
                    "Orchestrator",
                    "Inicio de análisis bloqueado: se requieren métricas financieras estructuradas pero no están presentes.",
                    DateTimeOffset.UtcNow
                ),
                cancellationToken
            );

            return Conflict(preflight);
        }

        try
        {
            // Once preflight passes, the workflow must finish even if the browser disconnects.
            var result = await _orchestrator.StartAnalysisAsync(id, CancellationToken.None);

            if (result is null)
            {
                return NotFound();
            }

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new
            {
                error = ex.Message
            });
        }
    }

    [HttpGet("{id:guid}/start-preflight")]
    public async Task<IActionResult> GetStartPreflight(
        Guid id,
        CancellationToken cancellationToken)
    {
        var session = await _dbContext.AnalysisSessions
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == CurrentUserId, cancellationToken);

        if (session is null)
        {
            return NotFound();
        }

        var preflight = await _startPreflightValidator.ValidateAsync(
            session,
            cancellationToken
        );

        return Ok(preflight);
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> ApproveSession(
        Guid id,
        [FromBody] HumanDecisionDto request,
        CancellationToken cancellationToken)
    {
        var session = await _dbContext.AnalysisSessions
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == CurrentUserId, cancellationToken);

        if (session is null)
        {
            return NotFound();
        }

        try
        {
            var result = await _orchestrator.ApproveAsync(
                id,
                request,
                cancellationToken
            );

            if (result is null)
            {
                return NotFound();
            }

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new
            {
                error = ex.Message
            });
        }
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> RejectSession(
        Guid id,
        [FromBody] HumanDecisionDto request,
        CancellationToken cancellationToken)
    {
        var session = await _dbContext.AnalysisSessions
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == CurrentUserId, cancellationToken);

        if (session is null)
        {
            return NotFound();
        }

        try
        {
            var result = await _orchestrator.RejectAsync(
                id,
                request,
                cancellationToken
            );

            if (result is null)
            {
                return NotFound();
            }

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new
            {
                error = ex.Message
            });
        }
    }

    [HttpGet("{id:guid}/events")]
    public async Task<IActionResult> GetSessionEvents(
        Guid id,
        CancellationToken cancellationToken)
    {
        var sessionExists = await _dbContext.AnalysisSessions
            .AnyAsync(x => x.Id == id && x.UserId == CurrentUserId, cancellationToken);

        if (!sessionExists)
        {
            return NotFound();
        }

        var events = await _dbContext.ActivityEvents
            .Where(x => x.SessionId == id)
            .OrderByDescending(x => x.Timestamp)
            .Select(x => new
            {
                x.SessionId,
                x.Type,
                x.Agent,
                x.Message,
                x.Timestamp
            })
            .ToListAsync(cancellationToken);

        return Ok(events);
    }

    [HttpPost("{id:guid}/financial-metrics")]
    public async Task<IActionResult> SaveFinancialMetrics(
        Guid id,
        [FromBody] StructuredFinancialMetricsInput input,
        CancellationToken cancellationToken)
    {
        if (input is null)
        {
            return BadRequest();
        }

        var sessionExists = await _dbContext.AnalysisSessions
            .AnyAsync(x => x.Id == id && x.UserId == CurrentUserId, cancellationToken);

        if (!sessionExists)
        {
            return NotFound();
        }

        var result = await _financialMetricsSessionService.SaveAsync(
            new SaveStructuredFinancialMetricsRequest(
                SessionId: id,
                Input: input,
                Provenance: new StructuredFinancialMetricsProvenanceInput(
                    IngestionMethod: "json_paste",
                    OriginalFileName: null,
                    FileSizeBytes: null,
                    ContentHash: null
                )
            ),
            cancellationToken
        );

        if (result is null)
        {
            return NotFound();
        }

        return Ok(new SaveFinancialMetricsResponse(
            result.SessionId,
            result.IsValid,
            result.Context,
            result.Errors,
            result.Warnings,
            result.ReportSummary
        ));
    }

    [HttpPost("{id:guid}/financial-metrics/csv")]
    public async Task<IActionResult> SaveFinancialMetricsCsv(
        Guid id,
        [FromBody] StructuredFinancialMetricsCsvInput input,
        CancellationToken cancellationToken)
    {
        if (input is null)
        {
            return BadRequest();
        }

        return await SaveCsvInputAsync(id, input, cancellationToken);
    }

    [HttpPost("{id:guid}/financial-metrics/file")]
    public async Task<IActionResult> SaveFinancialMetricsFile(
        Guid id,
        [FromForm] StructuredFinancialMetricsFileUploadRequest request,
        CancellationToken cancellationToken)
    {
        var sessionExists = await _dbContext.AnalysisSessions
            .AnyAsync(x => x.Id == id && x.UserId == CurrentUserId, cancellationToken);

        if (!sessionExists)
        {
            return NotFound();
        }

        var fileValidationResult = ValidateUploadedFile(request?.File);

        if (fileValidationResult is not null)
        {
            return fileValidationResult;
        }

        var file = request!.File!;
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

        if (extension == ".pdf")
        {
            return await SavePdfFileAsync(id, request, cancellationToken);
        }

        var content = await ReadFileContentAsync(file, cancellationToken);

        if (string.IsNullOrWhiteSpace(content))
        {
            return BadRequest(new FileUploadErrorResponse("El archivo subido está vacío."));
        }

        return extension switch
        {
            ".json" => await SaveJsonFileAsync(id, request, content, cancellationToken),
            ".csv" => await SaveCsvFileAsync(id, request, content, cancellationToken),
            _ => BadRequest(new FileUploadErrorResponse("Extensión de archivo no soportada."))
        };
    }

    [HttpGet("{id:guid}/financial-metrics")]
    public async Task<IActionResult> GetFinancialMetrics(
        Guid id,
        CancellationToken cancellationToken)
    {
        var sessionExists = await _dbContext.AnalysisSessions
            .AnyAsync(x => x.Id == id && x.UserId == CurrentUserId, cancellationToken);

        if (!sessionExists)
        {
            return NotFound();
        }

        var sessionContext = await _financialMetricsSessionService.GetSessionContextAsync(
            id,
            cancellationToken
        );

        return Ok(new GetFinancialMetricsResponse(
            SessionId: id,
            Context: sessionContext.Metrics,
            ReportSummary: sessionContext.ReportSummary
        ));
    }

    [HttpGet("{id:guid}/financial-metrics/review")]
    public async Task<IActionResult> GetFinancialMetricsReview(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _financialMetricsExtractionDraftService.GetPendingAsync(
            id,
            CurrentUserId,
            cancellationToken
        );

        return ToDraftActionResult(result);
    }

    [HttpPut("{id:guid}/financial-metrics/review/{draftId:guid}")]
    public async Task<IActionResult> UpdateFinancialMetricsReview(
        Guid id,
        Guid draftId,
        [FromBody] UpdateFinancialMetricsExtractionDraftRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest();
        }

        var result = await _financialMetricsExtractionDraftService.UpdateAsync(
            draftId,
            id,
            CurrentUserId,
            request,
            cancellationToken
        );

        return ToDraftActionResult(result);
    }

    [HttpPost("{id:guid}/financial-metrics/review/{draftId:guid}/confirm")]
    public async Task<IActionResult> ConfirmFinancialMetricsReview(
        Guid id,
        Guid draftId,
        [FromBody] ConfirmFinancialMetricsExtractionDraftRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest();
        }

        var result = await _financialMetricsExtractionDraftService.ConfirmAsync(
            draftId,
            id,
            CurrentUserId,
            request,
            cancellationToken
        );

        return ToDraftActionResult(result, invalidAsConflict: true);
    }

    [HttpPost("{id:guid}/financial-metrics/review/{draftId:guid}/discard")]
    public async Task<IActionResult> DiscardFinancialMetricsReview(
        Guid id,
        Guid draftId,
        CancellationToken cancellationToken)
    {
        var result = await _financialMetricsExtractionDraftService.DiscardAsync(
            draftId,
            id,
            CurrentUserId,
            cancellationToken
        );

        return ToDraftActionResult(result);
    }

    private async Task<IActionResult> SaveJsonFileAsync(
        Guid id,
        StructuredFinancialMetricsFileUploadRequest request,
        string content,
        CancellationToken cancellationToken)
    {
        StructuredFinancialMetricsInput? input;

        try
        {
            input = JsonSerializer.Deserialize<StructuredFinancialMetricsInput>(
                content,
                JsonOptions
            );
        }
        catch (JsonException)
        {
            return BadRequest(new FileUploadErrorResponse("Archivo JSON no válido."));
        }

        if (input is null)
        {
            return BadRequest(new FileUploadErrorResponse("Archivo JSON no válido."));
        }

        var result = await _financialMetricsSessionService.SaveAsync(
            new SaveStructuredFinancialMetricsRequest(
                SessionId: id,
                Input: ApplyFallbackMetadata(input, request),
                Provenance: CreateFileProvenance(
                    request.File!,
                    "json_file",
                    content
                )
            ),
            cancellationToken
        );

        if (result is null)
        {
            return NotFound();
        }

        return Ok(CreateFileResponse(request.File!, "json", result));
    }

    private async Task<IActionResult> SaveCsvFileAsync(
        Guid id,
        StructuredFinancialMetricsFileUploadRequest request,
        string content,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DocumentId))
        {
            return BadRequest(new FileUploadErrorResponse(
                "Se requiere el identificador de documento (DocumentId) para cargas de CSV."
            ));
        }

        var result = await SaveCsvInputCoreAsync(
            id,
            new StructuredFinancialMetricsCsvInput(
                DocumentId: request.DocumentId,
                Company: request.Company,
                Currency: request.Currency,
                Unit: request.Unit,
                Csv: content,
                ReportSummary: CreateReportSummaryInput(request)
            ),
            cancellationToken,
            CreateFileProvenance(
                request.File!,
                "csv_file",
                content
            )
        );

        return result.SaveResult is null
            ? result.ActionResult
            : Ok(CreateFileResponse(request.File!, "csv", result.SaveResult));
    }

    private async Task<IActionResult> SavePdfFileAsync(
        Guid id,
        StructuredFinancialMetricsFileUploadRequest request,
        CancellationToken cancellationToken)
    {
        StructuredFinancialMetricsPdfIngestionResult ingestion;

        try
        {
            var pdfBytes = await ReadFileBytesAsync(request.File!, cancellationToken);
            ingestion = await _financialMetricsPdfIngestionService.IngestAsync(
                new StructuredFinancialMetricsPdfIngestionRequest(
                    SessionId: id,
                    UserId: CurrentUserId,
                    PdfBytes: pdfBytes,
                    DocumentId: request.DocumentId ?? "",
                    Company: request.Company,
                    Currency: request.Currency,
                    Unit: request.Unit,
                    OriginalFileName: Path.GetFileName(request.File!.FileName),
                    FileSizeBytes: request.File.Length,
                    ContentHash: ComputeSha256(pdfBytes),
                    ReportSummary: CreateReportSummaryInput(request)
                ),
                cancellationToken
            );
        }
        catch (PdfOcrDependencyException)
        {
            return BadRequest(new FileUploadErrorResponse(
                "Las dependencias de OCR para PDF no están configuradas."
            ));
        }
        catch (InvalidDataException)
        {
            return BadRequest(new FileUploadErrorResponse("Archivo PDF no válido."));
        }
        catch (PdfDocumentFormatException)
        {
            return BadRequest(new FileUploadErrorResponse("Archivo PDF no válido."));
        }
        catch (IOException)
        {
            return BadRequest(new FileUploadErrorResponse("Archivo PDF no válido."));
        }

        return Ok(CreatePdfFileResponse(id, request.File!, ingestion));
    }


    private async Task<IActionResult> SaveCsvInputAsync(
        Guid id,
        StructuredFinancialMetricsCsvInput input,
        CancellationToken cancellationToken)
    {
        var result = await SaveCsvInputCoreAsync(
            id,
            input,
            cancellationToken,
            new StructuredFinancialMetricsProvenanceInput(
                IngestionMethod: "csv_paste",
                OriginalFileName: null,
                FileSizeBytes: null,
                ContentHash: null
            )
        );

        return result.ActionResult;
    }

    private async Task<CsvSaveResult> SaveCsvInputCoreAsync(
        Guid id,
        StructuredFinancialMetricsCsvInput input,
        CancellationToken cancellationToken,
        StructuredFinancialMetricsProvenanceInput? provenance = null)
    {
        var sessionExists = await _dbContext.AnalysisSessions
            .AnyAsync(x => x.Id == id && x.UserId == CurrentUserId, cancellationToken);

        if (!sessionExists)
        {
            return new CsvSaveResult(NotFound(), null);
        }

        var parseResult = _financialMetricsCsvParser.Parse(input);

        if (!parseResult.IsValid || parseResult.Input is null)
        {
            var invalidResult = new FinancialMetricsSessionSaveResult(
                SessionId: id,
                IsValid: false,
                Context: null,
                Errors: parseResult.Errors,
                Warnings: parseResult.Warnings
            );

            return new CsvSaveResult(
                Ok(new SaveFinancialMetricsResponse(
                    invalidResult.SessionId,
                    invalidResult.IsValid,
                    invalidResult.Context,
                    invalidResult.Errors,
                    invalidResult.Warnings,
                    invalidResult.ReportSummary
                )),
                invalidResult
            );
        }

        var saveResult = await _financialMetricsSessionService.SaveAsync(
            new SaveStructuredFinancialMetricsRequest(
                SessionId: id,
                Input: parseResult.Input,
                Provenance: provenance
            ),
            cancellationToken
        );

        if (saveResult is null)
        {
            return new CsvSaveResult(NotFound(), null);
        }

        return new CsvSaveResult(
            Ok(new SaveFinancialMetricsResponse(
                saveResult.SessionId,
                saveResult.IsValid,
                saveResult.Context,
                saveResult.Errors,
                saveResult.Warnings,
                saveResult.ReportSummary
            )),
            saveResult
        );
    }

    private IActionResult? ValidateUploadedFile(
        IFormFile? file)
    {
        if (file is null)
        {
            return BadRequest(new FileUploadErrorResponse("Falta el archivo."));
        }

        if (file.Length <= 0)
        {
            return BadRequest(new FileUploadErrorResponse("Archivo vacío."));
        }

        if (file.Length > _fileUploadOptions.MaxFileSizeBytes)
        {
            return BadRequest(new FileUploadErrorResponse(
                "El archivo excede el tamaño máximo permitido."
            ));
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

        if (!_fileUploadOptions.AllowedExtensions.Contains(
            extension,
            StringComparer.OrdinalIgnoreCase
        ))
        {
            return BadRequest(new FileUploadErrorResponse(
                "Extensión de archivo no soportada."
            ));
        }

        return null;
    }

    private static async Task<string> ReadFileContentAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(
            file.OpenReadStream(),
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true
        );

        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static async Task<byte[]> ReadFileBytesAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);

        return buffer.ToArray();
    }

    private static StructuredFinancialMetricsInput ApplyFallbackMetadata(
        StructuredFinancialMetricsInput input,
        StructuredFinancialMetricsFileUploadRequest request)
    {
        return input with
        {
            DocumentId = string.IsNullOrWhiteSpace(input.DocumentId)
                ? request.DocumentId ?? ""
                : input.DocumentId,
            Company = string.IsNullOrWhiteSpace(input.Company)
                ? request.Company
                : input.Company,
            Currency = string.IsNullOrWhiteSpace(input.Currency)
                ? request.Currency
                : input.Currency,
            Unit = string.IsNullOrWhiteSpace(input.Unit)
                ? request.Unit
                : input.Unit
        };
    }

    private static FinancialReportSummaryInput CreateReportSummaryInput(
        StructuredFinancialMetricsFileUploadRequest request)
    {
        return new FinancialReportSummaryInput(
            request.ReportName,
            request.TotalAmount,
            request.TransactionCount,
            request.SubmittedAt);
    }

    private static SaveFinancialMetricsFileResponse CreateFileResponse(
        IFormFile file,
        string fileType,
        FinancialMetricsSessionSaveResult result)
    {
        return new SaveFinancialMetricsFileResponse(
            SessionId: result.SessionId,
            IsValid: result.IsValid,
            Context: result.Context,
            Errors: result.Errors,
            Warnings: result.Warnings,
            FileName: Path.GetFileName(file.FileName),
            FileType: fileType,
            FileSizeBytes: file.Length,
            Outcome: "accepted",
            ReviewDraft: null,
            ReportSummary: result.ReportSummary
        );
    }

    private static SaveFinancialMetricsFileResponse CreatePdfFileResponse(
        Guid sessionId,
        IFormFile file,
        StructuredFinancialMetricsPdfIngestionResult result)
    {
        return result.Outcome switch
        {
            FinancialMetricsFileOutcome.Accepted => new SaveFinancialMetricsFileResponse(
                SessionId: result.SaveResult?.SessionId ?? sessionId,
                IsValid: result.SaveResult?.IsValid ?? false,
                Context: result.SaveResult?.Context,
                Errors: result.SaveResult?.Errors ?? result.Errors,
                Warnings: result.SaveResult?.Warnings ?? result.Warnings,
                FileName: Path.GetFileName(file.FileName),
                FileType: "pdf",
                FileSizeBytes: file.Length,
                Outcome: "accepted",
                ReviewDraft: null,
                ReportSummary: result.SaveResult?.ReportSummary
            ),
            FinancialMetricsFileOutcome.ReviewRequired => new SaveFinancialMetricsFileResponse(
                SessionId: sessionId,
                IsValid: false,
                Context: null,
                Errors: result.Errors,
                Warnings: result.Warnings,
                FileName: Path.GetFileName(file.FileName),
                FileType: "pdf",
                FileSizeBytes: file.Length,
                Outcome: "review_required",
                ReviewDraft: result.ReviewDraft,
                ReportSummary: null
            ),
            _ => new SaveFinancialMetricsFileResponse(
                SessionId: sessionId,
                IsValid: false,
                Context: null,
                Errors: result.Errors,
                Warnings: result.Warnings,
                FileName: Path.GetFileName(file.FileName),
                FileType: "pdf",
                FileSizeBytes: file.Length,
                Outcome: "failed",
                ReviewDraft: null,
                ReportSummary: null
            )
        };
    }

    private static StructuredFinancialMetricsProvenanceInput CreateFileProvenance(
        IFormFile file,
        string ingestionMethod,
        string content)
    {
        return new StructuredFinancialMetricsProvenanceInput(
            IngestionMethod: ingestionMethod,
            OriginalFileName: Path.GetFileName(file.FileName),
            FileSizeBytes: file.Length,
            ContentHash: ComputeSha256(content)
        );
    }

    private static async Task<StructuredFinancialMetricsProvenanceInput> CreateBinaryFileProvenanceAsync(
        IFormFile file,
        string ingestionMethod,
        CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);

        return new StructuredFinancialMetricsProvenanceInput(
            IngestionMethod: ingestionMethod,
            OriginalFileName: Path.GetFileName(file.FileName),
            FileSizeBytes: file.Length,
            ContentHash: Convert.ToHexString(hash).ToLowerInvariant()
        );
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

    private IActionResult ToDraftActionResult(
        FinancialMetricsExtractionDraftServiceResult result,
        bool invalidAsConflict = false)
    {
        if (result.Kind == FinancialMetricsExtractionDraftResultKind.Success)
        {
            return Ok(result.Draft ?? (object?)result.DraftIdentity);
        }

        var error = new FinancialMetricsExtractionDraftErrorResponse(
            result.Errors,
            result.ValidationIssues
        );

        return result.Kind switch
        {
            FinancialMetricsExtractionDraftResultKind.NotFound => NotFound(),
            FinancialMetricsExtractionDraftResultKind.Invalid when invalidAsConflict => Conflict(error),
            FinancialMetricsExtractionDraftResultKind.Invalid => BadRequest(error),
            FinancialMetricsExtractionDraftResultKind.Conflict => Conflict(error),
            _ => BadRequest(error)
        };
    }

    private sealed record CsvSaveResult(
        IActionResult ActionResult,
        FinancialMetricsSessionSaveResult? SaveResult
    );

}

public sealed class StructuredFinancialMetricsFileUploadRequest
{
    public IFormFile? File { get; init; }

    public string? DocumentId { get; init; }

    public string? Company { get; init; }

    public string? Currency { get; init; }

    public string? Unit { get; init; }

    public string? ReportName { get; init; }

    public decimal? TotalAmount { get; init; }

    public int? TransactionCount { get; init; }

    public DateTimeOffset? SubmittedAt { get; init; }
}

public sealed record SaveFinancialMetricsFileResponse(
    Guid SessionId,
    bool IsValid,
    StructuredFinancialMetricsContext? Context,
    IReadOnlyList<FinancialMetricsValidationIssue> Errors,
    IReadOnlyList<FinancialMetricsValidationIssue> Warnings,
    string FileName,
    string FileType,
    long FileSizeBytes,
    string Outcome = "accepted",
    FinancialMetricsExtractionDraftDto? ReviewDraft = null,
    FinancialReportSummary? ReportSummary = null
);

public sealed record FileUploadErrorResponse(
    string Error
);

public sealed record SaveFinancialMetricsResponse(
    Guid SessionId,
    bool IsValid,
    StructuredFinancialMetricsContext? Context,
    IReadOnlyList<FinancialMetricsValidationIssue> Errors,
    IReadOnlyList<FinancialMetricsValidationIssue> Warnings,
    FinancialReportSummary? ReportSummary = null
);

public sealed record GetFinancialMetricsResponse(
    Guid SessionId,
    StructuredFinancialMetricsContext? Context,
    FinancialReportSummary? ReportSummary = null
);

public sealed record FinancialMetricsExtractionDraftErrorResponse(
    IReadOnlyList<string> Errors,
    IReadOnlyList<FinancialMetricsValidationIssue> ValidationIssues
);
