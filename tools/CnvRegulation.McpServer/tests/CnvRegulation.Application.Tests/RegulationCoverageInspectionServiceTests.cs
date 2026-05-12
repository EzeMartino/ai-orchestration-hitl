using CnvRegulation.Application.Contracts;
using CnvRegulation.Domain;
using CnvRegulation.Infrastructure.Diagnostics;
using CnvRegulation.Infrastructure.Repositories;
using FluentAssertions;

namespace CnvRegulation.Application.Tests;

public sealed class RegulationCoverageInspectionServiceTests
{
    [Fact]
    public async Task InspectAsync_ShouldReportCoverageMetrics()
    {
        var repository = new InMemoryRegulationRepository();
        await repository.SaveAsync(CreateDocument("doc-1", "CNV", "622/2013", "https://www.cnv.gov.ar/1", "PDF"), CancellationToken.None);
        await repository.SaveAsync(CreateDocument("doc-2", "Infoleg", null, "https://servicios.infoleg.gob.ar/infolegInternet/a.htm", "HTML"), CancellationToken.None);
        await repository.SaveAsync(CreateDocument("doc-3", "Infoleg", null, "https://servicios.infoleg.gob.ar/infolegInternet/a.htm", "HTML"), CancellationToken.None);
        await repository.ReplaceForDocumentAsync(
            "doc-1",
            [
                CreateChunk("chunk-1", "doc-1", "Artículo 1", "Texto regulatorio duplicado para medir cobertura."),
                CreateChunk("chunk-2", "doc-1", "Artículo 2", "Texto regulatorio duplicado para medir cobertura.")
            ],
            CancellationToken.None);
        var service = CreateService(repository);

        var report = await service.InspectAsync(new InspectCoverageRequest(), CancellationToken.None);

        report.DocumentsTotal.Should().Be(3);
        report.ChunksTotal.Should().Be(2);
        report.ChunksWithArticle.Should().Be(2);
        report.DistinctArticleCount.Should().Be(2);
        report.DuplicateUrlCount.Should().Be(1);
        report.DuplicateChunkCount.Should().Be(2);
        report.DocumentsWithZeroChunks.Should().Be(2);
        report.PotentialWrapperDocuments.Should().Be(2);
        report.SourceDistribution.Should().Contain(item => item.Label == "Infoleg" && item.Count == 2);
        report.ResolutionNumberDistribution.Should().Contain(item => item.Label == "unknown" && item.Count == 2);
        report.Warnings.Should().Contain("2 documents produced zero chunks");
    }

    [Fact]
    public async Task InspectAsync_ShouldReturnEmptyReport_WhenRepositoryIsEmpty()
    {
        var repository = new InMemoryRegulationRepository();
        var service = CreateService(repository);

        var report = await service.InspectAsync(new InspectCoverageRequest(), CancellationToken.None);

        report.DocumentsTotal.Should().Be(0);
        report.ChunksTotal.Should().Be(0);
        report.Warnings.Should().BeEmpty();
    }

    private static RegulationCoverageInspectionService CreateService(InMemoryRegulationRepository repository) =>
        new(
            repository,
            repository,
            new ChunkQualityInspectionService(repository, repository));

    private static RegulationDocument CreateDocument(
        string id,
        string source,
        string? resolutionNumber,
        string url,
        string fileType) =>
        new()
        {
            Id = id,
            Source = source,
            DocumentType = "Resolucion General",
            ResolutionNumber = resolutionNumber,
            Title = $"Document {id}",
            Url = url,
            Status = "candidate",
            RequiresReview = true,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["fileType"] = fileType
            },
            Text = new string('x', 600)
        };

    private static RegulationChunk CreateChunk(string id, string documentId, string article, string text) =>
        new()
        {
            Id = id,
            DocumentId = documentId,
            Article = article,
            ChunkIndex = int.Parse(id.Split('-')[1], System.Globalization.CultureInfo.InvariantCulture),
            Text = text,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
}
