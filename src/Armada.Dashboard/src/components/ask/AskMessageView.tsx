import type { ReactNode } from 'react';
import type { AskActionProposal, AskMessage, CliPermissionRequest } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import Markdown from '../shared/Markdown';
import ChatToolChips from '../shared/ChatToolChips';
import { formatTurnDuration as formatDuration, toolCallsToEvents } from '../../lib/askFormat';
import { askTurnStatistics } from '../../lib/chatMetrics';
import ChatMetricsInfo from '../shared/ChatMetricsInfo';
import CopyButton from '../shared/CopyButton';
import AskConfirmCard from './AskConfirmCard';
import CliPermissionCard from '../cliPermissions/CliPermissionCard';

export { toolCallsToEvents } from '../../lib/askFormat';

interface AskMessageViewProps {
  message: AskMessage;
  proposal: AskActionProposal | null;
  captainName?: string | null;
  proposalBusy: boolean;
  onApprove: (proposal: AskActionProposal) => void;
  onReject: (proposal: AskActionProposal) => void;
  /** The live work card when this message hosts one. */
  workCard?: ReactNode;
  /** For milestones whose card lives on another message: scroll to it. */
  onShowWork?: () => void;
  /** CliPermission cards: the latest copy of the linked request. */
  cliRequest?: CliPermissionRequest | null;
  onCliDecided?: (request: CliPermissionRequest) => void;
  /** Explanation under tool calls the CLI refused for lack of permission (already localized). */
  permissionDeniedNote?: string;
}

/**
 * One persisted message, rendered by kind: text (Markdown for the captain, plain for the user), tool-call chips,
 * confirm cards, action results, milestone updates, work reports (a captain reply with a Report tag), summaries, and
 * errors.
 */
export default function AskMessageView({ message, proposal, captainName, proposalBusy, onApprove, onReject, workCard, onShowWork, cliRequest, onCliDecided, permissionDeniedNote }: AskMessageViewProps) {
  const { t, formatRelativeTime, formatDateTime } = useLocale();
  const kind = String(message.kind || 'Text');
  const role = String(message.role || 'Assistant');
  const text = message.contentText ?? '';
  // Icon-only copy of a response's raw text (the shared button flashes a green check and falls back off https).
  const copy = text.trim() ? (
    <CopyButton text={text} className="copy-btn ask-msg-copy" title={t('Copy this response to the clipboard.')} />
  ) : null;
  const when = message.createdUtc ? (
    <time className="ask-msg-time" dateTime={message.createdUtc} title={formatDateTime(message.createdUtc)}>
      {formatRelativeTime(message.createdUtc)}
    </time>
  ) : null;

  // The captain's reply is reserved (empty) when a turn starts so later cards sort after it; the live
  // streaming bubble shows the text until the turn completes and fills it in.
  if (kind === 'Text' && role === 'Assistant' && !text.trim() && !(message.toolCalls && message.toolCalls.length > 0) && !message.toolCallCount) {
    return null;
  }

  if (kind === 'ActionProposal') {
    return (
      <article className="ask-msg ask-msg-proposal" data-sequence={message.sequence}>
        {text && !proposal?.summaryText && <p className="ask-msg-caption">{text}</p>}
        {proposal
          ? <AskConfirmCard proposal={proposal} onApprove={onApprove} onReject={onReject} busy={proposalBusy} />
          : <div className="ask-confirm-card is-unknown"><Markdown>{text || t('A proposed action is loading...')}</Markdown></div>}
        {workCard}
        <div className="ask-msg-meta">{when}</div>
      </article>
    );
  }

  if (kind === 'CliPermission') {
    return (
      <article className="ask-msg ask-msg-proposal ask-msg-cli-permission" data-sequence={message.sequence}>
        {cliRequest
          ? <CliPermissionCard request={cliRequest} onDecided={onCliDecided} collapsible />
          : <div className="ask-confirm-card is-unknown"><p>{text || t('A CLI permission request is loading...')}</p></div>}
        <div className="ask-msg-meta">{when}</div>
      </article>
    );
  }

  if (kind === 'ActionResult') {
    return (
      <article className="ask-msg ask-msg-result" data-sequence={message.sequence}>
        <div className="ask-msg-result-head">
          <span className="ask-msg-result-icon" aria-hidden="true">&#9889;</span>
          <span className="ask-msg-label">{t('Action result')}</span>
          {when}
          {copy}
        </div>
        {text && <Markdown>{text}</Markdown>}
        {proposal && <AskConfirmCard proposal={proposal} onApprove={onApprove} onReject={onReject} busy={proposalBusy} compact />}
        {workCard}
      </article>
    );
  }

  if (kind === 'WorkUpdate') {
    return (
      <article className="ask-msg ask-msg-milestone" data-sequence={message.sequence}>
        <span className="ask-milestone-marker" aria-hidden="true" />
        <div className="ask-milestone-body">
          <div className="ask-milestone-head">
            <span className="ask-msg-label">{t('Progress update')}</span>
            {when}
            {onShowWork && (
              <button type="button" className="ask-link-btn ask-milestone-link" onClick={onShowWork} title={t('Scroll to the live card for this work, which shows its current status.')}>
                {t('Show live card')}
              </button>
            )}
            {/* Last in the header so keyboard order matches its pinned top-right position (see App.css). */}
            {copy}
          </div>
          <Markdown>{text}</Markdown>
          {workCard}
        </div>
      </article>
    );
  }

  if (kind === 'Summary') {
    return (
      <article className="ask-msg ask-msg-summary" data-sequence={message.sequence}>
        <div className="ask-msg-summary-head">
          <span className="ask-msg-label">{t('Conversation summary')}</span>
          {when}
          {copy}
        </div>
        <Markdown>{text}</Markdown>
      </article>
    );
  }

  // A turn that failed or was stopped keeps the tool calls it made before it ended; show them above the error, as the
  // live reply did.
  const toolChips = (
    <ChatToolChips
      tools={toolCallsToEvents(message.toolCalls)}
      runningLabel={t('running…')}
      argumentsLabel={t('Arguments')}
      resultLabel={t('Result')}
      noDetailsLabel={t('No details available.')}
      permissionDeniedNote={permissionDeniedNote}
      permissionDeniedLabel={t('Refused for lack of permission')}
    />
  );

  if (kind === 'Error') {
    return (
      <article className="ask-msg ask-msg-error" role="note" data-sequence={message.sequence}>
        {toolChips}
        <div className="ask-msg-error-head">
          <span className="ask-msg-label">{t('Error')}</span>
          {when}
        </div>
        <div className="ask-msg-error-text">{text || t('Something went wrong.')}</div>
      </article>
    );
  }

  if (role === 'User') {
    return (
      <article className="ask-msg ask-msg-user" data-sequence={message.sequence}>
        <div className="ask-bubble ask-bubble-user">
          <div className="ask-bubble-text">{text}</div>
        </div>
        <div className="ask-msg-meta">{when}</div>
        {workCard}
      </article>
    );
  }

  if (role === 'System') {
    return (
      <article className="ask-msg ask-msg-system" data-sequence={message.sequence}>
        <Markdown>{text}</Markdown>
        {workCard}
      </article>
    );
  }

  // Assistant text, the captain's automatic WorkReport (same bubble plus a Report tag), and any unknown kind: tool
  // chips, optional thinking, Markdown body.
  const isReport = kind === 'WorkReport';
  // The reply's turn statistics behind an (i), as on a Planning reply: the full set when the Admiral recorded the
  // turn's telemetry, otherwise (older replies) the turn's total and its tool calls (lib/chatMetrics).
  const stats = askTurnStatistics(t, message);
  return (
    <article className={isReport ? 'ask-msg ask-msg-assistant ask-msg-report' : 'ask-msg ask-msg-assistant'} data-sequence={message.sequence}>
      {toolChips}
      <div className="ask-bubble ask-bubble-assistant">
        <div className="ask-bubble-head text-dim">
          <span>{captainName || t('Captain')}</span>
          {isReport && <span className="ask-report-tag" title={t('Posted automatically by the captain when the work finished, summarizing the outcome. Turn this off in Server Settings > Ask Armada.')}>{t('Report')}</span>}
          {message.durationMs != null && <span title={t('How long the captain took to produce this reply.')}>{formatDuration(message.durationMs)}</span>}
          {stats.length > 0 && <ChatMetricsInfo rows={stats} />}
          {when}
          {copy}
        </div>
        {message.thinkingText && message.thinkingText.trim() && (
          <details className="chat-thinking">
            <summary className="text-dim" title={t('Show the reasoning the captain wrote before this reply.')}>{t('Thinking')}</summary>
            <div className="ask-thinking-text">{message.thinkingText}</div>
          </details>
        )}
        {proposal && <AskConfirmCard proposal={proposal} onApprove={onApprove} onReject={onReject} busy={proposalBusy} />}
        <Markdown>{text}</Markdown>
      </div>
      {workCard}
    </article>
  );
}
