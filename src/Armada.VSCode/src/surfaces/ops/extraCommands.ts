/*
 * The rest of the dashboard's surfaces, natively: the Armada home overview (a live page), starting a planning
 * session, importing vessels, and the API explorer (pick an operation from the Admiral's OpenAPI document, fill its
 * parameters and body in the editor, and see the response as a document).
 */

import * as crypto from 'crypto';
import * as vscode from 'vscode';
import * as api from '@dashboard/api/client';
import { buildExplorerOperations, ExplorerOperation, OpenApiSpec, schemaExample } from '@dashboard/lib/apiExplorer';
import { ConnectionManager } from '../../connection/connectionManager';
import { describeError } from '../../transport/errors';
import { credentialHeaders } from '../../transport/hostClient';
import { formatAge } from '../../util/format';
import { DetailListItem, DetailPage, isDetailMessage, renderDetailContent, renderDetailHtml } from './detailHtml';
import { statusTone } from './detailPages';
import { showForm } from './formPanel';
import { ACTIVE_MISSION_STATUSES, OpsStore, REVIEW_MISSION_STATUSES } from './opsStore';
import { elapsed, humanize } from './presentation';

/** Host dependencies. */
export interface ExtraCommandDeps {
  store: OpsStore;
  connection: ConnectionManager;
  openPlanning(sessionId: string): Promise<void>;
}

/** Builds the home overview from the live store. Pure, for tests. */
export function homePage(store: OpsStore, connection: { baseUrl?: string; version?: string | null; email?: string | null }, now: Date = new Date()): DetailPage {
  const active = store.missionsIn(ACTIVE_MISSION_STATUSES);
  const review = store.missionsIn(REVIEW_MISSION_STATUSES);
  const failed = store.missions.filter((m) => m.status === 'Failed' || m.status === 'LandingFailed').slice(0, 10);
  const working = store.captains.filter((c) => c.state === 'Working').length;
  const voyages = store.voyages.filter((v) => v.voyage.status === 'Open' || v.voyage.status === 'InProgress');
  const missionRow = (m: (typeof store.missions)[number]): DetailListItem => ({
    label: m.title || m.id,
    description: [store.vesselName(m.vesselId), store.captainName(m.captainId), ACTIVE_MISSION_STATUSES.includes(m.status) ? elapsed(m.startedUtc ?? m.createdUtc, null, now) : formatAge(m.completedUtc ?? m.lastUpdateUtc, now)].filter(Boolean).join(' · '),
    status: { label: humanize(m.status), tone: statusTone(m.status) },
    link: { command: 'armada.mission.open', args: [m.id] },
  });
  return {
    title: 'Armada',
    status: { label: 'Connected', tone: 'ok' },
    subtitle: connection.baseUrl,
    actions: [
      { label: 'Dispatch Mission', command: 'armada.dispatch', primary: true },
      { label: 'Dispatch Voyage', command: 'armada.dispatchVoyage' },
      { label: 'New Planning Session', command: 'armada.planning.new' },
      { label: 'Ask Armada', command: 'armada.ask.open' },
    ],
    sections: [
      {
        kind: 'fields',
        rows: [
          { label: 'Armada', value: connection.version ?? '' },
          { label: 'Signed in as', value: connection.email ?? '' },
          { label: 'Active missions', value: String(active.length) },
          { label: 'Waiting for review', value: String(review.length) },
          { label: 'Needs you', value: String(store.inbox.length) },
          { label: 'Captains', value: `${working} working, ${store.captains.length - working} not working, ${store.captains.length} total` },
          { label: 'Vessels', value: `${store.vessels.length} in ${store.fleets.length} ${store.fleets.length === 1 ? 'fleet' : 'fleets'}` },
          { label: 'Merge queue', value: String(store.mergeQueue.filter((e) => e.status === 'Queued' || e.status === 'Testing').length) },
        ],
      },
      { kind: 'list', title: `Needs you (${store.inbox.length})`, items: store.inbox.slice(0, 15).map((item) => ({ label: item.title, description: item.detail, status: { label: humanize(item.kind), tone: statusTone(String(item.severity) === 'Critical' ? 'Failed' : 'Review') }, link: { command: 'armada.inbox.open', args: [item] } })), empty: 'Nothing needs you right now.' },
      { kind: 'list', title: `Active missions (${active.length})`, items: active.map(missionRow), empty: 'No mission is running.' },
      { kind: 'list', title: `Active voyages (${voyages.length})`, items: voyages.map((v) => ({ label: v.voyage.title, description: `${v.completedMissions}/${v.totalMissions} complete${v.failedMissions ? `, ${v.failedMissions} failed` : ''}`, status: { label: humanize(v.voyage.status), tone: statusTone(v.voyage.status) }, link: { command: 'armada.voyage.open', args: [v.voyage.id] } })), empty: 'No voyage is running.' },
      { kind: 'list', title: 'Recent failures', items: failed.map(missionRow), empty: 'No recent failures.' },
    ],
  };
}

/** Registers the commands; returns their disposables. */
export function registerExtraCommands(deps: ExtraCommandDeps): vscode.Disposable[] {
  const { store, connection } = deps;
  const disposables: vscode.Disposable[] = [];
  const register = (id: string, fn: (...args: unknown[]) => Promise<unknown>): void => {
    disposables.push(vscode.commands.registerCommand(id, async (...args: unknown[]) => {
      try {
        return await fn(...args);
      } catch (err) {
        void vscode.window.showErrorMessage(`Armada: ${describeError(err)}`);
        return undefined;
      }
    }));
  };
  const requireConnected = async (): Promise<boolean> => connection.connected || connection.connect({ interactive: true });

  let charts: vscode.WebviewPanel | undefined;
  disposables.push({ dispose: () => charts?.dispose() });

  // Home
  let home: { panel: vscode.WebviewPanel; html: string } | undefined;
  const renderHome = (): void => {
    if (!home) return;
    const state = connection.state;
    const page = homePage(store, { baseUrl: state.target?.baseUrl, version: state.serverVersion, email: state.identity?.email });
    const body = renderDetailContent(page);
    if (!home.html) home.panel.webview.html = renderDetailHtml(page, crypto.randomBytes(16).toString('base64'));
    else if (body !== home.html) void home.panel.webview.postMessage({ type: 'update', html: body });
    home.html = body;
  };
  disposables.push({ dispose: () => home?.panel.dispose() }, store.onDidChange(() => renderHome()) as vscode.Disposable);
  register('armada.home', async () => {
    if (!(await requireConnected())) return;
    if (home) {
      home.panel.reveal(undefined, false);
      return;
    }
    const panel = vscode.window.createWebviewPanel('armada.home', 'Armada', vscode.ViewColumn.Active, { enableScripts: true });
    panel.webview.onDidReceiveMessage((message: unknown) => {
      if (isDetailMessage(message)) void vscode.commands.executeCommand(message.command, ...message.args);
    });
    panel.onDidDispose(() => {
      home = undefined;
    });
    home = { panel, html: '' };
    renderHome();
    void store.refreshAll();
  });

  // Activity charts
  register('armada.charts', async () => {
    if (!(await requireConnected())) return;
    const day = 86400000;
    const from14 = new Date(Date.now() - 14 * day).toISOString();
    const [history, tokens, requests] = await Promise.all([
      api.getMissionHistory({ fromUtc: from14, bucketMinutes: 1440 }).catch(() => undefined),
      api.getTokenUsage({ fromUtc: from14, bucketMinutes: 1440, modelLimit: 5 }).catch(() => undefined),
      api.getRequestHistorySummary({ fromUtc: new Date(Date.now() - day).toISOString(), bucketMinutes: 60 }).catch(() => undefined),
    ]);
    const dayLabel = (iso: string): string => new Date(iso).toLocaleDateString([], { month: 'short', day: 'numeric' });
    const hourLabel = (iso: string): string => new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    const page: DetailPage = {
      title: 'Activity Charts',
      actions: [{ label: 'Refresh', command: 'armada.charts', primary: true }],
      sections: [
        { kind: 'fields', rows: [
          { label: 'Missions (14 days)', value: history ? `${history.totalCount} total, ${history.completeCount} complete, ${history.failedCount} failed` : 'unavailable' },
          { label: 'Tokens (14 days)', value: tokens ? `${tokens.totalTokens.toLocaleString()} (${tokens.inputTokens.toLocaleString()} in, ${tokens.outputTokens.toLocaleString()} out, ${tokens.cachedTokens.toLocaleString()} cached)` : 'unavailable' },
          { label: 'API requests (24 hours)', value: requests ? `${requests.totalCount.toLocaleString()} total, ${Math.round(requests.successRate * 1000) / 10}% success, ${Math.round(requests.averageDurationMs)} ms average` : 'unavailable' },
        ] },
        { kind: 'bars', title: 'Missions per day', note: 'last 14 days', bars: (history?.buckets ?? []).map((b) => ({ label: dayLabel(b.startUtc), parts: [{ value: b.completeCount, tone: 'ok', name: 'Complete' }, { value: b.failedCount, tone: 'error', name: 'Failed' }, { value: b.otherCount, tone: 'muted', name: 'Other' }] })) },
        { kind: 'bars', title: 'Tokens per day', note: 'last 14 days', bars: (tokens?.buckets ?? []).map((b) => ({ label: dayLabel(b.bucketStartUtc), parts: [{ value: b.inputTokens, tone: 'info', name: 'Input' }, { value: b.outputTokens, tone: 'warn', name: 'Output' }, { value: b.cachedTokens, tone: 'muted', name: 'Cached' }] })) },
        { kind: 'bars', title: 'API requests per hour', note: 'last 24 hours', bars: (requests?.buckets ?? []).map((b) => ({ label: hourLabel(b.bucketStartUtc), parts: [{ value: b.successCount, tone: 'ok', name: 'Succeeded' }, { value: b.failureCount, tone: 'error', name: 'Failed' }] })) },
      ],
    };
    const existing = charts;
    if (existing) {
      existing.reveal(undefined, false);
      existing.webview.html = renderDetailHtml(page, crypto.randomBytes(16).toString('base64'));
      return;
    }
    const panel = vscode.window.createWebviewPanel('armada.charts', 'Activity Charts', vscode.ViewColumn.Active, { enableScripts: true });
    panel.webview.onDidReceiveMessage((message: unknown) => {
      if (isDetailMessage(message)) void vscode.commands.executeCommand(message.command, ...message.args);
    });
    panel.onDidDispose(() => {
      charts = undefined;
    });
    charts = panel;
    panel.webview.html = renderDetailHtml(page, crypto.randomBytes(16).toString('base64'));
  });

  // Planning
  register('armada.planning.new', async () => {
    if (!(await requireConnected())) return;
    await Promise.all([store.state('fleet') === 'ready' ? undefined : store.refresh('fleet'), store.state('captains') === 'ready' ? undefined : store.refresh('captains')]);
    const created = await showForm<string>({
      title: 'New Planning Session',
      intro: 'Plan work with a captain in a repository-aware conversation, then dispatch the plan as a voyage.',
      submitLabel: 'Start Planning',
      fields: [
        { key: 'captainId', label: 'Captain', type: 'select', required: true, value: store.captains.find((c) => c.supportsPlanningSessions)?.id, options: store.captains.map((c) => ({ value: c.id, label: `${c.name} (${c.runtime})${c.supportsPlanningSessions ? '' : ' - cannot plan'}` })) },
        { key: 'vesselId', label: 'Vessel', type: 'select', required: true, options: store.vessels.map((v) => ({ value: v.id, label: v.name })) },
        { key: 'title', label: 'Title', type: 'text', placeholder: 'What are you planning?' },
      ],
    }, async (values) => {
      try {
        const session = await api.createPlanningSession({ captainId: values.captainId, vesselId: values.vesselId, title: values.title?.trim() || undefined });
        return { result: session.session.id };
      } catch (err) {
        return { error: describeError(err) };
      }
    });
    if (created) await deps.openPlanning(created);
  });

  // Vessel import
  register('armada.vessel.import', async () => {
    if (!(await requireConnected())) return;
    let root: string | undefined;
    if (connection.state.target?.kind === 'local') {
      const picked = await vscode.window.showOpenDialog({ canSelectFolders: true, canSelectFiles: false, openLabel: 'Find Repositories', title: 'Import vessels: folder to search for git repositories' });
      root = picked?.[0]?.fsPath;
    } else {
      root = await browseAdmiralFolders();
    }
    if (!root) return;
    const found = await vscode.window.withProgress({ location: vscode.ProgressLocation.Notification, title: 'Armada: looking for repositories...' }, () => api.discoverVesselImport({ Roots: [root as string], MaxDepth: 4 }));
    const candidates = (found.candidates ?? []).filter((c) => !c.existingVesselId);
    if (!candidates.length) {
      void vscode.window.showInformationMessage(`Armada: no new repositories under ${root}.`);
      return;
    }
    const picks = await vscode.window.showQuickPick(candidates.map((c) => ({ label: c.proposedName, description: c.remoteUrl ?? '', detail: c.path, picked: true, path: c.path })), { title: `Import ${candidates.length} repositories as vessels`, canPickMany: true, matchOnDetail: true });
    if (!picks?.length) return;
    const result = await vscode.window.withProgress({ location: vscode.ProgressLocation.Notification, title: `Armada: importing ${picks.length} vessels...` }, () => api.importVessels({ BatchId: found.batchId, Paths: picks.map((p) => p.path) }));
    const imported = (result.items ?? []).filter((i) => i.vesselId).length;
    void vscode.window.showInformationMessage(`Armada: imported ${imported} of ${picks.length} vessels.`);
    void store.refresh('fleet');
  });

  // API explorer
  let operations: ExplorerOperation[] | undefined;
  let openApi: OpenApiSpec | null = null;
  register('armada.api.call', async () => {
    if (!(await requireConnected())) return;
    const active = connection.requireActive();
    const headers = { ...credentialHeaders(active.credential) };
    if (!operations) {
      const response = await fetch(`${active.baseUrl}/openapi.json`, { headers });
      if (!response.ok) throw new Error(`The Admiral's OpenAPI document returned ${response.status}.`);
      openApi = (await response.json()) as OpenApiSpec;
      operations = buildExplorerOperations(openApi);
    }
    const picked = await vscode.window.showQuickPick(operations.map((op) => ({ label: `${op.method.toUpperCase()} ${op.path}`, description: op.summary, detail: op.tag, op })), { title: 'Armada API: pick an operation', matchOnDescription: true, matchOnDetail: true });
    if (!picked) return;
    const op = picked.op;
    let path = op.path;
    const query = new URLSearchParams();
    for (const parameter of op.parameters) {
      if (parameter.in !== 'path' && parameter.in !== 'query') continue;
      if (parameter.in === 'query' && !parameter.required) continue;
      const value = await vscode.window.showInputBox({ title: `${op.method.toUpperCase()} ${op.path}`, prompt: `${parameter.name} (${parameter.in})${parameter.description ? `: ${parameter.description}` : ''}`, ignoreFocusOut: true });
      if (value === undefined) return;
      if (parameter.in === 'path') path = path.replace(`{${parameter.name}}`, encodeURIComponent(value));
      else if (value) query.set(parameter.name, value);
    }
    let body: string | undefined;
    if (op.requestBody) {
      const draft = await vscode.workspace.openTextDocument({ language: 'json', content: JSON.stringify(schemaExample(op.requestBody.schema, openApi), null, 2) });
      await vscode.window.showTextDocument(draft, { preview: false });
      const send = await vscode.window.showInformationMessage(`Edit the request body in the editor, then send ${op.method.toUpperCase()} ${path}.`, { modal: true }, 'Send');
      if (send !== 'Send') return;
      body = draft.getText();
    }
    const url = `${active.baseUrl}${path}${query.toString() ? `?${query}` : ''}`;
    const started = Date.now();
    const response = await fetch(url, { method: op.method.toUpperCase(), headers: { ...headers, ...(body !== undefined ? { 'Content-Type': op.requestBodyContentType || 'application/json' } : {}) }, body });
    const text = await response.text();
    let content = text;
    let language = 'plaintext';
    try {
      content = JSON.stringify(JSON.parse(text), null, 2);
      language = 'json';
    } catch {
      /* not JSON */
    }
    const doc = await vscode.workspace.openTextDocument({ language, content: content || '(empty response)' });
    await vscode.window.showTextDocument(doc, { preview: false });
    void vscode.window.showInformationMessage(`Armada API: ${response.status} ${response.statusText} in ${Date.now() - started} ms (${op.method.toUpperCase()} ${path}).`);
  });

  return disposables;
}

/** Walks the Admiral host's folders (the import browser) in a quick pick; resolves the chosen folder. */
async function browseAdmiralFolders(): Promise<string | undefined> {
  let path: string | null = null;
  for (;;) {
    const listing = await api.browseAllVesselImport(path);
    const here = listing.path ?? '';
    const items: Array<vscode.QuickPickItem & { go?: string | null; choose?: string }> = [];
    if (here) items.push({ label: '$(check) Search this folder', description: here, choose: here });
    if (listing.parent !== null && here) items.push({ label: '$(arrow-up) ..', description: listing.parent ?? 'top', go: listing.parent });
    for (const entry of listing.entries ?? []) {
      items.push({ label: `${entry.isGitRepository ? '$(repo)' : '$(folder)'} ${entry.name}`, description: entry.isGitRepository ? 'git repository' : '', detail: entry.path, go: entry.path });
    }
    const picked = await vscode.window.showQuickPick(items, { title: `Import vessels: folders on the Admiral host${here ? ` (${here})` : ''}`, matchOnDetail: true, ignoreFocusOut: true });
    if (!picked) return undefined;
    if (picked.choose) return picked.choose;
    path = picked.go ?? null;
  }
}

