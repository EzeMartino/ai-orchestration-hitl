using Microsoft.EntityFrameworkCore;
using Orchestration.Application.Activity;
using Orchestration.Application.Persistence;
using Orchestration.Domain.AnalysisSessions;
using System.Text.Json;

namespace Orchestration.Application.AnalysisSessions
{
    public class AnalysisOrchestratorService
    {
        private readonly IOrchestrationDbContext _dbContext;
        private readonly AnalysisSessionWorkflowService _workflow;
        private readonly IActivityEventPublisher _activityPublisher;

        public AnalysisOrchestratorService(
            IOrchestrationDbContext dbContext,
            AnalysisSessionWorkflowService workflow,
            IActivityEventPublisher activityPublisher)
        {
            _dbContext = dbContext;
            _workflow = workflow;
            _activityPublisher = activityPublisher;
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

            await PublishAsync(
                session.Id,
                "agent_started",
                "PlannerAgent",
                "PlannerAgent initialized. Preparing analysis plan.",
                cancellationToken
            );

            await PublishAsync(
                session.Id,
                "agent_started",
                "DataAgent",
                "DataAgent started anomaly detection over financial signals.",
                cancellationToken
            );

            await Task.Delay(800, cancellationToken);

            await PublishAsync(
                session.Id,
                "anomaly_detected",
                "DataAgent",
                "High-severity anomaly detected. Human approval is required.",
                cancellationToken
            );

            var awaitingApprovalStatus = _workflow.ApplyTrigger(
                session,
                AnalysisSessionTrigger.AnomalyDetected
            );

            session.SetStatus(awaitingApprovalStatus);
            session.SetCurrentAgent(null);
            session.SetContext(BuildAnomalyContext());

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

        private static string BuildAnomalyContext()
        {
            var context = new
            {
                anomaly = new
                {
                    detected = true,
                    severity = "High",
                    category = "FinancialTransactionAnomaly",
                    summary = "Unusual transaction pattern detected in the submitted financial report.",
                    evidence = new[]
                    {
                new
                {
                    metric = "TransactionAmountZScore",
                    value = 4.7,
                    threshold = 3.0,
                    interpretation = "Transaction amount is significantly above expected range."
                },
                new
                {
                    metric = "VelocityScore",
                    value = 0.91,
                    threshold = 0.75,
                    interpretation = "Transaction frequency increased abnormally in a short time window."
                }
            },
                    recommendation = "Human approval is required before continuing the analysis."
                }
            };

            return JsonSerializer.Serialize(context);
        }
    }
}
