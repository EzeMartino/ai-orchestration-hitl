using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orchestration.Domain.AnalysisSessions;
using Orchestration.Infrastructure.Persistence;
using Orchestration.Application.AnalysisSessions;

namespace Orchestration.Api.Controllers;

[ApiController]
[Route("api/analysis-sessions")]
public class AnalysisSessionsController : ControllerBase
{
    private readonly OrchestrationDbContext _dbContext;
    private readonly AnalysisOrchestratorService _orchestrator;
    public AnalysisSessionsController(
        OrchestrationDbContext dbContext,
        AnalysisOrchestratorService orchestrator)
    {
        _dbContext = dbContext;
        _orchestrator = orchestrator;
    }



    [HttpPost]
    public async Task<IActionResult> CreateSession(CancellationToken cancellationToken)
    {
        var session = AnalysisSession.Create();

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
        try
        {
            var result = await _orchestrator.StartAnalysisAsync(id, cancellationToken);

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
}
