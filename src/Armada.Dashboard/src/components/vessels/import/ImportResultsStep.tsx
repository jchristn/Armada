import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import type { VesselImportBatch, VesselImportItem, VesselImportOutcome } from '../../../types/models';
import { useLocale } from '../../../context/LocaleContext';
import CodeStatusBadge from '../../shared/CodeStatusBadge';
import DataTable from '../../shared/DataTable';
import { OUTCOMES, OUTCOME_META, batchStatusBadge, outcomeBadge, outcomeReasonLabel } from '../../../lib/vesselImportLabels';
import { usePersistedPageSize } from '../../../lib/usePersistedPageSize';
import { BACKGROUND_ACTIVITY_PATH } from '../../../lib/navTargets';

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
  const [pageSize, setPageSize] = usePersistedPageSize('vessel-import-results', 50);

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
        <span className="mono text-dim" title={t('Import batch ID. The batch stays in the import history.')}>{batch.id}</span>
        <span className="text-dim" title={t('When this import batch was started.')}>{formatDateTime(batch.createdUtc)}</span>
      </div>

      {polling && (
        <div className="alert alert-info" role="status" aria-live="polite">
          <p>{t('{count, plural, one {Importing # repository in the background.} other {Importing # repositories in the background.}}', { count: selectedCount ?? 0 })}</p>
          <div className="progress-bar" role="progressbar" aria-valuemin={0} aria-valuemax={100} aria-valuenow={percent} aria-label={t('Import progress')}>
            <div className="progress-fill" style={{ width: `${percent}%` }} />
          </div>
          <p className="text-dim">
            {t('{{processed}} of {{total}} processed', { processed: processed.toLocaleString(), total: target.toLocaleString() })}
            {jobId && <> {' - '}<Link to={BACKGROUND_ACTIVITY_PATH} title={t('View recorded background task outcomes in Events.')}>{t('View activity')}</Link> <span className="mono" title={t('Background job ID for this import.')}>({jobId})</span></>}
          </p>
          <p className="text-dim">{t('You can close this dialog; the import keeps running and appears in the import history.')}</p>
        </div>
      )}
      {pollError && <div className="alert alert-error" role="alert">{pollError}</div>}

      <div className="import-results-stats">
        <div className="fleet-run-stat" title={t('Vessels this import created.')}><span className="fleet-run-stat-label">{t('Created')}</span><span className="fleet-run-stat-value">{batch.createdCount.toLocaleString()}</span></div>
        <div className="fleet-run-stat" title={t('Candidates not imported because they were not selected or a matching vessel already existed.')}><span className="fleet-run-stat-label">{t('Skipped')}</span><span className="fleet-run-stat-value">{batch.skippedCount.toLocaleString()}</span></div>
        <div className="fleet-run-stat" title={t('Candidates whose vessel could not be created. Filter by Failed below to see why.')}><span className="fleet-run-stat-label">{t('Failed')}</span><span className="fleet-run-stat-value">{batch.failedCount.toLocaleString()}</span></div>
        <div className="fleet-run-stat" title={t('Repositories discovery found in the folders you chose.')}><span className="fleet-run-stat-label">{t('Candidates')}</span><span className="fleet-run-stat-value">{batch.candidateCount.toLocaleString()}</span></div>
      </div>

      {items.length > 0 && (
        <>
          <div className="import-status-chips" role="group" aria-label={t('Filter items by outcome')}>
            <button type="button" className={`chip${outcomeFilter === '' ? ' active' : ''}`} aria-pressed={outcomeFilter === ''} title={t('Show every item in this batch, whatever its outcome.')} onClick={() => { setOutcomeFilter(''); setPageNumber(1); }}>
              {t('All')} <span className="chip-count">{items.length.toLocaleString()}</span>
            </button>
            {OUTCOMES.filter((o) => (counts[o] ?? 0) > 0).map((o) => (
              <button key={o} type="button" className={`chip${outcomeFilter === o ? ' active' : ''}`} aria-pressed={outcomeFilter === o} title={t(OUTCOME_META[o].description)} onClick={() => { setOutcomeFilter(o); setPageNumber(1); }}>
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
              { key: 'name', label: t('Name'), headerTitle: t('Vessel name used for this repository.'), required: true, render: (item) => <strong data-i18n-skip="true">{item.proposedName}</strong> },
              { key: 'outcome', label: t('Outcome'), headerTitle: t('What happened to this candidate: created, skipped, not selected, failed, or still pending.'), cellClassName: 'cell-nowrap', render: (item) => <CodeStatusBadge {...outcomeBadge(t, item.outcome)} /> },
              {
                // The outcome message used to be a second line under the reason; it is in the tooltip and its own
                // (hidden by default) column now.
                key: 'reason', label: t('Reason'), headerTitle: t('Why the candidate was skipped or failed. Hover a cell for the full message.'),
                cellTitle: (item) => item.outcomeMessage || undefined,
                render: (item) => outcomeReasonLabel(t, item.outcomeReason) || <span className="text-dim">-</span>,
              },
              {
                key: 'message', label: t('Message'), headerTitle: t('Full detail message from the import, such as the error text.'), defaultHidden: true, cellClassName: 'text-dim truncate-cell',
                cellTitle: (item) => item.outcomeMessage || undefined,
                render: (item) => (item.outcomeMessage ? <span className="truncate-text" data-i18n-skip="true">{item.outcomeMessage}</span> : '-'),
              },
              {
                key: 'path', label: t('Path'), headerTitle: t('Folder on the Admiral host that was imported.'), cellClassName: 'mono', cellTitle: (item) => item.path,
                render: (item) => <span className="cell-clip" data-i18n-skip="true"><span>{item.path}</span></span>,
              },
              {
                key: 'vessel', label: t('Vessel'), headerTitle: t('The vessel created for this folder, or the existing vessel that matched it. Click to open it.'), interactive: true,
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
