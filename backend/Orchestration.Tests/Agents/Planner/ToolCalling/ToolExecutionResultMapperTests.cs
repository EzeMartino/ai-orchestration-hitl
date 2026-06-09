using System.Text.Json;
using FluentAssertions;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Planner.ToolCalling;
using Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;
using Orchestration.Infrastructure.Agents.Planner.ToolCalling;

namespace Orchestration.Tests.Agents.Planner.ToolCalling;

public class ToolExecutionResultMapperTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void TryMapDataResult_Should_parse_executed_data_tool_output()
    {
        var mapper = new ToolExecutionResultMapper();
        var dataResult = new DataAgentResult(
            HasAnomaly: true,
            Severity: "High",
            Summary: "Anomaly detected.",
            Engine: "Semantic Kernel + Python/CSnakes",
            Evidence:
            [
                new AnomalyEvidence(
                    Metric: "TransactionAmountZScore",
                    Value: 4.5,
                    Threshold: 3.0,
                    Interpretation: "Above threshold."
                )
            ]
        );

        var result = mapper.TryMapDataResult(
            [
                CreateExecutionResult(
                    "data.analyze_transactions",
                    JsonSerializer.Serialize(dataResult, JsonOptions)
                )
            ]
        );

        result.Should().BeEquivalentTo(dataResult);
    }

    [Fact]
    public void TryMapLegalResult_Should_map_cited_results_to_legal_evidence()
    {
        var mapper = new ToolExecutionResultMapper();
        var response = new CnvRegulationSearchResponse(
            Query: "agentes",
            Results:
            [
                new CnvRegulationSearchResult(
                    DocumentId: "doc-1",
                    ChunkId: "chunk-1",
                    Title: "Normas CNV",
                    Chapter: null,
                    Section: "Agentes",
                    Article: null,
                    Source: "CNV",
                    Url: "https://example.test/cnv",
                    Snippet: "Snippet text.",
                    Score: 0.9,
                    Citations:
                    [
                        new CnvRegulationCitation(
                            Source: "CNV",
                            DocumentType: "Resolucion",
                            ResolutionNumber: "123",
                            Title: "Normas CNV",
                            Chapter: null,
                            Section: "Agentes",
                            Article: "Articulo 1",
                            PublicationDate: null,
                            Url: "https://example.test/cnv",
                            QuotedText: "Texto citado."
                        )
                    ]
                )
            ],
            Warnings: ["Candidate source."]
        );

        var result = mapper.TryMapLegalResult(
            [
                CreateExecutionResult(
                    "legal.search_cnv_regulation",
                    JsonSerializer.Serialize(response, JsonOptions)
                )
            ]
        );

        result.Should().NotBeNull();
        result!.HasComplianceRisk.Should().BeTrue();
        result.RiskLevel.Should().Be("Medium");
        result.Engine.Should().Be("Semantic Kernel + MCP CNV Regulation Server");
        result.Evidence.Should().ContainSingle().Which.Should().Be(
            new LegalEvidence(
                Regulation: "Normas CNV",
                Section: "Articulo 1",
                Finding: "Texto citado.",
                Source: "CNV | Resolucion | 123 | https://example.test/cnv"
            )
        );
        result.Warnings.Should().Contain("Candidate source.");
        result.Warnings.Should().Contain(
            "Recuperación regulatoria automatizada únicamente. Se requiere revisión legal humana antes de tomar decisiones operativas."
        );
    }

    [Fact]
    public void TryMapLegalResult_Should_ignore_uncited_results()
    {
        var mapper = new ToolExecutionResultMapper();
        var response = new CnvRegulationSearchResponse(
            Query: "agentes",
            Results:
            [
                new CnvRegulationSearchResult(
                    DocumentId: "doc-1",
                    ChunkId: "chunk-1",
                    Title: "Normas CNV",
                    Chapter: null,
                    Section: "Agentes",
                    Article: null,
                    Source: "CNV",
                    Url: "https://example.test/cnv",
                    Snippet: "Snippet text.",
                    Score: 0.9,
                    Citations: []
                )
            ],
            Warnings: []
        );

        var result = mapper.TryMapLegalResult(
            [
                CreateExecutionResult(
                    "legal.search_cnv_regulation",
                    JsonSerializer.Serialize(response, JsonOptions)
                )
            ]
        );

        result.Should().NotBeNull();
        result!.HasComplianceRisk.Should().BeFalse();
        result.RiskLevel.Should().Be("Low");
        result.Evidence.Should().BeEmpty();
    }

    [Fact]
    public void TryMapDataResult_Should_return_null_for_invalid_json()
    {
        var mapper = new ToolExecutionResultMapper();

        var result = mapper.TryMapDataResult(
            [
                CreateExecutionResult(
                    "data.analyze_transactions",
                    "{ invalid-json"
                )
            ]
        );

        result.Should().BeNull();
    }

    private static ToolExecutionResult CreateExecutionResult(
        string toolName,
        string outputJson)
    {
        return new ToolExecutionResult(
            ToolName: toolName,
            Status: ToolExecutionStatus.Executed,
            Succeeded: true,
            Summary: "Executed.",
            Engine: "Test Executor",
            OutputJson: outputJson,
            Error: null
        );
    }
}
