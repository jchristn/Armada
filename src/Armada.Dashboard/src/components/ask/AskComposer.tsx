import { forwardRef, useImperativeHandle, useLayoutEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react';
import type { AskQuickAction } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import { quickActionForm } from '../../lib/askQuickActions';
import {
  buildCommandCatalog,
  filterCommands,
  messageText,
  resolveSubmit,
  unknownCommandHint,
  type AskCommandItem,
  type AskCommandOutcome,
  type AskLocalCommand,
} from '../../lib/askCommands';
import AskQuickActionMenu, { QUICK_MENU_ID, quickOptionId } from './AskQuickActionMenu';
import AskDispatchForm from './AskDispatchForm';
import AskFleetActionForm from './AskFleetActionForm';
import AskBlackBoxForm from './AskBlackBoxForm';
import { parseBlackBoxCommand } from '../../lib/askBlackBox';

interface AskComposerProps {
  quickActions: AskQuickAction[];
  /** A captain turn is running (shows Stop instead of Send). */
  turnActive: boolean;
  stopping: boolean;
  onStop: () => void;
  onSend: (text: string) => void;
  /** Runs a quick action; resolves true when it succeeded so the form can close. */
  onQuickAction: (action: AskQuickAction, args: Record<string, unknown>) => Promise<boolean>;
  /** Runs a local command (/new, /summarize, ...); `/help` is handled here. */
  onLocalCommand: (command: AskLocalCommand, args: string) => Promise<AskCommandOutcome>;
  actionBusy: boolean;
  onOpenImport: () => void;
  /** No captain is selected: plain messages cannot be sent, commands still work. */
  noCaptain: boolean;
  showThinking: boolean;
  onShowThinkingChange: (value: boolean) => void;
  /** The user changed the draft (typing, or it was cleared by a send or command); not called by `reset`. */
  onTextChange?: (text: string) => void;
}

export interface AskComposerOptions {
  /** Text to show (default empty). */
  text?: string;
  focus?: boolean;
}

export interface AskComposerHandle {
  /** Open a quick action as if it had been chosen from the `/` menu. */
  choose: (action: AskQuickAction) => void;
  focus: () => void;
  /** The current draft. */
  getText: () => string;
  /** Replace the draft and close the menu, any open form, and the hint (a new or different conversation). */
  reset: (options?: AskComposerOptions) => void;
}

/** localStorage key of the prompts sent from this browser, oldest first. */
export const PROMPT_HISTORY_KEY = 'armada.ask.promptHistory';
/** The most prompts kept in the history. */
export const PROMPT_HISTORY_LIMIT = 100;

function loadPromptHistory(): string[] {
  try {
    const parsed: unknown = JSON.parse(localStorage.getItem(PROMPT_HISTORY_KEY) ?? '[]');
    return Array.isArray(parsed) ? parsed.filter((v): v is string => typeof v === 'string').slice(-PROMPT_HISTORY_LIMIT) : [];
  } catch {
    return [];
  }
}

/** Adds a sent prompt to the history, skipping blank text and a repeat of the newest entry. */
function recordPrompt(text: string): void {
  if (!text.trim()) return;
  const history = loadPromptHistory();
  if (history[history.length - 1] === text) return;
  history.push(text);
  try {
    localStorage.setItem(PROMPT_HISTORY_KEY, JSON.stringify(history.slice(-PROMPT_HISTORY_LIMIT)));
  } catch {
    // Storage is full or unavailable; history is a convenience.
  }
}

interface Hint {
  text: string;
  params?: Record<string, string>;
}

/**
 * The message composer. Plain text goes to the thread's captain. Text starting with `/` is a command: the menu
 * lists matching quick actions and local commands, Return (or Send) runs the highlighted entry or the exact command
 * typed (with its arguments), and an unknown command shows a hint and keeps the text.
 */
const AskComposer = forwardRef<AskComposerHandle, AskComposerProps>(function AskComposer(props, ref) {
  const { quickActions, turnActive, stopping, onStop, onSend, onQuickAction, onLocalCommand, actionBusy, onOpenImport, noCaptain, showThinking, onShowThinkingChange, onTextChange } = props;
  const { t } = useLocale();
  const [input, setInput] = useState('');
  const [activeIndex, setActiveIndex] = useState(0);
  const [menuDismissed, setMenuDismissed] = useState(false);
  const [openForm, setOpenForm] = useState<AskQuickAction | null>(null);
  const [hint, setHint] = useState<Hint | null>(null);
  const inputRef = useRef<HTMLTextAreaElement>(null);
  // Shell-style prompt history: the entries being browsed, the index into them (-1 = not browsing), and the
  // draft that was being typed when browsing started.
  const historyRef = useRef<string[]>([]);
  const historyIndexRef = useRef(-1);
  const historyDraftRef = useRef('');
  const caretToEndRef = useRef(false);

  useLayoutEffect(() => {
    if (!caretToEndRef.current) return;
    caretToEndRef.current = false;
    const el = inputRef.current;
    if (el) el.setSelectionRange(el.value.length, el.value.length);
  }, [input]);

  const catalog = useMemo(() => buildCommandCatalog(quickActions), [quickActions]);
  const matches = useMemo(() => filterCommands(catalog, input), [catalog, input]);
  const menuOpen = !menuDismissed && !openForm && matches.length > 0;
  const safeIndex = Math.min(activeIndex, Math.max(0, matches.length - 1));
  const highlighted = menuOpen ? matches[safeIndex] : null;
  const pending = resolveSubmit(catalog, input, highlighted);
  const formKind = openForm ? quickActionForm(openForm) : null;

  function applyText(text: string) {
    setInput(text);
    setActiveIndex(0);
    setMenuDismissed(false);
  }

  function setText(text: string) {
    applyText(text);
    onTextChange?.(text);
  }

  function reset(options?: AskComposerOptions) {
    historyIndexRef.current = -1;
    applyText(options?.text ?? '');
    setOpenForm(null);
    setHint(null);
    if (options?.focus) inputRef.current?.focus();
  }

  async function choose(action: AskQuickAction) {
    setText('');
    setHint(null);
    const kind = quickActionForm(action);
    if (kind === 'import') { onOpenImport(); return; }
    if (kind === 'none') { await onQuickAction(action, {}); inputRef.current?.focus(); return; }
    setOpenForm(action);
  }

  async function runItem(item: AskCommandItem, args: string) {
    // `/blackbox msn_... [question]` typed in full runs at once; without a mission id it opens the form.
    if (item.action && quickActionForm(item.action) === 'blackbox') {
      const parsed = parseBlackBoxCommand(args);
      if (parsed) {
        setText('');
        setHint(null);
        const blackBoxArgs: Record<string, unknown> = { missionId: parsed.missionId };
        if (parsed.question) blackBoxArgs.question = parsed.question;
        await onQuickAction(item.action, blackBoxArgs);
        inputRef.current?.focus();
        return;
      }
    }
    if (item.action) { await choose(item.action); return; }
    const local = item.local;
    if (!local) return;
    if (local.name === 'help') { reset({ text: '/', focus: true }); return; }
    if (local.requiresArgs && !args) { reset({ text: `${local.command} `, focus: true }); return; }
    // A new conversation must not inherit the command as the old conversation's saved draft.
    if (local.name === 'new') setText('');
    const outcome = await onLocalCommand(local, args);
    if (outcome.ok) setText('');
    setHint(outcome.hint ? { text: outcome.hint, params: outcome.hintParams } : null);
    inputRef.current?.focus();
  }

  useImperativeHandle(ref, () => ({
    choose: (action: AskQuickAction) => { void choose(action); },
    focus: () => inputRef.current?.focus(),
    getText: () => input,
    reset,
  }));

  async function submitForm(args: Record<string, unknown>) {
    if (!openForm) return;
    const ok = await onQuickAction(openForm, args);
    if (ok) { setOpenForm(null); inputRef.current?.focus(); }
  }

  const canSendText = pending.kind === 'text' && !!input.trim() && !turnActive && !noCaptain;
  const canSubmit = pending.kind === 'command' || canSendText;

  function submit() {
    historyIndexRef.current = -1;
    if (pending.kind === 'command') { recordPrompt(input); void runItem(pending.item, pending.args); return; }
    if (pending.kind === 'unknown') {
      const outcome = unknownCommandHint(pending.command);
      setHint({ text: outcome.hint ?? '', params: outcome.hintParams });
      return;
    }
    if (!canSendText) return;
    recordPrompt(input);
    onSend(messageText(input));
    setText('');
    setHint(null);
  }

  function onKeyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    if (menuOpen) {
      if (event.key === 'ArrowDown') { event.preventDefault(); setActiveIndex((safeIndex + 1) % matches.length); return; }
      if (event.key === 'ArrowUp') { event.preventDefault(); setActiveIndex((safeIndex - 1 + matches.length) % matches.length); return; }
      if (event.key === 'Tab') { event.preventDefault(); void runItem(matches[safeIndex], ''); return; }
      if (event.key === 'Escape') { event.preventDefault(); setMenuDismissed(true); return; }
    }
    if ((event.key === 'ArrowUp' || event.key === 'ArrowDown') && browseHistory(event)) return;
    if (event.key === 'Enter' && !event.shiftKey && !event.nativeEvent.isComposing) {
      event.preventDefault();
      submit();
    }
  }

  /** Moves through the prompt history on ArrowUp (caret on the first line) or ArrowDown (caret on the last line). */
  function browseHistory(event: KeyboardEvent<HTMLTextAreaElement>): boolean {
    if (menuOpen || event.nativeEvent.isComposing || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return false;
    const el = event.currentTarget;
    const value = el.value;
    const start = el.selectionStart ?? 0;
    const end = el.selectionEnd ?? 0;
    if (start !== end) return false;
    let next: number;
    if (event.key === 'ArrowUp') {
      if (value.slice(0, start).includes('\n')) return false;
      if (historyIndexRef.current === -1) {
        historyRef.current = loadPromptHistory();
        if (historyRef.current.length === 0) return false;
        historyDraftRef.current = value;
        next = historyRef.current.length - 1;
      } else {
        if (historyIndexRef.current === 0) return false;
        next = historyIndexRef.current - 1;
      }
    } else {
      if (historyIndexRef.current === -1 || value.slice(end).includes('\n')) return false;
      next = historyIndexRef.current + 1;
    }
    event.preventDefault();
    const text = next >= historyRef.current.length ? historyDraftRef.current : historyRef.current[next];
    historyIndexRef.current = next >= historyRef.current.length ? -1 : next;
    caretToEndRef.current = true;
    setText(text);
    setHint(null);
    return true;
  }

  const placeholder = noCaptain
    ? t('Choose a captain to chat, or type / for commands')
    : t('Message the captain, or type / for commands');

  return (
    <div className="ask-composer">
      {menuOpen && (
        <AskQuickActionMenu items={matches} activeIndex={safeIndex} onHover={setActiveIndex} onChoose={(item) => void runItem(item, '')} />
      )}
      {openForm && formKind === 'dispatch' && (
        <AskDispatchForm busy={actionBusy} onSubmit={(args) => void submitForm(args)} onCancel={() => setOpenForm(null)} />
      )}
      {openForm && formKind === 'blackbox' && (
        <AskBlackBoxForm busy={actionBusy} onSubmit={(args) => void submitForm(args)} onCancel={() => setOpenForm(null)} />
      )}
      {openForm && formKind === 'fleet-action' && (
        <AskFleetActionForm busy={actionBusy} onSubmit={(args) => void submitForm(args)} onCancel={() => setOpenForm(null)} />
      )}

      <form className="ask-composer-form" onSubmit={(e) => { e.preventDefault(); submit(); }}>
        <textarea
          ref={inputRef}
          className="ask-composer-input"
          value={input}
          rows={1}
          placeholder={placeholder}
          aria-label={t('Message')}
          title={t('Type a message and press Enter to send; Shift+Enter adds a new line. Start with / to pick a command or quick action.')}
          role="combobox"
          aria-expanded={menuOpen}
          aria-controls={menuOpen ? QUICK_MENU_ID : undefined}
          aria-autocomplete="list"
          aria-activedescendant={highlighted ? quickOptionId(highlighted) : undefined}
          aria-describedby={hint ? 'ask-composer-hint' : undefined}
          onChange={(e) => { historyIndexRef.current = -1; setText(e.target.value); setHint(null); }}
          onKeyDown={onKeyDown}
        />
        {turnActive && pending.kind !== 'command' ? (
          <button type="button" className="btn" onClick={onStop} disabled={stopping} title={t('Stop the captain turn now. Anything already done stays done; the partial reply is kept.')}>
            {stopping ? t('Stopping...') : t('Stop')}
          </button>
        ) : (
          <button type="submit" className="btn btn-primary" disabled={!canSubmit} title={noCaptain ? t('Choose a captain in the header before sending a message.') : turnActive ? t('Wait for the captain to finish its current reply, or stop it.') : t('Send the message to the captain (Enter).')}>
            {t('Send')}
          </button>
        )}
      </form>
      <div id="ask-composer-hint" className="ask-composer-hint text-dim" role="status" aria-live="polite">
        {hint ? t(hint.text, hint.params) : null}
      </div>
      <div className="ask-composer-foot">
        <label className="ask-stream-toggle" title={t('Ask the captain to include its reasoning with each reply, shown collapsed above it. Applies to messages you send from now on; remembered in this browser.')}>
          <input type="checkbox" title={t('Ask the captain to include its reasoning with each reply, shown collapsed above it. Applies to messages you send from now on; remembered in this browser.')} checked={showThinking} onChange={(e) => onShowThinkingChange(e.target.checked)} />
          {t('Show thinking')}
        </label>
        <button type="button" className="ask-link-btn" onClick={() => reset({ text: '/', focus: true })} title={t('List the slash commands and quick actions you can use here, such as dispatching a mission or running a fleet action.')}>
          {t('Commands')}
        </button>
        <span className="ask-disclaimer-inline text-dim">{t('AI can make mistakes. Check answers.')}</span>
      </div>
    </div>
  );
});

export default AskComposer;
