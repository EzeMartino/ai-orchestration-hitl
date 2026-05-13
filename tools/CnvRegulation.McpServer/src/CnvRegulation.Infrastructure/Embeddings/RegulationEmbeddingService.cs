using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;
using CnvRegulation.Domain;
using CnvRegulation.Infrastructure.Search;

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
                var embedding = await generator.GenerateAsync(chunk.Text, cancellationToken).ConfigureAwait(false);
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
                if (warnings.Count < 10)
                {
                    warnings.Add($"Failed to generate embedding for chunk '{chunk.Id}': {exception.Message}");
                }
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
            Model = ResolveModel(provider, request.Model),
            Dimensions = dimensions,
            EstimatedTokenCount = estimatedTokenCount,
            ActualTokenCount = hasActualTokenCount ? actualTokenCount : null,
            Warnings = warnings
        };
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
                    Model = string.IsNullOrWhiteSpace(request.Model) ? options.Model : request.Model.Trim(),
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
            ? options.Model
            : "fake-deterministic";
    }

    private static int EstimateTokenCount(string text) =>
        Math.Max(1, (int)Math.Ceiling(text.Length / 4.0));
}
