namespace CnvRegulation.Infrastructure.Ingestion;

internal sealed class ParsedRegulationSource
{
    public required string Text { get; init; }

    public required IReadOnlyDictionary<string, string> Metadata { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }

    public required string FileType { get; init; }

    public int? PageCount { get; init; }
}
