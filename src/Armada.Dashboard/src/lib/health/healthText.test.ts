import { describe, expect, it } from 'vitest';
import detailCodesSource from '../../../../Armada.Core/Services/Health/VesselHealthDetailCodes.cs?raw';
import criterionEnumSource from '../../../../Armada.Core/Enums/VesselHealthCriterionEnum.cs?raw';
import statusEnumSource from '../../../../Armada.Core/Enums/VesselHealthStatusEnum.cs?raw';
import severityEnumSource from '../../../../Armada.Core/Enums/VulnerabilitySeverityEnum.cs?raw';
import driftEnumSource from '../../../../Armada.Core/Enums/DependencyDriftEnum.cs?raw';
import {
  CRITERION_LABELS,
  DETAIL_CODE_FORMATTERS,
  DRIFT_LABELS,
  SEVERITY_LABELS,
  STATUS_DESCRIPTIONS,
  STATUS_LABELS,
  describeFinding,
  type Translate,
} from './healthText';

/** Identity translator that interpolates params, like the runtime does for English. */
const t: Translate = (text, params) => {
  if (!params) return text;
  return Object.entries(params).reduce((acc, [k, v]) => acc.split(`{{${k}}}`).join(v == null ? '' : String(v)), text);
};

function constCodes(source: string): string[] {
  return Array.from(source.matchAll(/public const string (\w+)\s*=\s*"([^"]+)"/g)).map((m) => m[2]);
}

function enumMembers(source: string): string[] {
  const body = source.slice(source.indexOf('{', source.indexOf('enum ')) + 1);
  const withoutComments = body.replace(/\/\/\/.*$/gm, '').replace(/\[[^\]]*\]/g, '');
  return Array.from(withoutComments.matchAll(/^\s*([A-Z][A-Za-z0-9]*)\s*(?:=\s*\d+\s*)?,?\s*$/gm)).map((m) => m[1]);
}

describe('vessel health detail codes', () => {
  const backendCodes = constCodes(detailCodesSource);

  it('parses the backend code list', () => {
    expect(backendCodes.length).toBeGreaterThanOrEqual(40);
    expect(backendCodes).toContain('OutdatedPackages');
  });

  it('maps every backend detail code to a localized sentence', () => {
    const missing = backendCodes.filter((code) => !DETAIL_CODE_FORMATTERS[code]);
    expect(missing).toEqual([]);
  });

  it('has no mappings for codes the backend does not emit', () => {
    const extra = Object.keys(DETAIL_CODE_FORMATTERS).filter((code) => !backendCodes.includes(code));
    expect(extra).toEqual([]);
  });

  it('renders a non-empty sentence without leftover placeholders for every code', () => {
    for (const code of backendCodes) {
      for (const [a, b] of [[0, 0], [1, 1], [7, 2], [null, null]] as Array<[number | null, number | null]>) {
        const text = describeFinding({ detailCode: code, valueA: a, valueB: b }, t, 'en');
        expect(text.length, code).toBeGreaterThan(0);
        expect(text, code).not.toMatch(/\{\{/);
      }
    }
  });

  it('formats numbers with the locale and picks singular or plural', () => {
    expect(describeFinding({ detailCode: 'Behind', valueA: 0, valueB: 7 }, t, 'en')).toBe('Behind by 7 commits.');
    expect(describeFinding({ detailCode: 'Behind', valueA: 2, valueB: 7 }, t, 'en')).toBe('Behind by 7 commits (ahead 2).');
    expect(describeFinding({ detailCode: 'Ahead', valueA: 1, valueB: 0 }, t, 'en')).toBe('Ahead by 1 commit.');
    expect(describeFinding({ detailCode: 'OutdatedPackages', valueA: 1234, valueB: 5 }, t, 'en')).toBe('1,234 outdated packages (5 major).');
    expect(describeFinding({ detailCode: 'OutdatedPackages', valueA: 1234, valueB: 5 }, t, 'de')).toBe('1.234 outdated packages (5 major).');
    expect(describeFinding({ detailCode: 'VulnerablePackages', valueA: 3, valueB: 3 }, t, 'en')).toBe('3 vulnerable packages (highest severity High).');
    expect(describeFinding({ detailCode: 'Timeout', valueA: null, valueB: null }, t, 'en')).toBe('The package tool timed out.');
    expect(describeFinding({ detailCode: 'RecentFailures', valueA: 2, valueB: 7 }, t, 'en')).toBe('2 failed missions in the last 7 days.');
  });

  it('falls back to the raw code for an unknown code', () => {
    expect(describeFinding({ detailCode: 'SomethingNew', valueA: 1, valueB: 2 }, t, 'en')).toBe('SomethingNew');
  });
});

describe('vessel health label maps', () => {
  it('labels every backend criterion', () => {
    const members = enumMembers(criterionEnumSource);
    expect(members).toContain('GitDivergence');
    expect(members.filter((m) => !CRITERION_LABELS[m as keyof typeof CRITERION_LABELS])).toEqual([]);
  });

  it('labels and describes every backend status', () => {
    const members = enumMembers(statusEnumSource);
    expect(members).toEqual(['Pass', 'Warn', 'Fail', 'NotApplicable', 'Unknown']);
    for (const m of members) {
      expect(STATUS_LABELS[m as keyof typeof STATUS_LABELS]).toBeTruthy();
      expect(STATUS_DESCRIPTIONS[m as keyof typeof STATUS_DESCRIPTIONS]).toBeTruthy();
    }
  });

  it('labels every severity and drift value', () => {
    for (const m of enumMembers(severityEnumSource)) expect(SEVERITY_LABELS[m as keyof typeof SEVERITY_LABELS], m).toBeTruthy();
    for (const m of enumMembers(driftEnumSource)) expect(DRIFT_LABELS[m as keyof typeof DRIFT_LABELS], m).toBeTruthy();
  });
});
