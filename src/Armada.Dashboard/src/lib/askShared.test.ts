import { describe, expect, it } from 'vitest';
import { formatToolMs, formatTurnDuration, prettyJson, toolCallsToEvents, toolResultPreview } from './askFormat';
import { askCaptainAccess, instructionsDocUrl } from './askCaptain';
import { entityTypeLabel } from './askWork';
import { inboxActionLabel, isApprovalKind, splitInbox } from './inboxKinds';
import type { CaptainToolAccessResult, InboxItem } from '../types/models';

const t = (text: string, params?: Record<string, string | number | null | undefined>) =>
  text.replace(/\{\{(\w+)\}\}/g, (_m, k: string) => String(params?.[k] ?? ''));

describe('askFormat', () => {
  it('pretty-prints JSON and passes other text through', () => {
    expect(prettyJson('{"a":1}')).toBe('{\n  "a": 1\n}');
    expect(prettyJson('not json')).toBe('not json');
    expect(prettyJson(null)).toBe('');
  });

  it('previews a result on one line, truncated with an ellipsis', () => {
    expect(toolResultPreview('{ "a" :  1 }')).toBe('{"a":1}');
    expect(toolResultPreview('x'.repeat(100))).toBe('x'.repeat(80) + '\u2026');
  });

  it('formats tool and turn durations', () => {
    expect(formatToolMs(null)).toBe('');
    expect(formatToolMs(250.4)).toBe('250ms');
    expect(formatToolMs(1500)).toBe('1.50s');
    expect(formatToolMs(12000)).toBe('12.0s');
    expect(formatTurnDuration(1500)).toBe('1.5s');
    expect(formatTurnDuration(12345)).toBe('12s');
  });

  it('maps persisted tool calls to chip events', () => {
    const events = toolCallsToEvents([
      { callId: 'a', toolName: 'status', ok: true, resultText: 'ok' },
      { callId: '', toolName: '', ok: null },
      { callId: 'c', toolName: 'Bash', ok: true, permissionDenied: true },
    ]);
    expect(events.map((e) => e.status)).toEqual(['success', 'running', 'failed']);
    expect(events[1]).toMatchObject({ id: 'call-1', name: 'tool' });
    expect(events[2].permissionDenied).toBe(true);
    expect('permissionDenied' in events[0]).toBe(false);
  });
});

describe('askCaptain', () => {
  const tools = (over: Partial<CaptainToolAccessResult>) => ({ runtime: 'Codex', armadaToolCount: 0, ...over }) as CaptainToolAccessResult;

  it('reports no captain, missing MCP, and ungated runtimes', () => {
    expect(askCaptainAccess('', null)).toEqual({ noCaptain: true, mcpMissing: false, ungated: false });
    expect(askCaptainAccess('cpt_1', null)).toEqual({ noCaptain: false, mcpMissing: false, ungated: false });
    expect(askCaptainAccess('cpt_1', tools({}))).toMatchObject({ mcpMissing: true, ungated: false });
    expect(askCaptainAccess('cpt_1', tools({ armadaToolCount: 5 }))).toMatchObject({ mcpMissing: false, ungated: true });
    expect(askCaptainAccess('cpt_1', tools({ askApprovalGated: true }))).toMatchObject({ mcpMissing: false, ungated: false });
    expect(askCaptainAccess('cpt_1', tools({ runtime: 'ClaudeCode' }))).toMatchObject({ mcpMissing: false, ungated: false });
  });

  it('links the runtime instructions', () => {
    expect(instructionsDocUrl('Codex')).toMatch(/INSTRUCTIONS_FOR_CODEX\.md$/);
    expect(instructionsDocUrl(null)).toMatch(/MCP_API\.md$/);
  });
});

describe('entityTypeLabel', () => {
  it('labels known types and passes unknown ones through', () => {
    expect(entityTypeLabel(t, 'FleetActionRun')).toBe('Fleet action run');
    expect(entityTypeLabel(t, 'Other')).toBe('Other');
    expect(entityTypeLabel(t, null)).toBe('');
  });
});

describe('inboxKinds', () => {
  const item = (kind: string): InboxItem => ({ kind, severity: 'Info', title: kind, detail: '', entityType: null, entityId: kind, href: '/' });

  it('splits decisions from interventions in server order', () => {
    const items = [item('failed'), item('review'), item('stalled_captain'), item('cli_permission'), item('ask_proposal'), item('deployment_approval')];
    const { approvals, interventions } = splitInbox(items);
    expect(approvals.map((i) => i.kind)).toEqual(['review', 'cli_permission', 'ask_proposal', 'deployment_approval']);
    expect(interventions.map((i) => i.kind)).toEqual(['failed', 'stalled_captain']);
    expect(isApprovalKind('merge_failed')).toBe(false);
  });

  it('labels the open action per kind', () => {
    expect(inboxActionLabel(t, { kind: 'ask_proposal' })).toBe('Open conversation');
    expect(inboxActionLabel(t, { kind: 'failed' })).toBe('Open');
  });
});
