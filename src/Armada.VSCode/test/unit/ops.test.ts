import * as assert from 'assert';
import { beforeEach, describe, it } from 'node:test';
import type { MissionSummary } from '@dashboard/types/models';
import { nativeCommandForRoute } from '../../src/surfaces/blackbox/entryView';
import { isDetailMessage, renderDetailHtml } from '../../src/surfaces/ops/detailHtml';
import { attemptRow, dispositionTone, missionPage, statusTone, voyagePage } from '../../src/surfaces/ops/detailPages';
import { renderFormHtml } from '../../src/surfaces/ops/formPanel';
import { parseArmadaDocumentPath } from '../../src/surfaces/ops/opsDocuments';
import { domainsForEvent, OpsStore } from '../../src/surfaces/ops/opsStore';
import { MissionsTreeProvider, NeedsYouTreeProvider, OpsNode } from '../../src/surfaces/ops/opsTrees';
import { elapsed, humanize, missionLook } from '../../src/surfaces/ops/presentation';
import { useAdmiral } from '../../src/transport/hostClient';
import { MockFetch } from '../fakes/fetch';
import { fake, TreeItemCollapsibleState } from '../fakes/vscode';

const NOW = new Date('2026-11-20T12:00:00Z');

function summary(id: string, status: string, extra: Partial<MissionSummary> = {}): Record<string, unknown> {
  return {
    Id: id, TenantId: 'ten_1', UserId: null, VoyageId: extra.voyageId ?? null, VesselId: 'vsl_1', CaptainId: 'cpt_1', Title: `Mission ${id}`, Status: status,
    Priority: 100, ParentMissionId: null, Persona: null, DependsOnMissionId: null, BranchName: `armada/${id}`, DockId: null, ProcessId: null, PrUrl: null,
    CommitHash: null, FailureReason: status === 'Failed' ? 'tests failed' : null, RequiresReview: false, ReviewDenyAction: 'RetryStage', ReviewComment: null,
    ReviewedByUserId: null, ReviewRequestedUtc: null, ReviewedUtc: null, CreatedUtc: '2026-11-20T11:00:00Z', StartedUtc: '2026-11-20T11:30:00Z',
    CompletedUtc: status === 'Complete' || status === 'Failed' ? '2026-11-20T11:50:00Z' : null, TotalRuntimeMs: null, LastUpdateUtc: extra.lastUpdateUtc ?? '2026-11-20T11:55:00Z',
  };
}

function page(objects: unknown[]): Record<string, unknown> {
  return { Success: true, PageNumber: 1, PageSize: objects.length, TotalPages: 1, TotalRecords: objects.length, Objects: objects, TotalMs: 1 };
}

function admiral(): MockFetch {
  useAdmiral('http://127.0.0.1:7890', { secret: 'ak', header: 'X-Api-Key' });
  return new MockFetch().install()
    .on('GET', '/api/v1/missions/summaries', { json: page([summary('msn_a', 'InProgress'), summary('msn_b', 'Review'), summary('msn_c', 'Pending'), summary('msn_d', 'Failed', { lastUpdateUtc: '2026-11-20T10:00:00Z' }), summary('msn_e', 'Complete')]) })
    .on('POST', '/api/v1/inbox/enumerate', { json: page([{ Kind: 'review', Severity: 'Warning', Title: 'Review Mission msn_b', Detail: 'waiting', EntityType: 'mission', EntityId: 'msn_b', Href: '/dashboard/missions/msn_b' }]) })
    .on('GET', '/api/v1/vessels', { json: page([{ Id: 'vsl_1', Name: 'Armada', DefaultBranch: 'main', Active: true, FleetId: null }]) })
    .on('GET', '/api/v1/fleets', { json: page([]) })
    .on('GET', '/api/v1/captains', { json: page([{ Id: 'cpt_1', Name: 'claude-1', Runtime: 'ClaudeCode', State: 'Working', ActiveMissionIds: ['msn_a'] }]) });
}

beforeEach(() => fake.reset());

describe('ops store', () => {
  it('maps Admiral events to the domains they touch', () => {
    assert.deepStrictEqual(domainsForEvent('mission.status_changed'), ['missions', 'voyages', 'inbox']);
    assert.deepStrictEqual(domainsForEvent('captain.state_changed'), ['captains', 'inbox']);
    assert.deepStrictEqual(domainsForEvent('merge_queue.updated'), ['merge', 'missions']);
    assert.deepStrictEqual(domainsForEvent('cli-permission.requested'), ['inbox']);
    assert.deepStrictEqual(domainsForEvent('status.snapshot'), []);
  });

  it('loads live data, newest first, and clears it when disconnected', async () => {
    admiral();
    let connected = true;
    const store = new OpsStore({ isConnected: () => connected });
    const changed: string[] = [];
    store.onDidChange((d) => changed.push(d));
    await Promise.all([store.refresh('missions'), store.refresh('inbox'), store.refresh('fleet'), store.refresh('captains')]);
    assert.strictEqual(store.state('missions'), 'ready');
    assert.strictEqual(store.missions.length, 5);
    assert.strictEqual(store.missions[store.missions.length - 1].id, 'msn_d', 'the oldest update sorts last');
    assert.strictEqual(store.inbox[0].entityId, 'msn_b');
    assert.strictEqual(store.vesselName('vsl_1'), 'Armada');
    assert.strictEqual(store.captainName('cpt_1'), 'claude-1');
    assert.ok(changed.includes('missions') && changed.includes('inbox'));
    connected = false;
    await store.refresh('missions');
    assert.strictEqual(store.state('missions'), 'disconnected');
    assert.deepStrictEqual(store.missions, []);
    store.dispose();
  });

  it('reports a load error without throwing', async () => {
    useAdmiral('http://127.0.0.1:7890', undefined);
    new MockFetch().install().otherwise(() => ({ status: 500, json: { Error: 'InternalError', Message: 'boom' } }));
    const store = new OpsStore({ isConnected: () => true });
    await store.refresh('missions');
    assert.strictEqual(store.state('missions'), 'error');
    assert.match(store.error('missions') ?? '', /boom|server/i);
  });
});

describe('ops trees', () => {
  it('groups missions into Needs Review, Active, Queued and Recent, with status icons and menu context', async () => {
    admiral();
    const store = new OpsStore({ isConnected: () => true });
    await Promise.all([store.refresh('missions'), store.refresh('fleet'), store.refresh('captains')]);
    const tree = new MissionsTreeProvider(store, () => NOW);
    const roots = tree.getChildren();
    assert.deepStrictEqual(roots.map((r) => (r.kind === 'group' ? r.label : r.kind)), ['Needs Review (1)', 'Active (1)', 'Queued (1)', 'Recent (2)']);
    const active = tree.getChildren(roots[1])[0] as Extract<OpsNode, { kind: 'mission' }>;
    const item = tree.getTreeItem(active) as unknown as { label: string; description: string; contextValue: string; command: { command: string; arguments: unknown[] }; iconPath: { id: string } };
    assert.strictEqual(item.label, 'Mission msn_a');
    assert.strictEqual(item.description, 'In Progress · Armada · 30m');
    assert.match(item.contextValue, /armadaMission\.InProgress/);
    assert.deepStrictEqual(item.command, { command: 'armada.mission.open', title: 'Open Mission', arguments: ['msn_a'] });
    assert.strictEqual(item.iconPath.id, 'sync~spin');
    const recent = tree.getTreeItem(roots[3]) as unknown as { collapsibleState: TreeItemCollapsibleState };
    assert.strictEqual(recent.collapsibleState, TreeItemCollapsibleState.Collapsed, 'finished work starts collapsed');
    assert.deepStrictEqual(tree.badge(), { value: 1, tooltip: '1 active mission' });
    tree.dispose();
  });

  it('shows loading, errors and empty states, and badges what needs you', async () => {
    const store = new OpsStore({ isConnected: () => false });
    const tree = new NeedsYouTreeProvider(store, () => NOW);
    assert.deepStrictEqual(tree.getChildren(), [], 'disconnected: the welcome view shows');
    admiral();
    const live = new OpsStore({ isConnected: () => true });
    const needs = new NeedsYouTreeProvider(live, () => NOW);
    await live.refresh('inbox');
    const [first] = needs.getChildren();
    const item = needs.getTreeItem(first) as unknown as { label: string; contextValue: string };
    assert.strictEqual(item.label, 'Review Mission msn_b');
    assert.match(item.contextValue, /armadaInbox\.review/);
    assert.deepStrictEqual(needs.badge(), { value: 1, tooltip: '1 item needs you' });
  });
});

describe('ops detail pages and documents', () => {
  const mission = (status: string): Parameters<typeof missionPage>[0] => ({
    id: 'msn_x', tenantId: null, voyageId: 'vyg_1', vesselId: 'vsl_1', captainId: 'cpt_1', title: 'Fix <the> bug', description: 'Do it', status, mode: 'Default' as never, priority: 1,
    parentMissionId: null, persona: null, dependsOnMissionId: null, requiresReview: true, reviewDenyAction: 'RetryStage', reviewComment: null, reviewedByUserId: null,
    reviewRequestedUtc: null, reviewedUtc: null, branchName: 'armada/x', dockId: null, processId: null, prUrl: null, commitHash: null, failureReason: status === 'Failed' ? 'boom' : null,
    diffSnapshot: null, createdUtc: '2026-11-20T11:00:00Z', startedUtc: '2026-11-20T11:30:00Z', completedUtc: null, lastUpdateUtc: '2026-11-20T11:55:00Z',
  } as unknown as Parameters<typeof missionPage>[0]);

  it('offers the actions that fit the mission status', () => {
    const store = new OpsStore({ isConnected: () => false });
    const commands = (status: string) => missionPage(mission(status), store, undefined, NOW).actions.map((a) => a.command);
    assert.deepStrictEqual(commands('Review'), ['armada.mission.viewLog', 'armada.mission.viewDiff', 'armada.mission.approve', 'armada.mission.deny', 'armada.mission.cancel', 'armada.resource.action', 'armada.openInBrowser']);
    assert.deepStrictEqual(commands('Failed'), ['armada.mission.viewLog', 'armada.mission.viewDiff', 'armada.mission.restart', 'armada.openBlackBox', 'armada.resource.action', 'armada.openInBrowser']);
    const live = missionPage(mission('InProgress'), store, 'line 1\nline 2', NOW);
    assert.ok(live.sections.some((s) => s.kind === 'pre' && s.text === 'line 1\nline 2'), 'a running mission shows its live log');
    assert.strictEqual(statusTone('Failed'), 'error');
    assert.strictEqual(statusTone('Complete'), 'ok');
  });

  it('renders escaped, VS Code-themed HTML and accepts only armada.* commands from it', () => {
    const store = new OpsStore({ isConnected: () => false });
    const html = renderDetailHtml(missionPage(mission('Review'), store, undefined, NOW), 'n0nce');
    assert.ok(html.includes('Fix &lt;the&gt; bug'));
    assert.ok(html.includes("script-src 'nonce-n0nce'"));
    assert.ok(html.includes('var(--vscode-button-background)'));
    assert.ok(isDetailMessage({ command: 'armada.mission.cancel', args: ['msn_x'] }));
    assert.ok(!isDetailMessage({ command: 'workbench.action.terminal.sendSequence', args: [] }));
    assert.ok(!isDetailMessage({ command: 'armada.mission.cancel' }));
    const voyage = voyagePage({ voyage: { id: 'vyg_1', title: 'V', status: 'InProgress', createdUtc: '2026-11-20T11:00:00Z' } as never, totalMissions: 3, completedMissions: 1, failedMissions: 1, inProgressMissions: 1, missionCountsByStatus: {}, vesselId: 'vsl_1', vesselCount: 1 }, [], store, NOW);
    assert.deepStrictEqual(voyage.actions.map((a) => a.command), ['armada.voyage.cancel', 'armada.resource.action', 'armada.openInBrowser']);
  });

  it('parses document URIs and routes dashboard links to native views', () => {
    assert.deepStrictEqual(parseArmadaDocumentPath('/missions/msn_1/Fix the bug.log'), { kind: 'missionLog', id: 'msn_1' });
    assert.deepStrictEqual(parseArmadaDocumentPath('/missions/msn_1/Fix the bug.diff'), { kind: 'missionDiff', id: 'msn_1' });
    assert.deepStrictEqual(parseArmadaDocumentPath('/captains/cpt_1/claude.log'), { kind: 'captainLog', id: 'cpt_1' });
    assert.strictEqual(parseArmadaDocumentPath('/captains/cpt_1/x.diff'), undefined);
    assert.deepStrictEqual(nativeCommandForRoute('/missions/msn_1'), ['armada.mission.open', 'msn_1']);
    assert.deepStrictEqual(nativeCommandForRoute('/blackbox/bbx_1'), ['armada.openBlackBox', 'bbx_1']);
    assert.deepStrictEqual(nativeCommandForRoute('/runbooks/rbk_1'), ['armada.openInBrowser', '/runbooks/rbk_1']);
  });

  it('renders forms with escaped values and required markers', () => {
    const html = renderFormHtml({ title: 'Dispatch', submitLabel: 'Go', fields: [{ key: 'title', label: 'Title', type: 'text', required: true, value: '"x"' }, { key: 'v', label: 'Vessel', type: 'select', value: 'b', options: [{ value: 'a', label: 'A' }, { value: 'b', label: 'B' }] }] }, 'n');
    assert.ok(html.includes('value="&quot;x&quot;"'));
    assert.ok(html.includes('<option value="b" selected>B</option>'));
    assert.ok(html.includes('aria-required="true"'));
  });

  it('shows attempts, failure kinds, repairs and escalations from the attempt history', () => {
    const store = new OpsStore({ isConnected: () => false });
    const attempt = (n: number, extra: Record<string, unknown>) => ({
      outcomeId: `out_${n}`, attempt: n, reworkRound: 0, captainId: 'cpt_1', captainName: 'qwen-1', runtime: 'OpenCode', model: 'qwen3.8', modelProvider: null, modelEndpointId: null,
      disposition: 'Completed', failureKind: null, failureReason: null, runtimeFailureKind: null, contractRepairKind: null, isRepair: false, escalated: false, nextCaptainId: null,
      judgeVerdict: null, suspectedCause: null, isEvaluation: false, startedUtc: null, endedUtc: null, durationMs: 65000, ...extra,
    });
    const history = {
      missionId: 'msn_x', voyageId: 'vyg_1', persona: 'Worker', mode: 'Default', status: 'Complete', captainId: 'cpt_2', tier: 'Frontier', escalationCount: 1, escalatedFromCaptainId: 'cpt_1',
      escalatedFromModel: 'qwen3.8', reworkEscalated: false, redispatchAttempts: 0, outputRepairAttempts: 1, noOpRedispatchAttempts: 0, stallRecoveryAttempts: 0,
      attempts: [attempt(1, { disposition: 'Escalated', failureKind: 'ToolCallInvalid', isRepair: true, contractRepairKind: 'FormatOnly', escalated: true }), attempt(2, { model: 'opus', captainName: 'opus-1', captainId: 'cpt_2', durationMs: 800 })],
    };
    const page = missionPage(mission('Complete'), store, undefined, NOW, history);
    const recovery = page.sections.find((s) => s.kind === 'fields' && s.title === 'Recovery');
    assert.ok(recovery && recovery.kind === 'fields');
    assert.deepStrictEqual(recovery.rows.map((r) => `${r.label}=${r.value}`), ['Tier=Frontier', 'Escalations=1 (from qwen3.8)', 'Output repairs=1']);
    const list = page.sections.find((s) => s.kind === 'list' && s.title === 'Attempts (2)');
    assert.ok(list && list.kind === 'list');
    assert.strictEqual(list.items[0].label, 'Attempt 1: qwen3.8 on qwen-1');
    assert.strictEqual(list.items[0].description, 'Tool Call Invalid · repair (Format Only) · escalated · 1m 5s');
    assert.deepStrictEqual(list.items[0].status, { label: 'Escalated', tone: 'warn' });
    assert.strictEqual(attemptRow(history.attempts[1]).description, '800 ms');
    assert.strictEqual(dispositionTone('Failed'), 'error');
    assert.ok(!missionPage(mission('Complete'), store, undefined, NOW).sections.some((s) => s.kind === 'list'), 'no history, no attempts section');
  });

  it('shows the typed reason and advice of a waiting mission', () => {
    const store = new OpsStore({ isConnected: () => false });
    const pending = { ...mission('Pending'), assignmentBlocker: { reason: 'EndpointMissionLimit', summary: 'The endpoint is at its cap.', advice: [{ text: 'Raise MaxConcurrentMissions on the endpoint.' }], untilUtc: null, dependsOnMissionId: null, blockingMissionIds: [], captains: [], computedUtc: '2026-11-20T11:55:00Z' } } as unknown as Parameters<typeof missionPage>[0];
    const section = missionPage(pending, store, undefined, NOW).sections.find((s) => s.kind === 'text' && s.title === 'Endpoint Mission Limit');
    assert.ok(section && section.kind === 'text');
    assert.strictEqual(section.text, 'The endpoint is at its cap.\n- Raise MaxConcurrentMissions on the endpoint.');
    assert.strictEqual(section.tone, 'warn');
    const running = { ...pending, status: 'InProgress' } as typeof pending;
    assert.ok(!missionPage(running, store, undefined, NOW).sections.some((s) => s.kind === 'text' && s.title === 'Endpoint Mission Limit'), 'only waiting missions show a blocker');
  });

  it('humanizes statuses and times', () => {
    assert.strictEqual(humanize('PullRequestOpen'), 'Pull Request Open');
    assert.strictEqual(elapsed('2026-11-20T09:55:00Z', null, NOW), '2h 5m');
    assert.strictEqual(missionLook('Review').icon, 'eye');
  });
});

describe('home overview', () => {
  it('summarizes live work with links into the native pages', async () => {
    admiral();
    const { homePage } = await import('../../src/surfaces/ops/extraCommands');
    const store = new OpsStore({ isConnected: () => true });
    await Promise.all([store.refresh('missions'), store.refresh('inbox'), store.refresh('fleet'), store.refresh('captains')]);
    const page = homePage(store, { baseUrl: 'http://127.0.0.1:7890', version: '1.3.0', email: 'dev@example.com' }, NOW);
    assert.strictEqual(page.title, 'Armada');
    const fields = page.sections[0];
    assert.ok(fields.kind === 'fields' && fields.rows.some((r) => r.label === 'Active missions' && r.value === '1'));
    assert.ok(fields.kind === 'fields' && fields.rows.some((r) => r.label === 'Captains' && r.value === '1 working, 0 not working, 1 total'));
    const needs = page.sections.find((s) => s.kind === 'list' && s.title.startsWith('Needs you'));
    assert.ok(needs && needs.kind === 'list' && needs.items[0].link?.command === 'armada.inbox.open');
    const failures = page.sections.find((s) => s.kind === 'list' && s.title === 'Recent failures');
    assert.ok(failures && failures.kind === 'list' && failures.items[0].link?.command === 'armada.mission.open');
    assert.deepStrictEqual(page.actions.map((a) => a.command), ['armada.dispatch', 'armada.dispatchVoyage', 'armada.planning.new', 'armada.ask.open']);
  });
});

describe('needs you notifications', () => {
  it('reports only items that were not there before, and nothing on the first load', async () => {
    const { inboxKey, newInboxItems } = await import('../../src/surfaces/ops/localCommands');
    const a = { kind: 'review', severity: 'Warning', title: 'A', detail: '', entityType: 'mission', entityId: 'msn_a', href: '' } as never;
    const b = { kind: 'failed', severity: 'Error', title: 'B', detail: '', entityType: 'mission', entityId: 'msn_b', href: '' } as never;
    assert.deepStrictEqual(newInboxItems(undefined, [a, b]), []);
    assert.deepStrictEqual(newInboxItems(new Set([inboxKey(a)]), [a, b]), [b]);
  });
});

describe('views as editor tabs', () => {
  it('lays a tree out as a page: groups become sections, items keep their click command', async () => {
    const { treePage } = await import('../../src/surfaces/tabs/treeTab');
    admiral();
    const store = new OpsStore({ isConnected: () => true });
    await Promise.all([store.refresh('missions'), store.refresh('fleet'), store.refresh('captains')]);
    const provider = new MissionsTreeProvider(store, () => NOW);
    const page = await treePage({ viewId: 'armadaMissions', title: 'Missions', provider, actions: [{ label: 'Dispatch Mission', command: 'armada.dispatch', primary: true }] });
    assert.strictEqual(page.title, 'Missions');
    assert.deepStrictEqual(page.sections.map((s) => s.kind === 'list' ? s.title : ''), ['Needs Review (1)', 'Active (1)', 'Queued (1)', 'Recent (2)']);
    const active = page.sections[1];
    assert.ok(active.kind === 'list' && active.items[0].link?.command === 'armada.mission.open' && active.items[0].link.args?.[0] === 'msn_a');
    assert.ok(active.kind === 'list' && active.items[0].description === 'In Progress · Armada · 30m');
    assert.deepStrictEqual(page.actions.map((a) => a.command), ['armada.dispatch', 'armada.tab.refresh']);
  });
});
