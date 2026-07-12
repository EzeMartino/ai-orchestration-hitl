namespace Orchestration.Application.Agents.Planner.ToolCalling;

/// <summary>
/// Provides the production source of truth for planner-callable tools.
/// </summary>
public static class PlannerToolCatalog
{
    public const string AnalyzeTransactionsName = "data.analyze_transactions";
    public const string SearchCnvRegulationName = "legal.search_cnv_regulation";

    private static readonly IReadOnlyList<PlannerToolDefinition> Definitions =
        Array.AsReadOnly(
        [
            new PlannerToolDefinition(
                AnalyzeTransactionsName,
                "Analiza el reporte financiero mediante DataAgent y devuelve un resultado agregado trazable.",
                PlannerToolHandler.AnalyzeTransactions,
                PlannerToolResultKind.DataAgent,
                PlannerToolSatisfactionKind.DataAnalysis,
                "DataAgent",
                Array.AsReadOnly(
                [
                    Required("sessionId", PlannerToolArgumentType.Guid, "Identificador de la sesión."),
                    Required("reportName", PlannerToolArgumentType.String, "Nombre del reporte."),
                    Required("totalAmount", PlannerToolArgumentType.Decimal, "Monto total usando formato invariante."),
                    Required("transactionCount", PlannerToolArgumentType.Integer, "Cantidad de transacciones."),
                    Required("submittedAt", PlannerToolArgumentType.DateTimeOffset, "Fecha de envío en formato ISO 8601.")
                ])),
            new PlannerToolDefinition(
                SearchCnvRegulationName,
                "Recupera evidencia regulatoria CNV citada sin emitir conclusiones legales.",
                PlannerToolHandler.SearchCnvRegulation,
                PlannerToolResultKind.LegalAgent,
                PlannerToolSatisfactionKind.LegalReview,
                "LegalAgent",
                Array.AsReadOnly(
                [
                    Required("query", PlannerToolArgumentType.String, "Consulta breve en español."),
                    Optional("area", PlannerToolArgumentType.String, "Área regulatoria opcional."),
                    Optional("limit", PlannerToolArgumentType.Integer, "Máximo de resultados."),
                    Optional("source", PlannerToolArgumentType.String, "Fuente regulatoria opcional."),
                    Optional("documentType", PlannerToolArgumentType.String, "Tipo de documento opcional."),
                    Optional("resolutionNumber", PlannerToolArgumentType.String, "Número de resolución opcional."),
                    Optional("status", PlannerToolArgumentType.String, "Estado documental opcional."),
                    Optional("requiresReview", PlannerToolArgumentType.Boolean, "Filtro opcional de revisión requerida.")
                ]))
        ]);

    /// <summary>
    /// Gets every planner-callable production tool.
    /// </summary>
    public static IReadOnlyList<PlannerToolDefinition> All => Definitions;

    /// <summary>
    /// Finds a tool definition by canonical name using case-insensitive comparison.
    /// </summary>
    /// <param name="name">Tool name to resolve.</param>
    /// <returns>The matching definition, or <see langword="null"/> when unsupported.</returns>
    public static PlannerToolDefinition? Find(string? name)
    {
        return string.IsNullOrWhiteSpace(name)
            ? null
            : Definitions.FirstOrDefault(definition =>
                string.Equals(definition.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets catalog definitions explicitly enabled by the configured allowlist.
    /// </summary>
    /// <param name="options">Tool-calling configuration.</param>
    /// <returns>Allowed definitions in stable catalog order.</returns>
    public static IReadOnlyList<PlannerToolDefinition> GetAllowed(ToolCallingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var allowlist = new HashSet<string>(
            options.AllowedTools.Where(name => !string.IsNullOrWhiteSpace(name)),
            StringComparer.OrdinalIgnoreCase);

        return Definitions
            .Where(definition => allowlist.Contains(definition.Name))
            .ToArray();
    }

    private static PlannerToolArgumentDefinition Required(
        string name,
        PlannerToolArgumentType type,
        string description)
    {
        return new PlannerToolArgumentDefinition(name, type, true, description);
    }

    private static PlannerToolArgumentDefinition Optional(
        string name,
        PlannerToolArgumentType type,
        string description)
    {
        return new PlannerToolArgumentDefinition(name, type, false, description);
    }
}
