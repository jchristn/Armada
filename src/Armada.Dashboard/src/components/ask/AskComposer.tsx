import { forwardRef, useImperativeHandle, useMemo, useRef, useState, type KeyboardEvent } from 'react';
import type { AskQuickAction } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import { filterQuickActions, quickActionForm } from '../../lib/askQuickActions';
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
  actionBusy: boolean;
  onOpenImport: () => void;
  /** No captain is selected: plain messages cannot be sent, quick actions still work. */
  noCaptain: boolean;
  showThinking: boolean;
  onShowThinkingChange: (value: boolean) => void;
}

export interface AskComposerHandle {
  /** Open a quick action as if it had been chosen from the `/` menu. */
  choose: (action: AskQuickAction) => void;
  focus: () => void;
}

/**
 * The message composer. Plain text goes to the thread's captain; `/` opens the quick-action menu, and each
 * quick action opens its inline form (or runs immediately when it needs no input).
 */
const AskComposer = forwardRef<AskComposerHandle, AskComposerProps>(function AskComposer(props, ref) {
  const { quickActions, turnActive, stopping, onStop, onSend, onQuickAction, actionBusy, onOpenImport, noCaptain, showThinking, onShowThinkingChange } = props;
  const { t } = useLocale();
  const [input, setInput] = useState('');
  const [activeIndex, setActiveIndex] = useState(0);
  const [menuDismissed, setMenuDismissed] = useState(false);
  const [openForm, setOpenForm] = useState<AskQuickAction | null>(null);
  const inputRef = useRef<HTMLTextAreaElement>(null);

  const matches = useMemo(() => filterQuickActions(quickActions, input), [quickActions, input]);
  const menuOpen = !menuDismissed && !openForm && matches.length > 0;
  const safeIndex = Math.min(activeIndex, Math.max(0, matches.length - 1));
  const formKind = openForm ? quickActionForm(openForm) : null;

  async function choose(action: AskQuickAction) {
    setInput('');
    setActiveIndex(0);
    const kind = quickActionForm(action);
    if (kind === 'import') { onOpenImport(); return; }
    if (kind === 'none') { await onQuickAction(action, {}); inputRef.current?.focus(); return; }
    setOpenForm(action);
  }

  useImperativeHandle(ref, () => ({
    choose: (action: AskQuickAction) => { void choose(action); },
    focus: () => inputRef.current?.focus(),
  }));

  async function submitForm(args: Record<string, unknown>) {
    if (!openForm) return;
    const ok = await onQuickAction(openForm, args);
    if (ok) { setOpenForm(null); inputRef.current?.focus(); }
  }

  function send() {
    const text = input.trim();
    if (!text || turnActive || noCaptain) return;
    onSend(text);
    setInput('');
  }

  function onKeyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    if (menuOpen) {
      if (event.key === 'ArrowDown') { event.preventDefault(); setActiveIndex((safeIndex + 1) % matches.length); return; }
      if (event.key === 'ArrowUp') { event.preventDefault(); setActiveIndex((safeIndex - 1 + matches.length) % matches.length); return; }
      if (event.key === 'Enter' || event.key === 'Tab') { event.preventDefault(); void choose(matches[safeIndex]); return; }
      if (event.key === 'Escape') { event.preventDefault(); setMenuDismissed(true); return; }
    }
    if (event.key === 'Enter' && !event.shiftKey && !event.nativeEvent.isComposing) {
      event.preventDefault();
      send();
    }
  }

  const placeholder = noCaptain
    ? t('Choose a captain to chat, or type / for quick actions')
    : t('Message the captain, or type / for quick actions');

  return (
    <div className="ask-composer">
      {menuOpen && (
        <AskQuickActionMenu actions={matches} activeIndex={safeIndex} onHover={setActiveIndex} onChoose={(a) => void choose(a)} />
      )}
      {openForm && formKind === 'dispatch' && (
        <AskDispatchForm busy={actionBusy} onSubmit={(args) => void submitForm(args)} onCancel={() => setOpenForm(null)} />
      )}
      {openForm && formKind === 'fleet-action' && (
        <AskFleetActionForm busy={actionBusy} onSubmit={(args) => void submitForm(args)} onCancel={() => setOpenForm(null)} />
      )}

      <form className="ask-composer-form" onSubmit={(e) => { e.preventDefault(); send(); }}>
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
          aria-activedescendant={menuOpen ? quickOptionId(matches[safeIndex]) : undefined}
          onChange={(e) => { setInput(e.target.value); setMenuDismissed(false); setActiveIndex(0); }}
          onKeyDown={onKeyDown}
        />
        {turnActive ? (
          <button type="button" className="btn" onClick={onStop} disabled={stopping}>
            {stopping ? t('Stopping...') : t('Stop')}
          </button>
        ) : (
          <button type="submit" className="btn btn-primary" disabled={!input.trim() || noCaptain || input.trim().startsWith('/')}>
            {t('Send')}
          </button>
        )}
      </form>
      <div className="ask-composer-foot">
        <label className="ask-stream-toggle" title={t('Ask the captain to include its reasoning, shown collapsed above each reply')}>
          <input type="checkbox" checked={showThinking} onChange={(e) => onShowThinkingChange(e.target.checked)} />
          {t('Show thinking')}
        </label>
        <button type="button" className="ask-link-btn" onClick={() => { setInput('/'); setMenuDismissed(false); inputRef.current?.focus(); }}>
          {t('Quick actions')}
        </button>
        <span className="ask-disclaimer-inline text-dim">{t('AI can make mistakes. Check answers.')}</span>
      </div>
    </div>
  );
});

export default AskComposer;
