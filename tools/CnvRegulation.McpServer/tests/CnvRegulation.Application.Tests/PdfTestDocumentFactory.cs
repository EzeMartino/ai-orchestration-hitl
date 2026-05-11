using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace CnvRegulation.Application.Tests;

internal static class PdfTestDocumentFactory
{
    public static async Task WriteSampleRegulationPdfAsync(string filePath)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(595, 842);

        page.AddText("TITULO I", 12, new PdfPoint(50, 760), font);
        page.AddText("CAPITULO I", 12, new PdfPoint(50, 740), font);
        page.AddText("ARTICULO 1.- Texto de prueba PDF.", 12, new PdfPoint(50, 720), font);
        page.AddText("ARTICULO 2.- Otro texto de prueba PDF.", 12, new PdfPoint(50, 700), font);

        await File.WriteAllBytesAsync(filePath, builder.Build());
    }
}
