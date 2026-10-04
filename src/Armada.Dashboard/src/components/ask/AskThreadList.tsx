import { useEffect, useRef, useState, type KeyboardEvent } from 'react';
import type { AskThread } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import ActionMenu, { type ActionMenuItem } from '../shared/ActionMenu';
import { isThreadReplying, isThreadWorking, type ThreadActivityMap } from '../../lib/askThreads';

export interface AskThreadListProps {
  threads: AskThread[];
  selectedId: string | null;
  activity: ThreadActivityMap;
  loading: boolean;
  error: string | null;
  search: string;
  onSearchChange: (value: string) => void;
  includeArchived: boolean;
  onIncludeArchivedChange: (value: boolean) => void;
  hasMore: boolean;
  onLoadMore: () => void;
  onRetry: () => void;
  onSelect: (thread: AskThread) => void;
  onNew: () => void;
  onRename: (thread: AskThread, title: string) => void;
  onTogglePin: (thread: AskThread) => void;
  onSummarize: (thread: AskThread) => void;
  onToggleArchive: (thread: AskThread) => void;
  onDelete: (thread: AskThread) => void;
  /** Shown in the drawer on narrow screens. */
  onClose?: () => void;
}

/**
 * The conversation list: search (server-side), New conversation, pinned threads first, unread badges, a live
 * "working" dot for threads whose tracked work is still running, and a per-row menu. Arrow keys move between
 * rows; Enter opens one.
 */
export default function AskThreadList(props: AskThreadListProps) {
  const {
    threads, selectedId, activity, loading, error, search, onSearchChange, includeArchived, onIncludeArchivedChange,
    hasMore, onLoadMore, onRetry, onSelect, onNew, onRename, onTogglePin, onSummarize, onToggleArchive, onDelete, onClose,
  } = props;
  const { t, formatRelativeTime } = useLocale();
  const [renamingId, setRenamingId] = useState<string | null>(null);
  const [renameValue, setRenameValue] = useState('');
  const listRef = useRef<HTMLUListElement>(null);
  const renameRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (renamingId) renameRef.current?.select();
  }, [renamingId]);

  function startRename(thread: AskThread) {
    setRenamingId(thread.id);
    setRenameValue(thread.title || '');
  }

  function commitRename(thread: AskThread) {
    const value = renameValue.trim();
    setRenamingId(null);
    if (value && value !== thread.title) onRename(thread, value);
  }

  function onListKeyDown(event: KeyboardEvent<HTMLUListElement>) {
    if (!['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) return;
    const rows = Array.from(listRef.current?.querySelectorAll<HTMLButtonElement>('[data-thread-row]') ?? []);
    if (rows.length === 0) return;
    const index = rows.findIndex((row) => row === document.activeElement);
    let next = index;
    if (event.key === 'ArrowDown') next = index < 0 ? 0 : Math.min(rows.length - 1, index + 1);
    else if (event.key === 'ArrowUp') next = index < 0 ? rows.length - 1 : Math.max(0, index - 1);
    else if (event.key === 'Home') next = 0;
    else next = rows.length - 1;
    event.preventDefault();
    rows[next].focus();
  }

  function menuItems(thread: AskThread): ActionMenuItem[] {
    return [
      { label: 'Rename', onClick: () => startRename(thread) },
      thread.pinned
        ? { label: 'Unpin', onClick: () => onTogglePin(thread) }
        : { label: 'Pin', onClick: () => onTogglePin(thread) },
      { label: 'Summarize', onClick: () => onSummarize(thread) },
      thread.archived
        ? { label: 'Unarchive', onClick: () => onToggleArchive(thread) }
        : { label: 'Archive', onClick: () => onToggleArchive(thread) },
      { label: 'Delete', onClick: () => onDelete(thread), danger: true },
    ];
  }

  return (
    <nav className="ask-threads" aria-label={t('Conversations')}>
      <div className="ask-threads-top">
        <div className="ask-threads-title-row">
          <h3 className="ask-threads-title">{t('Conversations')}</h3>
          {onClose && (
            <button type="button" className="ask-icon-btn ask-threads-close" onClick={onClose} aria-label={t('Close conversation list')} title={t('Close conversation list')}>
              &times;
            </button>
          )}
        </div>
        <button type="button" className="btn btn-primary btn-sm ask-new-btn" onClick={onNew}>
          + {t('New conversation')}
        </button>
        <input
          type="search"
          className="ask-threads-search"
          value={search}
          onChange={(e) => onSearchChange(e.target.value)}
          placeholder={t('Search conversations')}
          aria-label={t('Search conversations')}
        />
        <label className="ask-threads-archived">
          <input type="checkbox" checked={includeArchived} onChange={(e) => onIncludeArchivedChange(e.target.checked)} />
          {t('Show archived')}
        </label>
      </div>

      {error && (
        <div className="alert alert-error ask-threads-error" role="alert">
          <span>{error}</span>
          <button type="button" className="btn btn-sm" onClick={onRetry}>{t('Retry')}</button>
        </div>
      )}

      {!error && !loading && threads.length === 0 && (
        <p className="ask-threads-empty text-dim">
          {search.trim() ? t('No conversations match your search.') : t('No conversations yet. Start one to ask a question or kick off work.')}
        </p>
      )}

      <ul className="ask-thread-items" ref={listRef} onKeyDown={onListKeyDown} aria-busy={loading || undefined}>
        {threads.map((thread) => {
          const working = isThreadWorking(thread, activity);
          const replying = isThreadReplying(thread, activity);
          const unread = thread.id === selectedId ? 0 : (thread.unreadCount ?? 0);
          const selected = thread.id === selectedId;
          const title = thread.title || t('New conversation');
          return (
            <li key={thread.id} className={`ask-thread-item${selected ? ' is-selected' : ''}${unread > 0 ? ' is-unread' : ''}`}>
              {renamingId === thread.id ? (
                <form className="ask-thread-rename" onSubmit={(e) => { e.preventDefault(); commitRename(thread); }}>
                  <input
                    ref={renameRef}
                    value={renameValue}
                    maxLength={200}
                    aria-label={t('Conversation title')}
                    onChange={(e) => setRenameValue(e.target.value)}
                    onBlur={() => commitRename(thread)}
                    onKeyDown={(e) => { if (e.key === 'Escape') { e.preventDefault(); setRenamingId(null); } }}
                  />
                </form>
              ) : (
                <button
                  type="button"
                  data-thread-row
                  className="ask-thread-row"
                  aria-current={selected ? 'page' : undefined}
                  onClick={() => onSelect(thread)}
                  title={title}
                >
                  <span className="ask-thread-row-top">
                    {thread.pinned && <span className="ask-thread-pin" aria-label={t('Pinned')} title={t('Pinned')}>&#128204;</span>}
                    <span className="ask-thread-name">{title}</span>
                    {unread > 0 && (
                      <span className="ask-unread-badge" aria-label={t('{{count}} unread', { count: unread })}>
                        {unread > 99 ? '99+' : unread}
                      </span>
                    )}
                  </span>
                  <span className="ask-thread-row-bottom text-dim">
                    {working && (
                      <span className="ask-thread-working">
                        <span className="ask-live-dot" aria-hidden="true" />
                        {t('Working')}
                      </span>
                    )}
                    {!working && replying && <span className="ask-thread-working">{t('Replying...')}</span>}
                    {thread.archived && <span className="ask-thread-archived-tag">{t('Archived')}</span>}
                    <span className="ask-thread-time">
                      {thread.lastMessageUtc ? formatRelativeTime(thread.lastMessageUtc) : ''}
                    </span>
                  </span>
                </button>
              )}
              <ActionMenu id={`ask-thread-${thread.id}`} items={menuItems(thread)} triggerLabel={t('Actions for {{title}}', { title })} />
            </li>
          );
        })}
      </ul>

      {loading && <p className="ask-threads-loading text-dim" role="status">{t('Loading conversations...')}</p>}
      {hasMore && !loading && (
        <button type="button" className="btn btn-sm ask-threads-more" onClick={onLoadMore}>{t('Load more')}</button>
      )}
    </nav>
  );
}
