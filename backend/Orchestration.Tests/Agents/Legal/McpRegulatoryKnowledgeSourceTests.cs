using FluentAssertions;
using Microsoft.Extensions.Options;
using Orchestration.Application.Agents.Shared;
using Orchestration.Infrastructure.Agents.Legal.Regulations;
using Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;

namespace Orchestration.Tests.Agents.Legal;

public class McpRegulatoryKnowledgeSourceTests
{
    [Fact]
    public async Task ReviewAsync_Should_map_cited_mcp_results_to_regulatory_findings()
    {
        var source = CreateSource(new FakeCnvRegulationMcpClient());

        var result = await source.ReviewAsync(
            CreateReport(),
            CancellationToken.None
        );

        result.SourceEngine.Should().Be("MCP CNV Regulation Server");
        result.HasComplianceRisk.Should().BeTrue();
        result.RiskLevel.Should().Be("Medium");
        result.Findings.Should().NotBeEmpty();
        result.Warnings.Should().Contain(
            "Automated regulatory retrieval only. Human legal review is required before making operational decisions."
        );

        result.Findings
            .Should()
            .Contain(x =>
                x.Regulation.Contains("622/2013") &&
                x.Section == "Articulo 1" &&
                x.Finding == "Texto normativo citado."
            );
    }

    [Fact]
    public async Task ReviewAsync_Should_map_only_cited_mcp_results_to_regulatory_findings()
    {
        var source = CreateSource(new MixedCitationCnvRegulationMcpClient());

        var result = await source.ReviewAsync(
            CreateReport(),
            CancellationToken.None
        );

        result.HasComplianceRisk.Should().BeTrue();
        result.Findings.Should().ContainSingle();
        result.Findings[0].Finding.Should().Be("Texto normativo citado.");
        result.Findings.Should().NotContain(x => x.Finding == "Snippet without citations.");
    }

    [Fact]
    public async Task ReviewAsync_Should_ignore_mcp_results_without_citations()
    {
        var source = CreateSource(new UncitedCnvRegulationMcpClient());

        var result = await source.ReviewAsync(
            CreateReport(),
            CancellationToken.None
        );

        result.HasComplianceRisk.Should().BeFalse();
        result.RiskLevel.Should().Be("Low");
        result.Findings.Should().BeEmpty();
        result.Warnings.Should().Contain("candidate source");
        result.Warnings.Should().Contain(
            "No cited CNV regulatory evidence was found by the MCP search strategy."
        );
    }

    [Fact]
    public async Task ReviewAsync_Should_include_mcp_response_warnings()
    {
        var source = CreateSource(new WarningCnvRegulationMcpClient());

        var result = await source.ReviewAsync(
            CreateReport(),
            CancellationToken.None
        );

        result.Warnings.Should().Contain("requires review");
        result.Warnings.Should().Contain(
            "Automated regulatory retrieval only. Human legal review is required before making operational decisions."
        );
    }

    private static McpRegulatoryKnowledgeSource CreateSource(
        ICnvRegulationMcpClient client)
    {
        var options = Options.Create(
            new CnvRegulationMcpOptions
            {
                Enabled = true,
                Command = "dotnet",
                Args = [],
                DefaultLimit = 5
            }
        );

        return new McpRegulatoryKnowledgeSource(
            client,
            options,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<McpRegulatoryKnowledgeSource>.Instance
        );
    }

    private static FinancialReportContext CreateReport()
    {
        return new FinancialReportContext(
            SessionId: Guid.NewGuid(),
            ReportName: "cnv-mcp-test-report",
            TotalAmount: 125000m,
            TransactionCount: 42,
            SubmittedAt: DateTimeOffset.UtcNow
        );
    }

    private sealed class MixedCitationCnvRegulationMcpClient : ICnvRegulationMcpClient
    {
        public bool IsConnected => true;
        public int ColdStartCount => 0;
        public int ResetCount => 0;
        public string? LastError => null;

        public Task<CnvRegulationSearchResponse> SearchAsync(
            CnvRegulationSearchRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                new CnvRegulationSearchResponse(
                    request.Query,
                    [
                        CreateResult(
                            "uncited",
                            "Uncited result",
                            "Snippet without citations.",
                            []
                        ),
                        CreateResult(
                            "cited",
                            "Cited result",
                            "Snippet with citations.",
                            [
                                CreateCitation()
                            ]
                        )
                    ],
                    []
                )
            );
        }
    }

    private sealed class UncitedCnvRegulationMcpClient : ICnvRegulationMcpClient
    {
        public bool IsConnected => true;
        public int ColdStartCount => 0;
        public int ResetCount => 0;
        public string? LastError => null;

        public Task<CnvRegulationSearchResponse> SearchAsync(
            CnvRegulationSearchRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                new CnvRegulationSearchResponse(
                    request.Query,
                    [
                        CreateResult(
                            "uncited",
                            "Uncited result",
                            "Snippet without citations.",
                            []
                        )
                    ],
                    ["candidate source"]
                )
            );
        }
    }

    private sealed class WarningCnvRegulationMcpClient : ICnvRegulationMcpClient
    {
        public bool IsConnected => true;
        public int ColdStartCount => 0;
        public int ResetCount => 0;
        public string? LastError => null;

        public Task<CnvRegulationSearchResponse> SearchAsync(
            CnvRegulationSearchRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                new CnvRegulationSearchResponse(
                    request.Query,
                    [
                        CreateResult(
                            "cited",
                            "Cited result",
                            "Snippet with citations.",
                            [
                                CreateCitation()
                            ]
                        )
                    ],
                    ["requires review"]
                )
            );
        }
    }

    private static CnvRegulationSearchResult CreateResult(
        string documentId,
        string title,
        string snippet,
        IReadOnlyList<CnvRegulationCitation> citations)
    {
        return new CnvRegulationSearchResult(
            DocumentId: documentId,
            ChunkId: $"{documentId}-chunk",
            Title: title,
            Chapter: "Capitulo I",
            Section: null,
            Article: "Articulo 1",
            Source: "Infoleg",
            Url: "https://servicios.infoleg.gob.ar/",
            Snippet: snippet,
            Score: 0.123,
            Citations: citations
        );
    }

    private static CnvRegulationCitation CreateCitation()
    {
        return new CnvRegulationCitation(
            Source: "Infoleg",
            DocumentType: "Resolucion General",
            ResolutionNumber: "622/2013",
            Title: "Resolución General 622/2013 - Texto actualizado",
            Chapter: "Capitulo I",
            Section: null,
            Article: "Articulo 1",
            PublicationDate: "2013-09-09",
            Url: "https://servicios.infoleg.gob.ar/",
            QuotedText: "Texto normativo citado."
        );
    }
}
