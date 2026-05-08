namespace CnvRegulation.Infrastructure.Ingestion;

/// <summary>
/// Parses plain text regulation source files.
/// </summary>
public sealed class PlainTextRegulationParser
{
    /// <summary>
    /// Reads plain text content from a local file.
    /// </summary>
    /// <param name="filePath">The text file path.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The normalized text content.</returns>
    public async Task<string> ParseAsync(string filePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var text = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        return NormalizeWhitespace(text);
    }

    internal static string NormalizeWhitespace(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0);

        return string.Join(Environment.NewLine, lines);
    }
}
