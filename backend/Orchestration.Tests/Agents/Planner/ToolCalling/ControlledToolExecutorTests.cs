using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Planner.ToolCalling;
using Orchestration.Application.Agents.Shared;
using Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;
using Orchestration.Infrastructure.Agents.Planner.ToolCalling;

namespace Orchestration.Tests.Agents.Planner.ToolCalling;

public class ControlledToolExecutorTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ExecuteAsync_Should_execute_data_analyze_transactions_with_valid_args()
    {
        var dataAgent = new FakeDataAgent();
        var executor = CreateExecutor(dataAgent);
        var sessionId = Guid.NewGuid();
        var submittedAt = DateTimeOffset.UtcNow;

        var results = await executor.ExecuteAsync(
            [
                CreateCall(
                    "data.analyze_transactions",
                    new Dictionary<string, string>
                    {
                        ["sessionId"] = sessionId.ToString(),
                        ["reportName"] = "financial-report",
                        ["totalAmount"] = "125000.50",
                        ["transactionCount"] = "42",
                        ["submittedAt"] = submittedAt.ToString("O")
                    }
                )
            ],
            CancellationToken.None
        );

        var result = results.Should().ContainSingle().Subject;

        result.ToolName.Should().Be("data.analyze_transactions");
        result.Succeeded.Should().BeTrue();
        result.Summary.Should().Be("Data analysis completed.");
        result.Engine.Should().Be("Fake DataAgent");
        result.Error.Should().BeNull();
        result.OutputJson.Should().NotBe("{}");

        dataAgent.WasCalled.Should().BeTrue();
        dataAgent.ReceivedReport.Should().BeEquivalentTo(
            new FinancialReportContext(
                sessionId,
                "financial-report",
                125000.50m,
                42,
                submittedAt
            )
        );

        var output = JsonSerializer.Deserialize<DataAgentResult>(
            result.OutputJson,
            JsonOptions
        );

        output.Should().NotBeNull();
        output!.Summary.Should().Be("Data analysis completed.");
    }

    [Fact]
    public async Task ExecuteAsync_Should_return_failure_when_data_required_argument_is_missing()
    {
        var dataAgent = new FakeDataAgent();
        var executor = CreateExecutor(dataAgent);

        var results = await executor.ExecuteAsync(
            [
                CreateCall(
                    "data.analyze_transactions",
                    new Dictionary<string, string>
                    {
                        ["sessionId"] = Guid.NewGuid().ToString(),
                        ["totalAmount"] = "125000",
                        ["transactionCount"] = "42",
                        ["submittedAt"] = DateTimeOffset.UtcNow.ToString("O")
                    }
                )
            ],
            CancellationToken.None
        );

        var result = results.Should().ContainSingle().Subject;

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Be("Missing required argument: reportName.");
        result.OutputJson.Should().Be("{}");
        dataAgent.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_Should_return_failure_when_total_amount_is_invalid()
    {
        var dataAgent = new FakeDataAgent();
        var executor = CreateExecutor(dataAgent);

        var results = await executor.ExecuteAsync(
            [
                CreateValidDataCall() with
                {
                    Arguments = new Dictionary<string, string>
                    {
                        ["sessionId"] = Guid.NewGuid().ToString(),
                        ["reportName"] = "financial-report",
                        ["totalAmount"] = "not-a-decimal",
                        ["transactionCount"] = "42",
                        ["submittedAt"] = DateTimeOffset.UtcNow.ToString("O")
                    }
                }
            ],
            CancellationToken.None
        );

        var result = results.Should().ContainSingle().Subject;

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Be("Invalid decimal argument: totalAmount.");
        result.OutputJson.Should().Be("{}");
        dataAgent.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_Should_execute_legal_search_cnv_regulation_with_valid_query()
    {
        var mcpClient = new FakeCnvRegulationMcpClient();
        var executor = CreateExecutor(mcpClient: mcpClient);

        var results = await executor.ExecuteAsync(
            [
                CreateCall(
                    "legal.search_cnv_regulation",
                    new Dictionary<string, string>
                    {
                        ["query"] = "agente liquidacion compensacion",
                        ["area"] = "Agentes",
                        ["limit"] = "3",
                        ["requiresReview"] = "true"
                    }
                )
            ],
            CancellationToken.None
        );

        var result = results.Should().ContainSingle().Subject;

        result.ToolName.Should().Be("legal.search_cnv_regulation");
        result.Succeeded.Should().BeTrue();
        result.Summary.Should().Be("CNV search returned 1 results.");
        result.Engine.Should().Be("MCP CNV Regulation Server");
        result.Error.Should().BeNull();
        result.OutputJson.Should().NotBe("{}");

        mcpClient.WasCalled.Should().BeTrue();
        mcpClient.ReceivedRequest.Should().NotBeNull();
        mcpClient.ReceivedRequest!.Query.Should().Be("agente liquidacion compensacion");
        mcpClient.ReceivedRequest.Area.Should().Be("Agentes");
        mcpClient.ReceivedRequest.Limit.Should().Be(3);
        mcpClient.ReceivedRequest.RequiresReview.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_Should_return_failure_when_legal_query_is_missing()
    {
        var mcpClient = new FakeCnvRegulationMcpClient();
        var executor = CreateExecutor(mcpClient: mcpClient);

        var results = await executor.ExecuteAsync(
            [
                CreateCall(
                    "legal.search_cnv_regulation",
                    new Dictionary<string, string>
                    {
                        ["area"] = "Agentes"
                    }
                )
            ],
            CancellationToken.None
        );

        var result = results.Should().ContainSingle().Subject;

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Be("Missing required argument: query.");
        result.OutputJson.Should().Be("{}");
        mcpClient.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_Should_reject_unknown_tool_defensively()
    {
        var dataAgent = new FakeDataAgent();
        var mcpClient = new FakeCnvRegulationMcpClient();
        var executor = CreateExecutor(dataAgent, mcpClient);

        var results = await executor.ExecuteAsync(
            [
                CreateCall(
                    "workflow.complete",
                    new Dictionary<string, string>()
                )
            ],
            CancellationToken.None
        );

        var result = results.Should().ContainSingle().Subject;

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Be("Tool is not executable.");
        result.OutputJson.Should().Be("{}");
        dataAgent.WasCalled.Should().BeFalse();
        mcpClient.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_Should_not_throw_for_invalid_input()
    {
        var executor = CreateExecutor();

        var act = async () => await executor.ExecuteAsync(
            [
                CreateValidDataCall() with
                {
                    Arguments = new Dictionary<string, string>
                    {
                        ["sessionId"] = Guid.NewGuid().ToString(),
                        ["reportName"] = "financial-report",
                        ["totalAmount"] = "125000",
                        ["transactionCount"] = "not-an-int",
                        ["submittedAt"] = "not-a-date"
                    }
                },
                CreateCall(
                    "legal.search_cnv_regulation",
                    new Dictionary<string, string>
                    {
                        ["query"] = "agentes",
                        ["limit"] = "not-an-int"
                    }
                )
            ],
            CancellationToken.None
        );

        var exception = await Record.ExceptionAsync(act);

        exception.Should().BeNull();
    }

    private static ControlledToolExecutor CreateExecutor(
        FakeDataAgent? dataAgent = null,
        FakeCnvRegulationMcpClient? mcpClient = null)
    {
        return new ControlledToolExecutor(
            dataAgent ?? new FakeDataAgent(),
            mcpClient ?? new FakeCnvRegulationMcpClient(),
            NullLogger<ControlledToolExecutor>.Instance
        );
    }

    private static ApprovedToolCall CreateValidDataCall()
    {
        return CreateCall(
            "data.analyze_transactions",
            new Dictionary<string, string>
            {
                ["sessionId"] = Guid.NewGuid().ToString(),
                ["reportName"] = "financial-report",
                ["totalAmount"] = "125000",
                ["transactionCount"] = "42",
                ["submittedAt"] = DateTimeOffset.UtcNow.ToString("O")
            }
        );
    }

    private static ApprovedToolCall CreateCall(
        string toolName,
        IReadOnlyDictionary<string, string> arguments)
    {
        return new ApprovedToolCall(
            ToolName: toolName,
            Arguments: arguments,
            Reason: "Approved read-only tool call."
        );
    }

    private sealed class FakeDataAgent : IDataAgent
    {
        public bool WasCalled { get; private set; }

        public FinancialReportContext? ReceivedReport { get; private set; }

        public Task<DataAgentResult> AnalyzeAsync(
            FinancialReportContext report,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            ReceivedReport = report;

            return Task.FromResult(
                new DataAgentResult(
                    HasAnomaly: true,
                    Severity: "High",
                    Summary: "Data analysis completed.",
                    Engine: "Fake DataAgent",
                    Evidence:
                    [
                        new AnomalyEvidence(
                            Metric: "TransactionAmountZScore",
                            Value: 4.5,
                            Threshold: 3.0,
                            Interpretation: "Above threshold."
                        )
                    ]
                )
            );
        }
    }

    private sealed class FakeCnvRegulationMcpClient : ICnvRegulationMcpClient
    {
        public bool WasCalled { get; private set; }

        public CnvRegulationSearchRequest? ReceivedRequest { get; private set; }

        public Task<CnvRegulationSearchResponse> SearchAsync(
            CnvRegulationSearchRequest request,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            ReceivedRequest = request;

            return Task.FromResult(
                new CnvRegulationSearchResponse(
                    request.Query,
                    [
                        new CnvRegulationSearchResult(
                            DocumentId: "cnv-result",
                            ChunkId: "cnv-result-chunk",
                            Title: "CNV cited result",
                            Chapter: "Capitulo I",
                            Section: null,
                            Article: "Articulo 1",
                            Source: "Infoleg",
                            Url: "https://servicios.infoleg.gob.ar/",
                            Snippet: "Texto encontrado.",
                            Score: 0.95,
                            Citations:
                            [
                                new CnvRegulationCitation(
                                    Source: "Infoleg",
                                    DocumentType: "Resolucion General",
                                    ResolutionNumber: "622/2013",
                                    Title: "Resolucion General 622/2013",
                                    Chapter: "Capitulo I",
                                    Section: null,
                                    Article: "Articulo 1",
                                    PublicationDate: "2013-09-09",
                                    Url: "https://servicios.infoleg.gob.ar/",
                                    QuotedText: "Texto normativo citado."
                                )
                            ]
                        )
                    ],
                    Warnings: ["requires review"]
                )
            );
        }
    }
}
