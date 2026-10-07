import { forwardRef, useCallback, useImperativeHandle, useMemo, useRef, type ReactElement } from 'react';
import { FlatList, StyleSheet, View, type NativeScrollEvent, type NativeSyntheticEvent } from 'react-native';
import type { AskActionProposal, AskMessage, AskTrackedWork, AskWorkSnapshot, CliPermissionRequest, CliPermissionResolution } from '@dashboard/types/models';
import { cliRequestForMessage, proposalForMessage, workCardHosts, type StreamingTurn } from '@dashboard/lib/askConversation';
import { permissionDeniedExplanation } from '@dashboard/lib/cliPermissions';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing } from '../../theme/typography';
import { AppText } from '../ui/AppText';
import { Button } from '../ui/Button';
import { Markdown } from './Markdown';
import { MessageView, ThinkingBlock } from './MessageView';
import { ToolChips } from './ToolChips';
import { WorkCard } from './WorkCard';

export interface MessageListHandle {
  /** Scroll the live card of a tracked item into view; false when no loaded message hosts it. */
  scrollToWork: (workId: string) => boolean;
  scrollToBottom: () => void;
}

interface MessageListProps {
  messages: AskMessage[];
  proposals: Record<string, AskActionProposal>;
  trackedWork: AskTrackedWork[];
  snapshots: Record<string, AskWorkSnapshot>;
  hasMore: boolean;
  loadingOlder: boolean;
  onLoadOlder: () => void;
  streaming: StreamingTurn | null;
  turnActive: boolean;
  waitingText: string;
  captainName: string | null;
  captainNames: Record<string, string>;
  busyProposalId: string | null;
  onApprove: (proposal: AskActionProposal) => void;
  onReject: (proposal: AskActionProposal) => void;
  highlightedWorkId: string | null;
  emptyState: ReactElement;
  turnError: string | null;
  cliPermissions: Record<string, CliPermissionRequest>;
  onCliDecided: (request: CliPermissionRequest) => void;
  cliResolution: CliPermissionResolution | null;
}

const LOAD_OLDER_THRESHOLD = 80;
const STICK_THRESHOLD = 140;

/**
 * The scrolling transcript (the dashboard's AskMessageList): follows new content while the reader is at the
 * bottom, loads older pages when the reader reaches the top (keeping their place), hosts each tracked item's live
 * card on the message that started it, and shows the streaming reply, the waiting text, and turn failures.
 */
export const MessageList = forwardRef<MessageListHandle, MessageListProps>(function MessageList(props, ref) {
  const {
    messages, proposals, trackedWork, snapshots, hasMore, loadingOlder, onLoadOlder, streaming, turnActive, waitingText,
    captainName, captainNames, busyProposalId, onApprove, onReject, highlightedWorkId, emptyState, turnError,
    cliPermissions, onCliDecided, cliResolution,
  } = props;
  const { t } = useLocale();
  const { colors } = useTheme();
  const listRef = useRef<FlatList<AskMessage>>(null);
  const stickRef = useRef(true);
  const deniedNote = permissionDeniedExplanation(t, cliResolution);

  const hosts = useMemo(() => workCardHosts(messages), [messages]);
  // A proposal shown as a confirm card on its ActionProposal message is not repeated on the ActionResult.
  const proposalHosts = useMemo(() => new Set(
    messages.filter((m) => m.kind === 'ActionProposal').map((m) => m.proposalId ?? m.proposal?.id).filter((id): id is string => !!id),
  ), [messages]);
  const workById = useMemo(() => new Map(trackedWork.map((w) => [w.id, w])), [trackedWork]);

  const indexOfWork = useCallback((workId: string) => {
    const hostId = hosts[workId];
    return hostId ? messages.findIndex((m) => m.id === hostId) : -1;
  }, [hosts, messages]);

  const scrollToIndex = useCallback((index: number) => {
    stickRef.current = false;
    listRef.current?.scrollToIndex({ index, viewPosition: 0, animated: true });
  }, []);

  useImperativeHandle(ref, () => ({
    scrollToWork: (workId: string) => {
      const index = indexOfWork(workId);
      if (index < 0) return false;
      scrollToIndex(index);
      return true;
    },
    scrollToBottom: () => {
      stickRef.current = true;
      listRef.current?.scrollToEnd({ animated: true });
    },
  }), [indexOfWork, scrollToIndex]);

  function onScroll(event: NativeSyntheticEvent<NativeScrollEvent>) {
    const { contentOffset, contentSize, layoutMeasurement } = event.nativeEvent;
    stickRef.current = contentSize.height - contentOffset.y - layoutMeasurement.height < STICK_THRESHOLD;
    if (contentOffset.y < LOAD_OLDER_THRESHOLD && hasMore && !loadingOlder && messages.length > 0) onLoadOlder();
  }

  const showWaiting = turnActive && (!streaming || (!streaming.text && streaming.tools.length === 0));
  const cliState = { cliPermissions };

  const renderItem = ({ item: message }: { item: AskMessage }) => {
    const workId = message.trackedWorkId ?? message.trackedWork?.id ?? null;
    const hosted = !!workId && hosts[workId] === message.id;
    const work = workId ? workById.get(workId) ?? message.trackedWork ?? null : null;
    const card = hosted && workId
      ? <WorkCard work={work} snapshot={snapshots[workId] ?? work?.snapshot ?? null} highlighted={highlightedWorkId === workId} />
      : null;
    const resolved = proposalForMessage({ proposals }, message);
    const proposal = resolved && message.kind === 'ActionResult' && proposalHosts.has(resolved.id) ? null : resolved;
    return (
      <MessageView
        message={message}
        proposal={proposal}
        captainName={(message.captainId && captainNames[message.captainId]) || captainName}
        proposalBusy={!!proposal && busyProposalId === proposal.id}
        onApprove={onApprove}
        onReject={onReject}
        workCard={card}
        cliRequest={message.kind === 'CliPermission' ? cliRequestForMessage(cliState, message) : null}
        onCliDecided={onCliDecided}
        permissionDeniedNote={deniedNote}
        onShowWork={workId && !hosted ? () => { const index = indexOfWork(workId); if (index >= 0) scrollToIndex(index); } : undefined}
      />
    );
  };

  const header = hasMore ? (
    <View style={styles.older}>
      <Button
        label={loadingOlder ? t('Loading earlier messages...') : t('Load earlier messages')}
        variant="ghost"
        onPress={onLoadOlder}
        disabled={loadingOlder}
        testID="ask-load-older"
      />
    </View>
  ) : null;

  const footer = (
    <View>
      {streaming && (streaming.text || streaming.tools.length > 0 || streaming.thinking) ? (
        <View style={styles.streaming} testID="ask-streaming" accessibilityState={{ busy: !streaming.finished }}>
          <ToolChips tools={streaming.tools} permissionDeniedNote={deniedNote} />
          <View style={[styles.bubble, { backgroundColor: colors.surface, borderColor: colors.border }]}>
            <AppText variant="caption" muted style={styles.bold}>{captainName || t('Captain')}</AppText>
            {streaming.thinking ? <ThinkingBlock key={streaming.finished ? 'done' : 'live'} text={streaming.thinking} live={!streaming.finished} /> : null}
            {streaming.text ? <Markdown>{streaming.text}</Markdown> : null}
          </View>
        </View>
      ) : null}
      {showWaiting ? <AppText muted style={styles.waiting} testID="ask-waiting" accessibilityLiveRegion="polite">{waitingText || t('Thinking...')}</AppText> : null}
      {turnError && !turnActive ? (
        <View style={[styles.error, { borderColor: colors.danger }]} accessibilityRole="alert" testID="ask-turn-error">
          <AppText color="danger">{t('The captain turn failed: {{reason}}', { reason: turnError })}</AppText>
        </View>
      ) : null}
    </View>
  );

  const empty = messages.length === 0 && !streaming && !turnActive;

  return (
    <FlatList
      ref={listRef}
      testID="ask-transcript"
      accessibilityLabel={t('Conversation messages')}
      data={empty ? [] : messages}
      keyExtractor={(m) => m.id}
      renderItem={renderItem}
      ListHeaderComponent={header}
      ListFooterComponent={footer}
      ListEmptyComponent={empty ? emptyState : null}
      contentContainerStyle={[styles.content, empty ? styles.grow : null]}
      onScroll={onScroll}
      scrollEventThrottle={64}
      onContentSizeChange={() => { if (stickRef.current) listRef.current?.scrollToEnd({ animated: false }); }}
      maintainVisibleContentPosition={{ minIndexForVisible: 0 }}
      keyboardShouldPersistTaps="handled"
      keyboardDismissMode="interactive"
      onScrollToIndexFailed={(info) => {
        listRef.current?.scrollToOffset({ offset: info.averageItemLength * info.index, animated: false });
        setTimeout(() => listRef.current?.scrollToIndex({ index: info.index, viewPosition: 0, animated: true }), 100);
      }}
      initialNumToRender={20}
      windowSize={11}
    />
  );
});

const styles = StyleSheet.create({
  content: { padding: spacing.md },
  grow: { flexGrow: 1 },
  older: { alignItems: 'center' },
  streaming: { gap: spacing.xs, marginBottom: spacing.md },
  bubble: { borderRadius: radius.lg, padding: spacing.md, gap: spacing.xs, borderWidth: StyleSheet.hairlineWidth },
  bold: { fontWeight: '600' },
  waiting: { fontStyle: 'italic', marginBottom: spacing.md },
  error: { borderWidth: 1, borderRadius: radius.md, padding: spacing.md, marginBottom: spacing.md },
});
