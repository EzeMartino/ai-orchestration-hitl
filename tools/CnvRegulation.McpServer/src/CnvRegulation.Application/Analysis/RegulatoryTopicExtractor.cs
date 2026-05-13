using System.Globalization;
using System.Text;
using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;

namespace CnvRegulation.Application.Analysis;

/// <summary>
/// Deterministic topic extractor for CNV regulatory analysis.
/// </summary>
public sealed class RegulatoryTopicExtractor : IRegulatoryTopicExtractor
{
    /// <inheritdoc />
    public IReadOnlyList<RegulatoryTopic> ExtractTopics(string text, string? regulationArea)
    {
        if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(regulationArea))
        {
            return [];
        }

        var normalized = Normalize(string.Join(' ', text, regulationArea));
        var topics = new List<RegulatoryTopic>();

        AddWhen(
            topics,
            normalized,
            "Client information and suitability",
            "ALyC obligaciones informacion clientes riesgos perfil",
            0.88,
            "cliente",
            "clientes",
            "perfil",
            "riesgo",
            "riesgos",
            "informar",
            "verificar");
        AddWhen(
            topics,
            normalized,
            "ALyC obligaciones",
            "ALyC obligaciones",
            0.86,
            "alyc",
            "agente",
            "agentes",
            "liquidacion y compensacion");
        AddWhen(
            topics,
            normalized,
            "Oferta publica",
            "oferta publica",
            0.84,
            "oferta publica",
            "publico inversor",
            "valores negociables");
        AddWhen(
            topics,
            normalized,
            "Hecho relevante",
            "hecho relevante informacion relevante",
            0.82,
            "hecho relevante",
            "hechos relevantes",
            "informacion relevante",
            "aif");
        AddWhen(
            topics,
            normalized,
            "Fondos comunes de inversion",
            "fondos comunes de inversion FCI",
            0.82,
            "fci",
            "fondo comun",
            "fondos comunes",
            "cuotaparte",
            "cuotapartes");
        AddWhen(
            topics,
            normalized,
            "Idoneidad",
            "idoneidad personal idoneo examen",
            0.80,
            "idoneidad",
            "idoneo",
            "idoneos",
            "examen",
            "personal idoneo",
            "perfil",
            "perfiles");
        AddWhen(
            topics,
            normalized,
            "Prevencion de lavado",
            "prevencion de lavado financiamiento terrorismo UIF",
            0.88,
            "lavado",
            "uif",
            "pld",
            "financiamiento del terrorismo",
            "prevencion de lavado");
        AddWhen(
            topics,
            normalized,
            "Fiduciario financiero",
            "fiduciario financiero fideicomiso financiero",
            0.82,
            "fiduciario",
            "fiduciarios",
            "fideicomiso financiero",
            "fideicomisos financieros");
        AddWhen(
            topics,
            normalized,
            "Emisora",
            "emisora emisor sociedad emisora",
            0.78,
            "emisora",
            "emisor",
            "emisoras",
            "emisores",
            "sociedad emisora");

        return topics
            .GroupBy(topic => topic.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(topic => topic.Confidence).First())
            .OrderByDescending(topic => topic.Confidence)
            .Take(5)
            .ToArray();
    }

    private static void AddWhen(
        List<RegulatoryTopic> topics,
        string normalized,
        string name,
        string searchQuery,
        double confidence,
        params string[] terms)
    {
        if (terms.Any(term => normalized.Contains(Normalize(term), StringComparison.OrdinalIgnoreCase)))
        {
            topics.Add(new RegulatoryTopic
            {
                Name = name,
                SearchQuery = searchQuery,
                Confidence = confidence
            });
        }
    }

    private static string Normalize(string value)
    {
        var normalized = value
            .ToLowerInvariant()
            .Replace('-', ' ')
            .Replace('_', ' ');
        var formD = normalized.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(formD.Length);
        foreach (var character in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return string.Join(' ', builder
            .ToString()
            .Normalize(NormalizationForm.FormC)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
