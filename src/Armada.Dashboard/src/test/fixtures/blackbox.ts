import type { BlackBoxEntry, BlackBoxEntrySummary, BlackBoxResilience } from '../../types/blackbox';

/** A landing-conflict black box entry with every section filled, for the panel and page tests. */
export function blackBoxEntryFixture(overrides: Partial<BlackBoxEntry> = {}): BlackBoxEntry {
  return {
    id: 'bbx_2',
    subjectType: 'Mission',
    missionId: 'msn_1',
    voyageId: 'vyg_1',
    vesselId: 'vsl_1',
    captainId: 'cpt_1',
    missionTitle: 'Add request logging',
    vesselName: 'api-server',
    attempt: 2,
    isCurrent: true,
    phase: 'Landing',
    step: 'LocalMerge',
    failureKind: 'LandingConflict',
    statusBefore: 'WorkProduced',
    statusAfter: 'LandingFailed',
    triage: 'UserConfig',
    fingerprint: 'fp_abcdef0123456789',
    captureMode: 'Inline',
    state: 'Analyzed',
    resolution: 'Open',
    pinned: false,
    reasonExcerpt: 'Merge conflict in src/logging.ts',
    totalBytes: 4096,
    objectCount: 3,
    createdUtc: '2026-10-28T10:00:00Z',
    dockId: 'dck_1',
    mergeEntryId: 'mrg_1',
    lastUpdateUtc: '2026-10-28T10:05:00Z',
    summary: 'The landing merge hit conflicts that another mission introduced.',
    record: {
      entryId: 'bbx_2',
      schemaVersion: 1,
      captureMode: 'Inline',
      state: 'Analyzed',
      subject: { missionId: 'msn_1', title: 'Add request logging', captainId: 'cpt_1', captainName: 'Claude', captainRuntime: 'ClaudeCode', captainModel: 'opus' },
      status: { before: 'WorkProduced', after: 'LandingFailed' },
      site: { phase: 'Landing', step: 'LocalMerge', host: 'Admiral', component: 'LandingService', method: 'MergeAsync', sourceFile: 'src/Armada.Core/Services/LandingService.cs', sourceLine: 412, armadaVersion: '1.2.0' },
      mechanism: { exceptionType: 'GitMergeConflictException', exceptionMessage: 'CONFLICT (content)', gitOperation: 'merge', gitStderrTail: 'CONFLICT (content): Merge conflict in src/logging.ts', stackFrames: ['LandingService.MergeAsync'] },
      classification: { kind: 'LandingConflict', rawReason: 'Merge conflict in src/logging.ts', suspectedCause: 'ConcurrentChange', causeRuleId: 'BB-CONFLICT-001' },
      workspace: { dockState: 'Active', branchName: 'armada/request-logging', targetBranch: 'main', commitsBehindTarget: 3, checkoutDirty: false },
      conflict: {
        isConflict: true,
        conflictedFiles: ['src/logging.ts', 'src/middleware/request.ts'],
        conflictedFileTotal: 2,
        conflictedDirectories: [{ directory: 'src/middleware', depth: 2, fileCount: 1 }],
        conflictingMissions: [{ missionId: 'msn_0', title: 'Refactor middleware', commitHash: 'abcdef0123456789', overlappingFiles: ['src/middleware/request.ts'] }],
      },
      configuration: { values: [{ setting: 'landingMode', value: 'LocalMerge', source: 'Vessel' }] },
      analysis: {
        summary: 'Another mission changed the same files before this one landed.',
        why: [{ ruleId: 'BB-WHY-CONFLICT', statement: 'Mission msn_0 landed changes to src/middleware/request.ts first.', confidence: 'High' }],
        configRecommendations: [{ id: 'rec_1', setting: 'landingMode', scope: 'Vessel', currentValue: 'LocalMerge', suggestedValue: 'PullRequest', rationale: 'A pull request lets you resolve the conflict.', applyAction: { tool: 'update_vessel', arguments: { landingMode: 'PullRequest' } } }],
        armadaImprovements: [{ area: 'Dispatch', hypothesis: 'Serialize missions that touch the same directory.', severity: 'Medium' }],
        recoveryPaths: [
          { id: 'rp2', rank: 2, title: 'Reclaim the dock and redispatch', dataLossRisk: true, risk: 'High', steps: [{ order: 1, description: 'Delete the dock', dataLossRisk: true, action: { tool: 'delete_dock', arguments: { dockId: 'dck_1' } } }] },
          { id: 'rp1', rank: 1, title: 'Retry the landing', risk: 'Low', expectedOutcome: 'The mission lands after a rebase.', steps: [{ order: 1, description: 'Retry landing', action: { tool: 'retry_landing', arguments: { missionId: 'msn_1' } } }] },
        ],
        triage: { verdict: 'UserConfig', confidence: 'High' },
      },
      attempts: [{ attempt: 1, entryId: 'bbx_1', kind: 'TestFail', reasonExcerpt: 'Tests failed' }, { attempt: 2, entryId: 'bbx_2', kind: 'LandingConflict' }],
      related: { fingerprint: 'fp_abcdef0123456789', occurrencesLast30Days: 4, similarEntries: [{ entryId: 'bbx_9', missionTitle: 'Older failure', resolution: 'Resolved' }] },
    },
    ...overrides,
  };
}

/** Summary rows of two attempts of msn_1. */
export function blackBoxAttemptsFixture(): BlackBoxEntrySummary[] {
  const current = blackBoxEntryFixture();
  return [
    { ...current, id: 'bbx_1', attempt: 1, isCurrent: false, step: 'StageCheck', reasonExcerpt: 'Tests failed' },
    { ...current, id: 'bbx_2', attempt: 2 },
  ].map(({ record: _record, ...summary }) => summary as BlackBoxEntrySummary);
}

/** An enumeration page. */
export function page<T>(objects: T[], overrides: Partial<{ pageNumber: number; pageSize: number; totalPages: number; totalRecords: number }> = {}) {
  return { success: true, pageNumber: 1, pageSize: 25, totalPages: 1, totalRecords: objects.length, totalMs: 1, objects, ...overrides };
}

/** The resilience section of an in-scope landing failure (the dock was deleted before landing). */
export function blackBoxResilienceFixture(overrides: Partial<BlackBoxResilience> = {}): BlackBoxResilience {
  return {
    inScope: true,
    scopeReason: 'phase Landing (step TargetIntegration) is Armada\'s handoff and landing',
    selfHealable: 'Yes',
    selfHealingProposal: 'recreated the worktree from armada/request-logging and retried the landing',
    detectionGapKind: 'Race',
    detectionGap: 'The dock was reclaimed while the mission still had to land.',
    invariant: 'A dock whose mission is WorkProduced stays on disk until the landing finishes.',
    codePointers: ['src/Armada.Server/MissionLandingHandler.cs:EnsureLandingDockAsync', 'src/Armada.Core/Services/DockService.cs:ReclaimAsync'],
    armadaVersion: '1.2.0',
    armadaCommit: 'abc9999',
    proposedChanges: [
      { rank: 2, title: 'Never reclaim a dock whose mission still has to land', change: 'Refuse to reclaim leased docks.', acceptanceTest: 'Services.DockLease: reclaim is refused.', severity: 'High', ruleId: 'BBX-LAND-002' },
      { rank: 1, title: 'Recreate a missing dock worktree from the branch before landing', change: 'Restore the worktree from the branch and continue.', acceptanceTest: 'Services.LandingPipeline: the dock is recreated and the mission lands.', severity: 'Critical', ruleId: 'BBX-LAND-002', missionBrief: 'Recreate a missing dock worktree from the branch before landing\n\nRestore the worktree.' },
    ],
    regressionTest: { suite: 'Services.LandingPipeline', description: 'Remove the dock directory before landing and assert it lands.' },
    severity: 'Critical',
    occurrences30Days: 5,
    armadaVersionCount: 2,
    attemptedRecoveries: ['LandingRetry x2 (Expired)'],
    ruleIds: ['BBX-LAND-002', 'BBX-PRIOR-ARMADA'],
    ...overrides,
  };
}
