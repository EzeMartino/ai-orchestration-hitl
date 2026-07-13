import type {
  FinancialAnalysisExecutionContext,
  FinancialAnalysisStageExecutionContext,
} from "../types/domain.types";

export type FinancialAnalysisExecutionBannerTone =
  | "warning"
  | "danger"
  | "neutral";

export type FinancialAnalysisExecutionBanner = {
  tone: FinancialAnalysisExecutionBannerTone;
  title: string;
  description: string;
  affectedStages: string[];
};

const visibleFailureCodes = new Set([
  "PYTHON_INVOCATION_FAILED",
  "PYTHON_RESPONSE_INVALID",
]);

function getStageLabel(operation: string): string | null {
  switch (operation) {
    case "ratios":
      return "Ratios financieros";
    case "comparisons":
      return "Comparaciones entre períodos";
    case "signals":
      return "Señales de riesgo";
    case "summary":
      return "Resumen de evidencia";
    default:
      return null;
  }
}

function formatAffectedStage(
  stage: FinancialAnalysisStageExecutionContext,
): string | null {
  if (stage.status !== "failed" || !stage.operation) {
    return null;
  }

  const label = getStageLabel(stage.operation);
  if (!label) {
    return null;
  }

  return stage.failureCode && visibleFailureCodes.has(stage.failureCode)
    ? `${label} — ${stage.failureCode}`
    : label;
}

export function getFinancialAnalysisExecutionBanner(
  execution?: FinancialAnalysisExecutionContext | null,
): FinancialAnalysisExecutionBanner | null {
  if (execution?.overallStatus === "succeeded") {
    return null;
  }

  const stages = Array.isArray(execution?.stages) ? execution.stages : [];
  const affectedStages = stages
    .map((stage) =>
      stage && typeof stage === "object" ? formatAffectedStage(stage) : null,
    )
    .filter((stage): stage is string => stage !== null);

  if (execution?.overallStatus === "degraded") {
    return {
      tone: "warning",
      title: "Análisis incompleto — revisión humana requerida.",
      description:
        "Algunas etapas técnicas no se completaron. La evidencia válida disponible se conserva para revisión.",
      affectedStages,
    };
  }

  if (execution?.overallStatus === "failed") {
    return {
      tone: "danger",
      title:
        "No se pudo completar la evaluación de riesgo — revisión humana requerida.",
      description:
        "La evaluación no es concluyente. Revise la evidencia disponible antes de continuar.",
      affectedStages,
    };
  }

  return {
    tone: "neutral",
    title:
      "No hay metadatos de ejecución disponibles para este análisis histórico.",
    description:
      "El estado técnico no puede verificarse con los datos guardados; no debe interpretarse como una ejecución exitosa.",
    affectedStages: [],
  };
}
