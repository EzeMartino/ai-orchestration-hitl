using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Infrastructure.Agents.Data.FinancialAnalysis;

namespace Orchestration.Tests.Agents.Data.FinancialAnalysis;

public sealed class CSnakesFinancialAnalysisServiceFailureTests
{
    private static readonly Guid SessionId = Guid.Parse("7f4b62cf-c8b1-459c-a881-e135c80f7f3d");

    public static TheoryData<string> Operations => new()
    {
        FinancialAnalysisOperations.Ratios,
        FinancialAnalysisOperations.Comparisons,
        FinancialAnalysisOperations.Signals,
        FinancialAnalysisOperations.Summary
    };

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task OperationAsync_InvokerThrows_ReturnsSafeFailureAndStructuredLog(
        string operation)
    {
        var invoker = new FakeFinancialAnalysisPythonInvoker(_ =>
            throw new InvalidOperationException("sensitive adapter detail"));
        var logger = new TestCapturingLogger<CSnakesFinancialAnalysisService>();
        var service = new CSnakesFinancialAnalysisService(invoker, logger);

        var result = await InvokeAsync(service, operation, CancellationToken.None);

        result.Execution.Status.Should().Be(FinancialAnalysisExecutionStatus.Failed);
        result.Execution.Operation.Should().Be(operation);
        result.Execution.FailureCode.Should().Be(FinancialAnalysisFailureCodes.PythonInvocationFailed);
        result.Execution.DurationMilliseconds.Should().BeGreaterThanOrEqualTo(0);
        result.Warnings.Should().NotBeEmpty()
            .And.OnlyContain(warning =>
                !string.IsNullOrWhiteSpace(warning) &&
                !warning.Contains("sensitive adapter detail", StringComparison.Ordinal));

        if (operation is FinancialAnalysisOperations.Signals or FinancialAnalysisOperations.Summary)
        {
            result.RiskLevel.Should().Be("Unknown");
        }

        invoker.Calls.Should().ContainSingle().Which.Operation.Should().Be(operation);

        var log = logger.Entries.Should().ContainSingle().Subject;
        log.Level.Should().Be(LogLevel.Error);
        log.Exception.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be("sensitive adapter detail");
        AssertStructuredLog(log, operation, FinancialAnalysisExecutionStatus.Failed,
            FinancialAnalysisFailureCodes.PythonInvocationFailed);
        AssertNoRawPayload(log);
    }

    [Fact]
    public async Task ComputeFinancialRatiosAsync_ValidResponse_LogsSucceededMetadata()
    {
        var invoker = new FakeFinancialAnalysisPythonInvoker(_ =>
            """{"ratios":[],"warnings":[],"limitations":[]}""");
        var logger = new TestCapturingLogger<CSnakesFinancialAnalysisService>();
        var service = new CSnakesFinancialAnalysisService(invoker, logger);

        var response = await service.ComputeFinancialRatiosAsync(
            CreateRatiosRequest(),
            CancellationToken.None);

        response.Execution.Status.Should().Be(FinancialAnalysisExecutionStatus.Succeeded);
        response.Execution.Operation.Should().Be(FinancialAnalysisOperations.Ratios);
        response.Execution.FailureCode.Should().BeNull();
        response.Execution.DurationMilliseconds.Should().BeGreaterThanOrEqualTo(0);

        var log = logger.Entries.Should().ContainSingle().Subject;
        log.Level.Should().Be(LogLevel.Information);
        log.Exception.Should().BeNull();
        AssertStructuredLog(log, FinancialAnalysisOperations.Ratios,
            FinancialAnalysisExecutionStatus.Succeeded, null);
        AssertNoRawPayload(log);
    }

    [Fact]
    public async Task ComputeFinancialRatiosAsync_ValidResponseSuccessLogThrows_PropagatesLoggerFailure()
    {
        var invoker = new FakeFinancialAnalysisPythonInvoker(_ =>
            """{"ratios":[],"warnings":[],"limitations":[]}""");
        var service = new CSnakesFinancialAnalysisService(
            invoker,
            new ThrowingInformationLogger<CSnakesFinancialAnalysisService>());

        await FluentActions.Awaiting(() => service.ComputeFinancialRatiosAsync(
                CreateRatiosRequest(),
                CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("success logger failure");

        invoker.Calls.Should().ContainSingle();
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task OperationAsync_MalformedJson_ReturnsInvalidResponseFailure(
        string operation)
    {
        var invoker = new FakeFinancialAnalysisPythonInvoker(_ => "not-json-sensitive-response");
        var logger = new TestCapturingLogger<CSnakesFinancialAnalysisService>();
        var service = new CSnakesFinancialAnalysisService(invoker, logger);

        var result = await InvokeAsync(service, operation, CancellationToken.None);

        result.Execution.Status.Should().Be(FinancialAnalysisExecutionStatus.Failed);
        result.Execution.Operation.Should().Be(operation);
        result.Execution.FailureCode.Should().Be(FinancialAnalysisFailureCodes.PythonResponseInvalid);
        result.Warnings.Should().OnlyContain(warning =>
            !warning.Contains("not-json-sensitive-response", StringComparison.Ordinal));
        logger.Entries.Should().ContainSingle().Which.Exception.Should().BeAssignableTo<JsonException>();
        AssertStructuredLog(
            logger.Entries.Single(),
            operation,
            FinancialAnalysisExecutionStatus.Failed,
            FinancialAnalysisFailureCodes.PythonResponseInvalid);
        AssertNoRawPayload(logger.Entries.Single());
    }

    [Theory]
    [InlineData(FinancialAnalysisOperations.Ratios, "{}")]
    [InlineData(FinancialAnalysisOperations.Comparisons, "{\"comparisons\":{}}")]
    [InlineData(FinancialAnalysisOperations.Signals, "{\"signals\":null}")]
    [InlineData(FinancialAnalysisOperations.Summary, "{\"evidence\":[]}")]
    public async Task InvalidResponseShape_ReturnsInvalidResponseFailure(
        string operation,
        string responseJson)
    {
        var invoker = new FakeFinancialAnalysisPythonInvoker(_ => responseJson);
        var logger = new TestCapturingLogger<CSnakesFinancialAnalysisService>();
        var service = new CSnakesFinancialAnalysisService(invoker, logger);

        var result = await InvokeAsync(service, operation, CancellationToken.None);

        result.Execution.Status.Should().Be(FinancialAnalysisExecutionStatus.Failed);
        result.Execution.FailureCode.Should().Be(FinancialAnalysisFailureCodes.PythonResponseInvalid);
        logger.Entries.Should().ContainSingle().Which.Exception.Should().BeAssignableTo<JsonException>();
    }

    [Theory]
    [InlineData(
        FinancialAnalysisOperations.Ratios,
        "{\"ratios\":[{\"period\":\"2025E\",\"value\":1.2}]}")]
    [InlineData(
        FinancialAnalysisOperations.Ratios,
        "{\"ratios\":[{\"name\":\"current_ratio\",\"period\":\"2025E\",\"value\":\"not-a-number\"}]}")]
    [InlineData(
        FinancialAnalysisOperations.Comparisons,
        "{\"comparisons\":[{\"basePeriod\":\"2024A\",\"comparisonPeriod\":\"2025E\",\"baseValue\":1,\"comparisonValue\":2,\"absoluteChange\":1}]}")]
    [InlineData(
        FinancialAnalysisOperations.Comparisons,
        "{\"comparisons\":[{\"metricName\":\"revenue\",\"basePeriod\":\"2024A\",\"comparisonPeriod\":\"2025E\",\"baseValue\":\"not-a-number\",\"comparisonValue\":2,\"absoluteChange\":1}]}")]
    [InlineData(
        FinancialAnalysisOperations.Signals,
        "{\"signals\":[{\"severity\":\"High\",\"period\":\"2025E\",\"summary\":\"Risk\",\"evidence\":[]}]}")]
    [InlineData(
        FinancialAnalysisOperations.Signals,
        "{\"signals\":[{\"name\":\"LOW_CURRENT_RATIO\",\"severity\":\"High\",\"period\":\"2025E\",\"summary\":\"Risk\",\"evidence\":[{\"metricName\":\"current_ratio\",\"period\":\"2025E\",\"value\":\"not-a-number\"}]}]}")]
    [InlineData(
        FinancialAnalysisOperations.Summary,
        "{\"summary\":\"Review\",\"evidence\":[{\"period\":\"2025E\",\"value\":1.2}]}")]
    [InlineData(
        FinancialAnalysisOperations.Summary,
        "{\"summary\":\"Review\",\"evidence\":[{\"metricName\":\"current_ratio\",\"period\":\"2025E\",\"value\":\"not-a-number\"}]}")]
    public async Task InvalidRequiredItemField_ReturnsInvalidResponseFailure(
        string operation,
        string responseJson)
    {
        var invoker = new FakeFinancialAnalysisPythonInvoker(_ => responseJson);
        var logger = new TestCapturingLogger<CSnakesFinancialAnalysisService>();
        var service = new CSnakesFinancialAnalysisService(invoker, logger);

        var result = await InvokeAsync(service, operation, CancellationToken.None);

        result.Execution.Status.Should().Be(FinancialAnalysisExecutionStatus.Failed);
        result.Execution.FailureCode.Should().Be(FinancialAnalysisFailureCodes.PythonResponseInvalid);
        logger.Entries.Should().ContainSingle().Which.Exception.Should().BeAssignableTo<JsonException>();
    }

    [Theory]
    [InlineData(FinancialAnalysisOperations.Ratios, "{\"ratios\":[]}")]
    [InlineData(FinancialAnalysisOperations.Comparisons, "{\"comparisons\":[]}")]
    [InlineData(FinancialAnalysisOperations.Signals, "{\"signals\":[]}")]
    [InlineData(FinancialAnalysisOperations.Summary, "{\"summary\":\"No evidence.\",\"evidence\":[]}")]
    public async Task ExplicitEmptyResults_RemainSuccessful(
        string operation,
        string responseJson)
    {
        var invoker = new FakeFinancialAnalysisPythonInvoker(_ => responseJson);
        var logger = new TestCapturingLogger<CSnakesFinancialAnalysisService>();
        var service = new CSnakesFinancialAnalysisService(invoker, logger);

        var result = await InvokeAsync(service, operation, CancellationToken.None);

        result.Execution.Status.Should().Be(FinancialAnalysisExecutionStatus.Succeeded);
        result.Execution.FailureCode.Should().BeNull();
        if (operation == FinancialAnalysisOperations.Signals)
        {
            result.RiskLevel.Should().Be("Low");
        }
    }

    [Fact]
    public async Task SignalWithoutOptionalEvidence_RemainsSuccessful()
    {
        var invoker = new FakeFinancialAnalysisPythonInvoker(_ =>
            """
            {
              "signals": [
                {
                  "name": "LOW_CURRENT_RATIO",
                  "severity": "High",
                  "period": "2025E",
                  "summary": "Risk"
                }
              ]
            }
            """);
        var logger = new TestCapturingLogger<CSnakesFinancialAnalysisService>();
        var service = new CSnakesFinancialAnalysisService(invoker, logger);

        var response = await service.DetectFinancialRiskSignalsAsync(
            new DetectFinancialRiskSignalsRequest([], [], [], SessionId: SessionId),
            CancellationToken.None);

        response.Execution.Status.Should().Be(FinancialAnalysisExecutionStatus.Succeeded);
        response.Signals.Should().ContainSingle()
            .Which.Evidence.Should().BeEmpty();
    }

    [Fact]
    public async Task SignalWithUnsupportedSeverity_ReturnsInvalidResponseFailure()
    {
        var invoker = new FakeFinancialAnalysisPythonInvoker(_ =>
            """
            {
              "signals": [
                {
                  "name": "LOW_CURRENT_RATIO",
                  "severity": "Critical",
                  "period": "2025E",
                  "summary": "Risk",
                  "evidence": []
                }
              ]
            }
            """);
        var logger = new TestCapturingLogger<CSnakesFinancialAnalysisService>();
        var service = new CSnakesFinancialAnalysisService(invoker, logger);

        var response = await service.DetectFinancialRiskSignalsAsync(
            new DetectFinancialRiskSignalsRequest([], [], [], SessionId: SessionId),
            CancellationToken.None);

        response.Execution.Status.Should().Be(FinancialAnalysisExecutionStatus.Failed);
        response.Execution.FailureCode.Should().Be(
            FinancialAnalysisFailureCodes.PythonResponseInvalid);
        response.Result.RiskLevel.Should().Be("Unknown");
        response.Result.Warnings.Should().NotBeEmpty()
            .And.OnlyContain(warning =>
                !string.IsNullOrWhiteSpace(warning) &&
                !warning.Contains("Critical", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("high", "High", "High")]
    [InlineData("HIGH", "High", "High")]
    [InlineData("  High  ", "High", "High")]
    [InlineData("Info", "Info", "Low")]
    public async Task SupportedSignalSeverity_IsNormalized(
        string severity,
        string expectedSeverity,
        string expectedRiskLevel)
    {
        var responseJson = $$"""
            {
              "signals": [
                {
                  "name": "LOW_CURRENT_RATIO",
                  "severity": "{{severity}}",
                  "period": "2025E",
                  "summary": "Risk",
                  "evidence": []
                }
              ]
            }
            """;
        var invoker = new FakeFinancialAnalysisPythonInvoker(_ => responseJson);
        var logger = new TestCapturingLogger<CSnakesFinancialAnalysisService>();
        var service = new CSnakesFinancialAnalysisService(invoker, logger);

        var response = await service.DetectFinancialRiskSignalsAsync(
            new DetectFinancialRiskSignalsRequest([], [], [], SessionId: SessionId),
            CancellationToken.None);

        response.Execution.Status.Should().Be(FinancialAnalysisExecutionStatus.Succeeded);
        response.Signals.Should().ContainSingle()
            .Which.Severity.Should().Be(expectedSeverity);
        response.Result.RiskLevel.Should().Be(expectedRiskLevel);
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task OperationAsync_PreCancelledToken_DoesNotInvokePython(
        string operation)
    {
        var invoker = new FakeFinancialAnalysisPythonInvoker(_ =>
            """{"ratios":[],"comparisons":[],"signals":[],"evidence":[],"summary":"ok"}""");
        var logger = new TestCapturingLogger<CSnakesFinancialAnalysisService>();
        var service = new CSnakesFinancialAnalysisService(invoker, logger);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await FluentActions.Awaiting(() => InvokeAsync(service, operation, cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        invoker.Calls.Should().BeEmpty();
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ComputeFinancialRatiosAsync_InvokerCancelsSuppliedToken_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var invoker = new FakeFinancialAnalysisPythonInvoker(_ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });
        var logger = new TestCapturingLogger<CSnakesFinancialAnalysisService>();
        var service = new CSnakesFinancialAnalysisService(invoker, logger);

        await FluentActions.Awaiting(() => service.ComputeFinancialRatiosAsync(
                CreateRatiosRequest(),
                cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        invoker.Calls.Should().ContainSingle();
        logger.Entries.Should().BeEmpty();
    }

    private static async Task<OperationResult> InvokeAsync(
        IPythonFinancialAnalysisService service,
        string operation,
        CancellationToken cancellationToken)
    {
        switch (operation)
        {
            case FinancialAnalysisOperations.Ratios:
            {
                var response = await service.ComputeFinancialRatiosAsync(
                    CreateRatiosRequest(), cancellationToken);
                return new OperationResult(response.Execution, response.Warnings, null);
            }
            case FinancialAnalysisOperations.Comparisons:
            {
                var response = await service.ComparePeriodsAsync(
                    new ComparePeriodsRequest(
                        [CreateSensitiveMetric()], "2024A", "2025E", ["secret-metric-name"], SessionId),
                    cancellationToken);
                return new OperationResult(response.Execution, response.Warnings, null);
            }
            case FinancialAnalysisOperations.Signals:
            {
                var response = await service.DetectFinancialRiskSignalsAsync(
                    new DetectFinancialRiskSignalsRequest(
                        [CreateSensitiveMetric()], [], [], SessionId: SessionId),
                    cancellationToken);
                return new OperationResult(
                    response.Execution, response.Result.Warnings, response.Result.RiskLevel);
            }
            case FinancialAnalysisOperations.Summary:
            {
                var response = await service.SummarizeQuantitativeEvidenceAsync(
                    new SummarizeQuantitativeEvidenceRequest(
                        [CreateSensitiveMetric()], [], [], [], SessionId: SessionId),
                    cancellationToken);
                return new OperationResult(
                    response.Execution, response.Result.Warnings, response.Result.RiskLevel);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
        }
    }

    private static ComputeFinancialRatiosRequest CreateRatiosRequest()
    {
        return new ComputeFinancialRatiosRequest(
            [CreateSensitiveMetric()], ["secret-ratio-name"], SessionId);
    }

    private static FinancialMetric CreateSensitiveMetric()
    {
        return new FinancialMetric(
            "secret-metric-name", "2025E", 123456.789m, "secret-unit", "secret-statement", "secret-source");
    }

    private static void AssertStructuredLog(
        CapturedLogEntry log,
        string operation,
        FinancialAnalysisExecutionStatus status,
        string? failureCode)
    {
        log.State["Operation"].Should().Be(operation);
        log.State["SessionId"].Should().Be(SessionId);
        log.State["DurationMilliseconds"].Should().BeOfType<long>()
            .Which.Should().BeGreaterThanOrEqualTo(0);
        log.State["ExecutionStatus"].Should().Be(status);
        log.State.Should().ContainKey("FailureCode").WhoseValue.Should().Be(failureCode);
    }

    private static void AssertNoRawPayload(CapturedLogEntry log)
    {
        var exposedText = string.Join(
            " ",
            log.State.Values.Select(value => value?.ToString()).Append(log.Message));

        exposedText.Should().NotContain("secret-metric-name");
        exposedText.Should().NotContain("secret-ratio-name");
        exposedText.Should().NotContain("123456.789");
        exposedText.Should().NotContain("not-json-sensitive-response");
        exposedText.Should().NotContain("{\"");
    }

    private sealed record OperationResult(
        FinancialAnalysisStageExecution Execution,
        IReadOnlyList<string> Warnings,
        string? RiskLevel);

    private sealed class FakeFinancialAnalysisPythonInvoker(Func<string, string> invoke)
        : IFinancialAnalysisPythonInvoker
    {
        public List<Invocation> Calls { get; } = [];

        public string ComputeFinancialRatios(string requestJson) =>
            Invoke(FinancialAnalysisOperations.Ratios, requestJson);

        public string ComparePeriods(string requestJson) =>
            Invoke(FinancialAnalysisOperations.Comparisons, requestJson);

        public string DetectFinancialRiskSignals(string requestJson) =>
            Invoke(FinancialAnalysisOperations.Signals, requestJson);

        public string SummarizeQuantitativeEvidence(string requestJson) =>
            Invoke(FinancialAnalysisOperations.Summary, requestJson);

        private string Invoke(string operation, string requestJson)
        {
            Calls.Add(new Invocation(operation, requestJson));
            return invoke(requestJson);
        }
    }

    private sealed record Invocation(string Operation, string RequestJson);

    private sealed class ThrowingInformationLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Information)
            {
                throw new InvalidOperationException("success logger failure");
            }
        }
    }
}
