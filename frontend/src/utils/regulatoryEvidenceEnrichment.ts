const regulatoryEnrichmentStatusLabels: Readonly<Record<string, string>> = {
  Verified: "Verificado",
  Partial: "Parcial",
  Conflict: "Conflicto",
  Unavailable: "No disponible",
};

export function formatRegulatoryEnrichmentStatus(status: string): string {
  return regulatoryEnrichmentStatusLabels[status] ?? status;
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
