/*
 * Detail pages in editor tabs, one per entity (opening it again reveals the tab). Each page reloads when the store
 * reports a change in its domain, and pages of running missions and working captains also follow the log every few
 * seconds; updates replace the page body in place, keeping the scroll position.
 */

import * as crypto from 'crypto';
import * as vscode from 'vscode';
import { getCaptain, getCaptainLog, getMergeEntry, getMission, getMissionLog, getVoyage, listMissionSummaries } from '@dashboard/api/client';
import type { MissionSummary } from '@dashboard/types/models';
import { describeError } from '../../transport/errors';
import { getMissionAttempts } from '../../transport/hostClient';
import { openAsPage } from '../dashboard/pageRouting';
import { entityRoute } from '../dashboard/routes';
import { DetailPage, escapeHtml, isDetailMessage, renderDetailContent, renderDetailHtml } from './detailHtml';
import { captainPage, mergePage, missionPage, voyagePage } from './detailPages';
import { ACTIVE_MISSION_STATUSES, OpsDomain, OpsStore } from './opsStore';

/** The kinds of detail page. */
export type DetailKind = 'mission' | 'voyage' | 'captain' | 'merge';

/** Lines of live log shown on a page. */
const LOG_TAIL_LINES = 60;

/** Domains whose changes reload each kind of page. */
const RELOAD_ON: Record<DetailKind, OpsDomain[]> = {
  mission: ['missions'],
  voyage: ['voyages', 'missions'],
  captain: ['captains', 'missions'],
  merge: ['merge'],
};

interface OpenPage {
  kind: DetailKind;
  id: string;
  panel: vscode.WebviewPanel;
  html: string;
  live: boolean;
  loading: boolean;
}

/** Opens and maintains detail pages. */
export class DetailPanelManager implements vscode.Disposable {
  private readonly _Store: OpsStore;
  private readonly _Pages = new Map<string, OpenPage>();
  private readonly _Subscription: { dispose(): void };
  private readonly _Timer: ReturnType<typeof setInterval>;

  /**
   * @throws Error when store is missing.
   */
  public constructor(store: OpsStore, liveRefreshMs = 4000) {
    if (!store) throw new Error('store is required');
    this._Store = store;
    this._Subscription = store.onDidChange((domain) => {
      for (const page of this._Pages.values()) {
        if (RELOAD_ON[page.kind].includes(domain)) void this.load(page);
      }
    });
    this._Timer = setInterval(() => {
      for (const page of this._Pages.values()) if (page.live && page.panel.visible) void this.load(page);
    }, liveRefreshMs);
  }

  /** Opens (or reveals) the page for an entity. */
  public async open(kind: DetailKind, id: string): Promise<void> {
    if (openAsPage(entityRoute(kind, id))) return;
    const key = `${kind}:${id}`;
    const existing = this._Pages.get(key);
    if (existing) {
      existing.panel.reveal(undefined, false);
      await this.load(existing);
      return;
    }
    const panel = vscode.window.createWebviewPanel('armada.detail', titleFor(kind, id, this._Store), vscode.ViewColumn.Active, { enableScripts: true, enableFindWidget: true });
    const page: OpenPage = { kind, id, panel, html: '', live: false, loading: false };
    this._Pages.set(key, page);
    panel.webview.html = loadingHtml(titleFor(kind, id, this._Store));
    panel.webview.onDidReceiveMessage((message: unknown) => {
      if (isDetailMessage(message)) void vscode.commands.executeCommand(message.command, ...message.args);
    });
    panel.onDidDispose(() => this._Pages.delete(key));
    await this.load(page);
  }

  /** Number of open pages (for the integration tests). */
  public get openCount(): number {
    return this._Pages.size;
  }

  /** Reloads every open page of a kind (after an action). */
  public reload(kind: DetailKind, id: string): void {
    const page = this._Pages.get(`${kind}:${id}`);
    if (page) void this.load(page);
  }

  public dispose(): void {
    clearInterval(this._Timer);
    this._Subscription.dispose();
    for (const page of this._Pages.values()) page.panel.dispose();
    this._Pages.clear();
  }

  private async load(page: OpenPage): Promise<void> {
    if (page.loading) return;
    page.loading = true;
    try {
      const built = await this.build(page);
      page.panel.title = built.title.length > 40 ? `${built.title.slice(0, 39)}…` : built.title;
      const body = renderDetailContent(built);
      if (!page.html) {
        page.panel.webview.html = renderDetailHtml(built, crypto.randomBytes(16).toString('base64'));
      } else if (body !== page.html) {
        void page.panel.webview.postMessage({ type: 'update', html: body });
      }
      page.html = body;
    } catch (err) {
      if (!page.html) page.panel.webview.html = errorHtml(titleFor(page.kind, page.id, this._Store), describeError(err));
    } finally {
      page.loading = false;
    }
  }

  private async build(page: OpenPage): Promise<DetailPage> {
    const now = new Date();
    switch (page.kind) {
      case 'mission': {
        const mission = await getMission(page.id);
        page.live = ACTIVE_MISSION_STATUSES.includes(mission.status);
        const tail = page.live ? await getMissionLog(page.id, LOG_TAIL_LINES).then((r) => r.log, () => undefined) : undefined;
        const history = await getMissionAttempts(page.id).catch(() => undefined);
        return missionPage(mission, this._Store, tail, now, history);
      }
      case 'voyage': {
        const progress = await getVoyage(page.id);
        page.live = false;
        let missions: MissionSummary[] = this._Store.missionsOfVoyage(page.id);
        try {
          missions = (await listMissionSummaries({ pageNumber: 1, pageSize: 250, filters: { voyageId: page.id } })).objects ?? missions;
        } catch {
          /* the store's page is the fallback */
        }
        return voyagePage(progress, missions, this._Store, now);
      }
      case 'captain': {
        const captain = await getCaptain(page.id);
        page.live = captain.state === 'Working';
        const tail = page.live ? await getCaptainLog(page.id, LOG_TAIL_LINES).then((r) => r.log, () => undefined) : undefined;
        const missions = this._Store.missions.filter((m) => m.captainId === captain.id || (captain.activeMissionIds ?? []).includes(m.id));
        return captainPage(captain, missions, this._Store, tail, now);
      }
      case 'merge': {
        const entry = await getMergeEntry(page.id);
        page.live = entry.status === 'Testing';
        return mergePage(entry, this._Store, now);
      }
    }
  }
}

function titleFor(kind: DetailKind, id: string, store: OpsStore): string {
  if (kind === 'mission') return store.missions.find((m) => m.id === id)?.title ?? id;
  if (kind === 'voyage') return store.voyages.find((v) => v.voyage.id === id)?.voyage.title ?? id;
  if (kind === 'captain') return store.captainName(id) || id;
  return id;
}

function shell(title: string, body: string): string {
  return `<!DOCTYPE html><html lang="en"><head><meta charset="UTF-8"><meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'">
<style>body { font-family: var(--vscode-font-family); color: var(--vscode-foreground); padding: 16px 20px; } .muted { color: var(--vscode-descriptionForeground); } .error { color: var(--vscode-errorForeground); }</style>
</head><body><h1 style="font-size:1.35em;font-weight:600">${escapeHtml(title)}</h1>${body}</body></html>`;
}

function loadingHtml(title: string): string {
  return shell(title, '<p class="muted">Loading...</p>');
}

function errorHtml(title: string, message: string): string {
  return shell(title, `<p class="error" role="alert">Could not load this from Armada. ${escapeHtml(message)}</p>`);
}
