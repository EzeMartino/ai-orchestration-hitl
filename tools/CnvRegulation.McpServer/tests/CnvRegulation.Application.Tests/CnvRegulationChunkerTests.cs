using CnvRegulation.Domain;
using CnvRegulation.Infrastructure.Chunking;
using FluentAssertions;

namespace CnvRegulation.Application.Tests;

public sealed class LegalStructureRegulationChunkerTests
{
    [Fact]
    public async Task Chunker_ShouldSplitTextByArticleBoundaries()
    {
        var chunker = new LegalStructureRegulationChunker(new LegalStructureDetector());

        var chunks = await chunker.ChunkAsync(CreateDocument(), CancellationToken.None);

        chunks.Should().HaveCount(3);
        chunks.Select(chunk => chunk.Article).Should().ContainInOrder("Artículo 1", "Artículo 2", "Artículo 3");
        chunks.Select(chunk => chunk.Id).Should().ContainInOrder(
            "cnv-nt-2013-sample-articulo-1",
            "cnv-nt-2013-sample-articulo-2",
            "cnv-nt-2013-sample-articulo-3");
    }

    [Fact]
    public async Task Chunker_ShouldPreserveCurrentTitleChapterAndSection()
    {
        var chunker = new LegalStructureRegulationChunker(new LegalStructureDetector());

        var chunks = await chunker.ChunkAsync(CreateDocument(), CancellationToken.None);

        chunks[0].Title.Should().Be("Título I");
        chunks[0].Chapter.Should().Be("Capítulo I");
        chunks[0].Article.Should().Be("Artículo 1");
        chunks[1].Chapter.Should().Be("Capítulo I");
        chunks[2].Chapter.Should().Be("Capítulo II");
        chunks[2].Metadata["documentId"].Should().Be("cnv-nt-2013-sample");
        chunks[2].Metadata["article"].Should().Be("Artículo 3");
    }

    public static RegulationDocument CreateDocument() =>
        new()
        {
            Id = "cnv-nt-2013-sample",
            Source = "CNV",
            DocumentType = "Texto Ordenado",
            ResolutionNumber = "622/2013",
            Title = "Normas CNV N.T. 2013 - Sample",
            PublicationDate = new DateOnly(2013, 9, 9),
            EffectiveDate = new DateOnly(2013, 9, 9),
            Url = "https://www.cnv.gov.ar/",
            Status = "mock",
            Text = SampleText
        };

    public const string SampleText = """
        TÍTULO I
        DISPOSICIONES GENERALES

        CAPÍTULO I
        ÁMBITO DE APLICACIÓN

        ARTÍCULO 1°.- Este es el primer artículo de prueba.

        ARTÍCULO 2°.- Este es el segundo artículo de prueba.

        CAPÍTULO II
        OTRAS DISPOSICIONES

        ARTÍCULO 3°.- Este es el tercer artículo de prueba.
        """;
}
