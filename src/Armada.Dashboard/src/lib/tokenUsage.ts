/** Token usage ranges. Bucket counts per range: hour = 2/min (120), day = 4/hour (96), week = 12/day (84), month = 4/day (120). */
export const TOKEN_USAGE_TIME_RANGES = [
  { label: 'Last Hour', value: 'hour', hours: 1, stepMinutes: 0.5 },
  { label: 'Last Day', value: 'day', hours: 24, stepMinutes: 15 },
  { label: 'Last Week', value: 'week', hours: 168, stepMinutes: 120 },
  { label: 'Last Month', value: 'month', hours: 720, stepMinutes: 360 },
] as const;

export type TokenUsageRangeValue = typeof TOKEN_USAGE_TIME_RANGES[number]['value'];

/** Round axis ticks from 0 to at least `max`. */
export function computeYTicks(max: number): number[] {
  if (max <= 0) return [0];
  const rawStep = max / 4;
  const magnitude = Math.pow(10, Math.floor(Math.log10(rawStep)));
  const normalized = rawStep / magnitude;
  const niceNormalized = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
  const step = niceNormalized * magnitude;
  const ticks: number[] = [];
  for (let value = 0; value <= max; value += step) ticks.push(value);
  if (ticks[ticks.length - 1] < max) ticks.push(ticks[ticks.length - 1] + step);
  return ticks;
}

function trimZero(value: number): string {
  const fixed = value.toFixed(1);
  return fixed.endsWith('.0') ? fixed.slice(0, -2) : fixed;
}

/** Compact token count: 1.2K, 3M, 4.5B. */
export function formatTokens(value: number): string {
  const abs = Math.abs(value);
  if (abs >= 1e9) return trimZero(value / 1e9) + 'B';
  if (abs >= 1e6) return trimZero(value / 1e6) + 'M';
  if (abs >= 1e3) return trimZero(value / 1e3) + 'K';
  return String(Math.round(value));
}
