using System.Text;
using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace CnvRegulation.Infrastructure.Parsing;

/// <summary>
/// Extracts text from PDF files using PdfPig.
/// </summary>
public sealed class PdfPigTextExtractor : IPdfTextExtractor
{
    private const double LineTolerance = 2.5;

    /// <inheritdoc />
    public Task<PdfExtractionResult> ExtractAsync(string filePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"PDF source file was not found: {filePath}", filePath);
        }

        var pages = new List<PdfPageText>();
        var warnings = new List<string>();

        using var document = PdfDocument.Open(filePath);
        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var text = ExtractPageText(page);
            if (string.IsNullOrWhiteSpace(text))
            {
                warnings.Add($"page {page.Number} produced no text");
            }

            pages.Add(new PdfPageText
            {
                PageNumber = page.Number,
                Text = text
            });
        }

        var fullText = string.Join(
            Environment.NewLine + Environment.NewLine,
            pages.Select(page => page.Text).Where(text => !string.IsNullOrWhiteSpace(text)));

        if (string.IsNullOrWhiteSpace(fullText))
        {
            warnings.Add("PDF extraction produced no text.");
        }

        return Task.FromResult(new PdfExtractionResult
        {
            Text = fullText,
            Pages = pages,
            Warnings = warnings
        });
    }

    private static string ExtractPageText(Page page)
    {
        var words = page.GetWords().ToArray();
        if (words.Length == 0)
        {
            return page.Text ?? string.Empty;
        }

        var lines = new List<List<Word>>();
        foreach (var word in words
            .OrderByDescending(word => word.BoundingBox.Bottom)
            .ThenBy(word => word.BoundingBox.Left))
        {
            var line = lines.FirstOrDefault(candidate =>
                Math.Abs(candidate[0].BoundingBox.Bottom - word.BoundingBox.Bottom) <= LineTolerance);

            if (line is null)
            {
                lines.Add([word]);
                continue;
            }

            line.Add(word);
        }

        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(string.Join(
                ' ',
                line.OrderBy(word => word.BoundingBox.Left).Select(word => word.Text)));
        }

        return builder.ToString();
    }
}
