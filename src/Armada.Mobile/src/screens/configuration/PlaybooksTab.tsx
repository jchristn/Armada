import { useMemo, useState } from 'react';
import { createPlaybook, deletePlaybook, listPlaybooks, updatePlaybook } from '@dashboard/api/client';
import type { Playbook } from '@dashboard/types/models';
import { buildPlaybookDuplicatePayload } from '@dashboard/lib/duplicates';
import { canEdit } from '@dashboard/lib/scoping';
import { useAuth } from '../../auth/AuthContext';
import { FormSheet } from '../../components/resource/FormSheet';
import { MasterDetail, useSelection } from '../../components/resource/Hub';
import { ResourceList, StatRow } from '../../components/resource/ResourceList';
import { ResourceRow, useConfirm } from '../../components/resource/ResourceRow';
import { resourceStyles } from '../../components/resource/styles';
import { Button } from '../../components/ui/Button';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { ALL } from '../../resource/lookups';
import { errorText, useLoad, useReloadOnFocus } from '../../resource/useLoad';
import { scopeBadge, useViewer } from './common';
import { PlaybookDetailView } from './PlaybookDetail';
import { playbookFields, playbookPayload, playbookValues } from './simpleForms';

/**
 * Configuration > Playbooks: markdown playbooks attached to voyages and missions. Anyone may create their own; edit,
 * duplicate, and delete follow the scope rules.
 */
export function PlaybooksTab() {
  const { t, formatRelativeTime } = useLocale();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { pushToast } = useNotifications();
  const viewer = useViewer();
  const canManage = isAdmin || isTenantAdmin;
  const { confirm, dialog } = useConfirm('playbook-confirm');
  const selection = useSelection((id) => `/playbooks/${id}`);
  const { data, loading, refreshing, error, reload, refresh } = useLoad(async () => (await listPlaybooks(ALL)).objects ?? [], [], { fallbackError: t('Failed to load playbooks.') });
  useReloadOnFocus(reload);
  const playbooks = useMemo(() => data ?? [], [data]);
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState('all');
  const [editing, setEditing] = useState<Playbook | 'new' | null>(null);

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    return playbooks.filter((p) => (!term || [p.fileName, p.description, p.id].some((v) => (v ?? '').toLowerCase().includes(term)))
      && (status === 'all' || (status === 'active' ? p.active : !p.active)));
  }, [playbooks, search, status]);

  function remove(p: Playbook) {
    confirm({
      title: t('Delete Playbook'),
      message: t('Delete "{{name}}"? Existing mission snapshots will remain, but this playbook will no longer be selectable.', { name: p.fileName }),
      confirmLabel: t('Delete'),
      danger: true,
      onConfirm: async () => {
        try {
          await deletePlaybook(p.id);
          pushToast('warning', t('Playbook "{{name}}" deleted.', { name: p.fileName }));
          if (selection.selected === p.id) selection.clear();
          await reload();
        } catch (err: unknown) {
          pushToast('error', errorText(err, t('Delete failed.')));
        }
      },
    });
  }

  async function duplicate(p: Playbook) {
    try {
      const created = await createPlaybook(buildPlaybookDuplicatePayload(p));
      pushToast('success', t('Playbook "{{name}}" duplicated.', { name: created.fileName }));
      await reload();
      selection.open(created.id);
    } catch (err: unknown) {
      pushToast('error', errorText(err, t('Duplicate failed.')));
    }
  }

  const existing = editing && editing !== 'new' ? editing : null;
  const list = (
    <ResourceList
      testID="playbooks"
      items={filtered}
      keyOf={(p) => p.id}
      loading={loading}
      error={error}
      onRetry={() => void reload()}
      refreshing={refreshing}
      onRefresh={() => void refresh()}
      search={{ value: search, onChange: setSearch, placeholder: t('Search by filename, description, or ID...') }}
      filters={[{ key: 'status', label: t('Status'), value: status, onChange: setStatus, options: [{ value: 'all', label: t('All statuses') }, { value: 'active', label: t('Active only') }, { value: 'inactive', label: t('Inactive only') }] }]}
      header={(
        <>
          <StatRow stats={[
            { label: t('Total Playbooks'), value: playbooks.length },
            { label: t('Active'), value: playbooks.filter((p) => p.active).length },
            { label: t('Inactive'), value: playbooks.filter((p) => !p.active).length },
            { label: t('Stored Markdown'), value: `${playbooks.reduce((n, p) => n + p.content.length, 0)} ${t('chars')}` },
          ]} />
          <Button label={t('Playbook')} icon="add" onPress={() => setEditing('new')} style={resourceStyles.create} testID="playbooks-create" />
        </>
      )}
      emptyTitle={t('No playbooks match the current filters.')}
      emptyMessage={canManage ? t('Create a playbook to start standardizing dispatch behavior.') : t('Ask a tenant administrator to create playbooks for shared guidance.')}
      renderItem={(p) => {
        const editable = canEdit(viewer, p);
        return (
          <ResourceRow
            testID={`playbook-row-${p.id}`}
            title={p.fileName}
            subtitle={[p.description, `${p.content.length} ${t('chars')}`].filter(Boolean).join(' \u2022 ')}
            badge={{ label: p.active ? t('Active') : t('Inactive'), tone: p.active ? 'success' : 'cancelled' }}
            meta={`${scopeBadge(t, p.scope).label} \u2022 ${formatRelativeTime(p.lastUpdateUtc)}`}
            selected={selection.selected === p.id}
            onPress={() => selection.open(p.id)}
            actions={[
              ...(editable ? [{ key: 'edit', label: t('Edit'), icon: 'create-outline' as const, onPress: () => setEditing(p) }] : []),
              { key: 'duplicate', label: t('Duplicate'), icon: 'copy-outline' as const, onPress: () => void duplicate(p) },
              ...(editable ? [{ key: 'delete', label: t('Delete'), icon: 'trash-outline' as const, tone: 'danger' as const, onPress: () => remove(p) }] : []),
            ]}
          />
        );
      }}
    />
  );

  return (
    <>
      <MasterDetail
        list={list}
        detail={selection.selected ? <PlaybookDetailView key={selection.selected} id={selection.selected} embedded onDeleted={() => { selection.clear(); void reload(); }} onChanged={() => void reload()} /> : null}
        onBack={selection.clear}
      />
      <FormSheet
        testID="playbook-form"
        open={editing !== null}
        title={existing ? t('Edit Playbook') : t('Create Playbook')}
        initial={playbookValues(viewer, existing)}
        fields={() => playbookFields(t, viewer)}
        submitLabel={existing ? t('Save Changes') : t('Create Playbook')}
        onClose={() => setEditing(null)}
        onSubmit={async (v) => {
          if (existing) {
            const updated = await updatePlaybook(existing.id, playbookPayload(viewer, v, existing));
            pushToast('success', t('Playbook "{{name}}" saved.', { name: updated.fileName }));
          } else {
            const created = await createPlaybook(playbookPayload(viewer, v, null));
            pushToast('success', t('Playbook "{{name}}" created.', { name: created.fileName }));
          }
          setEditing(null);
          await reload();
        }}
      />
      {dialog}
    </>
  );
}
