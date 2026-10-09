import { describe, expect, it, vi } from 'vitest';
import {
  buildCommandCatalog,
  filterCommands,
  LOCAL_COMMANDS,
  matchCaptains,
  parseCommand,
  parseThinkingArg,
  resolveSubmit,
  runLocalCommand,
  unknownCommandHint,
  type AskCommandContext,
  type AskLocalCommand,
} from './askCommands';
import { DEFAULT_QUICK_ACTIONS } from './askQuickActions';
import type { Captain } from '../types/models';

const catalog = buildCommandCatalog(DEFAULT_QUICK_ACTIONS);
const local = (name: string): AskLocalCommand => LOCAL_COMMANDS.find((c) => c.name === name)!;
const captains = [
  { id: 'cpt_1', name: 'Ada' },
  { id: 'cpt_2', name: 'Grace Hopper' },
  { id: 'cpt_3', name: 'Grace Kelly' },
] as Captain[];

function context(over: Partial<AskCommandContext> = {}): AskCommandContext {
  return {
    hasThread: true,
    archived: false,
    turnActive: false,
    captains,
    showThinking: false,
    newConversation: vi.fn(),
    openHelp: vi.fn(),
    summarize: vi.fn(),
    rename: vi.fn(),
    archive: vi.fn(),
    setCaptain: vi.fn(),
    openCaptainPicker: vi.fn(),
    setShowThinking: vi.fn(),
    ...over,
  };
}

describe('command catalog', () => {
  it('lists quick actions first, then the local commands', () => {
    expect(catalog.map((i) => i.command)).toEqual([
      '/dispatch', '/fleet-action', '/status', '/health', '/import',
      '/new', '/help', '/summarize', '/rename', '/archive', '/captain', '/thinking',
    ]);
    expect(catalog.find((i) => i.command === '/new')?.aliases).toEqual(['/clear']);
  });

  it('drops a server quick action that would shadow a local command', () => {
    const items = buildCommandCatalog([...DEFAULT_QUICK_ACTIONS, { name: 'clear', command: '/clear', toolName: 'x' }]);
    expect(items.filter((i) => i.command === '/clear')).toEqual([]);
  });

  it('filters the menu by command, alias, and name, with exact matches first', () => {
    expect(filterCommands(catalog, '/').length).toBe(catalog.length);
    expect(filterCommands(catalog, '/he').map((i) => i.command)).toEqual(['/health', '/help']);
    expect(filterCommands(catalog, '/cl').map((i) => i.command)).toEqual(['/new']);
    expect(filterCommands(catalog, '/HELP').map((i) => i.command)).toEqual(['/help']);
    expect(filterCommands(catalog, '/rename x')).toEqual([]);
    expect(filterCommands(catalog, 'hello')).toEqual([]);
    const shadowed = buildCommandCatalog([{ name: 'helpdesk', command: '/helpdesk', toolName: 't' }]);
    expect(filterCommands(shadowed, '/help').map((i) => i.command)).toEqual(['/help', '/helpdesk']);
  });
});

describe('parseCommand', () => {
  it('treats text that does not start with / as a message', () => {
    expect(parseCommand(catalog, 'hello /new')).toEqual({ kind: 'text' });
  });

  it('matches an exact command case-insensitively, with arguments', () => {
    const parsed = parseCommand(catalog, '  /Rename   Billing   retry  ');
    expect(parsed.kind).toBe('command');
    if (parsed.kind !== 'command') return;
    expect(parsed.item.local?.name).toBe('rename');
    expect(parsed.args).toBe('Billing   retry');
  });

  it('matches aliases and quick actions', () => {
    const clear = parseCommand(catalog, '/CLEAR');
    expect(clear.kind === 'command' && clear.item.local?.name).toBe('new');
    const status = parseCommand(catalog, '/status');
    expect(status.kind === 'command' && status.item.action?.name).toBe('status');
  });

  it('reports unknown commands with what was typed', () => {
    expect(parseCommand(catalog, '/foo bar')).toEqual({ kind: 'unknown', command: '/foo' });
    expect(parseCommand(catalog, '/he')).toEqual({ kind: 'unknown', command: '/he' });
    expect(unknownCommandHint('/foo')).toEqual({ ok: false, hint: 'Unknown command {{command}}. Type / to see commands', hintParams: { command: '/foo' } });
  });

  it('runs the highlighted menu entry, keeping arguments only for an exact match', () => {
    const help = catalog.find((i) => i.command === '/help')!;
    const health = catalog.find((i) => i.command === '/health')!;
    expect(resolveSubmit(catalog, '/he', health)).toEqual({ kind: 'command', item: health, args: '' });
    expect(resolveSubmit(catalog, '/help', help)).toEqual({ kind: 'command', item: help, args: '' });
    expect(resolveSubmit(catalog, '/thinking on', null)).toMatchObject({ kind: 'command', args: 'on' });
    expect(resolveSubmit(catalog, '/he', null)).toEqual({ kind: 'unknown', command: '/he' });
  });
});

describe('captain and thinking arguments', () => {
  it('matches captains by id, exact name, prefix, substring, then letters in order', () => {
    expect(matchCaptains(captains, 'cpt_3').map((c) => c.id)).toEqual(['cpt_3']);
    expect(matchCaptains(captains, 'ada').map((c) => c.id)).toEqual(['cpt_1']);
    expect(matchCaptains(captains, 'grace').map((c) => c.id)).toEqual(['cpt_2', 'cpt_3']);
    expect(matchCaptains(captains, 'kelly').map((c) => c.id)).toEqual(['cpt_3']);
    expect(matchCaptains(captains, 'ghop').map((c) => c.id)).toEqual(['cpt_2']);
    expect(matchCaptains(captains, 'zed')).toEqual([]);
    expect(matchCaptains(captains, ' ')).toEqual([]);
  });

  it('parses on, off, and toggle', () => {
    expect(parseThinkingArg('on', false)).toBe(true);
    expect(parseThinkingArg('OFF', true)).toBe(false);
    expect(parseThinkingArg('', true)).toBe(false);
    expect(parseThinkingArg('maybe', true)).toBeNull();
  });
});

describe('runLocalCommand', () => {
  it('/new and /help call the client', async () => {
    const ctx = context();
    expect(await runLocalCommand(local('new'), '', ctx)).toEqual({ ok: true });
    expect(ctx.newConversation).toHaveBeenCalled();
    expect(await runLocalCommand(local('help'), '', ctx)).toEqual({ ok: true });
    expect(ctx.openHelp).toHaveBeenCalled();
  });

  it('thread commands explain themselves on a new, unsaved conversation', async () => {
    const ctx = context({ hasThread: false });
    expect(await runLocalCommand(local('summarize'), '', ctx)).toEqual({ ok: false, hint: 'Nothing to summarize yet.' });
    expect(await runLocalCommand(local('rename'), 'x', ctx)).toEqual({ ok: false, hint: 'Nothing to rename yet. Send a message first.' });
    expect(await runLocalCommand(local('archive'), '', ctx)).toEqual({ ok: false, hint: 'Nothing to archive yet.' });
    expect(ctx.summarize).not.toHaveBeenCalled();
    expect(ctx.rename).not.toHaveBeenCalled();
    expect(ctx.archive).not.toHaveBeenCalled();
  });

  it('/summarize, /rename, and /archive act on the open conversation', async () => {
    const ctx = context();
    expect(await runLocalCommand(local('summarize'), '', ctx)).toEqual({ ok: true });
    expect(ctx.summarize).toHaveBeenCalled();
    expect(await runLocalCommand(local('rename'), '', ctx)).toEqual({ ok: false, hint: 'Type a title after /rename.' });
    expect(await runLocalCommand(local('rename'), 'x'.repeat(250), ctx)).toEqual({ ok: true });
    expect(ctx.rename).toHaveBeenCalledWith('x'.repeat(200));
    expect(await runLocalCommand(local('archive'), '', ctx)).toEqual({ ok: true, hint: 'Conversation archived.' });
    expect(ctx.archive).toHaveBeenCalledTimes(1);
    expect(await runLocalCommand(local('archive'), '', context({ archived: true }))).toEqual({ ok: true, hint: 'This conversation is already archived.' });
  });

  it('/captain switches on one match, opens the picker otherwise', async () => {
    const ctx = context();
    expect(await runLocalCommand(local('captain'), 'ada', ctx)).toEqual({ ok: true, hint: 'Captain: {{name}}', hintParams: { name: 'Ada' } });
    expect(ctx.setCaptain).toHaveBeenCalledWith('cpt_1');
    expect(await runLocalCommand(local('captain'), 'grace', ctx)).toMatchObject({ ok: true, hint: 'Several captains match "{{name}}". Choose one.' });
    expect(ctx.openCaptainPicker).toHaveBeenCalledTimes(1);
    expect(await runLocalCommand(local('captain'), '', ctx)).toEqual({ ok: true });
    expect(ctx.openCaptainPicker).toHaveBeenCalledTimes(2);
    expect(await runLocalCommand(local('captain'), 'zed', ctx)).toEqual({ ok: false, hint: 'No captain matches "{{name}}".', hintParams: { name: 'zed' } });
    expect(await runLocalCommand(local('captain'), 'ada', context({ turnActive: true }))).toMatchObject({ ok: false });
  });

  it('/thinking sets, toggles, and rejects other values', async () => {
    const ctx = context({ showThinking: false });
    expect(await runLocalCommand(local('thinking'), 'on', ctx)).toEqual({ ok: true, hint: 'Show thinking is on.' });
    expect(ctx.setShowThinking).toHaveBeenLastCalledWith(true);
    expect(await runLocalCommand(local('thinking'), '', context({ showThinking: true, setShowThinking: ctx.setShowThinking }))).toEqual({ ok: true, hint: 'Show thinking is off.' });
    expect(ctx.setShowThinking).toHaveBeenLastCalledWith(false);
    expect(await runLocalCommand(local('thinking'), 'sometimes', ctx)).toEqual({ ok: false, hint: 'Use /thinking on or /thinking off.' });
  });
});
