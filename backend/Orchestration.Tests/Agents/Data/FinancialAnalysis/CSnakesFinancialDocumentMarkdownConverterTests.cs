using System.Text.Json;
using FluentAssertions;
using Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.Extraction;

namespace Orchestration.Tests.Agents.Data.FinancialAnalysis;

public sealed class CSnakesFinancialDocumentMarkdownConverterTests
{
    [Fact]
    public async Task ConvertPdfAsync_Should_map_successful_markdown_result()
    {
        var module = new FakeDocumentMarkdownModule("""
            {
              "succeeded": true,
              "markdown": "# Income Statement",
              "truncated": false,
              "failureReason": null
            }
            """);
        var converter = new CSnakesFinancialDocumentMarkdownConverter(module);

        await using var pdf = new MemoryStream("%PDF-test"u8.ToArray());
        var result = await converter.ConvertPdfAsync(pdf, 10_000, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Markdown.Should().Be("# Income Statement");
        result.Truncated.Should().BeFalse();
        result.FailureReason.Should().BeNull();
    }

    [Fact]
    public async Task ConvertPdfAsync_Should_serialize_pdf_and_limit_for_python()
    {
        var module = new FakeDocumentMarkdownModule("""
            {
              "succeeded": true,
              "markdown": "",
              "truncated": false,
              "failureReason": null
            }
            """);
        var converter = new CSnakesFinancialDocumentMarkdownConverter(module);
        var pdfBytes = "%PDF-test"u8.ToArray();

        await using var pdf = new MemoryStream(pdfBytes);
        await converter.ConvertPdfAsync(pdf, 4_321, CancellationToken.None);

        module.RequestJson.Should().NotBeNull();
        using var request = JsonDocument.Parse(module.RequestJson!);
        request.RootElement.GetProperty("pdfBase64").GetString()
            .Should().Be(Convert.ToBase64String(pdfBytes));
        request.RootElement.GetProperty("maxCharacters").GetInt32()
            .Should().Be(4_321);
    }

    [Fact]
    public async Task ConvertPdfAsync_Should_map_invalid_json_to_safe_failure()
    {
        var converter = new CSnakesFinancialDocumentMarkdownConverter(
            new FakeDocumentMarkdownModule("invalid"));

        await using var pdf = new MemoryStream("%PDF-test"u8.ToArray());
        var result = await converter.ConvertPdfAsync(pdf, 10_000, CancellationToken.None);

        result.Should().BeEquivalentTo(new
        {
            Succeeded = false,
            Markdown = "",
            Truncated = false,
            FailureReason = "invalid_response"
        });
    }

    [Fact]
    public async Task ConvertPdfAsync_Should_map_module_exception_to_safe_failure()
    {
        var converter = new CSnakesFinancialDocumentMarkdownConverter(
            new FakeDocumentMarkdownModule(_ => throw new InvalidOperationException("conversion failed")));

        await using var pdf = new MemoryStream("%PDF-test"u8.ToArray());
        var result = await converter.ConvertPdfAsync(pdf, 10_000, CancellationToken.None);

        result.Should().BeEquivalentTo(new
        {
            Succeeded = false,
            Markdown = "",
            Truncated = false,
            FailureReason = "conversion_failed"
        });
    }

    [Fact]
    public async Task ConvertPdfAsync_Should_preserve_caller_cancellation()
    {
        var module = new FakeDocumentMarkdownModule("unused");
        var converter = new CSnakesFinancialDocumentMarkdownConverter(module);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await using var pdf = new MemoryStream("%PDF-test"u8.ToArray());
        var action = () => converter.ConvertPdfAsync(pdf, 10_000, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        module.RequestJson.Should().BeNull();
    }

    private sealed class FakeDocumentMarkdownModule : IDocumentMarkdownModule
    {
        private readonly Func<string, string> _convert;

        public FakeDocumentMarkdownModule(string responseJson)
            : this(_ => responseJson)
        {
        }

        public FakeDocumentMarkdownModule(Func<string, string> convert)
        {
            _convert = convert;
        }

        public string? RequestJson { get; private set; }

        public string ConvertPdfToMarkdown(string requestJson)
        {
            RequestJson = requestJson;
            return _convert(requestJson);
        }
    }
}
