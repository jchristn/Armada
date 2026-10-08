import type { CheckRun, CheckRunCoverageMetric } from '../types/models';

export type CheckRunComparisonScope = 'same-branch' | 'same-profile' | 'same-environment' | 'same-check-type';

export interface CheckRunComparison {
  baseline: CheckRun;
  scope: CheckRunComparisonScope;
  statusChanged: boolean;
  hasRegression: boolean;
  hasImprovement: boolean;
  durationDeltaMs: number | null;
  artifactCountDelta: number;
  testDelta: {
    passed: number | null;
    failed: number | null;
    skipped: number | null;
    total: number | null;
  };
  coverageDelta: {
    linesPct: number | null;
    branchesPct: number | null;
    functionsPct: number | null;
    statementsPct: number | null;
  };
}

const TERMINAL_BASELINE_STATUSES = new Set(['Passed', 'Failed']);

export function buildCheckRunComparisonMap(runs: CheckRun[]): Map<string, CheckRunComparison> {
  const comparisons = new Map<string, CheckRunComparison>();
  const grouped = new Map<string, CheckRun[]>();

  for (const run of runs) {
    const groupKey = buildGroupKey(run);
    if (!groupKey) continue;
    const existing = grouped.get(groupKey);
    if (existing) existing.push(run);
    else grouped.set(groupKey, [run]);
  }

  for (const groupRuns of grouped.values()) {
    const sorted = [...groupRuns].sort(compareByCreatedDesc);
    for (let index = 0; index < sorted.length; index += 1) {
      const current = sorted[index];
      const comparison = buildCheckRunComparison(current, sorted);
      if (comparison) comparisons.set(current.id, comparison);
    }
  }

  return comparisons;
}

export function buildCheckRunComparison(currentRun: CheckRun, runs: CheckRun[]): CheckRunComparison | null {
  const comparableRuns = runs
    .filter((run) => run.id !== currentRun.id)
    .filter((run) => buildGroupKey(run) === buildGroupKey(currentRun))
    .filter((run) => isOlderThan(run, currentRun))
    .filter((run) => TERMINAL_BASELINE_STATUSES.has(run.status))
    .sort(compareByCreatedDesc);

  if (comparableRuns.length === 0) return null;

  const workflowProfileId = normalize(currentRun.workflowProfileId);
  const environmentName = normalize(currentRun.environmentName);
  const branchName = normalize(currentRun.branchName);

  const exactScoped = comparableRuns.find((candidate) =>
    sameNormalized(workflowProfileId, normalize(candidate.workflowProfileId))
    && sameNormalized(environmentName, normalize(candidate.environmentName))
    && (!branchName || sameNormalized(branchName, normalize(candidate.branchName))));

  if (exactScoped) {
    return createComparison(
      currentRun,
      exactScoped,
      branchName ? 'same-branch'
        : workflowProfileId ? 'same-profile'
          : environmentName ? 'same-environment'
            : 'same-check-type');
  }

  if (workflowProfileId) {
    const sameProfile = comparableRuns.find((candidate) =>
      sameNormalized(workflowProfileId, normalize(candidate.workflowProfileId))
      && sameNormalized(environmentName, normalize(candidate.environmentName)));
    if (sameProfile) return createComparison(currentRun, sameProfile, 'same-profile');
  }

  if (environmentName) {
    const sameEnvironment = comparableRuns.find((candidate) =>
      sameNormalized(environmentName, normalize(candidate.environmentName)));
    if (sameEnvironment) return createComparison(currentRun, sameEnvironment, 'same-environment');
  }

  return createComparison(currentRun, comparableRuns[0], 'same-check-type');
}

export function formatCheckRunComparisonScope(scope: CheckRunComparisonScope): string {
  switch (scope) {
    case 'same-branch':
      return 'previous same branch';
    case 'same-profile':
      return 'previous same profile';
    case 'same-environment':
      return 'previous same environment';
    default:
      return 'previous same check type';
  }
}

export function formatCheckRunComparisonSummary(comparison: CheckRunComparison): string {
  const parts: string[] = [];
  if (comparison.statusChanged) parts.push(`status ${comparison.baseline.status} -> ${comparison.baseline.status === 'Passed' && comparison.hasRegression ? 'Failed' : comparison.baseline.status === 'Failed' && comparison.hasImprovement ? 'Passed' : 'changed'}`);

  const failedDelta = formatSignedCountDelta(comparison.testDelta.failed, 'failed');
  if (failedDelta) parts.push(failedDelta);

  const lineCoverageDelta = formatSignedPercentageDelta(comparison.coverageDelta.linesPct, 'lines');
  if (lineCoverageDelta) parts.push(lineCoverageDelta);

  const durationDelta = formatSignedDurationDelta(comparison.durationDeltaMs);
  if (durationDelta) parts.push(`duration ${durationDelta}`);

  const artifactDelta = formatSignedCountDelta(comparison.artifactCountDelta, 'artifacts');
  if (artifactDelta) parts.push(artifactDelta);

  return parts.length > 0 ? parts.join(' | ') : 'No material change';
}

export function formatSignedCountDelta(value: number | null, label: string): string | null {
  if (value == null || value === 0) return null;
  return `${value > 0 ? '+' : ''}${value} ${label}`;
}

export function formatSignedPercentageDelta(value: number | null, label?: string): string | null {
  if (value == null || Math.abs(value) < 0.01) return null;
  const formatted = `${value > 0 ? '+' : ''}${value.toFixed(Math.abs(value) % 1 < 0.01 ? 0 : 2)}%`;
  return label ? `${label} ${formatted}` : formatted;
}

export function formatSignedDurationDelta(durationMs: number | null): string | null {
  if (durationMs == null || Math.abs(durationMs) < 1) return null;
  const absolute = Math.abs(durationMs);
  const text = absolute < 1000
    ? `${Math.round(absolute)} ms`
    : absolute >= 60_000
      ? `${(absolute / 60_000).toFixed(absolute % 60_000 === 0 ? 0 : 2)} min`
      : `${(absolute / 1000).toFixed(absolute % 1000 === 0 ? 0 : 2)} s`;
  return `${durationMs > 0 ? '+' : '-'}${text}`;
}

function createComparison(currentRun: CheckRun, baseline: CheckRun, scope: CheckRunComparisonScope): CheckRunComparison {
  const failedDelta = delta(currentRun.testSummary?.failed, baseline.testSummary?.failed);
  const lineCoverageDelta = delta(metricPercentage(currentRun.coverageSummary?.lines), metricPercentage(baseline.coverageSummary?.lines));
  const statementCoverageDelta = delta(metricPercentage(currentRun.coverageSummary?.statements), metricPercentage(baseline.coverageSummary?.statements));

  const hasRegression = (baseline.status === 'Passed' && currentRun.status === 'Failed')
    || (failedDelta != null && failedDelta > 0)
    || (lineCoverageDelta != null && lineCoverageDelta < 0)
    || (statementCoverageDelta != null && statementCoverageDelta < 0);

  const hasImprovement = (baseline.status === 'Failed' && currentRun.status === 'Passed')
    || (failedDelta != null && failedDelta < 0)
    || (lineCoverageDelta != null && lineCoverageDelta > 0)
    || (statementCoverageDelta != null && statementCoverageDelta > 0);

  return {
    baseline,
    scope,
    statusChanged: currentRun.status !== baseline.status,
    hasRegression,
    hasImprovement,
    durationDeltaMs: delta(currentRun.durationMs, baseline.durationMs),
    artifactCountDelta: currentRun.artifacts.length - baseline.artifacts.length,
    testDelta: {
      passed: delta(currentRun.testSummary?.passed, baseline.testSummary?.passed),
      failed: failedDelta,
      skipped: delta(currentRun.testSummary?.skipped, baseline.testSummary?.skipped),
      total: delta(currentRun.testSummary?.total, baseline.testSummary?.total),
    },
    coverageDelta: {
      linesPct: lineCoverageDelta,
      branchesPct: delta(metricPercentage(currentRun.coverageSummary?.branches), metricPercentage(baseline.coverageSummary?.branches)),
      functionsPct: delta(metricPercentage(currentRun.coverageSummary?.functions), metricPercentage(baseline.coverageSummary?.functions)),
      statementsPct: statementCoverageDelta,
    },
  };
}

function metricPercentage(metric: CheckRunCoverageMetric | null | undefined): number | null {
  return metric?.percentage ?? null;
}

function delta(current: number | null | undefined, baseline: number | null | undefined): number | null {
  if (current == null || baseline == null) return null;
  return Math.round((current - baseline) * 100) / 100;
}

function buildGroupKey(run: CheckRun): string | null {
  if (!run.vesselId) return null;
  return `${run.vesselId}::${run.type}`;
}

function compareByCreatedDesc(left: CheckRun, right: CheckRun): number {
  const deltaMs = parseDate(right.createdUtc) - parseDate(left.createdUtc);
  if (deltaMs !== 0) return deltaMs;
  return right.id.localeCompare(left.id);
}

function isOlderThan(candidate: CheckRun, currentRun: CheckRun): boolean {
  const candidateMs = parseDate(candidate.createdUtc);
  const currentMs = parseDate(currentRun.createdUtc);
  if (candidateMs !== currentMs) return candidateMs < currentMs;
  return candidate.id.localeCompare(currentRun.id) < 0;
}

function parseDate(value: string | null | undefined): number {
  const parsed = Date.parse(value || '');
  return Number.isFinite(parsed) ? parsed : 0;
}

function normalize(value: string | null | undefined): string {
  return (value || '').trim();
}

function sameNormalized(left: string, right: string): boolean {
  return left === right;
}
