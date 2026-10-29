import { useState } from 'react';
import { Link } from 'react-router-dom';
import { getBlackBoxTimeline } from '../../api/client';
import type { BlackBoxTimelineItem, BlackBoxTimelinePage, BlackBoxTimelineSource } from '../../types/blackbox';
import { useLocale } from '../../context/LocaleContext';
import { usePagedInsight } from '../../lib/usePagedInsight';
import DataTable, { type DataTableColumn } from '../shared/DataTable';
import { EmptyState, ErrorState, LoadingState } from '../shared/StateBlocks';

/** The page an entity id in the timeline opens, by its prefix. */
export function timelineEntityHref(entityId?: string | null): string | null {
  if (!entityId) return null;
  if (entityId.startsWith('msn_')) return `/missions/${encodeURIComponent(entityId)}`;
  if (entityId.startsWith('vyg_')) return `/voyages/${encodeURIComponent(entityId)}`;
  if (entityId.startsWith('dck_')) return `/docks/${encodeURIComponent(entityId)}`;
  if (entityId.startsWith('mrg_')) return `/merge-queue/${encodeURIComponent(entityId)}`;
  if (entityId.startsWith('cpt_')) return `/captains/${encodeURIComponent(entityId)}`;
  if (entityId.startsWith('bbx_')) return `/blackbox/${encodeURIComponent(entityId)}`;
  if (entityId.startsWith('evt_')) return `/events/${encodeURIComponent(entityId)}`;
  return null;
}

const SOURCE_LABEL: Record<BlackBoxTimelineSource, string> = {
  Entry: 'Entry', Note: 'Note', Mission: 'Mission', Voyage: 'Voyage', Dock: 'Dock', MergeEntry: 'Merge entry', Captain: 'Captain', Harbor: 'Harbor', Unknown: 'Unknown',
};

/**
 * Combined timeline of a black box entry: the entry and its notes, the mission (or voyage) and its events, the dock,
 * the merge queue entry, the captain and the Harbor link, merged into one paged list (oldest first by default).
 */
export default function BlackBoxTimeline({ entryId }: { entryId: string }) {
  const { t, formatDateTime } = useLocale();
  const [order, setOrder] = useState<'CreatedAscending' | 'CreatedDescending'>('CreatedAscending');
  const paged = usePagedInsight<BlackBoxTimelineItem, BlackBoxTimelinePage>(
    `${entryId}|${order}`,
    (pageNumber, pageSize) => getBlackBoxTimeline(entryId, { order, pageNumber, pageSize }),
    25,
  );
  const result = paged.result;
  const cut = result?.sources?.filter((s) => s.truncated).map((s) => t(SOURCE_LABEL[s.source] ?? s.source)) ?? [];

  const columns: DataTableColumn<BlackBoxTimelineItem>[] = [
    { key: 'time', label: t('Time'), required: true, cellClassName: 'cell-nowrap', render: (r) => formatDateTime(r.timestampUtc) },
    { key: 'source', label: t('Source'), render: (r) => <span className="blackbox-chip">{t(SOURCE_LABEL[r.source] ?? r.source)}</span> },
    { key: 'kind', label: t('Kind'), cellClassName: 'mono', render: (r) => r.kind },
    {
      key: 'message', label: t('Message'), required: true, interactive: true, cellClassName: 'blackbox-wrap-cell',
      render: (r) => {
        const href = timelineEntityHref(r.entityId);
        return <span>{r.message}{r.entityId && <> {href ? <Link to={href} className="mono text-dim">{r.entityId}</Link> : <span className="mono text-dim">{r.entityId}</span>}</>}</span>;
      },
    },
  ];

  return (
    <div data-testid="blackbox-timeline">
      <div className="blackbox-filters">
        <label>{t('Order')}
          <select value={order} onChange={(e) => setOrder(e.target.value as typeof order)} aria-label={t('Timeline order')}>
            <option value="CreatedAscending">{t('Oldest first')}</option>
            <option value="CreatedDescending">{t('Newest first')}</option>
          </select>
        </label>
      </div>
      {(result?.truncated || cut.length > 0) && (
        <p className="text-dim" style={{ margin: '0 0 0.4rem', fontSize: '0.82rem' }}>
          {t('Some sources were cut to their newest items: {{sources}}.', { sources: cut.join(', ') || t('Timeline') })}
        </p>
      )}
      {paged.error ? <ErrorState message={t('The timeline could not be loaded: {{message}}', { message: paged.error })} onRetry={() => void paged.reload()} /> : (
        <DataTable
          tableKey="blackbox-timeline"
          columns={columns}
          rows={paged.rows}
          rowKey={(r) => `${r.timestampUtc}|${r.source}|${r.kind}|${r.entityId ?? ''}|${r.message}`}
          placeholder={paged.loading && paged.rows.length === 0 ? <LoadingState /> : paged.rows.length === 0 ? <EmptyState title={t('No timeline items')}>{t('Nothing was recorded around this failure.')}</EmptyState> : undefined}
          pagination={{ pageNumber: paged.pageNumber, pageSize: paged.pageSize, totalPages: paged.totalPages, totalRecords: paged.totalRecords, onPageChange: paged.setPageNumber, onPageSizeChange: paged.setPageSize }}
          onRefresh={paged.reload}
          refreshTitle="Reload the timeline from the server now."
          ariaLabel={t('Timeline')}
        />
      )}
    </div>
  );
}
