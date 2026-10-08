import { useEffect, useRef, useState } from 'react';
import type { CaptainChatMetrics } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import { chatTurnStatistics } from '../../lib/chatMetrics';
import type { ToolEvent } from './ChatToolChips';

/**
 * Per-turn statistics shown behind an (i) affordance rather than a strip under every reply.
 * Time to first token, streaming time, tokens/sec, token count, total time, and -- when the turn
 * called any tools -- the number of tool calls and the total time spent in them appear in a small
 * popover on click. Token count prefers completion tokens and falls back to the runtime's estimate.
 */
export default function ChatMetricsInfo({ metrics, tools }: { metrics: CaptainChatMetrics; tools?: ToolEvent[] }) {
  const { t } = useLocale();
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLSpanElement>(null);

  useEffect(() => {
    if (!open) return undefined;
    function onDown(e: MouseEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    }
    function onKey(e: globalThis.KeyboardEvent) {
      if (e.key === 'Escape') setOpen(false);
    }
    document.addEventListener('mousedown', onDown);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onDown);
      document.removeEventListener('keydown', onKey);
    };
  }, [open]);

  // When the turn invoked tools, the rows end with how many and the total time spent in them (lib/chatMetrics).
  const rows = chatTurnStatistics(t, metrics, tools);

  return (
    <span className="chat-metrics-info" ref={ref}>
      <button
        type="button"
        className="chat-metrics-info-btn"
        onClick={() => setOpen((v) => !v)}
        title={t('Turn statistics')}
        aria-label={t('Turn statistics')}
        aria-expanded={open}
      >
        <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <circle cx="12" cy="12" r="10" />
          <path d="M12 16v-4" />
          <path d="M12 8h.01" />
        </svg>
      </button>
      {open && (
        <span className="chat-metrics-popover" role="dialog" aria-label={t('Turn statistics')}>
          {rows.map(({ key, label, value }) => (
            <span key={key} className="chat-metrics-row">
              <span className="chat-metrics-row-label">{label}</span>
              <span className="chat-metrics-row-value">{value}</span>
            </span>
          ))}
        </span>
      )}
    </span>
  );
}
