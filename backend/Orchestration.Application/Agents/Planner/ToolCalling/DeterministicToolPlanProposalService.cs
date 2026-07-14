using System.Globalization;

namespace Orchestration.Application.Agents.Planner.ToolCalling;

public sealed class DeterministicToolPlanProposalService : IToolPlanProposalService
{
    private readonly ToolCallingOptions _options;

    public DeterministicToolPlanProposalService(
        ToolCallingOptions? options = null)
    {
        _options = options ?? new ToolCallingOptions();
    }

    public Task<ToolPlan> ProposeAsync(
        ToolPlanProposalInput input,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return Task.FromResult(new ToolPlan(
                ProposedCalls: [],
                ProposalSource: ToolPlanProposalSource.Deterministic,
                ProposalFallbackReason: null
            ));
        }

        var allowedHandlers = PlannerToolCatalog.GetAllowed(_options)
            .Select(definition => definition.Handler)
            .ToHashSet();
        var calls = new List<ProposedToolCall>();

        if (allowedHandlers.Contains(PlannerToolHandler.AnalyzeTransactions))
        {
            calls.Add(
                new ProposedToolCall(
                    ToolName: PlannerToolCatalog.AnalyzeTransactionsName,
                    Arguments: new Dictionary<string, string>
                    {
                        ["sessionId"] = input.SessionId.ToString(),
                        ["reportName"] = input.ReportName,
                        ["totalAmount"] = input.TotalAmount.ToString(CultureInfo.InvariantCulture),
                        ["transactionCount"] = input.TransactionCount.ToString(CultureInfo.InvariantCulture),
                        ["submittedAt"] = input.SubmittedAt.ToString("O", CultureInfo.InvariantCulture)
                    },
                    Reason: "Analizar senales cuantitativas del reporte financiero para detectar anomalias."
                ));
        }

        if (allowedHandlers.Contains(PlannerToolHandler.SearchCnvRegulation))
        {
            calls.Add(
                new ProposedToolCall(
                    ToolName: PlannerToolCatalog.SearchCnvRegulationName,
                    Arguments: new Dictionary<string, string>(),
                    Reason: "Autorizar una revisión regulatoria CNV derivada del análisis financiero completado."
                ));
        }

        return Task.FromResult(new ToolPlan(
            ProposedCalls: calls,
            ProposalSource: ToolPlanProposalSource.Deterministic,
            ProposalFallbackReason: null
        ));
    }
}
