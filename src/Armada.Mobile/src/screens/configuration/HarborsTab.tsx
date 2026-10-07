import { useMemo, useState } from 'react';
import { createHarbor, deleteHarbor, disableHarbor, enableHarbor, getHarbor, listHarbors, updateHarbor } from '@dashboard/api/client';
import type { Harbor } from '@dashboard/types/models';
import { useAuth } from '../../auth/AuthContext';
import { ActionBar, DetailHeader, DetailPending, Field, FieldCard, JsonSheet } from '../../components/resource/DetailParts';
import { bool, FormSheet, str, type FormValues } from '../../components/resource/FormSheet';
import { ResourceList, StatRow } from '../../components/resource/ResourceList';
import { ResourceRow, useConfirm } from '../../components/resource/ResourceRow';
import { resourceStyles } from '../../components/resource/styles';
import { Button } from '../../components/ui/Button';
import { StatusBadge, type StatusTone } from '../../components/ui/StatusBadge';
import type { Translate } from '../../i18n/LocaleContext';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { errorText, useLoad, useReloadOnFocus } from '../../resource/useLoad';
import { LocalBody, LocalMasterDetail, useLocalSelection } from './common';

function connectionTone(h: Harbor): StatusTone {
  switch (h.connectionStatus) {
    case 'Connected': return 'success';
    case 'Degraded': return 'warning';
    case 'Disconnected': return 'failed';
    default: return 'cancelled';
  }
}

export function harborValues(h: Harbor | null): FormValues {
  return h ? { name: h.name, maxConcurrentJobs: String(h.maxConcurrentJobs ?? 4), enabled: h.enabled } : { name: 'New Harbor', maxConcurrentJobs: '4', enabled: true };
}

export function harborPayload(v: FormValues): Partial<Harbor> {
  return { name: str(v, 'name'), maxConcurrentJobs: Number.parseInt(str(v, 'maxConcurrentJobs'), 10) || 4, enabled: bool(v, 'enabled') };
}

function capabilityText(t: Translate, h: Harbor): string {
  return h.capabilities.length === 0 ? '-' : h.capabilities.map((c) => `${c.name}${c.available ? '' : t(' (unavailable)')}`).join(', ');
}

interface HarborDetailProps {
  id: string;
  inSheet: boolean;
  canManage: boolean;
  onEdit: (h: Harbor) => void;
  onToggle: (h: Harbor) => Promise<void>;
  onDelete: (h: Harbor) => void;
}

/** One harbor (the dashboard's Details modal) with Edit, Enable / Disable, View JSON, and Delete for tenant admins. */
function HarborDetail({ id, inSheet, canManage, onEdit, onToggle, onDelete }: HarborDetailProps) {
  const { t, formatDateTime } = useLocale();
  const [jsonOpen, setJsonOpen] = useState(false);
  const { data: harbor, loading, error, reload } = useLoad(() => getHarbor(id), [id], { fallbackError: t('Failed to load harbors.') });
  if (!harbor) return <DetailPending loading={loading} error={error} onRetry={() => void reload()} />;
  const h: Harbor = harbor;
  return (
    <LocalBody inSheet={inSheet} testID="harbor-detail">
      <DetailHeader title={h.name} subtitle={h.id} testID="harbor-title" badges={<StatusBadge label={t(h.connectionStatus)} tone={connectionTone(h)} />} />
      <ActionBar>
        {canManage ? <Button label={t('Edit')} style={resourceStyles.action} onPress={() => onEdit(h)} /> : null}
        {canManage ? <Button label={h.enabled ? t('Disable') : t('Enable')} variant="secondary" style={resourceStyles.action} onPress={() => void onToggle(h).then(() => reload())} testID="harbor-toggle" /> : null}
        <Button label={t('View JSON')} variant="ghost" style={resourceStyles.action} onPress={() => setJsonOpen(true)} />
        {canManage ? <Button label={t('Delete')} variant="danger" style={resourceStyles.action} onPress={() => onDelete(h)} /> : null}
      </ActionBar>
      <FieldCard>
        <Field label={t('Status')} value={t(h.connectionStatus)} />
        <Field label={t('Enabled')} value={h.enabled ? t('Yes') : t('No')} />
        <Field label={t('Capacity')} value={h.maxConcurrentJobs} />
        <Field label={t('Platform')} value={[h.osPlatform, h.architecture].filter(Boolean).join(' / ') || '-'} />
        <Field label={t('Protocol')} value={h.protocolVersion || '-'} />
        <Field label={t('Last Seen')} value={h.lastSeenUtc ? formatDateTime(h.lastSeenUtc) : t('Never')} />
        <Field label={t('Capabilities')} value={capabilityText(t, h)} />
      </FieldCard>
      <JsonSheet open={jsonOpen} title={h.name} data={h} onClose={() => setJsonOpen(false)} />
    </LocalBody>
  );
}

/**
 * Configuration > Harbors: detached host runners with connection status, capacity, and capabilities. Tenant admins
 * register, edit, enable / disable, and delete them.
 */
export function HarborsTab() {
  const { t, formatRelativeTime } = useLocale();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { pushToast } = useNotifications();
  const canManage = isAdmin || isTenantAdmin;
  const { confirm, dialog } = useConfirm('harbor-confirm');
  const selection = useLocalSelection();
  const { data, loading, refreshing, error, reload, refresh } = useLoad(async () => (await listHarbors()) ?? [], [], { fallbackError: t('Failed to load harbors.') });
  useReloadOnFocus(reload);
  const harbors = useMemo(() => data ?? [], [data]);
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState('all');
  const [editing, setEditing] = useState<Harbor | 'new' | null>(null);

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    return harbors.filter((h) => (!term || [h.name, h.id, h.osPlatform].some((v) => (v ?? '').toLowerCase().includes(term)))
      && (status === 'all' || h.connectionStatus === status));
  }, [harbors, search, status]);

  async function toggle(h: Harbor) {
    try {
      if (h.enabled) await disableHarbor(h.id);
      else await enableHarbor(h.id);
      pushToast('success', t('Harbor "{{name}}" {{state}}.', { name: h.name, state: h.enabled ? t('disabled') : t('enabled') }));
      await reload();
    } catch (err: unknown) {
      pushToast('error', errorText(err, t('Update failed.')));
    }
  }

  function remove(h: Harbor) {
    confirm({
      title: t('Delete Harbor'),
      message: t('Delete "{{name}}"? Its registration is removed; running work on it is not affected until it reconnects.', { name: h.name }),
      confirmLabel: t('Delete'),
      danger: true,
      onConfirm: async () => {
        try {
          await deleteHarbor(h.id);
          pushToast('warning', t('Harbor "{{name}}" deleted.', { name: h.name }));
          if (selection.selected === h.id) selection.clear();
          await reload();
        } catch (err: unknown) {
          pushToast('error', errorText(err, t('Delete failed.')));
        }
      },
    });
  }

  const existing = editing && editing !== 'new' ? editing : null;
  const selected = harbors.find((h) => h.id === selection.selected);
  const list = (
    <ResourceList
      testID="harbors"
      items={filtered}
      keyOf={(h) => h.id}
      loading={loading}
      error={error}
      onRetry={() => void reload()}
      refreshing={refreshing}
      onRefresh={() => void refresh()}
      search={{ value: search, onChange: setSearch, placeholder: t('Search by name, platform, or ID...') }}
      filters={[{
        key: 'status', label: t('Status'), value: status, onChange: setStatus,
        options: [{ value: 'all', label: t('All statuses') }, ...['Connected', 'Degraded', 'Disconnected', 'Unknown'].map((s) => ({ value: s, label: t(s) }))],
      }]}
      header={(
        <>
          <StatRow stats={[
            { label: t('Total Harbors'), value: harbors.length },
            { label: t('Connected'), value: harbors.filter((h) => h.connectionStatus === 'Connected').length },
            { label: t('Disconnected'), value: harbors.filter((h) => h.connectionStatus === 'Disconnected').length },
            { label: t('Enabled'), value: harbors.filter((h) => h.enabled).length },
          ]} />
          {canManage ? <Button label={t('Harbor')} icon="add" onPress={() => setEditing('new')} style={resourceStyles.create} testID="harbors-create" /> : null}
        </>
      )}
      emptyTitle={t('No harbors match the current filters.')}
      emptyMessage={canManage ? t('Install the Harbor app on a host and connect it, or pre-register one here.') : t('Ask a tenant administrator to connect a Harbor.')}
      renderItem={(h) => (
        <ResourceRow
          testID={`harbor-row-${h.id}`}
          title={h.enabled ? h.name : `${h.name} (${t('disabled')})`}
          subtitle={`${t('Capacity')}: ${h.maxConcurrentJobs} \u2022 ${capabilityText(t, h)}`}
          badge={{ label: t(h.connectionStatus), tone: connectionTone(h) }}
          meta={h.lastSeenUtc ? formatRelativeTime(h.lastSeenUtc) : t('Never')}
          selected={selection.selected === h.id}
          onPress={() => selection.open(h.id)}
          actions={canManage ? [
            { key: 'edit', label: t('Edit'), icon: 'create-outline', onPress: () => setEditing(h) },
            { key: 'toggle', label: h.enabled ? t('Disable') : t('Enable'), icon: h.enabled ? 'pause-outline' : 'play-outline', onPress: () => void toggle(h) },
            { key: 'delete', label: t('Delete'), icon: 'trash-outline', tone: 'danger', onPress: () => remove(h) },
          ] : []}
        />
      )}
    />
  );

  return (
    <>
      <LocalMasterDetail
        list={list}
        title={selected?.name ?? t('Harbor')}
        open={!!selection.selected}
        onClose={selection.clear}
        detail={selection.selected ? (
          <HarborDetail key={selection.selected} id={selection.selected} inSheet={!selection.isTablet} canManage={canManage}
            onEdit={(h) => setEditing(h)} onToggle={toggle} onDelete={(h) => remove(h)} />
        ) : null}
      />
      <FormSheet
        testID="harbor-form"
        open={editing !== null}
        title={existing ? t('Edit Harbor') : t('Register Harbor')}
        initial={harborValues(existing)}
        fields={() => [
          { kind: 'text', key: 'name', label: t('Name'), required: true },
          { kind: 'integer', key: 'maxConcurrentJobs', label: t('Max Concurrent Jobs') },
          { kind: 'switch', key: 'enabled', label: t('Enabled for routing') },
        ]}
        submitLabel={existing ? t('Save Changes') : t('Register Harbor')}
        onClose={() => setEditing(null)}
        onSubmit={async (v) => {
          if (existing) {
            const updated = await updateHarbor(existing.id, harborPayload(v));
            pushToast('success', t('Harbor "{{name}}" saved.', { name: updated.name }));
          } else {
            const created = await createHarbor(harborPayload(v));
            pushToast('success', t('Harbor "{{name}}" registered.', { name: created.name }));
          }
          setEditing(null);
          await reload();
        }}
      />
      {dialog}
    </>
  );
}
