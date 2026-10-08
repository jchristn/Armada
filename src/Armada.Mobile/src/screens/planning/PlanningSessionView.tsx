import { Stack, useRouter, type Href } from 'expo-router';
import { useCallback, useEffect, useRef, useState } from 'react';
import { ActivityIndicator, Platform, ScrollView, StyleSheet, TextInput, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import {
  deletePlanningSession,
  dispatchPlanningSession,
  sendPlanningSessionMessage,
  stopPlanningSession,
  stopPlanningTurn,
  summarizePlanningSession,
} from '@dashboard/api/client';
import { randomThinkingMessage } from '@dashboard/lib/askThinkingMessages';
import { mergeSessionDetail } from '@dashboard/lib/liveMerge';
import { getLatestAssistantMessage, resolveDispatchSeedUpdate, type DispatchSeedState } from '@dashboard/lib/planningSessions';
import { ActionRow, InfoRow, SwitchField, useActionRunner } from '../../build/fields';
import { errorMessage } from '../../build/useLiveResource';
import { statusTone } from '../../components/ask/statusTone';
import { AppText, Banner, Button, ConfirmDialog, ErrorState, KeyboardAvoidingPane, LoadingState, Section, StatusBadge, TextField } from '../../components/ui';
import { Disclosure } from '../../components/ui/Disclosure';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { useTheme } from '../../theme/ThemeContext';
import { MIN_TOUCH, radius, spacing, typography } from '../../theme/typography';
import { PlanningMessageView } from './PlanningMessageView';
import { planningDispatchHref } from './planningDispatch';
import type { PlanningCatalog } from './usePlanningCatalog';
import { usePlanningSession } from './usePlanningSession';
import { useReducedMotion } from '../../lib/accessibility';

export interface PlanningSessionViewProps {
  sessionId: string;
  catalog: PlanningCatalog;
  /** Shown beside the list on tablets: no navigation header of its own. */
  embedded?: boolean;
  /** Draft first message (from a prefilled start). */
  initialComposer?: string | null;
  /** The session is gone (deleted here or elsewhere): leave it. */
  onClosed: () => void;
}

/**
 * One planning session (the dashboard's Current Session and Dispatch From Session cards): the session summary, the
 * live transcript with tool calls and reasoning, the composer (stream / show thinking, Send, Stop), End Session and
 * Delete with the dashboard's confirmations, and the dispatch draft (seeded from the latest reply, summarized on
 * request, dispatched as a voyage or opened in Dispatch).
 */
export function PlanningSessionView({ sessionId, catalog, embedded, initialComposer, onClosed }: PlanningSessionViewProps) {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { colors } = useTheme();
  const router = useRouter();
  const { pushToast } = useNotifications();
  const { busy, run } = useActionRunner();
  const deletingRef = useRef(false);

  const [selectedMessageId, setSelectedMessageId] = useState('');
  const [dispatchTitle, setDispatchTitle] = useState('');
  const [dispatchDescription, setDispatchDescription] = useState('');
  const seedRef = useRef<DispatchSeedState | null>(null);
  const [composer, setComposer] = useState(initialComposer ?? '');
  const [streaming, setStreaming] = useState(true);
  const [showThinking, setShowThinking] = useState(false);
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [confirmEnd, setConfirmEnd] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [thinkingLine, setThinkingLine] = useState('');
  const scrollRef = useRef<ScrollView | null>(null);

  const session = usePlanningSession(sessionId, {
    onSummary: (event) => {
      if (event.messageId) setSelectedMessageId(event.messageId);
      setDispatchTitle(event.draft?.title || '');
      setDispatchDescription(event.draft?.description || '');
      seedRef.current = { key: `${event.sessionId}:${event.messageId || 'latest'}`, title: event.draft?.title || '', description: event.draft?.description || '', source: 'summary' };
    },
    onDispatched: () => pushToast('success', t('Dispatch created from this planning session.')),
    onDeleted: () => {
      if (!deletingRef.current) pushToast('warning', t('Planning session deleted.'));
      onClosed();
    },
  });
  const { detail, setDetail } = session;
  const current = detail?.session ?? null;
  const messages = detail?.messages ?? [];
  const status = current?.status;
  const responding = status === 'Responding';

  // The latest reply is the dispatch source until the user picks another (or the picked one disappears).
  useEffect(() => {
    if (!detail) return;
    const latest = getLatestAssistantMessage(detail.messages);
    if (!latest) return;
    if (!selectedMessageId || !detail.messages.some((m) => m.id === selectedMessageId)) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- follows the transcript as replies arrive
      setSelectedMessageId(latest.id);
    }
  }, [detail, selectedMessageId]);

  // Seed the draft from the selected reply, keeping what the user typed (the dashboard's resolveDispatchSeedUpdate).
  useEffect(() => {
    if (!detail) return;
    const message = detail.messages.find((m) => m.id === selectedMessageId) ?? null;
    const next = resolveDispatchSeedUpdate({
      sessionId: detail.session.id,
      sessionTitle: detail.session.title,
      message,
      currentTitle: dispatchTitle,
      currentDescription: dispatchDescription,
      previousSeed: seedRef.current,
    });
    if (!next) return;
    seedRef.current = next;
    if (next.title !== dispatchTitle) setDispatchTitle(next.title);
    if (next.description !== dispatchDescription) setDispatchDescription(next.description);
  }, [detail, selectedMessageId, dispatchTitle, dispatchDescription]);

  // Rotate the waiting line every 4 seconds while the captain responds (the dashboard's thinking messages).
  useEffect(() => {
    if (!responding) return undefined;
    // eslint-disable-next-line react-hooks/set-state-in-effect -- the first line shows at once, then rotates
    setThinkingLine((prev) => randomThinkingMessage(prev));
    const timer = setInterval(() => setThinkingLine((prev) => randomThinkingMessage(prev)), 4000);
    return () => clearInterval(timer);
  }, [responding]);

  const reduceMotion = useReducedMotion();
  const scrollToEnd = useCallback(() => scrollRef.current?.scrollToEnd({ animated: !reduceMotion }), [reduceMotion]);
  const lastAssistantId = [...messages].reverse().find((m) => m.role.toLowerCase() === 'assistant')?.id;
  const busyTurn = responding || sending;
  const selectedMessage = messages.find((m) => m.id === selectedMessageId) ?? null;
  const canSend = status === 'Active' && composer.trim().length > 0 && !sending;
  const canEnd = !!current && (status === 'Active' || status === 'Responding') && busy !== 'end';
  const canSummarize = !!current && !!selectedMessage?.content.trim() && busy !== 'summarize';
  const canOpenInDispatch = !!current && dispatchDescription.trim().length > 0;
  const canDispatch = !!current && !!selectedMessage?.content.trim() && dispatchDescription.trim().length > 0 && busy !== 'dispatch';
  const captainName = detail?.captain?.name || current?.captainId || '';
  const runtime = detail?.captain?.runtime || '-';

  const send = async () => {
    if (!current || !canSend) return;
    setSending(true);
    setError(null);
    try {
      const result = await sendPlanningSessionMessage(current.id, { content: composer.trim(), showThinking, stream: streaming });
      setComposer('');
      // A fast captain can finish the turn over the socket before this response arrives; keep the newer state.
      setDetail((prev) => mergeSessionDetail(prev, result));
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setSending(false);
    }
  };

  const stopTurn = async () => {
    if (!current) return;
    const result = await run('stopTurn', () => stopPlanningTurn(current.id));
    if (result) setDetail(result);
  };

  const endSession = async () => {
    if (!current) return;
    const result = await run('end', () => stopPlanningSession(current.id), t('Planning session is ending.'));
    if (result) setDetail(result);
  };

  const deleteSession = async () => {
    if (!current) return;
    deletingRef.current = true;
    const done = await run('delete', async () => { await deletePlanningSession(current.id); return true; });
    if (done) {
      pushToast('warning', t('Planning session deleted.'));
      onClosed();
    } else {
      deletingRef.current = false;
    }
  };

  const summarize = async () => {
    if (!current || !selectedMessageId) return;
    const result = await run('summarize', () => summarizePlanningSession(current.id, { messageId: selectedMessageId, title: dispatchTitle.trim() || undefined }), t('Dispatch draft summarized from planning output.'));
    if (!result) return;
    setDispatchTitle(result.title);
    setDispatchDescription(result.description);
    seedRef.current = { key: `${result.sessionId}:${result.messageId}`, title: result.title, description: result.description, source: 'summary' };
  };

  const dispatch = async () => {
    if (!current || !dispatchDescription.trim()) return;
    const voyage = await run('dispatch', () => dispatchPlanningSession(current.id, {
      messageId: selectedMessageId || undefined,
      title: dispatchTitle.trim() || undefined,
      description: dispatchDescription.trim(),
    }), t('Dispatch created from planning session.'));
    if (voyage) router.push(`/voyages/${voyage.id}` as Href);
  };

  // Committing planning output to dispatch releases the reserved captain and dock (best-effort, like the dashboard).
  const openInDispatch = async (prompt: string, voyageTitle: string | null) => {
    if (!current || !prompt.trim()) return;
    try { setDetail(await stopPlanningSession(current.id)); } catch { /* the user can still end the session by hand */ }
    router.push(planningDispatchHref(current, catalog.pipelines, prompt.trim(), voyageTitle) as Href);
  };

  if (session.loading) return <LoadingState label={t('Loading planning session...')} />;
  if (!detail || !current) {
    return <ErrorState title={t('Planning session not found.')} message={session.error} retryLabel={t('Retry')} onRetry={() => void session.reload()} />;
  }

  return (
    <SafeAreaView edges={embedded ? [] : ['bottom', 'left', 'right']} style={[styles.fill, { backgroundColor: colors.background }]} testID="planning-session">
      {!embedded ? <Stack.Screen options={{ title: current.title }} /> : null}
      {/* Measures its own place in the window (a fixed 90 dp or 0 in the tablet pane left the composer under the keyboard). */}
      <KeyboardAvoidingPane behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <ScrollView ref={scrollRef} contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled" onContentSizeChange={scrollToEnd} testID="planning-transcript">
          <View style={styles.head}>
            <AppText variant="heading" accessibilityRole="header" style={styles.flex}>{current.title}</AppText>
            {status ? <StatusBadge label={t(status)} tone={statusTone(status)} /> : null}
          </View>
          <AppText muted style={styles.pad}>
            {t('Chat with {{captain}} against {{vessel}}, keep the transcript intact, and promote the right reply into dispatch.', { captain: captainName, vessel: detail.vessel?.name || current.vesselId })}
          </AppText>

          <View style={styles.pad}>
            <Disclosure title={t('Session details')} testID="planning-details">
              <View style={[styles.card, { borderColor: colors.border, backgroundColor: colors.surface }]}>
                <InfoRow label={t('Captain')} value={captainName} />
                <InfoRow label={t('Runtime')} value={runtime} />
                <InfoRow label={t('Vessel')} value={detail.vessel?.name || current.vesselId} />
                <InfoRow label={t('Branch')} value={current.branchName} mono />
                <InfoRow label={t('Pipeline')} value={catalog.pipelineName(current.pipelineId)} />
                <InfoRow label={t('Playbooks')} value={current.selectedPlaybooks?.length ?? 0} />
                <InfoRow label={t('Updated')} value={`${formatRelativeTime(current.lastUpdateUtc)} (${formatDateTime(current.lastUpdateUtc)})`} />
                <InfoRow label={t('Messages')} value={messages.length} />
                <InfoRow label={t('ID')} value={current.id} mono />
              </View>
              <SwitchField label={t('Stream responses')} value={streaming} onChange={setStreaming} disabled={busyTurn} testID="planning-stream" />
              <SwitchField label={t('Show thinking')} value={showThinking} onChange={setShowThinking} disabled={busyTurn} hint={t('Surface the model reasoning above the answer. Mux streams it natively; other runtimes are asked to include it.')} testID="planning-show-thinking" />
            </Disclosure>
          </View>

          <ActionRow>
            <Button
              label={busy === 'end' || status === 'Stopping' ? t('Ending...') : t('End Session')}
              variant="secondary"
              onPress={() => setConfirmEnd(true)}
              disabled={!canEnd}
              testID="planning-end"
            />
            <Button label={t('Delete')} variant="danger" icon="trash-outline" onPress={() => setConfirmDelete(true)} disabled={busyTurn || busy === 'delete'} testID="planning-delete" />
          </ActionRow>

          {current.failureReason ? <Banner tone="danger" title={current.failureReason} /> : null}
          {error ? <Banner tone="danger" title={error} testID="planning-error" /> : null}
          {runtime === 'Codex' ? <Banner tone="info" title={t('Codex responses cannot be streamed and will arrive upon completion.')} /> : null}

          <View style={styles.transcript}>
            {messages.length === 0 ? <AppText muted>{t('No transcript yet. Send the first planning message below.')}</AppText> : null}
            {messages.map((message) => (
              <PlanningMessageView
                key={message.id}
                message={message}
                captainName={captainName}
                tools={session.tools[message.id]}
                thinking={session.thinking[message.id]}
                streaming={busyTurn && message.id === lastAssistantId}
                selected={message.id === selectedMessageId}
                onSelect={setSelectedMessageId}
                onOpenInDispatch={(id) => {
                  const text = messages.find((m) => m.id === id)?.content ?? '';
                  void openInDispatch(text, dispatchTitle.trim() || current.title || null);
                }}
              />
            ))}
            {responding ? (
              // The line rotates every few seconds; the row's spoken label stays put so it is announced once.
              <View style={styles.thinkingRow} accessible accessibilityLabel={t('Thinking...')} accessibilityLiveRegion="polite" testID="planning-thinking">
                <ActivityIndicator color={colors.primary} />
                <AppText variant="caption" muted style={styles.flex}>{thinkingLine}</AppText>
              </View>
            ) : null}
          </View>

          <Section title={t('Dispatch From Session')}>
            <View style={styles.cardBody}>
              <AppText variant="caption" muted style={styles.gap}>
                {t('Select an assistant response, optionally summarize it into a cleaner draft, then dispatch or open the draft in the main dispatch page.')}
              </AppText>
              {selectedMessage ? <AppText variant="caption" muted style={[typography.mono, styles.gap]} selectable>{selectedMessage.id}</AppText> : null}
              <TextField label={t('Voyage Title')} value={dispatchTitle} onChangeText={setDispatchTitle} placeholder={t('Optional override for the resulting voyage title')} testID="planning-dispatch-title" />
              <TextField
                label={t('Mission Description')}
                value={dispatchDescription}
                onChangeText={setDispatchDescription}
                placeholder={t('Select a planning response to seed the dispatch description.')}
                multiline
                numberOfLines={8}
                textAlignVertical="top"
                testID="planning-dispatch-description"
              />
              <Button label={busy === 'summarize' ? t('Summarizing...') : t('Summarize Draft')} variant="secondary" onPress={() => void summarize()} disabled={!canSummarize} busy={busy === 'summarize'} testID="planning-summarize" />
              <Button label={t('Open In Dispatch')} variant="secondary" onPress={() => void openInDispatch(dispatchDescription, dispatchTitle || null)} disabled={!canOpenInDispatch} testID="planning-open-dispatch" />
              <Button label={busy === 'dispatch' ? t('Dispatching...') : t('Dispatch')} onPress={() => void dispatch()} disabled={!canDispatch} busy={busy === 'dispatch'} testID="planning-dispatch" />
            </View>
          </Section>
        </ScrollView>

        <View style={[styles.composer, { borderTopColor: colors.border, backgroundColor: colors.surface }]}>
          <TextInput
            testID="planning-input"
            value={composer}
            onChangeText={setComposer}
            editable={status === 'Active'}
            multiline
            placeholder={t('Describe the problem, ask for a plan, or negotiate the next steps with the captain.')}
            placeholderTextColor={colors.textMuted}
            accessibilityLabel={t('Message')}
            style={[styles.input, typography.body, { color: colors.text, borderColor: colors.control, backgroundColor: colors.background }]}
          />
          {responding ? (
            <Button label={t('Stop')} variant="danger" icon="stop" onPress={() => void stopTurn()} busy={busy === 'stopTurn'} testID="planning-stop" style={styles.sendButton} />
          ) : (
            <Button label={t('Send')} icon="send" onPress={() => void send()} disabled={!canSend} busy={sending} testID="planning-send" style={styles.sendButton} />
          )}
        </View>
      </KeyboardAvoidingPane>

      <ConfirmDialog
        open={confirmEnd}
        title={t('End Planning Session')}
        message={t('End this planning session and release the reserved captain and dock? The transcript will be kept until you delete it or it expires under server retention settings.')}
        confirmLabel={t('End Session')}
        cancelLabel={t('Cancel')}
        onConfirm={() => { setConfirmEnd(false); void endSession(); }}
        onCancel={() => setConfirmEnd(false)}
        testID="planning-end-confirm"
      />
      <ConfirmDialog
        open={confirmDelete}
        title={t('Delete Planning Session')}
        message={t('Delete this planning session and its transcript?')}
        confirmLabel={t('Delete Session')}
        cancelLabel={t('Cancel')}
        danger
        onConfirm={() => { setConfirmDelete(false); void deleteSession(); }}
        onCancel={() => setConfirmDelete(false)}
        testID="planning-delete-confirm"
      />
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  flex: { flex: 1 },
  content: { paddingVertical: spacing.lg, flexGrow: 1 },
  head: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, paddingHorizontal: spacing.lg, marginBottom: spacing.xs },
  pad: { paddingHorizontal: spacing.lg, marginBottom: spacing.md },
  gap: { marginBottom: spacing.sm },
  card: { borderWidth: StyleSheet.hairlineWidth, borderRadius: radius.md, overflow: 'hidden', marginBottom: spacing.md },
  cardBody: { padding: spacing.lg },
  transcript: { paddingHorizontal: spacing.md, marginBottom: spacing.lg },
  thinkingRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, paddingHorizontal: spacing.sm },
  composer: { flexDirection: 'row', alignItems: 'flex-end', gap: spacing.sm, padding: spacing.sm, borderTopWidth: StyleSheet.hairlineWidth },
  input: { flex: 1, minHeight: MIN_TOUCH, maxHeight: 140, borderWidth: 1, borderRadius: radius.md, paddingHorizontal: spacing.md, paddingVertical: spacing.sm },
  sendButton: { marginBottom: 0 },
});
