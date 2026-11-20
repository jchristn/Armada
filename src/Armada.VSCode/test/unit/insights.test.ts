import * as assert from 'assert';
import { beforeEach, describe, it } from 'node:test';
import type { BlackBoxAttemptDiff, BlackBoxDoctorPage, BlackBoxEntrySummary, BlackBoxTimelinePage, MissionOverlapPreview } from '@dashboard/types/blackbox';
import { InsightCommands, TIMELINE_NEXT } from '../../src/surfaces/blackbox/insightCommands';
import { isEntryViewMessage, recoveryExtras, renderEntryHtml } from '../../src/surfaces/blackbox/entryHtml';
import { cell, renderDiffMarkdown, renderOverlapMarkdown, renderTimelineMarkdown } from '../../src/surfaces/blackbox/insightMarkdown';
import { MockFetch, page } from '../fakes/fetch';
import { fromServer, fullEntry, SERVER_VESSEL_A, SERVER_VESSEL_B, summary } from '../fakes/fixtures';
import { makeStore } from '../fakes/storeHelpers';
import { fake } from '../fakes/vscode';

type Server = Record<string, unknown>;

/** A timeline page in the server's shape. */
function timelinePage(pageNumber: number, totalPages: number, overrides: Server = {}): Server {
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

const SERVER_DIFF: Server = {
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

const DIFF = fromServer<BlackBoxAttemptDiff>(SERVER_DIFF);

const SERVER_PREVIEW: Server = {
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

const PREVIEW = fromServer<MissionOverlapPreview>(SERVER_PREVIEW);

function insights(mock: MockFetch, picked: BlackBoxEntrySummary | null = summary(), storeOptions: Parameters<typeof makeStore>[1] = {}): InsightCommands {
  const store = makeStore(mock, storeOptions);
  return new InsightCommands({ requireConnection: () => ({ tenant: undefined, dashboardUrl: 'http://armada.test:7890/dashboard' }), store, pickEntry: async () => picked ?? undefined });
}

beforeEach(() => fake.reset());

describe('insight routes through the shared client', () => {
  it('pages the timeline oldest first with GET query parameters', async () => {
    const mock = new MockFetch().on('GET', '/api/v1/blackbox/bbx_1/timeline', { json: timelinePage(1, 1) });
    await insights(mock).showTimeline();
    const query = mock.calls('GET', '/api/v1/blackbox/bbx_1/timeline')[0].query;
    assert.deepStrictEqual([query.get('order'), query.get('pageNumber'), query.get('pageSize')], ['CreatedAscending', '1', '100']);
  });

  it('posts the overlap preview with the trimmed title and brief and reads the camelized result', async () => {
    const mock = new MockFetch()
      .on('GET', '/api/v1/vessels', { json: page([SERVER_VESSEL_A]) })
      .on('POST', '/api/v1/blackbox/enumerate', { json: page([]) })
      .on('POST', '/api/v1/blackbox/hotspots/enumerate', { json: page([]) })
      .on('POST', '/api/v1/vessels/vsl_a/overlap-preview', { json: SERVER_PREVIEW });
    fake.inputBox.push('  Login  ', ' Touch only src/auth/login.ts ');
    assert.strictEqual(await insights(mock).previewOverlap(), 'shown');
    assert.deepStrictEqual(mock.calls('POST', '/api/v1/vessels/vsl_a/overlap-preview')[0].body, { Title: 'Login', Description: 'Touch only src/auth/login.ts' });
  });
});

describe('insight Markdown', () => {
  it('renders a timeline page with escaped cells and source counts', () => {
    const md = renderTimelineMarkdown(summary(), fromServer<BlackBoxTimelinePage>(timelinePage(1, 2, { Truncated: true })));
    assert.match(md, /Page 1 of 2, 4 events in all/);
    assert.ok(md.includes('merge \\| conflicted'));
    assert.match(md, /\| Entry \| 1 \| yes \|/);
    assert.match(md, /Some sources were capped/);
  });

  it('renders an empty timeline', () => {
    const md = renderTimelineMarkdown(summary(), fromServer<BlackBoxTimelinePage>({ ...timelinePage(1, 0), Objects: [], TotalRecords: 0, Sources: [] }));
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
    const md = renderDiffMarkdown(fromServer<BlackBoxAttemptDiff>({ ...SERVER_DIFF, Differences: [], DifferenceCount: 0, ChangedFilesKnown: false }), 'a', 'b');
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
    const md = renderOverlapMarkdown(fromServer<MissionOverlapPreview>({ VesselId: 'vsl_a', Warnings: [], WarningCount: 0, BriefLint: { Findings: [], FindingCount: 0, Clean: true }, TrackedFilesKnown: false }), 'alpha', '');
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
    const mock = new MockFetch().on('GET', '/api/v1/blackbox/bbx_1/timeline', (req) => ({ json: timelinePage(Number(req.query.get('pageNumber')), 2) }));
    fake.info.push(TIMELINE_NEXT);
    const pages = await insights(mock).showTimeline();
    assert.deepStrictEqual(pages.map((p) => p.pageNumber), [1, 2]);
    assert.strictEqual(fake.documents.length, 2);
    assert.match(fake.documents[1].content ?? '', /page 2 created/);
    assert.strictEqual(fake.documents[0].language, 'markdown');
  });

  it('stops after one page when there is only one, and does nothing when the pick is cancelled', async () => {
    const mock = new MockFetch().on('GET', '/api/v1/blackbox/bbx_1/timeline', { json: timelinePage(1, 1) });
    assert.strictEqual((await insights(mock).showTimeline()).length, 1);
    assert.deepStrictEqual(fake.messages.map((m) => m.text), []);
    fake.reset();
    assert.strictEqual((await insights(mock, null).showTimeline()).length, 0);
    assert.strictEqual(fake.documents.length, 0);
  });

  it('shows an error for a missing entry', async () => {
    const mock = new MockFetch().on('GET', '/api/v1/blackbox/bbx_1/timeline', { status: 404, json: { Error: 'NotFound', Data: { Code: 'BlackBoxNotFound' } } });
    assert.strictEqual((await insights(mock).showTimeline()).length, 0);
    assert.strictEqual(fake.messages[0].level, 'error');
    assert.match(fake.messages[0].text, /No black box entry was found/);
  });
});

describe('Compare Black Box Attempts', () => {
  it('offers the mission\'s other attempts and opens the diff', async () => {
    const mock = new MockFetch()
      .on('GET', '/api/v1/missions/msn_x/blackbox/entries', { json: page([summary({ Id: 'bbx_1', Attempt: 2 }), summary({ Id: 'bbx_0', Attempt: 1 })]) })
      .on('GET', '/api/v1/blackbox/bbx_1/diff/bbx_0', { json: SERVER_DIFF });
    fake.quickPick.push((items: Array<{ label: string; entry?: BlackBoxEntrySummary }>) => {
      assert.deepStrictEqual(items.map((i) => i.entry?.id ?? 'enter'), ['bbx_0', 'enter']);
      return items[0];
    });
    const diff = await insights(mock).compareAttempts();
    assert.strictEqual(diff?.differenceCount, 2);
    assert.match(fake.documents[0].content ?? '', /\| Subject \| Model \| opus \| sonnet \|/);
  });

  it('asks for an entry id when the mission has no other attempts', async () => {
    const mock = new MockFetch()
      .on('GET', '/api/v1/missions/msn_x/blackbox/entries', { json: page([summary()]) })
      .on('GET', '/api/v1/blackbox/bbx_1/diff/bbx_7', { json: { ...SERVER_DIFF, OtherEntryId: 'bbx_7' } });
    fake.inputBox.push((options: { prompt: string; validateInput: (v: string) => string | undefined }) => {
      assert.match(options.prompt, /no other attempts/);
      assert.strictEqual(options.validateInput('bbx_1'), 'Pick a different entry');
      return ' bbx_7 ';
    });
    const diff = await insights(mock).compareAttempts();
    assert.strictEqual(diff?.otherEntryId, 'bbx_7');
  });

  it('uses an explicit other id and returns nothing when the id prompt is cancelled', async () => {
    const mock = new MockFetch()
      .on('GET', '/api/v1/missions/msn_x/blackbox/entries', { json: page([summary()]) })
      .on('GET', '/api/v1/blackbox/bbx_1/diff/bbx_3', { json: SERVER_DIFF });
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
  function overlapMock(preview: unknown = SERVER_PREVIEW, vessels = [SERVER_VESSEL_A, SERVER_VESSEL_B]): MockFetch {
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
    fake.quickPick.push((items: Array<{ label: string; vessel: { id: string } }>, options: { placeHolder: string }) => {
      assert.match(options.placeHolder, /No vessel matches the open repository/);
      return items.find((i) => i.vessel.id === 'vsl_a');
    });
    fake.inputBox.push('t', 'b');
    assert.strictEqual(await insights(mock, summary(), { remotes: {} }).previewOverlap(), 'shown');
  });

  it('says so when the server has no vessels', async () => {
    const mock = overlapMock(SERVER_PREVIEW, []);
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
      doctor: fromServer<BlackBoxDoctorPage>({ ...page([{ Key: 'k', Setting: 'LandingMode', CurrentValue: 'LocalMerge', SuggestedValue: 'PullRequest', Count: 6, ExampleEntryId: 'bbx_9', RuleId: 'BBX-LAND-001', Applicable: true }], 8), WindowDays: 30 }),
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
    assert.strictEqual(recoveryExtras({ id: 'x', rank: 1, title: 'x' }, ''), '');
    assert.ok(!renderEntryHtml(withPaths(), [], 'n', 'c', { doctor: fromServer<BlackBoxDoctorPage>({ ...page([]), WindowDays: 30 }) }).includes('Recurring on this vessel'));
  });

  it('accepts the new webview actions', () => {
    assert.ok(isEntryViewMessage({ action: 'timeline' }));
    assert.ok(isEntryViewMessage({ action: 'compareAttempts' }));
    assert.ok(!isEntryViewMessage({ action: 'diffEverything' }));
  });
});
