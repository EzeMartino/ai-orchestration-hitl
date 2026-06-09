using System.Text.Json;
using FluentAssertions;
using Microsoft.SemanticKernel;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Infrastructure.Agents.Data;

namespace Orchestration.Tests.Agents.Data.FinancialAnalysis;

public sealed class FinancialAnalysisPluginTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ComputeFinancialRatiosAsync_Should_call_service_and_return_ratios()
    {
        var service = new FakeFinancialAnalysisService();
        var plugin = new FinancialAnalysisPlugin(service);
        var request = new ComputeFinancialRatiosRequest(
            Metrics: [CreateMetric("revenue", "2025E", 100m)],
            RequestedRatios: ["gross_margin"]
        );

        var response = await plugin.ComputeFinancialRatiosAsync(
            JsonSerializer.Serialize(request, JsonOptions),
            CancellationToken.None
        );

        service.ComputeRequest.Should().BeEquivalentTo(request);
        response.Ratios.Should().ContainSingle()
            .Which.Name.Should().Be("gross_margin");
    }

    [Fact]
    public async Task ComparePeriodsAsync_Should_call_service_and_return_comparisons()
    {
        var service = new FakeFinancialAnalysisService();
        var plugin = new FinancialAnalysisPlugin(service);
        var request = new ComparePeriodsRequest(
            Metrics:
            [
                CreateMetric("revenue", "2024A", 100m),
                CreateMetric("revenue", "2025E", 120m)
            ],
            FromPeriod: "2024A",
            ToPeriod: "2025E",
            MetricNames: ["revenue"]
        );

        var response = await plugin.ComparePeriodsAsync(
            JsonSerializer.Serialize(request, JsonOptions),
            CancellationToken.None
        );

        service.CompareRequest.Should().BeEquivalentTo(request);
        response.Comparisons.Should().ContainSingle()
            .Which.MetricName.Should().Be("revenue");
    }

    [Fact]
    public async Task DetectFinancialRiskSignalsAsync_Should_call_service_and_return_signals()
    {
        var service = new FakeFinancialAnalysisService();
        var plugin = new FinancialAnalysisPlugin(service);
        var request = new DetectFinancialRiskSignalsRequest(
            Metrics: [CreateMetric("current_assets", "2025E", 40m)],
            Ratios: [],
            Comparisons: []
        );

        var response = await plugin.DetectFinancialRiskSignalsAsync(
            JsonSerializer.Serialize(request, JsonOptions),
            CancellationToken.None
        );

        service.SignalsRequest.Should().BeEquivalentTo(request);
        response.Signals.Should().ContainSingle()
            .Which.Name.Should().Be("LOW_CURRENT_RATIO");
    }

    [Fact]
    public async Task SummarizeQuantitativeEvidenceAsync_Should_call_service_and_return_evidence()
    {
        var service = new FakeFinancialAnalysisService();
        var plugin = new FinancialAnalysisPlugin(service);
        var request = new SummarizeQuantitativeEvidenceRequest(
            Metrics: [],
            Ratios: [],
            Comparisons: [],
            Signals: [],
            MaxItems: 1
        );

        var response = await plugin.SummarizeQuantitativeEvidenceAsync(
            JsonSerializer.Serialize(request, JsonOptions),
            CancellationToken.None
        );

        service.SummaryRequest.Should().BeEquivalentTo(request);
        response.Result.Evidence.Should().ContainSingle()
            .Which.MetricName.Should().Be("net_debt_to_ebitda");
    }

    [Fact]
    public async Task ComputeFinancialRatiosAsync_Should_return_safe_warning_response_for_invalid_json()
    {
        var service = new FakeFinancialAnalysisService();
        var plugin = new FinancialAnalysisPlugin(service);

        var response = await plugin.ComputeFinancialRatiosAsync(
            "{not-json",
            CancellationToken.None
        );

        service.ComputeRequest.Should().BeNull();
        response.Ratios.Should().BeEmpty();
        response.Warnings.Should().Contain("JSON de solicitud para calcular ratios financieros no válido.");
        response.Warnings.Should().Contain("No se pudieron calcular los ratios financieros a partir de la solicitud provista.");
    }

    [Fact]
    public async Task ComparePeriodsAsync_Should_return_safe_warning_response_for_empty_json()
    {
        var service = new FakeFinancialAnalysisService();
        var plugin = new FinancialAnalysisPlugin(service);

        var response = await plugin.ComparePeriodsAsync(
            "",
            CancellationToken.None
        );

        service.CompareRequest.Should().BeNull();
        response.Comparisons.Should().BeEmpty();
        response.Warnings.Should().Contain("JSON de solicitud para comparar periodos no válido.");
    }

    [Fact]
    public async Task DetectFinancialRiskSignalsAsync_Should_return_safe_warning_response_for_null_json()
    {
        var service = new FakeFinancialAnalysisService();
        var plugin = new FinancialAnalysisPlugin(service);

        var response = await plugin.DetectFinancialRiskSignalsAsync(
            null!,
            CancellationToken.None
        );

        service.SignalsRequest.Should().BeNull();
        response.Signals.Should().BeEmpty();
        response.Result.Warnings.Should().Contain("JSON de solicitud para detectar señales de riesgo financiero no válido.");
    }

    [Fact]
    public async Task KernelInvocation_Should_invoke_compute_financial_ratios_function()
    {
        var service = new FakeFinancialAnalysisService();
        var plugin = new FinancialAnalysisPlugin(service);
        var kernel = new Kernel();
        kernel.Plugins.AddFromObject(plugin, "FinancialAnalysis");
        var request = new ComputeFinancialRatiosRequest(
            Metrics: [CreateMetric("revenue", "2025E", 100m)],
            RequestedRatios: ["gross_margin"]
        );
        var arguments = new KernelArguments
        {
            ["requestJson"] = JsonSerializer.Serialize(request, JsonOptions)
        };

        var response = await kernel.InvokeAsync<ComputeFinancialRatiosResponse>(
            "FinancialAnalysis",
            "data_compute_financial_ratios",
            arguments,
            CancellationToken.None
        );

        service.ComputeRequest.Should().BeEquivalentTo(request);
        response.Should().NotBeNull();
        response!.Ratios.Should().ContainSingle()
            .Which.Name.Should().Be("gross_margin");
    }

    private static FinancialMetric CreateMetric(
        string name,
        string period,
        decimal value)
    {
        return new FinancialMetric(
            Name: name,
            Period: period,
            Value: value,
            Unit: "USD millions",
            Statement: "unit_test",
            Source: "unit_test"
        );
    }

    private sealed class FakeFinancialAnalysisService : IPythonFinancialAnalysisService
    {
        public ComputeFinancialRatiosRequest? ComputeRequest { get; private set; }
        public ComparePeriodsRequest? CompareRequest { get; private set; }
        public DetectFinancialRiskSignalsRequest? SignalsRequest { get; private set; }
        public SummarizeQuantitativeEvidenceRequest? SummaryRequest { get; private set; }

        public Task<ComputeFinancialRatiosResponse> ComputeFinancialRatiosAsync(
            ComputeFinancialRatiosRequest request,
            CancellationToken cancellationToken)
        {
            ComputeRequest = request;

            return Task.FromResult(new ComputeFinancialRatiosResponse(
                Engine: "Fake Financial Analysis",
                Ratios:
                [
                    new FinancialRatio(
                        Name: "gross_margin",
                        Period: "2025E",
                        Value: 0.42m,
                        Unit: "ratio",
                        Formula: "gross_profit / revenue",
                        Inputs: ["gross_profit", "revenue"],
                        Interpretation: "Gross margin computed for test."
                    )
                ],
                Warnings: []
            ));
        }

        public Task<ComparePeriodsResponse> ComparePeriodsAsync(
            ComparePeriodsRequest request,
            CancellationToken cancellationToken)
        {
            CompareRequest = request;

            return Task.FromResult(new ComparePeriodsResponse(
                Engine: "Fake Financial Analysis",
                Comparisons:
                [
                    new FinancialPeriodComparison(
                        MetricName: "revenue",
                        FromPeriod: "2024A",
                        ToPeriod: "2025E",
                        FromValue: 100m,
                        ToValue: 120m,
                        AbsoluteChange: 20m,
                        PercentageChange: 0.2m,
                        Unit: "USD millions",
                        Interpretation: "Revenue increased."
                    )
                ],
                Warnings: []
            ));
        }

        public Task<DetectFinancialRiskSignalsResponse> DetectFinancialRiskSignalsAsync(
            DetectFinancialRiskSignalsRequest request,
            CancellationToken cancellationToken)
        {
            SignalsRequest = request;
            var evidence = new RiskEvidenceItem(
                MetricName: "current_ratio",
                Period: "2025E",
                Value: 0.8m,
                Threshold: 1.0m,
                Unit: "x",
                Interpretation: "Current ratio requires review."
            );

            return Task.FromResult(new DetectFinancialRiskSignalsResponse(
                Engine: "Fake Financial Analysis",
                Signals:
                [
                    new FinancialRiskSignal(
                        Name: "LOW_CURRENT_RATIO",
                        Severity: "Medium",
                        Period: "2025E",
                        Summary: "Liquidity should be reviewed.",
                        Evidence: [evidence]
                    )
                ],
                Result: new FinancialAnalysisToolResult(
                    HasRiskSignals: true,
                    RiskLevel: "Medium",
                    Summary: "Liquidity should be reviewed.",
                    Engine: "Fake Financial Analysis",
                    Evidence: [evidence],
                    Warnings: []
                )
            ));
        }

        public Task<SummarizeQuantitativeEvidenceResponse> SummarizeQuantitativeEvidenceAsync(
            SummarizeQuantitativeEvidenceRequest request,
            CancellationToken cancellationToken)
        {
            SummaryRequest = request;
            var evidence = new RiskEvidenceItem(
                MetricName: "net_debt_to_ebitda",
                Period: "2025E",
                Value: 3.5m,
                Threshold: 3.0m,
                Unit: "x",
                Interpretation: "Leverage should be reviewed."
            );

            return Task.FromResult(new SummarizeQuantitativeEvidenceResponse(
                Engine: "Fake Financial Analysis",
                Narrative: "Quantitative evidence summarized.",
                Result: new FinancialAnalysisToolResult(
                    HasRiskSignals: true,
                    RiskLevel: "High",
                    Summary: "Quantitative evidence summarized.",
                    Engine: "Fake Financial Analysis",
                    Evidence: [evidence],
                    Warnings: []
                )
            ));
        }
    }
}
