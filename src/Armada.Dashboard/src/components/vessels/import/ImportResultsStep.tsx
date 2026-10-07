import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import type { VesselImportBatch, VesselImportItem, VesselImportOutcome } from '../../../types/models';
import { useLocale } from '../../../context/LocaleContext';
import CodeStatusBadge from '../../shared/CodeStatusBadge';
import DataTable from '../../shared/DataTable';
import { OUTCOMES, OUTCOME_META, batchStatusBadge, outcomeBadge, outcomeReasonLabel } from '../../../lib/vesselImportLabels';

interface ImportResultsStepProps {
  batch: VesselImportBatch;
  items: VesselImportItem[];
  /** Number of paths the operator selected (used for background progress). */
  selectedCount?: number;
  /** True while a background import is still running and being polled. */
  polling?: boolean;
  jobId?: string | null;
  pollError?: string;
}

/** Results step (also the batch detail view in import history): outcome summary and per-item table. */
export default function ImportResultsStep({ batch, items, selectedCount, polling = false, jobId, pollError }: ImportResultsStepProps) {
  const { t, formatDateTime } = useLocale();
  const [outcomeFilter, setOutcomeFilter] = useState<VesselImportOutcome | ''>('');
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(50);

  const counts = useMemo(() => {
    const c: Partial<Record<VesselImportOutcome, number>> = {};
    for (const item of items) c[item.outcome] = (c[item.outcome] ?? 0) + 1;
    return c;
  }, [items]);

  const filtered = useMemo(() => items.filter((i) => !outcomeFilter || i.outcome === outcomeFilter), [items, outcomeFilter]);
  const totalPages = Math.max(1, Math.ceil(filtered.length / pageSize));
  const page = Math.min(pageNumber, totalPages);
  const visible = filtered.slice((page - 1) * pageSize, page * pageSize);
  const statusBadge = batchStatusBadge(t, batch.status);

  const processed = batch.createdCount + batch.skippedCount + batch.failedCount;
  const target = Math.max(selectedCount ?? 0, processed, 1);
  const percent = Math.min(100, Math.round((processed / target) * 100));

  return (
    <div className="import-results">
      <div className="import-results-head">
        <CodeStatusBadge {...statusBadge} />
        <span className="mono text-dim">{batch.id}</span>
        <span className="text-dim">{formatDateTime(batch.createdUtc)}</span>
      </div>

      {polling && (
        <div className="alert alert-info" role="status" aria-live="polite">
          <p>{t('{count, plural, one {Importing # repository in the background.} other {Importing # repositories in the background.}}', { count: selectedCount ?? 0 })}</p>
          <div className="progress-bar" role="progressbar" aria-valuemin={0} aria-valuemax={100} aria-valuenow={percent} aria-label={t('Import progress')}>
            <div className="progress-fill" style={{ width: `${percent}%` }} />
          </div>
          <p className="text-dim">
            {t('{{processed}} of {{total}} processed', { processed: processed.toLocaleString(), total: target.toLocaleString() })}
            {jobId && <> {' - '}<Link to="/jobs">{t('Open the Jobs page')}</Link> <span className="mono">({jobId})</span></>}
          </p>
          <p className="text-dim">{t('You can close this dialog; the import keeps running and appears in the import history.')}</p>
        </div>
      )}
      {pollError && <div className="alert alert-error" role="alert">{pollError}</div>}

      <div className="import-results-stats">
        <div className="fleet-run-stat"><span className="fleet-run-stat-label">{t('Created')}</span><span className="fleet-run-stat-value">{batch.createdCount.toLocaleString()}</span></div>
        <div className="fleet-run-stat"><span className="fleet-run-stat-label">{t('Skipped')}</span><span className="fleet-run-stat-value">{batch.skippedCount.toLocaleString()}</span></div>
        <div className="fleet-run-stat"><span className="fleet-run-stat-label">{t('Failed')}</span><span className="fleet-run-stat-value">{batch.failedCount.toLocaleString()}</span></div>
        <div className="fleet-run-stat"><span className="fleet-run-stat-label">{t('Candidates')}</span><span className="fleet-run-stat-value">{batch.candidateCount.toLocaleString()}</span></div>
      </div>

      {items.length > 0 && (
        <>
          <div className="import-status-chips" role="group" aria-label={t('Filter items by outcome')}>
            <button type="button" className={`chip${outcomeFilter === '' ? ' active' : ''}`} aria-pressed={outcomeFilter === ''} onClick={() => { setOutcomeFilter(''); setPageNumber(1); }}>
              {t('All')} <span className="chip-count">{items.length.toLocaleString()}</span>
            </button>
            {OUTCOMES.filter((o) => (counts[o] ?? 0) > 0).map((o) => (
              <button key={o} type="button" className={`chip${outcomeFilter === o ? ' active' : ''}`} aria-pressed={outcomeFilter === o} onClick={() => { setOutcomeFilter(o); setPageNumber(1); }}>
                {t(OUTCOME_META[o].label)} <span className="chip-count">{(counts[o] ?? 0).toLocaleString()}</span>
              </button>
            ))}
          </div>
          <DataTable
            tableKey="vessel-import-results"
            wrapClassName="import-table-wrap"
            rows={visible}
            rowKey={(item) => item.id || item.path}
            pagination={{
              pageNumber: page,
              pageSize,
              totalPages,
              totalRecords: filtered.length,
              onPageChange: setPageNumber,
              onPageSizeChange: (size) => { setPageSize(size); setPageNumber(1); },
            }}
            emptyMessage={t('No items match the current filter.')}
            columns={[
              { key: 'name', label: t('Name'), required: true, render: (item) => <strong data-i18n-skip="true">{item.proposedName}</strong> },
              { key: 'outcome', label: t('Outcome'), cellClassName: 'cell-nowrap', render: (item) => <CodeStatusBadge {...outcomeBadge(t, item.outcome)} /> },
              {
                // The outcome message used to be a second line under the reason; it is in the tooltip and its own
                // (hidden by default) column now.
                key: 'reason', label: t('Reason'),
                cellTitle: (item) => item.outcomeMessage || undefined,
                render: (item) => outcomeReasonLabel(t, item.outcomeReason) || <span className="text-dim">-</span>,
              },
              {
                key: 'message', label: t('Message'), defaultHidden: true, cellClassName: 'text-dim truncate-cell',
                cellTitle: (item) => item.outcomeMessage || undefined,
                render: (item) => (item.outcomeMessage ? <span className="truncate-text" data-i18n-skip="true">{item.outcomeMessage}</span> : '-'),
              },
              {
                key: 'path', label: t('Path'), cellClassName: 'mono', cellTitle: (item) => item.path,
                render: (item) => <span className="cell-clip" data-i18n-skip="true"><span>{item.path}</span></span>,
              },
              {
                key: 'vessel', label: t('Vessel'), interactive: true,
                render: (item) => {
                  const vesselId = item.vesselId ?? item.existingVesselId;
                  return vesselId ? <Link to={`/vessels/${vesselId}`} className="mono">{vesselId}</Link> : <span className="text-dim">-</span>;
                },
              },
            ]}
          />
        </>
      )}
      {items.length === 0 && !polling && <p className="text-dim">{t('This batch has no items.')}</p>}
    </div>
  );
}
