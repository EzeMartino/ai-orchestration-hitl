using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Orchestration.Application.Agents.Data.FinancialAnalysis;

namespace Orchestration.Application.Agents.Data.FinancialAnalysis.Extraction;

public sealed class FinancialMetricCandidateReconciler(
    IStructuredFinancialMetricsValidator validator)
    : IFinancialMetricCandidateReconciler
{
    private const decimal DefaultDeterministicConfidence = 0.75m;
    private const decimal ExplicitUploadMetadataConfidence = 1m;
    private const string DeterministicExtractionStrategy =
        "deterministic_pdf_parser";

    private static readonly string[] RequiredMetadataFields =
        ["company", "currency", "unit"];

    private readonly IStructuredFinancialMetricsValidator _validator =
        validator ?? throw new ArgumentNullException(nameof(validator));

    public FinancialMetricReconciliationResult Reconcile(
        StructuredFinancialMetricsInput deterministicInput,
        FinancialDocumentExtractionResult? semanticResult,
        FinancialMetricsExtractionOptions options)
    {
        ArgumentNullException.ThrowIfNull(deterministicInput);
        ArgumentNullException.ThrowIfNull(options);

        var metricEntries = BuildMetricEntries(
            deterministicInput,
            semanticResult);
        var conflicts = new List<FinancialMetricCandidateConflict>();
        var acceptedMetricCandidates = new List<FinancialMetricCandidate>();
        var proposedMetrics = ReconcileMetrics(
            metricEntries,
            conflicts,
            acceptedMetricCandidates);

        var semanticMetadataCandidates = GetMetadataCandidates(semanticResult);
        var metadataCandidates = BuildResultMetadataCandidates(
            deterministicInput,
            semanticMetadataCandidates);
        var acceptedMetadataCandidates =
            new List<FinancialDocumentMetadataCandidate>();
        var proposedMetadata = ReconcileMetadata(
            deterministicInput,
            semanticMetadataCandidates,
            conflicts,
            acceptedMetadataCandidates);

        var proposedInput = new StructuredFinancialMetricsInput(
            DocumentId: deterministicInput.DocumentId,
            Company: proposedMetadata.Company,
            Currency: proposedMetadata.Currency,
            Unit: proposedMetadata.Unit,
            Metrics: proposedMetrics);
        var missingFields = GetMissingFields(proposedInput);
        var validation = _validator.Validate(proposedInput);
        var hasReviewRequiredCandidate =
            metricEntries.Any(entry =>
                RequiresCandidateReview(entry.Candidate)) ||
            metadataCandidates.Any(RequiresCandidateReview);
        var allAcceptedCandidatesAreExplicit =
            acceptedMetricCandidates.All(IsExplicit) &&
            acceptedMetadataCandidates.All(IsExplicit);
        var allAcceptedCandidatesMeetConfidence =
            acceptedMetricCandidates.All(candidate =>
                candidate.Confidence >= options.AutomaticAcceptanceConfidence) &&
            acceptedMetadataCandidates.All(candidate =>
                candidate.Confidence >= options.AutomaticAcceptanceConfidence);
        var canAutoAccept =
            string.Equals(
                options.Mode,
                "AutoAccept",
                StringComparison.OrdinalIgnoreCase) &&
            validation.IsValid &&
            allAcceptedCandidatesAreExplicit &&
            allAcceptedCandidatesMeetConfidence &&
            conflicts.Count == 0 &&
            missingFields.Count == 0 &&
            !hasReviewRequiredCandidate;
        var metricConflictIds = conflicts
            .SelectMany(conflict => conflict.MetricCandidates)
            .Select(candidate => candidate.Id)
            .ToHashSet();
        var metadataConflictIds = conflicts
            .SelectMany(conflict => conflict.MetadataCandidates)
            .Select(candidate => candidate.Id)
            .ToHashSet();
        var markedConflicts = conflicts
            .Select(conflict => conflict with
            {
                MetricCandidates = conflict.MetricCandidates
                    .Select(candidate => MarkConflict(
                        candidate,
                        metricConflictIds))
                    .ToArray(),
                MetadataCandidates = conflict.MetadataCandidates
                    .Select(candidate => MarkConflict(
                        candidate,
                        metadataConflictIds))
                    .ToArray()
            })
            .ToArray();

        return new FinancialMetricReconciliationResult(
            ProposedInput: proposedInput,
            Candidates: metricEntries
                .OrderBy(entry => entry.Key.Name, StringComparer.Ordinal)
                .ThenBy(entry => entry.Key.Period, StringComparer.Ordinal)
                .ThenBy(entry => entry.IsDeterministic ? 0 : 1)
                .ThenBy(
                    entry => entry.Candidate.ExtractionStrategy,
                    StringComparer.Ordinal)
                .ThenByDescending(entry => entry.Candidate.Confidence)
                .ThenBy(entry => entry.Candidate.Id)
                .Select(entry => MarkConflict(
                    entry.Candidate,
                    metricConflictIds))
                .ToArray(),
            Conflicts: markedConflicts
                .OrderBy(conflict => conflict.Kind, StringComparer.Ordinal)
                .ThenBy(
                    conflict => conflict.FieldName,
                    StringComparer.Ordinal)
                .ThenBy(
                    conflict => conflict.MetricName,
                    StringComparer.Ordinal)
                .ThenBy(conflict => conflict.Period, StringComparer.Ordinal)
                .ToArray(),
            MissingFields: missingFields,
            RequiresReview: !canAutoAccept,
            CanAutoAccept: canAutoAccept)
        {
            MetadataCandidates = metadataCandidates
                .Select(candidate => MarkConflict(
                    candidate,
                    metadataConflictIds))
                .ToArray()
        };
    }

    private static IReadOnlyList<MetricEntry> BuildMetricEntries(
        StructuredFinancialMetricsInput deterministicInput,
        FinancialDocumentExtractionResult? semanticResult)
    {
        var entries = new List<MetricEntry>();
        var deterministicMetrics =
            deterministicInput.Metrics ??
            Array.Empty<StructuredFinancialMetricInput>();

        for (var index = 0; index < deterministicMetrics.Count; index++)
        {
            var metric = deterministicMetrics[index];

            if (metric is null)
            {
                continue;
            }

            var key = MetricKeyFor(metric.Name, metric.Period);
            var confidence =
                metric.Confidence ?? DefaultDeterministicConfidence;
            var candidate = new FinancialMetricCandidate(
                Id: CreateStableId(
                    "metric",
                    deterministicInput.DocumentId,
                    index.ToString(CultureInfo.InvariantCulture),
                    key.Name,
                    key.Period,
                    DecimalText(metric.Value),
                    metric.Currency,
                    metric.Unit,
                    metric.Source,
                    metric.SourcePage?.ToString(CultureInfo.InvariantCulture)),
                Name: key.Name,
                Period: key.Period,
                Value: metric.Value,
                Currency: Clean(metric.Currency),
                Unit: Clean(metric.Unit),
                SourceKind: FinancialMetricCandidateSourceKinds.Reported,
                Confidence: confidence,
                SourcePage: metric.SourcePage,
                Evidence: Clean(metric.Source) ?? string.Empty,
                ExtractionStrategy: DeterministicExtractionStrategy,
                ReviewState: FinancialMetricCandidateReviewStates.Explicit,
                InferenceExplanation: null);
            var proposedMetric = metric with
            {
                Name = key.Name,
                Period = key.Period,
                Currency = Clean(metric.Currency),
                Unit = Clean(metric.Unit),
                Source = Clean(metric.Source),
                Confidence = confidence
            };

            entries.Add(new MetricEntry(
                key,
                candidate,
                proposedMetric,
                IsDeterministic: true));
        }

        foreach (var candidate in semanticResult?.Metrics ??
                 Array.Empty<FinancialMetricCandidate>())
        {
            if (candidate is null)
            {
                continue;
            }

            var key = MetricKeyFor(candidate.Name, candidate.Period);
            entries.Add(new MetricEntry(
                key,
                candidate,
                new StructuredFinancialMetricInput(
                    Name: key.Name,
                    Period: key.Period,
                    Value: candidate.Value,
                    Unit: Clean(candidate.Unit),
                    Currency: Clean(candidate.Currency),
                    Source: Clean(candidate.ExtractionStrategy),
                    SourcePage: candidate.SourcePage,
                    Confidence: candidate.Confidence),
                IsDeterministic: false));
        }

        return entries;
    }

    private static IReadOnlyList<StructuredFinancialMetricInput>
        ReconcileMetrics(
            IReadOnlyList<MetricEntry> entries,
            ICollection<FinancialMetricCandidateConflict> conflicts,
            ICollection<FinancialMetricCandidate> acceptedCandidates)
    {
        var proposedMetrics = new List<StructuredFinancialMetricInput>();

        foreach (var group in entries
                     .GroupBy(entry => entry.Key)
                     .OrderBy(group => group.Key.Name, StringComparer.Ordinal)
                     .ThenBy(group => group.Key.Period, StringComparer.Ordinal))
        {
            var alternatives = OrderMetricAlternatives(group).ToArray();
            var preferred = alternatives
                .OrderBy(entry => entry.IsDeterministic ? 0 : 1)
                .ThenBy(entry => ExplicitRank(entry.Candidate))
                .ThenByDescending(entry => entry.Candidate.Confidence)
                .ThenBy(entry => entry.Candidate.Id)
                .First();
            var valueCount = alternatives
                .Select(entry => entry.Candidate.Value)
                .Distinct()
                .Count();
            var currencies = DistinctValues(
                alternatives.Select(entry => entry.Candidate.Currency));
            var units = DistinctValues(
                alternatives.Select(entry => entry.Candidate.Unit));

            if (valueCount > 1)
            {
                conflicts.Add(MetricConflict(
                    "value",
                    preferred,
                    alternatives));
            }

            if (currencies.Count > 1)
            {
                conflicts.Add(MetricConflict(
                    "currency",
                    preferred,
                    alternatives));
            }

            if (units.Count > 1)
            {
                conflicts.Add(MetricConflict(
                    "unit",
                    preferred,
                    alternatives));
            }

            var hasConflict =
                valueCount > 1 ||
                currencies.Count > 1 ||
                units.Count > 1;
            var selected = hasConflict
                ? preferred
                : alternatives
                    .OrderBy(entry => ExplicitRank(entry.Candidate))
                    .ThenByDescending(entry => entry.Candidate.Confidence)
                    .ThenByDescending(entry =>
                        !string.IsNullOrWhiteSpace(entry.Candidate.Evidence))
                    .ThenBy(entry => entry.IsDeterministic ? 0 : 1)
                    .ThenBy(entry => entry.Candidate.Id)
                    .First();
            var proposed = selected.ProposedMetric with
            {
                Currency = Clean(selected.ProposedMetric.Currency) ??
                    FirstValue(alternatives.Select(entry =>
                        entry.ProposedMetric.Currency)),
                Unit = Clean(selected.ProposedMetric.Unit) ??
                    FirstValue(alternatives.Select(entry =>
                        entry.ProposedMetric.Unit))
            };

            proposedMetrics.Add(proposed);
            acceptedCandidates.Add(selected.Candidate);
        }

        return proposedMetrics;
    }

    private static IReadOnlyList<FinancialDocumentMetadataCandidate>
        GetMetadataCandidates(
            FinancialDocumentExtractionResult? semanticResult)
    {
        if (semanticResult is null)
        {
            return [];
        }

        var candidates = new List<FinancialDocumentMetadataCandidate>();

        if (semanticResult.MetadataCandidates is not null)
        {
            candidates.AddRange(
                semanticResult.MetadataCandidates.Where(candidate =>
                    candidate is not null));
        }

        candidates.AddRange(
            new[]
            {
                semanticResult.Company,
                semanticResult.Currency,
                semanticResult.Unit
            }
            .Where(candidate => candidate is not null)
            .Cast<FinancialDocumentMetadataCandidate>()
            .ToArray());

        return candidates
            .Where(candidate => candidate is not null)
            .GroupBy(candidate => candidate.Id)
            .Select(group => group.First())
            .ToArray();
    }

    private static IReadOnlyList<FinancialDocumentMetadataCandidate>
        BuildResultMetadataCandidates(
            StructuredFinancialMetricsInput input,
            IReadOnlyList<FinancialDocumentMetadataCandidate>
                semanticCandidates)
    {
        var uploadCandidates =
            new List<FinancialDocumentMetadataCandidate>();
        AddUploadMetadataCandidate(
            uploadCandidates,
            input.DocumentId,
            "company",
            Clean(input.Company));
        AddUploadMetadataCandidate(
            uploadCandidates,
            input.DocumentId,
            "currency",
            Clean(input.Currency));
        AddUploadMetadataCandidate(
            uploadCandidates,
            input.DocumentId,
            "unit",
            Clean(input.Unit));

        return uploadCandidates
            .Concat(semanticCandidates)
            .GroupBy(candidate => candidate.Id)
            .Select(group => group.First())
            .OrderBy(candidate => MetadataFieldRank(candidate.FieldName))
            .ThenBy(candidate =>
                string.Equals(
                    candidate.ExtractionStrategy,
                    DeterministicExtractionStrategy,
                    StringComparison.Ordinal)
                    ? 0
                    : 1)
            .ThenBy(candidate => ExplicitRank(candidate))
            .ThenByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => NormalizeValue(candidate.Value))
            .ThenBy(candidate => candidate.Id)
            .ToArray();
    }

    private static ProposedMetadata ReconcileMetadata(
        StructuredFinancialMetricsInput input,
        IReadOnlyList<FinancialDocumentMetadataCandidate> semanticCandidates,
        ICollection<FinancialMetricCandidateConflict> conflicts,
        ICollection<FinancialDocumentMetadataCandidate> acceptedCandidates)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["company"] = Clean(input.Company),
            ["currency"] = Clean(input.Currency),
            ["unit"] = Clean(input.Unit)
        };

        foreach (var fieldName in RequiredMetadataFields)
        {
            var uploadValue = values[fieldName];
            var fieldCandidates = semanticCandidates
                .Where(candidate => string.Equals(
                    Clean(candidate.FieldName),
                    fieldName,
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(candidate => ExplicitRank(candidate))
                .ThenByDescending(candidate => candidate.Confidence)
                .ThenBy(candidate => NormalizeValue(candidate.Value))
                .ThenBy(candidate => candidate.Id)
                .ToArray();

            if (uploadValue is not null)
            {
                var uploadCandidate = CreateUploadMetadataCandidate(
                    input.DocumentId,
                    fieldName,
                    uploadValue);
                acceptedCandidates.Add(uploadCandidate);
                var uploadExplicitCandidates = fieldCandidates
                    .Where(IsExplicit)
                    .ToArray();

                if (uploadExplicitCandidates.Any(candidate =>
                    !ValuesEqual(candidate.Value, uploadValue)))
                {
                    conflicts.Add(MetadataConflict(
                        fieldName,
                        uploadValue,
                        [uploadCandidate, .. uploadExplicitCandidates]));
                }

                continue;
            }

            if (fieldCandidates.Length == 0)
            {
                continue;
            }

            var selected = fieldCandidates[0];
            values[fieldName] = Clean(selected.Value);
            acceptedCandidates.Add(selected);
            var explicitFieldCandidates = fieldCandidates
                .Where(IsExplicit)
                .ToArray();

            if (DistinctValues(explicitFieldCandidates.Select(candidate =>
                    candidate.Value)).Count > 1)
            {
                conflicts.Add(MetadataConflict(
                    fieldName,
                    values[fieldName],
                    [
                        selected,
                        .. explicitFieldCandidates.Where(candidate =>
                            candidate.Id != selected.Id)
                    ]));
            }
        }

        return new ProposedMetadata(
            Company: values["company"],
            Currency: values["currency"],
            Unit: values["unit"]);
    }

    private static FinancialMetricCandidateConflict MetricConflict(
        string fieldName,
        MetricEntry preferred,
        IReadOnlyList<MetricEntry> alternatives)
    {
        var proposedValue = fieldName switch
        {
            "value" => DecimalText(preferred.Candidate.Value),
            "currency" => Clean(preferred.Candidate.Currency),
            "unit" => Clean(preferred.Candidate.Unit),
            _ => null
        };

        return new FinancialMetricCandidateConflict(
            Kind: "metric",
            FieldName: fieldName,
            MetricName: preferred.Key.Name,
            Period: preferred.Key.Period,
            ProposedValue: proposedValue,
            MetricCandidates: alternatives
                .Select(entry => entry.Candidate)
                .ToArray(),
            MetadataCandidates: []);
    }

    private static FinancialMetricCandidateConflict MetadataConflict(
        string fieldName,
        string? proposedValue,
        IReadOnlyList<FinancialDocumentMetadataCandidate> alternatives)
    {
        return new FinancialMetricCandidateConflict(
            Kind: "metadata",
            FieldName: fieldName,
            MetricName: null,
            Period: null,
            ProposedValue: proposedValue,
            MetricCandidates: [],
            MetadataCandidates: alternatives);
    }

    private static FinancialDocumentMetadataCandidate
        CreateUploadMetadataCandidate(
            string documentId,
            string fieldName,
            string value)
    {
        return new FinancialDocumentMetadataCandidate(
            Id: CreateStableId(
                "metadata",
                documentId,
                fieldName,
                value),
            FieldName: fieldName,
            Value: value,
            SourceKind: FinancialMetricCandidateSourceKinds.Reported,
            Confidence: ExplicitUploadMetadataConfidence,
            SourcePage: null,
            Evidence: value,
            ExtractionStrategy: DeterministicExtractionStrategy,
            ReviewState: FinancialMetricCandidateReviewStates.Explicit,
            InferenceExplanation: null);
    }

    private static void AddUploadMetadataCandidate(
        ICollection<FinancialDocumentMetadataCandidate> candidates,
        string documentId,
        string fieldName,
        string? value)
    {
        if (value is not null)
        {
            candidates.Add(CreateUploadMetadataCandidate(
                documentId,
                fieldName,
                value));
        }
    }

    private static int MetadataFieldRank(string? fieldName)
    {
        return (Clean(fieldName) ?? string.Empty).ToLowerInvariant() switch
        {
            "company" => 0,
            "currency" => 1,
            "unit" => 2,
            _ => 3
        };
    }

    private static FinancialMetricCandidate MarkConflict(
        FinancialMetricCandidate candidate,
        IReadOnlySet<Guid> conflictIds)
    {
        return conflictIds.Contains(candidate.Id)
            ? candidate with
            {
                ReviewState = FinancialMetricCandidateReviewStates.Conflict
            }
            : candidate;
    }

    private static FinancialDocumentMetadataCandidate MarkConflict(
        FinancialDocumentMetadataCandidate candidate,
        IReadOnlySet<Guid> conflictIds)
    {
        return conflictIds.Contains(candidate.Id)
            ? candidate with
            {
                ReviewState = FinancialMetricCandidateReviewStates.Conflict
            }
            : candidate;
    }

    private static IReadOnlyList<string> GetMissingFields(
        StructuredFinancialMetricsInput input)
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(input.Company))
        {
            missing.Add("company");
        }

        if (string.IsNullOrWhiteSpace(input.Currency))
        {
            missing.Add("currency");
        }

        if (string.IsNullOrWhiteSpace(input.Unit))
        {
            missing.Add("unit");
        }

        if (input.Metrics is null || input.Metrics.Count == 0)
        {
            missing.Add("metrics");
        }

        return missing;
    }

    private static IEnumerable<MetricEntry> OrderMetricAlternatives(
        IEnumerable<MetricEntry> entries)
    {
        return entries
            .OrderBy(entry => entry.IsDeterministic ? 0 : 1)
            .ThenBy(entry => ExplicitRank(entry.Candidate))
            .ThenByDescending(entry => entry.Candidate.Confidence)
            .ThenBy(
                entry => entry.Candidate.ExtractionStrategy,
                StringComparer.Ordinal)
            .ThenBy(entry => entry.Candidate.Id);
    }

    private static IReadOnlyList<string> DistinctValues(
        IEnumerable<string?> values)
    {
        return values
            .Select(Clean)
            .Where(value => value is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? FirstValue(IEnumerable<string?> values)
    {
        return values
            .Select(Clean)
            .FirstOrDefault(value => value is not null);
    }

    private static MetricKey MetricKeyFor(string? name, string? period)
    {
        var suppliedName = Clean(name) ?? string.Empty;
        var canonicalName = FinancialMetricNameCatalog.TryNormalize(
            suppliedName,
            out var normalizedName)
            ? normalizedName
            : suppliedName.ToLowerInvariant();

        return new MetricKey(
            canonicalName,
            (Clean(period) ?? string.Empty).ToUpperInvariant());
    }

    private static int ExplicitRank(FinancialMetricCandidate candidate)
    {
        return IsExplicit(candidate) ? 0 : 1;
    }

    private static int ExplicitRank(
        FinancialDocumentMetadataCandidate candidate)
    {
        return IsExplicit(candidate) ? 0 : 1;
    }

    private static bool IsExplicit(FinancialMetricCandidate candidate)
    {
        return string.Equals(
                candidate.SourceKind,
                FinancialMetricCandidateSourceKinds.Reported,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                candidate.ReviewState,
                FinancialMetricCandidateReviewStates.Explicit,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExplicit(
        FinancialDocumentMetadataCandidate candidate)
    {
        return string.Equals(
                candidate.SourceKind,
                FinancialMetricCandidateSourceKinds.Reported,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                candidate.ReviewState,
                FinancialMetricCandidateReviewStates.Explicit,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool RequiresCandidateReview(
        FinancialMetricCandidate candidate)
    {
        return string.Equals(
                candidate.SourceKind,
                FinancialMetricCandidateSourceKinds.Inferred,
                StringComparison.OrdinalIgnoreCase) ||
            IsUnresolvedReviewState(candidate.ReviewState);
    }

    private static bool RequiresCandidateReview(
        FinancialDocumentMetadataCandidate candidate)
    {
        return !IsExplicit(candidate);
    }

    private static bool IsUnresolvedReviewState(string reviewState)
    {
        return
            string.Equals(
                reviewState,
                FinancialMetricCandidateReviewStates.Inferred,
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                reviewState,
                FinancialMetricCandidateReviewStates.Conflict,
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                reviewState,
                FinancialMetricCandidateReviewStates.Missing,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool ValuesEqual(string? left, string? right)
    {
        return string.Equals(
            Clean(left),
            Clean(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeValue(string? value)
    {
        return (Clean(value) ?? string.Empty).ToUpperInvariant();
    }

    private static string? Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static string? DecimalText(decimal? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture);
    }

    private static Guid CreateStableId(params string?[] components)
    {
        var payload = string.Join(
            '\u001f',
            components.Select(component => component ?? string.Empty));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));

        return new Guid(hash.AsSpan(0, 16));
    }

    private sealed record MetricKey(string Name, string Period);

    private sealed record MetricEntry(
        MetricKey Key,
        FinancialMetricCandidate Candidate,
        StructuredFinancialMetricInput ProposedMetric,
        bool IsDeterministic);

    private sealed record ProposedMetadata(
        string? Company,
        string? Currency,
        string? Unit);
}
