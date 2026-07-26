import assert from "node:assert/strict";
import test from "node:test";
import * as regulatoryEvidenceEnrichment from "../src/utils/regulatoryEvidenceEnrichment.ts";

const {
  formatRegulatoryEnrichmentStatus,
  formatRegulatoryEnrichmentSummary,
  getSafeRegulatoryEvidenceUrl,
  normalizeRegulatoryEvidenceEnrichments,
} = regulatoryEvidenceEnrichment;

const citation = {
  source: "CNV",
  documentType: "Resolución General",
  resolutionNumber: "999",
  title: "Norma CNV",
  chapter: "Capítulo I",
  section: "Sección II",
  article: "Artículo 3",
  publicationDate: "2026-07-01",
  url: "https://www.argentina.gob.ar/cnv/norma",
  quotedText: "Texto citado",
};

function createValidEnrichment(
  enrichmentId = "enrichment-1",
  status = "Verified",
) {
  return {
    enrichmentId,
    documentId: `document-${enrichmentId}`,
    chunkId: `chunk-${enrichmentId}`,
    rank: enrichmentId === "enrichment-1" ? 1 : 2,
    score: enrichmentId === "enrichment-1" ? 0.91 : 0.82,
    original: {
      snippet: `Fragmento ${enrichmentId}`,
      citation: { ...citation },
    },
    document: {
      id: `document-${enrichmentId}`,
      source: "CNV",
      documentType: "Resolución General",
      resolutionNumber: "999",
      title: `Documento ${enrichmentId}`,
      publicationDate: "2026-07-01",
      effectiveDate: "2026-07-15",
      url: "https://www.argentina.gob.ar/cnv/documento",
      status: "vigente",
      requiresReview: true,
      retrievedAt: "2026-07-26T12:00:00Z",
      text: "Contexto documental",
      originalTextLength: 140,
      isTruncated: false,
      metadata: { issuer: "CNV" },
      citations: [{ ...citation }],
    },
    article: {
      citation: { ...citation },
      text: "Contexto del artículo",
      confidence: 0.88,
      originalTextLength: 80,
      isTruncated: false,
    },
    status,
    limitations: ["Requiere revisión humana."],
  };
}

function withoutProperty(
  value: Record<string, unknown>,
  property: string,
): Record<string, unknown> {
  const copy = { ...value };
  delete copy[property];
  return copy;
}

test("formats known regulatory enrichment statuses in Spanish", () => {
  assert.equal(formatRegulatoryEnrichmentStatus("Verified"), "Verificado");
  assert.equal(formatRegulatoryEnrichmentStatus("Partial"), "Parcial");
  assert.equal(formatRegulatoryEnrichmentStatus("Conflict"), "Conflicto");
  assert.equal(formatRegulatoryEnrichmentStatus("Unavailable"), "No disponible");
});

test("preserves unknown future regulatory enrichment statuses", () => {
  assert.equal(
    formatRegulatoryEnrichmentStatus("FutureStatus"),
    "FutureStatus",
  );
});

test("uses status-specific accessible canonical-context summaries", () => {
  assert.equal(
    formatRegulatoryEnrichmentSummary("Verified"),
    "Mostrar contexto canónico verificado",
  );
  assert.equal(
    formatRegulatoryEnrichmentSummary("Partial"),
    "Mostrar contexto canónico verificado parcialmente",
  );
  assert.equal(
    formatRegulatoryEnrichmentSummary("Conflict"),
    "Mostrar contexto canónico con conflicto",
  );
  assert.equal(
    formatRegulatoryEnrichmentSummary("Unavailable"),
    "Mostrar contexto canónico para revisión",
  );
  assert.equal(
    formatRegulatoryEnrichmentSummary("FutureStatus"),
    "Mostrar contexto canónico para revisión",
  );
});

test("accepts only absolute HTTP regulatory evidence URLs", () => {
  assert.equal(
    getSafeRegulatoryEvidenceUrl("https://www.argentina.gob.ar/cnv"),
    "https://www.argentina.gob.ar/cnv",
  );
  assert.equal(
    getSafeRegulatoryEvidenceUrl("http://example.test/regulation"),
    "http://example.test/regulation",
  );
  assert.equal(getSafeRegulatoryEvidenceUrl("javascript:alert(1)"), null);
  assert.equal(getSafeRegulatoryEvidenceUrl("data:text/html,unsafe"), null);
  assert.equal(getSafeRegulatoryEvidenceUrl("/relative-document"), null);
  assert.equal(getSafeRegulatoryEvidenceUrl(null), null);
});

test("normalizes missing, non-array, null, and invalid enrichment values to no items", () => {
  assert.deepEqual(normalizeRegulatoryEvidenceEnrichments(undefined), []);
  assert.deepEqual(normalizeRegulatoryEvidenceEnrichments(null), []);
  assert.deepEqual(normalizeRegulatoryEvidenceEnrichments("corrupt"), []);
  assert.deepEqual(
    normalizeRegulatoryEvidenceEnrichments([
      null,
      {},
      { ...createValidEnrichment(), original: null },
      {
        ...createValidEnrichment(),
        original: { snippet: "Sin cita", citation: null },
      },
    ]),
    [],
  );
});

test("preserves complete valid enrichments without mutation and in source order", () => {
  const input = [
    createValidEnrichment(),
    {
      ...createValidEnrichment("enrichment-2", "Partial"),
      document: null,
    },
  ];
  const before = structuredClone(input);

  const normalized = normalizeRegulatoryEvidenceEnrichments(input);

  assert.deepEqual(normalized, input);
  assert.deepEqual(input, before);
  assert.deepEqual(
    normalized.map((item) => item.enrichmentId),
    ["enrichment-1", "enrichment-2"],
  );
});

test("rejects verified items when a present canonical snapshot is malformed", () => {
  const valid = createValidEnrichment();
  const normalized = normalizeRegulatoryEvidenceEnrichments([
    {
      ...valid,
      article: {
        ...valid.article,
        citation: null,
      },
    },
    {
      ...valid,
      enrichmentId: "enrichment-2",
      document: {
        ...valid.document,
        text: 42,
      },
    },
  ]);

  assert.deepEqual(normalized, []);
});

test("keeps verified document-only and article-only items when the other snapshot is null or absent", () => {
  const valid = createValidEnrichment();
  const documentOnlyAbsent = {
    ...withoutProperty(valid, "article"),
    enrichmentId: "document-only-absent",
  };
  const articleOnlyAbsent = {
    ...withoutProperty(valid, "document"),
    enrichmentId: "article-only-absent",
  };

  const normalized = normalizeRegulatoryEvidenceEnrichments([
    { ...valid, enrichmentId: "document-only-null", article: null },
    documentOnlyAbsent,
    { ...valid, enrichmentId: "article-only-null", document: null },
    articleOnlyAbsent,
  ]);

  assert.deepEqual(
    normalized.map((item) => [item.enrichmentId, item.status]),
    [
      ["document-only-null", "Verified"],
      ["document-only-absent", "Verified"],
      ["article-only-null", "Verified"],
      ["article-only-absent", "Verified"],
    ],
  );
});

test("rejects partial items with a present malformed snapshot but keeps normal missing stages", () => {
  const valid = createValidEnrichment();
  const normalized = normalizeRegulatoryEvidenceEnrichments([
    {
      ...valid,
      enrichmentId: "partial-corrupt",
      status: "Partial",
      article: { ...valid.article, citation: null },
    },
    {
      ...valid,
      enrichmentId: "partial-document-only",
      status: "Partial",
      article: null,
    },
    {
      ...withoutProperty(valid, "document"),
      enrichmentId: "partial-article-only",
      status: "Partial",
    },
  ]);

  assert.deepEqual(
    normalized.map((item) => [item.enrichmentId, item.status]),
    [
      ["partial-document-only", "Partial"],
      ["partial-article-only", "Partial"],
    ],
  );
});

test("keeps conflict audit evidence while omitting its malformed snapshot", () => {
  const valid = createValidEnrichment();
  const normalized = normalizeRegulatoryEvidenceEnrichments([
    {
      ...valid,
      status: "Conflict",
      document: { ...valid.document, text: null },
      article: null,
    },
  ]);

  assert.equal(normalized.length, 1);
  assert.equal(normalized[0]?.status, "Conflict");
  assert.equal(normalized[0]?.document, null);
  assert.equal(normalized[0]?.article, null);
});

test("filters contradictory or unverifiable statuses without inventing semantics", () => {
  const valid = createValidEnrichment();
  const conflict = {
    ...valid,
    enrichmentId: "conflict",
    document: null,
    article: null,
    status: "Conflict",
  };
  const unavailable = {
    ...valid,
    enrichmentId: "unavailable",
    document: null,
    article: null,
    status: "Unavailable",
  };
  const future = {
    ...valid,
    enrichmentId: "future",
    document: null,
    article: null,
    status: "FutureStatus",
  };

  const normalized = normalizeRegulatoryEvidenceEnrichments([
    { ...valid, enrichmentId: "verified-empty", document: null, article: null },
    {
      ...valid,
      enrichmentId: "partial-empty",
      document: null,
      article: null,
      status: "Partial",
    },
    conflict,
    unavailable,
    { ...unavailable, enrichmentId: "unavailable-with-snapshot", document: valid.document },
    future,
  ]);

  assert.deepEqual(
    normalized.map((item) => [item.enrichmentId, item.status]),
    [
      ["conflict", "Conflict"],
      ["unavailable", "Unavailable"],
      ["future", "FutureStatus"],
    ],
  );
});

test("safely normalizes malformed optional collections and metadata", () => {
  const valid = createValidEnrichment();
  const normalized = normalizeRegulatoryEvidenceEnrichments([
    {
      ...valid,
      document: {
        ...valid.document,
        metadata: null,
        citations: [null, { ...citation }, { title: "incompleta" }],
      },
      limitations: ["Limitación válida", 42, null],
    },
    {
      ...valid,
      enrichmentId: "enrichment-2",
      document: {
        ...valid.document,
        metadata: "corrupt",
        citations: undefined,
      },
      limitations: undefined,
    },
  ]);

  assert.deepEqual(normalized[0]?.document?.metadata, {});
  assert.deepEqual(normalized[0]?.document?.citations, [citation]);
  assert.deepEqual(normalized[0]?.limitations, ["Limitación válida"]);
  assert.deepEqual(normalized[1]?.document?.metadata, {});
  assert.deepEqual(normalized[1]?.document?.citations, []);
  assert.deepEqual(normalized[1]?.limitations, []);
});
