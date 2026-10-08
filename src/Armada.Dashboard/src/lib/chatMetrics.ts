import type { AskMessage, CaptainChatMetrics } from '../types/models';
import { toolCallsToEvents } from './askFormat';
import type { ToolEvent } from './toolEvents';

// Per-turn statistics of a captain reply ("Turn statistics"), shared by the dashboard's ChatMetricsInfo / ChatMetricsBar
// and the mobile app, so both show the same rows, labels, and number formats.

/** Translator shape used by the shared libs (the dashboard's and the app's `t`). */
export type MetricsTranslate = (text: string, params?: Record<string, string | number | null | undefined>) => string;

/** One row of the statistics: a label (already translated) and its formatted value ('-' when unknown). */
export interface TurnStatistic {
  /** Stable key for lists and tests. */
  key: 'timeToFirstToken' | 'streaming' | 'tokensPerSecond' | 'tokens' | 'total' | 'toolCalls' | 'toolTime';
  label: string;
  value: string;
}

/** Milliseconds as the statistics show them: '-' when unknown, whole ms below a second, then seconds with two decimals. */
export function formatMetricMs(ms: number | null | undefined): string {
  if (ms == null) return '-';
  if (ms >= 1000) return (ms / 1000).toFixed(2) + 's';
  return Math.round(ms) + 'ms';
}

/**
 * The tool rows of a turn: how many calls completed and the total time spent in them (summed across every completed
 * call). Empty when no tool finished this turn, so the rows only appear when tools ran.
 */
export function toolStatistics(t: MetricsTranslate, tools: ToolEvent[] | null | undefined): TurnStatistic[] {
  const completed = (tools ?? []).filter((tool) => tool.status !== 'running');
  if (completed.length === 0) return [];
  const toolMs = completed.reduce((sum, tool) => sum + (tool.elapsedMs ?? 0), 0);
  return [
    { key: 'toolCalls', label: t('tool calls'), value: String(completed.length) },
    { key: 'toolTime', label: t('tool time'), value: formatMetricMs(toolMs) },
  ];
}

/**
 * Statistics of a captain chat turn with runtime metrics (Planning, captain chat): time to first token, streaming
 * time, tokens per second, token count, total time, then the tool rows. With `tokens: 'completion'` the token count is
 * the completion tokens only (the compact strip); by default it falls back to the runtime's total estimate.
 */
export function chatTurnStatistics(
  t: MetricsTranslate,
  metrics: CaptainChatMetrics,
  tools?: ToolEvent[] | null,
  tokens: 'completion' | 'completionOrTotal' = 'completionOrTotal',
): TurnStatistic[] {
  const tokenCount = tokens === 'completion' ? metrics.completionTokens : (metrics.completionTokens ?? metrics.totalTokens);
  return [
    { key: 'timeToFirstToken', label: t('time to first token'), value: formatMetricMs(metrics.timeToFirstTokenMs) },
    { key: 'streaming', label: t('streaming'), value: formatMetricMs(metrics.streamingMs) },
    { key: 'tokensPerSecond', label: t('tokens/sec'), value: metrics.tokensPerSecond != null ? metrics.tokensPerSecond.toFixed(1) : '-' },
    { key: 'tokens', label: t('tokens'), value: tokenCount != null ? String(tokenCount) : '-' },
    { key: 'total', label: t('total'), value: formatMetricMs(metrics.totalMs) },
    ...toolStatistics(t, tools),
  ];
}

/**
 * Statistics of an Ask Armada reply. The Admiral persists only the turn's total time on an Ask message
 * (`durationMs`, the runtime's total) and each tool call's own time (`toolCalls[].elapsedMs`); the dashboard shows
 * them as the reply's "Turn duration" and on the tool chips. These rows are the same numbers: total, tool calls, and
 * tool time. Empty when the reply has neither.
 */
export function askTurnStatistics(t: MetricsTranslate, message: Pick<AskMessage, 'durationMs' | 'toolCalls'>): TurnStatistic[] {
  const rows: TurnStatistic[] = [];
  if (message.durationMs != null) rows.push({ key: 'total', label: t('total'), value: formatMetricMs(message.durationMs) });
  return [...rows, ...toolStatistics(t, toolCallsToEvents(message.toolCalls))];
}
