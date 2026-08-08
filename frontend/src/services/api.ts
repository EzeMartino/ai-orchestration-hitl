import type {
  ActivityEvent,
  AnalysisSessionResponse,
  AnalysisSessionSummary,
  AnalysisSessionStartPreflightResult,
  FinancialMetricsExtractionDraft,
  FinancialMetricsExtractionDraftIdentity,
  ConfirmFinancialMetricsExtractionDraftRequest,
  GetFinancialMetricsResponse,
  SaveFinancialMetricsResponse,
  StructuredFinancialMetricsInput,
  StructuredFinancialMetricsCsvInput,
  StructuredFinancialMetricsFileMetadata,
  UpdateFinancialMetricsExtractionDraftRequest,
  LoginRequest,
  LoginResponse,
  RegisterRequest,
} from "../types/domain.types";
import { resolveApiBaseUrl } from "./apiBaseUrl";

export const apiBaseUrl = resolveApiBaseUrl(
  import.meta.env?.VITE_API_URL,
  import.meta.env?.DEV !== false
);

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

async function readBackendErrorMessage(response: Response, fallback: string): Promise<string> {
  const payload = await response.json().catch(() => null) as {
    error?: string;
    errors?: string[];
    validationIssues?: Array<{ message?: string }>;
  } | null;

  if (payload?.error) {
    return payload.error;
  }

  const messages = [
    ...(payload?.errors ?? []),
    ...((payload?.validationIssues ?? [])
      .map((issue) => issue.message)
      .filter((message): message is string => Boolean(message))),
  ];

  return messages.length > 0 ? messages.join(" ") : fallback;
}

export async function loadSessionEvents(sessionId: string): Promise<ActivityEvent[]> {
  const response = await authenticatedFetch(`${apiBaseUrl}/api/analysis-sessions/${sessionId}/events`);
  if (!response.ok) {
    throw new Error("No se pudieron cargar los eventos de la sesión.");
  }
  return (await response.json()) as ActivityEvent[];
}

export async function loadSavedSessions(): Promise<AnalysisSessionSummary[]> {
  const response = await authenticatedFetch(`${apiBaseUrl}/api/analysis-sessions`);
  if (!response.ok) {
    throw new Error("No se pudieron cargar las sesiones de análisis guardadas.");
  }
  return (await response.json()) as AnalysisSessionSummary[];
}

export async function loadSessionDetails(sessionId: string): Promise<AnalysisSessionResponse> {
  const response = await authenticatedFetch(`${apiBaseUrl}/api/analysis-sessions/${sessionId}`);
  if (!response.ok) {
    throw new Error("No se pudo cargar la sesión de análisis.");
  }
  return (await response.json()) as AnalysisSessionResponse;
}

export async function loadStructuredFinancialMetrics(sessionId: string): Promise<GetFinancialMetricsResponse> {
  const response = await authenticatedFetch(`${apiBaseUrl}/api/analysis-sessions/${sessionId}/financial-metrics`);
  if (!response.ok) {
    throw new Error("No se pudieron cargar las métricas financieras estructuradas.");
  }
  return (await response.json()) as GetFinancialMetricsResponse;
}

export async function getFinancialMetricsReview(
  sessionId: string
): Promise<FinancialMetricsExtractionDraft | null> {
  const response = await authenticatedFetch(`${apiBaseUrl}/api/analysis-sessions/${sessionId}/financial-metrics/review`);
  if (response.status === 404) {
    return null;
  }
  if (!response.ok) {
    throw new Error(
      await readBackendErrorMessage(
        response,
        "No se pudo cargar el borrador de revisión de métricas financieras."
      )
    );
  }
  return (await response.json()) as FinancialMetricsExtractionDraft;
}

export async function updateFinancialMetricsReview(
  sessionId: string,
  draftId: string,
  request: UpdateFinancialMetricsExtractionDraftRequest
): Promise<FinancialMetricsExtractionDraft> {
  const response = await authenticatedFetch(
    `${apiBaseUrl}/api/analysis-sessions/${sessionId}/financial-metrics/review/${draftId}`,
    {
      method: "PUT",
      body: JSON.stringify(request),
    }
  );
  if (!response.ok) {
    throw new Error(
      await readBackendErrorMessage(
        response,
        "No se pudieron guardar los cambios del borrador de revisión."
      )
    );
  }
  return (await response.json()) as FinancialMetricsExtractionDraft;
}

export async function confirmFinancialMetricsReview(
  sessionId: string,
  draftId: string,
  request: ConfirmFinancialMetricsExtractionDraftRequest
): Promise<FinancialMetricsExtractionDraft | FinancialMetricsExtractionDraftIdentity> {
  const response = await authenticatedFetch(
    `${apiBaseUrl}/api/analysis-sessions/${sessionId}/financial-metrics/review/${draftId}/confirm`,
    {
      method: "POST",
      body: JSON.stringify(request),
    }
  );
  if (!response.ok) {
    throw new Error(
      await readBackendErrorMessage(
        response,
        "No se pudo confirmar el borrador de revisión."
      )
    );
  }
  return (await response.json()) as FinancialMetricsExtractionDraft | FinancialMetricsExtractionDraftIdentity;
}

export async function discardFinancialMetricsReview(
  sessionId: string,
  draftId: string
): Promise<FinancialMetricsExtractionDraft | FinancialMetricsExtractionDraftIdentity> {
  const response = await authenticatedFetch(
    `${apiBaseUrl}/api/analysis-sessions/${sessionId}/financial-metrics/review/${draftId}/discard`,
    {
      method: "POST",
    }
  );
  if (!response.ok) {
    throw new Error(
      await readBackendErrorMessage(
        response,
        "No se pudo descartar el borrador de revisión."
      )
    );
  }
  return (await response.json()) as FinancialMetricsExtractionDraft | FinancialMetricsExtractionDraftIdentity;
}

export async function getStartPreflight(sessionId: string): Promise<AnalysisSessionStartPreflightResult> {
  const response = await authenticatedFetch(`${apiBaseUrl}/api/analysis-sessions/${sessionId}/start-preflight`);
  if (!response.ok) {
    throw new Error("No se pudo verificar la preparación de inicio.");
  }
  return (await response.json()) as AnalysisSessionStartPreflightResult;
}

export async function createSession(): Promise<AnalysisSessionResponse> {
  const response = await authenticatedFetch(`${apiBaseUrl}/api/analysis-sessions`, {
    method: "POST",
  });
  if (!response.ok) {
    throw new Error("No se pudo crear la sesión de análisis.");
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
    throw new Error(`No se pudo ${decision === "approve" ? "aprobar" : "rechazar"} la sesión de análisis.`);
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
    throw new Error("No se pudieron guardar las métricas financieras estructuradas.");
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
    throw new Error("No se pudieron guardar las métricas financieras estructuradas.");
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
  if (metadata.reportSummary?.reportName?.trim()) {
    formData.append("ReportName", metadata.reportSummary.reportName.trim());
  }
  if (metadata.reportSummary?.totalAmount !== null && metadata.reportSummary?.totalAmount !== undefined) {
    formData.append("TotalAmount", String(metadata.reportSummary.totalAmount));
  }
  if (metadata.reportSummary?.transactionCount !== null && metadata.reportSummary?.transactionCount !== undefined) {
    formData.append("TransactionCount", String(metadata.reportSummary.transactionCount));
  }
  if (metadata.reportSummary?.submittedAt) {
    formData.append("SubmittedAt", metadata.reportSummary.submittedAt);
  }

  const response = await authenticatedFetch(`${apiBaseUrl}/api/analysis-sessions/${sessionId}/financial-metrics/file`, {
    method: "POST",
    body: formData,
  });

  if (!response.ok) {
    const uploadError = (await response.json().catch(() => ({}))) as { error?: string };
    throw new Error(
      uploadError.error ?? "Falló la carga del archivo. Revise el formato e inténtelo nuevamente."
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
    throw new Error("Correo electrónico o contraseña no válidos.");
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
