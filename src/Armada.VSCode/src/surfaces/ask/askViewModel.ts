/*
 * What the Ask Armada chat shows, built from the dashboard's conversation state (@dashboard/lib/askConversation, the
 * same reducer the dashboard's Ask page runs): messages with their Markdown rendered, tool calls, action proposals and
 * CLI permission requests awaiting a decision, and the reply being streamed. Pure, for tests.
 */

import { cliRequestForMessage, ConversationState, proposalForMessage } from '@dashboard/lib/askConversation';
import { toolCallsToEvents } from '@dashboard/lib/askFormat';
import type { ToolEvent } from '@dashboard/lib/toolEvents';
import type { AskMessage, CaptainChatMetrics } from '@dashboard/types/models';
import { renderMarkdown } from './markdown';

/** A tool call chip. */
export interface ToolChip {
  name: string;
  status: 'running' | 'success' | 'failed';
  detail: string;
}

/** A decision card (action proposal or CLI permission request). */
export interface DecisionCard {
  kind: 'proposal' | 'cli';
  id: string;
  title: string;
  detail: string;
  status: string;
  pending: boolean;
}

/** One message as the chat shows it. */
export interface ChatMessage {
  id: string;
  role: 'user' | 'assistant' | 'system';
  kind: string;
  html: string;
  time: string;
  tools: ToolChip[];
  card?: DecisionCard;
  local: boolean;
  /** The reply's telemetry, one line ("TTFT 1.2s · streaming 8.3s · 1,234 in · 567 out · 58 tok/s"), or empty. */
  metrics: string;
  /** The same telemetry, one item per line, for the tooltip. */
  metricsDetail: string;
}

/** Totals over the conversation's replies. */
export interface ChatTotals {
  replies: number;
  promptTokens: number;
  completionTokens: number;
  cachedTokens: number;
  costUsd: number;
  estimated: boolean;
}

/** Everything the chat webview renders. */
export interface ChatView {
  connected: boolean;
  threadId: string | null;
  title: string;
  messages: ChatMessage[];
  streaming: { html: string; thinking: string; tools: ToolChip[] } | null;
  turnActive: boolean;
  error: string | null;
  hasMore: boolean;
  totals: ChatTotals;
}

function chips(tools: ToolEvent[] | null | undefined): ToolChip[] {
  return (tools ?? []).map((t) => ({ name: t.name, status: t.status, detail: [t.arguments ? t.arguments.slice(0, 400) : '', t.result ? `→ ${t.result.slice(0, 400)}` : ''].filter(Boolean).join('\n') }));
}

function role(message: AskMessage): ChatMessage['role'] {
  const value = String(message.role).toLowerCase();
  return value === 'user' ? 'user' : value === 'system' ? 'system' : 'assistant';
}

/** Builds the chat view from the conversation state. */
export function buildChatView(state: ConversationState, connected: boolean, error: string | null): ChatView {
  const messages: ChatMessage[] = state.messages.map((message) => {
    const view: ChatMessage = {
      id: message.id,
      role: role(message),
      kind: String(message.kind),
      html: renderMarkdown(message.contentText ?? ''),
      time: message.createdUtc ? new Date(message.createdUtc).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : '',
      tools: chips(toolCallsToEvents(message.toolCalls)),
      local: message.isLocal === true,
      metrics: formatMetrics(message.metrics, message.durationMs, false),
      metricsDetail: formatMetrics(message.metrics, message.durationMs, true),
    };
    const proposal = proposalForMessage(state, message);
    if (proposal) {
      view.card = {
        kind: 'proposal',
        id: proposal.id,
        title: proposal.summaryText || proposal.toolName,
        detail: [proposal.argumentsText ?? '', proposal.resultText ? `Result: ${proposal.resultText}` : '', proposal.errorText ? `Error: ${proposal.errorText}` : ''].filter(Boolean).join('\n'),
        status: String(proposal.status),
        pending: String(proposal.status).toLowerCase() === 'pending',
      };
    }
    const cli = cliRequestForMessage(state, message);
    if (cli) {
      view.card = {
        kind: 'cli',
        id: cli.id,
        title: `${cli.toolName}${cli.summaryText ? `: ${cli.summaryText}` : ''}`,
        detail: '',
        status: String(cli.status),
        pending: String(cli.status).toLowerCase() === 'pending',
      };
    }
    return view;
  });
  const streaming = state.streaming && !state.streaming.finished
    ? { html: renderMarkdown(state.streaming.text), thinking: state.streaming.thinking.slice(-600), tools: chips(state.streaming.tools) }
    : null;
  return {
    connected,
    threadId: state.threadId,
    title: state.thread?.title || (state.threadId ? 'Conversation' : 'New conversation'),
    messages,
    streaming,
    turnActive: state.turnActive,
    error: error ?? state.turnError,
    hasMore: state.hasMore,
    totals: totalsOf(state.messages),
  };
}

function seconds(ms: number | null | undefined): string {
  if (ms === null || ms === undefined || !Number.isFinite(ms)) return '';
  return ms < 1000 ? `${Math.round(ms)} ms` : `${(ms / 1000).toFixed(ms < 10000 ? 2 : 1)}s`;
}

function count(value: number | null | undefined): string {
  return value === null || value === undefined ? '' : Math.round(value).toLocaleString('en-US');
}

/** One line (or, with detail, one item per line) of a reply's telemetry. Empty without metrics. */
export function formatMetrics(metrics: CaptainChatMetrics | null | undefined, durationMs: number | null | undefined, detail: boolean): string {
  if (!metrics && !durationMs) return '';
  const m = metrics ?? ({} as CaptainChatMetrics);
  const est = m.tokensEstimated ? '~' : '';
  const parts: Array<[string, string]> = [];
  const add = (label: string, value: string): void => {
    if (value) parts.push([label, value]);
  };
  add('TTFT', seconds(m.timeToFirstTokenMs));
  if (m.timeToFirstTextMs !== undefined && m.timeToFirstTextMs !== null && m.timeToFirstTextMs !== m.timeToFirstTokenMs) add('first text', seconds(m.timeToFirstTextMs));
  add('streaming', seconds(m.streamingMs));
  add('total', seconds(m.totalMs ?? durationMs ?? null));
  add('in', count(m.promptTokens) && `${est}${count(m.promptTokens)}`);
  add('out', count(m.completionTokens) && `${est}${count(m.completionTokens)}`);
  add('cached', m.cachedTokens ? count(m.cachedTokens) : '');
  add('tok/s', m.tokensPerSecond ? (Math.round(m.tokensPerSecond * 10) / 10).toString() : '');
  add('cost', m.costUsd ? `$${m.costUsd < 0.01 ? m.costUsd.toFixed(4) : m.costUsd.toFixed(3)}` : '');
  add('tools', m.toolCallCount ? `${m.toolCallCount}${m.toolTimeMs ? ` (${seconds(m.toolTimeMs)})` : ''}` : '');
  if (detail) {
    const names: Record<string, string> = { TTFT: 'Time to first token', 'first text': 'Time to first text', streaming: 'Streaming time', total: 'Total time', in: 'Prompt tokens', out: 'Completion tokens', cached: 'Cached tokens', 'tok/s': 'Tokens per second', cost: 'Cost', tools: 'Tool calls' };
    const lines = parts.map(([label, value]) => `${names[label] ?? label}: ${value}`);
    if (m.tokensEstimated) lines.push('Token counts are estimated (~)');
    return lines.join('\n');
  }
  return parts.map(([label, value]) => (['TTFT', 'first text', 'streaming', 'total', 'cost', 'tools'].includes(label) ? `${label} ${value}` : `${value} ${label}`)).join(' · ');
}

function totalsOf(messages: AskMessage[]): ChatTotals {
  const totals: ChatTotals = { replies: 0, promptTokens: 0, completionTokens: 0, cachedTokens: 0, costUsd: 0, estimated: false };
  for (const message of messages) {
    const m = message.metrics;
    if (!m) continue;
    totals.replies++;
    totals.promptTokens += m.promptTokens ?? 0;
    totals.completionTokens += m.completionTokens ?? 0;
    totals.cachedTokens += m.cachedTokens ?? 0;
    totals.costUsd += m.costUsd ?? 0;
    totals.estimated = totals.estimated || m.tokensEstimated === true;
  }
  return totals;
}
