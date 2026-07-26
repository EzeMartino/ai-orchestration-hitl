import type {
  RegulatoryCanonicalArticleContext,
  RegulatoryCanonicalDocumentContext,
  RegulatoryEvidenceCitationContext,
  RegulatoryEvidenceEnrichmentContext,
} from "../types/domain.types";

const regulatoryEnrichmentStatusLabels: Readonly<Record<string, string>> = {
  Verified: "Verificado",
  Partial: "Parcial",
  Conflict: "Conflicto",
  Unavailable: "No disponible",
};

export function formatRegulatoryEnrichmentStatus(status: string): string {
  return regulatoryEnrichmentStatusLabels[status] ?? status;
}

export function formatRegulatoryEnrichmentSummary(status: string): string {
  switch (status) {
    case "Verified":
      return "Mostrar contexto canónico verificado";
    case "Partial":
      return "Mostrar contexto canónico verificado parcialmente";
    case "Conflict":
      return "Mostrar contexto canónico con conflicto";
    default:
      return "Mostrar contexto canónico para revisión";
  }
}

export function getSafeRegulatoryEvidenceUrl(
  value: string | null | undefined,
): string | null {
  if (!value) {
    return null;
  }

  try {
    const url = new URL(value);
    return url.protocol === "http:" || url.protocol === "https:" ? value : null;
  } catch {
    return null;
  }
}

export function normalizeRegulatoryEvidenceEnrichments(
  value: unknown,
): RegulatoryEvidenceEnrichmentContext[] {
  if (!Array.isArray(value)) {
    return [];
  }

  const normalized: RegulatoryEvidenceEnrichmentContext[] = [];

  for (const item of value) {
    const enrichment = normalizeEnrichment(item);
    if (enrichment) {
      normalized.push(enrichment);
    }
  }

  return normalized;
}

type UnknownRecord = Record<string, unknown>;

function normalizeEnrichment(
  value: unknown,
): RegulatoryEvidenceEnrichmentContext | null {
  if (!isRecord(value)) {
    return null;
  }

  const enrichmentId = requiredString(value.enrichmentId);
  const documentId = requiredString(value.documentId);
  const rank = nonNegativeInteger(value.rank);
  const score = finiteNumber(value.score);
  const status = requiredString(value.status);
  const original = normalizeOriginalEvidence(value.original);

  if (
    enrichmentId === null ||
    documentId === null ||
    rank === null ||
    score === null ||
    status === null ||
    original === null
  ) {
    return null;
  }

  const documentProvided = value.document !== null && value.document !== undefined;
  const articleProvided = value.article !== null && value.article !== undefined;
  const document = normalizeDocument(value.document);
  const article = normalizeArticle(value.article);
  const requiresCanonicalContext =
    status === "Verified" || status === "Partial";

  if (
    requiresCanonicalContext &&
    ((documentProvided && document === null) ||
      (articleProvided && article === null) ||
      (document === null && article === null))
  ) {
    return null;
  }

  if (status === "Unavailable" && (documentProvided || articleProvided)) {
    return null;
  }

  return {
    enrichmentId,
    documentId,
    chunkId: nullableString(value.chunkId),
    rank,
    score,
    original,
    document,
    article,
    status,
    limitations: stringArray(value.limitations),
  };
}

function normalizeOriginalEvidence(value: unknown) {
  if (!isRecord(value) || typeof value.snippet !== "string") {
    return null;
  }

  const normalizedCitation = normalizeCitation(value.citation);
  if (!normalizedCitation) {
    return null;
  }

  return {
    snippet: value.snippet,
    citation: normalizedCitation,
  };
}

function normalizeDocument(
  value: unknown,
): RegulatoryCanonicalDocumentContext | null {
  if (value === null || value === undefined) {
    return null;
  }

  if (!isRecord(value)) {
    return null;
  }

  const id = requiredString(value.id);
  const source = requiredString(value.source);
  const documentType = requiredString(value.documentType);
  const title = requiredString(value.title);
  const url = requiredString(value.url);
  const status = requiredString(value.status);
  const text = requiredString(value.text);
  const originalTextLength = nonNegativeInteger(value.originalTextLength);

  if (
    id === null ||
    source === null ||
    documentType === null ||
    title === null ||
    url === null ||
    status === null ||
    text === null ||
    originalTextLength === null ||
    typeof value.requiresReview !== "boolean" ||
    typeof value.isTruncated !== "boolean"
  ) {
    return null;
  }

  return {
    id,
    source,
    documentType,
    resolutionNumber: nullableString(value.resolutionNumber),
    title,
    publicationDate: nullableString(value.publicationDate),
    effectiveDate: nullableString(value.effectiveDate),
    url,
    status,
    requiresReview: value.requiresReview,
    retrievedAt: nullableString(value.retrievedAt),
    text,
    originalTextLength,
    isTruncated: value.isTruncated,
    metadata: stringRecord(value.metadata),
    citations: citationArray(value.citations),
  };
}

function normalizeArticle(
  value: unknown,
): RegulatoryCanonicalArticleContext | null {
  if (value === null || value === undefined) {
    return null;
  }

  if (!isRecord(value)) {
    return null;
  }

  const normalizedCitation = normalizeCitation(value.citation);
  const text = requiredString(value.text);
  const confidence = finiteNumber(value.confidence);
  const originalTextLength = nonNegativeInteger(value.originalTextLength);

  if (
    normalizedCitation === null ||
    text === null ||
    confidence === null ||
    originalTextLength === null ||
    typeof value.isTruncated !== "boolean"
  ) {
    return null;
  }

  return {
    citation: normalizedCitation,
    text,
    confidence,
    originalTextLength,
    isTruncated: value.isTruncated,
  };
}

function normalizeCitation(
  value: unknown,
): RegulatoryEvidenceCitationContext | null {
  if (!isRecord(value)) {
    return null;
  }

  const source = requiredString(value.source);
  const title = requiredString(value.title);
  if (source === null || title === null) {
    return null;
  }

  return {
    source,
    documentType: nullableString(value.documentType),
    resolutionNumber: nullableString(value.resolutionNumber),
    title,
    chapter: nullableString(value.chapter),
    section: nullableString(value.section),
    article: nullableString(value.article),
    publicationDate: nullableString(value.publicationDate),
    url: nullableString(value.url),
    quotedText: nullableString(value.quotedText),
  };
}

function citationArray(value: unknown): RegulatoryEvidenceCitationContext[] {
  if (!Array.isArray(value)) {
    return [];
  }

  return value
    .map(normalizeCitation)
    .filter(
      (citation): citation is RegulatoryEvidenceCitationContext =>
        citation !== null,
    );
}

function stringArray(value: unknown): string[] {
  return Array.isArray(value)
    ? value.filter((item): item is string => typeof item === "string")
    : [];
}

function stringRecord(value: unknown): Record<string, string> {
  if (!isRecord(value)) {
    return {};
  }

  return Object.fromEntries(
    Object.entries(value).filter(
      (entry): entry is [string, string] => typeof entry[1] === "string",
    ),
  );
}

function isRecord(value: unknown): value is UnknownRecord {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function requiredString(value: unknown): string | null {
  return typeof value === "string" && value.trim().length > 0 ? value : null;
}

function nullableString(value: unknown): string | null {
  return typeof value === "string" ? value : null;
}

function finiteNumber(value: unknown): number | null {
  return typeof value === "number" && Number.isFinite(value) ? value : null;
}

function nonNegativeInteger(value: unknown): number | null {
  return typeof value === "number" &&
    Number.isInteger(value) &&
    value >= 0
    ? value
    : null;
}
