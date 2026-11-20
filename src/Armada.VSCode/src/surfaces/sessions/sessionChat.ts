/*
 * Planning sessions and backlog refinement sessions as chats in editor tabs: talk with a captain about a vessel or a
 * backlog item, watch its replies arrive (the Admiral's *.message.created and *.message.updated events carry the
 * whole message as it grows), then summarize and dispatch the plan as a voyage, or apply the refinement to the
 * backlog item. One tab per session.
 */

import * as crypto from 'crypto';
import * as vscode from 'vscode';
import * as api from '@dashboard/api/client';
import type { WebSocketMessage } from '@dashboard/types/models';
import { describeError } from '../../transport/errors';
import { ArmadaEventBus } from '../../transport/eventBus';
import { renderMarkdown } from '../ask/markdown';
import { showForm } from '../ops/formPanel';

/** A message of either kind of session. */
interface SessionMessage {
  id: string;
  role: string;
  sequence: number;
  content: string;
  createdUtc?: string;
}

/** What differs between planning and refinement. */
export interface SessionKind {
  key: 'planning' | 'refinement';
  noun: string;
  eventPrefix: string;
  finishLabel: string;
  load(id: string): Promise<{ title: string; status: string; messages: SessionMessage[] }>;
  send(id: string, content: string): Promise<void>;
  stopTurn?(id: string): Promise<void>;
  end(id: string): Promise<void>;
  /** Summarizes and finishes (dispatch or apply); returns a message, or undefined when cancelled. */
  finish(id: string): Promise<string | undefined>;
}

/** Planning sessions: a captain plans work on a vessel, and the plan is dispatched as a voyage. */
export const PLANNING: SessionKind = {
  key: 'planning',
  noun: 'Planning Session',
  eventPrefix: 'planning-session.',
  finishLabel: 'Dispatch Plan...',
  async load(id) {
    const [detail, messages] = await Promise.all([api.getPlanningSession(id), api.listPlanningSessionMessages(id, { pageNumber: 1, pageSize: 500, order: 'CreatedAscending' } as never)]);
    return { title: detail.session.title, status: detail.session.status, messages: (messages.objects ?? []) as SessionMessage[] };
  },
  async send(id, content) {
    await api.sendPlanningSessionMessage(id, { content });
  },
  async stopTurn(id) {
    await api.stopPlanningTurn(id);
  },
  async end(id) {
    await api.stopPlanningSession(id);
  },
  async finish(id) {
    const summary = await api.summarizePlanningSession(id, {});
    const result = await showForm<string>({
      title: 'Dispatch the Plan',
      intro: 'The captain summarized the plan. Review it, then dispatch it as a voyage.',
      submitLabel: 'Dispatch Voyage',
      fields: [
        { key: 'title', label: 'Title', type: 'text', value: summary.title, required: true },
        { key: 'description', label: 'Plan', type: 'textarea', value: summary.description, required: true },
      ],
    }, async (values) => {
      try {
        const voyage = await api.dispatchPlanningSession(id, { messageId: summary.messageId, title: values.title, description: values.description });
        return { result: voyage.id };
      } catch (err) {
        return { error: describeError(err) };
      }
    });
    if (!result) return undefined;
    void vscode.commands.executeCommand('armada.voyage.open', result);
    return 'Dispatched the plan as a voyage.';
  },
};

/** Backlog refinement: a captain refines a backlog item, and the refinement is applied to it. */
export const REFINEMENT: SessionKind = {
  key: 'refinement',
  noun: 'Refinement Session',
  eventPrefix: 'objective-refinement-session.',
  finishLabel: 'Apply Refinement...',
  async load(id) {
    const [detail, messages] = await Promise.all([api.getObjectiveRefinementSession(id), api.listObjectiveRefinementMessages(id, { pageNumber: 1, pageSize: 500, order: 'CreatedAscending' } as never)]);
    return { title: detail.session.title ?? detail.objective?.title ?? 'Refinement', status: detail.session.status, messages: (messages.objects ?? []) as SessionMessage[] };
  },
  async send(id, content) {
    await api.sendObjectiveRefinementMessage(id, { content } as never);
  },
  async end(id) {
    await api.stopObjectiveRefinementSession(id);
  },
  async finish(id) {
    const summary = await api.summarizeObjectiveRefinementSession(id);
    const lines = (items: string[]): string => items.join('\n');
    const applied = await showForm<boolean>({
      title: 'Apply the Refinement',
      intro: 'The captain summarized the refinement. Review it, then apply it to the backlog item.',
      submitLabel: 'Apply',
      fields: [
        { key: 'summary', label: 'Summary', type: 'textarea', value: summary.summary },
        { key: 'acceptanceCriteria', label: 'Acceptance criteria', type: 'textarea', value: lines(summary.acceptanceCriteria), hint: 'One per line.' },
        { key: 'nonGoals', label: 'Non-goals', type: 'textarea', value: lines(summary.nonGoals), hint: 'One per line.' },
      ],
    }, async (values) => {
      try {
        const split = (text: string | undefined): string[] => (text ?? '').split(/\r?\n/).map((l) => l.trim()).filter(Boolean);
        await api.applyObjectiveRefinementSummary(id, { messageId: summary.messageId, summary: values.summary, acceptanceCriteria: split(values.acceptanceCriteria), nonGoals: split(values.nonGoals) } as never);
        return { result: true };
      } catch (err) {
        return { error: describeError(err) };
      }
    });
    return applied ? 'Applied the refinement to the backlog item.' : undefined;
  },
};

interface OpenSession {
  kind: SessionKind;
  id: string;
  panel: vscode.WebviewPanel;
  messages: SessionMessage[];
  title: string;
  status: string;
  busy: boolean;
}

/** Opens and maintains session chats. */
export class SessionChatManager implements vscode.Disposable {
  private readonly _Sessions = new Map<string, OpenSession>();
  private readonly _Subscription: { dispose(): void };

  /**
   * @throws Error when bus is missing.
   */
  public constructor(bus: ArmadaEventBus) {
    if (!bus) throw new Error('bus is required');
    this._Subscription = bus.subscribe((event) => this.onEvent(event as unknown as WebSocketMessage));
  }

  /** Opens (or reveals) a session's chat. */
  public async open(kind: SessionKind, id: string): Promise<void> {
    const key = `${kind.key}:${id}`;
    const existing = this._Sessions.get(key);
    if (existing) {
      existing.panel.reveal(undefined, false);
      return;
    }
    const panel = vscode.window.createWebviewPanel('armada.session', kind.noun, vscode.ViewColumn.Active, { enableScripts: true, retainContextWhenHidden: true });
    const session: OpenSession = { kind, id, panel, messages: [], title: kind.noun, status: '', busy: false };
    this._Sessions.set(key, session);
    panel.onDidDispose(() => this._Sessions.delete(key));
    panel.webview.html = sessionHtml(crypto.randomBytes(16).toString('base64'), kind.finishLabel, !!kind.stopTurn);
    panel.webview.onDidReceiveMessage((message: { type?: string; text?: string; href?: string }) => void this.onMessage(session, message));
    await this.reload(session);
  }

  public dispose(): void {
    this._Subscription.dispose();
    for (const s of this._Sessions.values()) s.panel.dispose();
  }

  private async reload(session: OpenSession): Promise<void> {
    try {
      const loaded = await session.kind.load(session.id);
      session.title = loaded.title;
      session.status = loaded.status;
      session.messages = loaded.messages.sort((a, b) => a.sequence - b.sequence);
      this.render(session, null);
    } catch (err) {
      this.render(session, `Could not load the session. ${describeError(err)}`);
    }
  }

  private async onMessage(session: OpenSession, message: { type?: string; text?: string; href?: string }): Promise<void> {
    try {
      switch (message.type) {
        case 'ready':
          this.render(session, null);
          return;
        case 'send':
          if (!message.text?.trim()) return;
          session.busy = true;
          session.messages.push({ id: `local-${Date.now()}`, role: 'User', sequence: Number.MAX_SAFE_INTEGER, content: message.text.trim() });
          this.render(session, null);
          await session.kind.send(session.id, message.text.trim());
          await this.reload(session);
          return;
        case 'stop':
          await session.kind.stopTurn?.(session.id);
          session.busy = false;
          await this.reload(session);
          return;
        case 'finish': {
          const done = await session.kind.finish(session.id);
          if (done) void vscode.window.showInformationMessage(`Armada: ${done}`);
          await this.reload(session);
          return;
        }
        case 'end':
          if ((await vscode.window.showWarningMessage(`End this ${session.kind.noun.toLowerCase()}? The captain is released.`, { modal: true }, 'End Session')) !== 'End Session') return;
          await session.kind.end(session.id);
          await this.reload(session);
          return;
        case 'link':
          if (message.href && /^(https?|mailto):/i.test(message.href)) await vscode.env.openExternal(vscode.Uri.parse(message.href));
          return;
      }
    } catch (err) {
      session.busy = false;
      this.render(session, describeError(err));
    }
  }

  private onEvent(raw: WebSocketMessage): void {
    if (!raw || typeof raw.type !== 'string') return;
    for (const session of this._Sessions.values()) {
      if (!raw.type.startsWith(session.kind.eventPrefix)) continue;
      const data = api.camelizeKeys(raw.data ?? null) as { sessionId?: string; session?: { id?: string; status?: string }; message?: SessionMessage } | null;
      const sessionId = data?.sessionId ?? data?.session?.id;
      if (sessionId !== session.id) continue;
      if (data?.message && raw.type.includes('.message.')) {
        const message = data.message;
        session.messages = session.messages.filter((m) => m.id !== message.id && !(m.id.startsWith('local-') && m.content === message.content && String(message.role).toLowerCase() === 'user'));
        session.messages.push(message);
        session.messages.sort((a, b) => a.sequence - b.sequence);
        if (String(message.role).toLowerCase() !== 'user') session.busy = false;
      }
      if (data?.session?.status) session.status = data.session.status;
      this.render(session, null);
    }
  }

  private render(session: OpenSession, error: string | null): void {
    session.panel.title = session.title.length > 40 ? `${session.title.slice(0, 39)}…` : session.title;
    const active = !/^(Stopped|Completed|Failed|Ended|Dispatched|Applied|Cancelled)$/i.test(session.status);
    void session.panel.webview.postMessage({
      type: 'state',
      title: session.title,
      status: session.status,
      active,
      busy: session.busy,
      error,
      messages: session.messages.map((m) => ({ id: m.id, user: String(m.role).toLowerCase() === 'user', html: renderMarkdown(m.content), time: m.createdUtc ? new Date(m.createdUtc).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : '' })),
    });
  }
}

function sessionHtml(nonce: string, finishLabel: string, canStop: boolean): string {
  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}'">
<style>
html, body { height: 100%; margin: 0; }
body { font-family: var(--vscode-font-family); font-size: var(--vscode-font-size); color: var(--vscode-foreground); display: flex; flex-direction: column; max-width: 920px; margin: 0 auto; }
header { padding: 12px 16px 8px; border-bottom: 1px solid var(--vscode-panel-border); display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
h1 { font-size: 1.25em; margin: 0; flex: 1; }
.status { font-size: 0.8em; color: var(--vscode-descriptionForeground); }
#messages { flex: 1; overflow-y: auto; padding: 12px 16px; }
.msg { margin-bottom: 14px; line-height: 1.5; overflow-wrap: anywhere; }
.meta { font-size: 0.8em; color: var(--vscode-descriptionForeground); margin-bottom: 2px; }
.msg.user .body { background: var(--vscode-input-background); border: 1px solid var(--vscode-input-border, transparent); border-radius: 6px; padding: 6px 10px; }
.body p { margin: 0 0 6px; } .body code { font-family: var(--vscode-editor-font-family); background: var(--vscode-textCodeBlock-background); padding: 0 3px; border-radius: 3px; }
.body pre { background: var(--vscode-textCodeBlock-background); padding: 6px 8px; overflow-x: auto; border-radius: 4px; } .body pre code { background: none; }
.body ul, .body ol { padding-left: 20px; } a { color: var(--vscode-textLink-foreground); }
.body table { border-collapse: collapse; } .body th, .body td { border: 1px solid var(--vscode-panel-border); padding: 2px 6px; }
.working { color: var(--vscode-descriptionForeground); font-size: 0.85em; } .working::after { content: ' ●'; animation: p 1.2s infinite; } @keyframes p { 50% { opacity: 0.2; } }
.error { color: var(--vscode-errorForeground); padding: 4px 16px; }
footer { padding: 10px 16px 14px; border-top: 1px solid var(--vscode-panel-border); }
.composer { display: flex; gap: 8px; align-items: flex-end; }
textarea { flex: 1; resize: vertical; min-height: 54px; max-height: 240px; color: var(--vscode-input-foreground); background: var(--vscode-input-background); border: 1px solid var(--vscode-input-border, var(--vscode-panel-border)); border-radius: 4px; padding: 6px 8px; font: inherit; }
textarea:focus, button:focus-visible { outline: 1px solid var(--vscode-focusBorder); }
button { font: inherit; color: var(--vscode-button-foreground); background: var(--vscode-button-background); border: 1px solid var(--vscode-button-border, transparent); border-radius: 2px; padding: 4px 12px; cursor: pointer; }
button:hover { background: var(--vscode-button-hoverBackground); } button.secondary { color: var(--vscode-button-secondaryForeground); background: var(--vscode-button-secondaryBackground); }
button:disabled { opacity: 0.6; cursor: default; }
</style>
</head>
<body>
<header><h1 id="title"></h1><span class="status" id="status"></span>
<button id="finish">${finishLabel}</button>${canStop ? '<button class="secondary" id="stop" hidden>Stop Reply</button>' : ''}<button class="secondary" id="end">End Session</button></header>
<div id="messages" role="log" aria-live="polite"></div>
<div class="error" id="error" role="alert" hidden></div>
<footer><div class="composer"><textarea id="input" placeholder="Message the captain (Enter to send, Shift+Enter for a new line)" aria-label="Message"></textarea><button id="send">Send</button></div></footer>
<script nonce="${nonce}">
const vscode = acquireVsCodeApi();
const $ = (id) => document.getElementById(id);
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c]);
let state = null;
function render() {
  if (!state) return;
  $('title').textContent = state.title;
  $('status').textContent = state.status;
  const box = $('messages');
  const atBottom = box.scrollTop + box.clientHeight >= box.scrollHeight - 30;
  box.innerHTML = state.messages.map((m) => '<div class="msg ' + (m.user ? 'user' : 'captain') + '"><div class="meta">' + (m.user ? 'You' : 'Captain') + (m.time ? ' · ' + esc(m.time) : '') + '</div><div class="body">' + m.html + '</div></div>').join('') + (state.busy ? '<div class="working">The captain is replying</div>' : '');
  if (atBottom || state.busy) box.scrollTop = box.scrollHeight;
  $('error').hidden = !state.error; $('error').textContent = state.error || '';
  $('send').disabled = !state.active; $('input').disabled = !state.active;
  $('finish').disabled = !state.active || state.busy || !state.messages.length;
  $('end').disabled = !state.active;
  if ($('stop')) $('stop').hidden = !state.busy;
}
function send() { const t = $('input').value.trim(); if (!t || !state || !state.active) return; vscode.postMessage({ type: 'send', text: t }); $('input').value = ''; }
$('send').addEventListener('click', send);
$('input').addEventListener('keydown', (e) => { if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) { e.preventDefault(); send(); } });
$('finish').addEventListener('click', () => vscode.postMessage({ type: 'finish' }));
$('end').addEventListener('click', () => vscode.postMessage({ type: 'end' }));
if ($('stop')) $('stop').addEventListener('click', () => vscode.postMessage({ type: 'stop' }));
document.addEventListener('click', (e) => { const a = e.target instanceof Element ? e.target.closest('a[href]') : null; if (a) { e.preventDefault(); vscode.postMessage({ type: 'link', href: a.getAttribute('href') }); } });
window.addEventListener('message', (e) => { if (e.data && e.data.type === 'state') { state = e.data; render(); } });
vscode.postMessage({ type: 'ready' });
</script>
</body>
</html>`;
}
