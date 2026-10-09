import { forwardRef, useCallback, useEffect, useImperativeHandle, useLayoutEffect, useMemo, useRef, useState, type ReactElement } from 'react';
import { FlatList, StyleSheet, View, type NativeScrollEvent, type NativeSyntheticEvent } from 'react-native';
import type { AskActionProposal, AskMessage, AskTrackedWork, AskWorkSnapshot, CliPermissionRequest, CliPermissionResolution } from '@dashboard/types/models';
import { cliRequestForMessage, proposalForMessage, workCardHosts, type StreamingTurn } from '@dashboard/lib/askConversation';
import { permissionDeniedExplanation } from '@dashboard/lib/cliPermissions';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing } from '../../theme/typography';
import { AppText } from '../ui/AppText';
import { Button } from '../ui/Button';
import { announce, useReducedMotion } from '../../lib/accessibility';
import { Markdown } from './Markdown';
import { MessageView, ThinkingBlock } from './MessageView';
import { ToolChips } from './ToolChips';
import { classifyMessagesChange, TranscriptFollow, type FollowScroll, type ScrollMetrics } from './transcriptFollow';
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
/** A follow scroll is repeated once a frame, at most this many times, while the native view has not caught up. */
const SETTLE_FRAMES = 30;
const FRAME_MS = 16;

function metricsOf(event: NativeSyntheticEvent<NativeScrollEvent>): ScrollMetrics {
  const { contentOffset, contentSize, layoutMeasurement } = event.nativeEvent;
  return { offset: contentOffset.y, contentHeight: contentSize.height, viewportHeight: layoutMeasurement.height };
}

/**
 * The scrolling transcript (the dashboard's AskMessageList): follows new content while the reader is at the
 * bottom (see TranscriptFollow: the reader's own scrolling decides, and "New messages" brings them back), loads older
 * pages when the reader reaches the top (keeping their place), hosts each tracked item's live card on the message
 * that started it, and shows the streaming reply, the waiting text, and turn failures.
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
  // Jumps (to a work card, to the newest message) scroll without animation under Reduce Motion.
  const reduceMotion = useReducedMotion();
  const animated = !reduceMotion;
  const deniedNote = permissionDeniedExplanation(t, cliResolution);

  // What the reader has seen of the end of the transcript: the newest message, and whether a reply was streaming.
  const lastId = messages.length > 0 ? messages[messages.length - 1].id : null;
  const tail = `${lastId ?? ''}|${streaming ? 'streaming' : ''}`;
  const tailRef = useRef(tail);
  const [seenTail, setSeenTail] = useState(tail);
  const [following, setFollowing] = useState(true);
  const [follow] = useState(() => new TranscriptFollow((value) => {
    setFollowing(value);
    // Stopping to read older messages: everything up to here has been seen; anything after it is new.
    setSeenTail(tailRef.current);
  }));
  const firstIdRef = useRef<string | null>(null);

  // A follow scroll can reach the native view before the content it was computed for has mounted (Android then
  // clamps it to the old end): repeat it on the next frames until a scroll event shows the end in place.
  const frameRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const settle = useCallback(() => {
    if (frameRef.current !== null) clearTimeout(frameRef.current);
    let frames = 0;
    const tick = () => {
      frameRef.current = null;
      const again = follow.unsettledScroll();
      if (!again || ++frames > SETTLE_FRAMES) return;
      listRef.current?.scrollToOffset(again);
      frameRef.current = setTimeout(tick, FRAME_MS);
    };
    frameRef.current = setTimeout(tick, FRAME_MS);
  }, [follow]);
  useEffect(() => () => { if (frameRef.current !== null) clearTimeout(frameRef.current); }, []);

  const scrollTo = useCallback((scroll: FollowScroll | null) => {
    if (!scroll) return;
    listRef.current?.scrollToOffset(scroll);
    if (!scroll.animated) settle();
  }, [settle]);

  // Before the new content's layout events arrive: older messages loaded above keep the reader's place, and another
  // conversation starts at its newest message.
  useLayoutEffect(() => {
    tailRef.current = tail;
    const change = classifyMessagesChange(firstIdRef.current, messages.map((m) => m.id));
    firstIdRef.current = messages.length > 0 ? messages[0].id : null;
    if (change === 'prepended') follow.prepended();
    else if (change === 'replaced') follow.reset();
  }, [messages, tail, follow]);

  const showNewMessages = !following && messages.length > 0 && tail !== seenTail;
  useEffect(() => {
    if (showNewMessages) announce(t('New messages'));
  }, [showNewMessages, t]);

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
    follow.jumped();
    listRef.current?.scrollToIndex({ index, viewPosition: 0, animated });
  }, [animated, follow]);

  const scrollToBottom = useCallback(() => scrollTo(follow.toBottom(animated)), [animated, follow, scrollTo]);

  useImperativeHandle(ref, () => ({
    scrollToWork: (workId: string) => {
      const index = indexOfWork(workId);
      if (index < 0) return false;
      scrollToIndex(index);
      return true;
    },
    scrollToBottom,
  }), [indexOfWork, scrollToIndex, scrollToBottom]);

  function onScroll(event: NativeSyntheticEvent<NativeScrollEvent>) {
    const metrics = metricsOf(event);
    scrollTo(follow.scrolled(metrics));
    if (metrics.offset < LOAD_OLDER_THRESHOLD && hasMore && !loadingOlder && messages.length > 0) onLoadOlder();
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
      {/* The visible line rotates; the spoken label stays put so TalkBack says it once, not on every rotation. */}
      {showWaiting ? <AppText muted style={styles.waiting} testID="ask-waiting" accessibilityLabel={t('Thinking...')} accessibilityLiveRegion="polite">{waitingText || t('Thinking...')}</AppText> : null}
      {turnError && !turnActive ? (
        <View style={[styles.error, { borderColor: colors.danger }]} accessibilityRole="alert" accessibilityLiveRegion="polite" testID="ask-turn-error">
          <AppText color="danger">{t('The captain turn failed: {{reason}}', { reason: turnError })}</AppText>
        </View>
      ) : null}
    </View>
  );

  const empty = messages.length === 0 && !streaming && !turnActive;

  return (
    <View style={styles.fill}>
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
        // Following is decided by the reader's own gestures, not by scroll events that content growth causes.
        onScrollBeginDrag={() => follow.dragBegan()}
        onScrollEndDrag={(event) => follow.dragEnded(metricsOf(event))}
        onMomentumScrollBegin={() => follow.momentumBegan()}
        onMomentumScrollEnd={(event) => follow.momentumEnded(metricsOf(event))}
        onContentSizeChange={(_width, height) => scrollTo(follow.contentSizeChanged(height))}
        // The viewport shrinks when the keyboard opens (and in landscape): a transcript following the newest message
        // keeps its end in view, or a reply that arrived just before the keyboard finished opening sat under it.
        onLayout={(event) => scrollTo(follow.layoutChanged(event.nativeEvent.layout.height))}
        // Keeps the reader's place when older messages load above. While following, the end is put back in view after
        // any adjustment it makes (TranscriptFollow.scrolled).
        maintainVisibleContentPosition={{ minIndexForVisible: 0 }}
        keyboardShouldPersistTaps="handled"
        keyboardDismissMode="interactive"
        onScrollToIndexFailed={(info) => {
          listRef.current?.scrollToOffset({ offset: info.averageItemLength * info.index, animated: false });
          setTimeout(() => listRef.current?.scrollToIndex({ index: info.index, viewPosition: 0, animated }), 100);
        }}
        initialNumToRender={20}
        windowSize={11}
      />
      {showNewMessages ? (
        <View style={styles.newMessages}>
          <Button
            label={t('New messages')}
            accessibilityHint={t('Scrolls to the newest message')}
            icon="arrow-down"
            onPress={scrollToBottom}
            testID="ask-new-messages"
            style={styles.newMessagesButton}
          />
        </View>
      ) : null}
    </View>
  );
});

const styles = StyleSheet.create({
  fill: { flex: 1 },
  newMessages: { position: 'absolute', left: 0, right: 0, bottom: spacing.md, alignItems: 'center', pointerEvents: 'box-none' },
  newMessagesButton: { borderRadius: radius.pill, paddingHorizontal: spacing.lg },
  content: { padding: spacing.md },
  grow: { flexGrow: 1 },
  older: { alignItems: 'center' },
  streaming: { gap: spacing.xs, marginBottom: spacing.md },
  bubble: { borderRadius: radius.lg, padding: spacing.md, gap: spacing.xs, borderWidth: StyleSheet.hairlineWidth },
  bold: { fontWeight: '600' },
  waiting: { fontStyle: 'italic', marginBottom: spacing.md },
  error: { borderWidth: 1, borderRadius: radius.md, padding: spacing.md, marginBottom: spacing.md },
});
