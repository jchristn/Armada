import type { AskQuickAction, Captain } from '../types/models';

/**
 * Ask Armada slash commands, shared by the dashboard and the mobile app (the TUI mirrors these rules in
 * Armada.Tui/Ask/AskCommands.cs). The catalog is the server's quick actions (lib/askQuickActions) plus the built-in
 * local commands below, which the client handles itself. Titles, descriptions and hints are English keys rendered
 * through `t`.
 */

export type AskLocalCommandName = 'new' | 'help' | 'summarize' | 'rename' | 'archive' | 'captain' | 'thinking';

export interface AskLocalCommand {
  name: AskLocalCommandName;
  /** The command with its leading slash. */
  command: string;
  /** Other spellings that run the same command (with their leading slash). */
  aliases: string[];
  /** Argument syntax shown after the command in the menu ('' for none). Not translated. */
  usage: string;
  /** Chosen from the menu without arguments, the command fills the composer so the user can type them. */
  requiresArgs: boolean;
  title: string;
  description: string;
}

/** The built-in local commands, in menu order. */
export const LOCAL_COMMANDS: AskLocalCommand[] = [
  { name: 'new', command: '/new', aliases: ['/clear'], usage: '', requiresArgs: false, title: 'New conversation', description: 'Start a new conversation with a fresh context (also /clear)' },
  { name: 'help', command: '/help', aliases: [], usage: '', requiresArgs: false, title: 'Help', description: 'List the commands you can type here' },
  { name: 'summarize', command: '/summarize', aliases: [], usage: '', requiresArgs: false, title: 'Summarize', description: 'Post a short summary of this conversation' },
  { name: 'rename', command: '/rename', aliases: [], usage: '<title>', requiresArgs: true, title: 'Rename', description: 'Rename this conversation' },
  { name: 'archive', command: '/archive', aliases: [], usage: '', requiresArgs: false, title: 'Archive', description: 'Archive this conversation' },
  { name: 'captain', command: '/captain', aliases: [], usage: '<name>', requiresArgs: false, title: 'Captain', description: 'Switch the captain by name' },
  { name: 'thinking', command: '/thinking', aliases: [], usage: 'on|off', requiresArgs: false, title: 'Show thinking', description: 'Turn Show thinking on or off' },
];

/** One entry in the `/` menu: a local command or a quick action. */
export interface AskCommandItem {
  /** Unique per catalog (`local:new`, `quick:dispatch`). */
  key: string;
  command: string;
  aliases: string[];
  usage: string;
  title: string;
  description: string;
  /** Set for a local command. */
  local: AskLocalCommand | null;
  /** Set for a quick action. */
  action: AskQuickAction | null;
}

/** What Return or Send does with the composer text. */
export type AskCommandParse =
  | { kind: 'text' }
  | { kind: 'command'; item: AskCommandItem; args: string }
  | { kind: 'unknown'; command: string };

/** The result of a local command: `ok` clears the composer; `hint` (an English key) is shown under it. */
export interface AskCommandOutcome {
  ok: boolean;
  hint?: string;
  hintParams?: Record<string, string>;
}

/** What a client provides so the shared rules can run a local command. */
export interface AskCommandContext {
  /** The open conversation has been saved (it has an id). */
  hasThread: boolean;
  archived: boolean;
  turnActive: boolean;
  captains: Captain[];
  showThinking: boolean;
  newConversation: () => void;
  openHelp: () => void;
  summarize: () => Promise<unknown> | void;
  rename: (title: string) => Promise<unknown> | void;
  archive: () => Promise<unknown> | void;
  /** Use this captain for the conversation (or for the new conversation's draft). */
  setCaptain: (captainId: string) => void;
  openCaptainPicker: () => void;
  setShowThinking: (value: boolean) => void;
}

/** Maximum conversation title length (the server's limit). */
export const MAX_TITLE_LENGTH = 200;

function slashed(raw: string): string {
  return raw.startsWith('/') ? raw : `/${raw}`;
}

function quickCommand(action: AskQuickAction): string {
  return slashed(action.command || action.name);
}

function spellings(item: AskCommandItem): string[] {
  return [item.command, ...item.aliases].map((c) => c.toLowerCase());
}

/** The `/` menu catalog: quick actions first (server order), then the local commands. A quick action whose command
 * collides with a local command is left out, so `/new`, `/clear` and the rest always mean the same thing. */
export function buildCommandCatalog(quickActions: AskQuickAction[]): AskCommandItem[] {
  const reserved = new Set(LOCAL_COMMANDS.flatMap((c) => [c.command, ...c.aliases]));
  const items: AskCommandItem[] = [];
  for (const action of quickActions) {
    const command = quickCommand(action);
    if (reserved.has(command.toLowerCase())) continue;
    items.push({
      key: `quick:${action.name}`,
      command,
      aliases: [],
      usage: '',
      title: action.title || action.name,
      description: action.description || '',
      local: null,
      action,
    });
  }
  for (const local of LOCAL_COMMANDS) {
    items.push({ key: `local:${local.name}`, command: local.command, aliases: local.aliases, usage: local.usage, title: local.title, description: local.description, local, action: null });
  }
  return items;
}

/** Menu entries for what the user typed: only while the text is a single `/word`; exact matches sort first. */
export function filterCommands(items: AskCommandItem[], input: string): AskCommandItem[] {
  if (!input.startsWith('/') || input.startsWith(COMMAND_ESCAPE) || /\s/.test(input)) return [];
  const typed = input.toLowerCase();
  const exact = items.filter((i) => spellings(i).includes(typed));
  const prefix = items.filter((i) => !exact.includes(i) && (spellings(i).some((s) => s.startsWith(typed))
    || (i.action !== null && `/${i.action.name.toLowerCase()}`.startsWith(typed))));
  return [...exact, ...prefix];
}

/** The escape for a message that starts with a slash: a leading `//` sends the rest as text beginning with one `/`. */
export const COMMAND_ESCAPE = '//';

/** The message to send for composer text that is not a command: trimmed, with a leading `//` turned into `/`. */
export function messageText(input: string): string {
  const text = input.trim();
  return text.startsWith(COMMAND_ESCAPE) ? text.slice(1) : text;
}

/** Classify the composer text: plain text, a command with its arguments (case-insensitive, aliases included), or an
 * unknown command. Text starting with `//` is plain text (see {@link messageText}). */
export function parseCommand(items: AskCommandItem[], input: string): AskCommandParse {
  const text = input.trim();
  if (!text.startsWith('/') || text.startsWith(COMMAND_ESCAPE)) return { kind: 'text' };
  const match = /^(\S*)\s*([\s\S]*)$/.exec(text);
  const word = match ? match[1] : text;
  const args = match ? match[2].trim() : '';
  const lower = word.toLowerCase();
  const item = items.find((i) => spellings(i).includes(lower));
  return item ? { kind: 'command', item, args } : { kind: 'unknown', command: word };
}

/** What Return runs: the highlighted menu entry while the menu is open, otherwise the parsed text. */
export function resolveSubmit(items: AskCommandItem[], input: string, highlighted: AskCommandItem | null): AskCommandParse {
  if (highlighted) {
    const parsed = parseCommand(items, input);
    return { kind: 'command', item: highlighted, args: parsed.kind === 'command' && parsed.item.key === highlighted.key ? parsed.args : '' };
  }
  return parseCommand(items, input);
}

/** The inline hint for an unknown command. */
export function unknownCommandHint(command: string): AskCommandOutcome {
  return { ok: false, hint: 'Unknown command {{command}}. Type / to see commands', hintParams: { command: command || '/' } };
}

/** Captains matching a name: an exact id or name wins, then names that start with it, contain it, or contain its
 * letters in order. */
export function matchCaptains(captains: Captain[], query: string): Captain[] {
  const q = query.trim().toLowerCase();
  if (!q) return [];
  const byId = captains.filter((c) => c.id.toLowerCase() === q);
  if (byId.length) return byId;
  const name = (c: Captain) => (c.name || '').toLowerCase();
  const tiers: ((c: Captain) => boolean)[] = [
    (c) => name(c) === q,
    (c) => name(c).startsWith(q),
    (c) => name(c).includes(q),
    (c) => isSubsequence(q.replace(/\s+/g, ''), name(c)),
  ];
  for (const tier of tiers) {
    const found = captains.filter(tier);
    if (found.length) return found;
  }
  return [];
}

function isSubsequence(needle: string, haystack: string): boolean {
  let i = 0;
  for (const ch of haystack) {
    if (ch === needle[i]) i++;
    if (i === needle.length) return true;
  }
  return needle.length === 0;
}

/** `/thinking` arguments: on or off, or empty to toggle. Returns null for anything else. */
export function parseThinkingArg(args: string, current: boolean): boolean | null {
  const value = args.trim().toLowerCase();
  if (!value) return !current;
  if (value === 'on' || value === 'true' || value === 'yes') return true;
  if (value === 'off' || value === 'false' || value === 'no') return false;
  return null;
}

/** Run a local command against the client's context. */
export async function runLocalCommand(command: AskLocalCommand, args: string, ctx: AskCommandContext): Promise<AskCommandOutcome> {
  switch (command.name) {
    case 'new':
      ctx.newConversation();
      return { ok: true };
    case 'help':
      ctx.openHelp();
      return { ok: true };
    case 'summarize':
      if (!ctx.hasThread) return { ok: false, hint: 'Nothing to summarize yet.' };
      await ctx.summarize();
      return { ok: true };
    case 'rename': {
      if (!ctx.hasThread) return { ok: false, hint: 'Nothing to rename yet. Send a message first.' };
      const title = args.trim().slice(0, MAX_TITLE_LENGTH);
      if (!title) return { ok: false, hint: 'Type a title after /rename.' };
      await ctx.rename(title);
      return { ok: true };
    }
    case 'archive':
      if (!ctx.hasThread) return { ok: false, hint: 'Nothing to archive yet.' };
      if (ctx.archived) return { ok: true, hint: 'This conversation is already archived.' };
      await ctx.archive();
      return { ok: true, hint: 'Conversation archived.' };
    case 'captain': {
      if (ctx.turnActive) return { ok: false, hint: 'Wait for the reply to finish before changing the captain.' };
      if (!args.trim()) { ctx.openCaptainPicker(); return { ok: true }; }
      const found = matchCaptains(ctx.captains, args);
      if (found.length === 0) return { ok: false, hint: 'No captain matches "{{name}}".', hintParams: { name: args.trim() } };
      if (found.length > 1) {
        ctx.openCaptainPicker();
        return { ok: true, hint: 'Several captains match "{{name}}". Choose one.', hintParams: { name: args.trim() } };
      }
      ctx.setCaptain(found[0].id);
      return { ok: true, hint: 'Captain: {{name}}', hintParams: { name: found[0].name } };
    }
    case 'thinking': {
      const value = parseThinkingArg(args, ctx.showThinking);
      if (value === null) return { ok: false, hint: 'Use /thinking on or /thinking off.' };
      ctx.setShowThinking(value);
      return { ok: true, hint: value ? 'Show thinking is on.' : 'Show thinking is off.' };
    }
    default:
      return { ok: false };
  }
}
