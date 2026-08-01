using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Orchestration.Api.Controllers;
using Orchestration.Application.Agents.Planner.ToolCalling;
using Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;

namespace Orchestration.Tests.Api;

public class DiagnosticsControllerTests
{
    [Fact]
    public void DiagnosticsController_Should_require_authorization()
    {
        typeof(DiagnosticsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Should()
            .ContainSingle();
    }

    [Fact]
    public async Task ExecuteToolCalling_Should_return_ok_in_development()
    {
        var service = new FakeToolCallingDiagnosticService();
        var controller = new DiagnosticsController(
            new FakeCnvRegulationMcpClient(),
            service,
            new FakeHostEnvironment(Environments.Development)
        );

        var result = await controller.ExecuteToolCalling(
            new ToolCallingDiagnosticRequest(),
            CancellationToken.None
        );

        result.Should().BeOfType<OkObjectResult>();
        service.WasCalled.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteToolCalling_Should_return_not_found_outside_development()
    {
        var service = new FakeToolCallingDiagnosticService();
        var controller = new DiagnosticsController(
            new FakeCnvRegulationMcpClient(),
            service,
            new FakeHostEnvironment(Environments.Production)
        );

        var result = await controller.ExecuteToolCalling(
            new ToolCallingDiagnosticRequest(),
            CancellationToken.None
        );

        result.Should().BeOfType<NotFoundResult>();
        service.WasCalled.Should().BeFalse();
    }

    private sealed class FakeToolCallingDiagnosticService : IToolCallingDiagnosticService
    {
        public bool WasCalled { get; private set; }

        public Task<ToolPlanAuditResult> ExecuteAsync(
            ToolCallingDiagnosticRequest request,
            CancellationToken cancellationToken)
        {
            WasCalled = true;

            return Task.FromResult(ToolPlanAuditResult.Empty);
        }
    }

    private sealed class FakeCnvRegulationMcpClient : ICnvRegulationMcpClient
    {
        public bool IsConnected => true;
        public int ColdStartCount => 0;
        public int ResetCount => 0;
        public string? LastError => null;

        public Task<CnvRegulationDocumentResponse> GetDocumentAsync(
            CnvRegulationDocumentRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CnvRegulationDocumentResponse(false, null, [], []));

        public Task<CnvRegulationArticleResponse> GetArticleAsync(
            CnvRegulationArticleRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CnvRegulationArticleResponse(false, null, null, 0, []));

        public Task<CnvRegulationSearchResponse> SearchAsync(
            CnvRegulationSearchRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                new CnvRegulationSearchResponse(
                    request.Query,
                    [],
                    []
                )
            );
        }
    }

    private sealed class FakeHostEnvironment : IWebHostEnvironment
    {
        public FakeHostEnvironment(
            string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }

        public string ApplicationName { get; set; } = "Orchestration.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();

        public string WebRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider WebRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
