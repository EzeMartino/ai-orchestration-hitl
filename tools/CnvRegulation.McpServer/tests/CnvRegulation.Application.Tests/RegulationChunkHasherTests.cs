using CnvRegulation.Infrastructure.Deduplication;
using FluentAssertions;

namespace CnvRegulation.Application.Tests;

public sealed class RegulationChunkHasherTests
{
    [Fact]
    public void ChunkHasher_ShouldProduceSameHashForWhitespaceVariants()
    {
        var hasher = new RegulationChunkHasher();

        var first = hasher.ComputeHash("ARTICULO 1. Texto   regulatorio\r\ncon espacios.");
        var second = hasher.ComputeHash("ARTICULO 1. Texto regulatorio con espacios.");

        first.Should().Be(second);
    }

    [Fact]
    public void ChunkHasher_ShouldProduceSameHashForCaseVariants()
    {
        var hasher = new RegulationChunkHasher();

        var first = hasher.ComputeHash("Artículo 1. Mercado de capitales.");
        var second = hasher.ComputeHash("ARTICULO 1. mercado de capitales.");

        first.Should().Be(second);
    }
}
