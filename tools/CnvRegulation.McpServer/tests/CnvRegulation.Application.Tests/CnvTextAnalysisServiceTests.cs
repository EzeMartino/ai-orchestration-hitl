using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Analysis;
using CnvRegulation.Application.Contracts;
using CnvRegulation.Domain;
using FluentAssertions;

namespace CnvRegulation.Application.Tests;

public sealed class CnvTextAnalysisServiceTests
{
    [Fact]
    public async Task AnalyzeText_ShouldReturnRequiresReview_WhenTopicDetectedAndEvidenceFound()
    {
        var searchService = new CapturingSearchService();
        var service = CreateService(searchService);

        var response = await service.AnalyzeAsync(
            new AnalyzeTextAgainstCnvRequest
            {
                Text = "El agente podra ofrecer instrumentos a clientes sin verificar su perfil ni informar riesgos.",
                RegulationArea = "ALyC",
                StrictMode = true
            },
            CancellationToken.None);

        response.Status.Should().Be("requires_review");
        response.Findings.Should().NotBeEmpty();
        response.Findings[0].RiskLevel.Should().Be("medium");
        response.Findings[0].Issue.Should().Contain("client information");
        response.Findings[0].ReasoningSummary.Should().Contain("Human legal review");
        response.Findings[0].Confidence.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AnalyzeText_ShouldReturnInsufficientEvidence_WhenNoTopicDetected()
    {
        var searchService = new CapturingSearchService();
        var service = CreateService(searchService);

        var response = await service.AnalyzeAsync(
            new AnalyzeTextAgainstCnvRequest
            {
                Text = "Texto comercial generico sin referencia regulatoria clara."
            },
            CancellationToken.None);

        response.Status.Should().Be("insufficient_evidence");
        response.Findings.Should().BeEmpty();
        response.Warnings.Should().Contain("No clear CNV regulatory topic was detected.");
        searchService.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task AnalyzeText_ShouldIncludeCitations()
    {
        var service = CreateService(new CapturingSearchService());

        var response = await service.AnalyzeAsync(
            new AnalyzeTextAgainstCnvRequest
            {
                Text = "ALyC debe informar riesgos a clientes."
            },
            CancellationToken.None);

        response.Findings.Should().OnlyContain(finding => !string.IsNullOrWhiteSpace(finding.Citation.Url));
        response.Findings.Should().OnlyContain(finding => !string.IsNullOrWhiteSpace(finding.Citation.QuotedText));
    }

    [Fact]
    public async Task AnalyzeText_ShouldPreserveWarnings()
    {
        var service = CreateService(new CapturingSearchService(warnings: ["candidate source"]));

        var response = await service.AnalyzeAsync(
            new AnalyzeTextAgainstCnvRequest
            {
                Text = "ALyC debe informar riesgos a clientes."
            },
            CancellationToken.None);

        response.Warnings.Should().Contain("Automated review only.");
        response.Warnings.Should().Contain("Sources may require legal validation.");
        response.Warnings.Should().Contain("candidate source");
        response.Disclaimer.Should().Be("This is an automated regulatory review aid, not legal advice.");
    }

    [Fact]
    public async Task AnalyzeText_ShouldUseFullTextAsPrimarySearch()
    {
        var searchService = new CapturingSearchService();
        var service = CreateService(searchService);

        await service.AnalyzeAsync(
            new AnalyzeTextAgainstCnvRequest
            {
                Text = "ALyC debe informar riesgos a clientes.",
                UseHybridSearch = false
            },
            CancellationToken.None);

        searchService.Requests.Should().NotBeEmpty();
        searchService.Requests[0].SearchMode.Should().Be("full_text");
        searchService.Requests.Should().OnlyContain(request => request.Area == null);
        searchService.Requests.Should().OnlyContain(request => request.SearchMode == "full_text");
    }

    [Fact]
    public async Task AnalyzeText_ShouldUseHybridOnlyAsSecondPass_WhenEnabled()
    {
        var searchService = new CapturingSearchService(resultsPerMode: mode => mode == "full_text" ? 1 : 1);
        var service = CreateService(searchService);

        var response = await service.AnalyzeAsync(
            new AnalyzeTextAgainstCnvRequest
            {
                Text = "ALyC debe informar riesgos a clientes.",
                UseHybridSearch = true
            },
            CancellationToken.None);

        searchService.Requests.Select(request => request.SearchMode).Should().StartWith("full_text");
        searchService.Requests.Should().Contain(request => request.SearchMode == "hybrid");
        response.Warnings.Should().Contain("Hybrid search was used only as secondary evidence.");
    }

    [Fact]
    public async Task AnalyzeText_ShouldNotClaimCompliance()
    {
        var service = CreateService(new CapturingSearchService());

        var response = await service.AnalyzeAsync(
            new AnalyzeTextAgainstCnvRequest
            {
                Text = "ALyC debe informar riesgos a clientes."
            },
            CancellationToken.None);

        response.Status.Should().NotBe("compliant");
        response.Findings.Should().OnlyContain(finding =>
            !finding.ReasoningSummary.Contains("compliant", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("ALyC obligaciones", "ALyC obligaciones")]
    [InlineData("oferta publica de valores negociables", "Oferta publica")]
    [InlineData("hecho relevante por AIF", "Hecho relevante")]
    [InlineData("prevencion de lavado UIF", "Prevencion de lavado")]
    [InlineData("fondos comunes FCI cuotapartes", "Fondos comunes de inversion")]
    [InlineData("fiduciario financiero fideicomiso", "Fiduciario financiero")]
    public void TopicExtractor_ShouldDetectExpectedTopics(string text, string expectedTopic)
    {
        var extractor = new RegulatoryTopicExtractor();

        var topics = extractor.ExtractTopics(text, regulationArea: null);

        topics.Should().Contain(topic => topic.Name == expectedTopic);
    }

    private static CnvTextAnalysisService CreateService(IRegulationSearchService searchService) =>
        new(
            new RegulatoryTopicExtractor(),
            searchService,
            new RegulatoryFindingBuilder());

    private sealed class CapturingSearchService(
        IReadOnlyList<string>? warnings = null,
        Func<string, int>? resultsPerMode = null) : IRegulationSearchService
    {
        private readonly Func<string, int> resultsPerMode = resultsPerMode ?? (_ => 3);

        public List<SearchRegulationRequest> Requests { get; } = [];

        public Task<SearchRegulationResponse> SearchAsync(
            SearchRegulationRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);

            var count = resultsPerMode(request.SearchMode);
            return Task.FromResult(new SearchRegulationResponse
            {
                Query = request.Query,
                Results = Enumerable
                    .Range(1, count)
                    .Select(index => CreateResult(request, index))
                    .ToArray(),
                Warnings = warnings ?? []
            });
        }

        private static RegulationSearchResult CreateResult(SearchRegulationRequest request, int index) =>
            new()
            {
                DocumentId = "cnv-toc-2013",
                ChunkId = $"{request.SearchMode}-{index}-{request.Query.GetHashCode(StringComparison.OrdinalIgnoreCase)}",
                Title = "Normas CNV N.T. 2013",
                Article = index == 1 ? "Articulo 18" : $"Articulo {index}",
                Source = "CNV",
                Url = "https://www.cnv.gov.ar/",
                Snippet = "El agente debera informar riesgos y obligaciones frente a clientes.",
                Score = request.SearchMode == "hybrid" ? 0.7 : 1.0,
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["searchMode"] = request.SearchMode
                },
                Citations =
                [
                    new RegulationCitation
                    {
                        Source = "CNV",
                        DocumentType = "Texto Ordenado",
                        Title = "Normas CNV N.T. 2013",
                        Article = index == 1 ? "Articulo 18" : $"Articulo {index}",
                        Url = "https://www.cnv.gov.ar/",
                        QuotedText = "El agente debera informar riesgos y obligaciones frente a clientes."
                    }
                ]
            };
    }
}
