using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;
using CnvRegulation.Infrastructure.Search;

namespace CnvRegulation.Infrastructure.Embeddings;

/// <summary>
/// Deterministic token-hashing embedding generator for tests and local diagnostics.
/// </summary>
public sealed class DeterministicFakeEmbeddingGenerator(EmbeddingOptions options) : IEmbeddingGenerator
{
    /// <inheritdoc />
    public Task<EmbeddingResult> GenerateAsync(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var dimensions = Math.Max(1, options.Dimensions);
        var vector = new float[dimensions];
        var tokens = StaticRegulationQueryExpander.Normalize(text)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var token in tokens)
        {
            var hash = StringComparer.OrdinalIgnoreCase.GetHashCode(token);
            var index = (hash & 0x7fffffff) % dimensions;
            vector[index] += 1;
        }

        Normalize(vector);

        return Task.FromResult(new EmbeddingResult
        {
            Vector = vector,
            Model = "fake-deterministic",
            Dimensions = dimensions
        });
    }

    private static void Normalize(float[] vector)
    {
        var length = Math.Sqrt(vector.Sum(value => value * value));
        if (length <= 0)
        {
            return;
        }

        for (var index = 0; index < vector.Length; index++)
        {
            vector[index] = (float)(vector[index] / length);
        }
    }
}
