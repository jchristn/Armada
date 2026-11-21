/*
 * The Conversations view: Ask Armada's conversation list, as on the dashboard's Ask page. Pinned conversations come
 * first, then the most recent; each row shows its unread count and age, its summary on hover, and the one open in the
 * chat is marked. Rows open in the chat on click; their menus rename, pin, archive, mark read or unread, summarize
 * and delete (several at once with multi-select). The title bar starts a new conversation, searches, and shows or
 * hides archived conversations.
 */

import * as vscode from 'vscode';
import {
  deleteAskThreads,
  enumerateAskThreads,
  markAskThreadRead,
  markAskThreadUnread,
  summarizeAskThread,
  updateAskThread,
} from '@dashboard/api/client';
import type { AskThread } from '@dashboard/types/models';
import { formatAge } from '../../util/format';

/** View id (package.json contributes.views.armada). */
export const CONVERSATIONS_VIEW_ID = 'armadaConversations';

/** How many conversations one load lists. */
const PAGE_SIZE = 100;

/** What the view needs from the chat. */
export interface ConversationsChat {
  readonly threadId: string | null;
  readonly onDidChangeThread: vscode.Event<string | null>;
  openConversation(threadId: string): Promise<void>;
  newConversation(): Promise<void>;
  threadUpdated(thread: AskThread): void;
  threadsDeleted(ids: string[]): Promise<void>;
}

/** The tree item for a conversation (pure, for tests). */
export function conversationItem(thread: AskThread, openId: string | null, now: Date = new Date()): vscode.TreeItem {
  const item = new vscode.TreeItem(thread.title || 'Conversation', vscode.TreeItemCollapsibleState.None);
  item.id = `conversation:${thread.id}`;
  const open = thread.id === openId;
  item.iconPath = new vscode.ThemeIcon(open ? 'comment-draft' : thread.pinned ? 'pinned' : thread.archived ? 'archive' : 'comment-discussion');
  const unread = thread.unreadCount ?? 0;
  item.description = [unread ? `${unread} unread` : '', open ? 'open' : '', formatAge(thread.lastMessageUtc ?? thread.createdUtc, now)].filter(Boolean).join(' · ');
  const tooltip = new vscode.MarkdownString(undefined, true);
  tooltip.appendMarkdown(`**${escapeMarkdown(thread.title || 'Conversation')}**\n\n`);
  if (thread.summaryText) tooltip.appendMarkdown(`${escapeMarkdown(thread.summaryText)}\n\n`);
  tooltip.appendMarkdown([`${thread.messageCount ?? 0} messages`, thread.pinned ? 'pinned' : '', thread.archived ? 'archived' : ''].filter(Boolean).join(' · '));
  item.tooltip = tooltip;
  item.command = { command: 'armada.conversations.open', title: 'Open', arguments: [thread.id] };
  item.contextValue = [
    'armadaConversation',
    thread.pinned ? 'armadaConversation.pinned' : 'armadaConversation.unpinned',
    thread.archived ? 'armadaConversation.archived' : 'armadaConversation.active',
    unread ? 'armadaConversation.unread' : 'armadaConversation.read',
  ].join(' ');
  return item;
}

function escapeMarkdown(text: string): string {
  return text.replace(/[\\`*_{}[\]()#+\-.!|<>]/g, (c) => `\\${c}`);
}

/** The Conversations tree. */
export class ConversationsTreeProvider implements vscode.TreeDataProvider<AskThread>, vscode.Disposable {
  private readonly _Chat: ConversationsChat;
  private readonly _IsConnected: () => boolean;
  private readonly _OnDidChange = new vscode.EventEmitter<AskThread | undefined>();
  private readonly _Subscriptions: vscode.Disposable[] = [];
  private _Threads: AskThread[] = [];
  private _HasMore = false;
  private _Loading: Promise<void> | undefined;
  private _Loaded = false;
  private _Error: string | undefined;
  private _Search = '';
  private _IncludeArchived = false;

  public readonly onDidChangeTreeData: vscode.Event<AskThread | undefined> = this._OnDidChange.event;

  /**
   * @throws Error when chat is missing.
   */
  public constructor(chat: ConversationsChat, isConnected: () => boolean) {
    if (!chat) throw new Error('chat is required');
    this._Chat = chat;
    this._IsConnected = isConnected;
    this._Subscriptions.push(chat.onDidChangeThread(() => this._OnDidChange.fire(undefined)));
  }

  /** The search text, or empty. */
  public get search(): string {
    return this._Search;
  }

  /** Whether archived conversations are listed. */
  public get includeArchived(): boolean {
    return this._IncludeArchived;
  }

  /** Whether more conversations exist than are listed. */
  public get hasMore(): boolean {
    return this._HasMore;
  }

  /** The last load error, if any. */
  public get error(): string | undefined {
    return this._Error;
  }

  /** The listed conversations. */
  public get threads(): AskThread[] {
    return this._Threads;
  }

  /** Lists conversations matching this text (empty lists all). */
  public async setSearch(search: string): Promise<void> {
    this._Search = search.trim();
    await this.reload();
  }

  /** Lists or hides archived conversations. */
  public async setIncludeArchived(value: boolean): Promise<void> {
    this._IncludeArchived = value;
    await this.reload();
  }

  /** Reloads the list from the Admiral. */
  public reload(): Promise<void> {
    if (!this._IsConnected()) {
      this._Threads = [];
      this._Loaded = false;
      this._OnDidChange.fire(undefined);
      return Promise.resolve();
    }
    if (this._Loading) return this._Loading;
    this._Loading = (async () => {
      try {
        const page = await enumerateAskThreads({ pageNumber: 1, pageSize: PAGE_SIZE, search: this._Search || undefined, includeArchived: this._IncludeArchived });
        this._Threads = page.objects ?? [];
        this._HasMore = (page.totalRecords ?? this._Threads.length) > this._Threads.length;
        this._Error = undefined;
      } catch (e) {
        this._Error = e instanceof Error ? e.message : String(e);
      } finally {
        this._Loaded = true;
        this._Loading = undefined;
        this._OnDidChange.fire(undefined);
      }
    })();
    return this._Loading;
  }

  /** Replaces one conversation in the list (a live ask.thread event or an edit made here). */
  public upsert(thread: AskThread): void {
    const index = this._Threads.findIndex((t) => t.id === thread.id);
    const hidden = thread.archived && !this._IncludeArchived;
    if (index < 0) {
      if (!hidden) void this.reload();
      return;
    }
    if (hidden) this._Threads.splice(index, 1);
    else this._Threads[index] = { ...this._Threads[index], ...thread };
    this._OnDidChange.fire(undefined);
  }

  /** Removes conversations from the list. */
  public remove(ids: string[]): void {
    this._Threads = this._Threads.filter((t) => !ids.includes(t.id));
    this._OnDidChange.fire(undefined);
  }

  public getChildren(element?: AskThread): AskThread[] {
    if (element || !this._IsConnected()) return [];
    if (!this._Loaded) void this.reload();
    return this._Threads;
  }

  public getTreeItem(thread: AskThread): vscode.TreeItem {
    return conversationItem(thread, this._Chat.threadId);
  }

  public dispose(): void {
    for (const s of this._Subscriptions) s.dispose();
    this._OnDidChange.dispose();
  }
}

/** The view, its title-bar state and the conversation commands. */
export function registerConversations(chat: ConversationsChat, isConnected: () => boolean, onConnectionChange: vscode.Event<unknown>, onThreadEvent: (listener: (thread: AskThread | null, threadId: string) => void) => vscode.Disposable): vscode.Disposable[] {
  const tree = new ConversationsTreeProvider(chat, isConnected);
  const view = vscode.window.createTreeView(CONVERSATIONS_VIEW_ID, { treeDataProvider: tree, canSelectMany: true });
  const updateView = (): void => {
    view.description = tree.search ? `"${tree.search}"` : tree.includeArchived ? 'with archived' : undefined;
    view.message = tree.error
      ? `Could not load conversations: ${tree.error}`
      : tree.hasMore ? `Showing the ${tree.threads.length} most recent. Search to find older conversations.` : tree.search && !tree.threads.length ? `No conversations match "${tree.search}".` : undefined;
    void vscode.commands.executeCommand('setContext', 'armada.conversations.filtered', !!tree.search);
    void vscode.commands.executeCommand('setContext', 'armada.conversations.archived', tree.includeArchived);
  };
  const disposables: vscode.Disposable[] = [tree, view, tree.onDidChangeTreeData(updateView)];
  updateView();

  // The selected rows when a menu command runs on a multi-selection, else the row it ran on.
  const targets = (item: unknown, selected: unknown): AskThread[] => {
    if (Array.isArray(selected) && selected.length) return selected as AskThread[];
    if (item && typeof item === 'object' && typeof (item as AskThread).id === 'string') return [item as AskThread];
    return view.selection.length ? [...view.selection] : [];
  };
  const run = async (label: string, work: () => Promise<void>): Promise<void> => {
    try {
      await work();
    } catch (e) {
      void vscode.window.showErrorMessage(`Armada: ${label} failed: ${e instanceof Error ? e.message : String(e)}`);
    }
  };
  const update = async (thread: AskThread, patch: Parameters<typeof updateAskThread>[1]): Promise<void> => {
    const updated = { ...thread, ...patch, ...(await updateAskThread(thread.id, patch)) } as AskThread;
    tree.upsert(updated);
    chat.threadUpdated(updated);
  };
  const register = (id: string, handler: (...args: unknown[]) => unknown): void => {
    disposables.push(vscode.commands.registerCommand(id, handler));
  };

  register('armada.conversations.open', async (arg) => {
    const id = typeof arg === 'string' ? arg : (arg as AskThread | undefined)?.id;
    if (!id) return;
    await chat.openConversation(id);
    const thread = tree.threads.find((t) => t.id === id);
    if (thread?.unreadCount) tree.upsert({ ...thread, unreadCount: 0 });
  });
  register('armada.conversations.new', () => chat.newConversation());
  register('armada.conversations.refresh', () => tree.reload());
  register('armada.conversations.search', async () => {
    const search = await vscode.window.showInputBox({ title: 'Ask Armada: search conversations', prompt: 'Titles, summaries and messages (empty lists every conversation)', value: tree.search });
    if (search !== undefined) await tree.setSearch(search);
  });
  register('armada.conversations.clearSearch', () => tree.setSearch(''));
  register('armada.conversations.showArchived', () => tree.setIncludeArchived(true));
  register('armada.conversations.hideArchived', () => tree.setIncludeArchived(false));
  register('armada.conversations.rename', (item) => run('Rename', async () => {
    const thread = targets(item, undefined)[0];
    if (!thread) return;
    const title = await vscode.window.showInputBox({ title: 'Rename conversation', value: thread.title || '', validateInput: (v) => (v.trim() ? undefined : 'Enter a title') });
    if (title !== undefined) await update(thread, { title: title.trim() });
  }));
  for (const [id, label, patch] of [
    ['armada.conversations.pin', 'Pin', { pinned: true }],
    ['armada.conversations.unpin', 'Unpin', { pinned: false }],
    ['armada.conversations.archive', 'Archive', { archived: true }],
    ['armada.conversations.unarchive', 'Unarchive', { archived: false }],
  ] as Array<[string, string, Parameters<typeof updateAskThread>[1]]>) {
    register(id, (item, selected) => run(label, async () => {
      for (const thread of targets(item, selected)) await update(thread, patch);
    }));
  }
  register('armada.conversations.markRead', (item, selected) => run('Mark read', async () => {
    for (const thread of targets(item, selected)) {
      await markAskThreadRead(thread.id);
      tree.upsert({ ...thread, unreadCount: 0 });
    }
  }));
  register('armada.conversations.markUnread', (item, selected) => run('Mark unread', async () => {
    for (const thread of targets(item, selected)) tree.upsert({ ...thread, ...(await markAskThreadUnread(thread.id)) });
  }));
  register('armada.conversations.summarize', (item) => run('Summarize', async () => {
    const thread = targets(item, undefined)[0];
    if (!thread) return;
    await summarizeAskThread(thread.id);
    void vscode.window.showInformationMessage(`Armada: summarizing "${thread.title || 'Conversation'}". The summary shows when you hover over it in Conversations.`);
  }));
  register('armada.conversations.delete', (item, selected) => run('Delete', async () => {
    const threads = targets(item, selected);
    if (!threads.length) return;
    const what = threads.length === 1 ? `the conversation "${threads[0].title || 'Conversation'}"` : `${threads.length} conversations`;
    if ((await vscode.window.showWarningMessage(`Delete ${what}? This cannot be undone.`, { modal: true }, 'Delete')) !== 'Delete') return;
    const ids = threads.map((t) => t.id);
    await deleteAskThreads(ids);
    tree.remove(ids);
    await chat.threadsDeleted(ids);
  }));

  disposables.push(onConnectionChange(() => void tree.reload()));
  // A finished turn changes the conversation's last message, unread count and order: reload, at most twice a second.
  let reloadTimer: ReturnType<typeof setTimeout> | undefined;
  disposables.push({ dispose: () => reloadTimer && clearTimeout(reloadTimer) });
  disposables.push(onThreadEvent((thread) => {
    if (thread) {
      tree.upsert(thread);
      return;
    }
    if (reloadTimer) return;
    reloadTimer = setTimeout(() => {
      reloadTimer = undefined;
      void tree.reload();
    }, 500);
  }));
  return disposables;
}
