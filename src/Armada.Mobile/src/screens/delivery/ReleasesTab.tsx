import { useMemo, useState } from 'react';
import { createRelease, deleteRelease, listReleases, updateRelease } from '@dashboard/api/client';
import type { Release } from '@dashboard/types/models';
import { RELEASE_STATUSES } from '@dashboard/lib/deliveryForms';
import { useAuth } from '../../auth/AuthContext';
import { JsonSheet } from '../../components/resource/DetailParts';
import { FormSheet } from '../../components/resource/FormSheet';
import { MasterDetail, useSelection } from '../../components/resource/Hub';
import { ResourceList, StatRow } from '../../components/resource/ResourceList';
import { ResourceRow, useConfirm } from '../../components/resource/ResourceRow';
import { resourceStyles } from '../../components/resource/styles';
import { Button } from '../../components/ui/Button';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { ALL, useNameMap, useVessels, valueOptions } from '../../resource/lookups';
import { statusBadge } from '../../resource/status';
import { errorText, useLoad, useReloadOnFocus } from '../../resource/useLoad';
import { useWorkflowProfiles } from './deliveryLookups';
import { ReleaseDetailView } from './ReleaseDetail';
import { newReleaseValues, releaseFields, releasePayload, releaseValues } from './releaseForm';

/**
 * Delivery > Releases: release records with summary counts, search, status / vessel filters, create and edit for
 * tenant admins, and row actions (open, edit, view JSON, delete).
 */
export function ReleasesTab() {
  const { t, formatRelativeTime } = useLocale();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { pushToast } = useNotifications();
  const canManage = isAdmin || isTenantAdmin;
  const vessels = useVessels();
  const vesselNames = useNameMap(vessels);
  const profiles = useWorkflowProfiles();
  const { confirm, dialog } = useConfirm('release-confirm');
  const selection = useSelection((id) => `/releases/${id}`);

  const { data, loading, refreshing, error, reload, refresh } = useLoad(async () => (await listReleases(ALL)).objects ?? [], [], { fallbackError: t('Failed to load releases.') });
  useReloadOnFocus(reload);
  const releases = useMemo(() => data ?? [], [data]);

  const [search, setSearch] = useState('');
  const [status, setStatus] = useState('all');
  const [vessel, setVessel] = useState('all');
  const [editing, setEditing] = useState<Release | 'new' | null>(null);
  const [json, setJson] = useState<Release | null>(null);

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    return releases.filter((r) => (!term || [r.title, r.version, r.tagName, r.summary, r.id].some((v) => (v ?? '').toLowerCase().includes(term)))
      && (status === 'all' || r.status === status)
      && (vessel === 'all' || r.vesselId === vessel));
  }, [releases, search, status, vessel]);

  function remove(r: Release) {
    confirm({
      title: t('Delete Release'),
      message: t('Delete "{{title}}"? This removes the release record but does not delete linked work or artifacts on disk.', { title: r.title }),
      confirmLabel: t('Delete'),
      danger: true,
      onConfirm: async () => {
        try {
          await deleteRelease(r.id);
          pushToast('warning', t('Release "{{title}}" deleted.', { title: r.title }));
          if (selection.selected === r.id) selection.clear();
          await reload();
        } catch (err: unknown) {
          pushToast('error', errorText(err, t('Delete failed.')));
        }
      },
    });
  }

  const linkedWork = (r: Release) => `${r.voyageIds.length} ${t('voyages')}, ${r.missionIds.length} ${t('missions')}, ${r.checkRunIds.length} ${t('checks')}, ${r.artifacts.length} ${t('artifacts')}`;
  const list = (
    <ResourceList
      testID="releases"
      items={filtered}
      keyOf={(r) => r.id}
      loading={loading}
      error={error}
      onRetry={() => void reload()}
      refreshing={refreshing}
      onRefresh={() => void refresh()}
      search={{ value: search, onChange: setSearch, placeholder: t('Search by title, version, tag, summary, or ID...') }}
      filters={[
        { key: 'status', label: t('Status'), value: status, onChange: setStatus, options: [{ value: 'all', label: t('All statuses') }, ...valueOptions(RELEASE_STATUSES, t)] },
        { key: 'vessel', label: t('Vessel'), value: vessel, onChange: setVessel, options: [{ value: 'all', label: t('All vessels') }, ...vessels.map((v) => ({ value: v.id, label: v.name }))] },
      ]}
      header={(
        <>
          <StatRow stats={[
            { label: t('Total Releases'), value: releases.length },
            { label: t('Shipped'), value: releases.filter((r) => r.status === 'Shipped').length, tone: 'success' },
            { label: t('Candidates'), value: releases.filter((r) => r.status === 'Candidate').length },
            { label: t('Failed / Rolled Back'), value: releases.filter((r) => r.status === 'Failed' || r.status === 'RolledBack').length },
          ]} />
          {canManage ? <Button label={t('Create Release')} icon="add" onPress={() => setEditing('new')} style={resourceStyles.create} testID="releases-create" /> : null}
        </>
      )}
      emptyTitle={t('No releases match the current filters.')}
      emptyMessage={canManage ? t('Create a draft release from voyages, missions, or checks to begin tracking what is shipping.') : t('Ask a tenant administrator to create and manage release records.')}
      renderItem={(r) => (
        <ResourceRow
          testID={`release-row-${r.id}`}
          title={r.title}
          subtitle={[r.version || t('Unversioned'), r.vesselId ? (vesselNames.get(r.vesselId) || r.vesselId) : null, linkedWork(r)].filter(Boolean).join(' \u2022 ')}
          badge={statusBadge(t, r.status)}
          meta={formatRelativeTime(r.lastUpdateUtc)}
          selected={selection.selected === r.id}
          onPress={() => selection.open(r.id)}
          actions={[
            ...(canManage ? [{ key: 'edit', label: t('Edit'), icon: 'create-outline' as const, onPress: () => setEditing(r) }] : []),
            { key: 'json', label: t('View JSON'), icon: 'code-outline' as const, onPress: () => setJson(r) },
            ...(canManage ? [{ key: 'delete', label: t('Delete'), icon: 'trash-outline' as const, tone: 'danger' as const, onPress: () => remove(r) }] : []),
          ]}
        />
      )}
    />
  );

  return (
    <>
      <MasterDetail
        list={list}
        detail={selection.selected ? <ReleaseDetailView key={selection.selected} id={selection.selected} embedded onDeleted={() => { selection.clear(); void reload(); }} onChanged={() => void reload()} /> : null}
        onBack={selection.clear}
      />
      <FormSheet
        testID="release-form"
        open={editing !== null}
        title={editing === 'new' ? t('Create Release') : t('Edit Release')}
        initial={editing && editing !== 'new' ? releaseValues(editing) : newReleaseValues()}
        fields={() => releaseFields(t, vessels, profiles)}
        submitLabel={editing === 'new' ? t('Create Release') : t('Save Changes')}
        onClose={() => setEditing(null)}
        onSubmit={async (values) => {
          if (editing && editing !== 'new') {
            const updated = await updateRelease(editing.id, releasePayload(values, []));
            pushToast('success', t('Release "{{title}}" saved.', { title: updated.title }));
          } else {
            const created = await createRelease(releasePayload(values, []));
            pushToast('success', t('Release "{{title}}" created.', { title: created.title }));
          }
          setEditing(null);
          await reload();
        }}
      />
      <JsonSheet open={json !== null} title={json?.title ?? ''} data={json} onClose={() => setJson(null)} />
      {dialog}
    </>
  );
}
