using Microsoft.AspNetCore.Mvc;
using Orchestration.Application.Agents.Planner.ToolCalling;
using Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;

namespace Orchestration.Api.Controllers;

[ApiController]
[Route("api/diagnostics")]
public class DiagnosticsController : ControllerBase
{
    private readonly ICnvRegulationMcpClient _client;
    private readonly IToolCallingDiagnosticService _toolCallingDiagnostics;
    private readonly IHostEnvironment _environment;

    public DiagnosticsController(
        ICnvRegulationMcpClient client,
        IToolCallingDiagnosticService toolCallingDiagnostics,
        IHostEnvironment environment)
    {
        _client = client;
        _toolCallingDiagnostics = toolCallingDiagnostics;
        _environment = environment;
    }

    [HttpPost("cnv-regulation/search")]
    public async Task<IActionResult> SearchCnvRegulation(
        [FromBody] CnvRegulationSearchRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _client.SearchAsync(
            request,
            cancellationToken
        );

        return Ok(response);
    }

    [HttpPost("tool-calling/execute")]
    public async Task<IActionResult> ExecuteToolCalling(
        [FromBody] ToolCallingDiagnosticRequest request,
        CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        var response = await _toolCallingDiagnostics.ExecuteAsync(
            request,
            cancellationToken
        );

        return Ok(response);
    }
}
