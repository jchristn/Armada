import { useState, type ReactElement } from 'react';
import { createCaptain, deleteCaptain, recallCaptain, restartCaptain, stopAllCaptains, stopCaptain, unquarantineCaptain } from '@dashboard/api/client';
import { buildCaptainDuplicatePayload } from '@dashboard/lib/duplicates';
import type { Captain } from '@dashboard/types/models';
import { errorMessage } from '../../build/useLiveResource';
import { ConfirmDialog } from '../../components/ui';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';

/** Lifecycle actions on a captain, each with the dashboard's confirmation text. */
export type CaptainAction = 'stop' | 'recall' | 'restart' | 'delete' | 'stopAll';

/** Which page asks: the list and the detail page word some confirmations differently (as on the dashboard). */
export type CaptainActionContext = 'list' | 'detail';

interface Pending {
  action: CaptainAction;
  captain: Captain | null;
}

export interface CaptainActions {
  /** Ask for confirmation, then run the action. */
  request: (action: CaptainAction, captain: Captain | null) => void;
  /** Lift a quarantine (no confirmation, as on the dashboard). */
  unquarantine: (captain: Captain) => Promise<void>;
  /** Create a copy; resolves to the new captain or null. */
  duplicate: (captain: Captain) => Promise<Captain | null>;
  /** The confirmation dialog; render it once on the screen. */
  dialog: ReactElement;
}

/**
 * Stop, recall, restart, delete, stop all, unquarantine, and duplicate a captain with the dashboard's confirmations
 * and toasts. `onDone` runs after a successful action (reload, or leave the detail page after a delete).
 */
export function useCaptainActions(context: CaptainActionContext, onDone: (action: CaptainAction | 'unquarantine', captain: Captain | null) => void): CaptainActions {
  const { t } = useLocale();
  const { pushToast } = useNotifications();
  const [pending, setPending] = useState<Pending | null>(null);

  const name = pending?.captain?.name ?? '';
  let title = '';
  let message = '';
  let confirmLabel = t('Confirm');
  switch (pending?.action) {
    case 'stop':
      title = t('Stop Captain');
      message = context === 'detail'
        ? t('Stop captain "{{name}}"? This will halt the current mission.', { name })
        : t('Stop captain "{{name}}"? The captain process will be terminated.', { name });
      confirmLabel = t('Stop');
      break;
    case 'recall':
      title = t('Recall Captain');
      message = context === 'detail'
        ? t('Recall captain "{{name}}"? The captain will finish current work and return to idle.', { name })
        : t('Recall captain "{{name}}"? The captain will be recalled from its current mission.', { name });
      confirmLabel = t('Recall');
      break;
    case 'restart':
      title = t('Restart Captain');
      message = t('Restart captain "{{name}}"? The captain will be deleted and recreated with the same saved configuration.', { name });
      confirmLabel = t('Restart');
      break;
    case 'delete':
      title = context === 'detail' ? t('Remove Captain') : t('Delete Captain');
      message = context === 'detail'
        ? t('Remove captain "{{name}}"? This cannot be undone.', { name })
        : t('Delete captain "{{name}}"? This cannot be undone.', { name });
      confirmLabel = context === 'detail' ? t('Remove') : t('Delete');
      break;
    case 'stopAll':
      title = t('Stop All Captains');
      message = t('Stop ALL captains? All captain processes will be terminated. This cannot be undone.');
      confirmLabel = t('Stop All');
      break;
    default:
      break;
  }

  async function run(p: Pending) {
    const n = p.captain?.name ?? '';
    try {
      switch (p.action) {
        case 'stop':
          await stopCaptain(p.captain!.id);
          pushToast('warning', t('Captain "{{name}}" stopped.', { name: n }));
          break;
        case 'recall':
          await recallCaptain(p.captain!.id);
          pushToast('warning', t('Captain "{{name}}" recalled.', { name: n }));
          break;
        case 'restart':
          await restartCaptain(p.captain!.id);
          pushToast('success', t('Captain "{{name}}" restarted.', { name: n }));
          break;
        case 'delete':
          await deleteCaptain(p.captain!.id);
          pushToast('warning', context === 'detail' ? t('Captain "{{name}}" removed.', { name: n }) : t('Captain "{{name}}" deleted.', { name: n }));
          break;
        case 'stopAll':
          await stopAllCaptains();
          pushToast('warning', t('All captains stopped.'));
          break;
      }
      onDone(p.action, p.captain);
    } catch (e) {
      pushToast('error', errorMessage(e));
    }
  }

  const dialog = (
    <ConfirmDialog
      open={!!pending}
      title={title}
      message={message}
      confirmLabel={confirmLabel}
      cancelLabel={t('Cancel')}
      danger={pending?.action !== 'recall'}
      onConfirm={() => { const p = pending; setPending(null); if (p) void run(p); }}
      onCancel={() => setPending(null)}
      testID="captain-confirm"
    />
  );

  return {
    request: (action, captain) => setPending({ action, captain }),
    unquarantine: async (captain) => {
      try {
        await unquarantineCaptain(captain.id);
        pushToast('success', t('Quarantine lifted for "{{name}}".', { name: captain.name }));
        onDone('unquarantine', captain);
      } catch (e) {
        pushToast('error', errorMessage(e));
      }
    },
    duplicate: async (captain) => {
      try {
        const created = await createCaptain(buildCaptainDuplicatePayload(captain));
        pushToast('success', t('Captain "{{name}}" duplicated.', { name: created.name }));
        return created;
      } catch (e) {
        pushToast('error', errorMessage(e));
        return null;
      }
    },
    dialog,
  };
}
