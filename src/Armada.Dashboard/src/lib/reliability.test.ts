import catalogSource from '../../../Armada.Server/wwwroot/i18n/armada.json?raw';
import attemptsSource from '../components/missions/MissionAttemptsCard.tsx?raw';
import submissionsSource from '../components/missions/MissionSubmissionsCard.tsx?raw';
import settingsSource from '../components/settings/ReliabilitySettings.tsx?raw';
import reliabilitySource from '../pages/Reliability.tsx?raw';
import batterySource from '../pages/BatteryRuns.tsx?raw';
import hubSource from '../pages/CaptainsHub.tsx?raw';
import libSource from './reliability.ts?raw';
import { attemptNextStep, dispositionClass, formatDurationMs, formatRate, submissionFindings, summarizeKinds, windowStartUtc } from './reliability';
import type { MissionAttempt, StageSubmission } from '../types/models';

interface I18nCatalog {
  defaultLocale: string;
  supportedLocales: { code: string }[];
  locales: Record<string, { terms?: Record<string, string>; phrases?: Record<string, string> }>;
}

const t = (key: string, values?: Record<string, string | number>) => key.replace(/\{\{(\w+)\}\}/g, (_m, k: string) => String(values?.[k] ?? ''));

describe('reliability helpers', () => {
  it('formats durations and rates', () => {
    expect(formatDurationMs(null)).toBe('-');
    expect(formatDurationMs(850)).toBe('850 ms');
    expect(formatDurationMs(42000)).toBe('42s');
    expect(formatDurationMs(190000)).toBe('3m 10s');
    expect(formatDurationMs(3900000)).toBe('1h 5m');
    expect(formatRate(0.875, 8)).toBe('88%');
    expect(formatRate(0, 0)).toBe('-');
  });

  it('summarizes kinds by frequency', () => {
    expect(summarizeKinds({ A: 1, B: 3, C: 0 })).toBe('B 3, A 1');
    expect(summarizeKinds({ A: 1, B: 2, C: 3, D: 4 }, 2)).toBe('D 4, C 3, +2');
    expect(summarizeKinds(undefined)).toBe('');
  });

  it('classifies dispositions and next steps', () => {
    expect(dispositionClass('Completed')).toBe('completed');
    expect(dispositionClass('Failed')).toBe('failed');
    expect(dispositionClass('Escalated')).toBe('warning');
    const base = { outcomeId: 'o', attempt: 1, reworkRound: 0, disposition: 'Relaunched', isRepair: false, escalated: false, isEvaluation: false } as MissionAttempt;
    expect(attemptNextStep({ ...base, escalated: true, nextCaptainId: 'cpt_2' }, t)).toBe('Escalated to cpt_2');
    expect(attemptNextStep({ ...base, isRepair: true }, t)).toBe('Repair rerun');
    expect(attemptNextStep({ ...base, contractRepairKind: 'None' }, t)).toBe('-');
  });

  it('orders findings blocking first', () => {
    const s = { submission: { kind: 'Result', result: { status: 'done', summary: '', findings: [{ severity: 'non_blocking', scenario: 'a' }, { severity: 'blocking', scenario: 'b' }] } } } as StageSubmission;
    expect(submissionFindings(s).map(f => f.scenario)).toEqual(['b', 'a']);
  });

  it('computes the window start', () => {
    expect(windowStartUtc(1, new Date('2026-11-26T00:00:00Z'))).toBe('2026-11-25T00:00:00.000Z');
  });
});

const S = "'((?:[^'\\\\]|\\\\.)*)'";
const PATTERNS = [new RegExp(`\\bt\\(\\s*${S}`, 'g'), new RegExp(`(?:label|tooltip): ${S}`, 'g')];
// API values shown as-is (statuses and dispositions go through t() but several are already catalog terms).
const UNTRANSLATED = new Set<string>([]);

function placeholders(text: string): string {
  return (text.match(/\{\{[\w.]+\}\}/g) ?? []).sort().join(',');
}

describe('Reliability i18n catalog', () => {
  const catalog = JSON.parse(catalogSource) as I18nCatalog;
  const keys = new Set<string>();
  for (const source of [attemptsSource, submissionsSource, settingsSource, reliabilitySource, batterySource, hubSource, libSource]) {
    for (const pattern of PATTERNS) {
      for (const m of source.matchAll(pattern)) {
        const key = m[1].replace(/\\'/g, "'");
        if (key.trim() && !UNTRANSLATED.has(key)) keys.add(key);
      }
    }
  }

  it('finds the strings', () => {
    expect(keys.has('Latest gate report')).toBe(true);
    expect(keys.has('Reliability and Routing')).toBe(true);
  });

  it('has a translation for every string in every non-English locale, with placeholders intact', () => {
    const problems: string[] = [];
    for (const meta of catalog.supportedLocales) {
      if (meta.code === catalog.defaultLocale) continue;
      const pack = catalog.locales[meta.code] ?? {};
      for (const key of keys) {
        const value = pack.phrases?.[key] ?? pack.terms?.[key];
        if (!value) problems.push(`${meta.code}: missing ${key}`);
        else if (placeholders(key) !== placeholders(value)) problems.push(`${meta.code}: placeholder ${key}`);
      }
    }
    expect(problems).toEqual([]);
  });
});
