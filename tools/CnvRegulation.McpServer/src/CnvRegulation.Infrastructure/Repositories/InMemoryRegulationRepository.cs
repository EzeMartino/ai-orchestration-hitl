using System.Collections.Concurrent;
using CnvRegulation.Application.Abstractions;
using CnvRegulation.Domain;

namespace CnvRegulation.Infrastructure.Repositories;

/// <summary>
/// In-memory repository for locally ingested regulation documents.
/// </summary>
public sealed class InMemoryRegulationRepository : IRegulationRepository
{
    private readonly ConcurrentDictionary<string, RegulationDocument> documents = new(StringComparer.OrdinalIgnoreCase);

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
}
