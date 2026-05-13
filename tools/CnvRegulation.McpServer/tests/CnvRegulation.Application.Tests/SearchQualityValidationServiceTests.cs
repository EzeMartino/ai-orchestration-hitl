using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;
using CnvRegulation.Domain;
using CnvRegulation.Infrastructure.Diagnostics;
using CnvRegulation.Infrastructure.InMemory;
using CnvRegulation.Infrastructure.Repositories;
using CnvRegulation.Infrastructure.Search;
using FluentAssertions;

namespace CnvRegulation.Application.Tests;

public sealed class SearchQualityValidationServiceTests
{
    [Fact]
    public async Task ValidateAsync_ShouldPass_WhenSearchMatchesExpectations()
    {
        using var testDirectory = TempDirectory.Create();
        var querySetPath = Path.Combine(testDirectory.Path, "queries.json");
        await File.WriteAllTextAsync(
            querySetPath,
            """
            {
              "queries": [
                {
                  "id": "alyc",
                  "query": "ALyC obligaciones",
                  "expectedAnyTerms": [ "agente", "liquidacion", "compensacion" ],
                  "expectedSources": [ "CNV" ],
                  "minResults": 1
                }
              ]
            }
            """);
        var repository = new InMemoryRegulationRepository();
        await repository.SaveAsync(
            new RegulationDocument
            {
                Id = "cnv-test",
                Source = "CNV",
                DocumentType = "Texto Ordenado",
                Title = "Normas CNV test",
                Url = "https://www.cnv.gov.ar/",
                Status = "candidate",
                Text = "Obligaciones del agente de liquidacion y compensacion."
            },
            CancellationToken.None);
        var queryExpander = CreateQueryExpander();
        var searchService = new InMemoryRegulationSearchService(repository, repository, queryExpander);
        var validator = new SearchQualityValidationService(searchService, queryExpander);

        var report = await validator.ValidateAsync(
            new ValidateSearchQualityRequest { QuerySetPath = querySetPath },
            CancellationToken.None);

        report.Queries.Should().Be(1);
        report.Passed.Should().Be(1);
        report.Failed.Should().Be(0);
        var result = report.Results.Should().ContainSingle().Which;
        result.ExpandedQueries.Should().Contain(query =>
            StaticRegulationQueryExpander.Normalize(query).Contains("agente de liquidacion", StringComparison.OrdinalIgnoreCase));
        result.CitationsPresent.Should().BeTrue();
        result.TopResultSource.Should().Be("CNV");
    }

    [Fact]
    public async Task ValidateAsync_ShouldFail_WhenMinimumResultsAreMissing()
    {
        using var testDirectory = TempDirectory.Create();
        var querySetPath = Path.Combine(testDirectory.Path, "queries.json");
        await File.WriteAllTextAsync(
            querySetPath,
            """
            {
              "queries": [
                {
                  "id": "missing",
                  "query": "missing term",
                  "expectedAnyTerms": [ "missing" ],
                  "minResults": 1
                }
              ]
            }
            """);
        var validator = new SearchQualityValidationService(new EmptySearchService(), CreateQueryExpander());

        var report = await validator.ValidateAsync(
            new ValidateSearchQualityRequest { QuerySetPath = querySetPath },
            CancellationToken.None);

        report.Queries.Should().Be(1);
        report.Passed.Should().Be(0);
        report.Failed.Should().Be(1);
        report.Results[0].FailureReasons.Should().Contain(reason =>
            reason.Contains("expected at least 1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateAsync_ShouldReturnWarning_WhenQueryFileIsMissing()
    {
        var validator = new SearchQualityValidationService(new EmptySearchService(), CreateQueryExpander());

        var report = await validator.ValidateAsync(
            new ValidateSearchQualityRequest { QuerySetPath = "missing.json" },
            CancellationToken.None);

        report.Queries.Should().Be(0);
        report.Warnings.Should().Contain(warning => warning.Contains("does not exist", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateSearchQuality_ShouldPassKnownRegressionQueries()
    {
        using var testDirectory = TempDirectory.Create();
        var querySetPath = Path.Combine(testDirectory.Path, "queries.json");
        await File.WriteAllTextAsync(
            querySetPath,
            """
            {
              "queries": [
                {
                  "id": "hecho-relevante",
                  "query": "hecho-relevante",
                  "expectedAnyTerms": [ "informacion relevante", "informaciones relevantes" ],
                  "minResults": 1
                },
                {
                  "id": "informacion-relevante",
                  "query": "informacion_relevante",
                  "expectedAnyTerms": [ "informacion relevante", "informaciones relevantes" ],
                  "minResults": 1
                },
                {
                  "id": "fiduciario-financiero",
                  "query": "fiduciario-financiero",
                  "expectedAnyTerms": [ "fideicomiso financiero", "fideicomisos financieros" ],
                  "minResults": 1
                },
                {
                  "id": "emisora",
                  "query": "emisora",
                  "expectedAnyTerms": [ "emisor", "emisores", "entidades emisoras" ],
                  "minResults": 1
                }
              ]
            }
            """);
        var repository = new InMemoryRegulationRepository();
        await repository.SaveAsync(
            new RegulationDocument
            {
                Id = "cnv-regression",
                Source = "CNV",
                DocumentType = "Texto Ordenado",
                Title = "Normas CNV regresiones",
                Url = "https://www.cnv.gov.ar/",
                Status = "candidate",
                Text = string.Join(
                    ' ',
                    "El regimen exige publicar informaciones relevantes.",
                    "Los fideicomisos financieros tienen reglas especificas.",
                    "Los emisores deben cumplir obligaciones informativas.")
            },
            CancellationToken.None);
        var queryExpander = CreateQueryExpander();
        var searchService = new InMemoryRegulationSearchService(repository, repository, queryExpander);
        var validator = new SearchQualityValidationService(searchService, queryExpander);

        var report = await validator.ValidateAsync(
            new ValidateSearchQualityRequest { QuerySetPath = querySetPath },
            CancellationToken.None);

        report.Queries.Should().Be(4);
        report.Failed.Should().Be(0);
        report.Passed.Should().Be(4);
    }

    [Fact]
    public async Task ValidateSearchQuality_ShouldRunInHybridMode()
    {
        using var testDirectory = TempDirectory.Create();
        var querySetPath = Path.Combine(testDirectory.Path, "queries.json");
        await File.WriteAllTextAsync(
            querySetPath,
            """
            {
              "queries": [
                {
                  "id": "hybrid",
                  "query": "mercado autorizado",
                  "expectedAnyTerms": [ "mercado autorizado" ],
                  "minResults": 1
                }
              ]
            }
            """);
        var searchService = new CapturingSearchService();
        var validator = new SearchQualityValidationService(searchService, CreateQueryExpander());

        var report = await validator.ValidateAsync(
            new ValidateSearchQualityRequest
            {
                QuerySetPath = querySetPath,
                SearchMode = "hybrid"
            },
            CancellationToken.None);

        report.Passed.Should().Be(1);
        searchService.LastRequest!.SearchMode.Should().Be("hybrid");
    }

    [Fact]
    public async Task CompareAsync_ShouldReportTopResultDifferencesAndHybridBreakdown()
    {
        using var testDirectory = TempDirectory.Create();
        var querySetPath = Path.Combine(testDirectory.Path, "queries.json");
        await File.WriteAllTextAsync(
            querySetPath,
            """
            {
              "queries": [
                {
                  "id": "compare",
                  "query": "mercado autorizado",
                  "expectedAnyTerms": [ "mercado autorizado" ],
                  "minResults": 1
                }
              ]
            }
            """);
        var validator = new SearchQualityValidationService(new ModeAwareSearchService(), CreateQueryExpander());

        var report = await validator.CompareAsync(
            new CompareSearchQualityRequest
            {
                QuerySetPath = querySetPath,
                Modes = ["full_text", "hybrid"]
            },
            CancellationToken.None);

        report.Queries.Should().Be(1);
        report.BaselinePassed.Should().Be(1);
        report.CandidatePassed.Should().Be(1);
        var result = report.Results.Should().ContainSingle().Which;
        result.TopResultChanged.Should().BeTrue();
        result.CandidateScoreBreakdown.Should().Contain("finalScore");
        result.BaselineResult.Should().NotBeNull();
        result.CandidateResult.Should().NotBeNull();
        result.CandidateResult!.TopResultSnippet.Should().Be("mercado autorizado");
        result.CandidateResult.TopResultCitation.Should().NotBeNull();
    }

    private static StaticRegulationQueryExpander CreateQueryExpander() =>
        new(new RegulationAliasesOptions());

    private sealed class EmptySearchService : IRegulationSearchService
    {
        public Task<SearchRegulationResponse> SearchAsync(
            SearchRegulationRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new SearchRegulationResponse
            {
                Query = request.Query,
                Results = [],
                Warnings = []
            });
        }
    }

    private sealed class CapturingSearchService : IRegulationSearchService
    {
        public SearchRegulationRequest? LastRequest { get; private set; }

        public Task<SearchRegulationResponse> SearchAsync(
            SearchRegulationRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;

            return Task.FromResult(new SearchRegulationResponse
            {
                Query = request.Query,
                Results =
                [
                    new RegulationSearchResult
                    {
                        DocumentId = "doc",
                        ChunkId = "chunk",
                        Title = "Documento",
                        Source = "CNV",
                        Url = "https://www.cnv.gov.ar/",
                        Snippet = "mercado autorizado",
                        Score = 1,
                        Citations =
                        [
                            new RegulationCitation
                            {
                                Source = "CNV",
                                DocumentType = "Texto Ordenado",
                                Title = "Documento",
                                Url = "https://www.cnv.gov.ar/",
                                QuotedText = "mercado autorizado"
                            }
                        ]
                    }
                ],
                Warnings = []
            });
        }
    }

    private sealed class ModeAwareSearchService : IRegulationSearchService
    {
        public Task<SearchRegulationResponse> SearchAsync(
            SearchRegulationRequest request,
            CancellationToken cancellationToken)
        {
            var isHybrid = request.SearchMode.Equals("hybrid", StringComparison.OrdinalIgnoreCase);

            return Task.FromResult(new SearchRegulationResponse
            {
                Query = request.Query,
                Results =
                [
                    new RegulationSearchResult
                    {
                        DocumentId = isHybrid ? "hybrid-doc" : "fts-doc",
                        ChunkId = isHybrid ? "hybrid-chunk" : "fts-chunk",
                        Title = isHybrid ? "Documento hybrid" : "Documento fts",
                        Source = "CNV",
                        Url = "https://www.cnv.gov.ar/",
                        Snippet = "mercado autorizado",
                        Score = isHybrid ? 0.9 : 0.8,
                        Metadata = isHybrid
                            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["scoreBreakdown"] = "fullTextScore=0.8;vectorScore=0.7;finalScore=0.76"
                            }
                            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                        Citations =
                        [
                            new RegulationCitation
                            {
                                Source = "CNV",
                                DocumentType = "Texto Ordenado",
                                Title = "Documento",
                                Url = "https://www.cnv.gov.ar/",
                                QuotedText = "mercado autorizado"
                            }
                        ]
                    }
                ],
                Warnings = []
            });
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);

            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
