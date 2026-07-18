using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Legal.AiReview;
using Orchestration.Application.Agents.Legal.Cnv;
using Orchestration.Application.Agents.Planner.ToolCalling;
using Orchestration.Application.Agents.Shared;
using Orchestration.Infrastructure.Agents.Planner.ToolCalling;

namespace Orchestration.Tests.Agents.Planner.ToolCalling;

public class ControlledToolExecutorTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Constructor_DependsOnLegalAgentInsteadOfDirectMcpClient()
    {
        var constructor = typeof(ControlledToolExecutor).GetConstructors()
            .Should().ContainSingle().Subject;
        var parameterTypes = constructor.GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        parameterTypes.Should().Contain(typeof(ILegalAgent));
        var dependsOnDirectMcpClient = parameterTypes.Any(parameterType =>
            parameterType.FullName?.Contains(
                "ICnvRegulationMcpClient",
                StringComparison.Ordinal) == true);
        dependsOnDirectMcpClient.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_Should_execute_data_analyze_transactions_with_valid_args()
    {
        var dataAgent = new FakeDataAgent();
        var executor = CreateExecutor(dataAgent);
        var sessionId = Guid.NewGuid();
        var submittedAt = new DateTimeOffset(
            2024, 2, 3, 4, 5, 6, TimeSpan.Zero);

        var results = await executor.ExecuteAsync(
            [
                CreateCall(
                    "data.analyze_transactions",
                    new Dictionary<string, string>
                    {
                        ["sessionId"] = sessionId.ToString(),
                        ["reportName"] = "financial-report",
                        ["totalAmount"] = "125000.50",
                        ["transactionCount"] = "42",
                        ["submittedAt"] = submittedAt.ToString("O")
                    }
                )
            ],
            CancellationToken.None
        );

        var result = results.Should().ContainSingle().Subject;

        result.ToolName.Should().Be("data.analyze_transactions");
        result.Status.Should().Be(ToolExecutionStatus.Executed);
        result.Succeeded.Should().BeTrue();
        result.Summary.Should().Be("Data analysis completed.");
        result.Engine.Should().Be("Fake DataAgent");
        result.Error.Should().BeNull();
        result.OutputJson.Should().NotBe("{}");

        dataAgent.WasCalled.Should().BeTrue();
        dataAgent.ReceivedReport!.SubmittedAt.Should().Be(submittedAt);
        dataAgent.ReceivedReport.Should().BeEquivalentTo(
            new FinancialReportContext(
                sessionId,
                "financial-report",
                125000.50m,
                42,
                submittedAt
            )
        );

        var output = JsonSerializer.Deserialize<DataAgentResult>(
            result.OutputJson,
            JsonOptions
        );

        output.Should().NotBeNull();
        output!.Summary.Should().Be("Data analysis completed.");
    }

    [Fact]
    public async Task ExecuteAsync_Should_return_failure_when_data_required_argument_is_missing()
    {
        var dataAgent = new FakeDataAgent();
        var executor = CreateExecutor(dataAgent);

        var results = await executor.ExecuteAsync(
            [
                CreateCall(
                    "data.analyze_transactions",
                    new Dictionary<string, string>
                    {
                        ["sessionId"] = Guid.NewGuid().ToString(),
                        ["totalAmount"] = "125000",
                        ["transactionCount"] = "42",
                        ["submittedAt"] = DateTimeOffset.UtcNow.ToString("O")
                    }
                )
            ],
            CancellationToken.None
        );

        var result = results.Should().ContainSingle().Subject;

        result.Succeeded.Should().BeFalse();
        result.Status.Should().Be(ToolExecutionStatus.Failed);
        result.Error.Should().Be("Falta el argumento obligatorio: reportName.");
        result.OutputJson.Should().Be("{}");
        dataAgent.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_Should_return_failure_when_total_amount_is_invalid()
    {
        var dataAgent = new FakeDataAgent();
        var executor = CreateExecutor(dataAgent);

        var results = await executor.ExecuteAsync(
            [
                CreateValidDataCall() with
                {
                    Arguments = new Dictionary<string, string>
                    {
                        ["sessionId"] = Guid.NewGuid().ToString(),
                        ["reportName"] = "financial-report",
                        ["totalAmount"] = "not-a-decimal",
                        ["transactionCount"] = "42",
                        ["submittedAt"] = DateTimeOffset.UtcNow.ToString("O")
                    }
                }
            ],
            CancellationToken.None
        );

        var result = results.Should().ContainSingle().Subject;

        result.Succeeded.Should().BeFalse();
        result.Status.Should().Be(ToolExecutionStatus.Failed);
        result.Error.Should().Be("Argumento decimal no válido: totalAmount.");
        result.OutputJson.Should().Be("{}");
        dataAgent.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_LegalWithRuntimeContext_UsesTrustedAggregateAgentContext()
    {
        var dataFinancialAnalysis = CreateFinancialAnalysis("data-document");
        var staleReportFinancialAnalysis = CreateFinancialAnalysis("stale-document");
        var dataResult = CreateDataResult(dataFinancialAnalysis);
        var dataEvidence = CreateDataEvidence();
        var report = CreateReport() with
        {
            FinancialAnalysis = staleReportFinancialAnalysis
        };
        var runtimeContext = new PlannerToolExecutionContext(
            report,
            dataResult,
            dataEvidence
        );
        var legalAgent = new FakeLegalAgent(CreateAggregateLegalResult());
        var executor = CreateExecutor(legalAgent: legalAgent);

        var results = await executor.ExecuteAsync(
            [CreateCall("legal.search_cnv_regulation", new Dictionary<string, string>())],
            CancellationToken.None,
            runtimeContext
        );

        var result = results.Should().ContainSingle().Subject;
        result.Status.Should().Be(ToolExecutionStatus.Executed);
        result.Succeeded.Should().BeTrue();
        result.Summary.Should().Be("Aggregate legal review completed.");
        result.Engine.Should().Be("Aggregate LegalAgent");
        result.Error.Should().BeNull();
        legalAgent.CallCount.Should().Be(1);
        legalAgent.ReceivedReport.Should().NotBeNull();
        legalAgent.ReceivedReport!.FinancialAnalysis.Should()
            .BeSameAs(dataFinancialAnalysis);
        legalAgent.ReceivedReport.FinancialAnalysis.Should()
            .NotBeSameAs(staleReportFinancialAnalysis);
        legalAgent.ReceivedContext.Should().NotBeNull();
        legalAgent.ReceivedContext!.ResolutionMode.Should().Be(
            FinancialAnalysisResolutionMode.ProvidedOnly);
        legalAgent.ReceivedContext.DataEvidence.Should().BeSameAs(dataEvidence);
    }

    [Fact]
    public async Task ExecuteAsync_LegalWithRuntimeContext_LegacyAgentFailsSafely()
    {
        var legalAgent = new LegacyLegalAgent(CreateAggregateLegalResult());
        var executor = CreateExecutor(legalAgent: legalAgent);

        var results = await executor.ExecuteAsync(
            [CreateCall("legal.search_cnv_regulation", new Dictionary<string, string>())],
            CancellationToken.None,
            CreateRuntimeContext());

        var result = results.Should().ContainSingle().Subject;
        result.Status.Should().Be(ToolExecutionStatus.Failed);
        result.Succeeded.Should().BeFalse();
        result.Error.Should().Be("Tool execution failed.");
        result.OutputJson.Should().Be("{}");
        legalAgent.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_Should_reject_removed_granular_tool()
    {
        var executor = CreateExecutor();

        var results = await executor.ExecuteAsync(
            [
                CreateCall(
                    "data.compute_financial_ratios",
                    new Dictionary<string, string>
                    {
                        ["requestJson"] = "{}"
                    })
            ],
            CancellationToken.None
        );

        var result = results.Should().ContainSingle().Subject;

        result.Status.Should().Be(ToolExecutionStatus.Failed);
        result.Succeeded.Should().BeFalse();
        result.Error.Should().Be("Tool is not executable.");
    }

    [Fact]
    public async Task ExecuteAsync_LegalWithoutRuntimeContext_FailsWithoutCallingAgent()
    {
        var legalAgent = new FakeLegalAgent(CreateAggregateLegalResult());
        var executor = CreateExecutor(legalAgent: legalAgent);

        var results = await executor.ExecuteAsync(
            [CreateCall("legal.search_cnv_regulation", new Dictionary<string, string>())],
            CancellationToken.None
        );

        var result = results.Should().ContainSingle().Subject;

        result.Succeeded.Should().BeFalse();
        result.Status.Should().Be(ToolExecutionStatus.Failed);
        result.Error.Should().Be("Trusted planner runtime context is required.");
        result.OutputJson.Should().Be("{}");
        legalAgent.CallCount.Should().Be(0);
    }

    [Theory]
    [InlineData(InvalidTrustedContextPart.Report)]
    [InlineData(InvalidTrustedContextPart.DataResult)]
    [InlineData(InvalidTrustedContextPart.DataEvidence)]
    [InlineData(InvalidTrustedContextPart.FailedStages)]
    [InlineData(InvalidTrustedContextPart.FailedStageItem)]
    public async Task ExecuteAsync_LegalWithStructurallyInvalidRuntimeContext_FailsSafely(
        InvalidTrustedContextPart invalidPart)
    {
        var runtimeContext = CreateInvalidRuntimeContext(invalidPart);
        var legalAgent = new FakeLegalAgent(CreateAggregateLegalResult());
        var executor = CreateExecutor(legalAgent: legalAgent);

        var results = await executor.ExecuteAsync(
            [CreateCall("legal.search_cnv_regulation", new Dictionary<string, string>())],
            CancellationToken.None,
            runtimeContext);

        var result = results.Should().ContainSingle().Subject;
        result.Status.Should().Be(ToolExecutionStatus.Failed);
        result.Succeeded.Should().BeFalse();
        result.Error.Should().Be("Trusted planner runtime context is required.");
        result.OutputJson.Should().Be("{}");
        legalAgent.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_LegalAllowsDataResultWithoutFinancialAnalysis()
    {
        var context = CreateRuntimeContext() with
        {
            DataResult = CreateDataResult(financialAnalysis: null)
        };
        var legalAgent = new FakeLegalAgent(CreateAggregateLegalResult());
        var executor = CreateExecutor(legalAgent: legalAgent);

        var results = await executor.ExecuteAsync(
            [CreateCall("legal.search_cnv_regulation", new Dictionary<string, string>())],
            CancellationToken.None,
            context);

        results.Should().ContainSingle().Which.Succeeded.Should().BeTrue();
        legalAgent.CallCount.Should().Be(1);
        legalAgent.ReceivedReport!.FinancialAnalysis.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_Should_reject_unknown_tool_defensively()
    {
        var dataAgent = new FakeDataAgent();
        var legalAgent = new FakeLegalAgent(CreateAggregateLegalResult());
        var executor = CreateExecutor(dataAgent, legalAgent);

        var results = await executor.ExecuteAsync(
            [
                CreateCall(
                    "workflow.complete",
                    new Dictionary<string, string>()
                )
            ],
            CancellationToken.None
        );

        var result = results.Should().ContainSingle().Subject;

        result.Succeeded.Should().BeFalse();
        result.Status.Should().Be(ToolExecutionStatus.Failed);
        result.Error.Should().Be("Tool is not executable.");
        result.OutputJson.Should().Be("{}");
        dataAgent.WasCalled.Should().BeFalse();
        legalAgent.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_Should_not_throw_for_invalid_input()
    {
        var executor = CreateExecutor();

        var act = async () => await executor.ExecuteAsync(
            [
                CreateValidDataCall() with
                {
                    Arguments = new Dictionary<string, string>
                    {
                        ["sessionId"] = Guid.NewGuid().ToString(),
                        ["reportName"] = "financial-report",
                        ["totalAmount"] = "125000",
                        ["transactionCount"] = "not-an-int",
                        ["submittedAt"] = "not-a-date"
                    }
                },
                CreateCall(
                    "legal.search_cnv_regulation",
                    new Dictionary<string, string>
                    {
                        ["query"] = "agentes",
                        ["limit"] = "not-an-int"
                    }
                )
            ],
            CancellationToken.None
        );

        var exception = await Record.ExceptionAsync(act);

        exception.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_LegalAggregate_RoundTripsThroughRealMapper()
    {
        var legalResult = CreateAggregateLegalResult();
        var executor = CreateExecutor(
            legalAgent: new FakeLegalAgent(legalResult));

        var executed = await executor.ExecuteAsync(
            [CreateCall("legal.search_cnv_regulation", new Dictionary<string, string>())],
            CancellationToken.None,
            CreateRuntimeContext()
        );
        var mapped = new ToolExecutionResultMapper().TryMapLegalResult(executed);

        mapped.Should().NotBeNull();
        mapped!.HasComplianceRisk.Should().Be(legalResult.HasComplianceRisk);
        mapped.RiskLevel.Should().Be(legalResult.RiskLevel);
        mapped.Summary.Should().Be(legalResult.Summary);
        mapped.Engine.Should().Be(legalResult.Engine);
        mapped.Evidence.Should().BeEquivalentTo(legalResult.Evidence);
        mapped.Warnings.Should().Equal(legalResult.Warnings);
        mapped.RequiresHumanReview.Should().BeTrue();
        mapped.LegalReview.Should().BeEquivalentTo(legalResult.LegalReview);

        var queryStrategy = mapped.QueryStrategy.Should()
            .BeOfType<LegalQueryStrategyAudit>().Subject;
        queryStrategy.StrategyVersion.Should().Be("aggregate_strategy_v1");
        queryStrategy.Queries.Should().HaveCount(2);
        queryStrategy.Queries[0].ExecutionStatus.Should()
            .Be(LegalCnvQueryExecutionStatuses.Succeeded);
        queryStrategy.Queries[1].ExecutionStatus.Should()
            .Be(LegalCnvQueryExecutionStatuses.Failed);
        queryStrategy.Queries[1].ResultCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_LegalAgentCancellationWithoutCanceledToken_Propagates()
    {
        var legalAgent = new FakeLegalAgent(
            CreateAggregateLegalResult(),
            cancel: true);
        var executor = CreateExecutor(legalAgent: legalAgent);

        Func<Task> act = async () => await executor.ExecuteAsync(
            [CreateCall("legal.search_cnv_regulation", new Dictionary<string, string>())],
            CancellationToken.None,
            CreateRuntimeContext()
        );

        await act.Should().ThrowAsync<OperationCanceledException>();
        legalAgent.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_DataWithRuntimeContext_RemainsArgumentDriven()
    {
        var dataAgent = new FakeDataAgent();
        var executor = CreateExecutor(dataAgent: dataAgent);
        var call = CreateValidDataCall();

        var results = await executor.ExecuteAsync(
            [call],
            CancellationToken.None,
            CreateRuntimeContext()
        );

        results.Should().ContainSingle().Which.Succeeded.Should().BeTrue();
        dataAgent.WasCalled.Should().BeTrue();
        dataAgent.ReceivedReport.Should().NotBeNull();
        dataAgent.ReceivedReport!.SessionId.Should().Be(
            Guid.Parse(call.Arguments["sessionId"]));
        dataAgent.ReceivedReport.ReportName.Should().Be(
            call.Arguments["reportName"]);
    }

    [Fact]
    public async Task ExecuteAsync_DataIgnoresStructurallyInvalidRuntimeContext()
    {
        var dataAgent = new FakeDataAgent();
        var executor = CreateExecutor(dataAgent: dataAgent);
        var invalidContext = CreateRuntimeContext() with { Report = null! };

        var results = await executor.ExecuteAsync(
            [CreateValidDataCall()],
            CancellationToken.None,
            invalidContext);

        results.Should().ContainSingle().Which.Succeeded.Should().BeTrue();
        dataAgent.WasCalled.Should().BeTrue();
    }

    private static ControlledToolExecutor CreateExecutor(
        FakeDataAgent? dataAgent = null,
        ILegalAgent? legalAgent = null)
    {
        return new ControlledToolExecutor(
            dataAgent ?? new FakeDataAgent(),
            legalAgent ?? new FakeLegalAgent(CreateAggregateLegalResult()),
            NullLogger<ControlledToolExecutor>.Instance
        );
    }

    private static PlannerToolExecutionContext CreateRuntimeContext()
    {
        var financialAnalysis = CreateFinancialAnalysis("data-document");
        return new PlannerToolExecutionContext(
            CreateReport(),
            CreateDataResult(financialAnalysis),
            CreateDataEvidence()
        );
    }

    private static FinancialReportContext CreateReport()
    {
        return new FinancialReportContext(
            Guid.NewGuid(),
            "trusted-runtime-report",
            125000m,
            42,
            DateTimeOffset.UtcNow
        );
    }

    private static DataAgentResult CreateDataResult(
        FinancialAnalysisContext? financialAnalysis)
    {
        return new DataAgentResult(
            true,
            "High",
            "Data analysis completed.",
            "Fake DataAgent",
            [],
            financialAnalysis
        );
    }

    private static PlannerToolExecutionContext CreateInvalidRuntimeContext(
        InvalidTrustedContextPart invalidPart)
    {
        var context = CreateRuntimeContext();
        return invalidPart switch
        {
            InvalidTrustedContextPart.Report => context with { Report = null! },
            InvalidTrustedContextPart.DataResult => context with { DataResult = null! },
            InvalidTrustedContextPart.DataEvidence => context with { DataEvidence = null! },
            InvalidTrustedContextPart.FailedStages => context with
            {
                DataEvidence = context.DataEvidence with { FailedStages = null! }
            },
            InvalidTrustedContextPart.FailedStageItem => context with
            {
                DataEvidence = context.DataEvidence with
                {
                    FailedStages = [null!]
                }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(invalidPart))
        };
    }

    public enum InvalidTrustedContextPart
    {
        Report,
        DataResult,
        DataEvidence,
        FailedStages,
        FailedStageItem
    }

    private static FinancialAnalysisContext CreateFinancialAnalysis(
        string documentId)
    {
        return new FinancialAnalysisContext(
            "Financial engine",
            documentId,
            "ACME",
            [],
            [],
            [],
            [],
            [],
            []
        );
    }

    private static LegalDataEvidenceContext CreateDataEvidence()
    {
        return new LegalDataEvidenceContext(
            true,
            null,
            LegalDataToolStatuses.Executed,
            FinancialAnalysisExecutionStatus.Succeeded,
            []
        );
    }

    private static LegalAgentResult CreateAggregateLegalResult()
    {
        var evidenceReference = new LegalEvidenceReference(
            "CNV",
            "Resolución General 123",
            "https://example.test/cnv",
            "Artículo 1",
            "Texto citado.",
            "Emisoras",
            0.95
        );
        var queryStrategy = new LegalQueryStrategyAudit(
            "aggregate_strategy_v1",
            LegalCnvQuerySources.Contextual,
            null,
            LegalDataToolStatuses.Executed,
            FinancialAnalysisExecutionStatus.Succeeded,
            [],
            [
                new LegalCnvQueryAudit(
                    1, 2, "query one", "Emisoras", "Reason one.", ["signal_one"],
                    LegalCnvQueryExecutionStatuses.Succeeded, 1, 1),
                new LegalCnvQueryAudit(
                    2, 2, "query two", "Emisoras", "Reason two.", ["signal_two"],
                    LegalCnvQueryExecutionStatuses.Failed, 0, 0)
            ]
        );
        var legalReview = new LegalAnalysisReviewResult(
            "Aggregate AI legal review.",
            [
                new PossibleRegulatoryReviewArea(
                    "Disclosure review",
                    "Review disclosure obligations.",
                    "Medium",
                    ["signal_one"],
                    ["Artículo 1"])
            ],
            [evidenceReference],
            ["AI warning"],
            ["AI limitation"],
            true,
            false,
            "provider",
            "model",
            null
        );

        return new LegalAgentResult(
            true,
            "Medium",
            "Aggregate legal review completed.",
            "Aggregate LegalAgent",
            [
                new LegalEvidence(
                    "Resolución General 123",
                    "Artículo 1",
                    "Texto citado.",
                    "CNV")
            ],
            ["Legal warning"],
            queryStrategy,
            legalReview,
            true
        );
    }

    private static ApprovedToolCall CreateValidDataCall()
    {
        return CreateCall(
            "data.analyze_transactions",
            new Dictionary<string, string>
            {
                ["sessionId"] = Guid.NewGuid().ToString(),
                ["reportName"] = "financial-report",
                ["totalAmount"] = "125000",
                ["transactionCount"] = "42",
                ["submittedAt"] = DateTimeOffset.UtcNow.ToString("O")
            }
        );
    }

    private static ApprovedToolCall CreateCall(
        string toolName,
        IReadOnlyDictionary<string, string> arguments)
    {
        return new ApprovedToolCall(
            ToolName: toolName,
            Arguments: arguments,
            Reason: "Approved read-only tool call."
        );
    }

    private sealed class FakeDataAgent : IDataAgent
    {
        public bool WasCalled { get; private set; }

        public FinancialReportContext? ReceivedReport { get; private set; }

        public Task<DataAgentResult> AnalyzeAsync(
            FinancialReportContext report,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            ReceivedReport = report;

            return Task.FromResult(
                new DataAgentResult(
                    HasAnomaly: true,
                    Severity: "High",
                    Summary: "Data analysis completed.",
                    Engine: "Fake DataAgent",
                    Evidence:
                    [
                        new AnomalyEvidence(
                            Metric: "TransactionAmountZScore",
                            Value: 4.5,
                            Threshold: 3.0,
                            Interpretation: "Above threshold."
                        )
                    ]
                )
            );
        }
    }

    private sealed class FakeLegalAgent(
        LegalAgentResult result,
        bool cancel = false) : ILegalAgent
    {
        public int CallCount { get; private set; }
        public FinancialReportContext? ReceivedReport { get; private set; }
        public LegalReviewContext? ReceivedContext { get; private set; }

        public Task<LegalAgentResult> ReviewAsync(
            FinancialReportContext report,
            CancellationToken cancellationToken)
        {
            return ReviewAsync(report, LegalReviewContext.Default, cancellationToken);
        }

        public Task<LegalAgentResult> ReviewAsync(
            FinancialReportContext report,
            LegalReviewContext context,
            CancellationToken cancellationToken)
        {
            CallCount++;
            ReceivedReport = report;
            ReceivedContext = context;
            if (cancel)
            {
                throw new OperationCanceledException(
                    "LegalAgent canceled without token state.");
            }

            return Task.FromResult(result);
        }
    }

    private sealed class LegacyLegalAgent(
        LegalAgentResult result) : ILegalAgent
    {
        public int CallCount { get; private set; }

        public Task<LegalAgentResult> ReviewAsync(
            FinancialReportContext report,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

}
