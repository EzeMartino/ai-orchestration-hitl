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
                "agente de liquidaci\u00f3n y compensaci\u00f3n",
                "agentes de liquidaci\u00f3n y compensaci\u00f3n",
                "liquidaci\u00f3n y compensaci\u00f3n"
            ],
            ["alac"] =
            [
                "agente de liquidaci\u00f3n y compensaci\u00f3n"
            ],
            ["fci"] =
            [
                "fondo com\u00fan de inversi\u00f3n",
                "fondos comunes de inversi\u00f3n"
            ],
            ["cnv"] =
            [
                "comisi\u00f3n nacional de valores"
            ],
            ["oferta publica"] =
            [
                "oferta p\u00fablica",
                "r\u00e9gimen de oferta p\u00fablica"
            ],
            ["hecho relevante"] =
            [
                "hechos relevantes",
                "informaci\u00f3n relevante",
                "informacion relevante",
                "informaciones relevantes"
            ],
            ["informacion relevante"] =
            [
                "hecho relevante",
                "hechos relevantes",
                "informaci\u00f3n relevante",
                "informaciones relevantes"
            ],
            ["fiduciario financiero"] =
            [
                "fiduciarios financieros",
                "fideicomiso financiero",
                "fideicomisos financieros"
            ],
            ["emisora"] =
            [
                "emisor",
                "emisoras",
                "entidad emisora",
                "sociedad emisora",
                "entidades emisoras",
                "emisores"
            ],
            ["lavado"] =
            [
                "prevenci\u00f3n de lavado",
                "prevenci\u00f3n de lavado de activos",
                "financiamiento del terrorismo",
                "pld",
                "uif"
            ],
            ["idoneidad"] =
            [
                "examen de idoneidad",
                "id\u00f3neos",
                "personal id\u00f3neo"
            ],
            ["r\u00e9gimen informativo"] =
            [
                "informaci\u00f3n peri\u00f3dica",
                "deber de informar",
                "informes"
            ]
        };
}
