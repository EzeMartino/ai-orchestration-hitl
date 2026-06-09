export type FinancialWarningGroupItem = {
  ratioName: string;
  missingInputs: string[];
  messages: string[];
};

export type FinancialWarningPeriodGroup = {
  period: string;
  messageCount: number;
  items: FinancialWarningGroupItem[];
};

export type GroupedFinancialWarnings = {
  periodGroups: FinancialWarningPeriodGroup[];
  ungrouped: string[];
};

const missingInputWarningPatterns = [
  /^Missing (?<input>numerator|denominator) inputs? for (?<ratio>[a-z0-9_]+) in (?<period>20\d{2}[AE])\.$/i,
  /^Faltan insumos del (?<input>numerador) para (?<ratio>[a-z0-9_]+) en (?<period>20\d{2}[AE])\.$/i,
  /^Falta el (?<input>numerador|denominador) para (?<ratio>[a-z0-9_]+) en (?<period>20\d{2}[AE])\.$/i,
];

const warningFormatters: Array<[RegExp, (match: RegExpMatchArray) => string]> = [
  [
    /^No period comparisons were produced from the provided metrics\.$/i,
    () =>
      "No se generaron comparaciones entre periodos a partir de las métricas provistas.",
  ],
  [
    /^No ratios were computed from the provided structured metrics\.$/i,
    () =>
      "No se calcularon ratios a partir de las métricas estructuradas provistas.",
  ],
  [
    /^Missing metric ([a-z0-9_]+) for (20\d{2}[AE]) or (20\d{2}[AE])\.$/i,
    (match) =>
      `Falta la métrica ${match[1]} para ${match[2]} o ${match[3]}.`,
  ],
  [
    /^Metric ([a-z0-9_]+) has a non-numeric value for comparison\.$/i,
    (match) =>
      `La métrica ${match[1]} tiene un valor no numérico para la comparación.`,
  ],
  [
    /^Zero denominator for ([a-z0-9_]+) in (20\d{2}[AE])\.$/i,
    (match) => `Denominador cero para ${match[1]} en ${match[2]}.`,
  ],
  [
    /^Invalid (numerator|denominator) input for ([a-z0-9_]+) in (20\d{2}[AE])\.$/i,
    (match) =>
      `${match[1].toLowerCase() === "numerator" ? "Numerador" : "Denominador"} no válido para ${match[2]} en ${match[3]}.`,
  ],
  [
    /^Not enough comparable periods to detect trend-based risk signals\.$/i,
    () =>
      "No hay suficientes periodos comparables para detectar señales de riesgo basadas en tendencias.",
  ],
  [
    /^Not enough EBITDA margin periods to detect margin compression\.$/i,
    () =>
      "No hay suficientes periodos de margen EBITDA para detectar compresión de margen.",
  ],
  [
    /^Structured financial metrics are required for this mode but were not attached to this session\.$/i,
    () =>
      "Se requieren métricas financieras estructuradas para este modo, pero no se adjuntaron a esta sesión.",
  ],
  [
    /^Structured financial metrics are required for this mode but were not attached to the session\.$/i,
    () =>
      "Se requieren métricas financieras estructuradas para este modo, pero no se adjuntaron a la sesión.",
  ],
];

export function formatFinancialWarningText(warning: string): string {
  for (const [pattern, format] of warningFormatters) {
    const match = warning.match(pattern);

    if (match) {
      return format(match);
    }
  }

  return warning;
}

export function groupFinancialWarnings(
  warnings: string[],
): GroupedFinancialWarnings {
  const periodGroups: FinancialWarningPeriodGroup[] = [];
  const ungrouped: string[] = [];

  for (const warning of warnings) {
    const match = matchMissingInputWarning(warning);
    const groups = match?.groups;

    if (!groups) {
      ungrouped.push(warning);
      continue;
    }

    const period = groups.period;
    const ratioName = groups.ratio;
    const missingInput = normalizeMissingInput(groups.input);
    const periodGroup = getOrAddPeriodGroup(periodGroups, period);
    const item = getOrAddRatioItem(periodGroup.items, ratioName);

    periodGroup.messageCount += 1;
    item.messages.push(warning);

    if (!item.missingInputs.includes(missingInput)) {
      item.missingInputs.push(missingInput);
    }
  }

  return {
    periodGroups,
    ungrouped,
  };
}

function matchMissingInputWarning(warning: string): RegExpExecArray | null {
  for (const pattern of missingInputWarningPatterns) {
    const match = pattern.exec(warning);

    if (match) {
      return match;
    }
  }

  return null;
}

function normalizeMissingInput(input: string) {
  switch (input.toLowerCase()) {
    case "numerador":
      return "numerator";
    case "denominador":
      return "denominator";
    default:
      return input.toLowerCase();
  }
}

function getOrAddPeriodGroup(
  periodGroups: FinancialWarningPeriodGroup[],
  period: string,
): FinancialWarningPeriodGroup {
  const existing = periodGroups.find((group) => group.period === period);

  if (existing) {
    return existing;
  }

  const created = {
    period,
    messageCount: 0,
    items: [],
  };

  periodGroups.push(created);
  return created;
}

function getOrAddRatioItem(
  items: FinancialWarningGroupItem[],
  ratioName: string,
): FinancialWarningGroupItem {
  const existing = items.find((item) => item.ratioName === ratioName);

  if (existing) {
    return existing;
  }

  const created = {
    ratioName,
    missingInputs: [],
    messages: [],
  };

  items.push(created);
  return created;
}
