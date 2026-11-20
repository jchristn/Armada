/*
 * Ask Armada as a chat view in the Armada sidebar (modeled on Mux's ChatViewProvider): talk to the Ask captain, see
 * its reply stream in with tool calls, approve or reject the actions it proposes and the CLI tools it asks to run,
 * stop a turn, start a new conversation or reopen an earlier one. The conversation state is the dashboard's own
 * (@dashboard/lib/askConversation reducer) fed by the same REST calls and live ask.* events, so the chat behaves
 * exactly like the dashboard's Ask page. The webview only renders; it has no network access.
 */

import * as crypto from 'crypto';
import * as vscode from 'vscode';
import {
  approveAskProposal,
  cancelAskTurn,
  createAskThread,
  decideCliPermissionRequest,
  deleteAskThread,
  getAskQuickActions,
  getAskWorkSnapshot,
  listCaptains,
  markAskThreadUnread,
  runAskQuickAction,
  enumerateAskMessages,
  enumerateAskThreads,
  markAskThreadRead,
  rejectAskProposal,
  sendAskMessage,
  setAskThreadCliPermissionPolicy,
  summarizeAskThread,
  updateAskThread,
} from '@dashboard/api/client';
import { ConversationAction, conversationReducer, ConversationState, initialConversation } from '@dashboard/lib/askConversation';
import { parseAskEvent } from '@dashboard/lib/askEvents';
import { loadAskThreadView, withToolCalls } from '@dashboard/lib/askPaging';
import { parseCliPermissionEvent } from '@dashboard/lib/cliPermissions';
import type { AskMessage, AskQuickAction, Captain, CliPermissionPolicy, WebSocketMessage } from '@dashboard/types/models';
import { ConnectionManager } from '../../connection/connectionManager';
import { describeError } from '../../transport/errors';
import { ArmadaEventBus } from '../../transport/eventBus';
import { formatAge } from '../../util/format';
import { nativeCommandForRoute } from '../blackbox/entryView';
import { buildChatView } from './askViewModel';

/** View id (package.json contributes.views.armada, type webview). */
export const ASK_VIEW_ID = 'armadaAsk';

/** globalState key of the conversation the chat last showed. */
const LAST_THREAD_KEY = 'armada.ask.lastThread';

/** globalState key of the captain new conversations start with. */
const CAPTAIN_KEY = 'armada.ask.captain';

/** The warning shown before auto-approve is turned on (the dashboard's wording). */
const AUTO_APPROVE_WARNING = 'Auto-approve runs state-changing actions the captain proposes immediately, without a confirm card. Only turn it on for conversations you trust; every action is still recorded here.';

const MESSAGE_PAGE_SIZE = 30;

/** Messages the chat webview posts. */
type ChatInbound =
  | { type: 'ready' }
  | { type: 'send'; text: string }
  | { type: 'stop' }
  | { type: 'new' }
  | { type: 'history' }
  | { type: 'quick' }
  | { type: 'older' }
  | { type: 'decide'; kind: 'proposal' | 'cli'; id: string; approve: boolean }
  | { type: 'link'; href: string }
  | { type: 'openInEditor' }
  | { type: 'setCaptain'; id: string }
  | { type: 'setAutoApprove'; value: boolean }
  | { type: 'setCliPolicy'; value: string };

/** Host dependencies. */
export interface AskViewDeps {
  extensionUri: vscode.Uri;
  connection: ConnectionManager;
  bus: ArmadaEventBus;
  globalState: vscode.Memento;
}

/** The Ask Armada chat view. */
export class AskViewProvider implements vscode.WebviewViewProvider, vscode.Disposable {
  private readonly _Deps: AskViewDeps;
  private readonly _Subscriptions: Array<{ dispose(): void }> = [];
  private _SideView: vscode.WebviewView | undefined;
  private _Panel: vscode.WebviewPanel | undefined;
  private readonly _Hosts = new Set<vscode.Webview>();
  private _Captains: Captain[] = [];
  private _DraftCaptainId: string | undefined;
  private _DraftAutoApprove = false;
  private _DraftCliPolicy: CliPermissionPolicy | null = null;
  private _State: ConversationState = initialConversation();
  private _Error: string | null = null;
  private _RenderTimer: ReturnType<typeof setTimeout> | undefined;
  private _Queued: string | undefined;
  private _Loaded = false;

  /**
   * @throws Error when deps is missing.
   */
  public constructor(deps: AskViewDeps) {
    if (!deps) throw new Error('deps is required');
    this._Deps = deps;
    this._Subscriptions.push(deps.bus.subscribe((event) => this.onSocket(event as unknown as WebSocketMessage)));
    this._Subscriptions.push(deps.connection.onDidChange((state) => {
      if (state.phase === 'connected' && this._Hosts.size && !this._Loaded) void this.restore();
      if (state.phase !== 'connecting') this.render();
    }));
  }

  public resolveWebviewView(view: vscode.WebviewView): void {
    this._SideView = view;
    this.attach(view.webview, false);
    view.onDidDispose(() => {
      this._Hosts.delete(view.webview);
      this._SideView = undefined;
    });
    view.onDidChangeVisibility(() => {
      if (view.visible && this._State.threadId) void markAskThreadRead(this._State.threadId).catch(() => undefined);
    });
  }

  /** Opens the chat in an editor tab (the same conversation as the sidebar), or reveals it. */
  public openInEditor(): void {
    if (this._Panel) {
      this._Panel.reveal(undefined, false);
      return;
    }
    const panel = vscode.window.createWebviewPanel('armada.ask', 'Ask Armada', vscode.ViewColumn.Active, { enableScripts: true, retainContextWhenHidden: true, enableFindWidget: true });
    this._Panel = panel;
    this.attach(panel.webview, true);
    panel.onDidDispose(() => {
      this._Hosts.delete(panel.webview);
      this._Panel = undefined;
    });
  }

  /** Shows the chat: the editor tab when one is open, else the sidebar view. */
  private async focus(): Promise<void> {
    if (this._Panel) this._Panel.reveal(undefined, false);
    else await vscode.commands.executeCommand(`${ASK_VIEW_ID}.focus`);
  }

  private attach(webview: vscode.Webview, inEditor: boolean): void {
    webview.options = { enableScripts: true, localResourceRoots: [] };
    webview.html = chatHtml(crypto.randomBytes(16).toString('base64'), inEditor);
    webview.onDidReceiveMessage((message: ChatInbound) => void this.onMessage(message));
    this._Hosts.add(webview);
  }

  /** Starts a new conversation with a prompt, showing the chat (Ask Armada About This Failure and similar). */
  public async ask(prompt: string): Promise<void> {
    await this.focus();
    this.dispatch({ type: 'reset', threadId: null });
    if (!this._Hosts.size) {
      this._Queued = prompt;
      return;
    }
    await this.send(prompt);
  }

  /** Starts a new, empty conversation. */
  public async newConversation(): Promise<void> {
    await this.focus();
    this.dispatch({ type: 'reset', threadId: null });
    this._Error = null;
    await this._Deps.globalState.update(LAST_THREAD_KEY, undefined);
  }

  /** Opens a conversation by id. */
  public async openConversation(threadId: string): Promise<void> {
    await this.focus();
    await this.load(threadId);
  }

  /** The most recent conversation's id, if any. */
  public async latestConversationId(): Promise<string | undefined> {
    return (await enumerateAskThreads({ pageNumber: 1, pageSize: 1 })).objects?.[0]?.id;
  }

  /** The conversation shown and its message count (for the integration tests). */
  public get shown(): { threadId: string | null; messages: number } {
    return { threadId: this._State.threadId, messages: this._State.messages.length };
  }

  /** Picks an earlier conversation to reopen; each row has a delete button. */
  public async pickConversation(): Promise<void> {
    if (!(await this.ensureConnected())) return;
    const threads = (await enumerateAskThreads({ pageNumber: 1, pageSize: 100 })).objects ?? [];
    if (!threads.length) {
      void vscode.window.showInformationMessage('Armada: no earlier conversations.');
      return;
    }
    const remove: vscode.QuickInputButton = { iconPath: new vscode.ThemeIcon('trash'), tooltip: 'Delete conversation' };
    const quickPick = vscode.window.createQuickPick<vscode.QuickPickItem & { id: string }>();
    quickPick.title = 'Ask Armada: Conversations';
    quickPick.placeholder = 'Reopen a conversation';
    quickPick.matchOnDetail = true;
    quickPick.items = threads.map((t) => ({
      label: `${t.pinned ? '$(pinned) ' : ''}${t.title || 'Conversation'}`,
      description: [t.unreadCount ? `${t.unreadCount} unread` : '', formatAge(t.lastMessageUtc ?? t.createdUtc)].filter(Boolean).join(' · '),
      detail: t.summaryText ?? undefined,
      id: t.id,
      buttons: [remove],
    }));
    const picked = await new Promise<string | undefined>((resolve) => {
      quickPick.onDidAccept(() => resolve(quickPick.selectedItems[0]?.id));
      quickPick.onDidHide(() => resolve(undefined));
      quickPick.onDidTriggerItemButton(async (event) => {
        if ((await vscode.window.showWarningMessage(`Delete the conversation "${event.item.label}"?`, { modal: true }, 'Delete')) !== 'Delete') return;
        await deleteAskThread(event.item.id);
        quickPick.items = quickPick.items.filter((i) => i.id !== event.item.id);
        if (this._State.threadId === event.item.id) await this.newConversation();
      });
      quickPick.show();
    });
    quickPick.dispose();
    if (!picked) return;
    await this.focus();
    await this.load(picked);
  }

  /** Runs one of the Admiral's Ask quick actions (as a proposal to approve, or as a message). */
  private async quickAction(): Promise<void> {
    if (!(await this.ensureConnected())) return;
    const actions = await getAskQuickActions();
    const threadId = this._State.threadId;
    const builtIn = threadId ? [
      { label: '$(note) Summarize this conversation', description: '', detail: 'The captain writes a summary shown in the conversation list', action: { name: '__summarize' } as AskQuickAction },
      { label: '$(mail) Mark this conversation unread', description: '', detail: 'It shows as unread in the conversation list', action: { name: '__unread' } as AskQuickAction },
      { label: '$(edit) Rename this conversation', description: '', detail: '', action: { name: '__rename' } as AskQuickAction },
      { label: '$(pulse) Show the work this conversation tracks', description: '', detail: 'Voyages, missions and runs Armada started for you here', action: { name: '__work' } as AskQuickAction },
    ] : [];
    const picked = await vscode.window.showQuickPick([...builtIn, ...actions.map((a) => ({ label: a.title ?? a.name, description: a.command ?? '', detail: a.description, action: a }))], { title: 'Ask Armada: Quick Actions', matchOnDetail: true });
    if (!picked) return;
    if (threadId && picked.action.name === '__summarize') {
      await summarizeAskThread(threadId);
      void vscode.window.showInformationMessage('Armada: summarizing the conversation.');
      return;
    }
    if (threadId && picked.action.name === '__work') {
      const tracked = this._State.trackedWork;
      if (!tracked.length) {
        void vscode.window.showInformationMessage('Armada: this conversation tracks no work yet.');
        return;
      }
      const snapshots = await Promise.all(tracked.map((w) => getAskWorkSnapshot(threadId, w.id).catch(() => w)));
      await vscode.commands.executeCommand('armada.showResult', 'Tracked work', snapshots);
      return;
    }
    if (threadId && picked.action.name === '__rename') {
      const title = await vscode.window.showInputBox({ title: 'Rename conversation', value: this._State.thread?.title ?? '', ignoreFocusOut: true });
      if (!title?.trim()) return;
      const thread = await updateAskThread(threadId, { title: title.trim() });
      this.dispatch({ type: 'thread', thread });
      return;
    }
    if (threadId && picked.action.name === '__unread') {
      await markAskThreadUnread(threadId);
      return;
    }
    if (picked.action.toolName) {
      let threadId = this._State.threadId;
      if (!threadId) {
        const created = await createAskThread({});
        threadId = created.id;
        this.dispatch({ type: 'reset', threadId });
        this.dispatch({ type: 'thread', thread: created });
      }
      const proposal = await runAskQuickAction(threadId, picked.action.toolName, {}, { propose: true });
      this.dispatch({ type: 'proposal', proposal });
      void this.refreshLatest(threadId);
      return;
    }
    await this.send(picked.action.command ?? picked.action.title ?? picked.action.name);
  }

  public dispose(): void {
    if (this._RenderTimer) clearTimeout(this._RenderTimer);
    for (const s of this._Subscriptions) s.dispose();
  }

  private async onMessage(message: ChatInbound): Promise<void> {
    if (!message || typeof message !== 'object') return;
    try {
      switch (message.type) {
        case 'ready':
          this.render();
          if (this._Queued) {
            const prompt = this._Queued;
            this._Queued = undefined;
            await this.send(prompt);
          } else if (!this._Loaded) {
            await this.restore();
          }
          return;
        case 'send':
          if (message.text.trim()) await this.send(message.text.trim());
          return;
        case 'stop':
          if (this._State.threadId) await cancelAskTurn(this._State.threadId);
          return;
        case 'new':
          await this.newConversation();
          return;
        case 'history':
          await this.pickConversation();
          return;
        case 'quick':
          await this.quickAction();
          return;
        case 'older':
          await this.loadOlder();
          return;
        case 'decide':
          await this.decide(message.kind, message.id, message.approve);
          return;
        case 'link':
          await this.openLink(message.href);
          return;
        case 'openInEditor':
          this.openInEditor();
          return;
        case 'setCaptain':
          await this.setCaptain(message.id || null);
          return;
        case 'setAutoApprove':
          await this.setAutoApprove(message.value === true);
          return;
        case 'setCliPolicy':
          await this.setCliPolicy(isCliPolicy(message.value) ? message.value : null);
          return;
      }
    } catch (err) {
      this._Error = describeError(err);
      this.render();
    }
  }

  private async ensureConnected(): Promise<boolean> {
    return this._Deps.connection.connected || this._Deps.connection.connect({ interactive: true });
  }

  private async restore(): Promise<void> {
    if (!this._Deps.connection.connected) return;
    this._Loaded = true;
    await this.loadCaptains();
    const last = this._Deps.globalState.get<string>(LAST_THREAD_KEY);
    if (last) await this.load(last).catch(() => this._Deps.globalState.update(LAST_THREAD_KEY, undefined));
  }

  private async load(threadId: string): Promise<void> {
    this._Error = null;
    this.dispatch({ type: 'reset', threadId });
    const [detail, page] = await Promise.all([
      loadAskThreadView(threadId),
      enumerateAskMessages(threadId, { pageSize: MESSAGE_PAGE_SIZE }).then(async (p) => ({ ...p, messages: await withToolCalls(threadId, p?.messages ?? []) })),
    ]);
    if (this._State.threadId !== threadId) return;
    this.dispatch({ type: 'loaded', threadId, detail, messages: page.messages ?? [], hasMore: !!page.hasMore });
    await this._Deps.globalState.update(LAST_THREAD_KEY, threadId);
    void markAskThreadRead(threadId).catch(() => undefined);
  }

  private async loadOlder(): Promise<void> {
    const threadId = this._State.threadId;
    const oldest = this._State.messages.find((m) => !m.isLocal);
    if (!threadId || !oldest) return;
    const page = await enumerateAskMessages(threadId, { beforeSequence: oldest.sequence, pageSize: MESSAGE_PAGE_SIZE });
    this.dispatch({ type: 'older', threadId, messages: await withToolCalls(threadId, page?.messages ?? []), hasMore: !!page?.hasMore });
  }

  private async refreshLatest(threadId: string): Promise<void> {
    try {
      const page = await enumerateAskMessages(threadId, { pageSize: MESSAGE_PAGE_SIZE });
      this.dispatch({ type: 'latest', threadId, messages: await withToolCalls(threadId, page?.messages ?? []) });
    } catch {
      /* the socket delivers the messages; this only reconciles */
    }
  }

  private async send(text: string): Promise<void> {
    if (!(await this.ensureConnected())) return;
    this._Error = null;
    let threadId = this._State.threadId;
    if (!threadId) {
      let created = await createAskThread({ captainId: this.draftCaptainId() ?? null, autoApprove: this._DraftAutoApprove });
      if (this._DraftCliPolicy) created = await setAskThreadCliPermissionPolicy(created.id, this._DraftCliPolicy);
      threadId = created.id;
      this.dispatch({ type: 'reset', threadId });
      this.dispatch({ type: 'thread', thread: created });
      await this._Deps.globalState.update(LAST_THREAD_KEY, threadId);
    }
    const localId = `local-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;
    const sequence = this._State.messages.reduce((max, m) => Math.max(max, m.sequence), 0) + 1;
    const optimistic: AskMessage = { id: localId, threadId, sequence, role: 'User', kind: 'Text', contentText: text, createdUtc: new Date().toISOString(), isLocal: true };
    this.dispatch({ type: 'optimisticUser', message: optimistic });
    try {
      const result = await sendAskMessage(threadId, text, true);
      this.dispatch({ type: 'confirmUser', localId, messageId: result?.messageId ?? null, turnId: result?.turnId ?? null });
    } catch (err) {
      this.dispatch({ type: 'dropOptimistic', localId });
      this._Error = `The message could not be sent. ${describeError(err)}`;
      this.render();
    }
  }

  private async decide(kind: 'proposal' | 'cli', id: string, approve: boolean): Promise<void> {
    const threadId = this._State.threadId;
    if (!threadId) return;
    if (kind === 'proposal') {
      const updated = approve ? await approveAskProposal(threadId, id) : await rejectAskProposal(threadId, id);
      if (updated) this.dispatch({ type: 'proposal', proposal: updated });
    } else {
      const updated = await decideCliPermissionRequest(id, { decision: approve ? 'AllowOnce' : 'Deny' });
      if (updated && typeof updated === 'object' && 'id' in updated) this.dispatch({ type: 'cliPermission', request: updated as never });
    }
    void this.refreshLatest(threadId);
  }

  private async openLink(href: string): Promise<void> {
    if (href === 'command:connect') {
      await this._Deps.connection.connect({ interactive: true });
      return;
    }
    const at = href.indexOf('/dashboard/');
    if (at >= 0) {
      const [command, arg] = nativeCommandForRoute(href.slice(at + '/dashboard'.length));
      await vscode.commands.executeCommand(command, arg);
      return;
    }
    if (/^(https?|mailto):/i.test(href)) await vscode.env.openExternal(vscode.Uri.parse(href));
  }

  private onSocket(raw: WebSocketMessage): void {
    const threadId = this._State.threadId;
    const cli = parseCliPermissionEvent(raw);
    if (cli) {
      if (threadId && cli.request.threadId === threadId) this.dispatch({ type: 'cliPermission', request: cli.request });
      return;
    }
    const event = parseAskEvent(raw);
    if (!event || event.threadId !== threadId) return;
    this.dispatch({ type: 'event', event });
    if (event.type === 'ask.turn' && event.state !== 'started') void this.refreshLatest(event.threadId);
  }

  private dispatch(action: ConversationAction): void {
    this._State = conversationReducer(this._State, action);
    this.scheduleRender();
  }

  private scheduleRender(): void {
    if (this._RenderTimer) return;
    this._RenderTimer = setTimeout(() => {
      this._RenderTimer = undefined;
      this.render();
    }, 60);
  }

  private render(): void {
    if (!this._Hosts.size) return;
    const thread = this._State.thread;
    const controls = {
      captains: this._Captains.map((c) => ({ id: c.id, label: `${c.name}${c.model || c.runtime ? ` (${c.model || c.runtime})` : ''}` })),
      captainId: thread ? thread.captainId ?? '' : this.draftCaptainId() ?? '',
      autoApprove: thread ? thread.autoApprove === true : this._DraftAutoApprove,
      cliPolicy: (thread ? thread.cliPermissionPolicy ?? null : this._DraftCliPolicy) ?? '',
      cliEffective: thread?.cliPermission ? `${policyLabel(thread.cliPermission.effective)} (from ${humanizeSource(thread.cliPermission.source)})` : '',
      canBypass: this._Deps.connection.state.identity?.globalAdmin === true,
    };
    const message = { type: 'state', view: buildChatView(this._State, this._Deps.connection.connected, this._Error), controls };
    for (const host of this._Hosts) void host.postMessage(message);
  }

  private draftCaptainId(): string | undefined {
    if (this._DraftCaptainId !== undefined) return this._DraftCaptainId || undefined;
    const stored = this._Deps.globalState.get<string>(CAPTAIN_KEY);
    if (stored && this._Captains.some((c) => c.id === stored)) return stored;
    return this._Captains[0]?.id;
  }

  private async loadCaptains(): Promise<void> {
    try {
      this._Captains = ((await listCaptains({ pageNumber: 1, pageSize: 500 })).objects ?? []).sort((a, b) => a.name.localeCompare(b.name));
    } catch {
      this._Captains = [];
    }
    this.render();
  }

  private async setCaptain(captainId: string | null): Promise<void> {
    const thread = this._State.thread;
    if (thread) {
      this.dispatch({ type: 'thread', thread: await updateAskThread(thread.id, { captainId }) });
    } else {
      this._DraftCaptainId = captainId ?? '';
      this.render();
    }
    await this._Deps.globalState.update(CAPTAIN_KEY, captainId ?? undefined);
  }

  private async setAutoApprove(value: boolean): Promise<void> {
    if (value && (await vscode.window.showWarningMessage(AUTO_APPROVE_WARNING, { modal: true }, 'Turn On Auto-approve')) !== 'Turn On Auto-approve') {
      this.render();
      return;
    }
    const thread = this._State.thread;
    if (thread) this.dispatch({ type: 'thread', thread: await updateAskThread(thread.id, { autoApprove: value }) });
    else {
      this._DraftAutoApprove = value;
      this.render();
    }
  }

  private async setCliPolicy(policy: CliPermissionPolicy | null): Promise<void> {
    const thread = this._State.thread;
    if (thread) this.dispatch({ type: 'thread', thread: await setAskThreadCliPermissionPolicy(thread.id, policy) });
    else {
      this._DraftCliPolicy = policy;
      this.render();
    }
  }
}

function policyLabel(policy: string): string {
  return policy === 'ApproveInArmada' ? 'Ask in Armada' : policy === 'Bypass' ? 'Bypass' : policy === 'Refuse' ? 'Refuse' : policy;
}

function humanizeSource(source: string): string {
  return source.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase();
}

function isCliPolicy(value: unknown): value is CliPermissionPolicy {
  return value === 'Refuse' || value === 'ApproveInArmada' || value === 'Bypass';
}

/** The chat page: header, messages, composer. Renders the ChatView the host posts. */
function chatHtml(nonce: string, inEditor: boolean): string {
  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}'">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<style>
html, body { height: 100%; margin: 0; padding: 0; }
body { font-family: var(--vscode-font-family); font-size: var(--vscode-font-size); color: var(--vscode-foreground); display: flex; flex-direction: column; }
header { display: flex; align-items: center; gap: 4px; padding: 4px 8px; border-bottom: 1px solid var(--vscode-sideBarSectionHeader-border, var(--vscode-panel-border)); }
header .title { flex: 1; font-weight: 600; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.icon { background: none; border: none; color: var(--vscode-icon-foreground); cursor: pointer; padding: 3px 6px; border-radius: 4px; font: inherit; }
.icon:hover { background: var(--vscode-toolbar-hoverBackground); }
.icon:focus-visible, button:focus-visible, textarea:focus { outline: 1px solid var(--vscode-focusBorder); outline-offset: -1px; }
.controls { display: flex; flex-wrap: wrap; align-items: center; gap: 4px 12px; padding: 5px 8px; border-bottom: 1px solid var(--vscode-sideBarSectionHeader-border, var(--vscode-panel-border)); font-size: 0.9em; color: var(--vscode-descriptionForeground); }
.controls label { display: inline-flex; align-items: center; gap: 5px; }
.controls select { max-width: 220px; color: var(--vscode-dropdown-foreground); background: var(--vscode-dropdown-background); border: 1px solid var(--vscode-dropdown-border, var(--vscode-panel-border)); border-radius: 2px; padding: 1px 4px; font: inherit; }
.controls .auto.on { color: var(--vscode-charts-yellow); }
.controls .effective { font-size: 0.9em; }
#messages { flex: 1; overflow-y: auto; padding: 8px 10px 4px; }
.msg { margin: 0 0 12px; line-height: 1.5; overflow-wrap: anywhere; }
.msg .meta { font-size: 0.8em; color: var(--vscode-descriptionForeground); margin-bottom: 2px; }
.msg.user .body { background: var(--vscode-input-background); border: 1px solid var(--vscode-input-border, transparent); border-radius: 6px; padding: 6px 9px; }
.msg.system .body, .msg.kind-WorkUpdate .body, .msg.kind-WorkReport .body { border-left: 2px solid var(--vscode-charts-blue); padding-left: 8px; color: var(--vscode-descriptionForeground); }
.msg.kind-Error .body { border-left: 2px solid var(--vscode-errorForeground); padding-left: 8px; }
.body p { margin: 0 0 6px; } .body p:last-child { margin-bottom: 0; }
.body h3, .body h4, .body h5, .body h6 { margin: 8px 0 4px; font-size: 1em; }
.body ul, .body ol { margin: 2px 0 6px; padding-left: 20px; }
.body code { font-family: var(--vscode-editor-font-family); font-size: 0.92em; background: var(--vscode-textCodeBlock-background); padding: 0 3px; border-radius: 3px; }
.body pre { background: var(--vscode-textCodeBlock-background); padding: 6px 8px; overflow-x: auto; border-radius: 4px; }
.body pre code { background: none; padding: 0; }
.body blockquote { margin: 4px 0; padding-left: 8px; border-left: 2px solid var(--vscode-textBlockQuote-border); color: var(--vscode-descriptionForeground); }
.body table { border-collapse: collapse; margin: 4px 0; } .body th, .body td { border: 1px solid var(--vscode-panel-border); padding: 2px 6px; }
a { color: var(--vscode-textLink-foreground); }
.tools { display: flex; flex-wrap: wrap; gap: 4px; margin: 4px 0; }
.chip { font-size: 0.8em; border: 1px solid var(--vscode-panel-border); border-radius: 10px; padding: 0 7px; color: var(--vscode-descriptionForeground); cursor: default; }
.chip.running { color: var(--vscode-charts-blue); border-color: var(--vscode-charts-blue); }
.chip.failed { color: var(--vscode-errorForeground); border-color: var(--vscode-errorForeground); }
.card { border: 1px solid var(--vscode-panel-border); border-radius: 6px; padding: 8px; margin-top: 6px; background: var(--vscode-editorWidget-background); }
.card .ct { font-weight: 600; } .card pre { white-space: pre-wrap; font-size: 0.85em; max-height: 140px; overflow: auto; margin: 6px 0 0; }
.card .st { font-size: 0.8em; color: var(--vscode-descriptionForeground); }
.card .actions { display: flex; gap: 6px; margin-top: 8px; }
button.btn { font: inherit; color: var(--vscode-button-foreground); background: var(--vscode-button-background); border: 1px solid var(--vscode-button-border, transparent); border-radius: 2px; padding: 3px 10px; cursor: pointer; }
button.btn:hover { background: var(--vscode-button-hoverBackground); }
button.btn.secondary { color: var(--vscode-button-secondaryForeground); background: var(--vscode-button-secondaryBackground); }
.thinking { font-size: 0.85em; color: var(--vscode-descriptionForeground); font-style: italic; white-space: pre-wrap; max-height: 80px; overflow: hidden; }
.working { color: var(--vscode-descriptionForeground); font-size: 0.85em; }
.working::after { content: ' ●'; animation: pulse 1.2s infinite; } @keyframes pulse { 50% { opacity: 0.2; } }
.error { color: var(--vscode-errorForeground); font-size: 0.9em; padding: 4px 10px; }
.empty { color: var(--vscode-descriptionForeground); padding: 12px 4px; }
.empty h2 { font-size: 1.05em; color: var(--vscode-foreground); margin: 0 0 6px; }
.suggestion { display: block; width: 100%; text-align: left; margin: 6px 0; font: inherit; color: var(--vscode-foreground); background: var(--vscode-editorWidget-background); border: 1px solid var(--vscode-panel-border); border-radius: 6px; padding: 6px 9px; cursor: pointer; }
.suggestion:hover { border-color: var(--vscode-focusBorder); }
.older { display: block; margin: 0 auto 10px; }
footer { border-top: 1px solid var(--vscode-sideBarSectionHeader-border, var(--vscode-panel-border)); padding: 8px; }
.composer { display: flex; flex-direction: column; border: 1px solid var(--vscode-input-border, var(--vscode-panel-border)); background: var(--vscode-input-background); border-radius: 6px; }
.composer:focus-within { border-color: var(--vscode-focusBorder); }
textarea { resize: none; border: none; background: transparent; color: var(--vscode-input-foreground); font: inherit; padding: 7px 9px; min-height: 38px; max-height: 200px; outline: none; }
.composer .row { display: flex; justify-content: space-between; align-items: center; padding: 2px 6px 6px 9px; }
.hint { font-size: 0.78em; color: var(--vscode-descriptionForeground); }
</style>
</head>
<body>
<header>
  <span class="title" id="title">Ask Armada</span>
  ${inEditor ? '' : '<button class="icon" id="openInEditor" title="Open in an editor tab" aria-label="Open in an editor tab">⧉</button>'}
  <button class="icon" id="quick" title="Quick actions" aria-label="Quick actions">⚡</button>
  <button class="icon" id="history" title="Earlier conversations" aria-label="Earlier conversations">⟲</button>
  <button class="icon" id="new" title="New conversation" aria-label="New conversation">＋</button>
</header>
<div class="controls" role="group" aria-label="Conversation settings">
  <label title="The captain (AI agent) that answers in this conversation. Changing it applies to the next message; no captain means only quick actions work.">Captain <select id="captain" aria-label="Captain"></select></label>
  <label class="auto" title="${AUTO_APPROVE_WARNING}"><input type="checkbox" id="auto" role="switch"> Auto-approve</label>
  <label title="How the captain CLI handles shell commands, file edits, and fetches that need permission in this conversation. Inherit uses the captain policy, then the server default.">CLI tools <select id="cli" aria-label="CLI tools"><option value="">Inherit</option><option value="ApproveInArmada">Ask in Armada</option><option value="Refuse">Refuse</option><option value="Bypass">Bypass (no prompts)</option></select></label>
  <span class="effective" id="effective"></span>
</div>
<div id="messages" role="log" aria-live="polite"></div>
<div class="error" id="error" role="alert" hidden></div>
<footer>
  <div class="composer">
    <textarea id="input" rows="1" placeholder="Ask Armada anything about your fleet, missions and failures" aria-label="Message"></textarea>
    <div class="row"><span class="hint" id="hint">Enter to send, Shift+Enter for a new line</span><span><button class="btn secondary" id="stop" hidden>Stop</button> <button class="btn" id="send">Send</button></span></div>
  </div>
</footer>
<script nonce="${nonce}">
const vscode = acquireVsCodeApi();
const $ = (id) => document.getElementById(id);
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c]);
const SUGGESTIONS = ['What needs my attention right now?', 'Summarize what my captains worked on today', 'Why did the most recent mission fail?', 'Which vessels have the most failures this week?'];
let view = null;
let draftKept = (vscode.getState() || {}).draft || '';
$('input').value = draftKept;

function chips(tools) {
  if (!tools || !tools.length) return '';
  return '<div class="tools">' + tools.map((t) => '<span class="chip ' + t.status + '" title="' + esc(t.detail) + '">' + (t.status === 'running' ? '⟳ ' : t.status === 'failed' ? '✗ ' : '✓ ') + esc(t.name) + '</span>').join('') + '</div>';
}
function card(c) {
  if (!c) return '';
  const actions = c.pending ? '<div class="actions"><button class="btn" data-decide="' + c.kind + '" data-id="' + esc(c.id) + '" data-approve="1">' + (c.kind === 'cli' ? 'Allow once' : 'Approve') + '</button><button class="btn secondary" data-decide="' + c.kind + '" data-id="' + esc(c.id) + '" data-approve="0">' + (c.kind === 'cli' ? 'Deny' : 'Reject') + '</button></div>' : '';
  return '<div class="card"><div class="ct">' + (c.kind === 'cli' ? 'Run a tool: ' : 'Proposed action: ') + esc(c.title) + '</div><div class="st">' + esc(c.status) + '</div>' + (c.detail ? '<pre>' + esc(c.detail) + '</pre>' : '') + actions + '</div>';
}
function render() {
  if (!view) return;
  $('title').textContent = view.title;
  const box = $('messages');
  const atBottom = box.scrollTop + box.clientHeight >= box.scrollHeight - 30;
  let html = '';
  if (!view.connected) {
    html = '<div class="empty"><h2>Not connected</h2>Connect to an Armada Admiral to ask questions.<br><br><button class="btn" id="connect">Connect</button></div>';
  } else if (!view.messages.length && !view.streaming) {
    html = '<div class="empty"><h2>Ask Armada</h2>Ask about your fleet, missions, captains and failures, or ask it to dispatch work. It proposes actions for you to approve.' + SUGGESTIONS.map((s) => '<button class="suggestion" data-suggest="' + esc(s) + '">' + esc(s) + '</button>').join('') + '</div>';
  } else {
    if (view.hasMore) html += '<button class="btn secondary older" id="older">Load earlier messages</button>';
    for (const m of view.messages) {
      const who = m.role === 'user' ? 'You' : m.role === 'system' ? 'Armada' : 'Armada';
      html += '<div class="msg ' + m.role + ' kind-' + esc(m.kind) + '"><div class="meta">' + who + (m.time ? ' · ' + esc(m.time) : '') + (m.local ? ' · sending' : '') + '</div>' + chips(m.tools) + (m.html ? '<div class="body">' + m.html + '</div>' : '') + card(m.card) + '</div>';
    }
    if (view.streaming) {
      html += '<div class="msg assistant"><div class="meta">Armada</div>' + (view.streaming.thinking && !view.streaming.html ? '<div class="thinking">' + esc(view.streaming.thinking) + '</div>' : '') + chips(view.streaming.tools) + (view.streaming.html ? '<div class="body">' + view.streaming.html + '</div>' : '') + '</div>';
    }
    if (view.turnActive) html += '<div class="working">Armada is working</div>';
  }
  box.innerHTML = html;
  if (atBottom || view.turnActive) box.scrollTop = box.scrollHeight;
  $('error').hidden = !view.error;
  $('error').textContent = view.error || '';
  $('stop').hidden = !view.turnActive;
  $('send').disabled = !view.connected;
}
function send() {
  const text = $('input').value.trim();
  if (!text || (view && view.turnActive)) return;
  vscode.postMessage({ type: 'send', text });
  $('input').value = '';
  vscode.setState({ draft: '' });
  grow();
}
function grow() { const t = $('input'); t.style.height = 'auto'; t.style.height = Math.min(t.scrollHeight, 200) + 'px'; }
$('input').addEventListener('input', () => { grow(); vscode.setState({ draft: $('input').value }); });
$('input').addEventListener('keydown', (e) => { if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) { e.preventDefault(); send(); } });
$('send').addEventListener('click', send);
$('stop').addEventListener('click', () => vscode.postMessage({ type: 'stop' }));
$('new').addEventListener('click', () => vscode.postMessage({ type: 'new' }));
$('history').addEventListener('click', () => vscode.postMessage({ type: 'history' }));
$('quick').addEventListener('click', () => vscode.postMessage({ type: 'quick' }));
document.addEventListener('click', (e) => {
  const el = e.target instanceof Element ? e.target : null;
  if (!el) return;
  const link = el.closest('a[href]');
  if (link) { e.preventDefault(); vscode.postMessage({ type: 'link', href: link.getAttribute('href') }); return; }
  const decide = el.closest('[data-decide]');
  if (decide) { decide.disabled = true; vscode.postMessage({ type: 'decide', kind: decide.getAttribute('data-decide'), id: decide.getAttribute('data-id'), approve: decide.getAttribute('data-approve') === '1' }); return; }
  const suggest = el.closest('[data-suggest]');
  if (suggest) { $('input').value = suggest.getAttribute('data-suggest'); send(); return; }
  if (el.id === 'older') { vscode.postMessage({ type: 'older' }); return; }
  if (el.id === 'connect') { vscode.postMessage({ type: 'link', href: 'command:connect' }); }
});
let captainOptions = '';
function renderControls(c) {
  if (!c) return;
  const options = '<option value="">No captain (quick actions only)</option>' + c.captains.map((x) => '<option value="' + esc(x.id) + '">' + esc(x.label) + '</option>').join('');
  if (options !== captainOptions) { $('captain').innerHTML = options; captainOptions = options; }
  $('captain').value = c.captainId || '';
  $('captain').disabled = !!(view && view.turnActive);
  $('auto').checked = !!c.autoApprove;
  $('auto').parentElement.classList.toggle('on', !!c.autoApprove);
  const bypass = $('cli').querySelector('option[value="Bypass"]');
  bypass.disabled = !c.canBypass && c.cliPolicy !== 'Bypass';
  $('cli').value = c.cliPolicy || '';
  $('effective').textContent = c.cliEffective ? 'In force: ' + c.cliEffective : '';
}
$('captain').addEventListener('change', () => vscode.postMessage({ type: 'setCaptain', id: $('captain').value }));
$('auto').addEventListener('change', () => vscode.postMessage({ type: 'setAutoApprove', value: $('auto').checked }));
$('cli').addEventListener('change', () => vscode.postMessage({ type: 'setCliPolicy', value: $('cli').value }));
if ($('openInEditor')) $('openInEditor').addEventListener('click', () => vscode.postMessage({ type: 'openInEditor' }));
window.addEventListener('message', (event) => {
  if (event.data && event.data.type === 'state') { view = event.data.view; render(); renderControls(event.data.controls); }
});
grow();
vscode.postMessage({ type: 'ready' });
</script>
</body>
</html>`;
}
