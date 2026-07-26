import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const componentSource = readFileSync(
  new URL("../src/components/CompliancePanel.tsx", import.meta.url),
  "utf8",
);
const domainTypesSource = readFileSync(
  new URL("../src/types/domain.types.ts", import.meta.url),
  "utf8",
);

test("declares the persisted regulatory evidence enrichment contract", () => {
  assert.match(
    domainTypesSource,
    /evidenceEnrichments\?: RegulatoryEvidenceEnrichmentContext\[\] \| null/,
  );
  assert.match(domainTypesSource, /originalTextLength: number/);
  assert.match(domainTypesSource, /isTruncated: boolean/);
  assert.match(domainTypesSource, /metadata: Record<string, string>/);
  assert.match(
    domainTypesSource,
    /citations: RegulatoryEvidenceCitationContext\[\]/,
  );
});

test("renders regulatory context verification as a separate accessible section", () => {
  assert.match(
    componentSource,
    /<section aria-labelledby="regulatory-context-verification-title">/,
  );
  assert.match(
    componentSource,
    /<h3 id="regulatory-context-verification-title">/,
  );
  assert.match(componentSource, /Verificación de contexto regulatorio/);
  assert.match(
    componentSource,
    /La verificación documental aporta contexto regulatorio, pero no determina aplicabilidad, incumplimiento ni asesoramiento legal\./,
  );
  assert.match(
    componentSource,
    /normalizeRegulatoryEvidenceEnrichments\(compliance\.evidenceEnrichments\)/,
  );
  assert.match(componentSource, /evidenceEnrichments\.length > 0/);
});

test("keeps original and canonical evidence distinct and canonical text collapsed", () => {
  assert.match(componentSource, /<h4>\s*Resultado \{item\.rank\}:/);
  assert.match(componentSource, /Fragmento original:/);
  assert.match(componentSource, /Cita original:/);
  assert.match(componentSource, /<details>/);
  assert.match(
    componentSource,
    /<summary>\{formatRegulatoryEnrichmentSummary\(item\.status\)\}<\/summary>/,
  );
  assert.match(componentSource, /Documento canónico:/);
  assert.match(componentSource, /Artículo canónico:/);
  assert.match(componentSource, /<h5>Documento canónico:/);
  assert.match(componentSource, /<h5>\s*Artículo canónico:/);
  assert.match(componentSource, /item\.document\.originalTextLength/);
  assert.match(componentSource, /item\.article\.confidence/);
  assert.match(componentSource, /item\.article\.originalTextLength/);
  assert.match(componentSource, /citation\.quotedText/);
  assert.match(
    componentSource,
    /El contexto mostrado fue truncado al límite seguro configurado\./,
  );
});

test("uses stable list keys, safe links, and React text rendering", () => {
  assert.match(
    componentSource,
    /<article\s+key=\{item\.enrichmentId\}\s+style=\{longRegulatoryContentStyle\}\s*>/,
  );
  assert.match(
    componentSource,
    /key=\{`\$\{item\.enrichmentId\}-limitation-\$\{index\}`\}/,
  );
  assert.match(componentSource, /rel="noopener noreferrer"/);
  assert.match(componentSource, /getSafeRegulatoryEvidenceUrl/);
  assert.match(componentSource, /overflowWrap: "anywhere"/);
  assert.match(componentSource, /whiteSpace: "pre-wrap"/);
  assert.doesNotMatch(componentSource, /dangerouslySetInnerHTML/);
});
