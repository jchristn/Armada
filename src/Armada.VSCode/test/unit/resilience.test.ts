import * as assert from 'assert';
import { beforeEach, describe, it } from 'node:test';
import * as vscode from 'vscode';
import { BlackBoxEntry, BlackBoxResilience, BlackBoxResilienceGap, EnumerationResult } from '../../src/api/types';
import { BlackBoxCommands, CHAT_OPEN_COMMAND } from '../../src/commands/blackBoxCommands';
import { InsightCommands, TIMELINE_NEXT } from '../../src/commands/insightCommands';
import { ArmadaSettings } from '../../src/config';
import { changeBrief, isEntryViewMessage, rankedChanges, renderEntryHtml, suspectedDefect } from '../../src/views/entryHtml';
import { renderGapsMarkdown } from '../../src/views/insightMarkdown';
import { MockFetch, page } from '../fakes/fetch';
import { client, fullEntry, summary } from '../fakes/fixtures';
import { makeStore } from '../fakes/storeHelpers';
import { fake, Uri } from '../fakes/vscode';

const SETTINGS: ArmadaSettings = {
  url: 'http://armada.test:7890',
  tenant: '',
  authHeader: 'Bearer',
  dashboardUrl: 'http://armada.test:7890/dashboard',
  filterToWorkspaceVessel: true,
  includeResolved: false,
  maxEntries: 50,
  useWebSocket: true,
  pollIntervalSeconds: 60,
  decorationsEnabled: true,
  hotspotLimit: 10,
  notifyOnCapture: true,
  previewFixThis: true,
};

const IN_SCOPE: BlackBoxResilience = {
  InScope: true,
  ScopeReason: 'phase Landing (step TargetIntegration) is Armada\'s handoff and landing',
  SelfHealable: 'Yes',
  SelfHealingProposal: 'recreated the worktree from the branch and retried the landing',
  DetectionGapKind: 'Race',
  DetectionGap: 'The dock was reclaimed while the mission still had to land.',
  Invariant: 'A dock whose mission still has to land stays leased.',
  CodePointers: ['src/Armada.Server/MissionLandingHandler.cs:EnsureLandingDockAsync'],
  ArmadaVersion: '1.2.0',
  ArmadaCommit: 'abc9999',
  ProposedChanges: [
    { Rank: 2, Title: 'Never reclaim a leased dock', Change: 'Refuse reclaims while WorkProduced.', AcceptanceTest: 'Services.DockLease: refused.', Severity: 'High' },
    { Rank: 1, Title: 'Recreate a missing dock worktree', Change: 'Recreate it before git runs.', AcceptanceTest: 'Services.LandingPipeline: lands.', Severity: 'Critical', MissionBrief: 'Recreate a missing dock worktree\n\nRecreate it before git runs.' },
  ],
  RegressionTest: { Suite: 'Services.LandingPipeline', Description: 'Delete the dock directory and land.' },
  Severity: 'Critical',
  Occurrences30Days: 5,
  ArmadaVersionCount: 2,
  AttemptedRecoveries: ['LandingRetry x2 (Expired)'],
};

function withResilience(resilience: BlackBoxResilience | null, verdict = 'ArmadaDefect', suspected = false): BlackBoxEntry {
  const base = fullEntry();
  return { ...base, Triage: verdict, Record: { ...base.Record, Analysis: { ...base.Record?.Analysis, Resilience: resilience, Triage: { Verdict: verdict, Confidence: 'Low', Suspected: suspected } } } };
}

function commands(mock: MockFetch): BlackBoxCommands {
  const c = client(mock);
  return new BlackBoxCommands({ getClient: () => c, store: makeStore(mock), getSettings: () => SETTINGS, storageUri: Uri.file('/tmp/armada-vscode-test-storage') as unknown as vscode.Uri });
}

function gapsPage(pageNumber: number, totalPages: number): EnumerationResult<BlackBoxResilienceGap> {
  return {
    PageNumber: pageNumber,
    PageSize: 25,
    TotalPages: totalPages,
    TotalRecords: totalPages,
    Objects: [{
      Fingerprint: `fp_${pageNumber}`,
      Phase: 'Landing',
      Step: 'TargetIntegration',
      Occurrences: 5,
      OpenCount: 4,
      LatestEntryId: 'bbx_1',
      LatestMissionId: 'msn_x',
      ArmadaVersions: ['1.1.0', '1.2.0'],
      Severity: 'Critical',
      SelfHealable: 'Yes',
      Suspected: true,
      DetectionGap: 'The dock | was reclaimed.',
      TopProposedChange: { Rank: 1, Title: `Recreate dock page ${pageNumber}`, Change: 'Recreate it.', AcceptanceTest: 'lands' },
    }],
  };
}

beforeEach(() => fake.reset());

describe('Armada resilience in the entry view', () => {
  it('renders the section right after the summary, with the suspected badge and the draft button', () => {
    const html = renderEntryHtml(withResilience(IN_SCOPE, 'ArmadaDefect', true), [], 'n', 'c');
    const summaryAt = html.indexOf('<h2>Summary</h2>');
    const resilienceAt = html.indexOf('<h2>Armada resilience</h2>');
    assert.ok(summaryAt > 0 && resilienceAt > summaryAt && resilienceAt < html.indexOf('<h2>Where</h2>'), 'right after the summary');
    assert.ok(html.includes('Suspected Armada defect (prior only)'));
    assert.ok(html.includes('data-action="draftFix"'));
    assert.ok(html.includes('recreated the worktree from the branch and retried the landing'));
    assert.ok(html.includes('Race: The dock was reclaimed while the mission still had to land.'));
    assert.ok(html.includes('Critical (5 in 30 days, 2 Armada versions)'));
    assert.ok(html.includes('Code pointers (Armada 1.2.0 commit abc9999)'));
    assert.ok(html.includes('LandingRetry x2 (Expired)'));
    assert.ok(html.includes('Services.LandingPipeline: Delete the dock directory and land.'));
    assert.ok(html.indexOf('Recreate a missing dock worktree') < html.indexOf('Never reclaim a leased dock'), 'ranked');
    assert.ok(html.includes('Acceptance test: Services.DockLease: refused.'));
    assert.ok(!html.includes('—'));
    assert.ok(isEntryViewMessage({ action: 'draftFix' }));
  });

  it('outside handoff and landing it is one line and offers no draft; old entries have no section', () => {
    const html = renderEntryHtml(withResilience({ InScope: false, ProposedChanges: [], NoChangeReason: 'No Armada change suggested: outside scope.' }, 'Transient'), [], 'n', 'c');
    assert.ok(html.includes('No Armada change suggested: outside scope.'));
    assert.ok(!html.includes('data-action="draftFix"'));
    assert.ok(!html.includes('Suspected Armada defect'));
    assert.ok(!renderEntryHtml(fullEntry(), [], 'n', 'c').includes('<h2>Armada resilience</h2>'));
  });

  it('ranks changes and builds briefs', () => {
    assert.deepStrictEqual(rankedChanges(IN_SCOPE).map((c) => c.Rank), [1, 2]);
    assert.strictEqual(changeBrief(rankedChanges(IN_SCOPE)[0]), 'Recreate a missing dock worktree\n\nRecreate it before git runs.');
    assert.strictEqual(changeBrief(rankedChanges(IN_SCOPE)[1]), 'Never reclaim a leased dock\n\nRefuse reclaims while WorkProduced.\n\nAcceptance test: Services.DockLease: refused.');
    assert.ok(suspectedDefect(withResilience(IN_SCOPE)));
    assert.ok(!suspectedDefect(fullEntry()));
  });
});

describe('Draft Armada Fix Mission', () => {
  it('opens chat with the top change as a mission brief and copies it', async () => {
    fake.availableCommands = [CHAT_OPEN_COMMAND];
    const mock = new MockFetch().on('GET', '/api/v1/blackbox/bbx_1', { json: withResilience(IN_SCOPE) });
    assert.strictEqual(await commands(mock).draftArmadaFixMission({ kind: 'entry', entry: summary() }), 'chat');
    assert.strictEqual(fake.clipboard, 'Recreate a missing dock worktree\n\nRecreate it before git runs.');
    const call = fake.executed.find((e) => e.command === CHAT_OPEN_COMMAND);
    assert.match((call?.args[0] as { query: string }).query, /^Draft this Armada fix mission on the Armada vessel:\n\nRecreate a missing dock worktree/);
  });

  it('copies when the host has no chat, and says so when there is no change', async () => {
    fake.availableCommands = [];
    const mock = new MockFetch().on('GET', '/api/v1/blackbox/bbx_1', { json: withResilience(IN_SCOPE) });
    assert.strictEqual(await commands(mock).draftArmadaFixMission({ kind: 'entry', entry: summary() }), 'copied');
    assert.ok(fake.messages.some((m) => /was copied/.test(m.text)));
    fake.reset();
    const none = new MockFetch().on('GET', '/api/v1/blackbox/bbx_1', { json: withResilience({ InScope: false, ProposedChanges: [], NoChangeReason: 'No Armada change suggested.' }, 'Transient') });
    assert.strictEqual(await commands(none).draftArmadaFixMission({ kind: 'entry', entry: summary() }), 'noChange');
    assert.strictEqual(fake.clipboard, '');
    assert.ok(fake.messages.some((m) => /No Armada change suggested/.test(m.text)));
  });
});

describe('Show Resilience Gaps', () => {
  it('posts the paged request, renders the gaps and follows Next Page', async () => {
    const mock = new MockFetch().on('POST', '/api/v1/blackbox/resilience-gaps/enumerate', (req) => ({ json: gapsPage((req.body as { PageNumber: number }).PageNumber, 2) }));
    fake.info.push(TIMELINE_NEXT);
    const c = client(mock);
    const insights = new InsightCommands({ getClient: () => c, store: makeStore(mock), pickEntry: async () => undefined });
    const pages = await insights.showResilienceGaps();
    assert.deepStrictEqual(pages.map((p) => p.PageNumber), [1, 2]);
    const body = mock.calls('POST', '/api/v1/blackbox/resilience-gaps/enumerate')[0].body as { PageNumber: number; PageSize: number };
    assert.deepStrictEqual([body.PageNumber, body.PageSize], [1, 25]);
    assert.match(fake.documents[1].content ?? '', /Recreate dock page 2/);
  });

  it('renders an empty page and escapes table cells', () => {
    assert.match(renderGapsMarkdown({ PageNumber: 1, PageSize: 25, TotalPages: 0, TotalRecords: 0, Objects: [] }), /No Armada resilience gaps were recorded/);
    const md = renderGapsMarkdown(gapsPage(1, 1));
    assert.match(md, /\| Critical \| Landing \/ TargetIntegration \| 5 \(4 open\) \| Yes \(suspected\) \| Recreate dock page 1 \| 1\.1\.0, 1\.2\.0 \| bbx_1 \|/);
    assert.match(md, /Detection gap: The dock \\\| was reclaimed\./);
  });

  it('caps the page size at 50', async () => {
    const mock = new MockFetch().on('POST', '/api/v1/blackbox/resilience-gaps/enumerate', { json: page([]) });
    await client(mock).getResilienceGaps({ vesselId: 'vsl_a', resolution: 'Open' }, 2, 500);
    assert.deepStrictEqual(mock.calls('POST', '/api/v1/blackbox/resilience-gaps/enumerate')[0].body, { PageNumber: 2, PageSize: 50, VesselId: 'vsl_a', Resolution: 'Open' });
  });
});
