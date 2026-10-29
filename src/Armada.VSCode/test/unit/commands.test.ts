import * as assert from 'assert';
import { beforeEach, describe, it } from 'node:test';
import * as vscode from 'vscode';
import { ArmadaApiError, askArmadaPrompt } from '../../src/api/blackbox';
import { BlackBoxEntrySummary, BlackBoxRecoveryResult } from '../../src/api/types';
import { BlackBoxCommands, CHAT_OPEN_COMMAND, DATA_LOSS_ACK_LABEL, describePlan, entryFromArg } from '../../src/commands/blackBoxCommands';
import { ArmadaSettings } from '../../src/config';
import { MockFetch, page } from '../fakes/fetch';
import { client, fullEntry, summary } from '../fakes/fixtures';
import { makeStore } from '../fakes/storeHelpers';
import { fake, Uri } from '../fakes/vscode';

const SETTINGS: ArmadaSettings = {
  url: 'http://armada.test:7890',
  tenant: '',
  authHeader: 'Bearer',
  dashboardUrl: 'http://armada.test:7890/dashboard',
  briefAudience: 'agent',
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

const NODE = { kind: 'entry', entry: summary() };

function recoveryMock(dry: Partial<BlackBoxRecoveryResult>, real: Partial<BlackBoxRecoveryResult> | { status: number; json: unknown } = { Succeeded: true, Message: 'landed' }): MockFetch {
  return new MockFetch()
    .on('GET', '/api/v1/blackbox/bbx_1', { json: fullEntry() })
    .on('POST', '/api/v1/blackbox/enumerate', { json: page([]) })
    .on('GET', '/api/v1/vessels', { json: page([]) })
    .on('POST', '/api/v1/blackbox/bbx_1/recovery/rp1', (req) => ((req.body as { DryRun: boolean }).DryRun ? { json: { DryRun: true, ...dry } } : 'status' in real ? real : { json: real }))
    .on('POST', '/api/v1/blackbox/bbx_1/recovery/rp2', (req) => ((req.body as { DryRun: boolean }).DryRun ? { json: { DryRun: true, ...dry } } : { json: { Success: true } }));
}

function commands(mock: MockFetch, settings: Partial<ArmadaSettings> = {}, opened: BlackBoxEntrySummary[] = []): BlackBoxCommands {
  const c = client(mock);
  return new BlackBoxCommands({
    getClient: () => c,
    store: makeStore(mock),
    getSettings: () => ({ ...SETTINGS, ...settings }),
    storageUri: Uri.file('/tmp/armada-vscode-test-storage') as unknown as vscode.Uri,
    openEntryView: async (entry) => {
      opened.push(entry);
    },
  });
}

function realRuns(mock: MockFetch, pathId = 'rp1'): unknown[] {
  return mock.calls('POST', `/api/v1/blackbox/bbx_1/recovery/${pathId}`).map((r) => r.body).filter((b) => (b as { DryRun: boolean }).DryRun === false);
}

beforeEach(() => fake.reset());

describe('Run Recovery Path', () => {
  it('dry runs, shows the plan in a modal, and runs with the confirm token after confirmation', async () => {
    const mock = recoveryMock({ DryRun: true, ConfirmToken: 'tok-1', Plan: [{ Order: 1, Description: 'Retry the landing', Action: { Tool: 'retry_landing', Arguments: { missionId: 'msn_x' } } }] });
    fake.warning.push((_text: string, options: { modal?: boolean; detail?: string }, items: string[]) => {
      assert.strictEqual(options.modal, true);
      assert.match(options.detail ?? '', /1\. Retry the landing \(retry_landing \{"missionId":"msn_x"\}\)/);
      assert.match(options.detail ?? '', /Expected outcome: The mission lands/);
      assert.deepStrictEqual(items, ['Run']);
      return 'Run';
    });
    const outcome = await commands(mock).runRecoveryPath(NODE, 'rp1');
    assert.strictEqual(outcome, 'ran');
    const calls = mock.calls('POST', '/api/v1/blackbox/bbx_1/recovery/rp1').map((r) => r.body);
    assert.deepStrictEqual(calls, [{ DryRun: true, AuthorKind: 'Operator' }, { DryRun: false, ConfirmToken: 'tok-1', AuthorKind: 'Operator' }]);
    assert.ok(fake.messages.some((m) => m.level === 'info' && /ran\. landed/.test(m.text)));
  });

  it('lets the user pick the path from a quick pick', async () => {
    const mock = recoveryMock({ ConfirmToken: 'tok' });
    fake.quickPick.push((items: Array<{ label: string; description: string; path: { Id: string } }>) => {
      assert.strictEqual(items.length, 2);
      assert.match(items[1].description, /DATA LOSS RISK/);
      return items[0];
    });
    fake.warning.push('Run');
    assert.strictEqual(await commands(mock).runRecoveryPath(NODE), 'ran');
  });

  it('runs nothing when the modal is dismissed', async () => {
    const mock = recoveryMock({ ConfirmToken: 'tok' });
    fake.warning.push(undefined);
    assert.strictEqual(await commands(mock).runRecoveryPath(NODE, 'rp1'), 'cancelled');
    assert.deepStrictEqual(realRuns(mock), []);
  });

  it('runs nothing when the path quick pick is cancelled', async () => {
    const mock = recoveryMock({ ConfirmToken: 'tok' });
    fake.quickPick.push(undefined);
    assert.strictEqual(await commands(mock).runRecoveryPath(NODE), 'cancelled');
    assert.strictEqual(mock.calls('POST', '/api/v1/blackbox/bbx_1/recovery/rp1').length, 0);
  });

  it('refuses a dataLossRisk path when the acknowledgement is not ticked', async () => {
    const mock = recoveryMock({ ConfirmToken: 'tok' });
    fake.quickPick.push((items: Array<{ label: string }>, options: { canPickMany?: boolean }) => {
      assert.strictEqual(options.canPickMany, true);
      assert.strictEqual(items[0].label, DATA_LOSS_ACK_LABEL);
      return [];
    });
    assert.strictEqual(await commands(mock).runRecoveryPath(NODE, 'rp2'), 'refusedDataLoss');
    assert.deepStrictEqual(realRuns(mock, 'rp2'), []);
    assert.ok(fake.messages.some((m) => m.level === 'warning' && /refused/.test(m.text)));
  });

  it('refuses a dataLossRisk path when the acknowledgement is escaped', async () => {
    const mock = recoveryMock({ ConfirmToken: 'tok' });
    fake.quickPick.push(undefined);
    assert.strictEqual(await commands(mock).runRecoveryPath(NODE, 'rp2'), 'refusedDataLoss');
    assert.deepStrictEqual(realRuns(mock, 'rp2'), []);
  });

  it('refuses when a dry run plan step (not the record) is marked data loss risk and it is not acknowledged', async () => {
    const mock = recoveryMock({ ConfirmToken: 'tok', Plan: [{ Order: 1, Description: 'Delete the branch', DataLossRisk: true }] });
    fake.quickPick.push([]);
    assert.strictEqual(await commands(mock).runRecoveryPath(NODE, 'rp1'), 'refusedDataLoss');
    assert.deepStrictEqual(realRuns(mock), []);
  });

  it('runs a dataLossRisk path with AcknowledgeDataLoss after the box is ticked and confirmed', async () => {
    const mock = recoveryMock({ ConfirmToken: 'tok' });
    fake.quickPick.push((items: unknown[]) => items);
    fake.warning.push((_t: string, options: { detail?: string }) => {
      assert.match(options.detail ?? '', /dataLossRisk/);
      return 'Run';
    });
    assert.strictEqual(await commands(mock).runRecoveryPath(NODE, 'rp2'), 'ran');
    assert.deepStrictEqual(realRuns(mock, 'rp2'), [{ DryRun: false, ConfirmToken: 'tok', AcknowledgeDataLoss: true, AuthorKind: 'Operator' }]);
  });

  it('refuses when the dry run returns no confirm token', async () => {
    const mock = recoveryMock({ Message: 'precondition failed' });
    assert.strictEqual(await commands(mock).runRecoveryPath(NODE, 'rp1'), 'noToken');
    assert.deepStrictEqual(realRuns(mock), []);
    assert.ok(fake.messages.some((m) => m.level === 'error' && /no confirm token/.test(m.text)));
  });

  it('explains an expired token or changed mission (409)', async () => {
    const mock = recoveryMock({ ConfirmToken: 'tok' }, { status: 409, json: { Error: 'Conflict', Message: 'token expired' } });
    fake.warning.push('Run');
    assert.strictEqual(await commands(mock).runRecoveryPath(NODE, 'rp1'), 'conflict');
    assert.ok(fake.messages.some((m) => /confirm token expired.*token expired/.test(m.text)));
  });

  it('reports step errors', async () => {
    const mock = recoveryMock({ ConfirmToken: 'tok' }, { Succeeded: false, StepResults: [{ Order: 1, Tool: 'retry_landing', Succeeded: false, Message: 'merge conflict again' }] });
    fake.warning.push('Run');
    assert.strictEqual(await commands(mock).runRecoveryPath(NODE, 'rp1'), 'ranWithErrors');
    assert.ok(fake.messages.some((m) => /merge conflict again/.test(m.text)));
  });

  it('reports an entry with no recovery paths', async () => {
    const mock = new MockFetch().on('GET', '/api/v1/blackbox/bbx_1', { json: fullEntry({ Record: {} }) });
    assert.strictEqual(await commands(mock).runRecoveryPath(NODE), 'noPaths');
  });

  it('shows a 503 error and never throws', async () => {
    const mock = new MockFetch().otherwise(() => ({ status: 503, json: { Error: 'ServiceUnavailable', Data: { Code: 'BlackBoxStoreUnavailable' } } }));
    assert.strictEqual(await commands(mock).runRecoveryPath(NODE), 'failed');
    assert.ok(fake.messages.some((m) => m.level === 'error' && /store is unavailable/.test(m.text)));
  });

  it('describes the plan from the path action when the dry run lists no steps', () => {
    const text = describePlan({ Id: 'rp', Title: 't', Action: { Tool: 'update_vessel', Arguments: { landingMode: 'PullRequest' } } }, { ConfirmTokenExpiresUtc: '2026-10-28T10:10:00Z' }, false);
    assert.match(text, /Will run: update_vessel \{"landingMode":"PullRequest"\}/);
    assert.match(text, /expires at 2026-10-28T10:10:00Z/);
  });
});

describe('FIX THIS commands', () => {
  function briefMock(): MockFetch {
    return new MockFetch().on('GET', '/api/v1/blackbox/bbx_1/brief', { text: '# FIX THIS brief' });
  }

  it('copies the brief', async () => {
    const mock = briefMock();
    assert.strictEqual(await commands(mock, { briefAudience: 'user' }).copyFixThis(NODE), '# FIX THIS brief');
    assert.strictEqual(fake.clipboard, '# FIX THIS brief');
    assert.ok(mock.requests[0].url.endsWith('audience=user'));
  });

  it('opens the brief as an untitled Markdown document with a preview', async () => {
    await commands(briefMock()).openFixThisInEditor(NODE);
    assert.strictEqual(fake.documents.length, 1);
    assert.strictEqual(fake.documents[0].language, 'markdown');
    assert.strictEqual(fake.documents[0].content, '# FIX THIS brief');
    assert.ok(fake.executed.some((e) => e.command === 'markdown.showPreviewToSide'));
  });

  it('skips the preview when turned off', async () => {
    await commands(briefMock(), { previewFixThis: false }).openFixThisInEditor(NODE);
    assert.ok(!fake.executed.some((e) => e.command === 'markdown.showPreviewToSide'));
  });

  it('opens chat with the brief attached when the host has chat', async () => {
    fake.availableCommands = [CHAT_OPEN_COMMAND];
    assert.strictEqual(await commands(briefMock()).sendFixThisToChat(NODE), 'chatWithAttachment');
    const call = fake.executed.find((e) => e.command === CHAT_OPEN_COMMAND);
    const options = call?.args[0] as { query: string; attachFiles: Uri[] };
    assert.strictEqual(options.query, 'FIX THIS');
    assert.strictEqual(fake.writtenFiles.get(options.attachFiles[0].fsPath), '# FIX THIS brief');
  });

  it('falls back to the brief as chat text when attachments are rejected', async () => {
    fake.availableCommands = [CHAT_OPEN_COMMAND];
    let first = true;
    fake.executeHandlers.set(CHAT_OPEN_COMMAND, () => {
      if (first) {
        first = false;
        throw new Error('unknown option attachFiles');
      }
    });
    assert.strictEqual(await commands(briefMock()).sendFixThisToChat(NODE), 'chatWithText');
    const calls = fake.executed.filter((e) => e.command === CHAT_OPEN_COMMAND);
    assert.strictEqual((calls[1].args[0] as { query: string }).query, 'FIX THIS\n\n# FIX THIS brief');
  });

  it('copies and explains when the host has no chat', async () => {
    fake.availableCommands = [];
    assert.strictEqual(await commands(briefMock()).sendFixThisToChat(NODE), 'copied');
    assert.strictEqual(fake.clipboard, '# FIX THIS brief');
    assert.ok(fake.messages.some((m) => /no chat/.test(m.text)));
  });

  it('shows a 404 for a mission without an entry', async () => {
    const mock = new MockFetch().on('GET', '/api/v1/missions/msn_none/blackbox', { status: 404, json: { Error: 'BlackBoxNotFound', Message: 'none' } });
    assert.strictEqual(await commands(mock).copyFixThis('msn_none'), undefined);
    assert.ok(fake.messages.some((m) => m.level === 'error' && /No black box entry was found/.test(m.text)));
    assert.strictEqual(fake.clipboard, '');
  });

  it('offers Connect on a bad key (401)', async () => {
    const mock = new MockFetch().otherwise(() => ({ status: 401, json: { Error: 'NotAuthorized' } }));
    fake.error.push('Connect');
    await commands(mock).copyFixThis(NODE);
    const err = fake.messages.find((m) => m.level === 'error');
    assert.deepStrictEqual(err?.items, ['Connect']);
    await new Promise((r) => setImmediate(r));
    assert.ok(fake.executed.some((e) => e.command === 'armada.connect'));
  });

  it('shows a clear message when not connected', async () => {
    const c = new BlackBoxCommands({
      getClient: () => {
        throw new ArmadaApiError('notConfigured', 'Armada is not connected.');
      },
      store: makeStore(undefined),
      getSettings: () => SETTINGS,
      storageUri: undefined,
    });
    assert.strictEqual(await c.copyFixThis(NODE), undefined);
    assert.ok(fake.messages.some((m) => /not connected/.test(m.text)));
  });
});

describe('Entry picking, resolve, notes and Ask Armada', () => {
  it('picks a recent failure from the quick pick and opens it', async () => {
    const mock = new MockFetch().on('POST', '/api/v1/blackbox/enumerate', { json: page([summary({ Id: 'bbx_7' })]) });
    fake.quickPick.push((items: Array<{ label: string; entry?: BlackBoxEntrySummary }>) => items.find((i) => i.entry?.Id === 'bbx_7'));
    const opened: BlackBoxEntrySummary[] = [];
    const result = await commands(mock, {}, opened).openBlackBox();
    assert.strictEqual(result?.Id, 'bbx_7');
    assert.strictEqual(opened[0].Id, 'bbx_7');
  });

  it('accepts a pasted mission id', async () => {
    const mock = new MockFetch()
      .on('POST', '/api/v1/blackbox/enumerate', { json: page([]) })
      .on('GET', '/api/v1/missions/msn_pasted/blackbox', { json: fullEntry({ Id: 'bbx_p', MissionId: 'msn_pasted' }) });
    fake.quickPick.push((items: Array<{ label: string }>) => items[0]);
    fake.inputBox.push(' msn_pasted ');
    const result = await commands(mock).openBlackBox();
    assert.strictEqual(result?.Id, 'bbx_p');
  });

  it('returns nothing when the pick is cancelled', async () => {
    const mock = new MockFetch().on('POST', '/api/v1/blackbox/enumerate', { json: page([]) });
    fake.quickPick.push(undefined);
    assert.strictEqual(await commands(mock).openBlackBox(), undefined);
  });

  it('resolves an entry with a note and link', async () => {
    const mock = new MockFetch().on('POST', '/api/v1/blackbox/bbx_1/resolve', { json: fullEntry({ Resolution: 'Resolved' }) });
    fake.quickPick.push((items: Array<{ label: string }>) => items.find((i) => i.label === 'ArmadaFixed'));
    fake.inputBox.push('fixed in 1.2.1', 'https://github.com/jchristn/armada/pull/1');
    assert.strictEqual(await commands(mock).resolveEntry(NODE), 'ArmadaFixed');
    assert.deepStrictEqual(mock.requests[0].body, { Resolution: 'ArmadaFixed', Note: 'fixed in 1.2.1', Link: 'https://github.com/jchristn/armada/pull/1' });
  });

  it('does not resolve when the note prompt is cancelled', async () => {
    const mock = new MockFetch();
    fake.quickPick.push((items: Array<{ label: string }>) => items[0]);
    fake.inputBox.push(undefined);
    assert.strictEqual(await commands(mock).resolveEntry(NODE), undefined);
    assert.strictEqual(mock.requests.length, 0);
  });

  it('adds a note and validates the input', async () => {
    const mock = new MockFetch().on('POST', '/api/v1/blackbox/bbx_1/notes', { json: { Body: 'looked at it' } });
    fake.inputBox.push((options: { validateInput: (v: string) => string | undefined }) => {
      assert.ok(options.validateInput('  '));
      assert.ok(options.validateInput('x'.repeat(8001)));
      assert.strictEqual(options.validateInput('ok'), undefined);
      return 'looked at it';
    });
    assert.strictEqual(await commands(mock).addNote(NODE), true);
    assert.deepStrictEqual(mock.requests[0].body, { Body: 'looked at it' });
  });

  it('copies the canonical prompt and opens the dashboard Ask page', async () => {
    const result = await commands(new MockFetch()).askArmada(NODE);
    assert.deepStrictEqual(result, { url: 'http://armada.test:7890/dashboard/ask', prefilled: false });
    assert.strictEqual(fake.clipboard, askArmadaPrompt('msn_x'));
    assert.deepStrictEqual(fake.openedExternal, ['http://armada.test:7890/dashboard/ask']);
  });

  it('still copies the prompt when the browser cannot be opened', async () => {
    fake.openExternalResult = false;
    await commands(new MockFetch()).askArmada(NODE);
    assert.strictEqual(fake.clipboard, askArmadaPrompt('msn_x'));
    assert.ok(fake.messages.some((m) => /could not open/.test(m.text)));
  });

  it('extracts entries from tree nodes and entry objects only', () => {
    assert.strictEqual(entryFromArg(NODE)?.Id, 'bbx_1');
    assert.strictEqual(entryFromArg(summary({ Id: 'bbx_9' }))?.Id, 'bbx_9');
    assert.strictEqual(entryFromArg({ kind: 'vessel' }), undefined);
    assert.strictEqual(entryFromArg('bbx_1'), undefined);
    assert.strictEqual(entryFromArg(undefined), undefined);
  });
});
