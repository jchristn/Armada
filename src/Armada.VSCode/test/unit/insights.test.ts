import * as assert from 'assert';
import { beforeEach, describe, it } from 'node:test';
import { ArmadaApiError, Routes } from '../../src/api/blackbox';
import { BlackBoxAttemptDiff, BlackBoxEntrySummary, BlackBoxTimelinePage, MissionOverlapPreview } from '../../src/api/types';
import { InsightCommands, TIMELINE_NEXT } from '../../src/commands/insightCommands';
import { isEntryViewMessage, recoveryExtras, renderEntryHtml } from '../../src/views/entryHtml';
import { cell, renderDiffMarkdown, renderOverlapMarkdown, renderTimelineMarkdown } from '../../src/views/insightMarkdown';
import { MockFetch, page } from '../fakes/fetch';
import { client, fullEntry, summary, VESSEL_A, VESSEL_B } from '../fakes/fixtures';
import { makeStore } from '../fakes/storeHelpers';
import { fake } from '../fakes/vscode';

function timelinePage(pageNumber: number, totalPages: number, overrides: Partial<BlackBoxTimelinePage> = {}): BlackBoxTimelinePage {
  return {
    PageNumber: pageNumber,
    PageSize: 100,
    TotalPages: totalPages,
    TotalRecords: totalPages * 2,
    Objects: [
      { TimestampUtc: '2026-10-28T10:00:00Z', Source: 'Mission', Kind: 'created', Message: `page ${pageNumber} created`, EntityId: 'msn_x' },
      { TimestampUtc: '2026-10-28T10:05:00Z', Source: 'Entry', Kind: 'captured', Message: 'merge | conflicted', EntityId: 'bbx_1' },
    ],
    EntryId: 'bbx_1',
    Sources: [{ Source: 'Mission', Count: 1, Truncated: false }, { Source: 'Entry', Count: 1, Truncated: true }],
    Truncated: false,
    ...overrides,
  };
}

const DIFF: BlackBoxAttemptDiff = {
  EntryId: 'bbx_1',
  OtherEntryId: 'bbx_0',
  SameMission: true,
  Attempt: 2,
  OtherAttempt: 1,
  SameFingerprint: false,
  Differences: [{ Section: 'Subject', Field: 'Model', Value: 'opus', OtherValue: 'sonnet' }, { Section: 'Configuration', Field: 'LandingMode', Value: 'LocalMerge', OtherValue: null }],
  DifferenceCount: 2,
  FilesOnlyInEntry: ['src/a.ts'],
  FilesOnlyInEntryCount: 1,
  FilesOnlyInOther: [],
  FilesOnlyInOtherCount: 0,
  FilesInBothCount: 3,
  ChangedFilesKnown: true,
};

const PREVIEW: MissionOverlapPreview = {
  VesselId: 'vsl_a',
  SerializeOverlappingMissions: true,
  LikelyFiles: ['src/auth/login.ts'],
  LikelyFileCount: 1,
  ScopedFileCount: 1,
  BriefPathCount: 0,
  TrackedFilesKnown: true,
  Warnings: [{ Kind: 'ActiveMission', OverlappingMissionId: 'msn_y', OverlappingMissionStatus: 'InProgress', Files: ['src/auth/login.ts'], FileCount: 1, Message: 'msn_y is changing src/auth/login.ts' }],
  WarningCount: 1,
  SuggestedDependsOnMissionId: 'msn_y',
  ActiveMissionsCompared: 4,
  ActiveMissionsTruncated: false,
  BriefLint: { Findings: [{ RuleId: 'BRIEF-DEP-001', Severity: 'Warning', Message: 'The brief describes a dependency', Excerpt: 'after mission 2 lands', Suggestion: 'Set dependsOnMissionId' }], FindingCount: 1, Clean: false, PathsChecked: true },
};

function insights(mock: MockFetch, picked: BlackBoxEntrySummary | null = summary(), storeOptions: Parameters<typeof makeStore>[1] = {}): InsightCommands {
  const c = client(mock);
  return new InsightCommands({ getClient: () => c, store: makeStore(mock, storeOptions), pickEntry: async () => picked ?? undefined });
}

beforeEach(() => fake.reset());

describe('insight API', () => {
  it('posts the overlap preview with the trimmed brief and reads the PascalCase result', async () => {
    const mock = new MockFetch().on('POST', '/api/v1/vessels/vsl_a/overlap-preview', { json: PREVIEW });
    const result = await client(mock).previewOverlap('vsl_a', { Title: '  Login  ', Description: ' Touch only src/auth/login.ts ', DependsOnMissionId: 'msn_d' });
    assert.strictEqual(result.Warnings?.[0].OverlappingMissionId, 'msn_y');
    assert.deepStrictEqual(mock.requests[0].body, { Title: 'Login', Description: 'Touch only src/auth/login.ts', DependsOnMissionId: 'msn_d' });
  });

  it('rejects an overlap preview without a vessel or brief, without calling the server', async () => {
    const mock = new MockFetch();
    await assert.rejects(client(mock).previewOverlap('', { Title: 'x' }), (e: ArmadaApiError) => e.kind === 'badRequest');
    await assert.rejects(client(mock).previewOverlap('vsl_a', { Title: ' ', Description: '' }), (e: ArmadaApiError) => e.kind === 'badRequest');
    await assert.rejects(client(mock).lintBrief({}), (e: ArmadaApiError) => e.kind === 'badRequest');
    assert.strictEqual(mock.requests.length, 0);
  });

  it('pages the timeline oldest first and newest first', async () => {
    const mock = new MockFetch().on('POST', '/api/v1/blackbox/bbx_1/timeline/enumerate', { json: timelinePage(2, 3) });
    const c = client(mock);
    const result = await c.getTimeline('bbx_1', 2, 25);
    await c.getTimeline('bbx_1', 1, 25, true);
    assert.strictEqual(result.PageNumber, 2);
    assert.deepStrictEqual(mock.requests[0].body, { PageNumber: 2, PageSize: 25, Order: 'CreatedAscending' });
    assert.strictEqual((mock.requests[1].body as { Order: string }).Order, 'CreatedDescending');
  });

  it('calls every other insight route with paging bodies', async () => {
    const mock = new MockFetch()
      .on('POST', '/api/v1/vessels/vsl_a/reservations/enumerate', { json: { ...page([{ Directory: 'src/', MissionId: 'msn_y' }]), Threshold: 3 } })
      .on('POST', '/api/v1/missions/lint-brief', { json: { Findings: [], FindingCount: 0, Clean: true, PathsChecked: false } })
      .on('POST', '/api/v1/vessels/vsl_a/blackbox-doctor/enumerate', { json: { ...page([]), WindowDays: 7 } })
      .on('POST', '/api/v1/blackbox/recovery-stats/enumerate', { json: page([{ PathId: 'bbx-land-001.retry', Runs: 4, SuccessRate: 0.75 }]) })
      .on('GET', '/api/v1/blackbox/bbx_1/diff/bbx_0', { json: DIFF })
      .on('POST', '/api/v1/blackbox/bbx_1/nearby-successes/enumerate', { json: page([{ SuccessId: 'bbs_1', MissionId: 'msn_ok' }]) })
      .on('GET', '/api/v1/voyages/vyg_1/blackbox/cascade', { json: { VoyageId: 'vyg_1', Nodes: [], Edges: [] } });
    const c = client(mock);
    assert.strictEqual((await c.listReservations('vsl_a', 2, 5)).Threshold, 3);
    assert.strictEqual((await c.lintBrief({ Title: 't', VesselId: 'vsl_a' })).Clean, true);
    assert.strictEqual((await c.getDoctor('vsl_a', 7)).WindowDays, 7);
    assert.strictEqual((await c.getRecoveryStats({ ruleId: 'BBX-LAND-001', vesselId: 'vsl_a' })).Objects[0].SuccessRate, 0.75);
    assert.strictEqual((await c.diffEntries('bbx_1', 'bbx_0')).DifferenceCount, 2);
    assert.strictEqual((await c.listNearbySuccesses('bbx_1')).Objects[0].SuccessId, 'bbs_1');
    assert.strictEqual((await c.getVoyageCascade('vyg_1')).VoyageId, 'vyg_1');
    assert.deepStrictEqual(mock.calls('POST', '/api/v1/vessels/vsl_a/reservations/enumerate')[0].body, { PageNumber: 2, PageSize: 5 });
    assert.deepStrictEqual(mock.calls('POST', '/api/v1/missions/lint-brief')[0].body, { Title: 't', Description: null, VesselId: 'vsl_a' });
    assert.deepStrictEqual(mock.calls('POST', '/api/v1/vessels/vsl_a/blackbox-doctor/enumerate')[0].body, { PageNumber: 1, PageSize: 10, WindowDays: 7 });
    assert.deepStrictEqual(mock.calls('POST', '/api/v1/blackbox/recovery-stats/enumerate')[0].body, { PageNumber: 1, PageSize: 10, RuleId: 'BBX-LAND-001', VesselId: 'vsl_a' });
  });

  it('refuses to diff an entry with itself and maps a 404 diff', async () => {
    const mock = new MockFetch().on('GET', '/api/v1/blackbox/bbx_1/diff/bbx_9', { status: 404, json: { Error: 'NotFound', Message: 'not found', Data: { Code: 'BlackBoxNotFound' } } });
    await assert.rejects(client(mock).diffEntries('bbx_1', 'bbx_1'), (e: ArmadaApiError) => e.kind === 'badRequest');
    await assert.rejects(client(mock).diffEntries('bbx_1', 'bbx_9'), (e: ArmadaApiError) => e.kind === 'notFound' && e.code === 'BlackBoxNotFound');
  });

  it('rejects a timeline response that is not a page', async () => {
    const mock = new MockFetch().on('POST', '/api/v1/blackbox/bbx_1/timeline/enumerate', { json: { EntryId: 'bbx_1' } });
    await assert.rejects(client(mock).getTimeline('bbx_1'), (e: ArmadaApiError) => e.kind === 'invalidResponse');
  });

  it('builds dashboard runbook and entry links', () => {
    assert.strictEqual(Routes.dashboardRunbook('rbk 1'), '/runbooks/rbk%201');
    assert.strictEqual(Routes.dashboardEntry('bbx_1'), '/blackbox/bbx_1');
  });
});

describe('insight Markdown', () => {
  it('renders a timeline page with escaped cells and source counts', () => {
    const md = renderTimelineMarkdown(summary(), timelinePage(1, 2, { Truncated: true }));
    assert.match(md, /Page 1 of 2, 4 events in all/);
    assert.ok(md.includes('merge \\| conflicted'));
    assert.match(md, /\| Entry \| 1 \| yes \|/);
    assert.match(md, /Some sources were capped/);
  });

  it('renders an empty timeline', () => {
    const md = renderTimelineMarkdown(summary(), { ...timelinePage(1, 0), Objects: [], TotalRecords: 0, Sources: [] });
    assert.match(md, /No timeline events were recorded/);
    assert.match(md, /Page 1 of 1/);
  });

  it('renders the diff side by side with the files only each side changed', () => {
    const md = renderDiffMarkdown(DIFF, 'bbx_1', 'bbx_0');
    assert.match(md, /\| Section \| Field \| bbx_1 \(attempt 2\) \| bbx_0 \(attempt 1\) \|/);
    assert.match(md, /\| Subject \| Model \| opus \| sonnet \|/);
    assert.match(md, /\| Configuration \| LandingMode \| LocalMerge \| - \|/);
    assert.match(md, /3 files changed in both/);
    assert.match(md, /### Only in bbx\\_0 \(attempt 1\) \(0\)\n\n_None._/);
    assert.match(md, /different failure fingerprints/);
  });

  it('renders a diff with no differences and unknown files', () => {
    const md = renderDiffMarkdown({ ...DIFF, Differences: [], DifferenceCount: 0, ChangedFilesKnown: false }, 'a', 'b');
    assert.match(md, /No recorded values differ/);
    assert.match(md, /changed files are not known/);
  });

  it('renders overlap and lint warnings, the suggestion and the serialize notice', () => {
    const md = renderOverlapMarkdown(PREVIEW, 'alpha', 'Login');
    assert.match(md, /## Overlap warnings \(1\)/);
    assert.match(md, /\*\*ActiveMission\*\*: msn\\_y is changing src\/auth\/login.ts \(`msn_y` InProgress\)/);
    assert.match(md, /## Brief lint warnings \(1\)/);
    assert.match(md, /\*\*BRIEF-DEP-001\*\*/);
    assert.match(md, /Suggestion: Set dependsOnMissionId/);
    assert.match(md, /Suggested dependency: `msn_y`/);
    assert.match(md, /serializes overlapping missions/);
  });

  it('renders a clean preview', () => {
    const md = renderOverlapMarkdown({ VesselId: 'vsl_a', Warnings: [], WarningCount: 0, BriefLint: { Findings: [], FindingCount: 0, Clean: true }, TrackedFilesKnown: false }, 'alpha', '');
    assert.match(md, /No overlaps with active missions/);
    assert.match(md, /The brief is clean/);
    assert.match(md, /no local repository on the Admiral/);
  });

  it('escapes table cells', () => {
    assert.strictEqual(cell('a|b\n<c>'), 'a\\|b &lt;c&gt;');
    assert.strictEqual(cell(null), '-');
  });
});

describe('Show Black Box Timeline', () => {
  it('opens the first page and follows Next Page', async () => {
    const mock = new MockFetch().on('POST', '/api/v1/blackbox/bbx_1/timeline/enumerate', (req) => ({ json: timelinePage((req.body as { PageNumber: number }).PageNumber, 2) }));
    fake.info.push(TIMELINE_NEXT);
    const pages = await insights(mock).showTimeline();
    assert.deepStrictEqual(pages.map((p) => p.PageNumber), [1, 2]);
    assert.strictEqual(fake.documents.length, 2);
    assert.match(fake.documents[1].content ?? '', /page 2 created/);
    assert.strictEqual(fake.documents[0].language, 'markdown');
  });

  it('stops after one page when there is only one, and does nothing when the pick is cancelled', async () => {
    const mock = new MockFetch().on('POST', '/api/v1/blackbox/bbx_1/timeline/enumerate', { json: timelinePage(1, 1) });
    assert.strictEqual((await insights(mock).showTimeline()).length, 1);
    assert.deepStrictEqual(fake.messages.map((m) => m.text), []);
    fake.reset();
    assert.strictEqual((await insights(mock, null).showTimeline()).length, 0);
    assert.strictEqual(fake.documents.length, 0);
  });

  it('shows an error for a missing entry', async () => {
    const mock = new MockFetch().on('POST', '/api/v1/blackbox/bbx_1/timeline/enumerate', { status: 404, json: { Error: 'NotFound', Data: { Code: 'BlackBoxNotFound' } } });
    assert.strictEqual((await insights(mock).showTimeline()).length, 0);
    assert.strictEqual(fake.messages[0].level, 'error');
    assert.match(fake.messages[0].text, /No black box entry was found/);
  });
});

describe('Compare Black Box Attempts', () => {
  it('offers the mission\'s other attempts and opens the diff', async () => {
    const mock = new MockFetch()
      .on('GET', '/api/v1/missions/msn_x/blackbox/entries', { json: page([summary({ Id: 'bbx_1', Attempt: 2 }), summary({ Id: 'bbx_0', Attempt: 1 })]) })
      .on('GET', '/api/v1/blackbox/bbx_1/diff/bbx_0', { json: DIFF });
    fake.quickPick.push((items: Array<{ label: string; entry?: BlackBoxEntrySummary }>) => {
      assert.deepStrictEqual(items.map((i) => i.entry?.Id ?? 'enter'), ['bbx_0', 'enter']);
      return items[0];
    });
    const diff = await insights(mock).compareAttempts();
    assert.strictEqual(diff?.DifferenceCount, 2);
    assert.match(fake.documents[0].content ?? '', /\| Subject \| Model \| opus \| sonnet \|/);
  });

  it('asks for an entry id when the mission has no other attempts', async () => {
    const mock = new MockFetch()
      .on('GET', '/api/v1/missions/msn_x/blackbox/entries', { json: page([summary()]) })
      .on('GET', '/api/v1/blackbox/bbx_1/diff/bbx_7', { json: { ...DIFF, OtherEntryId: 'bbx_7' } });
    fake.inputBox.push((options: { prompt: string; validateInput: (v: string) => string | undefined }) => {
      assert.match(options.prompt, /no other attempts/);
      assert.strictEqual(options.validateInput('bbx_1'), 'Pick a different entry');
      return ' bbx_7 ';
    });
    const diff = await insights(mock).compareAttempts();
    assert.strictEqual(diff?.OtherEntryId, 'bbx_7');
  });

  it('uses an explicit other id and returns nothing when the id prompt is cancelled', async () => {
    const mock = new MockFetch()
      .on('GET', '/api/v1/missions/msn_x/blackbox/entries', { json: page([summary()]) })
      .on('GET', '/api/v1/blackbox/bbx_1/diff/bbx_3', { json: DIFF });
    assert.ok(await insights(mock).compareAttempts(undefined, 'bbx_3'));
    assert.strictEqual(mock.calls('GET', '/api/v1/missions/msn_x/blackbox/entries').length, 0);
    fake.reset();
    assert.strictEqual(await insights(mock).compareAttempts(), undefined);
    assert.strictEqual(fake.documents.length, 0);
  });

  it('shows the server error when the diff fails', async () => {
    const mock = new MockFetch()
      .on('GET', '/api/v1/missions/msn_x/blackbox/entries', { json: page([summary()]) })
      .on('GET', '/api/v1/blackbox/bbx_1/diff/bbx_3', { status: 400, json: { Error: 'BadRequest', Message: 'same entry' } });
    assert.strictEqual(await insights(mock).compareAttempts(undefined, 'bbx_3'), undefined);
    assert.match(fake.messages[0].text, /same entry/);
  });
});

describe('Preview Mission Overlap', () => {
  function overlapMock(preview: unknown = PREVIEW, vessels = [VESSEL_A, VESSEL_B]): MockFetch {
    return new MockFetch()
      .on('GET', '/api/v1/vessels', { json: page(vessels) })
      .on('POST', '/api/v1/blackbox/enumerate', { json: page([]) })
      .on('POST', '/api/v1/blackbox/hotspots/enumerate', { json: page([]) })
      .on('POST', '/api/v1/vessels/vsl_a/overlap-preview', typeof preview === 'object' && preview && 'status' in preview ? (preview as { status: number; json: unknown }) : { json: preview });
  }

  it('uses the workspace vessel, asks for the brief and shows overlap and lint warnings', async () => {
    const mock = overlapMock();
    fake.inputBox.push('Login', 'Touch only src/auth/login.ts after mission 2 lands');
    const outcome = await insights(mock).previewOverlap();
    assert.strictEqual(outcome, 'shown');
    assert.strictEqual(fake.quickPick.length, 0);
    assert.deepStrictEqual(mock.calls('POST', '/api/v1/vessels/vsl_a/overlap-preview')[0].body, { Title: 'Login', Description: 'Touch only src/auth/login.ts after mission 2 lands' });
    assert.match(fake.documents[0].content ?? '', /BRIEF-DEP-001/);
    const warning = fake.messages.find((m) => m.level === 'warning');
    assert.match(warning?.text ?? '', /1 overlap warning and 1 brief lint warning on alpha/);
  });

  it('reports a clean brief', async () => {
    const mock = overlapMock({ VesselId: 'vsl_a', Warnings: [], WarningCount: 0, BriefLint: { Findings: [], FindingCount: 0, Clean: true } });
    fake.inputBox.push('', 'Add a README section');
    assert.strictEqual(await insights(mock).previewOverlap(), 'clean');
    assert.match(fake.messages[0].text, /no overlap or brief lint warnings/);
  });

  it('lets the user pick a vessel when none matches the workspace', async () => {
    const mock = overlapMock();
    fake.quickPick.push((items: Array<{ label: string; vessel: { Id: string } }>, options: { placeHolder: string }) => {
      assert.match(options.placeHolder, /No vessel matches the open repository/);
      return items.find((i) => i.vessel.Id === 'vsl_a');
    });
    fake.inputBox.push('t', 'b');
    assert.strictEqual(await insights(mock, summary(), { remotes: {} }).previewOverlap(), 'shown');
  });

  it('says so when the server has no vessels', async () => {
    const mock = overlapMock(PREVIEW, []);
    assert.strictEqual(await insights(mock, summary(), { remotes: {} }).previewOverlap(), 'noVessel');
    assert.match(fake.messages[0].text, /no vessels are registered/);
  });

  it('is cancelled when the brief prompt is dismissed, and validates an empty brief', async () => {
    const mock = overlapMock();
    fake.inputBox.push('', (options: { validateInput: (v: string) => string | undefined }) => {
      assert.strictEqual(options.validateInput('  '), 'Enter a brief');
      return undefined;
    });
    assert.strictEqual(await insights(mock).previewOverlap(), 'cancelled');
    assert.strictEqual(mock.calls('POST', '/api/v1/vessels/vsl_a/overlap-preview').length, 0);
  });

  it('shows the error when the vessel is not visible', async () => {
    const mock = overlapMock({ status: 404, json: { Error: 'NotFound', Message: 'Vessel not found' } });
    fake.inputBox.push('t', 'b');
    assert.strictEqual(await insights(mock).previewOverlap(), 'failed');
    assert.match(fake.messages[0].text, /Vessel not found/);
  });

  it('takes an explicit vessel id', async () => {
    const mock = overlapMock();
    fake.inputBox.push('t', 'b');
    assert.strictEqual(await insights(mock, summary(), { remotes: {} }).previewOverlap('vsl_a'), 'shown');
    assert.strictEqual(mock.calls('GET', '/api/v1/vessels').length, 0);
  });
});

describe('entry view insight extras', () => {
  const withPaths = () => fullEntry({
    Record: {
      Analysis: {
        RecoveryPaths: [
          { Id: 'rp1', Title: 'Retry landing', SuccessRate: 0.666, TrackedRuns: 3, RunbookId: 'rbk_1', RunbookTitle: 'Landing <runbook>' },
          { Id: 'rp2', Title: 'Untracked' },
        ],
      },
    },
  });

  it('shows success rates, runbook links and the vessel doctor', () => {
    const html = renderEntryHtml(withPaths(), [], 'n', 'c', {
      dashboardUrl: 'http://armada.test:7890/dashboard/',
      doctor: { ...page([{ Key: 'k', Setting: 'LandingMode', CurrentValue: 'LocalMerge', SuggestedValue: 'PullRequest', Count: 6, ExampleEntryId: 'bbx_9', RuleId: 'BBX-LAND-001', Applicable: true }], 8), WindowDays: 30 },
    });
    assert.ok(html.includes('recovered 67% of 3 tracked runs'));
    assert.ok(html.includes('<a href="http://armada.test:7890/dashboard/runbooks/rbk_1">Landing &lt;runbook&gt;</a>'));
    assert.ok(html.includes('<h2>Recurring on this vessel (last 30 days)</h2>'));
    assert.ok(html.includes('recommended 6 times by <code>BBX-LAND-001</code>'));
    assert.ok(html.includes('href="http://armada.test:7890/dashboard/blackbox/bbx_9"'));
    assert.ok(html.includes('7 more recurring recommendations'));
    assert.ok(html.includes('data-action="timeline"'));
    assert.ok(html.includes('data-action="compareAttempts"'));
  });

  it('omits the doctor and links without data', () => {
    const html = renderEntryHtml(withPaths(), [], 'n', 'c');
    assert.ok(!html.includes('Recurring on this vessel'));
    assert.ok(html.includes('Runbook: Landing &lt;runbook&gt; <code>rbk_1</code>'));
    assert.strictEqual(recoveryExtras({ Id: 'x', Title: 'x' }, ''), '');
    assert.ok(!renderEntryHtml(withPaths(), [], 'n', 'c', { doctor: { ...page([]), WindowDays: 30 } }).includes('Recurring on this vessel'));
  });

  it('accepts the new webview actions', () => {
    assert.ok(isEntryViewMessage({ action: 'timeline' }));
    assert.ok(isEntryViewMessage({ action: 'compareAttempts' }));
    assert.ok(!isEntryViewMessage({ action: 'diffEverything' }));
  });
});
