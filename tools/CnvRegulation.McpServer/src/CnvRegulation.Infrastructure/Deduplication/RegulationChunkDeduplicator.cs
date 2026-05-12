using CnvRegulation.Application.Abstractions;
using CnvRegulation.Domain;

namespace CnvRegulation.Infrastructure.Deduplication;

/// <summary>
/// Adds content hashes and duplicate markers to chunks.
/// </summary>
public sealed class RegulationChunkDeduplicator(IRegulationChunkHasher hasher)
{
    /// <summary>
    /// Annotates chunks with hash and duplicate metadata.
    /// </summary>
    /// <param name="chunks">Chunks to annotate.</param>
    /// <param name="existingChunks">Previously stored chunks from other documents.</param>
    /// <returns>Annotated chunks.</returns>
    public IReadOnlyList<RegulationChunk> Annotate(
        IReadOnlyList<RegulationChunk> chunks,
        IReadOnlyList<RegulationChunk> existingChunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(existingChunks);

        var canonicalByHash = existingChunks
            .Select(chunk => new { Chunk = chunk, Hash = chunk.ContentHash ?? hasher.ComputeHash(chunk.Text) })
            .Where(item => !string.IsNullOrWhiteSpace(item.Hash))
            .GroupBy(item => item.Hash!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Chunk.Id, StringComparer.OrdinalIgnoreCase);

        var result = new List<RegulationChunk>(chunks.Count);
        foreach (var chunk in chunks)
        {
            var hash = hasher.ComputeHash(chunk.Text);
            var duplicateOfChunkId = hash is not null && canonicalByHash.TryGetValue(hash, out var existingChunkId)
                ? existingChunkId
                : null;

            if (hash is not null && duplicateOfChunkId is null)
            {
                canonicalByHash[hash] = chunk.Id;
            }

            result.Add(CopyWithDeduplication(chunk, hash, duplicateOfChunkId));
        }

        return result;
    }

    private static RegulationChunk CopyWithDeduplication(
        RegulationChunk chunk,
        string? contentHash,
        string? duplicateOfChunkId)
    {
        var metadata = new Dictionary<string, string>(chunk.Metadata, StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(contentHash))
        {
            metadata["contentHash"] = contentHash;
        }

        if (!string.IsNullOrWhiteSpace(duplicateOfChunkId))
        {
            metadata["duplicate"] = "true";
            metadata["duplicateOfChunkId"] = duplicateOfChunkId;
        }
        else
        {
            metadata["duplicate"] = "false";
        }

        return new RegulationChunk
        {
            Id = chunk.Id,
            DocumentId = chunk.DocumentId,
            Title = chunk.Title,
            Chapter = chunk.Chapter,
            Section = chunk.Section,
            Article = chunk.Article,
            ChunkIndex = chunk.ChunkIndex,
            Text = chunk.Text,
            ContentHash = contentHash,
            DuplicateOfChunkId = duplicateOfChunkId,
            Metadata = metadata
        };
    }
}
