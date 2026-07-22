using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using Orchestration.Infrastructure.Agents.Legal.Regulations;
using Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;
using System.Text.Json;
using Xunit;

namespace Orchestration.Tests.Agents.Legal;

public class CnvRegulationStdioMcpClientTests
{
    [Fact]
    public async Task GetDocumentAsync_Should_call_exact_tool_and_deserialize_structured_content()
    {
        string? calledTool = null;
        IReadOnlyDictionary<string, object?>? calledArguments = null;
        using var document = JsonDocument.Parse("""
            {
              "found": true,
              "document": {
                "id": "doc-1",
                "source": "CNV",
                "documentType": "Resolución General",
                "resolutionNumber": "1010/2025",
                "title": "Normas CNV",
                "publicationDate": "2025-01-02",
                "effectiveDate": "2025-01-03",
                "url": "https://example.test/doc-1",
                "status": "Vigente",
                "requiresReview": false,
                "retrievedAt": "2026-07-21T12:00:00Z",
                "metadata": { "area": "Emisoras" },
                "text": "Texto completo"
              },
              "citations": [{
                "source": "CNV",
                "documentType": "Resolución General",
                "resolutionNumber": "1010/2025",
                "title": "Normas CNV",
                "chapter": "Capítulo I",
                "section": "Sección 1",
                "article": "Artículo 1",
                "publicationDate": "2025-01-02",
                "url": "https://example.test/doc-1",
                "quotedText": "Cita"
              }],
              "warnings": []
            }
            """);

        await using var client = CreateClient((tool, arguments, _) =>
        {
            calledTool = tool;
            calledArguments = arguments;
            return Task.FromResult(StructuredResult(document.RootElement));
        });

        var response = await client.GetDocumentAsync(
            new CnvRegulationDocumentRequest("doc-1"),
            CancellationToken.None);

        calledTool.Should().Be("get_cnv_document");
        calledArguments.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["documentId"] = "doc-1"
        });
        response.Found.Should().BeTrue();
        response.Document.Should().BeEquivalentTo(new CnvRegulationDocument(
            "doc-1",
            "CNV",
            "Resolución General",
            "1010/2025",
            "Normas CNV",
            "2025-01-02",
            "2025-01-03",
            "https://example.test/doc-1",
            "Vigente",
            false,
            "2026-07-21T12:00:00Z",
            new Dictionary<string, string> { ["area"] = "Emisoras" },
            "Texto completo"));
        response.Citations.Should().ContainSingle()
            .Which.Article.Should().Be("Artículo 1");
        response.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task GetArticleAsync_Should_omit_blank_optional_arguments_and_deserialize_structured_content()
    {
        string? calledTool = null;
        IReadOnlyDictionary<string, object?>? calledArguments = null;
        using var article = JsonDocument.Parse("""
            {
              "found": true,
              "text": "Texto del artículo 4",
              "citation": {
                "source": "CNV",
                "documentType": "Texto ordenado",
                "resolutionNumber": null,
                "title": "Título VII",
                "chapter": null,
                "section": "Sección 2",
                "article": "Artículo 4",
                "publicationDate": null,
                "url": "https://example.test/article-4",
                "quotedText": "Texto del artículo 4"
              },
              "confidence": 0.97,
              "warnings": []
            }
            """);

        await using var client = CreateClient((tool, arguments, _) =>
        {
            calledTool = tool;
            calledArguments = arguments;
            return Task.FromResult(StructuredResult(article.RootElement));
        });

        var response = await client.GetArticleAsync(
            new CnvRegulationArticleRequest("Artículo 4", "Título VII", null, "Sección 2"),
            CancellationToken.None);

        calledTool.Should().Be("get_cnv_article");
        calledArguments.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["article"] = "Artículo 4",
            ["title"] = "Título VII",
            ["section"] = "Sección 2"
        });
        calledArguments.Should().NotContainKey("chapter");
        response.Found.Should().BeTrue();
        response.Text.Should().Be("Texto del artículo 4");
        response.Citation.Should().NotBeNull();
        response.Confidence.Should().Be(0.97);
        response.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task GetArticleAsync_Should_deserialize_json_text_fallback()
    {
        await using var client = CreateClient((_, _, _) => Task.FromResult(TextResult("""
            {
              "found": true,
              "text": "Texto alternativo",
              "citation": null,
              "confidence": 0.5,
              "warnings": ["fallback"]
            }
            """)));

        var response = await client.GetArticleAsync(
            new CnvRegulationArticleRequest("Artículo 5"),
            CancellationToken.None);

        response.Found.Should().BeTrue();
        response.Text.Should().Be("Texto alternativo");
        response.Confidence.Should().Be(0.5);
        response.Warnings.Should().Equal("fallback");
    }

    [Fact]
    public async Task GetDocumentAsync_Should_return_not_found_response_normally()
    {
        await using var client = CreateClient((_, _, _) => Task.FromResult(TextResult("""
            { "found": false, "document": null, "citations": [], "warnings": [] }
            """)));

        var response = await client.GetDocumentAsync(
            new CnvRegulationDocumentRequest("missing"),
            CancellationToken.None);

        response.Found.Should().BeFalse();
        response.Document.Should().BeNull();
        response.Citations.Should().BeEmpty();
        response.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDocumentAsync_Should_set_last_error_when_json_is_malformed()
    {
        await using var client = CreateClient((_, _, _) =>
            Task.FromResult(TextResult("{ malformed")));

        var act = () => client.GetDocumentAsync(
            new CnvRegulationDocumentRequest("doc-1"),
            CancellationToken.None);

        await act.Should().ThrowAsync<JsonException>();
        client.LastError.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetArticleAsync_Should_extract_tool_error_and_set_last_error()
    {
        await using var client = CreateClient((_, _, _) => Task.FromResult(new CallToolResult
        {
            Content = [new TextContentBlock { Text = "article unavailable" }],
            IsError = true
        }));

        var act = () => client.GetArticleAsync(
            new CnvRegulationArticleRequest("Artículo 4"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*article unavailable*");
        client.LastError.Should().Contain("article unavailable");
    }

    [Fact]
    public async Task GetDocumentAsync_Should_translate_internal_timeout_and_reset_connection_state()
    {
        await using var client = CreateClient(async (_, _, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return TextResult("{}");
        }, toolCallTimeoutSeconds: 1);

        var act = () => client.GetDocumentAsync(
            new CnvRegulationDocumentRequest("doc-1"),
            CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>()
            .WithMessage("*get_cnv_document*excedió el tiempo de espera configurado*");
        client.ResetCount.Should().Be(1);
        client.IsConnected.Should().BeFalse();
        client.LastError.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetDocumentAsync_Should_propagate_caller_cancellation_without_timeout_reset()
    {
        await using var client = CreateClient(async (_, _, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return TextResult("{}");
        }, toolCallTimeoutSeconds: 30);
        using var callerCts = new CancellationTokenSource();
        callerCts.CancelAfter(TimeSpan.FromMilliseconds(50));

        var act = () => client.GetDocumentAsync(
            new CnvRegulationDocumentRequest("doc-1"),
            callerCts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        client.ResetCount.Should().Be(0);
        client.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task SearchAsync_Should_preserve_exact_arguments_through_generic_tool_path()
    {
        string? calledTool = null;
        IReadOnlyDictionary<string, object?>? calledArguments = null;
        await using var client = CreateClient((tool, arguments, _) =>
        {
            calledTool = tool;
            calledArguments = arguments;
            return Task.FromResult(TextResult("""
                { "query": "fondos", "results": [], "warnings": [] }
                """));
        });
        var request = new CnvRegulationSearchRequest(
            "fondos",
            "Fondos",
            7,
            "CNV",
            "Resolución",
            "1010/2025",
            "Vigente",
            true);

        var response = await client.SearchAsync(request, CancellationToken.None);

        calledTool.Should().Be("search_cnv_regulation");
        calledArguments.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["query"] = "fondos",
            ["limit"] = 7,
            ["area"] = "Fondos",
            ["source"] = "CNV",
            ["documentType"] = "Resolución",
            ["resolutionNumber"] = "1010/2025",
            ["status"] = "Vigente",
            ["requiresReview"] = true
        });
        response.Query.Should().Be("fondos");
    }

    [Fact]
    public async Task SearchAsync_Should_throw_when_args_are_missing()
    {
        var options = Options.Create(new CnvRegulationMcpOptions
        {
            Enabled = true,
            Command = "dotnet",
            Args = [] // Empty args
        });

        var client = new CnvRegulationStdioMcpClient(
            options,
            NullLogger<CnvRegulationStdioMcpClient>.Instance
        );

        var request = new CnvRegulationSearchRequest(Query: "test", Area: "Agentes", Limit: 5, RequiresReview: true);

        var act = async () => await client.SearchAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Faltan argumentos*");

        client.IsConnected.Should().BeFalse();
        client.ColdStartCount.Should().Be(0);
    }

    [Fact]
    public async Task SearchAsync_Should_track_failures_and_errors_on_invalid_command()
    {
        var options = Options.Create(new CnvRegulationMcpOptions
        {
            Enabled = true,
            Command = "non-existent-executable-file-name",
            Args = ["some-arg"],
            ConnectionTimeoutSeconds = 1,
            ToolCallTimeoutSeconds = 1
        });

        var client = new CnvRegulationStdioMcpClient(
            options,
            NullLogger<CnvRegulationStdioMcpClient>.Instance
        );

        var request = new CnvRegulationSearchRequest(Query: "test", Area: "Agentes", Limit: 5, RequiresReview: true);

        var act = async () => await client.SearchAsync(request, CancellationToken.None);

        // Attempt 1: Should fail to connect because the command doesn't exist
        await act.Should().ThrowAsync<Exception>();

        client.IsConnected.Should().BeFalse();
        client.LastError.Should().NotBeNullOrEmpty();
        client.ColdStartCount.Should().Be(0); // Failed cold starts do not increment successful connection counts

        // Let's dispose it and ensure no crash
        await client.DisposeAsync();
    }

    private static CnvRegulationStdioMcpClient CreateClient(
        Func<string, IReadOnlyDictionary<string, object?>, CancellationToken, Task<CallToolResult>> toolCallOverride,
        int toolCallTimeoutSeconds = 30)
    {
        var options = Options.Create(new CnvRegulationMcpOptions
        {
            Enabled = true,
            Command = "dotnet",
            Args = [],
            ToolCallTimeoutSeconds = toolCallTimeoutSeconds
        });

        return new CnvRegulationStdioMcpClient(
            options,
            NullLogger<CnvRegulationStdioMcpClient>.Instance,
            toolCallOverride);
    }

    private static CallToolResult StructuredResult(JsonElement structuredContent) => new()
    {
        Content = [],
        StructuredContent = structuredContent,
        IsError = false
    };

    private static CallToolResult TextResult(string text) => new()
    {
        Content = [new TextContentBlock { Text = text }],
        IsError = false
    };
}
