import type { BlackBoxEntry, BlackBoxEntrySummary, BlackBoxRecoveryPath } from '@dashboard/types/blackbox';

// A sanitized LandingFailed entry shaped like src/Test.Shared/Fixtures/BlackBox/land-001-dirty-checkout.json (a dirty
// checkout blocked the merge), plus a conflicting mission, conflicted files and directories, and two recovery paths
// (one marked dataLossRisk).

export const RETRY_PATH: BlackBoxRecoveryPath = {
  id: 'rp_retry',
  rank: 1,
  title: 'Clean the checkout and retry landing',
  steps: [
    { order: 1, description: 'Retry landing the mission', cliCommand: 'armada mission retry-landing msn_1', action: { tool: 'retry_landing', arguments: { missionId: 'msn_1' } } },
  ],
  action: { tool: 'retry_landing', arguments: { missionId: 'msn_1' } },
  risk: 'Low',
  dataLossRisk: false,
  expectedOutcome: 'The mission lands and completes.',
  ruleId: 'BBX-LAND-001',
};

export const CANCEL_PATH: BlackBoxRecoveryPath = {
  id: 'rp_cancel',
  rank: 2,
  title: 'Cancel the mission and free the dock',
  steps: [
    { order: 1, description: 'Cancel the mission', action: { tool: 'cancel_mission', arguments: { missionId: 'msn_1' } }, dataLossRisk: true },
    { order: 2, description: 'Unstick the dock', action: { tool: 'unstick_dock', arguments: { dockId: 'dck_1' } }, dataLossRisk: true },
  ],
  risk: 'High',
  dataLossRisk: true,
  ruleId: 'BBX-LAND-001',
};

export const MANUAL_PATH: BlackBoxRecoveryPath = {
  id: 'rp_manual',
  rank: 3,
  title: 'Commit or stash the local changes by hand',
  steps: [{ order: 1, description: 'Run git status in the checkout and commit or stash' }],
  risk: 'Low',
};

const CONFLICTED_FILES = Array.from({ length: 12 }, (_v, i) => `src/Armada.Core/File${i + 1}.cs`);

export function blackBoxEntry(overrides: Partial<BlackBoxEntry> = {}): BlackBoxEntry {
  return {
    id: 'bbx_1',
    subjectType: 'Mission',
    missionId: 'msn_1',
    voyageId: 'vyg_1',
    vesselId: 'vsl_1',
    captainId: 'cpt_1',
    missionTitle: 'Fix login',
    vesselName: 'api',
    attempt: 1,
    isCurrent: true,
    phase: 'Landing',
    step: 'DirtyCheckout',
    failureKind: 'LandingConflict',
    statusBefore: 'WorkProduced',
    statusAfter: 'LandingFailed',
    triage: 'UserConfig',
    fingerprint: 'fp_1',
    captureMode: 'Inline',
    state: 'Analyzed',
    resolution: 'Open',
    pinned: false,
    reasonExcerpt: 'Landing blocked: the checkout has 52 staged paths.',
    totalBytes: 2048,
    objectCount: 4,
    createdUtc: '2026-10-28T09:00:00Z',
    summary: 'Landing stopped because the local checkout was dirty.',
    record: {
      entryId: 'bbx_1',
      schemaVersion: 1,
      status: { before: 'WorkProduced', after: 'LandingFailed' },
      site: { phase: 'Landing', step: 'DirtyCheckout', hostKind: 'Admiral', host: 'build-01', component: 'LandingService', method: 'LandAsync', sourceFile: 'src/Armada.Server/LandingService.cs', sourceLine: 412 },
      mechanism: { gitOperation: 'merge', gitStderrTail: 'error: Your local changes would be overwritten by merge.' },
      subject: { branchName: 'armada/msn_1' },
      conflict: {
        isConflict: true,
        conflictedFiles: CONFLICTED_FILES.slice(0, 11),
        conflictedFileTotal: 12,
        conflictedDirectories: [{ directory: 'src/Armada.Core', depth: 2, fileCount: 12 }],
        conflictingMissions: [{ missionId: 'msn_2', title: 'Refactor auth', overlappingFileTotal: 3 }],
      },
      analysis: {
        summary: 'The mission produced work, but landing stopped because the checkout had 52 staged paths.',
        why: [{ ruleId: 'BBX-LAND-001', statement: 'The vessel checkout was dirty when landing started.', confidence: 'High' }],
        recoveryPaths: [CANCEL_PATH, RETRY_PATH, MANUAL_PATH],
      },
    },
    ...overrides,
  };
}

export function blackBoxSummary(id: string, overrides: Partial<BlackBoxEntrySummary> = {}): BlackBoxEntrySummary {
  const { record: _r, summary: _s, ...rest } = blackBoxEntry();
  return { ...rest, id, missionId: `msn_${id}`, missionTitle: `Mission ${id}`, ...overrides };
}

export const AGENT_BRIEF = '# FIX THIS\n\nMission msn_1 failed to land: the checkout was dirty.';
export const USER_BRIEF = '# What happened\n\nLanding stopped because the checkout was dirty.';
