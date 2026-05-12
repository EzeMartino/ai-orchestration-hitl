namespace CnvRegulation.Infrastructure.Sources;

/// <summary>
/// Classification for controlled Infoleg links.
/// </summary>
public enum InfolegLinkClassification
{
    /// <summary>
    /// Original norma page.
    /// </summary>
    Norma,

    /// <summary>
    /// Updated text page.
    /// </summary>
    Texact,

    /// <summary>
    /// Annex path.
    /// </summary>
    Anexos,

    /// <summary>
    /// Infoleg verNorma page.
    /// </summary>
    VerNorma,

    /// <summary>
    /// Official Infoleg link with unknown role.
    /// </summary>
    Unknown
}
