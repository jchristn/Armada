/*
 * What the Ask Armada chat shows, built from the dashboard's conversation state (@dashboard/lib/askConversation, the
 * same reducer the dashboard's Ask page runs): messages with their Markdown rendered, tool calls, action proposals and
 * CLI permission requests awaiting a decision, and the reply being streamed. Pure, for tests.
 */

import { cliRequestForMessage, ConversationState, proposalForMessage } from '@dashboard/lib/askConversation';
import { toolCallsToEvents } from '@dashboard/lib/askFormat';
import type { ToolEvent } from '@dashboard/lib/toolEvents';
import type { AskMessage } from '@dashboard/types/models';
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
  };
}
