export type FinancialReportSummaryFormState = {
  reportName: string;
  totalAmount: string;
  transactionCount: string;
  submittedAt: string;
};

export type FinancialReportSummaryValue = {
  reportName: string;
  totalAmount: number;
  transactionCount: number;
  submittedAt: string;
};

type FinancialReportSummaryLike = {
  reportName?: string | null;
  totalAmount?: number | null;
  transactionCount?: number | null;
  submittedAt?: string | null;
};

export type FinancialReportSummaryIssue = {
  code: string;
  message: string;
  field: keyof FinancialReportSummaryFormState;
};

export type FinancialReportSummaryValidation = {
  isValid: boolean;
  value?: FinancialReportSummaryValue;
  issues: FinancialReportSummaryIssue[];
};

export function emptyFinancialReportSummaryForm(): FinancialReportSummaryFormState {
  return {
    reportName: "",
    totalAmount: "",
    transactionCount: "",
    submittedAt: "",
  };
}

export function validateFinancialReportSummary(
  form: FinancialReportSummaryFormState,
): FinancialReportSummaryValidation {
  const issues: FinancialReportSummaryIssue[] = [];
  const reportName = form.reportName.trim();
  const totalText = form.totalAmount.trim();
  const countText = form.transactionCount.trim();
  const submittedText = form.submittedAt.trim();
  const totalAmount = Number(totalText);
  const transactionCount = Number(countText);
  const submittedDate = new Date(submittedText);

  if (!reportName) {
    issues.push(issue("REPORT_NAME_REQUIRED", "El nombre del informe es obligatorio.", "reportName"));
  }
  if (!totalText) {
    issues.push(issue("TOTAL_AMOUNT_REQUIRED", "El importe total es obligatorio.", "totalAmount"));
  } else if (!Number.isFinite(totalAmount) || totalAmount < 0) {
    issues.push(issue("TOTAL_AMOUNT_INVALID", "El importe total debe ser un número mayor o igual a cero.", "totalAmount"));
  }
  if (!countText) {
    issues.push(issue("TRANSACTION_COUNT_REQUIRED", "La cantidad de transacciones es obligatoria.", "transactionCount"));
  } else if (!Number.isInteger(transactionCount) || transactionCount < 0) {
    issues.push(issue("TRANSACTION_COUNT_INVALID", "La cantidad debe ser un entero mayor o igual a cero.", "transactionCount"));
  }
  if (!submittedText) {
    issues.push(issue("SUBMITTED_AT_REQUIRED", "La fecha de envío es obligatoria.", "submittedAt"));
  } else if (Number.isNaN(submittedDate.getTime())) {
    issues.push(issue("SUBMITTED_AT_INVALID", "La fecha de envío no es válida.", "submittedAt"));
  }

  if (issues.length > 0) {
    return { isValid: false, issues };
  }

  return {
    isValid: true,
    value: {
      reportName,
      totalAmount,
      transactionCount,
      submittedAt: submittedDate.toISOString(),
    },
    issues: [],
  };
}

export function toFinancialReportSummaryForm(
  summary?: FinancialReportSummaryLike | null,
): FinancialReportSummaryFormState {
  if (!summary) {
    return emptyFinancialReportSummaryForm();
  }

  const submittedAt = summary.submittedAt ? new Date(summary.submittedAt) : null;

  return {
    reportName: summary.reportName ?? "",
    totalAmount: summary.totalAmount === null || summary.totalAmount === undefined
      ? ""
      : String(summary.totalAmount),
    transactionCount:
      summary.transactionCount === null || summary.transactionCount === undefined
        ? ""
        : String(summary.transactionCount),
    submittedAt:
      !submittedAt || Number.isNaN(submittedAt.getTime())
        ? ""
        : toLocalDateTimeValue(submittedAt),
  };
}

function toLocalDateTimeValue(value: Date) {
  const pad = (part: number) => String(part).padStart(2, "0");
  return `${value.getFullYear()}-${pad(value.getMonth() + 1)}-${pad(value.getDate())}T${pad(value.getHours())}:${pad(value.getMinutes())}`;
}

function issue(
  code: string,
  message: string,
  field: keyof FinancialReportSummaryFormState,
): FinancialReportSummaryIssue {
  return { code, message, field };
}
