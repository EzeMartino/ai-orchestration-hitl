using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orchestration.Application.Agents.Legal.Cnv;
using Orchestration.Application.Agents.Legal.Regulations;
using Orchestration.Infrastructure.Agents.Legal.Regulations;
using Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;

namespace Orchestration.Tests.Agents.Legal;

public sealed class CnvRegulatoryHitEnricherTests
{
    [Fact]
    public async Task EnrichAsync_HitsFromMultipleQueries_RanksGloballyByScoreAndStableKeys()
    {
        var client = new BehaviorCnvRegulationMcpClient();
        var sut = CreateSut(client, articleLimit: 0);
        CnvRegulationEnrichmentHit[] hits =
        [
            Hit(3, documentId: "doc-z", score: 0.70),
            Hit(2, documentId: "doc-b", score: 0.90),
            Hit(1, documentId: "doc-a", score: 0.90)
        ];

        var result = await sut.EnrichAsync(hits, CancellationToken.None);

        result.Enrichments.Select(x => x.DocumentId).Should().Equal("doc-a", "doc-b");
        result.Enrichments.Select(x => x.Rank).Should().Equal(1, 2);
        client.DocumentRequests.Select(x => x.DocumentId).Should().Equal("doc-a", "doc-b");
    }

    [Fact]
    public async Task EnrichAsync_IneligibleOrMalformedHits_ExcludesThemWithoutCalls()
    {
        var client = new BehaviorCnvRegulationMcpClient();
        var sut = CreateSut(client);
        var valid = Hit(1);
        CnvRegulationEnrichmentHit?[] hits =
        [
            null,
            new(1, null!),
            valid with { Result = valid.Result with { Score = double.NaN } },
            valid with { Result = valid.Result with { Score = double.PositiveInfinity } },
            valid with { Result = valid.Result with { Score = double.NegativeInfinity } },
            valid with { Result = valid.Result with { Score = 0.399999 } },
            valid with { Result = valid.Result with { DocumentId = " \t " } },
            valid with { Result = valid.Result with { Citations = [] } },
            valid with { Result = valid.Result with { Citations = null! } },
            valid with { Result = valid.Result with { Citations = [null!] } }
        ];

        var result = await sut.EnrichAsync(hits!, CancellationToken.None);

        result.Enrichments.Should().BeEmpty();
        result.Audits.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
        client.CallOrder.Should().BeEmpty();
    }

    [Theory]
    [InlineData("article", "article-choice")]
    [InlineData("section", "section-choice")]
    [InlineData("chapter", "chapter-choice")]
    [InlineData("remaining", "a-choice")]
    [InlineData("lexical-article", "a-choice")]
    public async Task EnrichAsync_MultipleCitations_SelectsPrimaryByLocatorPriorityThenLexically(
        string scenario,
        string expectedTitle)
    {
        var citations = scenario switch
        {
            "article" => new[]
            {
                Citation(title: "chapter-choice", chapter: "I", article: null),
                Citation(title: "article-choice", chapter: null, article: "9")
            },
            "section" => new[]
            {
                Citation(title: "chapter-choice", chapter: "I", section: null, article: null),
                Citation(title: "section-choice", chapter: null, section: "2", article: null)
            },
            "chapter" => new[]
            {
                Citation(source: "A", title: "remaining-choice", chapter: null, section: null, article: null),
                Citation(title: "chapter-choice", chapter: "I", section: null, article: null)
            },
            "remaining" => new[]
            {
                Citation(source: "Z", title: "z-choice", chapter: null, section: null, article: null),
                Citation(source: "A", title: "a-choice", chapter: null, section: null, article: null)
            },
            _ => new[]
            {
                Citation(source: "CNV", title: "z-choice", article: "1"),
                Citation(source: "CNV", title: "a-choice", article: "1")
            }
        };
        var client = new BehaviorCnvRegulationMcpClient();
        var sut = CreateSut(client, documentLimit: 0, articleLimit: 0, maxHits: 1);

        var result = await sut.EnrichAsync(
            [Hit(1, citations: citations)],
            CancellationToken.None);

        result.Enrichments.Should().ContainSingle()
            .Which.Original.Citation.Title.Should().Be(expectedTitle);
    }

    [Fact]
    public async Task EnrichAsync_DuplicateKeys_DeduplicatesQueriesAndUsesStableTieBreak()
    {
        var client = new BehaviorCnvRegulationMcpClient();
        var sut = CreateSut(client, articleLimit: 0);
        CnvRegulationEnrichmentHit[] hits =
        [
            Hit(3, chunkId: "chunk-z", snippet: "z"),
            Hit(1, chunkId: "chunk-a", snippet: "a"),
            Hit(2, chunkId: "chunk-a", snippet: "a")
        ];

        var result = await sut.EnrichAsync(hits, CancellationToken.None);

        result.Enrichments.Should().ContainSingle().Which.ChunkId.Should().Be("chunk-a");
        result.Enrichments.Should().ContainSingle().Which.Original.Snippet.Should().Be("a");
        result.Audits.Should().ContainSingle().Which.ContributingQueryIndices.Should().Equal(1, 2, 3);
        client.DocumentRequests.Should().ContainSingle();
    }

    [Fact]
    public async Task EnrichAsync_DelimitedIdentityComponents_DoNotCollideAndRemainDeterministic()
    {
        CnvRegulationEnrichmentHit[] hits =
        [
            Hit(1, documentId: "A|B", article: "C"),
            Hit(2, documentId: "A", article: "B|C")
        ];
        var firstClient = new BehaviorCnvRegulationMcpClient();
        var secondClient = new BehaviorCnvRegulationMcpClient();

        var first = await CreateSut(firstClient, articleLimit: 0)
            .EnrichAsync(hits, CancellationToken.None);
        var second = await CreateSut(secondClient, articleLimit: 0)
            .EnrichAsync(hits.Reverse().ToArray(), CancellationToken.None);

        first.Enrichments.Should().HaveCount(2);
        firstClient.DocumentRequests.Should().HaveCount(2);
        first.Audits.Select(x => x.CandidateKey).Should().OnlyHaveUniqueItems()
            .And.AllSatisfy(key => key.Should().MatchRegex("^[0-9a-f]{64}$"));
        first.Enrichments.Select(x => x.EnrichmentId).Should().OnlyHaveUniqueItems();
        JsonSerializer.Serialize(first).Should().Be(JsonSerializer.Serialize(second));
    }

    [Fact]
    public async Task EnrichAsync_ThreeEligibleCandidates_SelectsAtMostTwoAndCallsSequentially()
    {
        var client = new BehaviorCnvRegulationMcpClient();
        var sut = CreateSut(client);
        CnvRegulationEnrichmentHit[] hits =
        [
            Hit(3, documentId: "doc-c", article: "3", score: 0.70),
            Hit(1, documentId: "doc-a", article: "1", score: 0.90),
            Hit(2, documentId: "doc-b", article: "2", score: 0.80)
        ];

        var result = await sut.EnrichAsync(hits, CancellationToken.None);

        result.Enrichments.Should().HaveCount(2);
        client.DocumentRequests.Should().HaveCount(2);
        client.ArticleRequests.Should().HaveCount(2);
        client.CallOrder.Should().Equal("document:doc-a", "article:1", "document:doc-b", "article:2");
        client.MaximumConcurrentCalls.Should().Be(1);
    }

    [Theory]
    [InlineData("success", LegalCnvEnrichmentStageStatuses.Succeeded)]
    [InlineData("missing", LegalCnvEnrichmentStageStatuses.Missing)]
    [InlineData("malformed", LegalCnvEnrichmentStageStatuses.Malformed)]
    [InlineData("timeout", LegalCnvEnrichmentStageStatuses.TimedOut)]
    [InlineData("failed", LegalCnvEnrichmentStageStatuses.Failed)]
    [InlineData("non-caller-cancellation", LegalCnvEnrichmentStageStatuses.Failed)]
    public async Task EnrichAsync_SameDocument_ReusesEveryRawDocumentOutcome(
        string outcome,
        string expectedStatus)
    {
        var client = new BehaviorCnvRegulationMcpClient
        {
            DocumentHandler = (request, _) => outcome switch
            {
                "success" => Task.FromResult(DocumentResponse(request.DocumentId)),
                "missing" => Task.FromResult(MissingDocumentResponse()),
                "malformed" => Task.FromResult(new CnvRegulationDocumentResponse(true, null, [], [])),
                "timeout" => Task.FromException<CnvRegulationDocumentResponse>(new TimeoutException()),
                "failed" => Task.FromException<CnvRegulationDocumentResponse>(new InvalidOperationException()),
                _ => Task.FromException<CnvRegulationDocumentResponse>(new OperationCanceledException())
            }
        };
        var sut = CreateSut(client, articleLimit: 0);
        CnvRegulationEnrichmentHit[] hits =
        [
            Hit(1, documentId: " Dóc  1 ", article: "1"),
            Hit(2, documentId: "doc 1", article: "2")
        ];

        var result = await sut.EnrichAsync(hits, CancellationToken.None);

        client.DocumentRequests.Should().ContainSingle();
        result.Audits.Should().HaveCount(2);
        result.Audits[0].Document.Should().BeEquivalentTo(new
        {
            Selected = true,
            Attempted = true,
            FromCache = false,
            Status = expectedStatus
        });
        result.Audits[1].Document.Should().BeEquivalentTo(new
        {
            Selected = true,
            Attempted = false,
            FromCache = true,
            Status = expectedStatus
        });
    }

    [Fact]
    public async Task EnrichAsync_CachedDocument_ReevaluatesIdentityAgainstEachOriginalCitation()
    {
        var client = new BehaviorCnvRegulationMcpClient();
        var sut = CreateSut(client, articleLimit: 0);
        CnvRegulationEnrichmentHit[] hits =
        [
            Hit(1, documentId: "doc-1", article: "1", citations: [Citation(source: "CNV", article: "1")]),
            Hit(2, documentId: "DOC-1", article: "2", citations: [Citation(source: "Otra", article: "2")])
        ];

        var result = await sut.EnrichAsync(hits, CancellationToken.None);

        client.DocumentRequests.Should().ContainSingle();
        result.Enrichments.Select(x => x.Status).Should().Equal(
            RegulatoryEvidenceEnrichmentStatuses.Verified,
            RegulatoryEvidenceEnrichmentStatuses.Conflict);
        result.Audits[1].Document.FromCache.Should().BeTrue();
        result.Audits[1].Document.Status.Should().Be(LegalCnvEnrichmentStageStatuses.Conflict);
    }

    [Fact]
    public async Task EnrichAsync_ArticleCandidate_SendsExactDocumentAndFallbackArticleFields()
    {
        var citation = Citation(
            title: "Citation title",
            chapter: null,
            section: "Citation section",
            article: "Article 7");
        var hit = Hit(1, documentId: "doc-exact", article: "Result article", citations: [citation]);
        hit = hit with
        {
            Result = hit.Result with
            {
                Title = "Result title",
                Chapter = "Result chapter",
                Section = "Result section"
            }
        };
        var client = new BehaviorCnvRegulationMcpClient();
        var sut = CreateSut(client);

        await sut.EnrichAsync([hit], CancellationToken.None);

        client.DocumentRequests.Should().Equal(new CnvRegulationDocumentRequest("doc-exact"));
        client.ArticleRequests.Should().Equal(new CnvRegulationArticleRequest(
            Article: "Article 7",
            Title: "Citation title",
            Chapter: "Result chapter",
            Section: "Citation section"));
        client.CallOrder.Should().Equal("document:doc-exact", "article:Article 7");
    }

    [Fact]
    public async Task EnrichAsync_DocumentAndArticleSucceed_ReturnsVerifiedSnapshotsAndAudits()
    {
        var client = new BehaviorCnvRegulationMcpClient();
        var sut = CreateSut(client);

        var result = await sut.EnrichAsync([Hit(4)], CancellationToken.None);

        var enrichment = result.Enrichments.Should().ContainSingle().Subject;
        enrichment.Status.Should().Be(RegulatoryEvidenceEnrichmentStatuses.Verified);
        enrichment.Document.Should().NotBeNull();
        enrichment.Article.Should().NotBeNull();
        enrichment.Original.Snippet.Should().Be("search snippet");
        var audit = result.Audits.Should().ContainSingle().Subject;
        audit.ContributingQueryIndices.Should().Equal(4);
        audit.Document.Should().BeEquivalentTo(new
        {
            Selected = true,
            Attempted = true,
            FromCache = false,
            Status = LegalCnvEnrichmentStageStatuses.Succeeded,
            IsTruncated = false
        });
        audit.Article.Status.Should().Be(LegalCnvEnrichmentStageStatuses.Succeeded);
    }

    [Fact]
    public async Task EnrichAsync_NoArticleLocator_VerifiesDocumentOnlyAndMarksArticleNotApplicable()
    {
        var client = new BehaviorCnvRegulationMcpClient();
        var sut = CreateSut(client);
        var hit = Hit(
            1,
            article: null,
            citations: [Citation(chapter: "I", article: null)]);

        var result = await sut.EnrichAsync([hit], CancellationToken.None);

        result.Enrichments.Should().ContainSingle().Which.Status
            .Should().Be(RegulatoryEvidenceEnrichmentStatuses.Verified);
        result.Audits.Should().ContainSingle().Which.Article.Should().BeEquivalentTo(new
        {
            Selected = false,
            Attempted = false,
            FromCache = false,
            Status = LegalCnvEnrichmentStageStatuses.NotApplicable
        });
        client.ArticleRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task EnrichAsync_DocumentDimensionDisabled_VerifiesArticleOnly()
    {
        var client = new BehaviorCnvRegulationMcpClient();
        var sut = CreateSut(client, documentLimit: 0);

        var result = await sut.EnrichAsync([Hit(1)], CancellationToken.None);

        result.Enrichments.Should().ContainSingle().Which.Status
            .Should().Be(RegulatoryEvidenceEnrichmentStatuses.Verified);
        result.Enrichments.Should().ContainSingle().Which.Document.Should().BeNull();
        result.Audits.Should().ContainSingle().Which.Document.Should().BeEquivalentTo(new
        {
            Selected = false,
            Attempted = false,
            FromCache = false,
            Status = LegalCnvEnrichmentStageStatuses.NotAttempted
        });
        client.DocumentRequests.Should().BeEmpty();
        client.ArticleRequests.Should().ContainSingle();
    }

    [Fact]
    public async Task EnrichAsync_ArticleFailsAfterDocumentSuccess_ReturnsPartial()
    {
        var client = new BehaviorCnvRegulationMcpClient
        {
            ArticleHandler = (_, _) =>
                Task.FromException<CnvRegulationArticleResponse>(new TimeoutException())
        };
        var sut = CreateSut(client);

        var result = await sut.EnrichAsync([Hit(1)], CancellationToken.None);

        result.Enrichments.Should().ContainSingle().Which.Status
            .Should().Be(RegulatoryEvidenceEnrichmentStatuses.Partial);
        result.Audits.Should().ContainSingle().Which.Article.Status
            .Should().Be(LegalCnvEnrichmentStageStatuses.TimedOut);
        result.Audits.Should().ContainSingle().Which.LimitationCodes
            .Should().Contain("article_timed_out");
    }

    [Fact]
    public async Task EnrichAsync_DocumentFailsBeforeArticleSuccess_ReturnsPartial()
    {
        var client = new BehaviorCnvRegulationMcpClient
        {
            DocumentHandler = (_, _) =>
                Task.FromException<CnvRegulationDocumentResponse>(new InvalidOperationException())
        };
        var sut = CreateSut(client);

        var result = await sut.EnrichAsync([Hit(1)], CancellationToken.None);

        result.Enrichments.Should().ContainSingle().Which.Status
            .Should().Be(RegulatoryEvidenceEnrichmentStatuses.Partial);
        result.Enrichments.Should().ContainSingle().Which.Article.Should().NotBeNull();
        result.Audits.Should().ContainSingle().Which.Document.Status
            .Should().Be(LegalCnvEnrichmentStageStatuses.Failed);
    }

    [Fact]
    public async Task EnrichAsync_NoUsableCanonicalStage_ReturnsUnavailable()
    {
        var client = new BehaviorCnvRegulationMcpClient
        {
            DocumentHandler = (_, _) => Task.FromResult(MissingDocumentResponse()),
            ArticleHandler = (_, _) => Task.FromResult(MissingArticleResponse())
        };
        var sut = CreateSut(client);

        var result = await sut.EnrichAsync([Hit(1)], CancellationToken.None);

        result.Enrichments.Should().ContainSingle().Which.Status
            .Should().Be(RegulatoryEvidenceEnrichmentStatuses.Unavailable);
        result.Enrichments.Should().ContainSingle().Which.Document.Should().BeNull();
        result.Enrichments.Should().ContainSingle().Which.Article.Should().BeNull();
        result.Warnings.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("source")]
    [InlineData("resolution")]
    public async Task EnrichAsync_DocumentIdentityDiffers_ReturnsConflictAndSkipsArticle(string conflict)
    {
        var client = new BehaviorCnvRegulationMcpClient
        {
            DocumentHandler = (request, _) => Task.FromResult(DocumentResponse(
                conflict == "id" ? "another-document" : request.DocumentId,
                source: conflict == "source" ? "Otra fuente" : "CNV",
                resolutionNumber: conflict == "resolution" ? "9/2099" : "1/2020"))
        };
        var sut = CreateSut(client);

        var result = await sut.EnrichAsync([Hit(1)], CancellationToken.None);

        var enrichment = result.Enrichments.Should().ContainSingle().Subject;
        enrichment.Status.Should().Be(RegulatoryEvidenceEnrichmentStatuses.Conflict);
        enrichment.Document.Should().NotBeNull("valid conflicting content remains available for audit");
        enrichment.Article.Should().BeNull();
        result.Audits.Should().ContainSingle().Which.Document.Status
            .Should().Be(LegalCnvEnrichmentStageStatuses.Conflict);
        result.Audits.Should().ContainSingle().Which.Article.Status
            .Should().Be(LegalCnvEnrichmentStageStatuses.NotAttempted);
        client.ArticleRequests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("locator")]
    [InlineData("source")]
    [InlineData("resolution")]
    public async Task EnrichAsync_ArticleIdentityDiffers_ReturnsConflictWithCanonicalContent(string conflict)
    {
        var client = new BehaviorCnvRegulationMcpClient
        {
            ArticleHandler = (request, _) => Task.FromResult(ArticleResponse(
                conflict == "locator" ? "Article 99" : request.Article,
                source: conflict == "source" ? "Otra fuente" : "CNV",
                resolutionNumber: conflict == "resolution" ? "9/2099" : "1/2020"))
        };
        var sut = CreateSut(client, documentLimit: 0);

        var result = await sut.EnrichAsync([Hit(1)], CancellationToken.None);

        var enrichment = result.Enrichments.Should().ContainSingle().Subject;
        enrichment.Status.Should().Be(RegulatoryEvidenceEnrichmentStatuses.Conflict);
        enrichment.Article.Should().NotBeNull("valid conflicting content remains available for audit");
        result.Audits.Should().ContainSingle().Which.Article.Status
            .Should().Be(LegalCnvEnrichmentStageStatuses.Conflict);
    }

    [Fact]
    public async Task EnrichAsync_NormalizedIdentityVariants_DoNotConflict()
    {
        var client = new BehaviorCnvRegulationMcpClient
        {
            DocumentHandler = (_, _) => Task.FromResult(DocumentResponse(
                " DÓC  1 ",
                source: "C.N.V.",
                resolutionNumber: "RG-1/2020")),
            ArticleHandler = (_, _) => Task.FromResult(ArticleResponse(
                "artículo.1",
                source: "C.N.V.",
                resolutionNumber: "RG-1/2020"))
        };
        var citation = Citation(
            source: "c.n.v.",
            resolutionNumber: "rg-1/2020",
            article: "Articulo 1");
        var sut = CreateSut(client);

        var result = await sut.EnrichAsync(
            [Hit(1, documentId: "dóc 1", article: "Articulo1", citations: [citation with { Article = "Articulo1" }])],
            CancellationToken.None);

        result.Enrichments.Should().ContainSingle().Which.Status
            .Should().Be(RegulatoryEvidenceEnrichmentStatuses.Verified);
    }

    [Fact]
    public async Task EnrichAsync_FoundFalseWithEmptyContent_MapsMissingStages()
    {
        var client = new BehaviorCnvRegulationMcpClient
        {
            DocumentHandler = (_, _) => Task.FromResult(MissingDocumentResponse()),
            ArticleHandler = (_, _) => Task.FromResult(MissingArticleResponse())
        };
        var sut = CreateSut(client);

        var result = await sut.EnrichAsync([Hit(1)], CancellationToken.None);

        result.Audits.Should().ContainSingle().Which.Document.Status
            .Should().Be(LegalCnvEnrichmentStageStatuses.Missing);
        result.Audits.Should().ContainSingle().Which.Article.Status
            .Should().Be(LegalCnvEnrichmentStageStatuses.Missing);
    }

    public static IEnumerable<object[]> MalformedDocumentResponses()
    {
        yield return ["null response", null!];
        yield return ["null warnings", new CnvRegulationDocumentResponse(true, Document("doc-1"), [], null)];
        yield return ["null citations", new CnvRegulationDocumentResponse(true, Document("doc-1"), null, [])];
        yield return ["false with document", new CnvRegulationDocumentResponse(false, Document("doc-1"), [], [])];
        yield return ["true without document", new CnvRegulationDocumentResponse(true, null, [], [])];
        yield return ["blank id", new CnvRegulationDocumentResponse(true, Document(" "), [], [])];
        yield return ["blank source", new CnvRegulationDocumentResponse(true, Document("doc-1") with { Source = " " }, [], [])];
        yield return ["blank document type", new CnvRegulationDocumentResponse(true, Document("doc-1") with { DocumentType = " " }, [], [])];
        yield return ["blank title", new CnvRegulationDocumentResponse(true, Document("doc-1") with { Title = " " }, [], [])];
        yield return ["blank url", new CnvRegulationDocumentResponse(true, Document("doc-1") with { Url = " " }, [], [])];
        yield return ["blank status", new CnvRegulationDocumentResponse(true, Document("doc-1") with { Status = " " }, [], [])];
        yield return ["blank text", new CnvRegulationDocumentResponse(true, Document("doc-1") with { Text = " " }, [], [])];
        yield return ["null citation element", new CnvRegulationDocumentResponse(true, Document("doc-1"), [null!], [])];
        yield return ["blank citation source", new CnvRegulationDocumentResponse(true, Document("doc-1"), [Citation(source: " ")], [])];
        yield return ["blank citation title", new CnvRegulationDocumentResponse(true, Document("doc-1"), [Citation(title: " ")], [])];
    }

    [Theory]
    [MemberData(nameof(MalformedDocumentResponses))]
    public async Task EnrichAsync_MalformedDocumentShape_FailsClosed(
        string _,
        CnvRegulationDocumentResponse? response)
    {
        var client = new BehaviorCnvRegulationMcpClient
        {
            DocumentHandler = (_, _) => Task.FromResult(response!)
        };
        var sut = CreateSut(client, articleLimit: 0);

        var result = await sut.EnrichAsync([Hit(1)], CancellationToken.None);

        result.Enrichments.Should().ContainSingle().Which.Document.Should().BeNull();
        result.Enrichments.Should().ContainSingle().Which.Status
            .Should().Be(RegulatoryEvidenceEnrichmentStatuses.Unavailable);
        result.Audits.Should().ContainSingle().Which.Document.Status
            .Should().Be(LegalCnvEnrichmentStageStatuses.Malformed);
    }

    public static IEnumerable<object[]> MalformedArticleResponses()
    {
        yield return ["null response", null!];
        yield return ["null warnings", new CnvRegulationArticleResponse(true, "text", Citation(), 0.9, null)];
        yield return ["false with text", new CnvRegulationArticleResponse(false, "text", null, 0, [])];
        yield return ["false with citation", new CnvRegulationArticleResponse(false, null, Citation(), 0, [])];
        yield return ["true with blank text", new CnvRegulationArticleResponse(true, " ", Citation(), 0.9, [])];
        yield return ["true without citation", new CnvRegulationArticleResponse(true, "text", null, 0.9, [])];
        yield return ["true with blank article", new CnvRegulationArticleResponse(
            true,
            "text",
            Citation(article: " "),
            0.9,
            [])];
        yield return ["true with blank citation source", new CnvRegulationArticleResponse(
            true,
            "text",
            Citation(source: " "),
            0.9,
            [])];
        yield return ["true with blank citation title", new CnvRegulationArticleResponse(
            true,
            "text",
            Citation(title: " "),
            0.9,
            [])];
        yield return ["true with NaN confidence", new CnvRegulationArticleResponse(true, "text", Citation(), double.NaN, [])];
        yield return ["true with positive infinite confidence", new CnvRegulationArticleResponse(true, "text", Citation(), double.PositiveInfinity, [])];
        yield return ["true with negative infinite confidence", new CnvRegulationArticleResponse(true, "text", Citation(), double.NegativeInfinity, [])];
    }

    [Theory]
    [MemberData(nameof(MalformedArticleResponses))]
    public async Task EnrichAsync_MalformedArticleShape_FailsClosed(
        string _,
        CnvRegulationArticleResponse? response)
    {
        var client = new BehaviorCnvRegulationMcpClient
        {
            ArticleHandler = (_, _) => Task.FromResult(response!)
        };
        var sut = CreateSut(client, documentLimit: 0);

        var result = await sut.EnrichAsync([Hit(1)], CancellationToken.None);

        result.Enrichments.Should().ContainSingle().Which.Article.Should().BeNull();
        result.Enrichments.Should().ContainSingle().Which.Status
            .Should().Be(RegulatoryEvidenceEnrichmentStatuses.Unavailable);
        result.Audits.Should().ContainSingle().Which.Article.Status
            .Should().Be(LegalCnvEnrichmentStageStatuses.Malformed);
    }

    [Fact]
    public async Task EnrichAsync_CallerCancellation_PropagatesImmediately()
    {
        var client = new BehaviorCnvRegulationMcpClient();
        var sut = CreateSut(client);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => sut.EnrichAsync([Hit(1)], cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        client.CallOrder.Should().BeEmpty();
    }

    [Fact]
    public async Task EnrichAsync_CallerCancellationCoincidesWithTimeout_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new BehaviorCnvRegulationMcpClient
        {
            DocumentHandler = (_, _) =>
            {
                cancellation.Cancel();
                return Task.FromException<CnvRegulationDocumentResponse>(new TimeoutException());
            }
        };
        var sut = CreateSut(client, articleLimit: 0);

        var act = () => sut.EnrichAsync([Hit(1)], cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task EnrichAsync_CanonicalDocumentCitationQuote_IsNotRetained()
    {
        const string quotedText = "0123456789-SECRET-SUFFIX";
        var client = new BehaviorCnvRegulationMcpClient
        {
            DocumentHandler = (request, _) => Task.FromResult(new CnvRegulationDocumentResponse(
                true,
                Document(request.DocumentId) with { Text = "short" },
                [Citation(quotedText: quotedText)],
                []))
        };
        var sut = CreateSut(client, documentLimit: 10, articleLimit: 0);

        var result = await sut.EnrichAsync([Hit(1)], CancellationToken.None);

        result.Enrichments.Should().ContainSingle().Which.Document!.Citations
            .Should().ContainSingle().Which.QuotedText.Should().BeNull();
        result.Audits.Should().ContainSingle().Which.LimitationCodes
            .Should().Contain("document_citations_truncated");
    }

    [Fact]
    public async Task EnrichAsync_OversizedMetadata_BoundsEntriesKeysValuesAndSecretSuffixes()
    {
        const string secretKeySuffix = "SECRET-METADATA-KEY-SUFFIX";
        const string secretValueSuffix = "SECRET-METADATA-VALUE-SUFFIX";
        var metadata = Enumerable.Range(0, 35)
            .ToDictionary(index => $"key-{index:D2}", index => $"value-{index:D2}", StringComparer.Ordinal);
        metadata["00-oversized-value"] = new string('v', 512) + secretValueSuffix;
        metadata[new string('K', 128) + secretKeySuffix] = "bounded-value";
        var client = new BehaviorCnvRegulationMcpClient
        {
            DocumentHandler = (request, _) => Task.FromResult(new CnvRegulationDocumentResponse(
                true,
                Document(request.DocumentId) with { Metadata = metadata },
                [Citation(quotedText: null)],
                []))
        };
        var sut = CreateSut(client, articleLimit: 0);

        var result = await sut.EnrichAsync([Hit(1)], CancellationToken.None);

        var document = result.Enrichments.Should().ContainSingle().Which.Document!;
        document.Metadata.Should().HaveCount(32);
        document.Metadata.Keys.Should().OnlyContain(key => key.Length <= 128);
        document.Metadata.Values.Should().OnlyContain(value => value.Length <= 512);
        result.Audits.Should().ContainSingle().Which.LimitationCodes
            .Should().Contain("document_metadata_truncated");
        JsonSerializer.Serialize(result).Should().NotContain(secretKeySuffix).And.NotContain(secretValueSuffix);
    }

    [Fact]
    public async Task EnrichAsync_ExcessCanonicalCitations_DeduplicatesCapsAndDropsQuotedSecrets()
    {
        var citations = Enumerable.Range(0, 24)
            .Select(index => Citation(
                title: $"Canonical title {index:D2}",
                article: $"Article {index:D2}",
                quotedText: $"quote-{index:D2}-SECRET-CITATION-{index:D2}"))
            .ToList();
        citations.Add(citations[0]);
        var client = new BehaviorCnvRegulationMcpClient
        {
            DocumentHandler = (request, _) => Task.FromResult(new CnvRegulationDocumentResponse(
                true,
                Document(request.DocumentId),
                citations,
                []))
        };
        var sut = CreateSut(client, articleLimit: 0);

        var result = await sut.EnrichAsync([Hit(1)], CancellationToken.None);

        var canonicalCitations = result.Enrichments.Should().ContainSingle().Which.Document!.Citations;
        canonicalCitations.Should().HaveCount(16);
        canonicalCitations.Should().OnlyContain(citation => citation.QuotedText == null);
        result.Audits.Should().ContainSingle().Which.LimitationCodes
            .Should().Contain("document_citations_truncated");
        JsonSerializer.Serialize(result).Should().NotContain("SECRET-CITATION");
    }

    [Fact]
    public async Task EnrichAsync_DocumentCitations_DeduplicatesBySanitizedOutputBeforeApplyingCap()
    {
        var rawQuoteVariants = Enumerable.Range(0, 24)
            .Select(index => Citation(
                title: "Same retained citation",
                article: "Article 1",
                quotedText: $"RAW-QUOTE-SECRET-{index:D2}"))
            .ToArray();
        var distinct = Citation(
            title: "Distinct retained citation",
            article: "Article 2",
            quotedText: "DISTINCT-QUOTE-SECRET");
        var hit = Hit(1, article: null, citations: [Citation(chapter: "I", section: null, article: null)]);

        var first = await CreateSut(ClientWithDocumentCitations([.. rawQuoteVariants, distinct]))
            .EnrichAsync([hit], CancellationToken.None);
        var second = await CreateSut(ClientWithDocumentCitations([distinct, .. rawQuoteVariants.Reverse()]))
            .EnrichAsync([hit], CancellationToken.None);

        var citations = first.Enrichments.Should().ContainSingle().Which.Document!.Citations;
        citations.Select(citation => citation.Title).Should().Equal(
            "Distinct retained citation",
            "Same retained citation");
        citations.Should().OnlyContain(citation => citation.QuotedText == null);
        JsonSerializer.Serialize(first).Should().NotContain("RAW-QUOTE-SECRET").And.NotContain("DISTINCT-QUOTE-SECRET");
        JsonSerializer.Serialize(first).Should().Be(JsonSerializer.Serialize(second));
    }

    [Fact]
    public async Task EnrichAsync_RepeatedCanonicalFieldTruncation_DeduplicatesFinalLimitationsAndCodes()
    {
        const string canonicalFieldLimitation =
            "Algunos campos del contexto canónico CNV fueron truncados por límites de seguridad.";
        const string documentCitationLimitation =
            "Parte de las citas canónicas del documento CNV fue truncada, deduplicada o descartada por límites de seguridad.";
        var client = new BehaviorCnvRegulationMcpClient
        {
            DocumentHandler = (request, _) => Task.FromResult(new CnvRegulationDocumentResponse(
                true,
                Document(request.DocumentId) with { Title = new string('D', 513) },
                [Citation(quotedText: "discarded document quote")],
                [])),
            ArticleHandler = (request, _) => Task.FromResult(new CnvRegulationArticleResponse(
                true,
                "article text",
                Citation(title: new string('A', 513), article: request.Article),
                0.9,
                []))
        };

        var result = await CreateSut(client).EnrichAsync([Hit(1)], CancellationToken.None);

        var enrichment = result.Enrichments.Should().ContainSingle().Subject;
        enrichment.Limitations.Should().ContainSingle(message => message == canonicalFieldLimitation);
        enrichment.Limitations.Should().ContainSingle(message => message == documentCitationLimitation);
        var audit = result.Audits.Should().ContainSingle().Subject;
        audit.LimitationCodes.Should().ContainSingle(code => code == "canonical_field_truncated");
        audit.LimitationCodes.Should().ContainSingle(code => code == "document_citations_truncated");
        result.Warnings.Should().ContainSingle(message => message == canonicalFieldLimitation);
        result.Warnings.Should().ContainSingle(message => message == documentCitationLimitation);
    }

    [Fact]
    public async Task EnrichAsync_OversizedCanonicalFields_BoundsDocumentAndArticleSnapshots()
    {
        const string documentSecret = "SECRET-DOCUMENT-FIELD-SUFFIX";
        const string articleSecret = "SECRET-ARTICLE-FIELD-SUFFIX";
        var client = new BehaviorCnvRegulationMcpClient
        {
            DocumentHandler = (request, _) => Task.FromResult(new CnvRegulationDocumentResponse(
                true,
                Document(request.DocumentId) with { Title = new string('D', 512) + documentSecret },
                [Citation(quotedText: null)],
                [])),
            ArticleHandler = (request, _) => Task.FromResult(new CnvRegulationArticleResponse(
                true,
                "article text",
                Citation(
                    title: new string('A', 512) + articleSecret,
                    article: request.Article,
                    url: new string('u', 2_048) + articleSecret,
                    quotedText: "12345678" + articleSecret),
                0.9,
                []))
        };
        var sut = CreateSut(client, articleLimit: 8);

        var result = await sut.EnrichAsync([Hit(1)], CancellationToken.None);

        var enrichment = result.Enrichments.Should().ContainSingle().Subject;
        enrichment.Document!.Title.Should().HaveLength(512);
        enrichment.Article!.Citation.Title.Should().HaveLength(512);
        enrichment.Article.Citation.Url.Should().HaveLength(2_048);
        enrichment.Article.Citation.QuotedText.Should().Be("12345678");
        result.Audits.Should().ContainSingle().Which.LimitationCodes
            .Should().Contain("canonical_field_truncated");
        JsonSerializer.Serialize(result).Should().NotContain(documentSecret).And.NotContain(articleSecret);
    }

    [Fact]
    public async Task EnrichAsync_CachedDocumentSnapshot_IsImmuneToRawResponseMutation()
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal) { ["stable"] = "before" };
        var citations = new List<CnvRegulationCitation>
        {
            Citation(title: "before-title", quotedText: null)
        };
        var client = new BehaviorCnvRegulationMcpClient
        {
            DocumentHandler = (request, _) => Task.FromResult(new CnvRegulationDocumentResponse(
                true,
                Document(request.DocumentId) with { Metadata = metadata },
                citations,
                [])),
            ArticleHandler = (request, _) =>
            {
                metadata["stable"] = "after-SECRET-MUTATION";
                citations[0] = citations[0] with { Title = "after-SECRET-MUTATION" };
                citations.Add(Citation(title: "added-SECRET-MUTATION", quotedText: null));
                return Task.FromResult(ArticleResponse(request.Article));
            }
        };
        var sut = CreateSut(client);

        var result = await sut.EnrichAsync(
            [
                Hit(1, documentId: "doc-1", article: "Article 1"),
                Hit(2, documentId: "DOC-1", article: "Article 2")
            ],
            CancellationToken.None);

        client.DocumentRequests.Should().ContainSingle();
        result.Enrichments.Should().HaveCount(2).And.AllSatisfy(enrichment =>
        {
            enrichment.Document!.Metadata["stable"].Should().Be("before");
            enrichment.Document.Citations.Should().ContainSingle().Which.Title.Should().Be("before-title");
        });
        JsonSerializer.Serialize(result).Should().NotContain("SECRET-MUTATION");
    }

    [Fact]
    public async Task EnrichAsync_CanonicalCitationOrderChanges_DeduplicatesAndSerializesIdentically()
    {
        var a = Citation(source: "cnv", title: "A title", article: "Article 1", quotedText: null);
        var z = Citation(source: "CNV", title: "Z title", article: "Article 9", quotedText: null);
        var firstClient = ClientWithDocumentCitations([z, a, a]);
        var secondClient = ClientWithDocumentCitations([a, z, a]);
        var hit = Hit(1, article: null, citations: [Citation(chapter: "I", section: null, article: null)]);

        var first = await CreateSut(firstClient).EnrichAsync([hit], CancellationToken.None);
        var second = await CreateSut(secondClient).EnrichAsync([hit], CancellationToken.None);

        first.Enrichments.Should().ContainSingle().Which.Document!.Citations
            .Select(citation => citation.Title).Should().Equal("A title", "Z title");
        JsonSerializer.Serialize(first).Should().Be(JsonSerializer.Serialize(second));
    }

    [Fact]
    public async Task EnrichAsync_NullDocumentMetadata_UsesImmutableEmptySnapshot()
    {
        var client = new BehaviorCnvRegulationMcpClient
        {
            DocumentHandler = (request, _) => Task.FromResult(new CnvRegulationDocumentResponse(
                true,
                Document(request.DocumentId) with { Metadata = null },
                [],
                []))
        };
        var sut = CreateSut(client, articleLimit: 0);

        var result = await sut.EnrichAsync([Hit(1)], CancellationToken.None);

        result.Enrichments.Should().ContainSingle().Which.Document!.Metadata.Should().BeEmpty();
        result.Enrichments.Should().ContainSingle().Which.Status
            .Should().Be(RegulatoryEvidenceEnrichmentStatuses.Verified);
    }

    [Fact]
    public async Task EnrichAsync_TruncatesCanonicalTextsAndCitationQuotesWithoutLeakingFullContent()
    {
        const string documentText = "safeTOP-SECRET-DOCUMENT-SUFFIX";
        const string articleText = "okayTOP-SECRET-ARTICLE-SUFFIX";
        var documentCitation = Citation(quotedText: "citeTOP-SECRET-DOCUMENT-CITATION");
        var articleCitation = Citation(quotedText: "quotTOP-SECRET-ARTICLE-CITATION");
        var client = new BehaviorCnvRegulationMcpClient
        {
            DocumentHandler = (request, _) => Task.FromResult(new CnvRegulationDocumentResponse(
                true,
                Document(request.DocumentId) with { Text = documentText },
                [documentCitation],
                [])),
            ArticleHandler = (request, _) => Task.FromResult(new CnvRegulationArticleResponse(
                true,
                articleText,
                articleCitation with { Article = request.Article },
                0.91,
                []))
        };
        var sut = CreateSut(client, documentLimit: 4, articleLimit: 4);

        var result = await sut.EnrichAsync([Hit(1)], CancellationToken.None);

        var enrichment = result.Enrichments.Should().ContainSingle().Subject;
        enrichment.Document!.Text.Should().Be("safe");
        enrichment.Document.OriginalTextLength.Should().Be(documentText.Length);
        enrichment.Document.IsTruncated.Should().BeTrue();
        enrichment.Document.Citations.Should().ContainSingle().Which.QuotedText.Should().BeNull();
        enrichment.Article!.Text.Should().Be("okay");
        enrichment.Article.OriginalTextLength.Should().Be(articleText.Length);
        enrichment.Article.IsTruncated.Should().BeTrue();
        enrichment.Article.Citation.QuotedText.Should().Be("quot");
        result.Audits.Should().ContainSingle().Which.LimitationCodes.Should().Contain(
            "document_truncated",
            "article_truncated");
        result.Warnings.Should().OnlyHaveUniqueItems().And.OnlyContain(x => x.Contains("trunc", StringComparison.OrdinalIgnoreCase));

        var serialized = JsonSerializer.Serialize(result);
        serialized.Should().NotContain("TOP-SECRET");
    }

    [Fact]
    public async Task EnrichAsync_ZeroContextLimits_DisablesBothDimensionsWithoutCalls()
    {
        var client = new BehaviorCnvRegulationMcpClient();
        var sut = CreateSut(client, documentLimit: 0, articleLimit: 0);

        var result = await sut.EnrichAsync([Hit(1)], CancellationToken.None);

        result.Enrichments.Should().ContainSingle().Which.Status
            .Should().Be(RegulatoryEvidenceEnrichmentStatuses.Unavailable);
        result.Audits.Should().ContainSingle().Which.Document.Status
            .Should().Be(LegalCnvEnrichmentStageStatuses.NotAttempted);
        result.Audits.Should().ContainSingle().Which.Article.Status
            .Should().Be(LegalCnvEnrichmentStageStatuses.NotAttempted);
        client.CallOrder.Should().BeEmpty();
    }

    [Fact]
    public async Task EnrichAsync_MaxHitsZero_ReturnsEmptyWithoutCalls()
    {
        var client = new BehaviorCnvRegulationMcpClient();
        var sut = CreateSut(client, maxHits: 0);

        var result = await sut.EnrichAsync([Hit(1)], CancellationToken.None);

        result.Enrichments.Should().BeEmpty();
        result.Audits.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
        client.CallOrder.Should().BeEmpty();
    }

    [Fact]
    public async Task EnrichAsync_InputOrderChanges_ProducesIdenticalStableOutputsAndIds()
    {
        CnvRegulationEnrichmentHit[] hits =
        [
            Hit(2, documentId: "doc-b", chunkId: "chunk-z", article: "2", score: 0.8),
            Hit(1, documentId: "doc-a", chunkId: "chunk-z", article: "1", score: 0.9),
            Hit(3, documentId: "DOC-A", chunkId: "chunk-a", article: "Article: 1", score: 0.9)
        ];
        var first = await CreateSut(new BehaviorCnvRegulationMcpClient())
            .EnrichAsync(hits, CancellationToken.None);
        var second = await CreateSut(new BehaviorCnvRegulationMcpClient())
            .EnrichAsync(hits.Reverse().ToArray(), CancellationToken.None);

        JsonSerializer.Serialize(first).Should().Be(JsonSerializer.Serialize(second));
        first.Enrichments.Select(x => x.EnrichmentId).Should().AllSatisfy(id =>
            id.Should().MatchRegex("^[0-9a-f]{64}$"));
    }

    [Fact]
    public async Task EnrichAsync_AuditAndWarnings_NeverContainCanonicalOrOriginalSecretText()
    {
        const string secret = "ULTRA-SECRET-CANONICAL-CONTEXT";
        var client = new BehaviorCnvRegulationMcpClient
        {
            DocumentHandler = (request, _) => Task.FromResult(new CnvRegulationDocumentResponse(
                true,
                Document(request.DocumentId) with { Text = secret },
                [],
                [])),
            ArticleHandler = (_, _) => Task.FromException<CnvRegulationArticleResponse>(new InvalidOperationException(secret))
        };
        var sut = CreateSut(client, documentLimit: 5);

        var result = await sut.EnrichAsync([Hit(1, snippet: "ORIGINAL-SECRET")], CancellationToken.None);

        JsonSerializer.Serialize(result.Audits).Should().NotContain(secret).And.NotContain("ORIGINAL-SECRET");
        string.Join('|', result.Warnings).Should().NotContain(secret).And.NotContain("ORIGINAL-SECRET");
    }

    [Fact]
    public void Constructor_NullDependency_ThrowsArgumentNullException()
    {
        var client = new BehaviorCnvRegulationMcpClient();
        var options = Options.Create(new CnvRegulationMcpOptions());
        var logger = NullLogger<CnvRegulatoryHitEnricher>.Instance;

        var nullClient = () => new CnvRegulatoryHitEnricher(null!, options, logger);
        var nullOptions = () => new CnvRegulatoryHitEnricher(client, null!, logger);
        var nullLogger = () => new CnvRegulatoryHitEnricher(client, options, null!);

        nullClient.Should().Throw<ArgumentNullException>();
        nullOptions.Should().Throw<ArgumentNullException>();
        nullLogger.Should().Throw<ArgumentNullException>();
    }

    private static CnvRegulatoryHitEnricher CreateSut(
        BehaviorCnvRegulationMcpClient client,
        int maxHits = 2,
        int documentLimit = 1_000,
        int articleLimit = 1_000) =>
        new(
            client,
            Options.Create(new CnvRegulationMcpOptions
            {
                MaxEnrichedHits = maxHits,
                MaxDocumentContextCharacters = documentLimit,
                MaxArticleContextCharacters = articleLimit
            }),
            NullLogger<CnvRegulatoryHitEnricher>.Instance);

    private static CnvRegulationEnrichmentHit Hit(
        int queryIndex,
        string documentId = "doc-1",
        string? chunkId = "chunk-1",
        string? article = "Article 1",
        double score = 0.90,
        string snippet = "search snippet",
        IReadOnlyList<CnvRegulationCitation>? citations = null) =>
        new(
            queryIndex,
            new CnvRegulationSearchResult(
                DocumentId: documentId,
                ChunkId: chunkId,
                Title: "Result title",
                Chapter: "Result chapter",
                Section: "Result section",
                Article: article,
                Source: "CNV",
                Url: "https://search.example/result",
                Snippet: snippet,
                Score: score,
                Citations: citations ?? [Citation(article: article)]));

    private static CnvRegulationCitation Citation(
        string source = "CNV",
        string? documentType = "Resolución General",
        string? resolutionNumber = "1/2020",
        string title = "Canonical title",
        string? chapter = "I",
        string? section = "1",
        string? article = "Article 1",
        string? publicationDate = "2020-01-01",
        string? url = "https://cnv.example/citation",
        string? quotedText = "quoted") =>
        new(
            source,
            documentType,
            resolutionNumber,
            title,
            chapter,
            section,
            article,
            publicationDate,
            url,
            quotedText);

    private static CnvRegulationDocument Document(
        string id,
        string source = "CNV",
        string? resolutionNumber = "1/2020") =>
        new(
            Id: id,
            Source: source,
            DocumentType: "Resolución General",
            ResolutionNumber: resolutionNumber,
            Title: "Canonical title",
            PublicationDate: "2020-01-01",
            EffectiveDate: "2020-02-01",
            Url: "https://cnv.example/document",
            Status: "current",
            RequiresReview: true,
            RetrievedAt: "2026-07-22T12:00:00Z",
            Metadata: new Dictionary<string, string> { ["jurisdiction"] = "AR" },
            Text: "canonical document text");

    private static CnvRegulationDocumentResponse DocumentResponse(
        string id,
        string source = "CNV",
        string? resolutionNumber = "1/2020") =>
        new(
            Found: true,
            Document: Document(id, source, resolutionNumber),
            Citations: [Citation(source: source, resolutionNumber: resolutionNumber)],
            Warnings: []);

    private static CnvRegulationArticleResponse ArticleResponse(
        string article,
        string source = "CNV",
        string? resolutionNumber = "1/2020") =>
        new(
            Found: true,
            Text: "canonical article text",
            Citation: Citation(source: source, resolutionNumber: resolutionNumber, article: article),
            Confidence: 0.95,
            Warnings: []);

    private static CnvRegulationDocumentResponse MissingDocumentResponse() =>
        new(false, null, [], []);

    private static CnvRegulationArticleResponse MissingArticleResponse() =>
        new(false, null, null, 0, []);

    private static BehaviorCnvRegulationMcpClient ClientWithDocumentCitations(
        IReadOnlyList<CnvRegulationCitation> citations) =>
        new()
        {
            DocumentHandler = (request, _) => Task.FromResult(new CnvRegulationDocumentResponse(
                true,
                Document(request.DocumentId),
                citations,
                []))
        };

    private sealed class BehaviorCnvRegulationMcpClient : ICnvRegulationMcpClient
    {
        private int _activeCalls;

        internal Func<CnvRegulationDocumentRequest, CancellationToken, Task<CnvRegulationDocumentResponse>>
            DocumentHandler { get; init; } =
                (request, _) => Task.FromResult(DocumentResponse(request.DocumentId));

        internal Func<CnvRegulationArticleRequest, CancellationToken, Task<CnvRegulationArticleResponse>>
            ArticleHandler { get; init; } =
                (request, _) => Task.FromResult(ArticleResponse(request.Article));

        internal List<CnvRegulationDocumentRequest> DocumentRequests { get; } = [];

        internal List<CnvRegulationArticleRequest> ArticleRequests { get; } = [];

        internal List<string> CallOrder { get; } = [];

        internal int MaximumConcurrentCalls { get; private set; }

        public bool IsConnected => true;

        public int ColdStartCount => 0;

        public int ResetCount => 0;

        public string? LastError => null;

        public Task<CnvRegulationSearchResponse> SearchAsync(
            CnvRegulationSearchRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CnvRegulationSearchResponse(request.Query, [], []));

        public Task<CnvRegulationDocumentResponse> GetDocumentAsync(
            CnvRegulationDocumentRequest request,
            CancellationToken cancellationToken)
        {
            DocumentRequests.Add(request);
            CallOrder.Add($"document:{request.DocumentId}");
            return TrackAsync(() => DocumentHandler(request, cancellationToken));
        }

        public Task<CnvRegulationArticleResponse> GetArticleAsync(
            CnvRegulationArticleRequest request,
            CancellationToken cancellationToken)
        {
            ArticleRequests.Add(request);
            CallOrder.Add($"article:{request.Article}");
            return TrackAsync(() => ArticleHandler(request, cancellationToken));
        }

        private async Task<T> TrackAsync<T>(Func<Task<T>> action)
        {
            var active = Interlocked.Increment(ref _activeCalls);
            MaximumConcurrentCalls = Math.Max(MaximumConcurrentCalls, active);
            try
            {
                await Task.Yield();
                return await action();
            }
            finally
            {
                Interlocked.Decrement(ref _activeCalls);
            }
        }
    }
}
