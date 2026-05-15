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
            return Task.FromResult(new ToolPlan([]));
        }

        var plan = new ToolPlan(
            [
                new ProposedToolCall(
                    ToolName: "data.analyze_transactions",
                    Arguments: new Dictionary<string, string>
                    {
                        ["sessionId"] = input.SessionId.ToString(),
                        ["reportName"] = input.ReportName,
                        ["totalAmount"] = input.TotalAmount.ToString(CultureInfo.InvariantCulture),
                        ["transactionCount"] = input.TransactionCount.ToString(CultureInfo.InvariantCulture),
                        ["submittedAt"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                    },
                    Reason: "Analizar senales cuantitativas del reporte financiero para detectar anomalias."
                ),
                new ProposedToolCall(
                    ToolName: "legal.search_cnv_regulation",
                    Arguments: new Dictionary<string, string>
                    {
                        ["query"] = "agentes",
                        ["area"] = "Agentes",
                        ["limit"] = "5",
                        ["requiresReview"] = "true"
                    },
                    Reason: "Recuperar evidencia regulatoria CNV citada relacionada con agentes regulados."
                )
            ]
        );

        return Task.FromResult(plan);
    }
}
