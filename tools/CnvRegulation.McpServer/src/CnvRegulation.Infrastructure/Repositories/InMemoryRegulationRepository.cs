using System.Collections.Concurrent;
using CnvRegulation.Application.Abstractions;
using CnvRegulation.Domain;

namespace CnvRegulation.Infrastructure.Repositories;

/// <summary>
/// In-memory repository for locally ingested regulation documents.
/// </summary>
public sealed class InMemoryRegulationRepository : IRegulationRepository, IRegulationChunkRepository, IRegulationEmbeddingRepository
{
    private readonly ConcurrentDictionary<string, RegulationDocument> documents = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RegulationChunk> chunks = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public Task SaveAsync(RegulationDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        documents[document.Id] = document;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<RegulationDocument?> GetByIdAsync(string documentId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(documentId))
        {
            return Task.FromResult<RegulationDocument?>(null);
        }

        documents.TryGetValue(documentId.Trim(), out var document);
        return Task.FromResult(document);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RegulationDocument>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<RegulationDocument> snapshot = documents.Values
            .OrderBy(document => document.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult(snapshot);
    }

    /// <inheritdoc />
    public Task ReplaceForDocumentAsync(
        string documentId,
        IReadOnlyList<RegulationChunk> documentChunks,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentNullException.ThrowIfNull(documentChunks);
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var chunk in chunks.Values
            .Where(chunk => string.Equals(chunk.DocumentId, documentId, StringComparison.OrdinalIgnoreCase))
            .ToArray())
        {
            chunks.TryRemove(chunk.Id, out _);
        }

        foreach (var chunk in documentChunks)
        {
            chunks[chunk.Id] = chunk;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RegulationChunk>> ListByDocumentIdAsync(
        string documentId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(documentId))
        {
            return Task.FromResult<IReadOnlyList<RegulationChunk>>([]);
        }

        IReadOnlyList<RegulationChunk> snapshot = chunks.Values
            .Where(chunk => string.Equals(chunk.DocumentId, documentId.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(chunk => chunk.ChunkIndex)
            .ToArray();

        return Task.FromResult(snapshot);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RegulationChunk>> ListChunksAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<RegulationChunk> snapshot = chunks.Values
            .OrderBy(chunk => chunk.DocumentId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(chunk => chunk.ChunkIndex)
            .ToArray();

        return Task.FromResult(snapshot);
    }

    /// <inheritdoc />
    public Task UpdateChunkEmbeddingAsync(
        string chunkId,
        IReadOnlyList<float> vector,
        string model,
        DateTimeOffset generatedAt,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chunkId);
        ArgumentNullException.ThrowIfNull(vector);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        cancellationToken.ThrowIfCancellationRequested();

        if (chunks.TryGetValue(chunkId.Trim(), out var chunk))
        {
            chunks[chunk.Id] = new RegulationChunk
            {
                Id = chunk.Id,
                DocumentId = chunk.DocumentId,
                Title = chunk.Title,
                Chapter = chunk.Chapter,
                Section = chunk.Section,
                Article = chunk.Article,
                ChunkIndex = chunk.ChunkIndex,
                Text = chunk.Text,
                ContentHash = chunk.ContentHash,
                DuplicateOfChunkId = chunk.DuplicateOfChunkId,
                Embedding = vector.ToArray(),
                EmbeddingModel = model,
                EmbeddingGeneratedAt = generatedAt,
                Metadata = chunk.Metadata
            };
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpdateChunkEmbeddingStatusAsync(
        string chunkId,
        string status,
        string? reason,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chunkId);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        cancellationToken.ThrowIfCancellationRequested();

        if (chunks.TryGetValue(chunkId.Trim(), out var chunk))
        {
            var metadata = new Dictionary<string, string>(chunk.Metadata, StringComparer.OrdinalIgnoreCase)
            {
                ["embeddingStatus"] = status.Trim(),
                ["embeddingStatusUpdatedAt"] = updatedAt.ToString("O")
            };

            if (!string.IsNullOrWhiteSpace(reason))
            {
                metadata["embeddingFailureReason"] = reason.Trim();
            }

            chunks[chunk.Id] = new RegulationChunk
            {
                Id = chunk.Id,
                DocumentId = chunk.DocumentId,
                Title = chunk.Title,
                Chapter = chunk.Chapter,
                Section = chunk.Section,
                Article = chunk.Article,
                ChunkIndex = chunk.ChunkIndex,
                Text = chunk.Text,
                ContentHash = chunk.ContentHash,
                DuplicateOfChunkId = chunk.DuplicateOfChunkId,
                Embedding = chunk.Embedding,
                EmbeddingModel = chunk.EmbeddingModel,
                EmbeddingGeneratedAt = chunk.EmbeddingGeneratedAt,
                Metadata = metadata
            };
        }

        return Task.CompletedTask;
    }
}
