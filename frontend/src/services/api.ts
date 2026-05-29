import type {
  ActivityEvent,
  AnalysisSessionResponse,
  AnalysisSessionSummary,
  AnalysisSessionStartPreflightResult,
  GetFinancialMetricsResponse,
  SaveFinancialMetricsResponse,
  StructuredFinancialMetricsInput,
  StructuredFinancialMetricsCsvInput,
  StructuredFinancialMetricsFileMetadata,
  LoginRequest,
  LoginResponse,
  RegisterRequest,
} from "../types/domain.types";

export const apiBaseUrl = import.meta.env.VITE_API_URL ?? "http://localhost:5148";

const getAuthToken = (): string | null => localStorage.getItem("auth_token");

async function authenticatedFetch(url: string, options: RequestInit = {}): Promise<Response> {
  const token = getAuthToken();
  const headers: Record<string, string> = {};

  // Safely copy incoming headers in any standard format
  if (options.headers) {
    if (options.headers instanceof Headers) {
      options.headers.forEach((value, key) => {
        headers[key] = value;
      });
    } else if (Array.isArray(options.headers)) {
      options.headers.forEach(([key, value]) => {
        headers[key] = value;
      });
    } else {
      Object.assign(headers, options.headers as Record<string, string>);
    }
  }

  if (token) {
    headers["Authorization"] = `Bearer ${token}`;
  }

  // Only set Content-Type if a non-FormData request body is supplied
  if (options.body && !(options.body instanceof FormData)) {
    headers["Content-Type"] = "application/json";
  }

  const response = await fetch(url, { ...options, headers });
  if (response.status === 401) {
    localStorage.removeItem("auth_token");
    window.location.reload();
  }
  return response;
}

export async function loadSessionEvents(sessionId: string): Promise<ActivityEvent[]> {
  const response = await authenticatedFetch(`${apiBaseUrl}/api/analysis-sessions/${sessionId}/events`);
  if (!response.ok) {
    throw new Error("Failed to load session events.");
  }
  return (await response.json()) as ActivityEvent[];
}

export async function loadSavedSessions(): Promise<AnalysisSessionSummary[]> {
  const response = await authenticatedFetch(`${apiBaseUrl}/api/analysis-sessions`);
  if (!response.ok) {
    throw new Error("Failed to load saved analysis sessions.");
  }
  return (await response.json()) as AnalysisSessionSummary[];
}

export async function loadSessionDetails(sessionId: string): Promise<AnalysisSessionResponse> {
  const response = await authenticatedFetch(`${apiBaseUrl}/api/analysis-sessions/${sessionId}`);
  if (!response.ok) {
    throw new Error("Failed to load analysis session.");
  }
  return (await response.json()) as AnalysisSessionResponse;
}

export async function loadStructuredFinancialMetrics(sessionId: string): Promise<GetFinancialMetricsResponse> {
  const response = await authenticatedFetch(`${apiBaseUrl}/api/analysis-sessions/${sessionId}/financial-metrics`);
  if (!response.ok) {
    throw new Error("Failed to load structured financial metrics.");
  }
  return (await response.json()) as GetFinancialMetricsResponse;
}

export async function getStartPreflight(sessionId: string): Promise<AnalysisSessionStartPreflightResult> {
  const response = await authenticatedFetch(`${apiBaseUrl}/api/analysis-sessions/${sessionId}/start-preflight`);
  if (!response.ok) {
    throw new Error("Failed to check start readiness.");
  }
  return (await response.json()) as AnalysisSessionStartPreflightResult;
}

export async function createSession(): Promise<AnalysisSessionResponse> {
  const response = await authenticatedFetch(`${apiBaseUrl}/api/analysis-sessions`, {
    method: "POST",
  });
  if (!response.ok) {
    throw new Error("Failed to create analysis session.");
  }
  return (await response.json()) as AnalysisSessionResponse;
}

export async function startSession(sessionId: string): Promise<Response> {
  return await authenticatedFetch(`${apiBaseUrl}/api/analysis-sessions/${sessionId}/start`, {
    method: "POST",
  });
}

export async function submitHumanDecision(
  sessionId: string,
  decision: "approve" | "reject",
  reason: string
): Promise<AnalysisSessionResponse> {
  const response = await authenticatedFetch(`${apiBaseUrl}/api/analysis-sessions/${sessionId}/${decision}`, {
    method: "POST",
    body: JSON.stringify({ reason }),
  });
  if (!response.ok) {
    throw new Error(`Failed to ${decision} analysis session.`);
  }
  return (await response.json()) as AnalysisSessionResponse;
}

export async function saveJsonMetrics(
  sessionId: string,
  input: StructuredFinancialMetricsInput
): Promise<SaveFinancialMetricsResponse> {
  const response = await authenticatedFetch(`${apiBaseUrl}/api/analysis-sessions/${sessionId}/financial-metrics`, {
    method: "POST",
    body: JSON.stringify(input),
  });
  if (!response.ok) {
    throw new Error("Failed to save structured financial metrics.");
  }
  return (await response.json()) as SaveFinancialMetricsResponse;
}

export async function saveCsvMetrics(
  sessionId: string,
  input: StructuredFinancialMetricsCsvInput
): Promise<SaveFinancialMetricsResponse> {
  const response = await authenticatedFetch(`${apiBaseUrl}/api/analysis-sessions/${sessionId}/financial-metrics/csv`, {
    method: "POST",
    body: JSON.stringify(input),
  });
  if (!response.ok) {
    throw new Error("Failed to save structured financial metrics.");
  }
  return (await response.json()) as SaveFinancialMetricsResponse;
}

export async function uploadFinancialMetricsFile(
  sessionId: string,
  file: File,
  metadata: StructuredFinancialMetricsFileMetadata
): Promise<SaveFinancialMetricsResponse> {
  const formData = new FormData();
  formData.append("file", file);

  if (metadata.documentId?.trim()) {
    formData.append("documentId", metadata.documentId.trim());
  }
  if (metadata.company?.trim()) {
    formData.append("company", metadata.company.trim());
  }
  if (metadata.currency?.trim()) {
    formData.append("currency", metadata.currency.trim());
  }
  if (metadata.unit?.trim()) {
    formData.append("unit", metadata.unit.trim());
  }

  const response = await authenticatedFetch(`${apiBaseUrl}/api/analysis-sessions/${sessionId}/financial-metrics/file`, {
    method: "POST",
    body: formData,
  });

  if (!response.ok) {
    const uploadError = (await response.json().catch(() => ({}))) as { error?: string };
    throw new Error(
      uploadError.error ?? "File upload failed. Please check the file format and try again."
    );
  }

  return (await response.json()) as SaveFinancialMetricsResponse;
}

export async function login(request: LoginRequest): Promise<LoginResponse> {
  const response = await fetch(`${apiBaseUrl}/api/auth/login`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(request),
  });
  if (!response.ok) {
    throw new Error("Invalid email or password.");
  }
  return (await response.json()) as LoginResponse;
}

export async function registerUser(request: RegisterRequest): Promise<void> {
  const response = await fetch(`${apiBaseUrl}/api/auth/register`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(request),
  });
  if (!response.ok) {
    const errorData = await response.json().catch(() => ({}));
    const errors = errorData.errors ? Object.values(errorData.errors).flat().join(" ") : "Registration failed.";
    throw new Error(errors);
  }
}
