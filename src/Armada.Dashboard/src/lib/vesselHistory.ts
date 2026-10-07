/**
 * Date and grouping helpers for the vessel history view (heatmap + commit timeline).
 * Calendar days are plain `yyyy-MM-dd` strings; arithmetic on them is done in UTC so the
 * browser time zone never shifts a day. Instants (commit dates, `before`) use the browser's
 * local time zone, which is also the offset the heatmap is bucketed in.
 */
import type { VesselCommit, VesselCommitActivityDay } from '../types/models';

/** Number of heatmap intensity levels (0 = no commits). */
export const HEAT_LEVELS = 5;

/** Days in the default "last year" range (to - 364 days .. to), matching the server default. */
export const DEFAULT_RANGE_DAYS = 365;

function pad(value: number): string {
  return value < 10 ? `0${value}` : String(value);
}

/** yyyy-MM-dd of a Date in the browser's local time zone. */
export function toLocalIsoDate(date: Date): string {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/** Today in the browser's local time zone, yyyy-MM-dd. */
export function todayIsoDate(now: Date = new Date()): string {
  return toLocalIsoDate(now);
}

/** Parse yyyy-MM-dd to its parts, or null when malformed. */
export function parseIsoDate(value: string): { year: number; month: number; day: number } | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (!match) return null;
  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  const check = new Date(Date.UTC(year, month - 1, day));
  if (check.getUTCFullYear() !== year || check.getUTCMonth() !== month - 1 || check.getUTCDate() !== day) return null;
  return { year, month, day };
}

function utcDate(value: string): Date {
  const parts = parseIsoDate(value);
  if (!parts) throw new Error(`Invalid date: ${value}`);
  return new Date(Date.UTC(parts.year, parts.month - 1, parts.day));
}

function utcToIso(date: Date): string {
  return `${date.getUTCFullYear()}-${pad(date.getUTCMonth() + 1)}-${pad(date.getUTCDate())}`;
}

/** Add whole days to a yyyy-MM-dd date. */
export function addDays(value: string, days: number): string {
  const date = utcDate(value);
  date.setUTCDate(date.getUTCDate() + days);
  return utcToIso(date);
}

/** Format a yyyy-MM-dd calendar day for display without any time zone shift. */
export function formatIsoDay(locale: string, value: string, options: Intl.DateTimeFormatOptions): string {
  const parts = parseIsoDate(value);
  if (!parts) return value;
  return new Date(Date.UTC(parts.year, parts.month - 1, parts.day)).toLocaleDateString(locale, { ...options, timeZone: 'UTC' });
}

/** Day of week for a yyyy-MM-dd date, 0 = Sunday. */
export function dayOfWeek(value: string): number {
  return utcDate(value).getUTCDay();
}

/**
 * The `before` bound that lists commits made on `value` (local day) and earlier: the start of
 * the following local day, as an ISO 8601 instant.
 */
export function beforeForDay(value: string): string {
  const parts = parseIsoDate(value);
  if (!parts) throw new Error(`Invalid date: ${value}`);
  return new Date(parts.year, parts.month - 1, parts.day + 1, 0, 0, 0, 0).toISOString();
}

/** The browser's current UTC offset in minutes (east positive), as the activity route expects. */
export function browserUtcOffsetMinutes(now: Date = new Date()): number {
  const offset = -now.getTimezoneOffset();
  return offset === 0 ? 0 : offset;
}

/** Heatmap level 0..4 for a day's count, scaled by the largest day in the range. */
export function heatLevel(count: number, maxDayCount: number): number {
  if (count <= 0 || maxDayCount <= 0) return 0;
  const level = Math.ceil((count / maxDayCount) * (HEAT_LEVELS - 1));
  return Math.min(HEAT_LEVELS - 1, Math.max(1, level));
}

/** A heatmap column: seven slots Sunday..Saturday, null where the slot is outside the range. */
export type HeatWeek = Array<VesselCommitActivityDay | null>;

/** Lay days out as week columns starting on Sunday, padding the first and last week with nulls. */
export function buildWeeks(days: VesselCommitActivityDay[]): HeatWeek[] {
  const weeks: HeatWeek[] = [];
  if (days.length === 0) return weeks;
  let current: HeatWeek = new Array<VesselCommitActivityDay | null>(dayOfWeek(days[0].date)).fill(null);
  for (const day of days) {
    current.push(day);
    if (current.length === 7) {
      weeks.push(current);
      current = [];
    }
  }
  if (current.length > 0) {
    while (current.length < 7) current.push(null);
    weeks.push(current);
  }
  return weeks;
}

/** A month label anchored to a week column. */
export interface MonthLabel {
  column: number;
  /** 1-based month. */
  month: number;
  year: number;
}

/**
 * Month labels for the week columns: one at the column holding each month's first day, plus the
 * first column when it starts mid-month and the next label is at least three columns away.
 */
export function buildMonthLabels(weeks: HeatWeek[]): MonthLabel[] {
  const labels: MonthLabel[] = [];
  weeks.forEach((week, column) => {
    const first = week.find((d) => d !== null && parseIsoDate(d.date)?.day === 1);
    if (first) {
      const parts = parseIsoDate(first.date)!;
      labels.push({ column, month: parts.month, year: parts.year });
    }
  });
  const firstDay = weeks[0]?.find((d) => d !== null);
  if (firstDay && (labels.length === 0 || labels[0].column >= 3)) {
    const parts = parseIsoDate(firstDay.date)!;
    if (labels.length === 0 || labels[0].column !== 0) labels.unshift({ column: 0, month: parts.month, year: parts.year });
  }
  return labels;
}

/** Commits on one local day. */
export interface CommitDayGroup {
  /** yyyy-MM-dd in the browser's local time zone. */
  date: string;
  commits: VesselCommit[];
}

/** Group commits (already newest first) by local commit day, preserving order. */
export function groupCommitsByDay(commits: VesselCommit[]): CommitDayGroup[] {
  const groups: CommitDayGroup[] = [];
  for (const commit of commits) {
    const date = toLocalIsoDate(new Date(commit.committedUtc));
    const last = groups[groups.length - 1];
    if (last && last.date === date) last.commits.push(commit);
    else groups.push({ date, commits: [commit] });
  }
  return groups;
}

/** Append a page to a list, dropping commits already present (pages can overlap if history moves). */
export function appendCommits(existing: VesselCommit[], page: VesselCommit[]): VesselCommit[] {
  const seen = new Set(existing.map((c) => c.sha));
  const added = page.filter((c) => !seen.has(c.sha));
  return added.length === 0 ? existing : existing.concat(added);
}

/** The heatmap range a year selection maps to. */
export interface HeatRange {
  from: string;
  to: string;
}

/**
 * Range for a year selection: null = the last year ending today (365 days, the server default);
 * a calendar year = Jan 1..Dec 31, clipped to today.
 */
export function rangeForYear(year: number | null, today: string): HeatRange {
  if (year === null) return { from: addDays(today, -(DEFAULT_RANGE_DAYS - 1)), to: today };
  const end = `${year}-12-31`;
  return { from: `${year}-01-01`, to: end > today ? today : end };
}

/** Selectable years, newest first, from today's year back to the first commit's year (local). */
export function selectableYears(firstCommitUtc: string | null | undefined, today: string): number[] {
  const thisYear = Number(today.slice(0, 4));
  let firstYear = thisYear;
  if (firstCommitUtc) {
    const first = new Date(firstCommitUtc);
    if (!Number.isNaN(first.getTime())) firstYear = Math.min(thisYear, first.getFullYear());
  }
  const years: number[] = [];
  for (let y = thisYear; y >= firstYear; y -= 1) years.push(y);
  return years;
}
