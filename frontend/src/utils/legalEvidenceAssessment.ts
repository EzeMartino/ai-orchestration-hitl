const legalAssessmentLabels: Record<string, string> = {
  None: "Ninguna",
  Weak: "Débil",
  Strong: "Fuerte",
  NotEstablished: "No determinada",
  Info: "Informativa",
  Warning: "Revisión requerida",
};

export function formatLegalRiskLevel(riskLevel: string): string {
  return riskLevel === "NotEstablished" ? "No determinado" : riskLevel;
}

export function formatLegalAssessmentValue(value: string): string {
  return legalAssessmentLabels[value] ?? value;
}
