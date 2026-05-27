using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Orchestration.Domain.AnalysisSessions;
using Orchestration.Application.Activity;
using Orchestration.Application.AnalysisSessions;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Persistence;

namespace Orchestration.Api.Controllers;

[ApiController]
[Route("api/analysis-sessions")]
public class AnalysisSessionsController(
    IOrchestrationDbContext dbContext,
    AnalysisOrchestratorService orchestrator,
    IAnalysisSessionStartPreflightValidator startPreflightValidator,
    IActivityEventPublisher activityPublisher,
    IStructuredFinancialMetricsSessionService financialMetricsSessionService,
    IStructuredFinancialMetricsCsvParser financialMetricsCsvParser,
    IOptions<StructuredFinancialMetricsFileUploadOptions> fileUploadOptions) : ControllerBase
{
    private readonly IOrchestrationDbContext _dbContext = dbContext;
    private readonly AnalysisOrchestratorService _orchestrator = orchestrator;
    private readonly IAnalysisSessionStartPreflightValidator _startPreflightValidator = startPreflightValidator;
    private readonly IActivityEventPublisher _activityPublisher = activityPublisher;
    private readonly IStructuredFinancialMetricsSessionService _financialMetricsSessionService = financialMetricsSessionService;
    private readonly IStructuredFinancialMetricsCsvParser _financialMetricsCsvParser = financialMetricsCsvParser;
    private readonly StructuredFinancialMetricsFileUploadOptions _fileUploadOptions = fileUploadOptions.Value;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

    [HttpGet]
    public async Task<IActionResult> GetSessions(CancellationToken cancellationToken)
    {
        var sessions = await _dbContext.AnalysisSessions
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
        var session = AnalysisSession.Create(Guid.Parse("00000000-0000-0000-0000-000000000001"));

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
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

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
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

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
                    "Analysis start blocked: structured financial metrics are required but missing.",
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
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

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
            .AnyAsync(x => x.Id == id, cancellationToken);

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
            result.Warnings
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
        var fileValidationResult = ValidateUploadedFile(request?.File);

        if (fileValidationResult is not null)
        {
            return fileValidationResult;
        }

        var file = request!.File!;
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
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
    }

    [HttpGet("{id:guid}/financial-metrics")]
    public async Task<IActionResult> GetFinancialMetrics(
        Guid id,
        CancellationToken cancellationToken)
    {
        var sessionExists = await _dbContext.AnalysisSessions
            .AnyAsync(x => x.Id == id, cancellationToken);

        if (!sessionExists)
        {
            return NotFound();
        }

        var context = await _financialMetricsSessionService.GetAsync(
            id,
            cancellationToken
        );

        return Ok(new GetFinancialMetricsResponse(
            SessionId: id,
            Context: context
        ));
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
            return BadRequest(new FileUploadErrorResponse("Invalid JSON file."));
        }

        if (input is null)
        {
            return BadRequest(new FileUploadErrorResponse("Invalid JSON file."));
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
                "DocumentId is required for CSV uploads."
            ));
        }

        var result = await SaveCsvInputCoreAsync(
            id,
            new StructuredFinancialMetricsCsvInput(
                DocumentId: request.DocumentId,
                Company: request.Company,
                Currency: request.Currency,
                Unit: request.Unit,
                Csv: content
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
            .AnyAsync(x => x.Id == id, cancellationToken);

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
                    invalidResult.Warnings
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
                saveResult.Warnings
            )),
            saveResult
        );
    }

    private IActionResult? ValidateUploadedFile(
        IFormFile? file)
    {
        if (file is null)
        {
            return BadRequest(new FileUploadErrorResponse("Missing file."));
        }

        if (file.Length <= 0)
        {
            return BadRequest(new FileUploadErrorResponse("Empty file."));
        }

        if (file.Length > _fileUploadOptions.MaxFileSizeBytes)
        {
            return BadRequest(new FileUploadErrorResponse(
                "File exceeds maximum allowed size."
            ));
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

        if (!_fileUploadOptions.AllowedExtensions.Contains(
            extension,
            StringComparer.OrdinalIgnoreCase
        ))
        {
            return BadRequest(new FileUploadErrorResponse(
                "Unsupported file extension."
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
            FileSizeBytes: file.Length
        );
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

    private static string ComputeSha256(
        string content)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));

        return Convert.ToHexString(hash).ToLowerInvariant();
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
}

public sealed record SaveFinancialMetricsFileResponse(
    Guid SessionId,
    bool IsValid,
    StructuredFinancialMetricsContext? Context,
    IReadOnlyList<FinancialMetricsValidationIssue> Errors,
    IReadOnlyList<FinancialMetricsValidationIssue> Warnings,
    string FileName,
    string FileType,
    long FileSizeBytes
);

public sealed record FileUploadErrorResponse(
    string Error
);

public sealed record SaveFinancialMetricsResponse(
    Guid SessionId,
    bool IsValid,
    StructuredFinancialMetricsContext? Context,
    IReadOnlyList<FinancialMetricsValidationIssue> Errors,
    IReadOnlyList<FinancialMetricsValidationIssue> Warnings
);

public sealed record GetFinancialMetricsResponse(
    Guid SessionId,
    StructuredFinancialMetricsContext? Context
);
