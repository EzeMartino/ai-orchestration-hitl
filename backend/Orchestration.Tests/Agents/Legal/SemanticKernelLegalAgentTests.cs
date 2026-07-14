using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Legal.Cnv;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Shared;
using Orchestration.Infrastructure.Agents.Legal;
using Orchestration.Application.Agents.Legal.Regulations;
using Orchestration.Infrastructure.Agents.Legal.Regulations;

namespace Orchestration.Tests.Agents.Legal;

public class SemanticKernelLegalAgentTests
{
    [Fact]
    public async Task ReviewAsync_Should_preserve_evidence_assessment_without_converting_it_to_compliance_risk()
    {
        var evidenceAssessment = new RegulatoryEvidenceAssessment(
            EvidenceFound: true,
            Relevance: "Strong",
            Applicability: "NotEstablished",
            EvidenceQuality: "Weak",
            Severity: "Warning",
            RequiresHumanReview: true,
            Reasons: ["La evidencia relevante no permite establecer el riesgo."]
        );
        var source = new StubRegulatoryKnowledgeSource(
            new RegulatoryReviewResult(
                HasComplianceRisk: false,
                RiskLevel: "NotEstablished",
                Summary: "La evidencia requiere revisión humana.",
                SourceEngine: "Test regulatory source",
                Findings: [],
                Warnings: [],
                EvidenceAssessment: evidenceAssessment
            )
        );
        var agent = new SemanticKernelLegalAgent(new LegalCompliancePlugin(source));

        var result = await agent.ReviewAsync(
            TestFinancialReport.CreateContext(),
            CancellationToken.None
        );

        result.HasComplianceRisk.Should().BeFalse();
        result.RiskLevel.Should().Be("NotEstablished");
        result.EvidenceAssessment.Should().BeEquivalentTo(evidenceAssessment);
        result.EvidenceAssessment!.RequiresHumanReview.Should().BeTrue();
    }

    [Fact]
    public async Task ReviewAsync_Should_return_compliance_risk_using_semantic_kernel_plugin()
    {
        var services = new ServiceCollection();

        services.AddScoped<IRegulatoryKnowledgeSource, MockRegulatoryKnowledgeSource>();
        services.AddScoped<LegalCompliancePlugin>();
        services.AddScoped<ILegalAgent, SemanticKernelLegalAgent>();

        await using var serviceProvider = services.BuildServiceProvider();

        var agent = serviceProvider.GetRequiredService<ILegalAgent>();

        var report = new FinancialReportContext(
            SessionId: Guid.NewGuid(),
            ReportName: "legal-agent-test-report",
            TotalAmount: 125000m,
            TransactionCount: 42,
            SubmittedAt: DateTimeOffset.UtcNow
        );

        var result = await agent.ReviewAsync(
            report,
            CancellationToken.None
        );

        result.Engine.Should().Be("Semantic Kernel + Fuente regulatoria simulada");
        result.HasComplianceRisk.Should().BeTrue();
        result.RiskLevel.Should().Be("Medium");
        result.Evidence.Should().NotBeEmpty();
        result.Warnings.Should().Contain(
            "Fuente regulatoria simulada. No usar para decisiones legales reales."
        );

        result.Evidence
            .Should()
            .Contain(x => x.Regulation == "Internal AML Policy");
    }

    [Fact]
    public async Task ReviewAsync_OldOverload_UsesDefaultResolutionContext()
    {
        var source = new CapturingRegulatoryKnowledgeSource();
        var agent = new SemanticKernelLegalAgent(new LegalCompliancePlugin(source));
        var report = CreateReport();

        await agent.ReviewAsync(report, CancellationToken.None);

        source.LastRequest.Should().NotBeNull();
        source.LastRequest!.Report.SessionId.Should().Be(report.SessionId);
        source.LastRequest.Report.ReportName.Should().Be(report.ReportName);
        source.LastRequest.Report.TotalAmount.Should().Be(report.TotalAmount);
        source.LastRequest.Report.TransactionCount.Should().Be(
            report.TransactionCount);
        source.LastRequest.Context.ResolutionMode.Should().Be(
            FinancialAnalysisResolutionMode.ProvidedOrPersisted);
        source.LastRequest.Context.DataEvidence.Should().BeNull();
    }

    [Fact]
    public async Task ReviewAsync_ExplicitContext_RoundTripsEvidenceAndHumanReview()
    {
        var source = new CapturingRegulatoryKnowledgeSource(
            requiresHumanReview: true);
        var agent = new SemanticKernelLegalAgent(new LegalCompliancePlugin(source));
        var evidence = new LegalDataEvidenceContext(
            CanUseSignals: false,
            FallbackReason: LegalCnvFallbackReasons.DataToolFailed,
            DataToolStatus: LegalDataToolStatuses.Failed,
            FinancialAnalysisStatus: FinancialAnalysisExecutionStatus.Failed,
            FailedStages:
            [
                new LegalDataStageFailureAudit(
                    FinancialAnalysisOperations.Ratios,
                    FinancialAnalysisFailureCodes.PythonInvocationFailed)
            ]
        );
        var context = new LegalReviewContext(
            FinancialAnalysisResolutionMode.ProvidedOnly,
            evidence
        );
        using var cancellationSource = new CancellationTokenSource();

        var result = await agent.ReviewAsync(
            CreateReport(),
            context,
            cancellationSource.Token
        );

        source.LastRequest.Should().NotBeNull();
        source.LastRequest!.Context.ResolutionMode.Should().Be(
            FinancialAnalysisResolutionMode.ProvidedOnly);
        source.LastRequest.Context.DataEvidence.Should().BeEquivalentTo(evidence);
        source.LastCancellationToken.Should().Be(cancellationSource.Token);
        result.RequiresHumanReview.Should().BeTrue();
    }

    [Theory]
    [InlineData("{ invalid")]
    [InlineData("[]")]
    public async Task Plugin_InvalidDataEvidence_IsSafeAndPreservesProvidedOnly(
        string invalidDataEvidenceJson)
    {
        var source = new CapturingRegulatoryKnowledgeSource();
        var plugin = new LegalCompliancePlugin(source);

        await plugin.ReviewFinancialComplianceAsync(
            reportName: "legal-agent-test-report",
            totalAmount: 125000d,
            transactionCount: 42,
            sessionId: Guid.NewGuid().ToString(),
            financialAnalysisJson: null,
            allowPersistedFinancialAnalysisFallback: false,
            dataEvidenceJson: invalidDataEvidenceJson,
            cancellationToken: CancellationToken.None
        );

        source.LastRequest.Should().NotBeNull();
        source.LastRequest!.Context.ResolutionMode.Should().Be(
            FinancialAnalysisResolutionMode.ProvidedOnly);
        source.LastRequest.Context.DataEvidence.Should().BeNull();
    }

    [Fact]
    public async Task RegulatorySource_DefaultRequestOverload_DelegatesToLegacyDouble()
    {
        var legacySource = new LegacyRegulatoryKnowledgeSource();
        IRegulatoryKnowledgeSource source = legacySource;
        var report = CreateReport();

        await source.ReviewAsync(
            new RegulatoryReviewRequest(
                report,
                LegalReviewContext.Default),
            CancellationToken.None
        );

        legacySource.ReceivedReport.Should().BeSameAs(report);
    }

    [Fact]
    public async Task RegulatorySource_DefaultRequestOverload_RejectsProvidedOnlyContext()
    {
        IRegulatoryKnowledgeSource source = new LegacyRegulatoryKnowledgeSource();

        Func<Task> act = () => source.ReviewAsync(
            new RegulatoryReviewRequest(
                CreateReport(),
                new LegalReviewContext(
                    FinancialAnalysisResolutionMode.ProvidedOnly)),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage(
                "Legacy regulatory knowledge sources support only the default review context.");
    }

    [Fact]
    public async Task RegulatorySource_DefaultRequestOverload_RejectsNullRequest()
    {
        IRegulatoryKnowledgeSource source = new LegacyRegulatoryKnowledgeSource();

        Func<Task> act = () => source.ReviewAsync(
            request: null!,
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ArgumentNullException>();
        exception.Which.ParamName.Should().Be("request");
    }

    [Fact]
    public async Task RegulatorySource_DefaultRequestOverload_RejectsNullReportBeforeContext()
    {
        IRegulatoryKnowledgeSource source = new LegacyRegulatoryKnowledgeSource();
        var request = new RegulatoryReviewRequest(
            Report: null!,
            Context: null!);

        Func<Task> act = () => source.ReviewAsync(
            request,
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ArgumentNullException>();
        exception.Which.ParamName.Should().Be("request.Report");
    }

    [Fact]
    public async Task RegulatorySource_DefaultRequestOverload_RejectsNullContext()
    {
        IRegulatoryKnowledgeSource source = new LegacyRegulatoryKnowledgeSource();

        Func<Task> act = () => source.ReviewAsync(
            new RegulatoryReviewRequest(CreateReport(), Context: null!),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ArgumentNullException>();
        exception.Which.ParamName.Should().Be("request.Context");
    }

    [Fact]
    public async Task RegulatorySource_DefaultRequestOverload_RejectsNullReport()
    {
        IRegulatoryKnowledgeSource source = new LegacyRegulatoryKnowledgeSource();
        var request = new RegulatoryReviewRequest(
            Report: null!,
            LegalReviewContext.Default);

        Func<Task> act = () => source.ReviewAsync(
            request,
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ArgumentNullException>();
        exception.Which.ParamName.Should().Be("request.Report");
    }

    [Fact]
    public async Task RegulatorySource_DefaultRequestOverload_CanceledProvidedOnly_ThrowsCancellation()
    {
        IRegulatoryKnowledgeSource source = new LegacyRegulatoryKnowledgeSource();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        Func<Task> act = () => source.ReviewAsync(
            new RegulatoryReviewRequest(
                CreateReport(),
                new LegalReviewContext(
                    FinancialAnalysisResolutionMode.ProvidedOnly)),
            cancellationSource.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RegulatorySource_DefaultRequestOverload_RejectsDataEvidence()
    {
        IRegulatoryKnowledgeSource source = new LegacyRegulatoryKnowledgeSource();

        Func<Task> act = () => source.ReviewAsync(
            new RegulatoryReviewRequest(
                CreateReport(),
                new LegalReviewContext(
                    FinancialAnalysisResolutionMode.ProvidedOrPersisted,
                    CreateDataEvidence())),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage(
                "Legacy regulatory knowledge sources support only the default review context.");
    }

    [Fact]
    public async Task LegalAgent_DefaultContextOverload_DelegatesToLegacyDouble()
    {
        ILegalAgent agent = new MockLegalAgent();

        var result = await agent.ReviewAsync(
            CreateReport(),
            LegalReviewContext.Default,
            CancellationToken.None
        );

        result.Engine.Should().Be("Mock Compliance Knowledge Base");
    }

    [Fact]
    public async Task LegalAgent_DefaultContextOverload_RejectsProvidedOnlyContext()
    {
        ILegalAgent agent = new MockLegalAgent();

        Func<Task> act = () => agent.ReviewAsync(
            CreateReport(),
            new LegalReviewContext(
                FinancialAnalysisResolutionMode.ProvidedOnly),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage(
                "Legacy legal agent implementations support only the default review context.");
    }

    [Fact]
    public async Task LegalAgent_DefaultContextOverload_RejectsNullContext()
    {
        ILegalAgent agent = new MockLegalAgent();

        Func<Task> act = () => agent.ReviewAsync(
            CreateReport(),
            context: null!,
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ArgumentNullException>();
        exception.Which.ParamName.Should().Be("context");
    }

    [Fact]
    public async Task LegalAgent_DefaultContextOverload_RejectsNullReportBeforeContext()
    {
        ILegalAgent agent = new MockLegalAgent();

        Func<Task> act = () => agent.ReviewAsync(
            report: null!,
            context: null!,
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ArgumentNullException>();
        exception.Which.ParamName.Should().Be("report");
    }

    [Fact]
    public async Task LegalAgent_DefaultContextOverload_CanceledProvidedOnly_ThrowsCancellation()
    {
        ILegalAgent agent = new MockLegalAgent();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        Func<Task> act = () => agent.ReviewAsync(
            CreateReport(),
            new LegalReviewContext(
                FinancialAnalysisResolutionMode.ProvidedOnly),
            cancellationSource.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task LegalAgent_DefaultContextOverload_RejectsDataEvidence()
    {
        ILegalAgent agent = new MockLegalAgent();

        Func<Task> act = () => agent.ReviewAsync(
            CreateReport(),
            new LegalReviewContext(
                FinancialAnalysisResolutionMode.ProvidedOrPersisted,
                CreateDataEvidence()),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage(
                "Legacy legal agent implementations support only the default review context.");
    }

    private static LegalDataEvidenceContext CreateDataEvidence() => new(
        CanUseSignals: true,
        FallbackReason: null,
        DataToolStatus: LegalDataToolStatuses.Executed,
        FinancialAnalysisStatus: FinancialAnalysisExecutionStatus.Succeeded,
        FailedStages: []);

    private static FinancialReportContext CreateReport()
    {
        return new FinancialReportContext(
            SessionId: Guid.NewGuid(),
            ReportName: "legal-agent-test-report",
            TotalAmount: 125000m,
            TransactionCount: 42,
            SubmittedAt: DateTimeOffset.UtcNow
        );
    }

    private sealed class CapturingRegulatoryKnowledgeSource(
        bool requiresHumanReview = false) : IRegulatoryKnowledgeSource
    {
        public RegulatoryReviewRequest? LastRequest { get; private set; }

        public CancellationToken LastCancellationToken { get; private set; }

        public Task<RegulatoryReviewResult> ReviewAsync(
            FinancialReportContext report,
            CancellationToken cancellationToken)
        {
            return ReviewAsync(
                new RegulatoryReviewRequest(report, LegalReviewContext.Default),
                cancellationToken
            );
        }

        public Task<RegulatoryReviewResult> ReviewAsync(
            RegulatoryReviewRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(CreateResult(requiresHumanReview));
        }
    }

    private sealed class LegacyRegulatoryKnowledgeSource : IRegulatoryKnowledgeSource
    {
        public FinancialReportContext? ReceivedReport { get; private set; }

        public Task<RegulatoryReviewResult> ReviewAsync(
            FinancialReportContext report,
            CancellationToken cancellationToken)
        {
            ReceivedReport = report;
            return Task.FromResult(CreateResult(requiresHumanReview: false));
        }
    }

    private static RegulatoryReviewResult CreateResult(bool requiresHumanReview)
    {
        return new RegulatoryReviewResult(
            HasComplianceRisk: false,
            RiskLevel: "Low",
            Summary: "No compliance risk.",
            SourceEngine: "Capturing source",
            Findings: [],
            Warnings: [],
            RequiresHumanReview: requiresHumanReview
        );
    }

    private sealed class StubRegulatoryKnowledgeSource(RegulatoryReviewResult result)
        : IRegulatoryKnowledgeSource
    {
        public Task<RegulatoryReviewResult> ReviewAsync(
            FinancialReportContext report,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(result);
        }
    }
}
