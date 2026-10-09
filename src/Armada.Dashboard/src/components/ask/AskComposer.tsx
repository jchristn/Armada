import { forwardRef, useImperativeHandle, useMemo, useRef, useState, type KeyboardEvent } from 'react';
import type { AskQuickAction } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import { quickActionForm } from '../../lib/askQuickActions';
import {
  buildCommandCatalog,
  filterCommands,
  resolveSubmit,
  unknownCommandHint,
  type AskCommandItem,
  type AskCommandOutcome,
  type AskLocalCommand,
} from '../../lib/askCommands';
import AskQuickActionMenu, { QUICK_MENU_ID, quickOptionId } from './AskQuickActionMenu';
import AskDispatchForm from './AskDispatchForm';
import AskFleetActionForm from './AskFleetActionForm';

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
  const { quickActions, turnActive, stopping, onStop, onSend, onQuickAction, onLocalCommand, actionBusy, onOpenImport, noCaptain, showThinking, onShowThinkingChange } = props;
  const { t } = useLocale();
  const [input, setInput] = useState('');
  const [activeIndex, setActiveIndex] = useState(0);
  const [menuDismissed, setMenuDismissed] = useState(false);
  const [openForm, setOpenForm] = useState<AskQuickAction | null>(null);
  const [hint, setHint] = useState<Hint | null>(null);
  const inputRef = useRef<HTMLTextAreaElement>(null);

  const catalog = useMemo(() => buildCommandCatalog(quickActions), [quickActions]);
  const matches = useMemo(() => filterCommands(catalog, input), [catalog, input]);
  const menuOpen = !menuDismissed && !openForm && matches.length > 0;
  const safeIndex = Math.min(activeIndex, Math.max(0, matches.length - 1));
  const highlighted = menuOpen ? matches[safeIndex] : null;
  const pending = resolveSubmit(catalog, input, highlighted);
  const formKind = openForm ? quickActionForm(openForm) : null;

  function setText(text: string) {
    setInput(text);
    setActiveIndex(0);
    setMenuDismissed(false);
  }

  function reset(options?: AskComposerOptions) {
    setText(options?.text ?? '');
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
    if (pending.kind === 'command') { void runItem(pending.item, pending.args); return; }
    if (pending.kind === 'unknown') {
      const outcome = unknownCommandHint(pending.command);
      setHint({ text: outcome.hint ?? '', params: outcome.hintParams });
      return;
    }
    if (!canSendText) return;
    onSend(input.trim());
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
    if (event.key === 'Enter' && !event.shiftKey && !event.nativeEvent.isComposing) {
      event.preventDefault();
      submit();
    }
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
          role="combobox"
          aria-expanded={menuOpen}
          aria-controls={menuOpen ? QUICK_MENU_ID : undefined}
          aria-autocomplete="list"
          aria-activedescendant={highlighted ? quickOptionId(highlighted) : undefined}
          aria-describedby={hint ? 'ask-composer-hint' : undefined}
          onChange={(e) => { setText(e.target.value); setHint(null); }}
          onKeyDown={onKeyDown}
        />
        {turnActive && pending.kind !== 'command' ? (
          <button type="button" className="btn" onClick={onStop} disabled={stopping}>
            {stopping ? t('Stopping...') : t('Stop')}
          </button>
        ) : (
          <button type="submit" className="btn btn-primary" disabled={!canSubmit}>
            {t('Send')}
          </button>
        )}
      </form>
      <div id="ask-composer-hint" className="ask-composer-hint text-dim" role="status" aria-live="polite">
        {hint ? t(hint.text, hint.params) : null}
      </div>
      <div className="ask-composer-foot">
        <label className="ask-stream-toggle" title={t('Ask the captain to include its reasoning, shown collapsed above each reply')}>
          <input type="checkbox" checked={showThinking} onChange={(e) => onShowThinkingChange(e.target.checked)} />
          {t('Show thinking')}
        </label>
        <button type="button" className="ask-link-btn" onClick={() => reset({ text: '/', focus: true })}>
          {t('Commands')}
        </button>
        <span className="ask-disclaimer-inline text-dim">{t('AI can make mistakes. Check answers.')}</span>
      </div>
    </div>
  );
});

export default AskComposer;
