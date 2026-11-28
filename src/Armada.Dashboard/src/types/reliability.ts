// Reliability surfaces (1.5.0): mission attempt history, structured stage submissions, the reliability summary per
// model and persona, acceptance gate reports, and battery runs. Field names follow the Admiral's camelCase JSON.

/** Outcome totals for one model and persona over the summary window. Rates are 0..1. */
export interface ReliabilitySummaryRow {
  model: string;
  persona: string;
  attempts: number;
  completed: number;
  failed: number;
  relaunched: number;
  repairs: number;
  escalations: number;
  unclassified: number;
  completionRate: number;
  unclassifiedRate: number;
  failureKinds: Record<string, number>;
  runtimeFailureKinds: Record<string, number>;
  medianDurationMs?: number | null;
}

export interface ReliabilitySummary {
  fromUtc: string;
  toUtc: string;
  outcomesRead: number;
  truncated: boolean;
  totals: ReliabilitySummaryRow;
  pageNumber: number;
  pageSize: number;
  totalRecords: number;
  totalPages: number;
  rows: ReliabilitySummaryRow[];
}

/** Server sort keys for the reliability summary. */
export type ReliabilitySort = 'Attempts' | 'CompletionRate' | 'UnclassifiedRate' | 'Repairs' | 'Escalations' | 'Failures' | 'Model' | 'Persona';

export interface ReliabilitySummaryQuery {
  model?: string;
  persona?: string;
  vesselId?: string;
  includeEvaluation?: boolean;
  fromUtc?: string;
  toUtc?: string;
  sort?: ReliabilitySort;
  descending?: boolean;
  pageNumber?: number;
  pageSize?: number;
}

export interface GateMeasure {
  id: string;
  name: string;
  value: string;
  threshold: string;
  status: string;
  detail: string;
}

export interface GatePipelineResult {
  pipeline: string;
  runId?: string | null;
  status: string;
  passed: number;
  failed: number;
  couldNotRun: number;
  pendingEvaluation: number;
}

/** A stored 1.5.0 acceptance gate report. */
export interface GateReport {
  id?: string | null;
  uploadedUtc?: string | null;
  startedUtc: string;
  finishedUtc: string;
  armadaVersion?: string | null;
  packs: string[];
  pipelines: GatePipelineResult[];
  measures: GateMeasure[];
  passed: boolean;
}

/** One battery run (GET /api/v1/battery/runs). */
export interface BatteryRun {
  id: string;
  tenantId?: string | null;
  userId?: string | null;
  captainId?: string | null;
  captainName?: string | null;
  runCount: number;
  status: string;
  isEvaluation: boolean;
  runtime?: string | null;
  model?: string | null;
  modelEndpointId?: string | null;
  totalTasks: number;
  passedCount: number;
  failedCount: number;
  couldNotRunCount: number;
  pendingEvaluationCount: number;
  failureReason?: string | null;
  createdUtc: string;
  startedUtc?: string | null;
  completedUtc?: string | null;
  lastUpdateUtc: string;
}
