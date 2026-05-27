export type ActivityEvent = {
  sessionId: string;
  type: string;
  agent: string;
  message: string;
  timestamp: string;
};

export type AnalysisSessionResponse = {
  id: string;
  status: string;
  createdAt?: string;
  updatedAt?: string;
  currentAgent?: string | null;
  contextJson?: string;
};

export type AnalysisSessionSummary = {
  id: string;
  status: string;
  currentAgent?: string | null;
  createdAt: string;
  updatedAt: string;
  completedAt?: string | null;
};

export type AnomalyEvidenceItem = {
  metric: string;
  value: number;
  threshold: number;
  interpretation: string;
};

export type ComplianceEvidenceItem = {
  regulation: string;
  section: string;
  finding: string;
  source: string;
};

export type LegalEvidenceReference = {
  source: string;
  title: string;
  url?: string | null;
  citation?: string | null;
  snippet?: string | null;
  regulationArea?: string | null;
  score?: number | null;
};

export type PossibleRegulatoryReviewArea = {
  title: string;
  description: string;
  severity: string;
  relatedFinancialSignals: string[];
  evidenceCitations: string[];
};

export type LegalAnalysisReviewResult = {
  reviewSummary: string;
  possibleRegulatoryReviewAreas: PossibleRegulatoryReviewArea[];
  evidenceReferences: LegalEvidenceReference[];
  warnings: string[];
  limitations: string[];
  usedLlm: boolean;
  usedFallback: boolean;
  provider?: string | null;
  model?: string | null;
  failureReason?: string | null;
};

export type ComplianceContext = {
  riskDetected: boolean;
  riskLevel: string;
  engine?: string;
  summary: string;
  evidence: ComplianceEvidenceItem[];
  warnings?: string[];
  legalReview?: LegalAnalysisReviewResult | null;
};

export type PlannerContext = {
  engine?: string;
  summary: string;
  recommendedActions: string[];
  riskFactors: string[];
  limitations: string[];
  usedLlm?: boolean;
  usedFallback?: boolean;
  provider?: string | null;
  model?: string | null;
  failureReason?: string | null;
};

export type ToolCallArguments = Record<string, string>;

export type ProposedToolCallContext = {
  toolName: string;
  arguments: ToolCallArguments;
  reason: string;
};

export type ApprovedToolCallContext = {
  toolName: string;
  arguments: ToolCallArguments;
  reason: string;
};

export type RejectedToolCallContext = {
  toolName: string;
  reason: string;
};

export type ExecutedToolCallContext = {
  toolName: string;
  status?: string;
  succeeded: boolean;
  summary: string;
  engine: string;
  error?: string | null;
};

export type ToolPlanContext = {
  proposedCalls: ProposedToolCallContext[];
  approvedCalls: ApprovedToolCallContext[];
  rejectedCalls: RejectedToolCallContext[];
  executedCalls: ExecutedToolCallContext[];
};

export type FinancialRatioContext = {
  name: string;
  period: string;
  value: number;
  unit?: string;
  formula?: string;
  source?: string;
  inputMetrics?: string[];
  sourcePage?: number | null;
  confidence?: number;
  interpretation?: string;
};

export type FinancialComparisonContext = {
  metricName: string;
  fromPeriod: string;
  toPeriod: string;
  fromValue: number;
  toValue: number;
  absoluteChange: number;
  percentageChange?: number | null;
  unit?: string;
  interpretation: string;
};

export type FinancialRiskSignalContext = {
  code: string;
  category: string;
  severity: string;
  metric: string;
  period: string;
  value?: number | null;
  threshold?: number | null;
  thresholdCode?: string | null;
  thresholdOperator?: string | null;
  thresholdValue?: number | null;
  reason?: string | null;
  explanation: string;
  sourcePage?: number | null;
  confidence?: number;
};

export type FinancialRiskEvidenceContext = {
  code: string;
  title: string;
  severity: string;
  message: string;
  metric?: string | null;
  period?: string | null;
  value?: number | null;
  threshold?: number | null;
  engine: string;
  sourceDocumentId?: string | null;
  sourcePage?: number | null;
  confidence?: number;
};

export type FinancialAnalysisAiKeyFindingContext = {
  title: string;
  description: string;
  severity: string;
  relatedMetrics: string[];
};

export type FinancialAnalysisAiDataQualityNoteContext = {
  message: string;
  severity: string;
  relatedFields: string[];
};

export type FinancialAnalysisAiReviewContext = {
  summary: string;
  keyFindings: FinancialAnalysisAiKeyFindingContext[];
  riskInterpretation: string;
  dataQualityNotes: FinancialAnalysisAiDataQualityNoteContext[];
  limitations: string[];
  usedLlm: boolean;
  usedFallback: boolean;
  provider?: string | null;
  model?: string | null;
  failureReason?: string | null;
};

export type FinancialRiskThreshold = {
  code: string;
  metric: string;
  operator: string;
  value: number;
  severity: string;
  description: string;
};

export type FinancialAnalysisContext = {
  engine: string;
  documentId: string;
  company?: string | null;
  ratios: FinancialRatioContext[];
  comparisons: FinancialComparisonContext[];
  riskSignals: FinancialRiskSignalContext[];
  riskEvidence: FinancialRiskEvidenceContext[];
  warnings: string[];
  limitations: string[];
  metricsInputSource?: string | null;
  metricsProvenance?: StructuredFinancialMetricsProvenanceContext | null;
  aiReview?: FinancialAnalysisAiReviewContext | null;
  thresholdProfile?: string | null;
  thresholdsUsed?: FinancialRiskThreshold[];
};

export type StructuredFinancialMetricInput = {
  name: string;
  period: string;
  value: number | null;
  unit?: string | null;
  currency?: string | null;
  source?: string | null;
  sourcePage?: number | null;
  confidence?: number | null;
};

export type StructuredFinancialMetricsInput = {
  documentId: string;
  company?: string | null;
  currency?: string | null;
  unit?: string | null;
  metrics: StructuredFinancialMetricInput[];
};

export type StructuredFinancialMetricsCsvInput = {
  documentId: string;
  company?: string | null;
  currency?: string | null;
  unit?: string | null;
  csv: string;
};

export type StructuredFinancialMetricsFileMetadata = {
  documentId?: string | null;
  company?: string | null;
  currency?: string | null;
  unit?: string | null;
};

export type FinancialMetricsValidationIssue = {
  code: string;
  message: string;
  metricName?: string | null;
  period?: string | null;
  severity: string;
};

export type StructuredFinancialMetricContext = {
  name: string;
  period: string;
  value: number;
  unit: string;
  statement?: string;
  source?: string | null;
  currency?: string | null;
  sourcePage?: number | null;
  confidence?: number | null;
};

export type StructuredFinancialMetricsProvenanceContext = {
  ingestionMethod: string;
  originalFileName?: string | null;
  fileSizeBytes?: number | null;
  contentHash?: string | null;
  metricCount: number;
  warningCount: number;
};

export type StructuredFinancialMetricsContext = {
  documentId: string;
  company?: string | null;
  currency?: string | null;
  unit?: string | null;
  metrics: StructuredFinancialMetricContext[];
  validationWarnings: FinancialMetricsValidationIssue[];
  uploadedAt: string;
  provenance?: StructuredFinancialMetricsProvenanceContext | null;
};

export type SaveFinancialMetricsResponse = {
  sessionId: string;
  isValid: boolean;
  context?: StructuredFinancialMetricsContext | null;
  errors: FinancialMetricsValidationIssue[];
  warnings: FinancialMetricsValidationIssue[];
};

export type GetFinancialMetricsResponse = {
  sessionId: string;
  context?: StructuredFinancialMetricsContext | null;
};

export type FileUploadErrorResponse = {
  error?: string;
};

export type AnalysisSessionStartPreflightIssue = {
  code: string;
  message: string;
  severity: string;
};

export type AnalysisSessionStartPreflightResult = {
  canStart: boolean;
  errors: AnalysisSessionStartPreflightIssue[];
  warnings: AnalysisSessionStartPreflightIssue[];
};

export type AnalysisContext = {
  summary?: string;
  planner?: PlannerContext;
  toolPlan?: ToolPlanContext;
  anomaly?: {
    detected: boolean;
    severity: string;
    engine?: string;
    category: string;
    summary: string;
    evidence: AnomalyEvidenceItem[];
    recommendation: string;
  };
  financialAnalysis?: FinancialAnalysisContext | null;
  compliance?: ComplianceContext;
};

export type LoginRequest = {
  email: string;
  password: string;
};

export type LoginResponse = {
  tokenType: string;
  accessToken: string;
  expiresIn: number;
  refreshToken: string;
};

export type RegisterRequest = {
  email: string;
  password: string;
};
