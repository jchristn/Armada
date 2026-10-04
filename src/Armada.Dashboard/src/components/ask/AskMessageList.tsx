import { forwardRef, useEffect, useImperativeHandle, useLayoutEffect, useRef, type ReactNode } from 'react';
import type { AskActionProposal, AskMessage, AskTrackedWork, AskWorkSnapshot } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import Markdown from '../shared/Markdown';
import ChatToolChips from '../shared/ChatToolChips';
import AskMessageView from './AskMessageView';
import AskWorkCard from './AskWorkCard';
import { proposalForMessage, workCardHosts, type StreamingTurn } from '../../lib/askConversation';

export interface AskMessageListHandle {
  /** Scroll the live card for a tracked item into view; false when it is not rendered. */
  scrollToWork: (workId: string) => boolean;
  scrollToBottom: () => void;
}

interface AskMessageListProps {
  messages: AskMessage[];
  proposals: Record<string, AskActionProposal>;
  trackedWork: AskTrackedWork[];
  snapshots: Record<string, AskWorkSnapshot>;
  hasMore: boolean;
  loadingOlder: boolean;
  onLoadOlder: () => void;
  streaming: StreamingTurn | null;
  turnActive: boolean;
  /** Rotating "waiting" message while the captain has not produced text yet. */
  waitingText: string;
  captainName: string | null;
  captainNames: Record<string, string>;
  busyProposalId: string | null;
  onApprove: (proposal: AskActionProposal) => void;
  onReject: (proposal: AskActionProposal) => void;
  highlightedWorkId: string | null;
  emptyState: ReactNode;
  turnError: string | null;
}

const LOAD_OLDER_THRESHOLD_PX = 80;

/**
 * The scrolling transcript. Follows new content while the reader is at the bottom, loads older pages when the
 * reader scrolls to the top (keeping their place), and hosts each tracked item's live card on the message
 * that started it.
 */
const AskMessageList = forwardRef<AskMessageListHandle, AskMessageListProps>(function AskMessageList(props, ref) {
  const {
    messages, proposals, trackedWork, snapshots, hasMore, loadingOlder, onLoadOlder, streaming, turnActive, waitingText,
    captainName, captainNames, busyProposalId, onApprove, onReject, highlightedWorkId, emptyState, turnError,
  } = props;
  const { t } = useLocale();
  const scrollRef = useRef<HTMLDivElement>(null);
  const stickRef = useRef(true);
  const prependRef = useRef<{ height: number; top: number; firstSeq: number | null } | null>(null);

  const hosts = workCardHosts(messages);
  // A proposal shown as a confirm card on its ActionProposal message is not repeated on the ActionResult.
  const proposalHosts = new Set(
    messages.filter((m) => m.kind === 'ActionProposal').map((m) => m.proposalId ?? m.proposal?.id).filter((id): id is string => !!id),
  );
  const workById = new Map(trackedWork.map((w) => [w.id, w]));
  const firstSeq = messages.length > 0 ? messages[0].sequence : null;

  useImperativeHandle(ref, () => ({
    scrollToWork: (workId: string) => {
      const container = scrollRef.current;
      const el = document.getElementById(`ask-work-${workId}`);
      if (!container || !el) return false;
      const offset = el.getBoundingClientRect().top - container.getBoundingClientRect().top;
      stickRef.current = false;
      container.scrollTop += offset - 24;
      return true;
    },
    scrollToBottom: () => {
      const container = scrollRef.current;
      if (!container) return;
      stickRef.current = true;
      container.scrollTop = container.scrollHeight;
    },
  }), []);

  // Follow new content (messages, streamed text, card growth) only while the reader is at the bottom.
  useEffect(() => {
    const el = scrollRef.current;
    if (!el) return undefined;
    const pin = () => { if (stickRef.current) el.scrollTop = el.scrollHeight; };
    pin();
    if (typeof MutationObserver === 'undefined') return undefined;
    const observer = new MutationObserver(pin);
    observer.observe(el, { childList: true, subtree: true, characterData: true });
    return () => observer.disconnect();
  }, []);

  // After older messages are prepended, keep the reader on the message they were looking at.
  useLayoutEffect(() => {
    const el = scrollRef.current;
    const saved = prependRef.current;
    if (!el || !saved || saved.firstSeq === firstSeq) return;
    el.scrollTop = saved.top + (el.scrollHeight - saved.height);
    prependRef.current = null;
  }, [firstSeq]);

  function onScroll() {
    const el = scrollRef.current;
    if (!el) return;
    stickRef.current = el.scrollHeight - el.scrollTop - el.clientHeight < 140;
    if (el.scrollTop < LOAD_OLDER_THRESHOLD_PX && hasMore && !loadingOlder) requestOlder();
  }

  function requestOlder() {
    const el = scrollRef.current;
    if (el) prependRef.current = { height: el.scrollHeight, top: el.scrollTop, firstSeq };
    onLoadOlder();
  }

  function cardFor(message: AskMessage): ReactNode {
    const workId = message.trackedWorkId ?? message.trackedWork?.id ?? null;
    if (!workId || hosts[workId] !== message.id) return null;
    const work = workById.get(workId) ?? message.trackedWork ?? null;
    return <AskWorkCard work={work} snapshot={snapshots[workId] ?? work?.snapshot ?? null} highlighted={highlightedWorkId === workId} />;
  }

  const showWaiting = turnActive && (!streaming || (!streaming.text && streaming.tools.length === 0));

  return (
    <div ref={scrollRef} className="ask-transcript" onScroll={onScroll} tabIndex={0} aria-label={t('Conversation messages')}>
      {hasMore && (
        <div className="ask-load-older">
          <button type="button" className="btn btn-sm" onClick={requestOlder} disabled={loadingOlder}>
            {loadingOlder ? t('Loading earlier messages...') : t('Load earlier messages')}
          </button>
        </div>
      )}

      {messages.length === 0 && !streaming && !turnActive ? (
        <div className="ask-transcript-empty">{emptyState}</div>
      ) : (
        messages.map((message) => {
          const workId = message.trackedWorkId ?? message.trackedWork?.id ?? null;
          const card = cardFor(message);
          const resolved = proposalForMessage({ proposals }, message);
          const proposal = resolved && message.kind === 'ActionResult' && proposalHosts.has(resolved.id) ? null : resolved;
          return (
            <AskMessageView
              key={message.id}
              message={message}
              proposal={proposal}
              captainName={(message.captainId && captainNames[message.captainId]) || captainName}
              proposalBusy={!!proposal && busyProposalId === proposal.id}
              onApprove={onApprove}
              onReject={onReject}
              workCard={card}
              onShowWork={workId && !card ? () => {
                const el = document.getElementById(`ask-work-${workId}`);
                if (el && scrollRef.current) {
                  stickRef.current = false;
                  scrollRef.current.scrollTop += el.getBoundingClientRect().top - scrollRef.current.getBoundingClientRect().top - 24;
                  el.focus?.();
                }
              } : undefined}
            />
          );
        })
      )}

      {streaming && (streaming.text || streaming.tools.length > 0 || streaming.thinking) && (
        <article className="ask-msg ask-msg-assistant is-streaming" aria-busy={!streaming.finished}>
          <ChatToolChips
            tools={streaming.tools}
            runningLabel={t('running…')}
            argumentsLabel={t('Arguments')}
            resultLabel={t('Result')}
            noDetailsLabel={t('No details available.')}
          />
          <div className="ask-bubble ask-bubble-assistant">
            <div className="ask-bubble-head text-dim"><span>{captainName || t('Captain')}</span></div>
            {streaming.thinking && (
              <details className="chat-thinking" open={!streaming.finished}>
                <summary className="text-dim">{streaming.finished ? t('Thinking') : t('Thinking…')}</summary>
                <div className="ask-thinking-text">{streaming.thinking}</div>
              </details>
            )}
            {streaming.text && <Markdown>{streaming.text}</Markdown>}
          </div>
        </article>
      )}

      {showWaiting && <div key={waitingText} className="text-dim ask-thinking ask-waiting" role="status">{waitingText || t('Thinking...')}</div>}
      {turnError && !turnActive && (
        <div className="ask-msg ask-msg-error" role="alert">
          <div className="ask-msg-error-text">{t('The captain turn failed: {{reason}}', { reason: turnError })}</div>
        </div>
      )}
    </div>
  );
});

export default AskMessageList;
