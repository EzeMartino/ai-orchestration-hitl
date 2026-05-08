namespace CnvRegulation.Infrastructure.Sources;

/// <summary>
/// Sanitizes manifest-provided file names before writing files locally.
/// </summary>
public static class SourceFileNameSanitizer
{
    /// <summary>
    /// Returns a file name that cannot escape the target directory.
    /// </summary>
    /// <param name="fileName">The proposed file name.</param>
    /// <param name="fallbackFileName">The file name to use when the proposed value is empty or unsafe.</param>
    /// <returns>A safe file name without path separators.</returns>
    public static string Sanitize(string? fileName, string fallbackFileName)
    {
        var candidate = string.IsNullOrWhiteSpace(fileName) ? fallbackFileName : fileName.Trim();
        candidate = candidate
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault() ?? fallbackFileName;

        var sanitized = new string(candidate
            .Select(character => IsSafeCharacter(character) ? character : '-')
            .ToArray());

        while (sanitized.Contains("..", StringComparison.Ordinal))
        {
            sanitized = sanitized.Replace("..", ".", StringComparison.Ordinal);
        }

        sanitized = sanitized.Trim('.', ' ');

        return string.IsNullOrWhiteSpace(sanitized) ? fallbackFileName : sanitized;
    }

    private static bool IsSafeCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_';
}
