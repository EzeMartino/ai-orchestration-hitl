using CnvRegulation.Application.Contracts;
using CnvRegulation.Domain;

namespace CnvRegulation.Application.Abstractions;

/// <summary>
/// Builds compliance findings from detected topics and cited evidence.
/// </summary>
public interface IRegulatoryFindingBuilder
{
    /// <summary>
    /// Builds findings from retrieved search evidence.
    /// </summary>
    /// <param name="inputText">The original text being analyzed.</param>
    /// <param name="topics">Detected regulatory topics.</param>
    /// <param name="searchResults">Retrieved search results.</param>
    /// <param name="strictMode">Whether strict review mode is enabled.</param>
    /// <returns>Evidence-based findings.</returns>
    IReadOnlyList<ComplianceFinding> BuildFindings(
        string inputText,
        IReadOnlyList<RegulatoryTopic> topics,
        IReadOnlyList<RegulationSearchResult> searchResults,
        bool strictMode);
}
