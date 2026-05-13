using CnvRegulation.Application.Contracts;

namespace CnvRegulation.Application.Abstractions;

/// <summary>
/// Extracts regulatory topics from user text.
/// </summary>
public interface IRegulatoryTopicExtractor
{
    /// <summary>
    /// Extracts deterministic regulatory topics.
    /// </summary>
    /// <param name="text">The text to analyze.</param>
    /// <param name="regulationArea">The optional regulatory area.</param>
    /// <returns>Detected regulatory topics.</returns>
    IReadOnlyList<RegulatoryTopic> ExtractTopics(string text, string? regulationArea);
}
