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

function isStageObject(
  value: unknown,
): value is FinancialAnalysisStageExecutionContext {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

function hasTrustworthySucceededStages(stages: unknown): boolean {
  if (!Array.isArray(stages)) {
    return false;
  }

  if (stages.length === 0) {
    return true;
  }

  if (stages.length !== 4) {
    return false;
  }

  const operations = new Set<string>();
  for (const stage of stages) {
    if (
      !isStageObject(stage) ||
      typeof stage.operation !== "string" ||
      getStageLabel(stage.operation) === null ||
      stage.status !== "succeeded" ||
      operations.has(stage.operation)
    ) {
      return false;
    }

    operations.add(stage.operation);
  }

  return operations.size === 4;
}

function formatAffectedStage(
  stage: unknown,
): string | null {
  if (
    !isStageObject(stage) ||
    stage.status !== "failed" ||
    typeof stage.operation !== "string"
  ) {
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

function getAffectedStages(stages: unknown): string[] {
  if (!Array.isArray(stages)) {
    return [];
  }

  const affectedStages: string[] = [];
  const affectedOperations = new Set<string>();

  for (const stage of stages) {
    if (
      !isStageObject(stage) ||
      typeof stage.operation !== "string" ||
      affectedOperations.has(stage.operation)
    ) {
      continue;
    }

    const affectedStage = formatAffectedStage(stage);
    if (affectedStage) {
      affectedOperations.add(stage.operation);
      affectedStages.push(affectedStage);
    }
  }

  return affectedStages;
}

export function getFinancialAnalysisExecutionBanner(
  execution?: FinancialAnalysisExecutionContext | null,
): FinancialAnalysisExecutionBanner | null {
  if (
    execution?.overallStatus === "succeeded" &&
    hasTrustworthySucceededStages(execution.stages)
  ) {
    return null;
  }

  const affectedStages = getAffectedStages(execution?.stages);

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
