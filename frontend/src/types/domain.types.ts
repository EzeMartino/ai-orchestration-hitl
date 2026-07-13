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

export type FinancialReportSummaryInput = {
  reportName?: string | null;
  totalAmount?: number | null;
  transactionCount?: number | null;
  submittedAt?: string | null;
};

export type FinancialReportSummary = {
  reportName: string;
  totalAmount: number;
  transactionCount: number;
  submittedAt: string;
};

export type StructuredFinancialMetricsInput = {
  documentId: string;
  company?: string | null;
  currency?: string | null;
  unit?: string | null;
  metrics: StructuredFinancialMetricInput[];
  reportSummary?: FinancialReportSummaryInput | null;
};

export type StructuredFinancialMetricsCsvInput = {
  documentId: string;
  company?: string | null;
  currency?: string | null;
  unit?: string | null;
  csv: string;
  reportSummary?: FinancialReportSummaryInput | null;
};

export type StructuredFinancialMetricsFileMetadata = {
  documentId?: string | null;
  company?: string | null;
  currency?: string | null;
  unit?: string | null;
  reportSummary?: FinancialReportSummaryInput | null;
};

export type FinancialMetricsValidationIssue = {
  code: string;
  message: string;
  metricName?: string | null;
  period?: string | null;
  severity: string;
};

export type FinancialMetricsFileOutcome =
  | "accepted"
  | "review_required"
  | "failed";

export type FinancialMetricCandidateReviewState =
  | "explicit"
  | "inferred"
  | "conflict"
  | "missing"
  | "accepted"
  | "rejected"
  | "human_corrected";

export type FinancialMetricCandidateSourceKind =
  | "reported"
  | "inferred"
  | "computed"
  | "human_corrected";

export type FinancialMetricCandidate = {
  id: string;
  name: string;
  period: string;
  value?: number | null;
  currency?: string | null;
  unit?: string | null;
  sourceKind: FinancialMetricCandidateSourceKind | string;
  confidence: number;
  sourcePage?: number | null;
  evidence: string;
  extractionStrategy: string;
  reviewState: FinancialMetricCandidateReviewState;
  inferenceExplanation?: string | null;
};

export type FinancialDocumentMetadataCandidate = {
  id: string;
  fieldName: string;
  value: string;
  sourceKind: FinancialMetricCandidateSourceKind | string;
  confidence: number;
  sourcePage?: number | null;
  evidence: string;
  extractionStrategy: string;
  reviewState: FinancialMetricCandidateReviewState;
  inferenceExplanation?: string | null;
};

export type FinancialMetricCandidateConflict = {
  kind: string;
  fieldName: string;
  metricName?: string | null;
  period?: string | null;
  proposedValue?: string | null;
  metricCandidates: FinancialMetricCandidate[];
  metadataCandidates: FinancialDocumentMetadataCandidate[];
  isResolved: boolean;
  selectedCandidateId?: string | null;
  resolutionDecision?: string | null;
};

export type FinancialMetricsExtractionDiagnostics = {
  nativeTextAvailable: boolean;
  ocrAttempted: boolean;
  ocrSucceeded: boolean;
  markItDownAttempted: boolean;
  markItDownSucceeded: boolean;
  semanticAttempted: boolean;
  semanticSucceeded: boolean;
  pageCount?: number | null;
  markdownCharacterCount?: number | null;
  candidateCount?: number | null;
  conflictCount?: number | null;
  nativeTextDurationMilliseconds?: number | null;
  ocrDurationMilliseconds?: number | null;
  markItDownDurationMilliseconds?: number | null;
  semanticDurationMilliseconds?: number | null;
  totalDurationMilliseconds?: number | null;
  reasonCodes: string[];
};

export type FinancialMetricsExtractionDraftPayload = {
  schemaVersion: number;
  proposedInput: StructuredFinancialMetricsInput;
  candidates: FinancialMetricCandidate[];
  conflicts: FinancialMetricCandidateConflict[];
  missingFields: string[];
  fallbackReasons: string[];
  validationIssues: FinancialMetricsValidationIssue[];
  diagnostics: FinancialMetricsExtractionDiagnostics;
  metadataCandidates?: FinancialDocumentMetadataCandidate[];
};

export type FinancialMetricsExtractionDraft = {
  id: string;
  sessionId: string;
  status: string;
  originalFileName: string;
  fileSizeBytes: number;
  contentHash: string;
  payload: FinancialMetricsExtractionDraftPayload;
  createdAt: string;
  updatedAt: string;
  completedAt?: string | null;
};

export type FinancialMetricsExtractionDraftIdentity = Omit<
  FinancialMetricsExtractionDraft,
  "payload"
>;

export type FinancialMetricCandidateReviewDecision =
  | "accepted"
  | "rejected"
  | "human_corrected";

export type FinancialMetricCandidateReviewUpdate = {
  candidateId: string;
  decision: FinancialMetricCandidateReviewDecision;
  value?: number | null;
  currency?: string | null;
  unit?: string | null;
  metadataValue?: string | null;
};

export type FinancialMetricCandidateAddition = {
  name: string;
  period: string;
  value?: number | null;
  currency?: string | null;
  unit?: string | null;
};

export type FinancialDocumentMetadataCandidateAddition = {
  fieldName: string;
  value: string;
};

export type UpdateFinancialMetricsExtractionDraftRequest = {
  candidates: FinancialMetricCandidateReviewUpdate[];
  proposedInput: StructuredFinancialMetricsInput;
  metricAdditions?: FinancialMetricCandidateAddition[];
  metadataAdditions?: FinancialDocumentMetadataCandidateAddition[];
};

export type ConfirmFinancialMetricsExtractionDraftRequest = {
  reportSummary?: FinancialReportSummaryInput | null;
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
  fileName?: string;
  fileType?: string;
  fileSizeBytes?: number;
  outcome?: FinancialMetricsFileOutcome;
  reviewDraft?: FinancialMetricsExtractionDraft | null;
  reportSummary?: FinancialReportSummary | null;
};

export type GetFinancialMetricsResponse = {
  sessionId: string;
  context?: StructuredFinancialMetricsContext | null;
  reportSummary?: FinancialReportSummary | null;
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
