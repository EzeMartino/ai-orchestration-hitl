namespace CnvRegulation.Infrastructure.Search;

/// <summary>
/// Options for static regulation query aliases.
/// </summary>
public sealed class RegulationAliasesOptions
{
    /// <summary>
    /// Gets configured aliases keyed by shorthand or common phrase.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Aliases { get; init; } =
        CreateDefaultAliases();

    /// <summary>
    /// Gets the maximum number of search queries produced by expansion.
    /// </summary>
    public int MaxSearchQueries { get; init; } = 8;

    /// <summary>
    /// Gets the default CNV regulatory aliases.
    /// </summary>
    /// <returns>The default aliases.</returns>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> CreateDefaultAliases() =>
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["alyc"] =
            [
                "agente de liquidación y compensación",
                "agentes de liquidación y compensación",
                "liquidación y compensación"
            ],
            ["alac"] =
            [
                "agente de liquidación y compensación"
            ],
            ["fci"] =
            [
                "fondo común de inversión",
                "fondos comunes de inversión"
            ],
            ["cnv"] =
            [
                "comisión nacional de valores"
            ],
            ["oferta publica"] =
            [
                "oferta pública",
                "régimen de oferta pública"
            ],
            ["hecho relevante"] =
            [
                "información relevante",
                "hechos relevantes"
            ],
            ["lavado"] =
            [
                "prevención de lavado",
                "prevención de lavado de activos",
                "financiamiento del terrorismo",
                "pld",
                "uif"
            ],
            ["idoneidad"] =
            [
                "examen de idoneidad",
                "idóneos",
                "personal idóneo"
            ],
            ["régimen informativo"] =
            [
                "información periódica",
                "deber de informar",
                "informes"
            ]
        };
}
