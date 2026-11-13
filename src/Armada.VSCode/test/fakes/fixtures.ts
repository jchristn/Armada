import { ArmadaBlackBoxClient } from '../../src/api/blackbox';
import { BlackBoxEntry, BlackBoxEntrySummary, Vessel } from '../../src/api/types';
import { MockFetch } from './fetch';

export const BASE = 'http://armada.test:7890';

export const VESSEL_A: Vessel = { Id: 'vsl_a', Name: 'alpha', RepoUrl: 'https://github.com/org/alpha.git' };
export const VESSEL_B: Vessel = { Id: 'vsl_b', Name: 'bravo', RepoUrl: 'git@github.com:org/bravo.git' };

export function summary(overrides: Partial<BlackBoxEntrySummary> = {}): BlackBoxEntrySummary {
  return {
    Id: 'bbx_1',
    MissionId: 'msn_x',
    MissionTitle: 'Add login',
    VesselId: 'vsl_a',
    VesselName: 'alpha',
    Phase: 'Landing',
    Step: 'LocalMerge',
    FailureKind: 'LandingConflict',
    StatusBefore: 'WorkProduced',
    StatusAfter: 'LandingFailed',
    Resolution: 'Open',
    IsCurrent: true,
    Attempt: 1,
    CreatedUtc: '2026-10-28T10:00:00Z',
    ...overrides,
  };
}

export function fullEntry(overrides: Partial<BlackBoxEntry> = {}): BlackBoxEntry {
  return {
    ...summary(),
    Record: {
      Analysis: {
        Summary: 'The landing merge conflicted with msn_y.',
        Why: [{ RuleId: 'R-CONFLICT', Statement: 'msn_y changed the same files', Confidence: 'High' }],
        RecoveryPaths: [
          { Id: 'rp1', Title: 'Retry landing', Risk: 'Low', DataLossRisk: false, ExpectedOutcome: 'The mission lands' },
          { Id: 'rp2', Title: 'Reclaim dock and delete branch', Risk: 'High', DataLossRisk: true, ExpectedOutcome: 'Clean restart' },
        ],
      },
      Conflict: { IsConflict: true, ConflictedFiles: ['src/a.ts'], ConflictingMissions: [{ MissionId: 'msn_y', Title: 'Refactor' }] },
    },
    ...overrides,
  };
}

export function client(mock: MockFetch, extra: { tenant?: string; authHeader?: 'Bearer' | 'X-Api-Key'; timeoutMs?: number } = {}): ArmadaBlackBoxClient {
  return new ArmadaBlackBoxClient({ baseUrl: BASE, apiKey: 'secret-key', fetchImpl: mock.fetch, ...extra });
}
