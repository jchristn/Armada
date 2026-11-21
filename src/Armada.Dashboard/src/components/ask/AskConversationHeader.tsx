import { useEffect, useRef, useState } from 'react';
import type { AskThread, Captain, CliPermissionPolicy, Harbor } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import ActionMenu, { type ActionMenuItem } from '../shared/ActionMenu';
import CliPermissionPolicySelect from '../cliPermissions/CliPermissionPolicySelect';
import { resolutionSummary } from '../../lib/cliPermissions';

/** The captain picker's id (the /captain command focuses it when a name is ambiguous). */
export const CAPTAIN_SELECT_ID = 'ask-captain-select';

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
  /** Harbors the conversation can run on (the user's machines). */
  harbors?: Harbor[];
  /** Run the conversation on a Harbor ('' runs it on the Armada server). */
  onRunOnChange?: (harborId: string) => void;
  /** Set ('' clears) the folder the captain works in on the machine that runs the turn. */
  onWorkingDirectoryChange?: (folder: string) => void;
}

/**
 * Conversation header: editable title, captain picker, the Auto-approve toggle (with a clear warning), Summarize,
 * and an overflow menu. On narrow screens a button opens the conversation list drawer.
 */
export default function AskConversationHeader(props: AskConversationHeaderProps) {
  const { thread, captains, draftCaptainId, onDraftCaptainChange, onRename, onCaptainChange, onAutoApproveChange, onSummarize, onTogglePin, onToggleArchive, onDelete, onOpenList, busy, onCliPolicyChange, canBypassCli, harbors, onRunOnChange, onWorkingDirectoryChange } = props;
  const [folder, setFolder] = useState(thread?.workingDirectory ?? '');
  useEffect(() => { setFolder(thread?.workingDirectory ?? ''); }, [thread?.id, thread?.workingDirectory]);
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
      <button type="button" className="btn btn-sm ask-open-list" onClick={onOpenList} aria-label={t('Show conversations')} title={t('Open the list of your conversations to switch, search, or start a new one.')}>
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
              title={t('Type a new title and press Enter to save, or Escape to cancel. Up to 200 characters.')}
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
                title={t('Click to rename this conversation. The title shows in the conversation list.')}
              >
                {thread.title || t('New conversation')}
                <span className="ask-conv-title-edit" aria-hidden="true">&#9998;</span>
              </button>
            ) : t('New conversation')}
          </h2>
        )}
      </div>

      <div className="ask-conv-controls">
        <label className="ask-captain-field" title={t('The captain (AI agent) that answers in this conversation. Changing it applies to the next message; no captain means only quick actions work. Locked while a reply is running.')}>
          <span className="text-dim">{t('Captain')}</span>
          <select
            id={CAPTAIN_SELECT_ID}
            value={captainValue}
            disabled={busy}
            title={t('The captain (AI agent) that answers in this conversation. Changing it applies to the next message; no captain means only quick actions work. Locked while a reply is running.')}
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
              title={autoWarning}
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
          <div className="ask-cli-policy-field" title={t('How the captain CLI handles shell commands, file edits, and fetches that need permission in this conversation. Inherit uses the captain policy, then the server default.')}>
            <span className="text-dim" id="ask-cli-policy-label">{t('CLI tools')}</span>
            <CliPermissionPolicySelect
              value={thread.cliPermissionPolicy ?? null}
              onChange={onCliPolicyChange}
              allowBypass={!!canBypassCli}
              ariaLabel={t('CLI tools')}
              title={t('How the captain CLI handles shell commands, file edits, and fetches that need permission in this conversation. Inherit uses the captain policy, then the server default.')}
              ariaDescribedBy={thread.cliPermission ? 'ask-cli-policy-effective' : undefined}
            />
            {thread.cliPermission && (
              <span id="ask-cli-policy-effective" className="ask-cli-policy-effective text-dim" data-testid="ask-cli-policy-effective" title={t('The policy actually in force for this conversation and which setting it came from.')}>
                {resolutionSummary(t, { ...thread.cliPermission, fallbackReason: null })}
              </span>
            )}
          </div>
        )}

        {thread && onRunOnChange && (
          <label className="ask-run-on-field" title={t('Where the captain runs: a Harbor runs it on that machine, so it sees that machine\'s files; the Armada server runs it on the server.')}>
            <span className="text-dim">{t('Run on')}</span>
            <select value={thread.harborId ?? ''} disabled={busy} onChange={(e) => onRunOnChange(e.target.value)} aria-label={t('Run on')}>
              <option value="">{t('Armada server')}</option>
              {(harbors ?? []).map((h) => <option key={h.id} value={h.id}>{t('Harbor')}: {h.name}{String(h.connectionStatus) === 'Connected' ? '' : ` (${String(h.connectionStatus ?? 'offline').toLowerCase()})`}</option>)}
            </select>
          </label>
        )}

        {thread && onWorkingDirectoryChange && (
          <label className="ask-folder-field" title={t('The folder the captain works in on the machine that runs the turn (~ is that machine\'s home). Empty uses a temporary folder.')}>
            <span className="text-dim">{t('Folder')}</span>
            <input
              value={folder}
              placeholder={t('temporary folder')}
              disabled={busy}
              onChange={(e) => setFolder(e.target.value)}
              onBlur={() => { if (folder.trim() !== (thread.workingDirectory ?? '')) onWorkingDirectoryChange(folder.trim()); }}
              onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); (e.target as HTMLInputElement).blur(); } }}
              aria-label={t('Folder')}
            />
          </label>
        )}

        {thread && (
          <button type="button" className="btn btn-sm" onClick={onSummarize} title={t('Ask the captain to post a short summary of this conversation. The summary also stands in for older messages the captain no longer sees each turn.')}>
            {t('Summarize')}
          </button>
        )}
        {thread && <ActionMenu id="ask-conversation-menu" items={overflow} triggerLabel={t('More conversation actions')} />}
      </div>
    </header>
  );
}
