using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;

namespace CnvRegulation.Infrastructure.Embeddings;

/// <summary>
/// OpenAI embeddings provider. It is disabled unless explicitly selected.
/// </summary>
public sealed class OpenAiEmbeddingGenerator(HttpClient httpClient, EmbeddingOptions options) : IEmbeddingGenerator
{
    /// <inheritdoc />
    public async Task<EmbeddingResult> GenerateAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.OpenAiApiKey))
        {
            throw new InvalidOperationException("OpenAI embeddings require CNV_REGULATION_OPENAI_API_KEY or OPENAI_API_KEY.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, options.Endpoint)
        {
            Content = JsonContent.Create(new OpenAiEmbeddingRequest(options.Model, text, options.Dimensions))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.OpenAiApiKey);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                $"OpenAI embeddings request failed ({(int)response.StatusCode} {response.ReasonPhrase}): {Truncate(errorBody)}",
                inner: null,
                response.StatusCode);
        }

        var payload = await response.Content
            .ReadFromJsonAsync<OpenAiEmbeddingResponse>(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("OpenAI embeddings response was empty.");
        var vector = payload.Data.FirstOrDefault()?.Embedding
            ?? throw new InvalidOperationException("OpenAI embeddings response did not include a vector.");

        return new EmbeddingResult
        {
            Vector = vector,
            Model = payload.Model ?? options.Model,
            Dimensions = vector.Length,
            TokenCount = payload.Usage?.TotalTokens
        };
    }

    private static string Truncate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "(empty response)";
        }

        const int maxLength = 500;
        return value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength), "...");
    }

    private sealed record OpenAiEmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] string Input,
        [property: JsonPropertyName("dimensions")] int Dimensions);

    private sealed class OpenAiEmbeddingResponse
    {
        [JsonPropertyName("model")]
        public string? Model { get; init; }

        [JsonPropertyName("data")]
        public IReadOnlyList<OpenAiEmbeddingData> Data { get; init; } = [];

        [JsonPropertyName("usage")]
        public OpenAiUsage? Usage { get; init; }
    }

    private sealed class OpenAiEmbeddingData
    {
        [JsonPropertyName("embedding")]
        public float[] Embedding { get; init; } = [];
    }

    private sealed class OpenAiUsage
    {
        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; init; }
    }
}
