using Microsoft.AspNetCore.Mvc;
using Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;

namespace Orchestration.Api.Controllers;

[ApiController]
[Route("api/diagnostics")]
public class DiagnosticsController : ControllerBase
{
    private readonly ICnvRegulationMcpClient _client;

    public DiagnosticsController(ICnvRegulationMcpClient client)
    {
        _client = client;
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
}