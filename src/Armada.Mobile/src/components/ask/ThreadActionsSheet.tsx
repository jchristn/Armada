import { useState } from 'react';
import type { AskThread } from '@dashboard/types/models';
import { useLocale } from '../../i18n/LocaleContext';
import { BottomSheet } from '../ui/BottomSheet';
import { Button } from '../ui/Button';
import { ListRow } from '../ui/ListRow';
import { TextField } from '../ui/TextField';

export interface ThreadActions {
  onRename: (thread: AskThread, title: string) => void;
  onTogglePin: (thread: AskThread) => void;
  onSummarize: (thread: AskThread) => void;
  onToggleArchive: (thread: AskThread) => void;
  onDelete: (thread: AskThread) => void;
}

/** A conversation's actions as a sheet (the dashboard's row and header menus): Rename, Pin, Summarize, Archive, Delete. */
export function ThreadActionsSheet({ thread, onClose, actions }: { thread: AskThread | null; onClose: () => void; actions: ThreadActions }) {
  const { t } = useLocale();
  // Renaming applies to one thread; opening the sheet for another thread starts on the action list again.
  const [renamingId, setRenamingId] = useState<string | null>(null);
  const [title, setTitle] = useState('');
  const renaming = !!thread && renamingId === thread.id;
  const setRenaming = (value: boolean) => {
    setRenamingId(value && thread ? thread.id : null);
    if (value) setTitle(thread?.title ?? '');
  };
  const name = thread?.title || t('New conversation');

  const run = (fn: (target: AskThread) => void) => () => {
    if (!thread) return;
    onClose();
    fn(thread);
  };

  function commitRename() {
    const next = title.trim();
    if (thread && next && next !== thread.title) actions.onRename(thread, next);
    onClose();
  }

  return (
    <BottomSheet open={!!thread} title={t('Actions for {{title}}', { title: name })} onClose={onClose} closeLabel={t('Close')} testID="ask-thread-actions">
      {renaming ? (
        <>
          <TextField
            label={t('Conversation title')}
            value={title}
            maxLength={200}
            onChangeText={setTitle}
            autoFocus
            returnKeyType="done"
            onSubmitEditing={commitRename}
            testID="ask-rename-input"
          />
          <Button label={t('Rename')} onPress={commitRename} disabled={!title.trim()} testID="ask-rename-save" />
          <Button label={t('Cancel')} variant="ghost" onPress={() => setRenaming(false)} />
        </>
      ) : thread ? (
        <>
          <ListRow title={t('Rename')} icon="create-outline" onPress={() => setRenaming(true)} testID="ask-action-rename" />
          <ListRow title={thread.pinned ? t('Unpin') : t('Pin')} icon="pin-outline" onPress={run(actions.onTogglePin)} testID="ask-action-pin" />
          <ListRow title={t('Summarize')} icon="document-text-outline" onPress={run(actions.onSummarize)} testID="ask-action-summarize" />
          <ListRow title={thread.archived ? t('Unarchive') : t('Archive')} icon="archive-outline" onPress={run(actions.onToggleArchive)} testID="ask-action-archive" />
          <ListRow title={t('Delete')} icon="trash-outline" destructive onPress={run(actions.onDelete)} testID="ask-action-delete" />
        </>
      ) : null}
    </BottomSheet>
  );
}
