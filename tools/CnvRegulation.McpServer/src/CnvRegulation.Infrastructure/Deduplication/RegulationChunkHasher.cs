using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CnvRegulation.Application.Abstractions;

namespace CnvRegulation.Infrastructure.Deduplication;

/// <summary>
/// Computes stable hashes for regulatory chunk text after light normalization.
/// </summary>
public sealed partial class RegulationChunkHasher : IRegulationChunkHasher
{
    /// <inheritdoc />
    public string? ComputeHash(string text)
    {
        var normalized = Normalize(text);
        if (normalized.Length == 0)
        {
            return null;
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var withoutAccents = RemoveDiacritics(text.ToLowerInvariant());
        var withoutNoise = PageNoiseRegex().Replace(withoutAccents, " ");
        return WhitespaceRegex().Replace(withoutNoise, " ").Trim();
    }

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    [GeneratedRegex(@"\b(pagina|page)\s+\d+\b|\b\d+\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PageNoiseRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();
}
