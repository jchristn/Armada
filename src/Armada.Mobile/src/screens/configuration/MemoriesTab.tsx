import { useMemo, useState } from 'react';
import { createMemory, deleteMemory, getMemory, listMemories, updateMemory } from '@dashboard/api/client';
import type { EnumerationResult, Memory, MemoryType } from '@dashboard/types/models';
import { MEMORY_TYPES, splitList } from '@dashboard/lib/configuration';
import { canEdit, resolveCreateScope, type ScopeViewer } from '@dashboard/lib/scoping';
import { ActionBar, DetailHeader, DetailPending, Field, FieldCard, JsonSheet, TextBlock } from '../../components/resource/DetailParts';
import { FormSheet, numOrNull, str, type FormField, type FormValues } from '../../components/resource/FormSheet';
import { ResourceList } from '../../components/resource/ResourceList';
import { ResourceRow, useConfirm } from '../../components/resource/ResourceRow';
import { resourceStyles } from '../../components/resource/styles';
import { Button } from '../../components/ui/Button';
import { StatusBadge } from '../../components/ui/StatusBadge';
import type { Translate } from '../../i18n/LocaleContext';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { useVessels } from '../../resource/lookups';
import { errorText, useLoad, useReloadOnFocus } from '../../resource/useLoad';
import { LocalBody, LocalMasterDetail, scopeBadge, scopeField, scopeValue, useLocalSelection, useViewer } from './common';

/** Memories per server page (the dashboard's default page size). */
export const MEMORY_PAGE_SIZE = 25;

export function memoryFilters(type: string, search: string): Record<string, string> {
  const filters: Record<string, string> = {};
  if (type) filters.type = type;
  if (search.trim()) filters.search = search.trim();
  return filters;
}

export function memoryValues(viewer: ScopeViewer, m: Memory | null): FormValues {
  if (!m) return { type: 'Semantic', topic: '', key: '', summary: '', content: '', salience: '0.5', tags: '', vesselId: '', scope: resolveCreateScope(viewer) };
  return {
    type: m.type, topic: m.topic || '', key: m.key || '', summary: m.summary || '', content: m.content, salience: String(m.salience), tags: m.tags.join(', '),
    vesselId: m.vesselId || '', scope: m.scope,
  };
}

export function memoryPayload(viewer: ScopeViewer, v: FormValues, existing: Memory | null): Partial<Memory> {
  return {
    type: str(v, 'type') as MemoryType,
    topic: str(v, 'topic').trim() || null,
    key: str(v, 'key').trim() || null,
    summary: str(v, 'summary').trim() || null,
    content: str(v, 'content'),
    salience: numOrNull(v, 'salience') ?? 0.5,
    tags: splitList(str(v, 'tags')),
    vesselId: str(v, 'vesselId') || null,
    scope: scopeValue(viewer, v, existing?.scope ?? null),
  };
}

function memoryFields(t: Translate, viewer: ScopeViewer, vessels: { id: string; name: string }[]): FormField[] {
  return [
    { kind: 'select', key: 'type', label: t('Type'), options: MEMORY_TYPES.map((x) => ({ value: x, label: t(x) })) },
    { kind: 'text', key: 'topic', label: t('Topic') },
    { kind: 'text', key: 'key', label: t('Key') },
    { kind: 'text', key: 'summary', label: t('Summary') },
    { kind: 'multiline', key: 'content', label: t('Content'), required: true },
    { kind: 'number', key: 'salience', label: t('Salience') },
    { kind: 'text', key: 'tags', label: t('Tags') },
    { kind: 'select', key: 'vesselId', label: t('Vessel'), options: [{ value: '', label: '-' }, ...vessels.map((x) => ({ value: x.id, label: x.name }))] },
    scopeField(t, viewer),
  ];
}

interface MemoryDetailProps {
  id: string;
  inSheet: boolean;
  onEdit: (m: Memory) => void;
  onDelete: (m: Memory) => void;
}

/** One memory: summary, content, provenance, Edit, View JSON, and Delete. */
function MemoryDetail({ id, inSheet, onEdit, onDelete }: MemoryDetailProps) {
  const { t, formatDateTime } = useLocale();
  const viewer = useViewer();
  const [jsonOpen, setJsonOpen] = useState(false);
  const { data: memory, loading, error, reload } = useLoad(() => getMemory(id), [id], { fallbackError: t('Failed to load memories.') });
  if (!memory) return <DetailPending loading={loading} error={error} onRetry={() => void reload()} />;
  const m: Memory = memory;
  const editable = canEdit(viewer, { scope: m.scope, tenantId: m.tenantId, userId: m.userId });
  return (
    <LocalBody inSheet={inSheet} testID="memory-detail">
      <DetailHeader title={m.topic || m.key || t('Memory')} subtitle={m.id} testID="memory-title" badges={(
        <>
          <StatusBadge label={t(m.type)} tone="info" />
          <StatusBadge {...scopeBadge(t, m.scope)} />
        </>
      )} />
      <ActionBar>
        {editable ? <Button label={t('Edit')} style={resourceStyles.action} onPress={() => onEdit(m)} /> : null}
        <Button label={t('View JSON')} variant="ghost" style={resourceStyles.action} onPress={() => setJsonOpen(true)} />
        {editable ? <Button label={t('Delete')} variant="danger" style={resourceStyles.action} onPress={() => onDelete(m)} /> : null}
      </ActionBar>
      <TextBlock title={t('Summary')} text={m.summary} />
      <TextBlock title={t('Content')} text={m.content} />
      <FieldCard>
        <Field label={t('Key')} value={m.key} />
        <Field label={t('Salience')} value={m.salience.toFixed(2)} />
        <Field label={t('Version')} value={m.version} />
        <Field label={t('Tags')} value={m.tags.join(', ')} />
        <Field label={t('Vessel')} value={m.vesselId || m.sourceVesselId} mono />
        <Field label={t('Source')} value={[t(m.sourceKind), m.sourceDetail].filter(Boolean).join(': ')} />
        {m.sourceVoyageId ? <Field label={t('Voyage')} value={m.sourceVoyageId} mono /> : null}
        {m.sourceMissionId ? <Field label={t('Mission')} value={m.sourceMissionId} mono /> : null}
        <Field label={t('Created')} value={formatDateTime(m.createdUtc)} />
        <Field label={t('Updated')} value={formatDateTime(m.lastUpdateUtc)} />
      </FieldCard>
      <JsonSheet open={jsonOpen} title={`${t('Memory')}: ${m.id}`} data={m} onClose={() => setJsonOpen(false)} />
    </LocalBody>
  );
}

/**
 * Configuration > Memory: durable memories distilled from voyages (episodic, semantic, procedural), paged from the
 * server with type and text filters; open one to read, edit, or delete it. Adding a memory by hand is a mobile
 * addition over the dashboard (the same createMemory call agents use).
 */
export function MemoriesTab() {
  const { t, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const viewer = useViewer();
  const vessels = useVessels();
  const { confirm, dialog } = useConfirm('memory-confirm');
  const selection = useLocalSelection();
  const [type, setType] = useState('');
  const [search, setSearch] = useState('');
  const [editing, setEditing] = useState<Memory | 'new' | null>(null);
  const filters = useMemo(() => memoryFilters(type, search), [type, search]);

  const { data: first, loading, refreshing, error, reload, refresh } = useLoad<EnumerationResult<Memory>>(
    () => listMemories({ pageNumber: 1, pageSize: MEMORY_PAGE_SIZE, filters }), [filters], { fallbackError: t('Failed to load memories.') });
  useReloadOnFocus(reload);
  const [extra, setExtra] = useState<{ key: EnumerationResult<Memory> | null; items: Memory[]; page: number }>({ key: null, items: [], page: 1 });
  const [loadingMore, setLoadingMore] = useState(false);
  const pages = extra.key === first ? extra : { key: first, items: [] as Memory[], page: 1 };
  const memories = useMemo(() => [...(first?.objects ?? []), ...pages.items], [first, pages.items]);
  const hasMore = pages.page < (first?.totalPages ?? 1);

  async function loadMore() {
    if (!first || loadingMore) return;
    setLoadingMore(true);
    try {
      const next = await listMemories({ pageNumber: pages.page + 1, pageSize: MEMORY_PAGE_SIZE, filters });
      setExtra({ key: first, items: [...pages.items, ...(next.objects ?? [])], page: pages.page + 1 });
    } catch {
      pushToast('error', t('Failed to load memories.'));
    } finally {
      setLoadingMore(false);
    }
  }

  function remove(m: Memory) {
    confirm({
      title: t('Delete Memory'),
      message: t('Delete this memory? This cannot be undone.'),
      confirmLabel: t('Delete'),
      danger: true,
      onConfirm: async () => {
        try {
          await deleteMemory(m.id);
          pushToast('success', t('Memory deleted.'));
          if (selection.selected === m.id) selection.clear();
          await reload();
        } catch {
          pushToast('error', t('Failed to delete memory.'));
        }
      },
    });
  }

  const existing = editing && editing !== 'new' ? editing : null;
  const list = (
    <ResourceList
      testID="memories"
      items={memories}
      keyOf={(m) => m.id}
      loading={loading}
      error={error}
      onRetry={() => void reload()}
      refreshing={refreshing}
      onRefresh={() => void refresh()}
      onEndReached={() => void loadMore()}
      hasMore={hasMore}
      loadingMore={loadingMore}
      search={{ value: search, onChange: setSearch, placeholder: t('Search content, topic, tags...') }}
      filters={[{ key: 'type', label: t('Type'), value: type, allValue: '', onChange: setType, options: [{ value: '', label: t('All types') }, ...MEMORY_TYPES.map((x) => ({ value: x, label: t(x) }))] }]}
      header={<Button label={t('Memory')} icon="add" onPress={() => setEditing('new')} style={resourceStyles.create} testID="memories-create" />}
      emptyTitle={t('No memories recorded yet.')}
      renderItem={(m) => (
        <ResourceRow
          testID={`memory-row-${m.id}`}
          title={m.summary || m.content}
          subtitle={[m.topic, `${t('Salience')} ${m.salience.toFixed(2)}`, m.vesselId || m.sourceVesselId].filter(Boolean).join(' \u2022 ')}
          badge={{ label: t(m.type), tone: 'info' }}
          meta={formatRelativeTime(m.lastUpdateUtc)}
          selected={selection.selected === m.id}
          onPress={() => selection.open(m.id)}
          actions={canEdit(viewer, { scope: m.scope, tenantId: m.tenantId, userId: m.userId }) ? [{ key: 'delete', label: t('Delete'), icon: 'trash-outline', tone: 'danger', onPress: () => remove(m) }] : []}
        />
      )}
    />
  );

  return (
    <>
      <LocalMasterDetail
        list={list}
        title={t('Memory')}
        open={!!selection.selected}
        onClose={selection.clear}
        detail={selection.selected ? <MemoryDetail key={selection.selected} id={selection.selected} inSheet={!selection.isTablet} onEdit={(m) => setEditing(m)} onDelete={(m) => remove(m)} /> : null}
      />
      <FormSheet
        testID="memory-form"
        open={editing !== null}
        title={t('Memory')}
        initial={memoryValues(viewer, existing)}
        fields={() => memoryFields(t, viewer, vessels)}
        submitLabel={t('Save')}
        onClose={() => setEditing(null)}
        onSubmit={async (v) => {
          try {
            if (existing) await updateMemory(existing.id, memoryPayload(viewer, v, existing));
            else await createMemory(memoryPayload(viewer, v, null));
          } catch (err: unknown) {
            throw new Error(errorText(err, t('Save failed.')));
          }
          pushToast('success', t('Saved.'));
          setEditing(null);
          if (existing && selection.selected === existing.id) selection.clear();
          await reload();
        }}
      />
      {dialog}
    </>
  );
}
