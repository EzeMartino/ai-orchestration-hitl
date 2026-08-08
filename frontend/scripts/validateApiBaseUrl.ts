import { apiBaseUrlValidationMessage, resolveApiBaseUrl } from "../src/services/apiBaseUrl.ts";

try {
  resolveApiBaseUrl(process.env.VITE_API_URL, false);
} catch {
  console.error(apiBaseUrlValidationMessage);
  process.exitCode = 1;
}
