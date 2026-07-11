using FluentAssertions;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Data.FinancialAnalysis.Extraction;

namespace Orchestration.Tests.Agents.Data.FinancialAnalysis;

public sealed class FinancialMetricCandidateReconcilerTests
{
    [Fact]
    public void FinancialMetricReconciliationResult_Should_default_metadata_candidates_to_empty()
    {
        var result = new FinancialMetricReconciliationResult(
            ProposedInput: Input(),
            Candidates: [],
            Conflicts: [],
            MissingFields: [],
            RequiresReview: false,
            CanAutoAccept: true);

        result.MetadataCandidates.Should().BeEmpty();
    }

    [Fact]
    public void Reconcile_Should_merge_identical_values_and_retain_both_candidates()
    {
        var reconciler = CreateReconciler();
        var deterministic = Input(
            metrics:
            [
                Metric(
                    "revenue",
                    "2024A",
                    100m,
                    confidence: 0.92m,
                    sourcePage: 4)
            ]);
        var semantic = Extraction(
            metrics:
            [
                Candidate(
                    "revenue",
                    "2024A",
                    100m,
                    confidence: 0.98m,
                    sourcePage: 7,
                    evidence: "Revenue 100"),
                Candidate(
                    "revenue",
                    "2024A",
                    100m,
                    sourceKind: FinancialMetricCandidateSourceKinds.Inferred,
                    confidence: 0.999m,
                    sourcePage: 8,
                    evidence: "",
                    reviewState: FinancialMetricCandidateReviewStates.Inferred,
                    inferenceExplanation: "Inferred from a summary chart."),
                Candidate(
                    "revenue",
                    "2024A",
                    100m,
                    sourceKind: FinancialMetricCandidateSourceKinds.Computed,
                    confidence: 1m,
                    sourcePage: 9,
                    evidence: "Computed revenue",
                    reviewState: FinancialMetricCandidateReviewStates.Explicit)
            ]);

        var result = reconciler.Reconcile(
            deterministic,
            semantic,
            AutoAcceptOptions());

        result.ProposedInput.Metrics.Should().ContainSingle();
        result.ProposedInput.Metrics.Single().Should().BeEquivalentTo(
            new
            {
                Name = "revenue",
                Period = "2024A",
                Value = 100m,
                Source = "semantic_markdown_agent",
                SourcePage = 7,
                Confidence = 0.98m
            },
            options => options.ExcludingMissingMembers());
        result.Candidates.Should().HaveCount(4);
        result.Candidates.Should().Contain(candidate =>
            candidate.ExtractionStrategy == "deterministic_pdf_parser" &&
            candidate.SourceKind == FinancialMetricCandidateSourceKinds.Reported &&
            candidate.ReviewState == FinancialMetricCandidateReviewStates.Explicit &&
            candidate.SourcePage == 4 &&
            candidate.Confidence == 0.92m);
        result.Candidates.Should().Contain(candidate =>
            candidate.ExtractionStrategy == "semantic_markdown_agent" &&
            candidate.Evidence == "Revenue 100");
        result.Candidates.Should().Contain(candidate =>
            candidate.SourceKind == FinancialMetricCandidateSourceKinds.Inferred &&
            candidate.ReviewState == FinancialMetricCandidateReviewStates.Inferred);
        result.Candidates.Should().Contain(candidate =>
            candidate.SourceKind == FinancialMetricCandidateSourceKinds.Computed &&
            candidate.ReviewState == FinancialMetricCandidateReviewStates.Explicit);
        result.Conflicts.Should().BeEmpty();
    }

    [Fact]
    public void Reconcile_Should_force_review_for_unsupported_metric_source()
    {
        var reconciler = CreateReconciler();
        var unsupported = Candidate(
            "revenue",
            "2024A",
            100m,
            sourceKind: FinancialMetricCandidateSourceKinds.Computed,
            confidence: 0.999m,
            sourcePage: 9,
            evidence: "Computed revenue",
            reviewState: FinancialMetricCandidateReviewStates.Explicit);

        var result = reconciler.Reconcile(
            Input(
                metrics:
                [
                    Metric(
                        "revenue",
                        "2024A",
                        100m,
                        confidence: 0.95m)
                ]),
            Extraction(metrics: [unsupported]),
            AutoAcceptOptions());

        result.ProposedInput.Metrics.Single().Source.Should()
            .Be("pdf_extraction");
        result.Conflicts.Should().BeEmpty();
        result.Candidates.Single(candidate =>
            candidate.Id == unsupported.Id).Should().Be(unsupported);
        result.RequiresReview.Should().BeTrue();
        result.CanAutoAccept.Should().BeFalse();
    }

    [Fact]
    public void Reconcile_Should_not_create_conflict_from_inferred_unequal_metric()
    {
        var reconciler = CreateReconciler();
        var inferred = Candidate(
            "revenue",
            "2024A",
            110m,
            sourceKind: FinancialMetricCandidateSourceKinds.Inferred,
            confidence: 0.999m,
            reviewState: FinancialMetricCandidateReviewStates.Inferred,
            inferenceExplanation: "Inferred from a chart.");

        var result = reconciler.Reconcile(
            Input(metrics: [Metric("revenue", "2024A", 100m)]),
            Extraction(metrics: [inferred]),
            AutoAcceptOptions());

        result.ProposedInput.Metrics.Single().Value.Should().Be(100m);
        result.Conflicts.Should().BeEmpty();
        result.Candidates.Single(candidate =>
            candidate.ExtractionStrategy == "deterministic_pdf_parser")
            .ReviewState.Should().Be(FinancialMetricCandidateReviewStates.Explicit);
        result.Candidates.Single(candidate => candidate.Id == inferred.Id)
            .Should().Be(inferred);
        result.RequiresReview.Should().BeTrue();
        result.CanAutoAccept.Should().BeFalse();
    }

    [Fact]
    public void Reconcile_Should_choose_stable_proposal_without_conflicting_inferred_only_alternatives()
    {
        var reconciler = CreateReconciler();
        var lowerConfidence = Candidate(
            "revenue",
            "2024A",
            100m,
            sourceKind: FinancialMetricCandidateSourceKinds.Inferred,
            confidence: 0.8m,
            reviewState: FinancialMetricCandidateReviewStates.Inferred,
            inferenceExplanation: "First inference.");
        var higherConfidence = Candidate(
            "revenue",
            "2024A",
            110m,
            sourceKind: FinancialMetricCandidateSourceKinds.Inferred,
            confidence: 0.9m,
            reviewState: FinancialMetricCandidateReviewStates.Inferred,
            inferenceExplanation: "Second inference.");

        var first = reconciler.Reconcile(
            Input(metrics: []),
            Extraction(metrics: [lowerConfidence, higherConfidence]),
            AutoAcceptOptions());
        var second = reconciler.Reconcile(
            Input(metrics: []),
            Extraction(metrics: [higherConfidence, lowerConfidence]),
            AutoAcceptOptions());

        first.Conflicts.Should().BeEmpty();
        second.Conflicts.Should().BeEmpty();
        first.ProposedInput.Metrics.Should()
            .Equal(second.ProposedInput.Metrics);
        first.Candidates.Should().OnlyContain(candidate =>
            candidate.ReviewState == FinancialMetricCandidateReviewStates.Inferred);
        first.RequiresReview.Should().BeTrue();
        first.CanAutoAccept.Should().BeFalse();
    }

    [Fact]
    public void Reconcile_Should_combine_non_overlapping_metrics_in_stable_order()
    {
        var reconciler = CreateReconciler();
        var deterministic = Input(
            metrics:
            [
                Metric("revenue", "2025E", 120m),
                Metric("revenue", "2024A", 100m)
            ]);
        var semantic = Extraction(
            metrics:
            [
                Candidate("net_income", "2024A", 20m),
                Candidate("ebitda", "2024A", 30m)
            ]);

        var result = reconciler.Reconcile(
            deterministic,
            semantic,
            ReviewOnlyOptions());

        result.ProposedInput.Metrics
            .Select(metric => $"{metric.Name}:{metric.Period}")
            .Should()
            .Equal(
                "ebitda:2024A",
                "net_income:2024A",
                "revenue:2024A",
                "revenue:2025E");
        result.Conflicts.Should().BeEmpty();
    }

    [Fact]
    public void Reconcile_Should_report_unequal_values_for_same_metric_and_period()
    {
        var reconciler = CreateReconciler();
        var deterministic = Input(
            metrics: [Metric("revenue", "2024A", 100m)]);
        var semanticCandidate = Candidate(
            "revenue",
            "2024A",
            110m,
            evidence: "Revenue was reported as 110.");
        var semantic = Extraction(
            metrics: [semanticCandidate]);

        var result = reconciler.Reconcile(
            deterministic,
            semantic,
            AutoAcceptOptions());

        result.ProposedInput.Metrics.Single().Value.Should().Be(100m);
        result.Conflicts.Should().ContainSingle();
        result.Conflicts.Single().Should().BeEquivalentTo(
            new
            {
                Kind = "metric",
                FieldName = "value",
                MetricName = "revenue",
                Period = "2024A",
                ProposedValue = "100"
            },
            options => options.ExcludingMissingMembers());
        result.Conflicts.Single().MetricCandidates.Should().HaveCount(2);
        result.Candidates.Should().OnlyContain(candidate =>
            candidate.ReviewState ==
            FinancialMetricCandidateReviewStates.Conflict);
        result.Conflicts.Single().MetricCandidates.Should().OnlyContain(candidate =>
            candidate.ReviewState ==
            FinancialMetricCandidateReviewStates.Conflict);
        result.Conflicts.Single().MetricCandidates.Select(candidate => candidate.Id)
            .Should()
            .Equal(result.Candidates.Select(candidate => candidate.Id));
        result.Candidates.Single(candidate =>
            candidate.Id == semanticCandidate.Id).Should().Be(
            semanticCandidate with
            {
                ReviewState = FinancialMetricCandidateReviewStates.Conflict
            });
        result.RequiresReview.Should().BeTrue();
        result.CanAutoAccept.Should().BeFalse();
    }

    [Fact]
    public void Reconcile_Should_force_review_for_any_inferred_candidate()
    {
        var reconciler = CreateReconciler();
        var semantic = Extraction(
            metrics:
            [
                Candidate(
                    "revenue",
                    "2024A",
                    100m,
                    sourceKind: FinancialMetricCandidateSourceKinds.Inferred,
                    reviewState: FinancialMetricCandidateReviewStates.Inferred,
                    inferenceExplanation: "Derived from the chart axis.")
            ]);

        var result = reconciler.Reconcile(
            Input(metrics: []),
            semantic,
            AutoAcceptOptions());

        result.ProposedInput.Metrics.Should().ContainSingle();
        result.Candidates.Single().SourceKind.Should()
            .Be(FinancialMetricCandidateSourceKinds.Inferred);
        result.Candidates.Single().ReviewState.Should()
            .Be(FinancialMetricCandidateReviewStates.Inferred);
        result.RequiresReview.Should().BeTrue();
        result.CanAutoAccept.Should().BeFalse();
    }

    [Fact]
    public void Reconcile_Should_retain_explicit_upload_metadata_when_semantic_metadata_agrees()
    {
        var reconciler = CreateReconciler();
        var deterministic = Input(
            company: " ACME Holdings ",
            currency: "usd",
            unit: "USD_million");
        var semantic = Extraction(
            metadata:
            [
                Metadata("company", "acme holdings", 0.99m),
                Metadata("currency", "USD", 0.99m),
                Metadata("unit", "usd_million", 0.99m)
            ]);

        var result = reconciler.Reconcile(
            deterministic,
            semantic,
            AutoAcceptOptions());

        result.ProposedInput.Company.Should().Be("ACME Holdings");
        result.ProposedInput.Currency.Should().Be("usd");
        result.ProposedInput.Unit.Should().Be("USD_million");
        result.Conflicts.Should().BeEmpty();
        result.CanAutoAccept.Should().BeTrue();
        result.RequiresReview.Should().BeFalse();
    }

    [Fact]
    public void Reconcile_Should_report_metadata_conflict_and_preserve_all_alternatives()
    {
        var reconciler = CreateReconciler();
        var representative = Metadata("currency", "EUR", 0.95m);
        var alternatives = new[]
        {
            Metadata("currency", "EUR", 0.95m),
            Metadata("currency", "GBP", 0.91m),
            Metadata("company", "Acme", 0.99m)
        };
        var semantic = new FinancialDocumentExtractionResult(
            Company: alternatives[2],
            Currency: representative,
            Unit: null,
            Metrics: [],
            MetadataCandidates: alternatives);

        var result = reconciler.Reconcile(
            Input(currency: "USD"),
            semantic,
            AutoAcceptOptions());

        result.ProposedInput.Currency.Should().Be("USD");
        result.Conflicts.Should().ContainSingle(conflict =>
            conflict.Kind == "metadata" &&
            conflict.FieldName == "currency");
        var conflict = result.Conflicts.Single();
        conflict.MetadataCandidates.Select(candidate => candidate.Value)
            .Should()
            .Equal("USD", "EUR", "EUR", "GBP");
        conflict.MetadataCandidates.Should().Contain(candidate =>
            candidate.Id == representative.Id);
        conflict.MetadataCandidates.Should().OnlyContain(candidate =>
            candidate.ReviewState ==
            FinancialMetricCandidateReviewStates.Conflict);
        result.MetadataCandidates
            .Where(candidate => candidate.FieldName == "currency")
            .Should()
            .OnlyContain(candidate =>
                candidate.ReviewState ==
                FinancialMetricCandidateReviewStates.Conflict);
        result.MetadataCandidates.Single(candidate =>
            candidate.Id == representative.Id).Should().Be(
            representative with
            {
                ReviewState = FinancialMetricCandidateReviewStates.Conflict
            });
        result.MetadataCandidates.Single(candidate =>
            candidate.Id == alternatives[2].Id).Should().Be(alternatives[2]);
        result.RequiresReview.Should().BeTrue();
        result.CanAutoAccept.Should().BeFalse();
    }

    [Fact]
    public void Reconcile_Should_not_use_confidence_to_resolve_unequal_values()
    {
        var reconciler = CreateReconciler();
        var deterministic = Input(
            metrics:
            [
                Metric(
                    "revenue",
                    "2024A",
                    100m,
                    confidence: 0.51m)
            ]);
        var semantic = Extraction(
            metrics:
            [
                Candidate(
                    "revenue",
                    "2024A",
                    999m,
                    confidence: 0.999m)
            ]);

        var result = reconciler.Reconcile(
            deterministic,
            semantic,
            AutoAcceptOptions());

        result.ProposedInput.Metrics.Single().Value.Should().Be(100m);
        result.Conflicts.Should().ContainSingle(conflict =>
            conflict.Kind == "metric" &&
            conflict.FieldName == "value");
        result.CanAutoAccept.Should().BeFalse();
    }

    [Fact]
    public void Reconcile_Should_auto_accept_only_explicit_high_confidence_valid_candidates()
    {
        var validator = new StubValidator(isValid: true);
        var reconciler = CreateReconciler(validator);

        var result = reconciler.Reconcile(
            Input(
                metrics:
                [
                    Metric(
                        "revenue",
                        "2024A",
                        100m,
                        confidence: 0.95m)
                ]),
            null,
            AutoAcceptOptions());

        validator.ValidatedInput.Should().BeSameAs(result.ProposedInput);
        result.CanAutoAccept.Should().BeTrue();
        result.RequiresReview.Should().BeFalse();
    }

    [Fact]
    public void Reconcile_Should_not_auto_accept_defaulted_or_low_confidence_candidates()
    {
        var reconciler = CreateReconciler();

        var defaulted = reconciler.Reconcile(
            Input(metrics: [Metric("revenue", "2024A", 100m, confidence: null)]),
            null,
            AutoAcceptOptions());
        var lowConfidence = reconciler.Reconcile(
            Input(metrics: [Metric("revenue", "2024A", 100m, confidence: 0.89m)]),
            null,
            AutoAcceptOptions());

        defaulted.Candidates.Single().Confidence.Should().Be(0.75m);
        defaulted.CanAutoAccept.Should().BeFalse();
        defaulted.RequiresReview.Should().BeTrue();
        lowConfidence.CanAutoAccept.Should().BeFalse();
        lowConfidence.RequiresReview.Should().BeTrue();
    }

    [Fact]
    public void Reconcile_Should_gate_borrowed_explicit_metric_field_by_supporter_confidence()
    {
        var reconciler = CreateReconciler();
        var lowConfidenceSupporter = Candidate(
            "revenue",
            "2024A",
            100m,
            currency: "EUR",
            confidence: 0.1m,
            evidence: "Revenue reported in EUR");

        var result = reconciler.Reconcile(
            Input(
                metrics:
                [
                    Metric(
                        "revenue",
                        "2024A",
                        100m,
                        currency: null,
                        confidence: 0.95m)
                ]),
            Extraction(metrics: [lowConfidenceSupporter]),
            AutoAcceptOptions());

        result.ProposedInput.Metrics.Single().Currency.Should().Be("EUR");
        result.Conflicts.Should().BeEmpty();
        result.RequiresReview.Should().BeTrue();
        result.CanAutoAccept.Should().BeFalse();
    }

    [Fact]
    public void Reconcile_Should_not_borrow_metric_fields_from_non_explicit_candidates()
    {
        var reconciler = CreateReconciler();
        var inferred = Candidate(
            "revenue",
            "2024A",
            100m,
            currency: "EUR",
            sourceKind: FinancialMetricCandidateSourceKinds.Inferred,
            confidence: 0.999m,
            reviewState: FinancialMetricCandidateReviewStates.Inferred,
            inferenceExplanation: "Inferred currency.");
        var unsupported = Candidate(
            "revenue",
            "2024A",
            100m,
            currency: "GBP",
            sourceKind: FinancialMetricCandidateSourceKinds.Computed,
            confidence: 1m,
            reviewState: FinancialMetricCandidateReviewStates.Explicit);

        var inferredResult = reconciler.Reconcile(
            Input(
                metrics:
                [
                    Metric(
                        "revenue",
                        "2024A",
                        100m,
                        currency: null,
                        confidence: 0.95m)
                ]),
            Extraction(metrics: [inferred]),
            AutoAcceptOptions());
        var unsupportedResult = reconciler.Reconcile(
            Input(
                metrics:
                [
                    Metric(
                        "revenue",
                        "2024A",
                        100m,
                        currency: null,
                        confidence: 0.95m)
                ]),
            Extraction(metrics: [unsupported]),
            AutoAcceptOptions());

        inferredResult.ProposedInput.Metrics.Single().Currency.Should().BeNull();
        unsupportedResult.ProposedInput.Metrics.Single().Currency.Should().BeNull();
        inferredResult.Conflicts.Should().BeEmpty();
        unsupportedResult.Conflicts.Should().BeEmpty();
        inferredResult.CanAutoAccept.Should().BeFalse();
        unsupportedResult.CanAutoAccept.Should().BeFalse();
    }

    [Fact]
    public void Reconcile_Should_auto_accept_field_supported_by_high_confidence_explicit_candidate()
    {
        var reconciler = CreateReconciler();
        var supporter = Candidate(
            "revenue",
            "2024A",
            100m,
            currency: "EUR",
            unit: " usd_MILLION ",
            confidence: 0.95m,
            evidence: "Revenue reported in EUR");

        var result = reconciler.Reconcile(
            Input(
                metrics:
                [
                    Metric(
                        "revenue",
                        "2024A",
                        100m,
                        currency: null,
                        confidence: 0.95m)
                ]),
            Extraction(metrics: [supporter]),
            AutoAcceptOptions());

        result.ProposedInput.Metrics.Single().Currency.Should().Be("EUR");
        result.CanAutoAccept.Should().BeTrue();
        result.RequiresReview.Should().BeFalse();
    }

    [Fact]
    public void Reconcile_Should_require_matching_unit_when_borrowing_currency()
    {
        var reconciler = CreateReconciler();
        var incompatibleSupporter = Candidate(
            "revenue",
            "2024A",
            100m,
            currency: "USD",
            unit: "USD_million",
            confidence: 0.99m,
            evidence: "Revenue reported in USD millions");

        var result = reconciler.Reconcile(
            Input(
                metrics:
                [
                    Metric(
                        "revenue",
                        "2024A",
                        100m,
                        unit: "shares",
                        currency: null,
                        confidence: 0.95m)
                ]),
            Extraction(metrics: [incompatibleSupporter]),
            AutoAcceptOptions());

        result.ProposedInput.Metrics.Single().Should().Match(
            (StructuredFinancialMetricInput metric) =>
                metric.Unit == "shares" &&
                metric.Currency == null);
        result.Candidates.Should().Contain(candidate =>
            candidate.Id == incompatibleSupporter.Id);
        result.Conflicts.Should().ContainSingle(conflict =>
            conflict.Kind == "metric" &&
            conflict.FieldName == "unit" &&
            conflict.MetricCandidates.Any(candidate =>
                candidate.Id == incompatibleSupporter.Id));
    }

    [Fact]
    public void Reconcile_Should_require_matching_currency_when_borrowing_unit()
    {
        var reconciler = CreateReconciler();
        var incompatibleSupporter = Candidate(
            "revenue",
            "2024A",
            100m,
            currency: "USD",
            unit: "USD_million",
            confidence: 0.99m,
            evidence: "Revenue reported in USD millions");

        var result = reconciler.Reconcile(
            Input(
                metrics:
                [
                    Metric(
                        "revenue",
                        "2024A",
                        100m,
                        unit: null,
                        currency: "EUR",
                        confidence: 0.95m)
                ]),
            Extraction(metrics: [incompatibleSupporter]),
            AutoAcceptOptions());

        result.ProposedInput.Metrics.Single().Should().Match(
            (StructuredFinancialMetricInput metric) =>
                metric.Currency == "EUR" &&
                metric.Unit == null);
        result.Candidates.Should().Contain(candidate =>
            candidate.Id == incompatibleSupporter.Id);
        result.Conflicts.Should().ContainSingle(conflict =>
            conflict.Kind == "metric" &&
            conflict.FieldName == "currency" &&
            conflict.MetricCandidates.Any(candidate =>
                candidate.Id == incompatibleSupporter.Id));
    }

    [Fact]
    public void Reconcile_Should_not_combine_fields_from_incompatible_partial_supporters()
    {
        var reconciler = CreateReconciler();
        var currencySupporter = Candidate(
            "revenue",
            "2024A",
            100m,
            currency: "USD",
            unit: null,
            confidence: 0.95m,
            evidence: "Revenue reported in USD");
        var unitSupporter = Candidate(
            "revenue",
            "2024A",
            100m,
            currency: null,
            unit: "shares",
            confidence: 0.94m,
            evidence: "Revenue reported in shares");

        var result = reconciler.Reconcile(
            Input(
                metrics:
                [
                    Metric(
                        "revenue",
                        "2024A",
                        100m,
                        unit: null,
                        currency: null,
                        confidence: 0.99m)
                ]),
            Extraction(metrics: [currencySupporter, unitSupporter]),
            AutoAcceptOptions());

        result.ProposedInput.Metrics.Single().Should().Match(
            (StructuredFinancialMetricInput metric) =>
                metric.Currency == "USD" &&
                metric.Unit == null);
        result.Candidates.Should().Contain(candidate =>
            candidate.Id == currencySupporter.Id);
        result.Candidates.Should().Contain(candidate =>
            candidate.Id == unitSupporter.Id);
    }

    [Fact]
    public void Reconcile_Should_not_borrow_metric_fields_across_explicit_value_conflict()
    {
        var reconciler = CreateReconciler();
        var conflicting = Candidate(
            "revenue",
            "2024A",
            110m,
            currency: "EUR",
            confidence: 0.99m,
            evidence: "Conflicting revenue in EUR");

        var result = reconciler.Reconcile(
            Input(
                metrics:
                [
                    Metric(
                        "revenue",
                        "2024A",
                        100m,
                        currency: null,
                        confidence: 0.95m)
                ]),
            Extraction(metrics: [conflicting]),
            AutoAcceptOptions());

        result.ProposedInput.Metrics.Single().Currency.Should().BeNull();
        result.Conflicts.Should().ContainSingle(conflict =>
            conflict.Kind == "metric" &&
            conflict.FieldName == "value");
        result.CanAutoAccept.Should().BeFalse();
    }

    [Fact]
    public void Reconcile_Should_never_auto_accept_in_review_only_mode()
    {
        var reconciler = CreateReconciler();

        var result = reconciler.Reconcile(
            Input(),
            null,
            ReviewOnlyOptions());

        result.CanAutoAccept.Should().BeFalse();
        result.RequiresReview.Should().BeTrue();
    }

    [Fact]
    public void Reconcile_Should_require_review_when_validator_rejects_proposal()
    {
        var reconciler = CreateReconciler(new StubValidator(isValid: false));

        var result = reconciler.Reconcile(
            Input(),
            null,
            AutoAcceptOptions());

        result.CanAutoAccept.Should().BeFalse();
        result.RequiresReview.Should().BeTrue();
    }

    [Fact]
    public void Reconcile_Should_support_null_semantic_result()
    {
        var reconciler = CreateReconciler();

        var result = reconciler.Reconcile(
            Input(),
            null,
            AutoAcceptOptions());

        result.ProposedInput.Metrics.Should().ContainSingle();
        result.Candidates.Should().ContainSingle(candidate =>
            candidate.ExtractionStrategy == "deterministic_pdf_parser");
        result.Conflicts.Should().BeEmpty();
        result.MissingFields.Should().BeEmpty();
    }

    [Fact]
    public void Reconcile_Should_report_missing_fields_once_in_stable_order()
    {
        var reconciler = CreateReconciler();
        var input = new StructuredFinancialMetricsInput(
            DocumentId: "document-1",
            Company: " ",
            Currency: null,
            Unit: "",
            Metrics: null!);
        var semantic = new FinancialDocumentExtractionResult(
            Company: null,
            Currency: null,
            Unit: null,
            Metrics: null!,
            MetadataCandidates: null);

        var result = reconciler.Reconcile(
            input,
            semantic,
            AutoAcceptOptions());

        result.MissingFields.Should().Equal(
            "company",
            "currency",
            "unit",
            "metrics");
        result.ProposedInput.Metrics.Should().BeEmpty();
        result.RequiresReview.Should().BeTrue();
        result.CanAutoAccept.Should().BeFalse();
    }

    [Fact]
    public void Reconcile_Should_use_metadata_candidates_instead_of_only_representatives()
    {
        var reconciler = CreateReconciler();
        var semantic = new FinancialDocumentExtractionResult(
            Company: Metadata("company", "Acme", 0.99m),
            Currency: Metadata("currency", "USD", 0.99m),
            Unit: Metadata("unit", "USD_million", 0.99m),
            Metrics: [],
            MetadataCandidates:
            [
                Metadata("company", "Acme", 0.99m),
                Metadata("currency", "USD", 0.99m),
                Metadata("currency", "EUR", 0.98m),
                Metadata("unit", "USD_million", 0.99m)
            ]);

        var result = reconciler.Reconcile(
            Input(currency: null),
            semantic,
            AutoAcceptOptions());

        result.ProposedInput.Currency.Should().Be("USD");
        result.Conflicts.Should().ContainSingle(conflict =>
            conflict.Kind == "metadata" &&
            conflict.FieldName == "currency");
        result.Conflicts.Single().MetadataCandidates
            .Select(candidate => candidate.Value)
            .Should()
            .Equal("USD", "USD", "EUR");
        result.CanAutoAccept.Should().BeFalse();
    }

    [Fact]
    public void Reconcile_Should_fallback_to_representative_metadata_when_alternatives_are_empty()
    {
        var reconciler = CreateReconciler();
        var semantic = new FinancialDocumentExtractionResult(
            Company: Metadata("company", "Acme", 0.99m),
            Currency: Metadata("currency", "USD", 0.99m),
            Unit: Metadata("unit", "USD_million", 0.99m),
            Metrics: [Candidate("revenue", "2024A", 100m)],
            MetadataCandidates: []);

        var result = reconciler.Reconcile(
            Input(
                company: " ",
                currency: null,
                unit: null,
                metrics: []),
            semantic,
            AutoAcceptOptions());

        result.ProposedInput.Company.Should().Be("Acme");
        result.ProposedInput.Currency.Should().Be("USD");
        result.ProposedInput.Unit.Should().Be("USD_million");
        result.MissingFields.Should().BeEmpty();
        result.CanAutoAccept.Should().BeFalse();
        result.RequiresReview.Should().BeTrue();
    }

    [Fact]
    public void Reconcile_Should_preserve_all_metadata_candidates_in_stable_order()
    {
        var reconciler = CreateReconciler();
        var agreeingCompany = Metadata("company", "Acme", 0.98m);
        var inferredCompany = Metadata(
            "company",
            "Acme Holdings",
            0.99m,
            FinancialMetricCandidateSourceKinds.Inferred,
            FinancialMetricCandidateReviewStates.Inferred,
            "Expanded from the report title.");
        var conflictingCurrency = Metadata("currency", "EUR", 0.97m);
        var inferredCurrency = Metadata(
            "currency",
            "GBP",
            0.99m,
            FinancialMetricCandidateSourceKinds.Inferred,
            FinancialMetricCandidateReviewStates.Inferred,
            "Inferred from a regional note.");
        var agreeingUnit = Metadata("unit", "USD_million", 0.96m);
        var semantic = new FinancialDocumentExtractionResult(
            Company: agreeingCompany,
            Currency: conflictingCurrency,
            Unit: agreeingUnit,
            Metrics: [],
            MetadataCandidates:
            [
                inferredCurrency,
                agreeingUnit,
                agreeingCompany,
                conflictingCurrency,
                agreeingCompany,
                inferredCompany
            ]);

        var first = reconciler.Reconcile(
            Input(),
            semantic,
            AutoAcceptOptions());
        var second = reconciler.Reconcile(
            Input(),
            semantic with
            {
                MetadataCandidates = semantic.MetadataCandidates!.Reverse().ToArray()
            },
            AutoAcceptOptions());

        first.MetadataCandidates
            .Select(candidate =>
                $"{candidate.FieldName}:{candidate.ExtractionStrategy}:{candidate.SourceKind}:{candidate.Value}")
            .Should()
            .Equal(
                "company:deterministic_pdf_parser:reported:Acme",
                "company:semantic_markdown_agent:reported:Acme",
                "company:semantic_markdown_agent:inferred:Acme Holdings",
                "currency:deterministic_pdf_parser:reported:USD",
                "currency:semantic_markdown_agent:reported:EUR",
                "currency:semantic_markdown_agent:inferred:GBP",
                "unit:deterministic_pdf_parser:reported:USD_million",
                "unit:semantic_markdown_agent:reported:USD_million");
        first.MetadataCandidates.Select(candidate => candidate.Id)
            .Should()
            .Equal(second.MetadataCandidates.Select(candidate => candidate.Id));
        first.MetadataCandidates.Count(candidate =>
            candidate.Id == agreeingCompany.Id).Should().Be(1);
        first.MetadataCandidates.Single(candidate =>
            candidate.Id == agreeingCompany.Id).Should().Be(agreeingCompany);
        first.MetadataCandidates.Single(candidate =>
            candidate.Id == inferredCompany.Id).Should().Be(inferredCompany);
        first.MetadataCandidates.Single(candidate =>
            candidate.Id == inferredCurrency.Id).Should().Be(inferredCurrency);
        first.MetadataCandidates.Single(candidate =>
            candidate.Id == conflictingCurrency.Id).Should().BeEquivalentTo(
            new
            {
                conflictingCurrency.Id,
                conflictingCurrency.Evidence,
                conflictingCurrency.ExtractionStrategy,
                conflictingCurrency.SourceKind,
                conflictingCurrency.SourcePage,
                conflictingCurrency.InferenceExplanation
            });
    }

    [Fact]
    public void Reconcile_Should_force_review_for_inferred_metadata_without_overriding_upload()
    {
        var reconciler = CreateReconciler();
        var inferred = Metadata(
            "company",
            "Different Company",
            0.99m,
            FinancialMetricCandidateSourceKinds.Inferred,
            FinancialMetricCandidateReviewStates.Inferred,
            "Inferred from context.");

        var result = reconciler.Reconcile(
            Input(company: "Acme"),
            Extraction(metadata: [inferred]),
            AutoAcceptOptions());

        result.ProposedInput.Company.Should().Be("Acme");
        result.Conflicts.Should().BeEmpty();
        result.RequiresReview.Should().BeTrue();
        result.CanAutoAccept.Should().BeFalse();
    }

    [Fact]
    public void Reconcile_Should_require_review_when_semantic_metadata_is_the_only_authority()
    {
        var reconciler = CreateReconciler();
        var semanticMetadata = new[]
        {
            Metadata("company", "Acme", 0.99m),
            Metadata("currency", "USD", 0.99m),
            Metadata("unit", "USD_million", 0.99m)
        };

        var result = reconciler.Reconcile(
            Input(company: null, currency: null, unit: null),
            Extraction(metadata: semanticMetadata),
            AutoAcceptOptions());

        result.ProposedInput.Company.Should().Be("Acme");
        result.ProposedInput.Currency.Should().Be("USD");
        result.ProposedInput.Unit.Should().Be("USD_million");
        result.RequiresReview.Should().BeTrue();
        result.CanAutoAccept.Should().BeFalse();
    }

    [Fact]
    public void Reconcile_Should_prefer_explicit_metadata_without_conflicting_with_inferred_alternative()
    {
        var reconciler = CreateReconciler();
        var explicitCandidate = Metadata("currency", "USD", 0.95m);
        var inferredCandidate = Metadata(
            "currency",
            "EUR",
            0.99m,
            FinancialMetricCandidateSourceKinds.Inferred,
            FinancialMetricCandidateReviewStates.Inferred,
            "Inferred from surrounding narrative.");

        var result = reconciler.Reconcile(
            Input(currency: null),
            Extraction(metadata: [inferredCandidate, explicitCandidate]),
            AutoAcceptOptions());

        result.ProposedInput.Currency.Should().Be("USD");
        result.Conflicts.Should().BeEmpty();
        result.RequiresReview.Should().BeTrue();
        result.CanAutoAccept.Should().BeFalse();
    }

    [Fact]
    public void Reconcile_Should_force_review_for_unsupported_metadata_without_creating_conflict()
    {
        var reconciler = CreateReconciler();
        var unsupported = Metadata(
            "currency",
            "EUR",
            0.99m,
            FinancialMetricCandidateSourceKinds.Computed,
            FinancialMetricCandidateReviewStates.Explicit);

        var result = reconciler.Reconcile(
            Input(currency: "USD"),
            Extraction(metadata: [unsupported]),
            AutoAcceptOptions());

        result.ProposedInput.Currency.Should().Be("USD");
        result.Conflicts.Should().BeEmpty();
        result.MetadataCandidates.Should().Contain(candidate =>
            candidate.Id == unsupported.Id &&
            candidate.SourceKind == FinancialMetricCandidateSourceKinds.Computed &&
            candidate.ReviewState == FinancialMetricCandidateReviewStates.Explicit);
        result.RequiresReview.Should().BeTrue();
        result.CanAutoAccept.Should().BeFalse();
    }

    [Fact]
    public void Reconcile_Should_require_review_for_unresolved_candidate_review_state()
    {
        var reconciler = CreateReconciler();
        var semantic = Extraction(
            metrics:
            [
                Candidate(
                    "revenue",
                    "2024A",
                    100m,
                    reviewState: FinancialMetricCandidateReviewStates.Conflict)
            ]);

        var result = reconciler.Reconcile(
            Input(),
            semantic,
            AutoAcceptOptions());

        result.Conflicts.Should().BeEmpty();
        result.RequiresReview.Should().BeTrue();
        result.CanAutoAccept.Should().BeFalse();
    }

    [Fact]
    public void Reconcile_Should_assign_order_independent_deterministic_candidate_ids()
    {
        var reconciler = CreateReconciler();
        var metrics = new[]
        {
            Metric(
                "revenue",
                "2024A",
                100m,
                sourcePage: 4,
                confidence: 0.94m),
            Metric(
                "ebitda",
                "2024A",
                30m,
                sourcePage: 7,
                confidence: 0.97m)
        };

        var first = reconciler.Reconcile(
            Input(metrics: metrics),
            semanticResult: null,
            AutoAcceptOptions());
        var second = reconciler.Reconcile(
            Input(metrics: metrics.Reverse().ToArray()),
            semanticResult: null,
            AutoAcceptOptions());

        first.Candidates
            .Select(candidate =>
                $"{candidate.Name}:{candidate.Period}:{candidate.Value}:{candidate.Id}")
            .Should()
            .Equal(second.Candidates.Select(candidate =>
                $"{candidate.Name}:{candidate.Period}:{candidate.Value}:{candidate.Id}"));
    }

    [Fact]
    public void Reconcile_Should_deduplicate_exact_deterministic_metric_candidates()
    {
        var reconciler = CreateReconciler();
        var duplicate = Metric(
            "revenue",
            "2024A",
            100m,
            sourcePage: 4,
            confidence: 0.94m);

        var result = reconciler.Reconcile(
            Input(metrics: [duplicate, duplicate]),
            semanticResult: null,
            AutoAcceptOptions());

        result.Candidates.Should().ContainSingle();
        result.ProposedInput.Metrics.Should().ContainSingle();
    }

    [Fact]
    public void Reconcile_Should_order_candidates_and_conflicts_deterministically()
    {
        var reconciler = CreateReconciler();
        var semantic = Extraction(
            metrics:
            [
                Candidate("revenue", "2025E", 130m),
                Candidate("ebitda", "2024A", 31m),
                Candidate("revenue", "2024A", 110m)
            ],
            metadata:
            [
                Metadata("unit", "EUR_million", 0.95m),
                Metadata("currency", "EUR", 0.95m)
            ]);
        var input = Input(
            metrics:
            [
                Metric("revenue", "2024A", 100m),
                Metric("ebitda", "2024A", 30m)
            ]);

        var result = reconciler.Reconcile(
            input,
            semantic,
            AutoAcceptOptions());

        result.Candidates
            .Select(candidate =>
                $"{candidate.Name}:{candidate.Period}:{candidate.ExtractionStrategy}")
            .Should()
            .Equal(
                "ebitda:2024A:deterministic_pdf_parser",
                "ebitda:2024A:semantic_markdown_agent",
                "revenue:2024A:deterministic_pdf_parser",
                "revenue:2024A:semantic_markdown_agent",
                "revenue:2025E:semantic_markdown_agent");
        result.Conflicts
            .Select(conflict =>
                $"{conflict.Kind}:{conflict.FieldName}:{conflict.MetricName}:{conflict.Period}")
            .Should()
            .Equal(
                "metadata:currency::",
                "metadata:unit::",
                "metric:value:ebitda:2024A",
                "metric:value:revenue:2024A");
    }

    private static FinancialMetricCandidateReconciler CreateReconciler(
        IStructuredFinancialMetricsValidator? validator = null)
    {
        return new FinancialMetricCandidateReconciler(
            validator ?? new StubValidator(isValid: true));
    }

    private static StructuredFinancialMetricsInput Input(
        string? company = "Acme",
        string? currency = "USD",
        string? unit = "USD_million",
        IReadOnlyList<StructuredFinancialMetricInput>? metrics = null)
    {
        return new StructuredFinancialMetricsInput(
            DocumentId: "document-1",
            Company: company,
            Currency: currency,
            Unit: unit,
            Metrics: metrics ?? [Metric("revenue", "2024A", 100m)]);
    }

    private static StructuredFinancialMetricInput Metric(
        string name,
        string period,
        decimal value,
        string? unit = "USD_million",
        string? currency = "USD",
        string? source = "pdf_extraction",
        int? sourcePage = 1,
        decimal? confidence = 0.95m)
    {
        return new StructuredFinancialMetricInput(
            Name: name,
            Period: period,
            Value: value,
            Unit: unit,
            Currency: currency,
            Source: source,
            SourcePage: sourcePage,
            Confidence: confidence);
    }

    private static FinancialMetricCandidate Candidate(
        string name,
        string period,
        decimal value,
        string? currency = "USD",
        string? unit = "USD_million",
        string sourceKind = FinancialMetricCandidateSourceKinds.Reported,
        decimal confidence = 0.95m,
        int? sourcePage = 2,
        string evidence = "Reported financial metric",
        string reviewState = FinancialMetricCandidateReviewStates.Explicit,
        string? inferenceExplanation = null)
    {
        return new FinancialMetricCandidate(
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
            ExtractionStrategy: "semantic_markdown_agent",
            ReviewState: reviewState,
            InferenceExplanation: inferenceExplanation);
    }

    private static FinancialDocumentMetadataCandidate Metadata(
        string fieldName,
        string value,
        decimal confidence,
        string sourceKind = FinancialMetricCandidateSourceKinds.Reported,
        string reviewState = FinancialMetricCandidateReviewStates.Explicit,
        string? inferenceExplanation = null)
    {
        return new FinancialDocumentMetadataCandidate(
            Id: Guid.NewGuid(),
            FieldName: fieldName,
            Value: value,
            SourceKind: sourceKind,
            Confidence: confidence,
            SourcePage: 1,
            Evidence: value,
            ExtractionStrategy: "semantic_markdown_agent",
            ReviewState: reviewState,
            InferenceExplanation: inferenceExplanation);
    }

    private static FinancialDocumentExtractionResult Extraction(
        IReadOnlyList<FinancialMetricCandidate>? metrics = null,
        IReadOnlyList<FinancialDocumentMetadataCandidate>? metadata = null)
    {
        var metadataCandidates = metadata ?? [];

        return new FinancialDocumentExtractionResult(
            Company: metadataCandidates.FirstOrDefault(candidate =>
                candidate.FieldName == "company"),
            Currency: metadataCandidates.FirstOrDefault(candidate =>
                candidate.FieldName == "currency"),
            Unit: metadataCandidates.FirstOrDefault(candidate =>
                candidate.FieldName == "unit"),
            Metrics: metrics ?? [],
            MetadataCandidates: metadataCandidates);
    }

    private static FinancialMetricsExtractionOptions AutoAcceptOptions()
    {
        return new FinancialMetricsExtractionOptions
        {
            Mode = "AutoAccept",
            AutomaticAcceptanceConfidence = 0.9m
        };
    }

    private static FinancialMetricsExtractionOptions ReviewOnlyOptions()
    {
        return new FinancialMetricsExtractionOptions
        {
            Mode = "ReviewOnly",
            AutomaticAcceptanceConfidence = 0.9m
        };
    }

    private sealed class StubValidator(bool isValid)
        : IStructuredFinancialMetricsValidator
    {
        public StructuredFinancialMetricsInput? ValidatedInput { get; private set; }

        public FinancialMetricsValidationResult Validate(
            StructuredFinancialMetricsInput input)
        {
            ValidatedInput = input;

            return new FinancialMetricsValidationResult(
                IsValid: isValid,
                Metrics: [],
                Errors: isValid
                    ? []
                    :
                    [
                        new FinancialMetricsValidationIssue(
                            Code: "INVALID",
                            Message: "Invalid.",
                            MetricName: null,
                            Period: null,
                            Severity: "Error")
                    ],
                Warnings: []);
        }
    }
}
