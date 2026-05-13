using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;
using CnvRegulation.Domain;
using CnvRegulation.Infrastructure.Search;
using System.Net;
using System.Text.Json;

namespace CnvRegulation.Infrastructure.Embeddings;

/// <summary>
/// Generates and persists missing regulation chunk embeddings.
/// </summary>
public sealed class RegulationEmbeddingService(
    IRegulationRepository documentRepository,
    IRegulationChunkRepository chunkRepository,
    IRegulationEmbeddingRepository embeddingRepository,
    DeterministicFakeEmbeddingGenerator fakeGenerator,
    OpenAiEmbeddingGenerator openAiGenerator,
    EmbeddingOptions options,
    HttpClient httpClient,
    TimeProvider timeProvider) : IRegulationEmbeddingService
{
    private const int MaxTransientRetries = 2;

    private static readonly JsonSerializerOptions ReportJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    /// <inheritdoc />
    public async Task<GenerateEmbeddingsResponse> GenerateMissingEmbeddingsAsync(
        GenerateEmbeddingsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var provider = string.IsNullOrWhiteSpace(request.Provider) ? options.Provider : request.Provider.Trim();
        var dimensions = request.Dimensions ?? options.Dimensions;
        var generator = ResolveGenerator(provider, request);
        var documents = await documentRepository.ListAsync(cancellationToken).ConfigureAwait(false);
        var documentLookup = documents.ToDictionary(document => document.Id, StringComparer.OrdinalIgnoreCase);
        var chunks = await chunkRepository.ListChunksAsync(cancellationToken).ConfigureAwait(false);
        var warnings = new List<string>();
        var generated = 0;
        var missing = 0;
        var eligible = 0;
        var alreadyEmbedded = 0;
        var failed = 0;
        var skippedDuplicates = 0;
        var skippedNonSearchable = 0;
        var skippedEmpty = 0;
        var estimatedTokenCount = 0;
        var actualTokenCount = 0;
        var hasActualTokenCount = false;
        var batchSize = request.BatchSize <= 0 ? 32 : request.BatchSize;
        var delayMs = Math.Max(0, request.DelayMs);
        var maxInputCharacters = request.MaxInputCharacters <= 0 ? 24_000 : request.MaxInputCharacters;
        var failedItems = new List<EmbeddingFailureItem>();

        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!documentLookup.TryGetValue(chunk.DocumentId, out var document))
            {
                continue;
            }

            if (chunk.Embedding is null || chunk.Embedding.Count == 0)
            {
                missing++;
            }
            else
            {
                alreadyEmbedded++;
                continue;
            }

            if (!request.IncludeDuplicates && !string.IsNullOrWhiteSpace(chunk.DuplicateOfChunkId))
            {
                skippedDuplicates++;
                continue;
            }

            if (!request.IncludeNonSearchable && SourcePriorityScorer.IsNonSearchable(document))
            {
                skippedNonSearchable++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(chunk.Text))
            {
                skippedEmpty++;
                continue;
            }

            eligible++;
            estimatedTokenCount += EstimateTokenCount(chunk.Text);

            if (chunk.Text.Length > maxInputCharacters)
            {
                var reason = $"Chunk length {chunk.Text.Length} exceeds max input length {maxInputCharacters}.";
                failed++;
                if (!request.DryRun)
                {
                    await embeddingRepository.UpdateChunkEmbeddingStatusAsync(
                        chunk.Id,
                        "skipped_too_long",
                        reason,
                        timeProvider.GetUtcNow(),
                        cancellationToken).ConfigureAwait(false);
                }

                failedItems.Add(CreateFailureItem(
                    document,
                    chunk,
                    status: "skipped_too_long",
                    reason: reason,
                    estimatedTokens: EstimateTokenCount(chunk.Text)));
                AddWarning(
                    warnings,
                    $"Skipped embedding for chunk '{chunk.Id}' because it exceeds max input length {maxInputCharacters}.");
                continue;
            }

            if (request.Limit is > 0 && generated + failed >= request.Limit.Value)
            {
                continue;
            }

            if (request.DryRun)
            {
                continue;
            }

            try
            {
                var embedding = await GenerateWithTransientRetryAsync(generator, chunk.Text, delayMs, cancellationToken)
                    .ConfigureAwait(false);
                if (embedding.Dimensions != dimensions)
                {
                    throw new InvalidOperationException(
                        $"Embedding dimensions mismatch. Expected {dimensions}, got {embedding.Dimensions} from {embedding.Model}.");
                }

                await embeddingRepository.UpdateChunkEmbeddingAsync(
                    chunk.Id,
                    embedding.Vector,
                    string.IsNullOrWhiteSpace(request.Model) ? embedding.Model : request.Model.Trim(),
                    timeProvider.GetUtcNow(),
                    cancellationToken).ConfigureAwait(false);

                generated++;
                if (embedding.TokenCount is > 0)
                {
                    actualTokenCount += embedding.TokenCount.Value;
                    hasActualTokenCount = true;
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failed++;
                await embeddingRepository.UpdateChunkEmbeddingStatusAsync(
                    chunk.Id,
                    "failed",
                    exception.Message,
                    timeProvider.GetUtcNow(),
                    cancellationToken).ConfigureAwait(false);
                failedItems.Add(CreateFailureItem(
                    document,
                    chunk,
                    status: "failed",
                    reason: exception.Message,
                    estimatedTokens: EstimateTokenCount(chunk.Text)));
                AddWarning(warnings, $"Failed to generate embedding for chunk '{chunk.Id}': {exception.Message}");
            }

            if (delayMs > 0 && generated > 0 && generated % batchSize == 0)
            {
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }
        }

        if (request.DryRun)
        {
            warnings.Add("Dry run only. No embeddings were generated or persisted.");
        }

        if (request.OnlyMissing)
        {
            warnings.Add("Resume mode: chunks with existing embeddings were skipped.");
        }

        if (!provider.Equals("Fake", StringComparison.OrdinalIgnoreCase)
            && !provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"Unknown provider '{provider}' was not used.");
        }

        var model = ResolveModel(provider, request.Model);
        var failedReportPath = await WriteFailedReportAsync(
            request.FailedReportPath,
            provider,
            model,
            failedItems,
            cancellationToken).ConfigureAwait(false);

        return new GenerateEmbeddingsResponse
        {
            ChunksScanned = chunks.Count,
            MissingEmbeddings = missing,
            EligibleChunks = eligible,
            AlreadyEmbedded = alreadyEmbedded,
            Generated = generated,
            Failed = failed,
            SkippedDuplicates = skippedDuplicates,
            SkippedNonSearchable = skippedNonSearchable,
            SkippedEmpty = skippedEmpty,
            Provider = provider,
            Model = model,
            Dimensions = dimensions,
            EstimatedTokenCount = estimatedTokenCount,
            ActualTokenCount = hasActualTokenCount ? actualTokenCount : null,
            Warnings = warnings,
            FailedItems = failedItems,
            FailedReportPath = failedReportPath
        };
    }

    private async Task<EmbeddingResult> GenerateWithTransientRetryAsync(
        IEmbeddingGenerator generator,
        string text,
        int delayMs,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await generator.GenerateAsync(text, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException exception) when (IsTransient(exception.StatusCode) && attempt < MaxTransientRetries)
            {
                var retryDelay = delayMs > 0 ? delayMs : 250 * (attempt + 1);
                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private IEmbeddingGenerator ResolveGenerator(string provider, GenerateEmbeddingsRequest request)
    {
        if (provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.Model) && request.Dimensions is null)
            {
                return openAiGenerator;
            }

            return new OpenAiEmbeddingGenerator(
                httpClient,
                new EmbeddingOptions
                {
                    Enabled = options.Enabled,
                    Provider = options.Provider,
                    Model = ResolveOpenAiModel(request.Model),
                    Dimensions = request.Dimensions ?? options.Dimensions,
                    OpenAiApiKey = options.OpenAiApiKey,
                    Endpoint = options.Endpoint
                });
        }

        return fakeGenerator;
    }

    private string ResolveModel(string provider, string? requestedModel)
    {
        if (!string.IsNullOrWhiteSpace(requestedModel))
        {
            return requestedModel.Trim();
        }

        return provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase)
            ? ResolveOpenAiModel(requestedModel: null)
            : "fake-deterministic";
    }

    private string ResolveOpenAiModel(string? requestedModel)
    {
        if (!string.IsNullOrWhiteSpace(requestedModel))
        {
            return requestedModel.Trim();
        }

        return string.IsNullOrWhiteSpace(options.Model)
            || options.Model.Equals("fake-deterministic", StringComparison.OrdinalIgnoreCase)
            ? "text-embedding-3-small"
            : options.Model;
    }

    private static int EstimateTokenCount(string text) =>
        Math.Max(1, (int)Math.Ceiling(text.Length / 4.0));

    private static bool IsTransient(HttpStatusCode? statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private static EmbeddingFailureItem CreateFailureItem(
        RegulationDocument document,
        RegulationChunk chunk,
        string status,
        string reason,
        int estimatedTokens) =>
        new()
        {
            DocumentId = document.Id,
            ChunkId = chunk.Id,
            Source = document.Source,
            Title = document.Title,
            Article = chunk.Article,
            Status = status,
            Reason = reason,
            TextLength = chunk.Text.Length,
            EstimatedTokens = estimatedTokens
        };

    private static void AddWarning(List<string> warnings, string warning)
    {
        if (warnings.Count < 10)
        {
            warnings.Add(warning);
        }
    }

    private async Task<string?> WriteFailedReportAsync(
        string? failedReportPath,
        string provider,
        string model,
        IReadOnlyList<EmbeddingFailureItem> failedItems,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(failedReportPath))
        {
            return null;
        }

        var path = Path.GetFullPath(failedReportPath.Trim());
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var report = new EmbeddingFailureReport
        {
            GeneratedAt = timeProvider.GetUtcNow(),
            Provider = provider,
            Model = model,
            Failed = failedItems.Count,
            Items = failedItems
        };

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, report, ReportJsonOptions, cancellationToken).ConfigureAwait(false);

        return path;
    }
}
