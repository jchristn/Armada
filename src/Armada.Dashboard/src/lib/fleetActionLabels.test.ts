import { describe, expect, it } from 'vitest';
import { backendCodes } from '../test/backendCodes';
import {
  FAILURE_REASON_LABELS,
  RUN_STATUS_META,
  SKIP_REASON_LABELS,
  TARGET_STATUS_META,
  MISSION_MODES,
  MISSION_MODE_LABELS,
  TEMPLATE_VARIABLES,
  isReportOnly,
  reasonLabel,
  formatDurationMs,
} from './fleetActionLabels';
import { findUnknownTemplateVariables, renderTemplatePreview } from './fleetActionTemplate';
import { buildRunReportMarkdown, reportFileName } from './fleetActionReport';
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

  it('maps every fleet categorization request error code to an error label', () => {
    const codes = backendCodes.VesselImportCategorizationCodes;
    expect(codes).toContain('CategorizationCaptainRequired');
    const missing = codes.filter((code) => !IMPORT_ERROR_LABELS[code]);
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

describe('report-only missions and new skip reasons', () => {
  it('labels NotApplicable and every mission mode', () => {
    expect(reasonLabel(t, 'NotApplicable', null)).toBe('Not applicable');
    expect(MISSION_MODES.map((m) => MISSION_MODE_LABELS[m])).toEqual(['Implementation', 'Audit (read-only)', 'Research (read-only)']);
  });

  it('treats only Audit and Research Mission actions as report-only', () => {
    expect(isReportOnly({ kind: 'Mission', missionMode: 'Audit' })).toBe(true);
    expect(isReportOnly({ kind: 'Mission', missionMode: 'Research' })).toBe(true);
    expect(isReportOnly({ kind: 'Mission', missionMode: 'Implementation' })).toBe(false);
    expect(isReportOnly({ kind: 'Mission' })).toBe(false);
    expect(isReportOnly({ kind: 'Command', missionMode: 'Audit' })).toBe(false);
    expect(isReportOnly(null)).toBe(false);
  });
});

describe('template variables', () => {
  it('knows the outdated and license report commands', () => {
    expect(TEMPLATE_VARIABLES.map((v) => v.name)).toEqual(expect.arrayContaining(['vessel.outdatedCommand', 'vessel.licensesCommand']));
    expect(findUnknownTemplateVariables('{{vessel.outdatedCommand}} && {{ VESSEL.LICENSESCOMMAND }} {{vessel.nope}}')).toEqual(['vessel.nope']);
    for (const v of TEMPLATE_VARIABLES) expect(findUnknownTemplateVariables(`{{${v.name}}}`)).toEqual([]);
  });

  it('leaves a missing build command for the server to resolve instead of warning it will be skipped', () => {
    const vessel = { id: 'vsl_1', name: 'web', defaultBranch: 'main', workingDirectory: '/w' };
    expect(renderTemplatePreview('{{vessel.buildCommand}}', vessel, '')).toEqual({ text: '{{vessel.buildCommand}}', usesHealthSummary: false, missingBuildCommand: false });
    expect(renderTemplatePreview('{{vessel.buildCommand}}', { ...vessel, definitionOfDoneBuildCommand: 'make' }, '').text).toBe('make');
  });
});

describe('run report markdown', () => {
  it('writes a vessel and status heading per target with the report, or the reason when there is none', () => {
    const run = { id: 'far/1', actionName: 'Security review', kind: 'Mission' as const, missionMode: 'Audit' as const, targetCount: 2, createdUtc: '2026-11-01T00:00:00Z' };
    const text = buildRunReportMarkdown(t, run, [
      { id: 'a', vesselId: 'vsl_a', vesselName: 'api', status: 'Succeeded', skipReason: null, failureReason: null, outputText: '  All clear.  ', outputTruncated: true },
      { id: 'b', vesselId: 'vsl_b', vesselName: 'web', status: 'Skipped', skipReason: 'NotApplicable', failureReason: null, outputText: null, outputTruncated: false },
    ]);
    expect(text).toBe([
      '# Security review', '', '- Run: far/1', '- Mission mode: Audit (read-only)', '- Targets: 2', '- Created: 2026-11-01T00:00:00Z',
      '', '## api (Succeeded)', '', '_Only the end of this report was kept._', '', 'All clear.',
      '', '## web (Skipped)', '', '_Not applicable_', '',
    ].join('\n'));
    expect(reportFileName('far/1')).toBe('fleet-action-report-far_1.md');
  });
});
