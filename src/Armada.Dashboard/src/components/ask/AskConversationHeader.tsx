import { useEffect, useRef, useState } from 'react';
import type { AskThread, Captain, CliPermissionPolicy } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import ActionMenu, { type ActionMenuItem } from '../shared/ActionMenu';
import CliPermissionPolicySelect from '../cliPermissions/CliPermissionPolicySelect';
import { resolutionSummary } from '../../lib/cliPermissions';

interface AskConversationHeaderProps {
  thread: AskThread | null;
  captains: Captain[];
  /** Captain for a conversation that does not exist yet. */
  draftCaptainId: string;
  onDraftCaptainChange: (id: string) => void;
  onRename: (title: string) => void;
  onCaptainChange: (captainId: string | null) => void;
  onAutoApproveChange: (value: boolean) => void;
  onSummarize: () => void;
  onTogglePin: () => void;
  onToggleArchive: () => void;
  onDelete: () => void;
  onOpenList: () => void;
  busy: boolean;
  /** Set or clear (null) the conversation's CLI tool permission policy. */
  onCliPolicyChange?: (policy: CliPermissionPolicy | null) => void;
  /** The current user may choose Bypass (global or tenant admin). */
  canBypassCli?: boolean;
}

/**
 * Conversation header: editable title, captain picker, the Auto-approve toggle (with a clear warning), Summarize,
 * and an overflow menu. On narrow screens a button opens the conversation list drawer.
 */
export default function AskConversationHeader(props: AskConversationHeaderProps) {
  const { thread, captains, draftCaptainId, onDraftCaptainChange, onRename, onCaptainChange, onAutoApproveChange, onSummarize, onTogglePin, onToggleArchive, onDelete, onOpenList, busy, onCliPolicyChange, canBypassCli } = props;
  const { t } = useLocale();
  const [editing, setEditing] = useState(false);
  const [value, setValue] = useState('');
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => { if (editing) inputRef.current?.select(); }, [editing]);
  useEffect(() => { setEditing(false); }, [thread?.id]);

  function commit() {
    const next = value.trim();
    setEditing(false);
    if (thread && next && next !== thread.title) onRename(next);
  }

  const captainValue = thread ? (thread.captainId ?? '') : draftCaptainId;
  const autoWarning = t('Auto-approve runs state-changing actions the captain proposes immediately, without a confirm card. Only turn it on for conversations you trust; every action is still recorded here.');

  const overflow: ActionMenuItem[] = thread ? [
    { label: 'Rename', onClick: () => { setValue(thread.title || ''); setEditing(true); } },
    thread.pinned ? { label: 'Unpin', onClick: onTogglePin } : { label: 'Pin', onClick: onTogglePin },
    thread.archived ? { label: 'Unarchive', onClick: onToggleArchive } : { label: 'Archive', onClick: onToggleArchive },
    { label: 'Delete', onClick: onDelete, danger: true },
  ] : [];

  return (
    <header className="ask-conv-header">
      <button type="button" className="btn btn-sm ask-open-list" onClick={onOpenList} aria-label={t('Show conversations')}>
        &#9776; <span>{t('Conversations')}</span>
      </button>

      <div className="ask-conv-title-wrap">
        {editing && thread ? (
          <form onSubmit={(e) => { e.preventDefault(); commit(); }} className="ask-conv-title-form">
            <input
              ref={inputRef}
              value={value}
              maxLength={200}
              aria-label={t('Conversation title')}
              onChange={(e) => setValue(e.target.value)}
              onBlur={commit}
              onKeyDown={(e) => { if (e.key === 'Escape') { e.preventDefault(); setEditing(false); } }}
            />
          </form>
        ) : (
          <h2 className="ask-conv-title">
            {thread ? (
              <button
                type="button"
                className="ask-conv-title-btn"
                onClick={() => { setValue(thread.title || ''); setEditing(true); }}
                title={t('Rename conversation')}
              >
                {thread.title || t('New conversation')}
                <span className="ask-conv-title-edit" aria-hidden="true">&#9998;</span>
              </button>
            ) : t('New conversation')}
          </h2>
        )}
      </div>

      <div className="ask-conv-controls">
        <label className="ask-captain-field">
          <span className="text-dim">{t('Captain')}</span>
          <select
            value={captainValue}
            disabled={busy}
            onChange={(e) => (thread ? onCaptainChange(e.target.value || null) : onDraftCaptainChange(e.target.value))}
            aria-label={t('Captain')}
          >
            <option value="">{t('No captain (quick actions only)')}</option>
            {captains.map((c) => <option key={c.id} value={c.id}>{c.name}{c.model || c.runtime ? ` (${c.model || c.runtime})` : ''}</option>)}
          </select>
        </label>

        {thread && (
          <label className={`ask-auto-approve${thread.autoApprove ? ' is-on' : ''}`} title={autoWarning}>
            <input
              type="checkbox"
              role="switch"
              checked={!!thread.autoApprove}
              onChange={(e) => onAutoApproveChange(e.target.checked)}
              aria-describedby="ask-auto-approve-help"
            />
            <span>{t('Auto-approve')}</span>
            {thread.autoApprove && <span className="ask-auto-approve-warn" aria-hidden="true">&#9888;</span>}
            <span id="ask-auto-approve-help" className="sr-only">{autoWarning}</span>
          </label>
        )}

        {thread && onCliPolicyChange && (
          <div className="ask-cli-policy-field" title={t('How the captain CLI handles shell commands, file edits, and fetches that need permission. Inherit uses the captain policy, then the server default.')}>
            <span className="text-dim" id="ask-cli-policy-label">{t('CLI tools')}</span>
            <CliPermissionPolicySelect
              value={thread.cliPermissionPolicy ?? null}
              onChange={onCliPolicyChange}
              allowBypass={!!canBypassCli}
              ariaLabel={t('CLI tools')}
              ariaDescribedBy={thread.cliPermission ? 'ask-cli-policy-effective' : undefined}
            />
            {thread.cliPermission && (
              <span id="ask-cli-policy-effective" className="ask-cli-policy-effective text-dim" data-testid="ask-cli-policy-effective">
                {resolutionSummary(t, { ...thread.cliPermission, fallbackReason: null })}
              </span>
            )}
          </div>
        )}

        {thread && (
          <button type="button" className="btn btn-sm" onClick={onSummarize} title={t('Post a short summary of this conversation')}>
            {t('Summarize')}
          </button>
        )}
        {thread && <ActionMenu id="ask-conversation-menu" items={overflow} triggerLabel={t('More conversation actions')} />}
      </div>
    </header>
  );
}
