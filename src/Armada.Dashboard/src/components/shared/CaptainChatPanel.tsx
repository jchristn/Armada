import { useEffect, useRef, type ReactNode, type RefObject } from 'react';
import type { CaptainChatMetrics } from '../../types/models';
import Markdown from './Markdown';
import ChatMetricsInfo from './ChatMetricsInfo';
import ChatToolChips, { type ToolEvent } from './ChatToolChips';

/**
 * One rendered turn in a captain chat. Shared by Ask Armada and the Planning current-session chat so
 * both surfaces render identically. `id` lets callers correlate a turn back to a persisted message.
 */
export interface ChatTurn {
  id?: string;
  role: 'user' | 'assistant' | 'system';
  text: string;
  metrics?: CaptainChatMetrics | null;
  model?: string | null;
  streaming?: boolean;
  tools?: ToolEvent[];
  /** The model's reasoning ("thinking"), shown collapsed above the answer when present. */
  thinking?: string;
}

interface CaptainChatPanelProps {
  t: (value: string, vars?: Record<string, string | number>) => string;
  turns: ChatTurn[];
  /** Content shown when there are no turns yet (centered, dimmed). */
  emptyState?: ReactNode;
  /** Fallback display name for assistant turns that carry no model label. */
  assistantName?: string;
  input: string;
  onInputChange: (value: string) => void;
  onSend: () => void;
  /** Invoked to abort the in-flight generation. When omitted, no Stop button is shown. */
  onStop?: () => void;
  /** True while a reply is generating (shows the thinking line and the Stop button). */
  busy: boolean;
  canSend: boolean;
  canStop?: boolean;
  /** Rotating "waiting" message shown while busy. */
  thinking?: string;
  inputPlaceholder?: string;
  /** Disables the composer input independently of busy (e.g. no target selected / session inactive). */
  inputDisabled?: boolean;
  /** Optional per-turn footer (e.g. Planning's "Open in Dispatch" action) rendered under the bubble. */
  renderTurnFooter?: (turn: ChatTurn, index: number) => ReactNode;
  /** Optional external ref to the scrolling chat window (callers that need to scroll it themselves). */
  windowRef?: RefObject<HTMLDivElement | null>;
  /** Optional small, always-visible notice shown at the top of the message window (e.g. a streaming caveat). */
  notice?: ReactNode;
  /** Invoked when the clear (trash) button beside Send is pressed. When omitted, no clear button is shown. */
  onClear?: () => void;
  /** Disables the clear button (e.g. while busy or when there is nothing to clear). */
  clearDisabled?: boolean;
}

/**
 * The shared captain-chat surface: a scrolling window of aligned message bubbles (user right, assistant
 * left) with per-turn (i) statistics and tool-call chips, an "AI can make mistakes" disclaimer, and a
 * single-line composer whose action toggles between Send and Stop while a reply is generating. Ask Armada
 * is the gold standard for this layout; the Planning current-session chat renders through the same panel.
 */
export default function CaptainChatPanel(props: CaptainChatPanelProps) {
  const {
    t,
    turns,
    emptyState,
    assistantName,
    input,
    onInputChange,
    onSend,
    onStop,
    busy,
    canSend,
    canStop,
    thinking,
    inputPlaceholder,
    inputDisabled,
    renderTurnFooter,
    windowRef,
    notice,
    onClear,
    clearDisabled,
  } = props;

  const scrollRef = useRef<HTMLDivElement | null>(null);
  const lastText = turns.length > 0 ? turns[turns.length - 1].text : '';

  // Merge the internal scroll-container ref with any external windowRef the caller passed.
  const setScrollContainer = (node: HTMLDivElement | null) => {
    scrollRef.current = node;
    if (windowRef) (windowRef as { current: HTMLDivElement | null }).current = node;
  };

  // Keep the newest content in view as turns stream in. Scroll ONLY the transcript container (never via
  // scrollIntoView, which also scrolls every scrollable ancestor and drags the whole Planning page down
  // toward the Dispatch panel), and only when the user is already near the bottom so streaming updates
  // never yank a user who has scrolled up to read.
  useEffect(() => {
    const el = scrollRef.current;
    if (!el) return;
    const distanceFromBottom = el.scrollHeight - el.scrollTop - el.clientHeight;
    if (distanceFromBottom < 140) {
      el.scrollTop = el.scrollHeight;
    }
  }, [turns.length, lastText, busy, thinking]);

  function roleLabel(turn: ChatTurn): string {
    if (turn.role === 'user') return t('You');
    if (turn.role === 'system') return t('System');
    return turn.model || assistantName || t('Captain');
  }

  return (
    <div className="chat-panel">
      {notice && (
        <div className="chat-stream-notice" role="status">
          <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <circle cx="12" cy="12" r="10" />
            <path d="M12 16v-4" />
            <path d="M12 8h.01" />
          </svg>
          <span>{notice}</span>
        </div>
      )}
      <div
        ref={setScrollContainer}
        className="card ask-chat-window"
        style={{ padding: '1rem', display: 'flex', flexDirection: 'column', gap: '0.75rem' }}
      >
        {turns.length === 0 ? (
          <div className="text-dim" style={{ margin: 'auto', textAlign: 'center' }}>
            {emptyState ?? <p>{t('Send the first message to begin.')}</p>}
          </div>
        ) : (
          turns.map((turn, i) => (
            <div
              key={turn.id ?? i}
              className="chat-turn"
              style={{
                alignSelf: turn.role === 'user' ? 'flex-end' : 'flex-start',
                maxWidth: '85%',
                display: 'flex',
                flexDirection: 'column',
                alignItems: turn.role === 'user' ? 'flex-end' : 'flex-start',
                gap: '0.35rem',
              }}
            >
              {/* Tool-call cards render as a fixed-width sibling of the answer bubble, not inside it, so
                  streaming text and tool output never stretch each other's width. */}
              {turn.role === 'assistant' && (
                <ChatToolChips
                  tools={turn.tools}
                  runningLabel={t('running…')}
                  argumentsLabel={t('Arguments')}
                  resultLabel={t('Result')}
                  noDetailsLabel={t('No details available.')}
                />
              )}
              <div
                className="card chat-bubble"
                style={{ padding: '0.6rem 0.85rem', maxWidth: '100%', background: turn.role === 'user' ? 'var(--accent-soft, rgba(80,120,255,0.12))' : undefined }}
              >
                <div className="text-dim chat-turn-header" style={{ fontSize: '0.7rem', marginBottom: '0.2rem' }}>
                  <span>{roleLabel(turn)}</span>
                  {turn.role === 'assistant' && turn.metrics && <ChatMetricsInfo metrics={turn.metrics} />}
                </div>
                {turn.role === 'assistant' && turn.thinking && turn.thinking.trim().length > 0 && (
                  <details className="chat-thinking" open={turn.streaming} style={{ marginBottom: '0.4rem' }}>
                    <summary className="text-dim" style={{ fontSize: '0.72rem', cursor: 'pointer' }}>
                      {turn.streaming ? t('Thinking…') : t('Thinking')}
                    </summary>
                    <div className="text-dim" style={{ fontSize: '0.8rem', whiteSpace: 'pre-wrap', opacity: 0.85, marginTop: '0.3rem' }}>
                      {turn.thinking}
                    </div>
                  </details>
                )}
                {turn.role === 'assistant'
                  ? <Markdown>{turn.text}</Markdown>
                  : <div style={{ whiteSpace: 'pre-wrap' }}>{turn.text}</div>}
              </div>
              {renderTurnFooter?.(turn, i)}
            </div>
          ))
        )}
        {busy && (
          <div key={thinking} className="text-dim ask-thinking" style={{ alignSelf: 'flex-start' }}>
            {thinking || t('Thinking...')}
          </div>
        )}
      </div>

      <form
        className="ask-input-form"
        style={{ display: 'flex', gap: '0.5rem' }}
        onSubmit={(e) => { e.preventDefault(); if (!busy) onSend(); }}
      >
        <input
          type="text"
          value={input}
          onChange={(e) => onInputChange(e.target.value)}
          placeholder={inputPlaceholder ?? t('Message the captain...')}
          style={{ flex: 1 }}
          disabled={busy || inputDisabled}
        />
        {busy && onStop ? (
          <button type="button" className="btn" onClick={onStop} disabled={canStop === false}>
            {t('Stop')}
          </button>
        ) : (
          <button type="submit" className="btn btn-primary" disabled={!canSend}>
            {t('Send')}
          </button>
        )}
        {onClear && (
          <button
            type="button"
            className="icon-btn icon-btn-delete"
            onClick={onClear}
            disabled={clearDisabled}
            title={t('Clear conversation')}
            aria-label={t('Clear conversation')}
          />
        )}
      </form>

      <p className="ask-disclaimer">{t('AI can make mistakes. Check answers.')}</p>
    </div>
  );
}
