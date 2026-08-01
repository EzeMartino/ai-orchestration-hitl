export const localApiBaseUrl = "http://localhost:5148";
export const apiBaseUrlValidationMessage =
  "VITE_API_URL debe configurarse como una URL HTTP(S) absoluta en producción.";

export function normalizeApiBaseUrl(apiUrl: string): string {
  const normalizedApiUrl = apiUrl.trim().replace(/\/+$/, "");

  try {
    const parsedApiUrl = new URL(normalizedApiUrl);
    if (
      (parsedApiUrl.protocol !== "http:" && parsedApiUrl.protocol !== "https:") ||
      !parsedApiUrl.hostname
    ) {
      throw new Error(apiBaseUrlValidationMessage);
    }
  } catch {
    throw new Error(apiBaseUrlValidationMessage);
  }

  return normalizedApiUrl;
}

export function resolveApiBaseUrl(apiUrl: string | undefined, isDevelopment: boolean): string {
  if (!apiUrl?.trim()) {
    if (isDevelopment) {
      return localApiBaseUrl;
    }

    throw new Error(apiBaseUrlValidationMessage);
  }

  return normalizeApiBaseUrl(apiUrl);
}
