import { Stack, useFocusEffect, useRouter, type Href } from 'expo-router';
import { useCallback, useContext, useEffect, useRef, useState } from 'react';
import { KeyboardAvoidingView, Linking, Modal, Pressable, StyleSheet, View } from 'react-native';
import { GestureHandlerRootView } from 'react-native-gesture-handler';
import { SafeAreaView } from 'react-native-safe-area-context';
import { HeaderHeightContext } from 'expo-router/react-navigation';
import type { AskThread, AskTrackedWork, CaptainToolAccessResult } from '@dashboard/types/models';
import { askCaptainAccess, instructionsDocUrl } from '@dashboard/lib/askCaptain';
import { randomGreeting } from '@dashboard/lib/askGreetings';
import { workRoute } from '@dashboard/lib/askWork';
import { fallbackReasonText } from '@dashboard/lib/cliPermissions';
import { useAuth } from '../auth/AuthContext';
import { useAsk } from '../ask/AskContext';
import { useAskConversation } from '../ask/useAskConversation';
import { HeaderActions } from '../components/app/HeaderActions';
import { Composer, type ComposerHandle } from '../components/ask/Composer';
import { ConversationOptionsSheet } from '../components/ask/ConversationOptionsSheet';
import { MessageList, type MessageListHandle } from '../components/ask/MessageList';
import { ThreadList } from '../components/ask/ThreadList';
import { WorkStrip } from '../components/ask/WorkStrip';
import { AppText, Banner, BottomSheet, Button, ConfirmDialog, ErrorState, IconButton, LoadingState, SplitView, TextField } from '../components/ui';
import { useLocale } from '../i18n/LocaleContext';
import { useLayout } from '../navigation/useLayout';
import { useNotifications } from '../notifications/NotificationContext';
import { useTheme } from '../theme/ThemeContext';
import { spacing } from '../theme/typography';

/** The last conversation reopens once per app session (MOBILE_APP_PLAN.md design principle 1). */
let restoredLastThread = false;

/** True the first time it is called in an app session (the bare /ask route then reopens the last conversation). */
function claimLastThreadRestore(): boolean {
  if (restoredLastThread) return false;
  restoredLastThread = true;
  return true;
}

/** Test hook: forget that the last conversation was restored. */
export function resetAskSessionForTests(): void {
  restoredLastThread = false;
}

const HIGHLIGHT_MS = 2000;

/**
 * Ask Armada (the dashboard's AskArmada page): the conversation list and the open conversation. Phones show the
 * conversation with the list one tap away (a full-screen sheet); tablets show both side by side. Conversations
 * stream captain replies, show confirm cards for state-changing actions and CLI permission cards, and follow the
 * work they start with live cards and milestone messages, all from owner-scoped `ask.*` socket events.
 */
export function AskScreen({ routeThreadId }: { routeThreadId: string | null }) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const { isTablet } = useLayout();
  const router = useRouter();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { pushToast } = useNotifications();
  const headerHeight = useContext(HeaderHeightContext) ?? 0;
  const ask = useAsk();
  const {
    captains, captainNames, draftCaptainId, setDraftCaptainId, quickActions, showThinking, setShowThinking, loadCaptainTools,
    threads, listLoading, listError, search, setSearch, includeArchived, setIncludeArchived, listHasMore, loadMoreThreads,
    reloadThreads, activity, setOpenThreadId, lastThreadId, lastThreadLoaded, updateThread, changeCliPolicy, summarize, deleteThread,
  } = ask;

  const [threadId, setThreadId] = useState<string | null>(routeThreadId);
  const [listOpen, setListOpen] = useState(false);
  const [optionsOpen, setOptionsOpen] = useState(false);
  const [renameOpen, setRenameOpen] = useState(false);
  const [renameValue, setRenameValue] = useState('');
  const [deleteTarget, setDeleteTarget] = useState<AskThread | null>(null);
  const [highlightedWorkId, setHighlightedWorkId] = useState<string | null>(null);
  const [toolsFor, setToolsFor] = useState<{ captainId: string; tools: CaptainToolAccessResult | null } | null>(null);
  const [greeting] = useState(() => randomGreeting());
  const messageListRef = useRef<MessageListHandle>(null);
  const composerRef = useRef<ComposerHandle>(null);

  // A deep link to a conversation (or a new route param) opens it.
  const [seenRoute, setSeenRoute] = useState(routeThreadId);
  if (routeThreadId !== seenRoute) {
    setSeenRoute(routeThreadId);
    if (routeThreadId) setThreadId(routeThreadId);
  }

  // The bare /ask route reopens the last conversation once per app session.
  if (!routeThreadId && lastThreadLoaded && claimLastThreadRestore() && lastThreadId && threadId === null) {
    setThreadId(lastThreadId);
  }

  // The conversation on screen (unread stays zero, approval toasts skip it). Both /ask and /ask/:threadId can be in
  // the stack at once, so only the focused one registers.
  useFocusEffect(useCallback(() => {
    setOpenThreadId(threadId);
    return () => setOpenThreadId(null);
  }, [threadId, setOpenThreadId]));

  const onCreated = useCallback((id: string) => setThreadId(id), []);
  const conversation = useAskConversation({ threadId, onCreated });
  const { conv } = conversation;
  const thread = conv.thread;
  const activeCaptainId = thread ? (thread.captainId ?? '') : draftCaptainId;
  const activeCaptain = captains.find((c) => c.id === activeCaptainId) ?? null;

  // Whether the captain can reach Armada over MCP (it cannot propose actions otherwise).
  useEffect(() => {
    if (!activeCaptainId) return undefined;
    let active = true;
    void loadCaptainTools(activeCaptainId).then((result) => { if (active) setToolsFor({ captainId: activeCaptainId, tools: result }); });
    return () => { active = false; };
  }, [activeCaptainId, loadCaptainTools]);
  const tools = toolsFor && toolsFor.captainId === activeCaptainId ? toolsFor.tools : null;

  const { mcpMissing, ungated, noCaptain } = askCaptainAccess(activeCaptainId, tools);

  async function applyUpdate(target: AskThread, patch: Parameters<typeof updateThread>[1]) {
    const merged = await updateThread(target, patch);
    if (merged && merged.id === conv.threadId) conversation.applyThread(merged);
    return merged;
  }

  const threadActions = {
    onRename: (th: AskThread, title: string) => { void applyUpdate(th, { title }); },
    onTogglePin: (th: AskThread) => { void applyUpdate(th, { pinned: !th.pinned }); },
    onSummarize: (th: AskThread) => { void summarize(th); },
    onToggleArchive: (th: AskThread) => { void applyUpdate(th, { archived: !th.archived }); },
    onDelete: (th: AskThread) => setDeleteTarget(th),
  };

  function selectThread(th: AskThread) {
    setListOpen(false);
    setThreadId(th.id);
  }

  function newConversation() {
    setListOpen(false);
    setThreadId(null);
    setTimeout(() => composerRef.current?.focus(), 0);
  }

  async function confirmDelete() {
    const target = deleteTarget;
    setDeleteTarget(null);
    if (!target) return;
    if (await deleteThread(target) && target.id === conv.threadId) setThreadId(null);
  }

  function commitRename() {
    setRenameOpen(false);
    const next = renameValue.trim();
    if (thread && next && next !== thread.title) void applyUpdate(thread, { title: next });
  }

  function selectWork(work: AskTrackedWork) {
    if (messageListRef.current?.scrollToWork(work.id)) {
      setHighlightedWorkId(work.id);
      setTimeout(() => setHighlightedWorkId((current) => (current === work.id ? null : current)), HIGHLIGHT_MS);
      return;
    }
    router.push(workRoute(work.entityType, work.entityId) as Href);
  }

  const emptyState = thread ? (
    <View style={styles.empty}><AppText muted>{t('Send the first message to begin.')}</AppText></View>
  ) : (
    <View style={styles.empty} testID="ask-empty">
      <AppText variant="title" style={styles.center}>{greeting}</AppText>
      <AppText muted style={styles.center}>
        {activeCaptain
          ? t('Ask {{name}} anything about your fleet, or start work with a quick action.', { name: activeCaptain.name })
          : t('Choose a captain to chat, or start work with a quick action.')}
      </AppText>
      <View style={styles.quickRow}>
        {quickActions.map((action) => (
          <Button
            key={action.name}
            label={action.command || `/${action.name}`}
            variant="secondary"
            accessibilityHint={action.description ? t(action.description) : undefined}
            onPress={() => composerRef.current?.choose(action)}
            testID={`ask-empty-${action.name}`}
            style={styles.quickButton}
          />
        ))}
      </View>
    </View>
  );

  const conversationPane = (
    <KeyboardAvoidingView style={styles.fill} behavior="padding" keyboardVerticalOffset={headerHeight}>
      {ungated ? (
        <Banner tone="warning" title={t('Actions from this captain run without approval cards.')} message={t('This runtime uses its own Armada connection, so anything it does through Armada tools happens immediately.')} testID="ask-ungated-note" />
      ) : null}
      {thread?.autoApprove ? (
        <Banner tone="warning" title={t('Auto-approve is on: actions the captain proposes run immediately. Every action is still recorded below.')} testID="ask-auto-banner" />
      ) : null}
      {thread?.cliPermission?.fallbackReason ? (
        <Banner tone="info" title={fallbackReasonText(t, thread.cliPermission.fallbackReason) ?? ''} testID="ask-cli-fallback-note" />
      ) : null}
      {mcpMissing ? (
        <Pressable accessibilityRole="link" onPress={() => void Linking.openURL(instructionsDocUrl(activeCaptain?.runtime)).catch(() => undefined)}>
          <Banner tone="info" title={t('This captain is not connected to Armada over MCP, so it can answer but cannot propose actions. Quick actions still work.')} message={t('How to connect')} testID="ask-mcp-note" />
        </Pressable>
      ) : null}

      <WorkStrip work={conv.trackedWork} onSelect={selectWork} />

      {conversation.error ? (
        <View style={styles.fill}>
          <ErrorState title={conversation.error} retryLabel={threadId ? t('Retry') : undefined} onRetry={threadId ? conversation.reload : undefined} />
          <Button label={t('Start a new conversation')} variant="ghost" onPress={newConversation} />
        </View>
      ) : conversation.loading && conv.messages.length === 0 ? (
        <LoadingState label={t('Loading conversation...')} />
      ) : (
        <MessageList
          ref={messageListRef}
          messages={conv.messages}
          proposals={conv.proposals}
          trackedWork={conv.trackedWork}
          snapshots={conv.snapshots}
          hasMore={conv.hasMore}
          loadingOlder={conversation.loadingOlder}
          onLoadOlder={() => void conversation.loadOlder()}
          streaming={conv.streaming}
          turnActive={conv.turnActive}
          waitingText={conversation.waitingText}
          captainName={activeCaptain?.name ?? null}
          captainNames={captainNames}
          busyProposalId={conversation.busyProposalId}
          onApprove={(p) => void conversation.decide(p, true)}
          onReject={(p) => void conversation.decide(p, false)}
          highlightedWorkId={highlightedWorkId}
          emptyState={emptyState}
          turnError={conv.turnError}
          cliPermissions={conv.cliPermissions}
          onCliDecided={conversation.cliDecided}
          cliResolution={thread?.cliPermission ?? null}
        />
      )}

      <Composer
        ref={composerRef}
        quickActions={quickActions}
        turnActive={conv.turnActive}
        stopping={conversation.stopping}
        onStop={conversation.stop}
        onSend={(text) => { messageListRef.current?.scrollToBottom(); void conversation.send(text); }}
        onQuickAction={async (action, args) => {
          const ok = await conversation.runQuickAction(action, args);
          if (ok) messageListRef.current?.scrollToBottom();
          return ok;
        }}
        actionBusy={conversation.actionBusy}
        onOpenImport={() => router.push('/vessels/import' as Href)}
        noCaptain={noCaptain}
        showThinking={showThinking}
        onShowThinkingChange={setShowThinking}
      />
    </KeyboardAvoidingView>
  );

  const threadList = (
    <ThreadList
      threads={threads}
      selectedId={threadId}
      activity={activity}
      loading={listLoading}
      error={listError}
      search={search}
      onSearchChange={setSearch}
      includeArchived={includeArchived}
      onIncludeArchivedChange={setIncludeArchived}
      hasMore={listHasMore}
      onLoadMore={loadMoreThreads}
      onRetry={reloadThreads}
      onSelect={selectThread}
      onNew={newConversation}
      {...threadActions}
    />
  );

  const unreadElsewhere = threads.reduce((sum, th) => sum + (th.id === threadId ? 0 : (th.unreadCount ?? 0)), 0);

  return (
    <SafeAreaView edges={['left', 'right']} style={[styles.fill, { backgroundColor: colors.background }]} testID="ask-screen">
      <Stack.Screen
        options={{
          title: thread?.title || t('Ask Armada'),
          headerRight: () => (
            <View style={styles.headerRow}>
              {!isTablet ? (
                <IconButton icon="chatbubbles-outline" label={t('Show conversations')} badge={unreadElsewhere} onPress={() => setListOpen(true)} testID="ask-open-list" />
              ) : null}
              <IconButton icon="options-outline" label={t('More conversation actions')} onPress={() => setOptionsOpen(true)} testID="ask-open-options" />
              {!isTablet ? <HeaderActions /> : null}
            </View>
          ),
        }}
      />
      <SplitView master={threadList} detail={conversationPane} />

      <Modal visible={listOpen && !isTablet} animationType="slide" presentationStyle="pageSheet" onRequestClose={() => setListOpen(false)}>
        {/* A modal is a new native root: swipe actions in the list need their own gesture root. */}
        <GestureHandlerRootView style={styles.fill}>
        <SafeAreaView style={[styles.fill, { backgroundColor: colors.background }]} edges={['top', 'bottom', 'left', 'right']}>
          <View style={[styles.modalHead, { borderBottomColor: colors.border }]}>
            <AppText variant="heading" accessibilityRole="header" style={styles.fill}>{t('Conversations')}</AppText>
            <IconButton icon="close" label={t('Close conversation list')} color="textMuted" onPress={() => setListOpen(false)} testID="ask-close-list" />
          </View>
          {threadList}
        </SafeAreaView>
        </GestureHandlerRootView>
      </Modal>

      <ConversationOptionsSheet
        open={optionsOpen}
        onClose={() => setOptionsOpen(false)}
        thread={thread}
        captains={captains}
        draftCaptainId={draftCaptainId}
        onDraftCaptainChange={setDraftCaptainId}
        onCaptainChange={(captainId) => { if (thread) void applyUpdate(thread, { captainId }); }}
        onAutoApproveChange={(value) => {
          if (!thread) return;
          void applyUpdate(thread, { autoApprove: value }).then((updated) => {
            if (updated && value) pushToast('warning', t('Auto-approve is on. Actions the captain proposes in this conversation now run without asking.'));
          });
        }}
        onCliPolicyChange={(policy) => {
          if (!thread) return;
          void changeCliPolicy(thread, policy).then((merged) => { if (merged) conversation.applyThread(merged); });
        }}
        canBypassCli={!!isAdmin || !!isTenantAdmin}
        busy={conv.turnActive}
        onStartRename={() => { setRenameValue(thread?.title ?? ''); setRenameOpen(true); }}
        {...threadActions}
      />

      <BottomSheet open={renameOpen} title={t('Rename conversation')} onClose={() => setRenameOpen(false)} closeLabel={t('Cancel')} testID="ask-rename-sheet">
        <TextField
          label={t('Conversation title')}
          value={renameValue}
          maxLength={200}
          onChangeText={setRenameValue}
          autoFocus
          returnKeyType="done"
          onSubmitEditing={commitRename}
          testID="ask-rename-title"
        />
        <Button label={t('Rename')} disabled={!renameValue.trim()} onPress={commitRename} testID="ask-rename-confirm" />
      </BottomSheet>

      <ConfirmDialog
        open={!!deleteTarget}
        title={t('Delete conversation')}
        message={t('Delete "{{title}}"? Its messages and action history are removed. Work it started keeps running and stays visible on the normal pages.', { title: deleteTarget?.title || t('New conversation') })}
        confirmLabel={t('Delete')}
        cancelLabel={t('Cancel')}
        danger
        onConfirm={() => void confirmDelete()}
        onCancel={() => setDeleteTarget(null)}
        testID="ask-delete-confirm"
      />
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  center: { textAlign: 'center' },
  empty: { flex: 1, alignItems: 'center', justifyContent: 'center', gap: spacing.md, padding: spacing.lg },
  quickRow: { flexDirection: 'row', flexWrap: 'wrap', justifyContent: 'center', gap: spacing.sm },
  quickButton: { marginBottom: 0 },
  headerRow: { flexDirection: 'row', alignItems: 'center' },
  modalHead: { flexDirection: 'row', alignItems: 'center', paddingLeft: spacing.lg, paddingRight: spacing.xs, borderBottomWidth: StyleSheet.hairlineWidth },
});
