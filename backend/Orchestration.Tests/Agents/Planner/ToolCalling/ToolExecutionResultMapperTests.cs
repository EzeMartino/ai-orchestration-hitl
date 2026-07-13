using System.Text.Json;
using FluentAssertions;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
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

    [Fact]
    public void TryMapDataResult_Should_preserve_explicit_human_review()
    {
        var result = MapDataResult(CreateDataResult(requiresHumanReview: true));

        result!.RequiresHumanReview.Should().BeTrue();
    }

    [Theory]
    [InlineData(FinancialAnalysisExecutionStatus.Failed)]
    [InlineData(FinancialAnalysisExecutionStatus.Degraded)]
    [InlineData(FinancialAnalysisExecutionStatus.LegacyUnknown)]
    public void TryMapDataResult_Should_require_review_for_incomplete_financial_execution(
        FinancialAnalysisExecutionStatus status)
    {
        var result = MapDataResult(CreateDataResult(status: status));

        result!.RequiresHumanReview.Should().BeTrue();
    }

    [Fact]
    public void TryMapDataResult_Should_preserve_false_review_for_succeeded_financial_execution()
    {
        var result = MapDataResult(CreateDataResult(
            status: FinancialAnalysisExecutionStatus.Succeeded));

        result!.RequiresHumanReview.Should().BeFalse();
    }

    [Fact]
    public void TryMapDataResult_Should_not_force_review_for_legacy_result_without_financial_analysis()
    {
        var result = MapDataResult(CreateDataResult());

        result!.FinancialAnalysis.Should().BeNull();
        result.RequiresHumanReview.Should().BeFalse();
    }

    [Fact]
    public void TryMapDataResult_Should_normalize_null_financial_execution_to_legacy_unknown_review()
    {
        const string outputJson = """
            {
              "hasAnomaly": false,
              "severity": "Low",
              "summary": "Partial financial result.",
              "engine": "Financial Workflow",
              "evidence": [],
              "financialAnalysis": {
                "engine": "Financial Workflow",
                "documentId": "document-1",
                "company": "Acme",
                "ratios": [],
                "comparisons": [],
                "riskSignals": [],
                "riskEvidence": [],
                "warnings": ["Preserved warning."],
                "limitations": [],
                "execution": null
              },
              "requiresHumanReview": false
            }
            """;

        var result = new ToolExecutionResultMapper().TryMapDataResult(
        [
            CreateExecutionResult("data.analyze_transactions", outputJson)
        ]);

        result.Should().NotBeNull();
        result!.RequiresHumanReview.Should().BeTrue();
        result.FinancialAnalysis.Should().NotBeNull();
        result.FinancialAnalysis!.DocumentId.Should().Be("document-1");
        result.FinancialAnalysis.Company.Should().Be("Acme");
        result.FinancialAnalysis.Warnings.Should().ContainSingle()
            .Which.Should().Be("Preserved warning.");
        result.FinancialAnalysis.Execution.Should().BeSameAs(
            FinancialAnalysisExecution.LegacyUnknown);
    }

    private static DataAgentResult? MapDataResult(DataAgentResult dataResult)
    {
        return new ToolExecutionResultMapper().TryMapDataResult(
        [
            CreateExecutionResult(
                "data.analyze_transactions",
                JsonSerializer.Serialize(dataResult, JsonOptions))
        ]);
    }

    private static DataAgentResult CreateDataResult(
        FinancialAnalysisExecutionStatus? status = null,
        bool requiresHumanReview = false)
    {
        FinancialAnalysisContext? financialAnalysis = null;
        if (status is not null)
        {
            financialAnalysis = new FinancialAnalysisContext(
                "Financial Workflow", "document", null, [], [], [], [], [], [])
            {
                Execution = new FinancialAnalysisExecution(status.Value, [])
            };
        }

        return new DataAgentResult(
            false, "Low", "No anomaly.", "Test", [],
            financialAnalysis, requiresHumanReview);
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
