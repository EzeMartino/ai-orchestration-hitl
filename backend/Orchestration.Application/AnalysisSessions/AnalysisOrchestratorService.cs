using Microsoft.EntityFrameworkCore;
using Orchestration.Application.Activity;
using Orchestration.Application.Persistence;
using Orchestration.Domain.AnalysisSessions;
using System.Text.Json;
using Orchestration.Application.Agents.Planner;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Shared;
using System.Text.Json.Nodes;

namespace Orchestration.Application.AnalysisSessions
{
    public class AnalysisOrchestratorService
    {
        private readonly IOrchestrationDbContext _dbContext;
        private readonly AnalysisSessionWorkflowService _workflow;
        private readonly IActivityEventPublisher _activityPublisher;
        private readonly IPlannerAgent _plannerAgent;
        private readonly IFinancialReportContextResolver _reportContextResolver;

        public AnalysisOrchestratorService(
            IOrchestrationDbContext dbContext,
            AnalysisSessionWorkflowService workflow,
            IActivityEventPublisher activityPublisher,
            IPlannerAgent plannerAgent,
            IFinancialReportContextResolver reportContextResolver)
        {
            _dbContext = dbContext;
            _workflow = workflow;
            _activityPublisher = activityPublisher;
            _plannerAgent = plannerAgent;
            _reportContextResolver = reportContextResolver;
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

            var reportResolution = _reportContextResolver.Resolve(session);

            if (!reportResolution.IsValid || reportResolution.Report is null)
            {
                session.SetCurrentAgent(null);
                session.MarkFailed(reportResolution.ErrorMessage!);
                await _dbContext.SaveChangesAsync(cancellationToken);
                await PublishAsync(
                    session.Id,
                    "financial_report_context_invalid",
                    "Orchestrator",
                    reportResolution.ErrorMessage!,
                    cancellationToken);

                return ToDto(session);
            }

            await PublishAsync(
                session.Id,
                "state_transition_requested",
                "Orchestrator",
                "Iniciando sesión de análisis.",
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
                $"La sesión cambió al estado: {session.Status}.",
                cancellationToken
            );

            var plannerResult = await _plannerAgent.RunAsync(
                reportResolution.Report,
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
                    "Ejecución pausada. Esperando la aprobación del auditor humano.",
                    cancellationToken
                );

                await PublishAsync(
                    session.Id,
                    "state_changed",
                    "Orchestrator",
                    $"La sesión cambió al estado: {session.Status}.",
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
                $"La sesión cambió al estado: {session.Status}.",
                cancellationToken
            );

            await PublishAsync(
                session.Id,
                "analysis_completed",
                "Orchestrator",
                "Análisis completado sin requerir aprobación humana.",
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

            var completedStatus = _workflow.ApplyTrigger(
                session,
                AnalysisSessionTrigger.HumanApproved
            );

            await PublishAsync(
                session.Id,
                "human_decision_received",
                "HumanAuditor",
                $"Aprobación recibida. Motivo: {request.Reason ?? "No se proporcionó ningún motivo."}",
                cancellationToken
            );

            session.SetStatus(completedStatus);
            session.SetCurrentAgent("Orchestrator");

            await _dbContext.SaveChangesAsync(cancellationToken);

            await PublishAsync(
                session.Id,
                "state_changed",
                "Orchestrator",
                $"La sesión cambió al estado: {session.Status}.",
                cancellationToken
            );

            await PublishAsync(
                session.Id,
                "analysis_completed",
                "Orchestrator",
                "Análisis completado tras la aprobación humana.",
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

            var failedStatus = _workflow.ApplyTrigger(
                session,
                AnalysisSessionTrigger.HumanRejected
            );

            await PublishAsync(
                session.Id,
                "human_decision_received",
                "HumanAuditor",
                $"Rechazo recibido. Motivo: {request.Reason ?? "No se proporcionó ningún motivo."}",
                cancellationToken
            );

            session.SetStatus(failedStatus);
            session.SetCurrentAgent(null);
            session.MarkFailed(request.Reason ?? "Rechazado por el auditor humano.");

            await _dbContext.SaveChangesAsync(cancellationToken);

            await PublishAsync(
                session.Id,
                "state_changed",
                "Orchestrator",
                $"La sesión cambió al estado: {session.Status}.",
                cancellationToken
            );

            await PublishAsync(
                session.Id,
                "analysis_rejected",
                "Orchestrator",
                "El análisis fue rechazado por el auditor humano.",
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
                        ? "Se requiere aprobación humana antes de continuar con el análisis."
                        : "No se requiere aprobación humana según el análisis de datos actual."
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
                            source = ratio.Source,
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
                                metric = signal.Metric ?? primaryEvidence?.MetricName ?? signal.Name,
                                period = signal.Period,
                                value = signal.Value ?? primaryEvidence?.Value,
                                threshold = signal.ThresholdValue ?? primaryEvidence?.Threshold,
                                thresholdCode = signal.ThresholdCode,
                                thresholdOperator = signal.ThresholdOperator,
                                thresholdValue = signal.ThresholdValue,
                                reason = signal.Reason,
                                explanation = signal.Reason ?? signal.Summary,
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
                        limitations = plannerResult.DataResult.FinancialAnalysis.Limitations,
                        metricsInputSource = plannerResult.DataResult.FinancialAnalysis.MetricsInputSource,
                        metricsProvenance = plannerResult.DataResult.FinancialAnalysis.MetricsProvenance is null
                            ? null
                            : new
                            {
                                ingestionMethod = plannerResult.DataResult.FinancialAnalysis.MetricsProvenance.IngestionMethod,
                                originalFileName = plannerResult.DataResult.FinancialAnalysis.MetricsProvenance.OriginalFileName,
                                fileSizeBytes = plannerResult.DataResult.FinancialAnalysis.MetricsProvenance.FileSizeBytes,
                                contentHash = plannerResult.DataResult.FinancialAnalysis.MetricsProvenance.ContentHash,
                                metricCount = plannerResult.DataResult.FinancialAnalysis.MetricsProvenance.MetricCount,
                                warningCount = plannerResult.DataResult.FinancialAnalysis.MetricsProvenance.WarningCount
                            },
                        aiReview = plannerResult.DataResult.FinancialAnalysis.AiReview is null
                            ? null
                            : new
                            {
                                summary = plannerResult.DataResult.FinancialAnalysis.AiReview.Summary,
                                keyFindings = plannerResult.DataResult.FinancialAnalysis.AiReview.KeyFindings.Select(finding => new
                                {
                                    title = finding.Title,
                                    description = finding.Description,
                                    severity = finding.Severity,
                                    relatedMetrics = finding.RelatedMetrics
                                }),
                                riskInterpretation = plannerResult.DataResult.FinancialAnalysis.AiReview.RiskInterpretation,
                                dataQualityNotes = plannerResult.DataResult.FinancialAnalysis.AiReview.DataQualityNotes.Select(note => new
                                {
                                    message = note.Message,
                                    severity = note.Severity,
                                    relatedFields = note.RelatedFields
                                }),
                                limitations = plannerResult.DataResult.FinancialAnalysis.AiReview.Limitations,
                                usedLlm = plannerResult.DataResult.FinancialAnalysis.AiReview.UsedLlm,
                                usedFallback = plannerResult.DataResult.FinancialAnalysis.AiReview.UsedFallback,
                                provider = plannerResult.DataResult.FinancialAnalysis.AiReview.Provider,
                                model = plannerResult.DataResult.FinancialAnalysis.AiReview.Model,
                                failureReason = plannerResult.DataResult.FinancialAnalysis.AiReview.FailureReason
                            },
                        thresholdProfile = plannerResult.DataResult.FinancialAnalysis.ThresholdProfile,
                        thresholdsUsed = plannerResult.DataResult.FinancialAnalysis.ThresholdsUsed.Select(t => new
                        {
                            code = t.Code,
                            metric = t.Metric,
                            @operator = t.Operator,
                            value = t.Value,
                            severity = t.Severity,
                            description = t.Description
                        }).ToList()
                    },
                compliance = new
                {
                    riskDetected = plannerResult.LegalResult.HasComplianceRisk,
                    riskLevel = plannerResult.LegalResult.RiskLevel,
                    engine = plannerResult.LegalResult.Engine,
                    summary = plannerResult.LegalResult.Summary,
                    warnings = plannerResult.LegalResult.Warnings,
                    queryStrategy = plannerResult.LegalResult.QueryStrategy,
                    evidence = plannerResult.LegalResult.Evidence.Select(e => new
                    {
                        regulation = e.Regulation,
                        section = e.Section,
                        finding = e.Finding,
                        source = e.Source
                    }),
                    legalReview = plannerResult.LegalResult.LegalReview is null
                        ? null
                        : new
                        {
                            reviewSummary = plannerResult.LegalResult.LegalReview.ReviewSummary,
                            possibleRegulatoryReviewAreas = plannerResult.LegalResult.LegalReview.PossibleRegulatoryReviewAreas.Select(area => new
                            {
                                title = area.Title,
                                description = area.Description,
                                severity = area.Severity,
                                relatedFinancialSignals = area.RelatedFinancialSignals,
                                evidenceCitations = area.EvidenceCitations
                            }),
                            evidenceReferences = plannerResult.LegalResult.LegalReview.EvidenceReferences.Select(ev => new
                            {
                                source = ev.Source,
                                title = ev.Title,
                                url = ev.Url,
                                citation = ev.Citation,
                                snippet = ev.Snippet,
                                regulationArea = ev.RegulationArea,
                                score = ev.Score
                            }),
                            warnings = plannerResult.LegalResult.LegalReview.Warnings,
                            limitations = plannerResult.LegalResult.LegalReview.Limitations,
                            usedLlm = plannerResult.LegalResult.LegalReview.UsedLlm,
                            usedFallback = plannerResult.LegalResult.LegalReview.UsedFallback,
                            provider = plannerResult.LegalResult.LegalReview.Provider,
                            model = plannerResult.LegalResult.LegalReview.Model,
                            failureReason = plannerResult.LegalResult.LegalReview.FailureReason
                        }
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
