import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import test from "node:test";
import { fileURLToPath } from "node:url";
import {
  apiBaseUrlValidationMessage,
  normalizeApiBaseUrl,
  resolveApiBaseUrl,
} from "../src/services/apiBaseUrl.ts";

const frontendRoot = fileURLToPath(new URL("../", import.meta.url));

function runPrebuild(apiUrl?: string): ReturnType<typeof spawnSync> {
  const { VITE_API_URL: _, ...environment } = process.env;
  return spawnSync(process.execPath, ["scripts/validateApiBaseUrl.ts"], {
    cwd: frontendRoot,
    encoding: "utf8",
    env: apiUrl === undefined ? environment : { ...environment, VITE_API_URL: apiUrl },
  });
}

test("normalizes valid HTTP API URLs by trimming whitespace and trailing slashes", () => {
  assert.equal(
    normalizeApiBaseUrl(" https://ai-orchestration-hitl-api.onrender.com/// "),
    "https://ai-orchestration-hitl-api.onrender.com"
  );
  assert.equal(
    normalizeApiBaseUrl("http://localhost:5148/api/"),
    "http://localhost:5148/api"
  );
});

test("rejects invalid configured API URLs without exposing their values", () => {
  for (const invalidApiUrl of ["/", "////", "api", "localhost:5148", "ftp://api.example.com", "https://"]) {
    assert.throws(
      () => normalizeApiBaseUrl(invalidApiUrl),
      new Error(apiBaseUrlValidationMessage)
    );
  }
});

test("uses localhost only when API URL is missing in development", () => {
  assert.equal(resolveApiBaseUrl(undefined, true), "http://localhost:5148");
  assert.equal(resolveApiBaseUrl("   ", true), "http://localhost:5148");
});

test("rejects missing production API URLs and never falls back for an invalid development value", () => {
  for (const missingApiUrl of [undefined, "", "   "]) {
    assert.throws(
      () => resolveApiBaseUrl(missingApiUrl, false),
      new Error(apiBaseUrlValidationMessage)
    );
  }
  assert.throws(
    () => resolveApiBaseUrl("/", true),
    new Error(apiBaseUrlValidationMessage)
  );
});

test("prebuild fails closed for missing, blank, and invalid API URLs without echoing them", () => {
  for (const invalidApiUrl of [undefined, "   ", "not-a-valid-api-value"]) {
    const result = runPrebuild(invalidApiUrl);

    assert.equal(result.status, 1);
    assert.equal(result.stderr, `${apiBaseUrlValidationMessage}\n`);
    assert.doesNotMatch(result.stderr, /not-a-valid-api-value/);
  }
});

test("prebuild accepts a valid production API URL", () => {
  const result = runPrebuild("https://ai-orchestration-hitl-api.onrender.com/");

  assert.equal(result.status, 0);
  assert.equal(result.stderr, "");
});
