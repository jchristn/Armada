import { describe, expect, it } from 'vitest';
import {
  addDays,
  appendCommits,
  beforeForDay,
  buildMonthLabels,
  buildWeeks,
  dayOfWeek,
  groupCommitsByDay,
  heatLevel,
  parseIsoDate,
  rangeForYear,
  selectableYears,
  toLocalIsoDate,
} from './vesselHistory';
import type { VesselCommit, VesselCommitActivityDay } from '../types/models';

function days(from: string, count: number): VesselCommitActivityDay[] {
  return Array.from({ length: count }, (_, i) => ({ date: addDays(from, i), count: i }));
}

function commit(sha: string, committedUtc: string): VesselCommit {
  return {
    sha, shortSha: sha.slice(0, 7), subject: sha, body: '', authorName: 'a', authorEmail: '', authoredUtc: committedUtc,
    committerName: 'a', committerEmail: '', committedUtc, parentShas: [], isMerge: false, filesChanged: 0,
    addedLines: 0, deletedLines: 0, files: [], filesTruncated: false,
  };
}

describe('vessel history date helpers', () => {
  it('does calendar arithmetic without time zone drift', () => {
    expect(addDays('2024-02-28', 1)).toBe('2024-02-29');
    expect(addDays('2024-03-01', -1)).toBe('2024-02-29');
    expect(addDays('2026-12-31', 1)).toBe('2027-01-01');
    expect(dayOfWeek('2026-10-04')).toBe(0); // Sunday
    expect(dayOfWeek('2026-10-10')).toBe(6); // Saturday
    expect(parseIsoDate('2026-02-30')).toBeNull();
    expect(parseIsoDate('nope')).toBeNull();
  });

  it('before for a day is the start of the following local day', () => {
    expect(beforeForDay('2026-10-03')).toBe(new Date(2026, 9, 4, 0, 0, 0, 0).toISOString());
    expect(beforeForDay('2026-12-31')).toBe(new Date(2027, 0, 1, 0, 0, 0, 0).toISOString());
    // The bound is exclusive: the last instant of the day is before it, the next day's start is not.
    const bound = new Date(beforeForDay('2026-10-03')).getTime();
    expect(new Date(2026, 9, 3, 23, 59, 59).getTime()).toBeLessThan(bound);
    expect(toLocalIsoDate(new Date(bound))).toBe('2026-10-04');
  });

  it('scales levels by the busiest day', () => {
    expect(heatLevel(0, 10)).toBe(0);
    expect(heatLevel(1, 10)).toBe(1);
    expect(heatLevel(5, 10)).toBe(2);
    expect(heatLevel(8, 10)).toBe(4);
    expect(heatLevel(10, 10)).toBe(4);
    expect(heatLevel(1, 1)).toBe(4);
    expect(heatLevel(3, 0)).toBe(0);
  });

  it('lays days out as Sunday-first week columns', () => {
    // 2026-10-01 is a Thursday: four leading blanks.
    const weeks = buildWeeks(days('2026-10-01', 9));
    expect(weeks).toHaveLength(2);
    expect(weeks[0].slice(0, 4)).toEqual([null, null, null, null]);
    expect(weeks[0][4]?.date).toBe('2026-10-01');
    expect(weeks[1][0]?.date).toBe('2026-10-04');
    expect(weeks[1][6]).toBeNull();
    expect(weeks.every((w) => w.length === 7)).toBe(true);
  });

  it('labels the columns holding each month start', () => {
    const weeks = buildWeeks(days('2026-01-04', 70)); // Sunday Jan 4 .. Mar 14
    const labels = buildMonthLabels(weeks);
    expect(labels.map((l) => l.month)).toEqual([1, 2, 3]);
    expect(labels[0].column).toBe(0);
    expect(labels[1].column).toBe(4); // Feb 1 (a Sunday) starts the fifth week
    // A first column starting late in a month is not labeled when the next label is too close.
    const crowded = buildMonthLabels(buildWeeks(days('2026-01-25', 20)));
    expect(crowded.map((l) => l.month)).toEqual([2]);
  });

  it('groups commits by local day in order', () => {
    const a = commit('a', new Date(2026, 9, 3, 15).toISOString());
    const b = commit('b', new Date(2026, 9, 3, 9).toISOString());
    const c = commit('c', new Date(2026, 9, 1, 23).toISOString());
    const groups = groupCommitsByDay([a, b, c]);
    expect(groups.map((g) => g.date)).toEqual(['2026-10-03', '2026-10-01']);
    expect(groups[0].commits.map((x) => x.sha)).toEqual(['a', 'b']);
  });

  it('appends pages without duplicating commits', () => {
    const first = [commit('a', '2026-10-03T00:00:00Z'), commit('b', '2026-10-02T00:00:00Z')];
    const merged = appendCommits(first, [commit('b', '2026-10-02T00:00:00Z'), commit('c', '2026-10-01T00:00:00Z')]);
    expect(merged.map((x) => x.sha)).toEqual(['a', 'b', 'c']);
  });

  it('maps year selections to ranges bounded by today and the first commit', () => {
    expect(rangeForYear(null, '2026-10-06')).toEqual({ from: '2025-10-07', to: '2026-10-06' });
    expect(rangeForYear(2026, '2026-10-06')).toEqual({ from: '2026-01-01', to: '2026-10-06' });
    expect(rangeForYear(2024, '2026-10-06')).toEqual({ from: '2024-01-01', to: '2024-12-31' });
    expect(selectableYears('2023-06-15T12:00:00Z', '2026-10-06')).toEqual([2026, 2025, 2024, 2023]);
    expect(selectableYears(null, '2026-10-06')).toEqual([2026]);
  });
});
