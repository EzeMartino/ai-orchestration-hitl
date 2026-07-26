import assert from "node:assert/strict";
import test from "node:test";
import {
  formatRegulatoryEnrichmentStatus,
  getSafeRegulatoryEvidenceUrl,
} from "../src/utils/regulatoryEvidenceEnrichment.ts";

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
  assert.equal(getSafeRegulatoryEvidenceUrl("/relative-document"), null);
  assert.equal(getSafeRegulatoryEvidenceUrl(null), null);
});
