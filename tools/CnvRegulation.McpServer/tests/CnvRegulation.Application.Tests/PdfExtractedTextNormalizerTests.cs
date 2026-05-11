using CnvRegulation.Infrastructure.Parsing;
using FluentAssertions;

namespace CnvRegulation.Application.Tests;

public sealed class PdfExtractedTextNormalizerTests
{
    [Fact]
    public void PdfTextNormalizer_ShouldCollapseExcessiveWhitespace()
    {
        var normalizer = new PdfExtractedTextNormalizer();

        var normalized = normalizer.Normalize("ARTICULO     1\r\n\r\n\r\nTexto     con     espacios.");

        normalized.Should().Be($"ARTÍCULO 1{Environment.NewLine}{Environment.NewLine}Texto con espacios.");
    }

    [Fact]
    public void PdfTextNormalizer_ShouldRemovePageNumberOnlyLines()
    {
        var normalizer = new PdfExtractedTextNormalizer();

        var normalized = normalizer.Normalize("ARTICULO 1\n12\nTexto de prueba.\n003\nFinal.");

        normalized.Should().NotContain($"{Environment.NewLine}12{Environment.NewLine}");
        normalized.Should().NotContain($"{Environment.NewLine}003{Environment.NewLine}");
        normalized.Should().Contain("Texto de prueba.");
    }

    [Fact]
    public void PdfTextNormalizer_ShouldFixSimpleHyphenatedLineBreaks()
    {
        var normalizer = new PdfExtractedTextNormalizer();

        var normalized = normalizer.Normalize("El merca-\ndo regulado.");

        normalized.Should().Be("El mercado regulado.");
    }

    [Fact]
    public void PdfTextNormalizer_ShouldRemoveRepeatedHeaderFooterLines()
    {
        var normalizer = new PdfExtractedTextNormalizer();

        var normalized = normalizer.Normalize(
            """
            COMISION NACIONAL DE VALORES
            ARTICULO 1.- Texto uno.
            COMISION NACIONAL DE VALORES
            ARTICULO 2.- Texto dos.
            COMISION NACIONAL DE VALORES
            ARTICULO 3.- Texto tres.
            """);

        normalized.Should().NotContain("COMISION NACIONAL DE VALORES");
        normalized.Should().Contain("ARTÍCULO 1");
        normalized.Should().Contain("ARTÍCULO 3");
    }
}
