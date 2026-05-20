using Microsoft.EntityFrameworkCore;
using Orchestration.Application.Activity;
using Orchestration.Application.Persistence;
using Orchestration.Domain.AnalysisSessions;
using System.Text.Json;
using Orchestration.Application.Agents.Planner;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Legal;
using System.Text.Json.Nodes;

namespace Orchestration.Application.AnalysisSessions
{
    public class AnalysisOrchestratorService
    {
        private readonly IOrchestrationDbContext _dbContext;
        private readonly AnalysisSessionWorkflowService _workflow;
        private readonly IActivityEventPublisher _activityPublisher;
        private readonly IPlannerAgent _plannerAgent;

        public AnalysisOrchestratorService(
            IOrchestrationDbContext dbContext,
            AnalysisSessionWorkflowService workflow,
            IActivityEventPublisher activityPublisher,
            IPlannerAgent plannerAgent)
        {
            _dbContext = dbContext;
            _workflow = workflow;
            _activityPublisher = activityPublisher;
            _plannerAgent = plannerAgent;
        }

        public async Task<AnalysisSessionDto?> StartAnalysisAsync(
            Guid sessionId,
            CancellationToken cancellationToken)
        {
            var session = await _dbContext.AnalysisSessions
                .FirstOrDefaultAsync(x => x.Id == sessionId, cancellationToken);

            if (session is null)
            {
                return null;
            }

            await PublishAsync(
                session.Id,
                "state_transition_requested",
                "Orchestrator",
                "Starting analysis session.",
                cancellationToken
            );

            var dataGatheringStatus = _workflow.ApplyTrigger(
                session,
                AnalysisSessionTrigger.Start
            );

            session.SetStatus(dataGatheringStatus);
            session.SetCurrentAgent("PlannerAgent");

            await _dbContext.SaveChangesAsync(cancellationToken);

            await PublishAsync(
                session.Id,
                "state_changed",
                "Orchestrator",
                $"Session moved to {session.Status}.",
                cancellationToken
            );

            var plannerResult = await _plannerAgent.RunAsync(
                session,
                cancellationToken
            );

            session.SetContext(BuildAnalysisContext(plannerResult, session.ContextJson));

            if (plannerResult.RequiresHumanApproval)
            {
                var awaitingApprovalStatus = _workflow.ApplyTrigger(
                    session,
                    AnalysisSessionTrigger.AnomalyDetected
                );

                session.SetStatus(awaitingApprovalStatus);
                session.SetCurrentAgent(null);

                await _dbContext.SaveChangesAsync(cancellationToken);

                await PublishAsync(
                    session.Id,
                    "human_approval_required",
                    "Orchestrator",
                    "Execution paused. Waiting for human auditor approval.",
                    cancellationToken
                );

                await PublishAsync(
                    session.Id,
                    "state_changed",
                    "Orchestrator",
                    $"Session moved to {session.Status}.",
                    cancellationToken
                );

                return ToDto(session);
            }

            var completedStatus = _workflow.ApplyTrigger(
                session,
                AnalysisSessionTrigger.DataCollected
            );

            session.SetStatus(completedStatus);
            session.SetCurrentAgent("Orchestrator");

            await _dbContext.SaveChangesAsync(cancellationToken);

            await PublishAsync(
                session.Id,
                "state_changed",
                "Orchestrator",
                $"Session moved to {session.Status}.",
                cancellationToken
            );

            await PublishAsync(
                session.Id,
                "analysis_completed",
                "Orchestrator",
                "Analysis completed without requiring human approval.",
                cancellationToken
            );

            return ToDto(session);
        }

        public async Task<AnalysisSessionDto?> ApproveAsync(
            Guid sessionId,
            HumanDecisionDto request,
            CancellationToken cancellationToken)
        {
            var session = await _dbContext.AnalysisSessions
                .FirstOrDefaultAsync(x => x.Id == sessionId, cancellationToken);

            if (session is null)
            {
                return null;
            }

            await PublishAsync(
                session.Id,
                "human_decision_received",
                "HumanAuditor",
                $"Approval received. Reason: {request.Reason ?? "No reason provided."}",
                cancellationToken
            );

            var completedStatus = _workflow.ApplyTrigger(
                session,
                AnalysisSessionTrigger.HumanApproved
            );

            session.SetStatus(completedStatus);
            session.SetCurrentAgent("Orchestrator");

            await _dbContext.SaveChangesAsync(cancellationToken);

            await PublishAsync(
                session.Id,
                "state_changed",
                "Orchestrator",
                $"Session moved to {session.Status}.",
                cancellationToken
            );

            await PublishAsync(
                session.Id,
                "analysis_completed",
                "Orchestrator",
                "Analysis completed after human approval.",
                cancellationToken
            );

            return ToDto(session);
        }

        public async Task<AnalysisSessionDto?> RejectAsync(
            Guid sessionId,
            HumanDecisionDto request,
            CancellationToken cancellationToken)
        {
            var session = await _dbContext.AnalysisSessions
                .FirstOrDefaultAsync(x => x.Id == sessionId, cancellationToken);

            if (session is null)
            {
                return null;
            }

            await PublishAsync(
                session.Id,
                "human_decision_received",
                "HumanAuditor",
                $"Rejection received. Reason: {request.Reason ?? "No reason provided."}",
                cancellationToken
            );

            var failedStatus = _workflow.ApplyTrigger(
                session,
                AnalysisSessionTrigger.HumanRejected
            );

            session.SetStatus(failedStatus);
            session.SetCurrentAgent(null);
            session.MarkFailed(request.Reason ?? "Rejected by human auditor.");

            await _dbContext.SaveChangesAsync(cancellationToken);

            await PublishAsync(
                session.Id,
                "state_changed",
                "Orchestrator",
                $"Session moved to {session.Status}.",
                cancellationToken
            );

            await PublishAsync(
                session.Id,
                "analysis_rejected",
                "Orchestrator",
                "Analysis was rejected by the human auditor.",
                cancellationToken
            );

            return ToDto(session);
        }

        private async Task PublishAsync(
            Guid sessionId,
            string type,
            string agent,
            string message,
            CancellationToken cancellationToken)
        {
            await _activityPublisher.PublishAsync(
                new ActivityEvent(
                    sessionId,
                    type,
                    agent,
                    message,
                    DateTimeOffset.UtcNow
                ),
                cancellationToken
            );
        }

        private static AnalysisSessionDto ToDto(AnalysisSession session)
        {
            return new AnalysisSessionDto(
                session.Id,
                session.Status.ToString(),
                session.CurrentAgent,
                session.ContextJson,
                session.CreatedAt,
                session.UpdatedAt
            );
        }

        internal static string BuildAnalysisContext(
            PlannerAgentResult plannerResult,
            string? existingContextJson = null)
        {
            var context = new
            {
                summary = plannerResult.Summary,
                planner = new
                {
                    engine = plannerResult.ReasoningResult.Engine,
                    summary = plannerResult.ReasoningResult.Summary,
                    recommendedActions = plannerResult.ReasoningResult.RecommendedActions,
                    riskFactors = plannerResult.ReasoningResult.RiskFactors,
                    limitations = plannerResult.ReasoningResult.Limitations,
                    usedLlm = plannerResult.ReasoningResult.UsedLlm,
                    usedFallback = plannerResult.ReasoningResult.UsedFallback,
                    provider = plannerResult.ReasoningResult.Provider,
                    model = plannerResult.ReasoningResult.Model,
                    failureReason = plannerResult.ReasoningResult.FailureReason
                },
                toolPlan = new
                {
                    proposedCalls = plannerResult.ToolPlan.ProposedCalls.Select(call => new
                    {
                        toolName = call.ToolName,
                        arguments = call.Arguments,
                        reason = call.Reason
                    }),
                    approvedCalls = plannerResult.ToolPlan.ApprovedCalls.Select(call => new
                    {
                        toolName = call.ToolName,
                        arguments = call.Arguments,
                        reason = call.Reason
                    }),
                    rejectedCalls = plannerResult.ToolPlan.RejectedCalls.Select(call => new
                    {
                        toolName = call.ToolName,
                        reason = call.Reason
                    }),
                    executedCalls = plannerResult.ToolPlan.ExecutedCalls.Select(call => new
                    {
                        toolName = call.ToolName,
                        status = call.Status.ToString(),
                        succeeded = call.Succeeded,
                        summary = call.Summary,
                        engine = call.Engine,
                        error = call.Error
                    })
                },
                anomaly = new
                {
                    detected = plannerResult.DataResult.HasAnomaly,
                    severity = plannerResult.DataResult.Severity,
                    engine = plannerResult.DataResult.Engine,
                    category = "FinancialTransactionAnomaly",
                    summary = plannerResult.DataResult.Summary,
                    evidence = plannerResult.DataResult.Evidence.Select(e => new
                    {
                        metric = e.Metric,
                        value = e.Value,
                        threshold = e.Threshold,
                        interpretation = e.Interpretation
                    }),
                    recommendation = plannerResult.RequiresHumanApproval
                        ? "Human approval is required before continuing the analysis."
                        : "No human approval is required based on the current data analysis."
                },
                financialAnalysis = plannerResult.DataResult.FinancialAnalysis is null
                    ? null
                    : new
                    {
                        engine = plannerResult.DataResult.FinancialAnalysis.Engine,
                        documentId = plannerResult.DataResult.FinancialAnalysis.DocumentId,
                        company = plannerResult.DataResult.FinancialAnalysis.Company,
                        ratios = plannerResult.DataResult.FinancialAnalysis.Ratios.Select(ratio => new
                        {
                            name = ratio.Name,
                            period = ratio.Period,
                            value = ratio.Value,
                            unit = ratio.Unit,
                            formula = ratio.Formula,
                            source = "computed",
                            inputMetrics = ratio.Inputs,
                            sourcePage = (int?)null,
                            confidence = 0.75m,
                            interpretation = ratio.Interpretation
                        }),
                        comparisons = plannerResult.DataResult.FinancialAnalysis.Comparisons.Select(comparison => new
                        {
                            metricName = comparison.MetricName,
                            fromPeriod = comparison.FromPeriod,
                            toPeriod = comparison.ToPeriod,
                            fromValue = comparison.FromValue,
                            toValue = comparison.ToValue,
                            absoluteChange = comparison.AbsoluteChange,
                            percentageChange = comparison.PercentageChange,
                            unit = comparison.Unit,
                            interpretation = comparison.Interpretation
                        }),
                        riskSignals = plannerResult.DataResult.FinancialAnalysis.RiskSignals.Select(signal =>
                        {
                            var primaryEvidence = signal.Evidence.FirstOrDefault();

                            return new
                            {
                                code = signal.Name,
                                category = "FinancialRiskSignal",
                                severity = signal.Severity,
                                metric = primaryEvidence?.MetricName ?? signal.Name,
                                period = signal.Period,
                                value = primaryEvidence?.Value,
                                threshold = primaryEvidence?.Threshold,
                                explanation = signal.Summary,
                                sourcePage = (int?)null,
                                confidence = 0.75m
                            };
                        }),
                        riskEvidence = plannerResult.DataResult.FinancialAnalysis.RiskEvidence.Select(evidence => new
                        {
                            code = evidence.MetricName,
                            title = evidence.MetricName,
                            severity = ResolveFinancialEvidenceSeverity(
                                evidence,
                                plannerResult.DataResult.FinancialAnalysis.RiskSignals
                            ),
                            message = evidence.Interpretation,
                            metric = evidence.MetricName,
                            period = evidence.Period,
                            value = evidence.Value,
                            threshold = evidence.Threshold,
                            engine = plannerResult.DataResult.FinancialAnalysis.Engine,
                            sourceDocumentId = plannerResult.DataResult.FinancialAnalysis.DocumentId,
                            sourcePage = (int?)null,
                            confidence = 0.75m
                        }),
                        warnings = plannerResult.DataResult.FinancialAnalysis.Warnings,
                        limitations = plannerResult.DataResult.FinancialAnalysis.Limitations
                    },
                compliance = new
                {
                    riskDetected = plannerResult.LegalResult.HasComplianceRisk,
                    riskLevel = plannerResult.LegalResult.RiskLevel,
                    engine = plannerResult.LegalResult.Engine,
                    summary = plannerResult.LegalResult.Summary,
                    warnings = plannerResult.LegalResult.Warnings,
                    evidence = plannerResult.LegalResult.Evidence.Select(e => new
                    {
                        regulation = e.Regulation,
                        section = e.Section,
                        finding = e.Finding,
                        source = e.Source
                    })
                }
            };

            return PreserveStructuredFinancialMetrics(
                JsonSerializer.Serialize(context),
                existingContextJson
            );
        }

        private static string ResolveFinancialEvidenceSeverity(
            RiskEvidenceItem evidence,
            IReadOnlyList<FinancialRiskSignal> signals)
        {
            return signals
                .FirstOrDefault(signal =>
                    signal.Evidence.Any(item =>
                        item.MetricName == evidence.MetricName &&
                        item.Period == evidence.Period
                    )
                )?.Severity ?? "Info";
        }

        private static string PreserveStructuredFinancialMetrics(
            string contextJson,
            string? existingContextJson)
        {
            if (string.IsNullOrWhiteSpace(existingContextJson))
            {
                return contextJson;
            }

            try
            {
                var existingRoot = JsonNode.Parse(existingContextJson) as JsonObject;
                var structuredMetrics = existingRoot?["structuredFinancialMetrics"];

                if (structuredMetrics is null)
                {
                    return contextJson;
                }

                var contextRoot = JsonNode.Parse(contextJson) as JsonObject;

                if (contextRoot is null)
                {
                    return contextJson;
                }

                contextRoot["structuredFinancialMetrics"] =
                    JsonNode.Parse(structuredMetrics.ToJsonString());

                return contextRoot.ToJsonString();
            }
            catch (JsonException)
            {
                return contextJson;
            }
        }
    }
}
