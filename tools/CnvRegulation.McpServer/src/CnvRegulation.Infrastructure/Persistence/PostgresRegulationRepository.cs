using System.Text.Json;
using CnvRegulation.Application.Abstractions;
using CnvRegulation.Domain;
using Dapper;

namespace CnvRegulation.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL-backed repository for regulation documents and citable chunks.
/// </summary>
public sealed class PostgresRegulationRepository(
    RegulationDbConnectionFactory connectionFactory) : IRegulationRepository, IRegulationChunkRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public async Task SaveAsync(RegulationDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        await using var connection = await connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO regulation_documents (
                id,
                source,
                document_type,
                resolution_number,
                title,
                publication_date,
                effective_date,
                url,
                status,
                requires_review,
                retrieved_at,
                text,
                metadata,
                updated_at
            )
            VALUES (
                @Id,
                @Source,
                @DocumentType,
                @ResolutionNumber,
                @Title,
                @PublicationDate,
                @EffectiveDate,
                @Url,
                @Status,
                @RequiresReview,
                @RetrievedAt,
                @Text,
                CAST(@Metadata AS jsonb),
                now()
            )
            ON CONFLICT (id) DO UPDATE SET
                source = EXCLUDED.source,
                document_type = EXCLUDED.document_type,
                resolution_number = EXCLUDED.resolution_number,
                title = EXCLUDED.title,
                publication_date = EXCLUDED.publication_date,
                effective_date = EXCLUDED.effective_date,
                url = EXCLUDED.url,
                status = EXCLUDED.status,
                requires_review = EXCLUDED.requires_review,
                retrieved_at = EXCLUDED.retrieved_at,
                text = EXCLUDED.text,
                metadata = EXCLUDED.metadata,
                updated_at = now();
            """,
            CreateDocumentParameters(document),
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RegulationDocument?> GetByIdAsync(string documentId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(documentId))
        {
            return null;
        }

        await using var connection = await connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var row = await connection.QuerySingleOrDefaultAsync<DocumentRow>(new CommandDefinition(
            """
            SELECT
                id,
                source,
                document_type AS DocumentType,
                resolution_number AS ResolutionNumber,
                title,
                publication_date AS PublicationDate,
                effective_date AS EffectiveDate,
                url,
                status,
                requires_review AS RequiresReview,
                retrieved_at AS RetrievedAt,
                text,
                metadata::text AS Metadata
            FROM regulation_documents
            WHERE id = @DocumentId;
            """,
            new { DocumentId = documentId.Trim() },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return row is null ? null : MapDocument(row);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RegulationDocument>> ListAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = await connection.QueryAsync<DocumentRow>(new CommandDefinition(
            """
            SELECT
                id,
                source,
                document_type AS DocumentType,
                resolution_number AS ResolutionNumber,
                title,
                publication_date AS PublicationDate,
                effective_date AS EffectiveDate,
                url,
                status,
                requires_review AS RequiresReview,
                retrieved_at AS RetrievedAt,
                text,
                metadata::text AS Metadata
            FROM regulation_documents
            ORDER BY id;
            """,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.Select(MapDocument).ToArray();
    }

    /// <inheritdoc />
    public async Task ReplaceForDocumentAsync(
        string documentId,
        IReadOnlyList<RegulationChunk> chunks,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentNullException.ThrowIfNull(chunks);

        await using var connection = await connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM regulation_chunks WHERE document_id = @DocumentId;",
            new { DocumentId = documentId.Trim() },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        foreach (var chunk in chunks)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO regulation_chunks (
                    id,
                    document_id,
                    title,
                    chapter,
                    section,
                    article,
                    chunk_index,
                    text,
                    metadata
                )
                VALUES (
                    @Id,
                    @DocumentId,
                    @Title,
                    @Chapter,
                    @Section,
                    @Article,
                    @ChunkIndex,
                    @Text,
                    CAST(@Metadata AS jsonb)
                );
                """,
                CreateChunkParameters(chunk),
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RegulationChunk>> ListByDocumentIdAsync(
        string documentId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(documentId))
        {
            return [];
        }

        await using var connection = await connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = await connection.QueryAsync<ChunkRow>(new CommandDefinition(
            """
            SELECT
                id,
                document_id AS DocumentId,
                title,
                chapter,
                section,
                article,
                chunk_index AS ChunkIndex,
                text,
                metadata::text AS Metadata
            FROM regulation_chunks
            WHERE document_id = @DocumentId
            ORDER BY chunk_index;
            """,
            new { DocumentId = documentId.Trim() },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.Select(MapChunk).ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RegulationChunk>> ListChunksAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = await connection.QueryAsync<ChunkRow>(new CommandDefinition(
            """
            SELECT
                id,
                document_id AS DocumentId,
                title,
                chapter,
                section,
                article,
                chunk_index AS ChunkIndex,
                text,
                metadata::text AS Metadata
            FROM regulation_chunks
            ORDER BY document_id, chunk_index;
            """,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.Select(MapChunk).ToArray();
    }

    private static object CreateDocumentParameters(RegulationDocument document) =>
        new
        {
            document.Id,
            document.Source,
            document.DocumentType,
            document.ResolutionNumber,
            document.Title,
            PublicationDate = ToDateTime(document.PublicationDate),
            EffectiveDate = ToDateTime(document.EffectiveDate),
            document.Url,
            document.Status,
            document.RequiresReview,
            document.RetrievedAt,
            document.Text,
            Metadata = SerializeMetadata(document.Metadata)
        };

    private static object CreateChunkParameters(RegulationChunk chunk) =>
        new
        {
            chunk.Id,
            chunk.DocumentId,
            chunk.Title,
            chunk.Chapter,
            chunk.Section,
            chunk.Article,
            chunk.ChunkIndex,
            chunk.Text,
            Metadata = SerializeMetadata(chunk.Metadata)
        };

    private static RegulationDocument MapDocument(DocumentRow row) =>
        new()
        {
            Id = row.Id,
            Source = row.Source,
            DocumentType = row.DocumentType,
            ResolutionNumber = row.ResolutionNumber,
            Title = row.Title,
            PublicationDate = row.PublicationDate,
            EffectiveDate = row.EffectiveDate,
            Url = row.Url,
            Status = row.Status,
            RequiresReview = row.RequiresReview,
            RetrievedAt = row.RetrievedAt,
            Text = row.Text,
            Metadata = DeserializeMetadata(row.Metadata)
        };

    private static RegulationChunk MapChunk(ChunkRow row) =>
        new()
        {
            Id = row.Id,
            DocumentId = row.DocumentId,
            Title = row.Title,
            Chapter = row.Chapter,
            Section = row.Section,
            Article = row.Article,
            ChunkIndex = row.ChunkIndex,
            Text = row.Text,
            Metadata = DeserializeMetadata(row.Metadata)
        };

    private static string SerializeMetadata(IReadOnlyDictionary<string, string> metadata) =>
        JsonSerializer.Serialize(metadata, JsonOptions);

    private static DateTime? ToDateTime(DateOnly? value) =>
        value?.ToDateTime(TimeOnly.MinValue);

    private static IReadOnlyDictionary<string, string> DeserializeMetadata(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return JsonSerializer.Deserialize<Dictionary<string, string>>(metadata, JsonOptions)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class DocumentRow
    {
        public required string Id { get; init; }

        public required string Source { get; init; }

        public required string DocumentType { get; init; }

        public string? ResolutionNumber { get; init; }

        public required string Title { get; init; }

        public DateOnly? PublicationDate { get; init; }

        public DateOnly? EffectiveDate { get; init; }

        public required string Url { get; init; }

        public required string Status { get; init; }

        public bool RequiresReview { get; init; }

        public DateTimeOffset? RetrievedAt { get; init; }

        public required string Text { get; init; }

        public string? Metadata { get; init; }
    }

    private sealed class ChunkRow
    {
        public required string Id { get; init; }

        public required string DocumentId { get; init; }

        public string? Title { get; init; }

        public string? Chapter { get; init; }

        public string? Section { get; init; }

        public string? Article { get; init; }

        public int ChunkIndex { get; init; }

        public required string Text { get; init; }

        public string? Metadata { get; init; }
    }
}
