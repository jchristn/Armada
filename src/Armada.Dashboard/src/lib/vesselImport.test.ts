import { describe, expect, it } from 'vitest';
import type { Captain, VesselImportFleetRecommendation } from '../types/models';
import {
  EMPTY_CATEGORIZATION,
  IMPORT_LANDING_MODES,
  buildApplyPayload,
  draftTotals,
  isCaptainAvailable,
  isUncategorized,
  moveVessel,
  newFleetDraft,
  parseMaxDepth,
  parsePastedPaths,
  toDrafts,
  validateCategorization,
  validateDrafts,
} from './vesselImport';

function rec(over: Partial<VesselImportFleetRecommendation>): VesselImportFleetRecommendation {
  return { id: 'rec_1', tenantId: null, batchId: 'vib_1', name: 'Web', description: null, rationale: null, sortOrder: 0, appliedFleetId: null, vesselIds: [], createdUtc: '', lastUpdateUtc: '', ...over };
}

describe('vesselImport', () => {
  it('parses pasted paths (trim, quotes, duplicates, CRLF)', () => {
    expect(parsePastedPaths(' /a \r\n"/b"\n\n/a\n\'/c\'')).toEqual(['/a', '/b', '/c']);
  });

  it('validates max depth 1-16', () => {
    expect(parseMaxDepth('')).toEqual({ value: null, invalid: false });
    expect(parseMaxDepth('4')).toEqual({ value: 4, invalid: false });
    expect(parseMaxDepth('0').invalid).toBe(true);
    expect(parseMaxDepth('17').invalid).toBe(true);
    expect(parseMaxDepth('2.5').invalid).toBe(true);
  });

  it('offers every landing mode plus the global default', () => {
    expect(IMPORT_LANDING_MODES.map((m) => m.value)).toEqual(['', 'LocalMerge', 'MergeAndPush', 'PullRequest', 'MergeQueue', 'None']);
  });

  it('validates categorization only when enabled', () => {
    expect(validateCategorization(EMPTY_CATEGORIZATION)).toEqual({});
    expect(validateCategorization({ ...EMPTY_CATEGORIZATION, enabled: true }).captain).toBeDefined();
    expect(validateCategorization({ ...EMPTY_CATEGORIZATION, enabled: true, captainId: 'cpt_1', prompt: 'x'.repeat(32769) }).prompt).toBeDefined();
    expect(isCaptainAvailable({ state: 'Idle' } as Captain)).toBe(true);
    expect(isCaptainAvailable({ state: 'Working' } as Captain)).toBe(false);
  });

  it('edits fleet drafts and builds the apply payload', () => {
    const drafts = toDrafts([rec({ id: 'r1', name: 'Web', vesselIds: ['v1', 'v2'] }), rec({ id: 'r2', name: 'Uncategorized', vesselIds: ['v3'] })]);
    const moved = moveVessel(drafts, 'v3', 'r1');
    expect(moved[0].vesselIds).toEqual(['v1', 'v2', 'v3']);
    expect(moved[1].vesselIds).toEqual([]);
    expect(isUncategorized(drafts[1])).toBe(true);
    expect(draftTotals(drafts)).toEqual({ assignedCount: 2, fleetCount: 1 });
    const added = { ...newFleetDraft(), name: ' web ', vesselIds: ['v9'] };
    expect(validateDrafts([...drafts, added])[added.key]).toBe('Another fleet already uses this name.');
    expect(validateDrafts([{ ...added, name: '' }])[added.key]).toBe('Give this fleet a name.');
    expect(buildApplyPayload([...moved, { ...newFleetDraft(), name: 'Empty' }])).toEqual({
      Fleets: [{ Name: 'Web', Description: null, VesselIds: ['v1', 'v2', 'v3'] }],
    });
  });
});
