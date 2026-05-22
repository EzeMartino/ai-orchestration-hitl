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
} from "../types/domain.types";

export const apiBaseUrl = import.meta.env.VITE_API_URL ?? "http://localhost:5148";

export async function loadSessionEvents(sessionId: string): Promise<ActivityEvent[]> {
  const response = await fetch(`${apiBaseUrl}/api/analysis-sessions/${sessionId}/events`);
  if (!response.ok) {
    throw new Error("Failed to load session events.");
  }
  return (await response.json()) as ActivityEvent[];
}

export async function loadSavedSessions(): Promise<AnalysisSessionSummary[]> {
  const response = await fetch(`${apiBaseUrl}/api/analysis-sessions`);
  if (!response.ok) {
    throw new Error("Failed to load saved analysis sessions.");
  }
  return (await response.json()) as AnalysisSessionSummary[];
}

export async function loadSessionDetails(sessionId: string): Promise<AnalysisSessionResponse> {
  const response = await fetch(`${apiBaseUrl}/api/analysis-sessions/${sessionId}`);
  if (!response.ok) {
    throw new Error("Failed to load analysis session.");
  }
  return (await response.json()) as AnalysisSessionResponse;
}

export async function loadStructuredFinancialMetrics(sessionId: string): Promise<GetFinancialMetricsResponse> {
  const response = await fetch(`${apiBaseUrl}/api/analysis-sessions/${sessionId}/financial-metrics`);
  if (!response.ok) {
    throw new Error("Failed to load structured financial metrics.");
  }
  return (await response.json()) as GetFinancialMetricsResponse;
}

export async function getStartPreflight(sessionId: string): Promise<AnalysisSessionStartPreflightResult> {
  const response = await fetch(`${apiBaseUrl}/api/analysis-sessions/${sessionId}/start-preflight`);
  if (!response.ok) {
    throw new Error("Failed to check start readiness.");
  }
  return (await response.json()) as AnalysisSessionStartPreflightResult;
}

export async function createSession(): Promise<AnalysisSessionResponse> {
  const response = await fetch(`${apiBaseUrl}/api/analysis-sessions`, {
    method: "POST",
  });
  if (!response.ok) {
    throw new Error("Failed to create analysis session.");
  }
  return (await response.json()) as AnalysisSessionResponse;
}

export async function startSession(sessionId: string): Promise<Response> {
  return await fetch(`${apiBaseUrl}/api/analysis-sessions/${sessionId}/start`, {
    method: "POST",
  });
}

export async function submitHumanDecision(
  sessionId: string,
  decision: "approve" | "reject",
  reason: string
): Promise<AnalysisSessionResponse> {
  const response = await fetch(`${apiBaseUrl}/api/analysis-sessions/${sessionId}/${decision}`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
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
  const response = await fetch(`${apiBaseUrl}/api/analysis-sessions/${sessionId}/financial-metrics`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
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
  const response = await fetch(`${apiBaseUrl}/api/analysis-sessions/${sessionId}/financial-metrics/csv`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
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

  const response = await fetch(`${apiBaseUrl}/api/analysis-sessions/${sessionId}/financial-metrics/file`, {
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
