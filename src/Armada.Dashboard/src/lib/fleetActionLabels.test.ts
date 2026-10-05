import { describe, expect, it } from 'vitest';
import { backendCodes } from '../test/backendCodes';
import {
  FAILURE_REASON_LABELS,
  RUN_STATUS_META,
  SKIP_REASON_LABELS,
  TARGET_STATUS_META,
  reasonLabel,
  formatDurationMs,
} from './fleetActionLabels';
import {
  BATCH_STATUS_META,
  CANDIDATE_STATUS_META,
  HINT_LABELS,
  IMPORT_ERROR_LABELS,
  OUTCOME_META,
  OUTCOME_REASON_LABELS,
} from './vesselImportLabels';
import { translateTemplate } from '../i18n/runtime';

/**
 * These tests read the backend's code constants and enum members from the generated fixture (kept equal to the C#
 * types by the .NET suite Models.DashboardCodeList), so a new server code without a localized label (or a renamed
 * one) fails here instead of rendering a raw code to operators.
 */

const t = (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params);

describe('fleet action reason code labels', () => {
  const codes = backendCodes.FleetActionReasonCodes;

  it('reads the backend reason codes', () => {
    expect(codes.length).toBeGreaterThanOrEqual(17);
    expect(codes).toContain('DirtyTree');
    expect(codes).toContain('VoyageMissing');
  });

  it('maps every backend skip or failure reason code to a label', () => {
    const missing = codes.filter((code) => !SKIP_REASON_LABELS[code] && !FAILURE_REASON_LABELS[code]);
    expect(missing).toEqual([]);
  });

  it('has no labels for codes the backend does not define', () => {
    const extra = [...Object.keys(SKIP_REASON_LABELS), ...Object.keys(FAILURE_REASON_LABELS)].filter((code) => !codes.includes(code));
    expect(extra).toEqual([]);
  });

  it('renders a human label instead of the raw code', () => {
    expect(reasonLabel(t, 'DirtyTree', null)).toBe('Working tree has uncommitted changes');
    expect(reasonLabel(t, null, 'NonZeroExit')).toBe('Command exited with a non-zero code');
    expect(reasonLabel(t, null, 'SomethingNew')).toBe('SomethingNew');
    expect(reasonLabel(t, null, null)).toBe('');
  });
});

describe('fleet action status metadata', () => {
  it('covers every run status', () => {
    expect(Object.keys(RUN_STATUS_META).sort()).toEqual([...backendCodes.FleetActionRunStatusEnum].sort());
  });

  it('covers every target status', () => {
    expect(Object.keys(TARGET_STATUS_META).sort()).toEqual([...backendCodes.FleetActionTargetStatusEnum].sort());
  });

  it('gives every status an icon and a label', () => {
    for (const meta of [...Object.values(RUN_STATUS_META), ...Object.values(TARGET_STATUS_META)]) {
      expect(meta.icon).toBeTruthy();
      expect(meta.label).toBeTruthy();
    }
  });
});

describe('vessel import code labels', () => {
  it('maps every backend import code to a hint, outcome reason, or error label', () => {
    const codes = backendCodes.VesselImportCodes;
    expect(codes.length).toBeGreaterThanOrEqual(14);
    const missing = codes.filter((code) => !HINT_LABELS[code] && !OUTCOME_REASON_LABELS[code] && !IMPORT_ERROR_LABELS[code]);
    expect(missing).toEqual([]);
  });

  it('covers every candidate status, outcome, and batch status', () => {
    expect(Object.keys(CANDIDATE_STATUS_META).sort()).toEqual([...backendCodes.VesselImportCandidateStatusEnum].sort());
    expect(Object.keys(OUTCOME_META).sort()).toEqual([...backendCodes.VesselImportOutcomeEnum].sort());
    expect(Object.keys(BATCH_STATUS_META).sort()).toEqual([...backendCodes.VesselImportBatchStatusEnum].sort());
  });
});

describe('formatDurationMs', () => {
  it('formats durations compactly with locale numbers', () => {
    expect(formatDurationMs(t, 'en', 850)).toBe('850 ms');
    expect(formatDurationMs(t, 'en', 4200)).toBe('4.2 s');
    expect(formatDurationMs(t, 'en', 185000)).toBe('3 min 5 s');
    expect(formatDurationMs(t, 'en', null)).toBe('-');
  });
});
