using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Shared;
using Orchestration.Domain.AnalysisSessions;

namespace Orchestration.Tests;

internal static class TestFinancialReport
{
    internal const string ReportName = "balance-sheet-2025.pdf";
    internal const decimal TotalAmount = 842350.75m;
    internal const int TransactionCount = 187;

    internal static readonly DateTimeOffset SubmittedAt =
        new(2026, 7, 12, 18, 30, 0, TimeSpan.Zero);

    internal static FinancialReportContext CreateContext(
        Guid? sessionId = null,
        FinancialAnalysisContext? financialAnalysis = null)
    {
        return new FinancialReportContext(
            sessionId ?? Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ReportName,
            TotalAmount,
            TransactionCount,
            SubmittedAt,
            financialAnalysis);
    }

    internal static AnalysisSession CreateSession(bool includeStructuredMetrics = false)
    {
        var session = AnalysisSession.Create(Guid.NewGuid());
        SetPersistedContext(session, includeStructuredMetrics);
        return session;
    }

    internal static void SetPersistedContext(
        AnalysisSession session,
        bool includeStructuredMetrics = false)
    {
        session.SetContext(includeStructuredMetrics
            ? $$"""
                {
                  "financialReport": {
                    "reportName": "{{ReportName}}",
                    "totalAmount": {{TotalAmount}},
                    "transactionCount": {{TransactionCount}},
                    "submittedAt": "{{SubmittedAt:O}}"
                  },
                  "structuredFinancialMetrics": {
                    "documentId": "test-metrics",
                    "metrics": [
                      {
                        "name": "revenue",
                        "period": "2024A",
                        "value": 100,
                        "unit": "USD",
                        "statement": "",
                        "source": "test",
                        "confidence": 1
                      }
                    ]
                  }
                }
                """
            : $$"""
                {
                  "financialReport": {
                    "reportName": "{{ReportName}}",
                    "totalAmount": {{TotalAmount}},
                    "transactionCount": {{TransactionCount}},
                    "submittedAt": "{{SubmittedAt:O}}"
                  }
                }
                """);
    }
}
