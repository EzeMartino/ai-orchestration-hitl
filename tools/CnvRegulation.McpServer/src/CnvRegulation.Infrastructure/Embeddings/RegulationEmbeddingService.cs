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
        var skippedDuplicates = 0;
        var skippedNonSearchable = 0;
        var skippedEmpty = 0;

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

            if (request.Limit is > 0 && generated >= request.Limit.Value)
            {
                break;
            }
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
            Generated = generated,
            SkippedDuplicates = skippedDuplicates,
            SkippedNonSearchable = skippedNonSearchable,
            SkippedEmpty = skippedEmpty,
            Provider = provider,
            Model = ResolveModel(provider, request.Model),
            Dimensions = dimensions,
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
}
