import type { AskToolCall } from '../types/models';
import type { ToolEvent } from './toolEvents';

// Display helpers for Ask Armada transcripts and approval cards, shared by the dashboard and the mobile app so
// both show the same arguments, previews, and durations.

/** Pretty-print JSON text; fall back to the raw text (empty for null or empty input). */
export function prettyJson(raw: string | null | undefined): string {
  if (!raw) return '';
  try { return JSON.stringify(JSON.parse(raw), null, 2); } catch { return raw; }
}

/** Longest tool-result preview in a collapsed tool chip. */
export const TOOL_RESULT_PREVIEW_MAX = 80;

/**
 * A compact one-line preview of a tool result for the collapsed chip: JSON is flattened to a single line,
 * whitespace collapsed, and the whole thing truncated (with an ellipsis) so it never wraps the summary row.
 */
export function toolResultPreview(raw: string): string {
  let text = raw;
  try { text = JSON.stringify(JSON.parse(raw)); } catch { /* not JSON: use as-is */ }
  text = text.replace(/\s+/g, ' ').trim();
  return text.length > TOOL_RESULT_PREVIEW_MAX ? text.slice(0, TOOL_RESULT_PREVIEW_MAX) + '…' : text;
}

/** Compact runtime label for a tool call (ms below a second, then seconds with two or one decimals). */
export function formatToolMs(ms: number | null | undefined): string {
  if (ms == null) return '';
  if (ms < 1000) return `${Math.round(ms)}ms`;
  return `${(ms / 1000).toFixed(ms < 10000 ? 2 : 1)}s`;
}

/** Duration of a captain turn shown on its reply (ms below a second, then seconds with one or no decimals). */
export function formatTurnDuration(ms: number | null | undefined): string {
  if (ms == null) return '';
  if (ms < 1000) return `${Math.round(ms)}ms`;
  return `${(ms / 1000).toFixed(ms < 10000 ? 1 : 0)}s`;
}

/** Persisted tool calls in the shape the tool chips render. */
export function toolCallsToEvents(calls: AskToolCall[] | null | undefined): ToolEvent[] {
  return (calls ?? []).map((call, i) => ({
    id: call.callId || call.id || `call-${i}`,
    name: call.toolName || 'tool',
    status: call.permissionDenied === true || call.ok === false ? 'failed' : call.ok == null && !call.resultText ? 'running' : 'success',
    arguments: call.argumentsText ?? null,
    result: call.resultText ?? null,
    elapsedMs: call.elapsedMs ?? null,
    ...(call.permissionDenied === true ? { permissionDenied: true } : {}),
  }));
}
