import { describe, expect, it } from 'vitest';
import { blackBoxAskPrompt, blackBoxChips, cardFollowUps, followUpArguments, parseBlackBoxCommand, shortCommit } from './askBlackBox';
import { ASK_PREFILL_QUERY_PARAM, askPrefillPath, readAskPrefill } from './askNavigation';
import { quickActionForm } from './askQuickActions';
import { buildCommandCatalog } from './askCommands';

describe('Ask black box helpers', () => {
  it('uses the canonical question of the black box entry points', () => {
    expect(blackBoxAskPrompt('msn_abc')).toBe('Show me the pertinent details from the black box for mission msn_abc and give me your opinion on why it failed and what could have been done differently');
  });

  it('parses /blackbox arguments: a mission id and an optional question', () => {
    expect(parseBlackBoxCommand('msn_1abc_X2 why did it fail?')).toEqual({ missionId: 'msn_1abc_X2', question: 'why did it fail?' });
    expect(parseBlackBoxCommand('  msn_only ')).toEqual({ missionId: 'msn_only', question: '' });
    expect(parseBlackBoxCommand('')).toBeNull();
    expect(parseBlackBoxCommand('why did msn_1 fail')).toBeNull();
    expect(parseBlackBoxCommand('vyg_1')).toBeNull();
  });

  it('builds status chips from the digest, skipping unknown values and an inline capture', () => {
    const chips = blackBoxChips({ entryId: 'bbx_1', triage: 'UserConfig', triageConfidence: 'High', kind: 'LandingConflict', phase: 'Landing', step: 'TargetIntegration', captureMode: 'Sweeper', resolution: 'Open' });
    expect(chips.map((c) => [c.key, c.value])).toEqual([
      ['triage', 'UserConfig (High)'], ['kind', 'LandingConflict'], ['step', 'Landing / TargetIntegration'], ['capture', 'Sweeper'], ['resolution', 'Open'],
    ]);
    expect(chips.find((c) => c.key === 'capture')?.tone).toBe('warn');
    expect(blackBoxChips({ entryId: 'bbx_2', triage: 'Unknown', captureMode: 'Inline', resolution: 'Resolved' }).map((c) => c.key)).toEqual(['resolution']);
    expect(blackBoxChips({ entryId: 'bbx_2', resolution: 'Resolved' })[0].tone).toBe('good');
    expect(blackBoxChips(null)).toEqual([]);
  });

  it('reads follow-up arguments and drops empty follow-ups', () => {
    expect(followUpArguments({ name: 'fix', label: 'Fix it', toolName: 'run_blackbox_recovery', argumentsText: '{"entryId":"bbx_1","pathId":"p1"}' })).toEqual({ entryId: 'bbx_1', pathId: 'p1' });
    expect(followUpArguments({ name: 'fix', label: 'Fix it', argumentsText: '[1]' })).toEqual({});
    expect(followUpArguments({ name: 'fix', label: 'Fix it', argumentsText: 'not json' })).toEqual({});
    expect(cardFollowUps({ digest: { entryId: 'bbx_1' }, followUps: [{ name: 'x', label: 'X' }, { name: 'compare', label: 'Compare with similar failures', prompt: 'Compare' }] }).map((f) => f.name)).toEqual(['compare']);
    expect(shortCommit('0123456789abcdef')).toBe('0123456789');
  });

  it('opens the /blackbox form and shows its usage in the menu', () => {
    const action = { name: 'blackbox', command: '/blackbox', toolName: 'get_blackbox', title: 'Black box of a failure' };
    expect(quickActionForm(action)).toBe('blackbox');
    expect(buildCommandCatalog([action])[0].usage).toBe('<missionId> [question]');
  });
});

describe('Ask prefill query parameter', () => {
  it('is named prompt and round-trips encoded text', () => {
    expect(ASK_PREFILL_QUERY_PARAM).toBe('prompt');
    const path = askPrefillPath('Why did msn_1 fail? & how');
    expect(path).toBe('/ask?prompt=Why%20did%20msn_1%20fail%3F%20%26%20how');
    expect(readAskPrefill(path.slice('/ask'.length))).toEqual({ text: 'Why did msn_1 fail? & how', rest: '' });
  });

  it('keeps other parameters and ignores a blank prompt', () => {
    expect(readAskPrefill('?a=1&prompt=hi&b=2')).toEqual({ text: 'hi', rest: '?a=1&b=2' });
    expect(readAskPrefill('?prompt=%20')).toBeNull();
    expect(readAskPrefill('')).toBeNull();
  });
});
