using System.Text.Json;
using CSnakes.Runtime;
using Orchestration.Application.Agents.Data.FinancialAnalysis.Extraction;

namespace Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.Extraction;

public sealed class CSnakesFinancialDocumentMarkdownConverter : IFinancialDocumentMarkdownConverter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDocumentMarkdownModule _module;

    public CSnakesFinancialDocumentMarkdownConverter(IPythonEnvironment pythonEnvironment)
        : this(new CSnakesDocumentMarkdownModule(pythonEnvironment))
    {
    }

    internal CSnakesFinancialDocumentMarkdownConverter(IDocumentMarkdownModule module)
    {
        _module = module;
    }

    public async Task<FinancialDocumentMarkdownResult> ConvertPdfAsync(
        Stream pdf,
        int maxCharacters,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await using var buffer = new MemoryStream();
            await pdf.CopyToAsync(buffer, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var requestJson = JsonSerializer.Serialize(
                new DocumentMarkdownRequest(
                    Convert.ToBase64String(buffer.ToArray()),
                    maxCharacters),
                JsonOptions);
            var responseJson = _module.ConvertPdfToMarkdown(requestJson);

            return ParseResponse(responseJson);
        }
        catch (OperationCanceledException exception)
            when (exception.CancellationToken == cancellationToken &&
                  cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ConversionFailed();
        }
    }

    private static FinancialDocumentMarkdownResult ParseResponse(string responseJson)
    {
        if (string.IsNullOrWhiteSpace(responseJson))
        {
            return InvalidResponse();
        }

        try
        {
            var response = JsonSerializer.Deserialize<DocumentMarkdownResponse>(
                responseJson,
                JsonOptions);

            if (response?.Succeeded is null ||
                response.Markdown is null ||
                response.Truncated is null)
            {
                return InvalidResponse();
            }

            return new FinancialDocumentMarkdownResult(
                response.Succeeded.Value,
                response.Markdown,
                response.Truncated.Value,
                response.FailureReason);
        }
        catch (JsonException)
        {
            return InvalidResponse();
        }
    }

    private static FinancialDocumentMarkdownResult InvalidResponse()
    {
        return new FinancialDocumentMarkdownResult(false, "", false, "invalid_response");
    }

    private static FinancialDocumentMarkdownResult ConversionFailed()
    {
        return new FinancialDocumentMarkdownResult(false, "", false, "conversion_failed");
    }

    private sealed record DocumentMarkdownRequest(
        string PdfBase64,
        int MaxCharacters);

    private sealed record DocumentMarkdownResponse(
        bool? Succeeded,
        string? Markdown,
        bool? Truncated,
        string? FailureReason);

    private sealed class CSnakesDocumentMarkdownModule(
        IPythonEnvironment pythonEnvironment) : IDocumentMarkdownModule
    {
        public string ConvertPdfToMarkdown(string requestJson)
        {
            return pythonEnvironment
                .DocumentMarkdown()
                .ConvertPdfToMarkdown(requestJson);
        }
    }
}

internal interface IDocumentMarkdownModule
{
    string ConvertPdfToMarkdown(string requestJson);
}
