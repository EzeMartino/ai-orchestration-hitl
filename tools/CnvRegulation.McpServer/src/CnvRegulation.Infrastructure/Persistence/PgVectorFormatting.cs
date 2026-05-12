using System.Globalization;

namespace CnvRegulation.Infrastructure.Persistence;

/// <summary>
/// Formats vectors for PostgreSQL pgvector casts.
/// </summary>
internal static class PgVectorFormatting
{
    /// <summary>
    /// Formats a float vector as pgvector text.
    /// </summary>
    /// <param name="vector">The vector values.</param>
    /// <returns>A pgvector literal.</returns>
    public static string Format(IReadOnlyList<float> vector)
    {
        ArgumentNullException.ThrowIfNull(vector);

        return $"[{string.Join(",", vector.Select(value => value.ToString("G9", CultureInfo.InvariantCulture)))}]";
    }
}
