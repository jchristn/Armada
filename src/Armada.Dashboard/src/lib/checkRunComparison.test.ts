import type { CheckRun } from '../types/models';
import {
  buildCheckRunComparison,
  buildCheckRunComparisonMap,
  formatCheckRunComparisonScope,
} from './checkRunComparison';

function createRun(overrides: Partial<CheckRun>): CheckRun {
  return {
    id: overrides.id || 'chk_current',
    tenantId: null,
    userId: null,
    workflowProfileId: null,
    vesselId: 'vsl_123',
    missionId: null,
    voyageId: null,
    deploymentId: null,
    source: 'Armada',
    providerName: null,
    externalId: null,
    externalUrl: null,
    label: null,
    type: 'UnitTest',
    status: 'Passed',
    environmentName: null,
    command: 'dotnet test',
    workingDirectory: 'C:/code/repo',
    branchName: null,
    commitHash: null,
    exitCode: 0,
    output: '',
    summary: '',
    testSummary: null,
    coverageSummary: null,
    artifacts: [],
    durationMs: 1000,
    startedUtc: '2026-05-03T00:00:01Z',
    completedUtc: '2026-05-03T00:00:02Z',
    createdUtc: '2026-05-03T00:00:00Z',
    lastUpdateUtc: '2026-05-03T00:00:02Z',
    ...overrides,
  };
}

describe('checkRunComparison', () => {
  it('prefers a previous run on the same branch', () => {
    const current = createRun({
      id: 'chk_current',
      branchName: 'feature/refactor',
      workflowProfileId: 'wfp_1',
      createdUtc: '2026-05-03T12:00:00Z',
    });
    const sameBranch = createRun({
      id: 'chk_same_branch',
      branchName: 'feature/refactor',
      workflowProfileId: 'wfp_1',
      createdUtc: '2026-05-03T11:00:00Z',
    });
    const differentBranch = createRun({
      id: 'chk_other_branch',
      branchName: 'main',
      workflowProfileId: 'wfp_1',
      createdUtc: '2026-05-03T11:30:00Z',
    });

    const comparison = buildCheckRunComparison(current, [current, differentBranch, sameBranch]);

    expect(comparison?.baseline.id).toBe('chk_same_branch');
    expect(comparison?.scope).toBe('same-branch');
    expect(formatCheckRunComparisonScope(comparison!.scope)).toBe('previous same branch');
  });

  it('falls back to the previous same check type and flags regressions', () => {
    const current = createRun({
      id: 'chk_current',
      status: 'Failed',
      createdUtc: '2026-05-03T12:00:00Z',
      durationMs: 2200,
      artifacts: [{ path: 'coverage.xml', sizeBytes: 10, lastWriteUtc: '2026-05-03T12:00:00Z' }],
      testSummary: { format: 'dotnet', total: 10, passed: 8, failed: 2, skipped: 0, durationMs: 2200 },
      coverageSummary: {
        format: 'cobertura',
        sourcePath: 'coverage.xml',
        lines: { covered: 80, total: 100, percentage: 80 },
        branches: null,
        functions: null,
        statements: { covered: 80, total: 100, percentage: 80 },
      },
    });
    const baseline = createRun({
      id: 'chk_previous',
      status: 'Passed',
      createdUtc: '2026-05-03T11:00:00Z',
      durationMs: 1800,
      artifacts: [
        { path: 'coverage.xml', sizeBytes: 10, lastWriteUtc: '2026-05-03T11:00:00Z' },
        { path: 'results.trx', sizeBytes: 12, lastWriteUtc: '2026-05-03T11:00:00Z' },
      ],
      testSummary: { format: 'dotnet', total: 10, passed: 10, failed: 0, skipped: 0, durationMs: 1800 },
      coverageSummary: {
        format: 'cobertura',
        sourcePath: 'coverage.xml',
        lines: { covered: 85, total: 100, percentage: 85 },
        branches: null,
        functions: null,
        statements: { covered: 85, total: 100, percentage: 85 },
      },
    });

    const comparison = buildCheckRunComparison(current, [current, baseline]);

    expect(comparison?.scope).toBe('same-check-type');
    expect(comparison?.hasRegression).toBe(true);
    expect(comparison?.statusChanged).toBe(true);
    expect(comparison?.testDelta.failed).toBe(2);
    expect(comparison?.coverageDelta.linesPct).toBe(-5);
    expect(comparison?.artifactCountDelta).toBe(-1);
  });

  it('builds a map for multiple runs in one comparable group', () => {
    const oldest = createRun({ id: 'chk_oldest', createdUtc: '2026-05-03T10:00:00Z' });
    const middle = createRun({ id: 'chk_middle', createdUtc: '2026-05-03T11:00:00Z' });
    const newest = createRun({ id: 'chk_newest', createdUtc: '2026-05-03T12:00:00Z' });

    const comparisons = buildCheckRunComparisonMap([newest, middle, oldest]);

    expect(comparisons.get('chk_newest')?.baseline.id).toBe('chk_middle');
    expect(comparisons.get('chk_middle')?.baseline.id).toBe('chk_oldest');
    expect(comparisons.has('chk_oldest')).toBe(false);
  });
});
