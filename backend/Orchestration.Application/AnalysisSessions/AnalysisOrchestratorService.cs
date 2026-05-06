using Microsoft.EntityFrameworkCore;
using Orchestration.Application.Activity;
using Orchestration.Application.Persistence;
using Orchestration.Domain.AnalysisSessions;
using System.Text.Json;
using Orchestration.Application.Agents.Planner;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Legal;

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

            session.SetContext(BuildAnalysisContext(plannerResult));

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

        private static string BuildAnalysisContext(PlannerAgentResult plannerResult)
        {
            var context = new
            {
                summary = plannerResult.Summary,
                anomaly = new
                {
                    detected = plannerResult.DataResult.HasAnomaly,
                    severity = plannerResult.DataResult.Severity,
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
                compliance = new
                {
                    riskDetected = plannerResult.LegalResult.HasComplianceRisk,
                    riskLevel = plannerResult.LegalResult.RiskLevel,
                    summary = plannerResult.LegalResult.Summary,
                    evidence = plannerResult.LegalResult.Evidence.Select(e => new
                    {
                        regulation = e.Regulation,
                        section = e.Section,
                        finding = e.Finding,
                        source = e.Source
                    })
                }
            };

            return JsonSerializer.Serialize(context);
        }
    }
}
