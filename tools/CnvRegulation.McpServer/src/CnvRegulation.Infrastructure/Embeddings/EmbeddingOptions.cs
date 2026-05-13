namespace CnvRegulation.Infrastructure.Embeddings;

/// <summary>
/// Embedding configuration.
/// </summary>
public sealed class EmbeddingOptions
{
    /// <summary>
    /// Gets whether semantic search is enabled by configuration.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Gets the configured provider.
    /// </summary>
    public string Provider { get; init; } = "Fake";

    /// <summary>
    /// Gets the configured model.
    /// </summary>
    public string Model { get; init; } = "text-embedding-3-small";

    /// <summary>
    /// Gets the vector dimensions.
    /// </summary>
    public int Dimensions { get; init; } = 1536;

    /// <summary>
    /// Gets the OpenAI API key when configured.
    /// </summary>
    public string? OpenAiApiKey { get; init; }

    /// <summary>
    /// Gets the OpenAI embeddings endpoint.
    /// </summary>
    public string Endpoint { get; init; } = "https://api.openai.com/v1/embeddings";

    /// <summary>
    /// Creates options from explicit values and supported environment variables.
    /// </summary>
    /// <param name="enabled">Whether embeddings are enabled.</param>
    /// <param name="provider">The provider name.</param>
    /// <param name="model">The model name.</param>
    /// <param name="dimensions">The vector dimensions.</param>
    /// <param name="apiKey">The API key.</param>
    /// <param name="endpoint">The provider endpoint.</param>
    /// <returns>The embedding options.</returns>
    public static EmbeddingOptions Create(
        string? enabled,
        string? provider,
        string? model,
        string? dimensions,
        string? apiKey,
        string? endpoint)
    {
        var resolvedDimensions = int.TryParse(dimensions, out var parsedDimensions)
            ? parsedDimensions
            : 1536;

        return new EmbeddingOptions
        {
            Enabled = bool.TryParse(enabled, out var parsedEnabled) && parsedEnabled,
            Provider = string.IsNullOrWhiteSpace(provider) ? "Fake" : provider.Trim(),
            Model = string.IsNullOrWhiteSpace(model) ? "text-embedding-3-small" : model.Trim(),
            Dimensions = resolvedDimensions,
            OpenAiApiKey = apiKey
                ?? Environment.GetEnvironmentVariable("CNV_REGULATION_OPENAI_API_KEY")
                ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
            Endpoint = string.IsNullOrWhiteSpace(endpoint) ? "https://api.openai.com/v1/embeddings" : endpoint.Trim()
        };
    }
}
