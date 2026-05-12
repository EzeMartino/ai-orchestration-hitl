using CnvRegulation.Application.Contracts;
using CnvRegulation.Domain;
using CnvRegulation.Infrastructure.Diagnostics;
using CnvRegulation.Infrastructure.InMemory;
using CnvRegulation.Infrastructure.Repositories;
using CnvRegulation.Infrastructure.Search;
using FluentAssertions;

namespace CnvRegulation.Application.Tests;

public sealed class ExplainSearchQueryServiceTests
{
    [Fact]
    public async Task ExplainSearchQuery_ShouldReportGeneratedQueries()
    {
        var service = await CreateServiceAsync();

        var report = await service.ExplainAsync(
            new ExplainSearchQueryRequest
            {
                Query = "hecho-relevante",
                Limit = 5
            },
            CancellationToken.None);

        report.NormalizedQuery.Should().Be("hecho relevante");
        report.ExpandedTerms.Should().Contain("informaci\u00f3n relevante");
        report.GeneratedQueries.Should().Contain(query => query.Query == "hecho-relevante");
        report.GeneratedQueries.Should().Contain(query => query.Query == "informaci\u00f3n relevante");
    }

    [Fact]
    public async Task ExplainSearchQuery_ShouldReportRawResultCounts()
    {
        var service = await CreateServiceAsync();

        var report = await service.ExplainAsync(
            new ExplainSearchQueryRequest
            {
                Query = "hecho relevante",
                Limit = 5
            },
            CancellationToken.None);

        report.GeneratedQueries.Should().Contain(query =>
            query.Query == "informaci\u00f3n relevante" && query.RawResultCount > 0);
        report.TermPresence.Should().Contain(term =>
            term.Term == "informacion relevante" && term.FoundInDocuments > 0);
        report.TopPartialMatches.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ExplainSearchQuery_ShouldReportDuplicateAndWrapperFiltering()
    {
        var repository = new InMemoryRegulationRepository();
        var queryExpander = CreateQueryExpander();
        var searchService = new InMemoryRegulationSearchService(repository, repository, queryExpander);
        var service = new ExplainSearchQueryService(searchService, queryExpander, repository, repository);
        var document = new RegulationDocument
        {
            Id = "wrapper-doc",
            Source = "Infoleg",
            DocumentType = "Resolucion General",
            Title = "Wrapper",
            Url = "https://servicios.infoleg.gob.ar/infolegInternet/test.htm",
            Status = "candidate",
            Text = "Texto de emisores",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["searchable"] = "false"
            }
        };
        await repository.SaveAsync(document, CancellationToken.None);
        await repository.ReplaceForDocumentAsync(
            document.Id,
            [
                new RegulationChunk
                {
                    Id = "wrapper-doc-art-1",
                    DocumentId = document.Id,
                    Article = "Articulo 1",
                    ChunkIndex = 0,
                    Text = "Los emisores deben informar.",
                    ContentHash = "same",
                    Metadata = new Dictionary<string, string>()
                },
                new RegulationChunk
                {
                    Id = "wrapper-doc-art-2",
                    DocumentId = document.Id,
                    Article = "Articulo 2",
                    ChunkIndex = 1,
                    Text = "Los emisores deben informar.",
                    ContentHash = "same",
                    DuplicateOfChunkId = "wrapper-doc-art-1",
                    Metadata = new Dictionary<string, string>()
                }
            ],
            CancellationToken.None);

        var report = await service.ExplainAsync(
            new ExplainSearchQueryRequest { Query = "emisora", Limit = 5 },
            CancellationToken.None);

        report.HiddenNonSearchableResults.Should().BeGreaterThan(0);
        report.HiddenDuplicateResults.Should().BeGreaterThan(0);
    }

    private static async Task<ExplainSearchQueryService> CreateServiceAsync()
    {
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
                Text = "El regimen exige publicar informaciones relevantes."
            },
            CancellationToken.None);
        var queryExpander = CreateQueryExpander();
        var searchService = new InMemoryRegulationSearchService(repository, repository, queryExpander);

        return new ExplainSearchQueryService(searchService, queryExpander, repository, repository);
    }

    private static StaticRegulationQueryExpander CreateQueryExpander() =>
        new(new RegulationAliasesOptions());
}
