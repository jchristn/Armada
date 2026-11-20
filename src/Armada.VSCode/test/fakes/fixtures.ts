/*
 * Test data. Builders take and produce the server's PascalCase shape (serverSummary, serverEntry) or the client shape
 * the shared client hands the extension (summary, fullEntry: the same data through the shared client's camelizeKeys).
 */
import { camelizeKeys } from '@dashboard/api/client';
import type { BlackBoxEntry, BlackBoxEntrySummary } from '@dashboard/types/blackbox';
import type { Vessel } from '@dashboard/types/models';

export { BASE } from './fetch';

type Server = Record<string, unknown>;

export const SERVER_VESSEL_A: Server = { Id: 'vsl_a', Name: 'alpha', RepoUrl: 'https://github.com/org/alpha.git' };
export const SERVER_VESSEL_B: Server = { Id: 'vsl_b', Name: 'bravo', RepoUrl: 'git@github.com:org/bravo.git' };
export const VESSEL_A = camelizeKeys(SERVER_VESSEL_A) as Vessel;
export const VESSEL_B = camelizeKeys(SERVER_VESSEL_B) as Vessel;

/** A vessel in the client shape, from PascalCase fields. */
export function vessel(fields: Server): Vessel {
  return camelizeKeys(fields) as Vessel;
}

export function serverSummary(overrides: Server = {}): Server {
  return {
    Id: 'bbx_1',
    SubjectType: 'Mission',
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

export function serverEntry(overrides: Server = {}): Server {
  return {
    ...serverSummary(),
    Record: {
      EntryId: 'bbx_1',
      SchemaVersion: 1,
      Analysis: {
        Summary: 'The landing merge conflicted with msn_y.',
        Why: [{ RuleId: 'R-CONFLICT', Statement: 'msn_y changed the same files', Confidence: 'High' }],
        RecoveryPaths: [
          { Id: 'rp1', Rank: 1, Title: 'Retry landing', Risk: 'Low', DataLossRisk: false, ExpectedOutcome: 'The mission lands' },
          { Id: 'rp2', Rank: 2, Title: 'Reclaim dock and delete branch', Risk: 'High', DataLossRisk: true, ExpectedOutcome: 'Clean restart' },
        ],
      },
      Conflict: { IsConflict: true, ConflictedFiles: ['src/a.ts'], ConflictingMissions: [{ MissionId: 'msn_y', Title: 'Refactor' }] },
    },
    ...overrides,
  };
}

/** A summary in the client shape (camelCase), from PascalCase overrides. */
export function summary(overrides: Server = {}): BlackBoxEntrySummary {
  return camelizeKeys(serverSummary(overrides)) as BlackBoxEntrySummary;
}

/** A full entry in the client shape (camelCase), from PascalCase overrides. */
export function fullEntry(overrides: Server = {}): BlackBoxEntry {
  return camelizeKeys(serverEntry(overrides), ['arguments']) as BlackBoxEntry;
}

/** Any server-shaped (PascalCase) object as the shared client hands it to the extension. */
export function fromServer<T>(server: Record<string, unknown>): T {
  return camelizeKeys(server, ['arguments']) as T;
}
