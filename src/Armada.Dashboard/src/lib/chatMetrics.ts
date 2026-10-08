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
  key:
    | 'timeToFirstToken'
    | 'timeToFirstText'
    | 'streaming'
    | 'tokensPerSecond'
    | 'tokens'
    | 'inputTokens'
    | 'cachedTokens'
    | 'cost'
    | 'total'
    | 'toolCalls'
    | 'toolTime';
  label: string;
  value: string;
}

/** Milliseconds as the statistics show them: '-' when unknown, whole ms below a second, then seconds with two decimals. */
export function formatMetricMs(ms: number | null | undefined): string {
  if (ms == null) return '-';
  if (ms >= 1000) return (ms / 1000).toFixed(2) + 's';
  return Math.round(ms) + 'ms';
}

/** A cost in US dollars: '-' when unknown, four decimals below a dollar (turns usually cost cents), else two. */
export function formatCostUsd(usd: number | null | undefined): string {
  if (usd == null) return '-';
  return '$' + (usd >= 1 ? usd.toFixed(2) : usd.toFixed(4));
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
 * The tool rows from a turn's recorded counts (Ask replies persist them), used when the caller has no tool events.
 * Empty when no tool call completed.
 */
function recordedToolStatistics(t: MetricsTranslate, metrics: CaptainChatMetrics): TurnStatistic[] {
  if (metrics.toolCallCount == null || metrics.toolCallCount <= 0) return [];
  return [
    { key: 'toolCalls', label: t('tool calls'), value: String(metrics.toolCallCount) },
    { key: 'toolTime', label: t('tool time'), value: formatMetricMs(metrics.toolTimeMs ?? 0) },
  ];
}

/**
 * Statistics of a captain chat turn with runtime metrics (Planning, captain chat, Ask replies): time to first token,
 * time to first text (only when it differs), streaming time, tokens per second, token count, the input and cached
 * tokens and the cost (only when the runtime reported them), total time, then the tool rows (from `tools` when given,
 * otherwise the turn's recorded tool counts). An estimated token count (the runtime reported none) reads "~N". With
 * `tokens: 'completion'` the token count is the completion tokens only (the compact strip, which has no tool rows); by
 * default it falls back to the runtime's total estimate.
 */
export function chatTurnStatistics(
  t: MetricsTranslate,
  metrics: CaptainChatMetrics,
  tools?: ToolEvent[] | null,
  tokens: 'completion' | 'completionOrTotal' = 'completionOrTotal',
): TurnStatistic[] {
  const tokenCount = tokens === 'completion' ? metrics.completionTokens : (metrics.completionTokens ?? metrics.totalTokens);
  const estimated = metrics.tokensEstimated === true && tokenCount != null && tokenCount === metrics.completionTokens;
  const hasInput = metrics.promptTokens != null;
  const rows: TurnStatistic[] = [
    { key: 'timeToFirstToken', label: t('time to first token'), value: formatMetricMs(metrics.timeToFirstTokenMs) },
  ];
  const firstText = metrics.timeToFirstTextMs;
  if (firstText != null && formatMetricMs(firstText) !== formatMetricMs(metrics.timeToFirstTokenMs)) {
    rows.push({ key: 'timeToFirstText', label: t('time to first text'), value: formatMetricMs(firstText) });
  }
  rows.push(
    { key: 'streaming', label: t('streaming'), value: formatMetricMs(metrics.streamingMs) },
    { key: 'tokensPerSecond', label: t('tokens/sec'), value: metrics.tokensPerSecond != null ? (estimated ? '~' : '') + metrics.tokensPerSecond.toFixed(1) : '-' },
    {
      key: 'tokens',
      label: hasInput && metrics.completionTokens != null ? t('output tokens') : t('tokens'),
      value: tokenCount != null ? (estimated ? '~' : '') + String(tokenCount) : '-',
    },
  );
  if (hasInput) rows.push({ key: 'inputTokens', label: t('input tokens'), value: String(metrics.promptTokens) });
  if (metrics.cachedTokens != null) rows.push({ key: 'cachedTokens', label: t('cached tokens'), value: String(metrics.cachedTokens) });
  if (metrics.costUsd != null) rows.push({ key: 'cost', label: t('cost'), value: formatCostUsd(metrics.costUsd) });
  rows.push({ key: 'total', label: t('total'), value: formatMetricMs(metrics.totalMs) });
  if (tokens === 'completion') return rows;
  return [...rows, ...(tools != null && tools.length > 0 ? toolStatistics(t, tools) : recordedToolStatistics(t, metrics))];
}

/**
 * Statistics of an Ask Armada reply. A reply the Admiral recorded telemetry for (`metrics`) shows the full set, as a
 * Planning reply does (see chatTurnStatistics), with the tool rows from its tool calls. An older reply has only the
 * turn's total time (`durationMs`) and each tool call's own time (`toolCalls[].elapsedMs`), so it shows those: total,
 * tool calls, and tool time. Empty when the reply has none of them.
 */
export function askTurnStatistics(t: MetricsTranslate, message: Pick<AskMessage, 'durationMs' | 'toolCalls' | 'metrics'>): TurnStatistic[] {
  const tools = toolCallsToEvents(message.toolCalls);
  if (message.metrics) {
    const metrics: CaptainChatMetrics = { ...message.metrics, totalMs: message.metrics.totalMs ?? message.durationMs ?? null };
    return chatTurnStatistics(t, metrics, tools);
  }

  const rows: TurnStatistic[] = [];
  if (message.durationMs != null) rows.push({ key: 'total', label: t('total'), value: formatMetricMs(message.durationMs) });
  return [...rows, ...toolStatistics(t, tools)];
}
