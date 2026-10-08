import { useRouter, type Href } from 'expo-router';
import { useRef, useState, type ReactNode } from 'react';
import { createVessel, deleteVessel } from '@dashboard/api/client';
import type { Fleet, Pipeline, Vessel } from '@dashboard/types/models';
import { buildVesselDuplicatePayload } from '@dashboard/lib/duplicates';
import { errorMessage } from '../../build/useLiveResource';
import { StyleSheet, View } from 'react-native';
import { AppText, BottomSheet, Button, ListRow } from '../../components/ui';
import type { IconName } from '../../components/ui';
import { useAuth } from '../../auth/AuthContext';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { spacing } from '../../theme/typography';
import { Branches } from './Branches';
import { BuildContext } from './BuildContext';
import { JsonView } from './JsonView';
import { VesselForm, VesselFormActions, type VesselFormHandle, type VesselFormStatus } from './VesselForm';
import { vesselLinks } from './vesselLinks';

/** Every action of the dashboard's vessel row menu (and the vessel page's menu). */
export type VesselActionKey =
  | 'dispatch' | 'branches' | 'history' | 'objectives' | 'fleet' | 'workspace' | 'onboarding' | 'detail' | 'runCheck'
  | 'health' | 'context' | 'runAction' | 'edit' | 'duplicate' | 'json' | 'delete';

interface SheetState {
  kind: 'menu' | 'form' | 'branches' | 'context' | 'json' | 'delete';
  /** The vessel acted on (null: the create form). */
  vessel: Vessel | null;
}

export interface VesselActionsOptions {
  fleets: Fleet[];
  pipelines: Pipeline[];
  /** After a create, edit, branch change, or context build: reload. */
  onChanged: () => void;
  /** After a delete (the vessel page leaves; the list reloads). */
  onDeleted: (vessel: Vessel) => void;
  /** Leave out actions that make no sense where the menu is (the vessel page has no "View Detail"). */
  exclude?: VesselActionKey[];
}

export interface VesselActions {
  /** Run one action on a vessel. */
  run: (key: VesselActionKey, vessel: Vessel) => void;
  /** Open the full action menu for a vessel. */
  openMenu: (vessel: Vessel) => void;
  /** Open the create form. */
  create: () => void;
  /** Sheets and dialogs the actions open; render once in the screen. */
  elements: ReactNode;
}

/**
 * The vessel actions shared by the Vessels tab and the vessel page: the action menu as a bottom sheet (the mobile
 * form of the dashboard's ActionMenu) and what it opens (form, branches, Build Context, JSON, delete confirmation), all
 * in one sheet whose content switches.
 */
export function useVesselActions({ fleets, pipelines, onChanged, onDeleted, exclude = [] }: VesselActionsOptions): VesselActions {
  const { t } = useLocale();
  const router = useRouter();
  const { isTenantAdmin } = useAuth();
  const { pushToast } = useNotifications();
  // One sheet whose content switches (menu -> form, branches, ...): chaining two native modals is unreliable on iOS.
  const [sheet, setSheet] = useState<SheetState | null>(null);
  const close = () => setSheet(null);
  const formRef = useRef<VesselFormHandle>(null);
  const [formStatus, setFormStatus] = useState<VesselFormStatus>({ canSave: false, saving: false });

  const go = (path: string) => router.push(path as Href);

  async function duplicate(vessel: Vessel) {
    try {
      const created = await createVessel(buildVesselDuplicatePayload(vessel));
      pushToast('success', t('Vessel "{{name}}" duplicated.', { name: created.name }));
      onChanged();
      go(vesselLinks.edit(created.id));
    } catch (e) {
      pushToast('error', errorMessage(e) || t('Duplicate failed.'));
    }
  }

  async function confirmDelete(vessel: Vessel) {
    close();
    try {
      await deleteVessel(vessel.id);
      pushToast('warning', t('Vessel "{{name}}" deleted.', { name: vessel.name }));
      onDeleted(vessel);
    } catch {
      pushToast('error', t('Delete failed.'));
    }
  }

  function run(key: VesselActionKey, vessel: Vessel) {
    close();
    switch (key) {
      case 'dispatch': go(vesselLinks.dispatch(vessel.id)); return;
      case 'branches': setSheet({ kind: 'branches', vessel }); return;
      case 'history': go(vesselLinks.history(vessel.id)); return;
      case 'objectives': go(vesselLinks.objectives(vessel)); return;
      case 'fleet': if (vessel.fleetId) go(vesselLinks.fleet(vessel.fleetId)); return;
      case 'workspace': go(vesselLinks.workspace(vessel.id)); return;
      case 'onboarding': go(vesselLinks.onboarding(vessel.id)); return;
      case 'detail': go(vesselLinks.detail(vessel.id)); return;
      case 'runCheck': go(vesselLinks.runCheck(vessel)); return;
      case 'health': go(vesselLinks.health(vessel.id)); return;
      case 'context': setSheet({ kind: 'context', vessel }); return;
      case 'runAction': go(vesselLinks.runAction([vessel.id])); return;
      case 'edit': setSheet({ kind: 'form', vessel }); return;
      case 'duplicate': void duplicate(vessel); return;
      case 'json': setSheet({ kind: 'json', vessel }); return;
      case 'delete': setSheet({ kind: 'delete', vessel }); return;
    }
  }

  const menuItems = (vessel: Vessel): { key: VesselActionKey; label: string; icon: IconName; disabled?: boolean; danger?: boolean }[] => {
    const hasContext = !!(vessel.modelContext && vessel.modelContext.trim().length > 0);
    const all: { key: VesselActionKey; label: string; icon: IconName; disabled?: boolean; danger?: boolean }[] = [
      { key: 'dispatch', label: t('Dispatch'), icon: 'paper-plane-outline' },
      { key: 'branches', label: t('Manage Branches'), icon: 'git-branch-outline' },
      { key: 'history', label: t('View History'), icon: 'time-outline' },
      { key: 'objectives', label: t('Manage Objectives'), icon: 'flag-outline' },
      { key: 'fleet', label: t('Manage Fleet'), icon: 'boat-outline', disabled: !vessel.fleetId },
      { key: 'workspace', label: t('Open Workspace'), icon: 'folder-open-outline' },
      { key: 'onboarding', label: t('Onboarding'), icon: 'checkbox-outline' },
      { key: 'detail', label: t('View Detail'), icon: 'information-circle-outline' },
      { key: 'runCheck', label: t('Run Check'), icon: 'checkmark-done-outline' },
      { key: 'health', label: t('Health'), icon: 'pulse-outline' },
      { key: 'context', label: hasContext ? t('Refine Context') : t('Build Context'), icon: 'sparkles-outline' },
      { key: 'edit', label: t('Edit'), icon: 'create-outline' },
      { key: 'duplicate', label: t('Duplicate'), icon: 'copy-outline' },
      { key: 'json', label: t('View JSON'), icon: 'code-slash-outline' },
      { key: 'delete', label: t('Delete'), icon: 'trash-outline', danger: true },
    ];
    if (isTenantAdmin) all.splice(11, 0, { key: 'runAction', label: t('Run action...'), icon: 'flash-outline' });
    return all.filter((a) => !exclude.includes(a.key));
  };

  const hasContext = !!(sheet?.vessel?.modelContext && sheet.vessel.modelContext.trim().length > 0);
  let title = '';
  let body: ReactNode = null;
  let footer: ReactNode = null;
  if (sheet) {
    const vessel = sheet.vessel;
    switch (sheet.kind) {
      case 'menu':
        title = vessel?.name ?? '';
        body = vessel ? menuItems(vessel).map((item) => (
          <ListRow
            key={item.key}
            title={item.label}
            icon={item.icon}
            destructive={item.danger}
            onPress={item.disabled ? undefined : () => run(item.key, vessel)}
            subtitle={item.disabled ? t('This vessel has no fleet.') : null}
            testID={`vessel-action-${item.key}`}
          />
        )) : null;
        break;
      case 'form':
        title = vessel ? t('Edit Vessel') : t('Create Vessel');
        body = (
          <VesselForm
            ref={formRef}
            vessel={vessel}
            fleets={fleets}
            pipelines={pipelines}
            onStatus={setFormStatus}
            onError={(message) => pushToast('error', message)}
            onSaved={(name, created) => {
              close();
              pushToast('success', created ? t('Vessel "{{name}}" created.', { name }) : t('Vessel "{{name}}" saved.', { name }));
              onChanged();
            }}
          />
        );
        footer = <VesselFormActions status={formStatus} onSave={() => formRef.current?.save()} onCancel={close} />;
        break;
      case 'branches':
        title = `${t('Manage Branches')} -- ${vessel?.name ?? ''}`;
        body = vessel ? <Branches vesselId={vessel.id} /> : null;
        break;
      case 'context':
        title = hasContext ? t('Refine Model Context') : t('Build Model Context');
        body = vessel ? (
          <BuildContext
            vessel={vessel}
            refine={hasContext}
            onClose={close}
            onBuilt={(updated) => {
              pushToast('success', t('Model Context updated for "{{name}}".', { name: updated.name }));
              onChanged();
            }}
          />
        ) : null;
        break;
      case 'json':
        title = `${t('Vessel')}: ${vessel?.name ?? ''}`;
        body = <JsonView data={vessel} />;
        break;
      case 'delete':
        title = t('Delete Vessel');
        body = vessel ? (
          <View>
            <AppText muted style={styles.message}>{t('Delete vessel "{{name}}"? This cannot be undone.', { name: vessel.name })}</AppText>
            <View style={styles.actions}>
              <Button label={t('Cancel')} variant="ghost" onPress={close} testID="vessel-delete-confirm-cancel" />
              <Button label={t('Delete')} variant="danger" onPress={() => void confirmDelete(vessel)} testID="vessel-delete-confirm-confirm" />
            </View>
          </View>
        ) : null;
        break;
    }
  }

  const elements = (
    <BottomSheet
      open={!!sheet}
      title={title}
      onClose={() => {
        // Closing the branches sheet reloads (counts and sync changed), like the dashboard's BranchesModal onClose.
        if (sheet?.kind === 'branches') onChanged();
        close();
      }}
      closeLabel={t('Close')}
      testID={sheet ? `vessel-${sheet.kind === 'menu' ? 'actions' : sheet.kind}` : undefined}
      footer={footer}
    >
      {body}
    </BottomSheet>
  );

  return {
    run,
    openMenu: (vessel: Vessel) => setSheet({ kind: 'menu', vessel }),
    create: () => setSheet({ kind: 'form', vessel: null }),
    elements,
  };
}

const styles = StyleSheet.create({
  message: { marginBottom: spacing.lg },
  actions: { flexDirection: 'row', justifyContent: 'flex-end', gap: spacing.sm },
});
