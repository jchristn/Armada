import type { RefObject } from 'react';
import type { PlanningSessionMessage } from '../../types/models';
import StatusBadge from '../shared/StatusBadge';
import CaptainChatPanel, { type ChatTurn } from '../shared/CaptainChatPanel';
import { type ToolEvent } from '../shared/ChatToolChips';

interface PlanningTranscriptCardProps {
  t: (value: string, vars?: Record<string, string | number>) => string;
  transcriptRef: RefObject<HTMLDivElement | null>;
  title: string;
  captainName: string;
  captainRuntime: string;
  vesselName: string;
  branchName: string | null;
  pipelineName: string;
  playbookCount: number;
  currentStatus?: string;
  failureReason?: string | null;
  updatedUtc: string;
  messages: PlanningSessionMessage[];
  /** Transcript length on the server (every page); defaults to the loaded count. */
  messageTotal?: number;
  /** Loads the next older transcript page; offered while fewer than messageTotal are loaded. */
  onLoadOlder?: () => void;
  loadingOlder?: boolean;
  messageTools?: Record<string, ToolEvent[]>;
  messageThinking?: Record<string, string>;
  thinkingMessage?: string;
  streamingEnabled?: boolean;
  onStreamingChange?: (value: boolean) => void;
  showThinking?: boolean;
  onShowThinkingChange?: (value: boolean) => void;
  composer: string;
  sending: boolean;
  canSend: boolean;
  canEndSession: boolean;
  endingSession: boolean;
  deleting: boolean;
  formatDateTime: (value: string) => string;
  formatRelativeTime: (value: string) => string;
  /** Sends a specific assistant reply to the main Dispatch page (its full text becomes the dispatch prompt). */
  onOpenMessageInDispatch: (messageId: string) => void;
  onComposerChange: (value: string) => void;
  onSend: () => void;
  onStopTurn: () => void;
  onEndSession: () => void;
  onDelete: () => void;
}

export default function PlanningTranscriptCard(props: PlanningTranscriptCardProps) {
  const {
    t,
    transcriptRef,
    title,
    captainName,
    captainRuntime,
    vesselName,
    branchName,
    pipelineName,
    playbookCount,
    currentStatus,
    failureReason,
    updatedUtc,
    messages,
    messageTotal,
    onLoadOlder,
    loadingOlder,
    messageTools,
    messageThinking,
    thinkingMessage,
    streamingEnabled = true,
    onStreamingChange,
    showThinking = false,
    onShowThinkingChange,
    composer,
    sending,
    canSend,
    canEndSession,
    endingSession,
    deleting,
    formatDateTime,
    formatRelativeTime,
    onOpenMessageInDispatch,
    onComposerChange,
    onSend,
    onStopTurn,
    onEndSession,
    onDelete,
  } = props;

  // Map the persisted planning transcript onto the shared chat-turn shape so the Planning chat renders
  // through the exact same panel as Ask Armada.
  const busy = currentStatus === 'Responding' || sending;
  const lastAssistantId = [...messages].reverse().find((m) => m.role.toLowerCase() === 'assistant')?.id;

  const turns: ChatTurn[] = messages.map((message) => {
    const role = message.role.toLowerCase();
    const kind: ChatTurn['role'] = role === 'user' ? 'user' : role === 'assistant' ? 'assistant' : 'system';
    return {
      id: message.id,
      role: kind,
      text: message.content,
      metrics: message.metrics ?? undefined,
      tools: kind === 'assistant' ? messageTools?.[message.id] : undefined,
      thinking: kind === 'assistant' ? messageThinking?.[message.id] : undefined,
      streaming: kind === 'assistant' && busy && message.id === lastAssistantId,
    };
  });

  return (
    <div className="card planning-current-session">
      <div className="planning-current-session-head">
        <div>
          <div className="card-label">{t('Current Session')}</div>
          <h3 style={{ marginTop: 0, marginBottom: '0.35rem' }}>{title}</h3>
          <p className="text-muted">
            {t('Chat with {{captain}} against {{vessel}}, keep the transcript intact, and promote the right reply into dispatch.', {
              captain: captainName,
              vessel: vesselName,
            })}
          </p>
        </div>
        <div className="planning-current-session-actions">
          {currentStatus && <span title={t('Active is ready for your next message, Responding is the captain replying, Stopping and Stopped mean it has ended, Failed means setup or a turn failed.')}><StatusBadge status={currentStatus} /></span>}
          <label className="ask-stream-toggle" title={t('Show the captain\'s reply word by word as it is written. Turn off to get each reply only when it is finished. Some runtimes, such as Codex, cannot stream.')}>
            <input type="checkbox" checked={streamingEnabled} onChange={(e) => onStreamingChange?.(e.target.checked)} disabled={busy} />
            {t('Stream responses')}
          </label>
          <label className="ask-stream-toggle" title={t('Show the model\'s reasoning above each answer. Mux streams it natively; other runtimes are asked to include it, which can make replies longer.')}>
            <input type="checkbox" checked={showThinking} onChange={(e) => onShowThinkingChange?.(e.target.checked)} disabled={busy} />
            {t('Show thinking')}
          </label>
          <button type="button" className="btn btn-sm" disabled={!canEndSession} onClick={onEndSession} title={t('Stop the session, release the captain and reclaim its dock. The transcript is kept.')}>
            {endingSession || currentStatus === 'Stopping' ? t('Ending...') : t('End Session')}
          </button>
        </div>
      </div>

      <div className="planning-current-session-summary">
        <div className="planning-current-session-summary-item">
          <span title={t('Captain reserved for this session.')}>{t('Captain')}</span>
          <strong>{captainName}</strong>
        </div>
        <div className="planning-current-session-summary-item">
          <span title={t('Agent software the captain runs, such as Claude Code, Codex or Mux.')}>{t('Runtime')}</span>
          <strong>{captainRuntime || '-'}</strong>
        </div>
        <div className="planning-current-session-summary-item">
          <span title={t('Repository the session plans against.')}>{t('Vessel')}</span>
          <strong>{vesselName}</strong>
        </div>
        <div className="planning-current-session-summary-item">
          <span title={t('Git branch of the session\'s dock. Changes the captain makes while planning are made here.')}>{t('Branch')}</span>
          <strong className="mono">{branchName || '-'}</strong>
        </div>
        <div className="planning-current-session-summary-item">
          <span title={t('Pipeline used when dispatching from this session. - means it is inherited from the vessel or fleet at dispatch time.')}>{t('Pipeline')}</span>
          <strong>{pipelineName}</strong>
        </div>
        <div className="planning-current-session-summary-item">
          <span title={t('Number of playbooks (reusable instruction files) given to the captain for this session.')}>{t('Playbooks')}</span>
          <strong>{playbookCount}</strong>
        </div>
        <div className="planning-current-session-summary-item">
          <span title={t('When the session last changed, such as a new message.')}>{t('Updated')}</span>
          <strong title={formatDateTime(updatedUtc)}>{formatRelativeTime(updatedUtc)}</strong>
        </div>
        <div className="planning-current-session-summary-item">
          <span title={t('Number of messages in the transcript, yours and the captain\'s.')}>{t('Messages')}</span>
          <strong>{messageTotal ?? messages.length}</strong>
        </div>
      </div>

      {failureReason && (
        <div className="alert alert-error">
          {failureReason}
        </div>
      )}

      {onLoadOlder && (messageTotal ?? 0) > messages.length && (
        <div className="planning-chat-older">
          <button type="button" className="btn btn-sm" onClick={onLoadOlder} disabled={loadingOlder} title={t('Load the next older page of the transcript.')}>
            {loadingOlder ? t('Loading earlier messages...') : t('Load earlier messages ({{shown}} of {{total}})', { shown: messages.length, total: messageTotal ?? messages.length })}
          </button>
        </div>
      )}
      <div className="planning-chat-shell">
        <CaptainChatPanel
          t={t}
          turns={turns}
          windowRef={transcriptRef}
          notice={captainRuntime === 'Codex' ? t('Codex responses cannot be streamed and will arrive upon completion.') : undefined}
          assistantName={captainName}
          emptyState={<p>{t('No transcript yet. Send the first planning message below.')}</p>}
          input={composer}
          onInputChange={onComposerChange}
          onSend={onSend}
          onStop={onStopTurn}
          busy={busy}
          canSend={canSend}
          canStop={currentStatus === 'Responding'}
          thinking={thinkingMessage}
          inputPlaceholder={t('Describe the problem, ask for a plan, or negotiate the next steps with the captain.')}
          inputDisabled={currentStatus !== 'Active'}
          onClear={onDelete}
          clearDisabled={busy || deleting}
          renderTurnFooter={(turn) =>
            turn.role === 'assistant' && turn.id && turn.text.trim().length > 0 ? (
              <div className="planning-chat-message-actions">
                <button
                  type="button"
                  className="btn btn-sm btn-primary"
                  onClick={() => onOpenMessageInDispatch(turn.id!)}
                  title={t('Ends this session (releasing the captain), then opens the Dispatch page with this reply as the description so you can review and dispatch it.')}
                >
                  {t('Open in Dispatch')}
                </button>
              </div>
            ) : null
          }
        />
      </div>
    </div>
  );
}
