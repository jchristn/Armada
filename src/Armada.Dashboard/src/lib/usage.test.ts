import { captainHeadline, chunkIds, highestApplicableWindow, usageLevel, viewKind } from './usage';
import { buildUsageCapturePayload, usageCaptureFormError, usageCaptureFormFromCaptain } from './usageCaptureForm';
import { buildUsageSettingsGroup, usageSettingsErrors, usageSettingsFormFrom } from './usageSettingsForm';
import type { UsageSnapshotView, UsageWindow } from '../types/models';

function w(name: string, usedPercent: number | null, resetsUtc: string | null = null): UsageWindow {
  return { name, usedPercent, used: null, limit: null, unit: 'percent', windowMinutes: null, resetsUtc, model: null, scope: null, coverageNote: null };
}
function v(windows: UsageWindow[], status: UsageSnapshotView['status'] = 'Ok', stale = false): UsageSnapshotView {
  return { id: null, tenantId: null, hostId: 'h', harborId: null, runtime: 'Codex', provider: 'openai', accountKey: 'a', captainIds: ['c'], capturedUtc: '', status, errorCode: null, windows, stale, observedUtc: null, requestedIntervalSeconds: 300, effectiveIntervalSeconds: 300, capabilityReason: null };
}

describe('usage thresholds and window selection', () => {
  it('uses 75 and 90 as the warning and danger boundaries', () => {
    expect(usageLevel(0)).toBe('normal');
    expect(usageLevel(74.9)).toBe('normal');
    expect(usageLevel(75)).toBe('warning');
    expect(usageLevel(89.99)).toBe('warning');
    expect(usageLevel(90)).toBe('danger');
    expect(usageLevel(100)).toBe('danger');
  });

  it('picks the highest window whose reset has not passed and ignores unknown percentages', () => {
    const now = Date.parse('2026-11-01T12:00:00Z');
    const view = v([w('expired', 99, '2026-11-01T11:59:00Z'), w('unknown', null), w('weekly', 60, '2026-11-05T00:00:00Z'), w('five_hour', 80, '2026-11-01T14:00:00Z')]);
    expect(highestApplicableWindow(view, now)?.name).toBe('five_hour');
    expect(highestApplicableWindow(v([w('unknown', null)]), now)).toBeNull();
  });

  it('prefers fresh observations over stale or failed ones for the headline', () => {
    const now = Date.parse('2026-11-01T12:00:00Z');
    const headline = captainHeadline([v([w('old', 95)], 'Failed', true), v([w('fresh', 30)])], now);
    expect(headline?.window?.name).toBe('fresh');
  });

  it('maps statuses to distinct kinds', () => {
    expect(viewKind(v([], 'Ok'))).toBe('ok');
    expect(viewKind(v([], 'Ok', true))).toBe('stale');
    expect(viewKind(v([], 'Estimated'))).toBe('estimated');
    expect(viewKind(v([], 'AuthMissing'))).toBe('authMissing');
    expect(viewKind(v([], 'Unsupported'))).toBe('unsupported');
    expect(viewKind(v([], 'Failed'))).toBe('failed');
  });

  it('splits more than 100 distinct ids into bounded batches', () => {
    const ids = Array.from({ length: 150 }, (_, i) => `cpt_${i}`);
    const chunks = chunkIds([...ids, 'cpt_0', ''], 100);
    expect(chunks.map((c) => c.length)).toEqual([100, 50]);
  });
});

describe('captain capture form', () => {
  it('a minimally deserialized captain preserves the inherited enabled/Auto/300 defaults', () => {
    expect(usageCaptureFormFromCaptain({})).toEqual({ enabled: null, mode: 'Auto', interval: '300', executable: '', argumentsText: '' });
  });

  it('round-trips a fully populated Custom captain', () => {
    const form = usageCaptureFormFromCaptain({ usageCaptureEnabled: true, usageCaptureMode: 'Custom', usageCaptureIntervalSeconds: 120, usageCaptureCommand: '{"executable":"/bin/q","arguments":["-a","b c"]}' });
    expect(form).toEqual({ enabled: true, mode: 'Custom', interval: '120', executable: '/bin/q', argumentsText: '-a\nb c' });
    expect(buildUsageCapturePayload(form).usageCaptureCommand).toBe('{"executable":"/bin/q","arguments":["-a","b c"]}');
    expect(buildUsageCapturePayload({ ...form, mode: 'Auto' }).usageCaptureCommand).toBeNull();
  });

  it('rejects out-of-range intervals and shell syntax', () => {
    const base = usageCaptureFormFromCaptain({});
    expect(usageCaptureFormError({ ...base, interval: '59' })).toBe('intervalInvalid');
    expect(usageCaptureFormError({ ...base, interval: '86401' })).toBe('intervalInvalid');
    expect(usageCaptureFormError({ ...base, interval: '1.5' })).toBe('intervalInvalid');
    expect(usageCaptureFormError({ ...base, interval: '60' })).toBeNull();
    expect(usageCaptureFormError({ ...base, mode: 'Custom' })).toBe('executableRequired');
    expect(usageCaptureFormError({ ...base, mode: 'Custom', executable: '$(whoami)' })).toBe('executableShellSyntax');
  });
});

describe('server usage settings form', () => {
  it('missing or empty groups yield the disabled-by-default server values', () => {
    for (const group of [null, undefined, {}]) {
      const form = usageSettingsFormFrom(group);
      expect(form.enabled).toBe(null);
      expect(form.allowPtyFallback).toBe(null);
      expect(form.values.minimumIntervalSeconds).toBe('60');
      expect(usageSettingsErrors(form)).toEqual([]);
    }
  });

  it('keeps untouched retention and excerpt values when building the replacement group', () => {
    const loaded = { rawSnapshotRetentionDays: 30, rollupRetentionDays: 365, maximumExcerptBytes: 1024 };
    const group = buildUsageSettingsGroup({ ...usageSettingsFormFrom(loaded), enabled: true }, loaded);
    expect(group).toMatchObject({ enabled: true, rawSnapshotRetentionDays: 30, rollupRetentionDays: 365, maximumExcerptBytes: 1024, minimumIntervalSeconds: 60 });
  });

  it('flags each out-of-range field', () => {
    const form = usageSettingsFormFrom(null);
    expect(usageSettingsErrors({ ...form, values: { ...form.values, maximumConcurrentProbes: '0', maximumBackoffSeconds: '59' } })).toEqual(['maximumConcurrentProbes', 'maximumBackoffSeconds']);
  });
});
