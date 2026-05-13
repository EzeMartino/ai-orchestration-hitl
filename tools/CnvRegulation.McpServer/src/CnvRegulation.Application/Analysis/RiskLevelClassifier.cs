namespace CnvRegulation.Application.Analysis;

/// <summary>
/// Classifies deterministic risk levels for CNV analysis findings.
/// </summary>
public static class RiskLevelClassifier
{
    /// <summary>
    /// Classifies a risk level from input text and topic name.
    /// </summary>
    /// <param name="inputText">The input text.</param>
    /// <param name="topicName">The topic name.</param>
    /// <returns>The risk level.</returns>
    public static string Classify(string inputText, string topicName)
    {
        var text = string.Join(' ', inputText, topicName).ToLowerInvariant();

        if (ContainsAny(text, "sin autoriz", "engaños", "enganos", "falso", "fondos de clientes", "custodia", "lavado", "uif", "financiamiento del terrorismo"))
        {
            return "high";
        }

        if (ContainsAny(text, "sin verificar", "sin informar", "riesgo", "riesgos", "perfil", "idoneidad", "regimen informativo", "régimen informativo", "agente", "alyc"))
        {
            return "medium";
        }

        return ContainsAny(text, "concepto", "definicion", "definición") ? "low" : "unknown";
    }

    private static bool ContainsAny(string text, params string[] terms) =>
        terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
}
