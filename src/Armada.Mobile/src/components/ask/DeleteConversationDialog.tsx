import type { AskThread } from '@dashboard/types/models';
import { useLocale } from '../../i18n/LocaleContext';
import { ConfirmDialog } from '../ui/ConfirmDialog';

/**
 * The dashboard's "Delete conversation" confirmation. Render it next to the control that asks for it, inside the same
 * container: on iOS a view controller presents one modal at a time, so a dialog owned by the Ask screen could not
 * appear over the phone conversation list (itself a modal presented by that screen) and the delete never happened.
 */
export function DeleteConversationDialog({ target, onConfirm, onCancel }: {
  target: AskThread | null;
  onConfirm: (target: AskThread) => void;
  onCancel: () => void;
}) {
  const { t } = useLocale();
  return (
    <ConfirmDialog
      open={!!target}
      title={t('Delete conversation')}
      message={t('Delete "{{title}}"? Its messages and action history are removed. Work it started keeps running and stays visible on the normal pages.', { title: target?.title || t('New conversation') })}
      confirmLabel={t('Delete')}
      cancelLabel={t('Cancel')}
      danger
      onConfirm={() => { if (target) onConfirm(target); }}
      onCancel={onCancel}
      testID="ask-delete-confirm"
    />
  );
}
