using CnvRegulation.Application.Contracts;
using CnvRegulation.Domain;
using CnvRegulation.Infrastructure.Diagnostics;
using CnvRegulation.Infrastructure.Repositories;
using FluentAssertions;

namespace CnvRegulation.Application.Tests;

public sealed class ChunkQualityInspectionServiceTests
{
    [Fact]
    public async Task ChunkQualityInspector_ShouldDetectVeryShortChunks()
    {
        var service = await CreateServiceAsync([CreateChunk("short", "tiny")]);

        var report = await service.InspectAsync(new InspectChunksRequest(), CancellationToken.None);

        report.VeryShortChunks.Should().Be(1);
        report.Warnings.Should().Contain(warning => warning.WarningType == "very_short");
    }

    [Fact]
    public async Task ChunkQualityInspector_ShouldDetectVeryLongChunks()
    {
        var service = await CreateServiceAsync([CreateChunk("long", new string('a', 10_001))]);

        var report = await service.InspectAsync(new InspectChunksRequest(), CancellationToken.None);

        report.VeryLongChunks.Should().Be(1);
        report.Warnings.Should().Contain(warning => warning.WarningType == "very_long");
    }

    [Fact]
    public async Task ChunkQualityInspector_ShouldDetectChunksWithoutArticle()
    {
        var chunk = CreateChunk("missing-article", "Texto suficiente para no ser corto y aislar el warning.", article: null);
        var service = await CreateServiceAsync([chunk]);

        var report = await service.InspectAsync(new InspectChunksRequest(), CancellationToken.None);

        report.ChunksWithoutArticle.Should().Be(1);
        report.Warnings.Should().Contain(warning => warning.WarningType == "missing_article");
    }

    [Fact]
    public async Task ChunkQualityInspector_ShouldDetectDuplicateChunks()
    {
        var text = "ARTICULO 1.- Texto duplicado suficientemente largo para diagnostico.";
        var service = await CreateServiceAsync([
            CreateChunk("duplicate-1", text, article: "Artículo 1"),
            CreateChunk("duplicate-2", text, article: "Artículo 2")
        ]);

        var report = await service.InspectAsync(new InspectChunksRequest(), CancellationToken.None);

        report.PossibleDuplicateChunks.Should().Be(2);
        report.Warnings.Should().Contain(warning => warning.WarningType == "possible_duplicate");
    }

    [Fact]
    public async Task ChunkQualityInspector_ShouldDetectRepeatedHeaderFooterPollution()
    {
        var chunks = Enumerable
            .Range(1, 5)
            .Select(index => CreateChunk(
                $"header-{index}",
                $"CNV HEADER REPETIDO{Environment.NewLine}ARTICULO {index}.- Texto suficiente para diagnostico.",
                article: $"Artículo {index}"))
            .ToArray();
        var service = await CreateServiceAsync(chunks);

        var report = await service.InspectAsync(new InspectChunksRequest(), CancellationToken.None);

        report.SuspiciousHeaderFooterPollution.Should().Be(5);
        report.Warnings.Should().Contain(warning => warning.WarningType == "repeated_header_footer");
    }

    private static async Task<ChunkQualityInspectionService> CreateServiceAsync(IReadOnlyList<RegulationChunk> chunks)
    {
        var repository = new InMemoryRegulationRepository();
        var document = CreateDocument();

        await repository.SaveAsync(document, CancellationToken.None);
        await repository.ReplaceForDocumentAsync(document.Id, chunks, CancellationToken.None);

        return new ChunkQualityInspectionService(repository, repository);
    }

    private static RegulationDocument CreateDocument() =>
        new()
        {
            Id = "test-document",
            Source = "CNV",
            DocumentType = "Texto Ordenado",
            Title = "Test Document",
            Url = "https://example.test",
            Status = "candidate",
            Text = "Document text."
        };

    private static RegulationChunk CreateChunk(
        string id,
        string text,
        string? article = "Artículo 1") =>
        new()
        {
            Id = id,
            DocumentId = "test-document",
            Article = article,
            ChunkIndex = 0,
            Text = text,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
}
