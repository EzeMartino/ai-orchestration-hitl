using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Orchestration.Application.Agents.Data.FinancialAnalysis;

namespace Orchestration.Application.Agents.Data.FinancialAnalysis.Extraction;

public sealed class FinancialDocumentExtractionResponseParser
{
    public const string SchemaValidationFailed = "schema_validation_failed";
    public const string ExtractionStrategy = "semantic_markdown_agent";
    public const int MaxResponseCharacters = 1_000_000;
    public const int MaxMetricCandidates = 500;
    public const int MaxJsonDepth = 8;

    private const int MaxMetadataValueCharacters = 500;
    private const int MaxMetricNameCharacters = 128;
    private const int MaxPeriodCharacters = 16;
    private const int MaxCurrencyCharacters = 32;
    private const int MaxUnitCharacters = 128;

    private static readonly Regex PeriodRegex = new(
        @"^(?:FY)?(?<year>20\d{2})(?<suffix>[AE])?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly IReadOnlySet<string> RootProperties = Properties(
        "document",
        "metrics");

    private static readonly IReadOnlySet<string> DocumentProperties = Properties(
        "company",
        "currency",
        "unit");

    private static readonly IReadOnlySet<string> NoRequiredProperties = Properties();

    private static readonly IReadOnlySet<string> MetadataProperties = Properties(
        "value",
        "sourceKind",
        "confidence",
        "sourcePage",
        "evidence",
        "inferenceExplanation");

    private static readonly IReadOnlySet<string> RequiredMetadataProperties = Properties(
        "value",
        "sourceKind",
        "confidence",
        "sourcePage",
        "evidence");

    private static readonly IReadOnlySet<string> MetricProperties = Properties(
        "name",
        "period",
        "value",
        "currency",
        "unit",
        "sourceKind",
        "confidence",
        "sourcePage",
        "evidence",
        "inferenceExplanation");

    public FinancialDocumentExtractionParseResult Parse(
        string? content,
        int maxEvidenceExcerptCharacters,
        int maxSourcePage,
        bool allowEmptyResult = false)
    {
        if (content is null ||
            content.Length > MaxResponseCharacters ||
            string.IsNullOrWhiteSpace(content) ||
            maxSourcePage < 1)
        {
            return Fail();
        }

        var trimmed = content.Trim();

        if (!trimmed.StartsWith('{') || !trimmed.EndsWith('}'))
        {
            return Fail();
        }

        try
        {
            using var document = JsonDocument.Parse(
                trimmed,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = MaxJsonDepth
                });

            if (!TryReadObject(
                    document.RootElement,
                    RootProperties,
                    RootProperties,
                    out var root) ||
                !TryReadObject(
                    root["document"],
                    DocumentProperties,
                    NoRequiredProperties,
                    out var documentProperties))
            {
                return Fail();
            }

            var maxEvidenceLength = Math.Max(1, maxEvidenceExcerptCharacters);

            if (!TryReadOptionalMetadataCandidate(
                    "company",
                    documentProperties,
                    maxEvidenceLength,
                    maxSourcePage,
                    out var company) ||
                !TryReadOptionalMetadataCandidate(
                    "currency",
                    documentProperties,
                    maxEvidenceLength,
                    maxSourcePage,
                    out var currency) ||
                !TryReadOptionalMetadataCandidate(
                    "unit",
                    documentProperties,
                    maxEvidenceLength,
                    maxSourcePage,
                    out var unit) ||
                !TryReadMetrics(
                    root["metrics"],
                    maxEvidenceLength,
                    maxSourcePage,
                    out var metrics))
            {
                return Fail();
            }

            if (!allowEmptyResult &&
                company is null &&
                currency is null &&
                unit is null &&
                metrics.Count == 0)
            {
                return Fail();
            }

            var metadataCandidates = new[]
            {
                company,
                currency,
                unit
            }.OfType<FinancialDocumentMetadataCandidate>().ToArray();

            return new FinancialDocumentExtractionParseResult(
                Succeeded: true,
                Result: new FinancialDocumentExtractionResult(
                    Company: company,
                    Currency: currency,
                    Unit: unit,
                    Metrics: metrics,
                    MetadataCandidates: metadataCandidates),
                FailureReason: null);
        }
        catch (JsonException)
        {
            return Fail();
        }
        catch (InvalidOperationException)
        {
            return Fail();
        }
    }

    private static bool TryReadOptionalMetadataCandidate(
        string fieldName,
        IReadOnlyDictionary<string, JsonElement> properties,
        int maxEvidenceLength,
        int maxSourcePage,
        out FinancialDocumentMetadataCandidate? candidate)
    {
        candidate = null;

        return !properties.TryGetValue(fieldName, out var element) ||
            element.ValueKind == JsonValueKind.Null ||
            TryReadMetadataCandidate(
                fieldName,
                element,
                maxEvidenceLength,
                maxSourcePage,
                out candidate);
    }

    private static bool TryReadMetadataCandidate(
        string fieldName,
        JsonElement element,
        int maxEvidenceLength,
        int maxSourcePage,
        out FinancialDocumentMetadataCandidate candidate)
    {
        candidate = null!;

        if (!TryReadObject(
                element,
                MetadataProperties,
                RequiredMetadataProperties,
                out var properties) ||
            !TryReadBoundedNonblankString(
                properties["value"],
                MaxMetadataValueCharacters,
                out var value) ||
            !TryReadSourceKind(properties["sourceKind"], out var sourceKind) ||
            !TryReadConfidence(properties["confidence"], out var confidence) ||
            !TryReadSourcePage(
                properties["sourcePage"],
                maxSourcePage,
                out var sourcePage) ||
            !TryReadEvidence(
                properties["evidence"],
                sourceKind,
                maxEvidenceLength,
                out var evidence) ||
            !TryReadOptionalExplanation(
                properties,
                sourceKind,
                maxEvidenceLength,
                out var inferenceExplanation))
        {
            return false;
        }

        candidate = new FinancialDocumentMetadataCandidate(
            Id: Guid.NewGuid(),
            FieldName: fieldName,
            Value: value,
            SourceKind: sourceKind,
            Confidence: confidence,
            SourcePage: sourcePage,
            Evidence: evidence,
            ExtractionStrategy: ExtractionStrategy,
            ReviewState: ReviewStateFor(sourceKind),
            InferenceExplanation: inferenceExplanation);

        return true;
    }

    private static bool TryReadMetrics(
        JsonElement element,
        int maxEvidenceLength,
        int maxSourcePage,
        out IReadOnlyList<FinancialMetricCandidate> metrics)
    {
        metrics = [];

        if (element.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var count = element.GetArrayLength();

        if (count > MaxMetricCandidates)
        {
            return false;
        }

        var parsed = new List<FinancialMetricCandidate>(count);

        foreach (var metricElement in element.EnumerateArray())
        {
            if (!TryReadMetricCandidate(
                    metricElement,
                    maxEvidenceLength,
                    maxSourcePage,
                    out var metric))
            {
                return false;
            }

            parsed.Add(metric);
        }

        metrics = parsed;
        return true;
    }

    private static bool TryReadMetricCandidate(
        JsonElement element,
        int maxEvidenceLength,
        int maxSourcePage,
        out FinancialMetricCandidate candidate)
    {
        candidate = null!;

        if (!TryReadObject(
                element,
                MetricProperties,
                MetricProperties,
                out var properties) ||
            !TryReadBoundedNonblankString(
                properties["name"],
                MaxMetricNameCharacters,
                out var suppliedName) ||
            !FinancialMetricNameCatalog.TryNormalize(
                suppliedName,
                out var name) ||
            !TryReadBoundedNonblankString(
                properties["period"],
                MaxPeriodCharacters,
                out var suppliedPeriod) ||
            !TryNormalizePeriod(suppliedPeriod, out var period) ||
            properties["value"].ValueKind != JsonValueKind.Number ||
            !properties["value"].TryGetDecimal(out var value) ||
            !TryReadNullableBoundedNonblankString(
                properties["currency"],
                MaxCurrencyCharacters,
                out var currency) ||
            !TryReadNullableBoundedNonblankString(
                properties["unit"],
                MaxUnitCharacters,
                out var unit) ||
            !TryReadSourceKind(properties["sourceKind"], out var sourceKind) ||
            !TryReadConfidence(properties["confidence"], out var confidence) ||
            !TryReadSourcePage(
                properties["sourcePage"],
                maxSourcePage,
                out var sourcePage) ||
            !TryReadEvidence(
                properties["evidence"],
                sourceKind,
                maxEvidenceLength,
                out var evidence) ||
            !TryReadNullableBoundedString(
                properties["inferenceExplanation"],
                maxEvidenceLength,
                out var inferenceExplanation) ||
            !HasRequiredInferenceExplanation(sourceKind, inferenceExplanation))
        {
            return false;
        }

        candidate = new FinancialMetricCandidate(
            Id: Guid.NewGuid(),
            Name: name,
            Period: period,
            Value: value,
            Currency: currency,
            Unit: unit,
            SourceKind: sourceKind,
            Confidence: confidence,
            SourcePage: sourcePage,
            Evidence: evidence,
            ExtractionStrategy: ExtractionStrategy,
            ReviewState: ReviewStateFor(sourceKind),
            InferenceExplanation: inferenceExplanation);

        return true;
    }

    private static bool TryReadNullableBoundedNonblankString(
        JsonElement element,
        int maxLength,
        out string? value)
    {
        value = null;

        return element.ValueKind == JsonValueKind.Null ||
            TryReadBoundedNonblankString(element, maxLength, out value);
    }

    private static bool TryReadObject(
        JsonElement element,
        IReadOnlySet<string> allowedProperties,
        IReadOnlySet<string> requiredProperties,
        out IReadOnlyDictionary<string, JsonElement> properties)
    {
        var parsed = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        properties = parsed;

        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!allowedProperties.Contains(property.Name) ||
                !parsed.TryAdd(property.Name, property.Value))
            {
                return false;
            }
        }

        return requiredProperties.All(parsed.ContainsKey);
    }

    private static bool TryReadBoundedNonblankString(
        JsonElement element,
        int maxLength,
        out string value)
    {
        value = string.Empty;

        if (!TryReadSafeString(element, out var raw))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        value = raw.Trim();
        return value.Length <= maxLength;
    }

    private static bool TryReadNullableBoundedString(
        JsonElement element,
        int maxLength,
        out string? value)
    {
        value = null;

        if (element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (!TryReadSafeString(element, out var raw))
        {
            return false;
        }

        value = raw.Trim();
        return value.Length <= maxLength;
    }

    private static bool TryReadSourceKind(
        JsonElement element,
        out string sourceKind)
    {
        sourceKind = string.Empty;

        if (!TryReadSafeString(element, out sourceKind))
        {
            return false;
        }

        return sourceKind is
            FinancialMetricCandidateSourceKinds.Reported or
            FinancialMetricCandidateSourceKinds.Inferred;
    }

    private static bool TryReadConfidence(
        JsonElement element,
        out decimal confidence)
    {
        confidence = 0m;

        return element.ValueKind == JsonValueKind.Number &&
            element.TryGetDecimal(out confidence) &&
            confidence is >= 0m and <= 1m;
    }

    private static bool TryReadSourcePage(
        JsonElement element,
        int maxSourcePage,
        out int? sourcePage)
    {
        sourcePage = null;

        if (element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Number ||
            !element.TryGetInt32(out var parsed) ||
            parsed < 1 ||
            parsed > maxSourcePage)
        {
            return false;
        }

        sourcePage = parsed;
        return true;
    }

    private static bool TryReadEvidence(
        JsonElement element,
        string sourceKind,
        int maxEvidenceLength,
        out string evidence)
    {
        evidence = string.Empty;

        if (!TryReadSafeString(element, out var raw))
        {
            return false;
        }

        var trimmed = raw.Trim();

        if (trimmed.Length > maxEvidenceLength ||
            sourceKind == FinancialMetricCandidateSourceKinds.Reported &&
            string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        evidence = trimmed;
        return true;
    }

    private static bool TryReadOptionalExplanation(
        IReadOnlyDictionary<string, JsonElement> properties,
        string sourceKind,
        int maxExplanationLength,
        out string? inferenceExplanation)
    {
        inferenceExplanation = null;

        if (properties.TryGetValue("inferenceExplanation", out var element) &&
            !TryReadNullableBoundedString(
                element,
                maxExplanationLength,
                out inferenceExplanation))
        {
            return false;
        }

        return HasRequiredInferenceExplanation(sourceKind, inferenceExplanation);
    }

    private static bool TryReadSafeString(
        JsonElement element,
        out string value)
    {
        value = string.Empty;

        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        try
        {
            value = element.GetString() ?? string.Empty;
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        return IsSafeDecodedText(value);
    }

    private static bool IsSafeDecodedText(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];

            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= value.Length ||
                    !char.IsLowSurrogate(value[index + 1]))
                {
                    return false;
                }

                index++;
                continue;
            }

            if (char.IsLowSurrogate(character) ||
                char.IsControl(character) &&
                character is not ('\r' or '\n' or '\t'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasRequiredInferenceExplanation(
        string sourceKind,
        string? inferenceExplanation)
    {
        return sourceKind != FinancialMetricCandidateSourceKinds.Inferred ||
            !string.IsNullOrWhiteSpace(inferenceExplanation);
    }

    private static bool TryNormalizePeriod(
        string suppliedPeriod,
        out string period)
    {
        period = string.Empty;
        var match = PeriodRegex.Match(suppliedPeriod.Trim());

        if (!match.Success)
        {
            return false;
        }

        var suffix = match.Groups["suffix"].Value;
        period = string.IsNullOrWhiteSpace(suffix)
            ? $"{match.Groups["year"].Value}A"
            : $"{match.Groups["year"].Value}{suffix.ToUpperInvariant()}";

        return true;
    }

    private static string ReviewStateFor(string sourceKind)
    {
        return sourceKind == FinancialMetricCandidateSourceKinds.Reported
            ? FinancialMetricCandidateReviewStates.Explicit
            : FinancialMetricCandidateReviewStates.Inferred;
    }

    private static IReadOnlySet<string> Properties(params string[] names)
    {
        return new HashSet<string>(names, StringComparer.Ordinal);
    }

    private static FinancialDocumentExtractionParseResult Fail()
    {
        return new FinancialDocumentExtractionParseResult(
            Succeeded: false,
            Result: null,
            FailureReason: SchemaValidationFailed);
    }
}
