namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Indicates whether a search term is present in indexed material.
/// </summary>
public sealed class SearchTermPresence
{
    /// <summary>
    /// Gets the normalized term.
    /// </summary>
    public required string Term { get; init; }

    /// <summary>
    /// Gets matching chunk count.
    /// </summary>
    public int FoundInChunks { get; init; }

    /// <summary>
    /// Gets matching document count.
    /// </summary>
    public int FoundInDocuments { get; init; }

    /// <summary>
    /// Gets whether the exact normalized phrase exists.
    /// </summary>
    public bool ExactPhraseFound { get; init; }
}
