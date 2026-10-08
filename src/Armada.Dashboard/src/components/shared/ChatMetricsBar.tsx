import type { CaptainChatMetrics } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import { chatTurnStatistics } from '../../lib/chatMetrics';

/**
 * Compact per-turn statistics strip for a chat reply: time to first token, streaming time, tokens
 * per second, output token count, and total time. Shared by the Ask Armada and Planning chats.
 */
export default function ChatMetricsBar({ metrics }: { metrics: CaptainChatMetrics }) {
  const { t } = useLocale();
  const items = chatTurnStatistics(t, metrics, null, 'completion');
  return (
    <div className="chat-metrics text-dim">
      {items.map(({ key, label, value }) => (
        <span key={key} className="chat-metric"><span className="chat-metric-value">{value}</span> {label}</span>
      ))}
    </div>
  );
}
