import { useRef, useState } from 'react';
import { StyleSheet, TextInput, View } from 'react-native';
import { chatWithCaptain } from '@dashboard/api/client';
import type { CaptainChatMessage, CaptainChatMetrics } from '@dashboard/types/models';
import { errorMessage } from '../../build/useLiveResource';
import { Markdown } from '../../components/ask/Markdown';
import { AppText, Button, IconButton } from '../../components/ui';
import { Disclosure } from '../../components/ui/Disclosure';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { MIN_TOUCH, radius, spacing, typography } from '../../theme/typography';

/** One turn of a captain chat (the dashboard's ChatTurn, without streaming: replies arrive whole). */
export interface CaptainChatTurn {
  role: 'user' | 'assistant' | 'system';
  text: string;
  model?: string | null;
  metrics?: CaptainChatMetrics | null;
  thinking?: string | null;
}

/** The history sent with a chat turn: earlier user and assistant turns (system notices are local). */
export function chatHistory(turns: CaptainChatTurn[]): CaptainChatMessage[] {
  return turns
    .filter((turn): turn is CaptainChatTurn & { role: 'user' | 'assistant' } => turn.role === 'user' || turn.role === 'assistant')
    .map((turn) => ({ role: turn.role, content: turn.text }));
}

/** "1.2 s . 340 tokens . 52 tok/s" from a reply's metrics (empty when none are reported). */
export function metricsLine(metrics: CaptainChatMetrics | null | undefined): string {
  if (!metrics) return '';
  const parts: string[] = [];
  if (typeof metrics.totalMs === 'number') parts.push(`${(metrics.totalMs / 1000).toFixed(1)} s`);
  if (typeof metrics.totalTokens === 'number') parts.push(`${metrics.totalTokens} tokens`);
  if (typeof metrics.tokensPerSecond === 'number') parts.push(`${metrics.tokensPerSecond.toFixed(0)} tok/s`);
  return parts.join(' \u00b7 ');
}

/**
 * A direct chat with one captain (POST /captains/{id}/chat; not an Ask Armada thread): the runtime answers headlessly
 * with the conversation so far as history. Stop aborts the request (the server then cancels the runtime); Clear
 * starts over. The conversation lives only on this screen, as in the dashboard's captain chat panel.
 */
export function CaptainChat({ captainId, captainName, disabledReason }: { captainId: string; captainName: string; disabledReason?: string | null }) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const [turns, setTurns] = useState<CaptainChatTurn[]>([]);
  const [input, setInput] = useState('');
  const [busy, setBusy] = useState(false);
  const abortRef = useRef<AbortController | null>(null);

  async function send() {
    const message = input.trim();
    if (!message || busy) return;
    const history = chatHistory(turns);
    setTurns((current) => [...current, { role: 'user', text: message }]);
    setInput('');
    setBusy(true);
    const controller = new AbortController();
    abortRef.current = controller;
    try {
      const reply = await chatWithCaptain(captainId, { message, history, showThinking: true }, { signal: controller.signal });
      if (reply.success) {
        setTurns((current) => [...current, { role: 'assistant', text: reply.reply, model: reply.model, metrics: reply.metrics, thinking: reply.thinking }]);
      } else {
        setTurns((current) => [...current, { role: 'system', text: reply.error || t('The captain did not reply.') }]);
      }
    } catch (e) {
      const stopped = controller.signal.aborted;
      setTurns((current) => [...current, { role: 'system', text: stopped ? t('Stopped.') : errorMessage(e) }]);
    } finally {
      abortRef.current = null;
      setBusy(false);
    }
  }

  function roleLabel(turn: CaptainChatTurn): string {
    if (turn.role === 'user') return t('You');
    if (turn.role === 'system') return t('System');
    return turn.model || captainName || t('Captain');
  }

  return (
    <View style={styles.wrap} testID="captain-chat">
      <View style={[styles.window, { borderColor: colors.border, backgroundColor: colors.surface }]}>
        {turns.length === 0 ? (
          <AppText muted style={styles.center}>{disabledReason || t('Send the first message to begin.')}</AppText>
        ) : turns.map((turn, i) => (
          <View
            key={i}
            testID={`captain-chat-turn-${turn.role}`}
            style={[
              styles.bubble,
              { borderColor: colors.border, alignSelf: turn.role === 'user' ? 'flex-end' : 'flex-start', backgroundColor: turn.role === 'user' ? colors.surfaceRaised : colors.background },
            ]}
          >
            <AppText variant="caption" muted>{[roleLabel(turn), metricsLine(turn.metrics)].filter(Boolean).join(' \u00b7 ')}</AppText>
            {turn.role === 'assistant' && turn.thinking && turn.thinking.trim() ? (
              <Disclosure title={t('Thinking')}>
                <AppText variant="caption" muted>{turn.thinking}</AppText>
              </Disclosure>
            ) : null}
            {turn.role === 'assistant' ? <Markdown>{turn.text}</Markdown> : <AppText selectable>{turn.text}</AppText>}
          </View>
        ))}
        {busy ? <AppText muted accessibilityLiveRegion="polite">{t('Thinking...')}</AppText> : null}
      </View>
      <View style={styles.composer}>
        <TextInput
          testID="captain-chat-input"
          value={input}
          onChangeText={setInput}
          placeholder={t('Message the captain...')}
          placeholderTextColor={colors.textMuted}
          accessibilityLabel={t('Message the captain...')}
          editable={!busy && !disabledReason}
          multiline
          style={[styles.input, typography.body, { color: colors.text, borderColor: colors.border, backgroundColor: colors.surface }]}
        />
        {busy ? (
          <Button label={t('Stop')} variant="secondary" onPress={() => abortRef.current?.abort()} testID="captain-chat-stop" style={styles.noMargin} />
        ) : (
          <Button label={t('Send')} onPress={() => void send()} disabled={!input.trim() || !!disabledReason} testID="captain-chat-send" style={styles.noMargin} />
        )}
        <IconButton icon="trash-outline" label={t('Clear conversation')} onPress={() => setTurns([])} color="textMuted" testID="captain-chat-clear" />
      </View>
      <AppText variant="caption" muted>{t('AI can make mistakes. Check answers.')}</AppText>
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: { paddingHorizontal: spacing.lg, marginBottom: spacing.lg, gap: spacing.sm },
  window: { borderWidth: StyleSheet.hairlineWidth, borderRadius: radius.md, padding: spacing.md, gap: spacing.sm, minHeight: 120 },
  center: { textAlign: 'center', marginVertical: spacing.lg },
  bubble: { maxWidth: '90%', borderWidth: StyleSheet.hairlineWidth, borderRadius: radius.md, padding: spacing.sm, gap: 2 },
  composer: { flexDirection: 'row', alignItems: 'flex-end', gap: spacing.sm },
  input: { flex: 1, minHeight: MIN_TOUCH, maxHeight: 140, borderWidth: 1.5, borderRadius: radius.md, paddingHorizontal: spacing.md, paddingVertical: spacing.sm },
  noMargin: { marginBottom: 0 },
});
