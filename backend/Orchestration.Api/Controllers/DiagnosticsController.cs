using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orchestration.Application.Agents.Planner.ToolCalling;
using Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;

namespace Orchestration.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/diagnostics")]
public class DiagnosticsController(
    ICnvRegulationMcpClient client,
    IToolCallingDiagnosticService toolCallingDiagnostics,
    IHostEnvironment environment) : ControllerBase
{
    private readonly ICnvRegulationMcpClient _client = client;
    private readonly IToolCallingDiagnosticService _toolCallingDiagnostics = toolCallingDiagnostics;
    private readonly IHostEnvironment _environment = environment;

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

    [HttpGet("cnv-regulation/status")]
    public IActionResult GetCnvRegulationStatus()
    {
        return Ok(new
        {
            connected = _client.IsConnected,
            coldStartCount = _client.ColdStartCount,
            resetCount = _client.ResetCount,
            lastError = _client.LastError
        });
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
