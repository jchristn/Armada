import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import type { VesselImportBatch, VesselImportItem, VesselImportOutcome } from '../../../types/models';
import { useLocale } from '../../../context/LocaleContext';
import CodeStatusBadge from '../../shared/CodeStatusBadge';
import Pagination from '../../shared/Pagination';
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
          <Pagination
            pageNumber={page}
            pageSize={pageSize}
            totalPages={totalPages}
            totalRecords={filtered.length}
            onPageChange={setPageNumber}
            onPageSizeChange={(s) => { setPageSize(s); setPageNumber(1); }}
          />
          <div className="table-wrap import-table-wrap">
            <table>
              <thead>
                <tr>
                  <th scope="col">{t('Name')}</th>
                  <th scope="col">{t('Outcome')}</th>
                  <th scope="col">{t('Reason')}</th>
                  <th scope="col">{t('Path')}</th>
                  <th scope="col">{t('Vessel')}</th>
                </tr>
              </thead>
              <tbody>
                {visible.map((item) => {
                  const badge = outcomeBadge(t, item.outcome);
                  const vesselId = item.vesselId ?? item.existingVesselId;
                  return (
                    <tr key={item.id || item.path}>
                      <td><strong data-i18n-skip="true">{item.proposedName}</strong></td>
                      <td><CodeStatusBadge {...badge} /></td>
                      <td>
                        {outcomeReasonLabel(t, item.outcomeReason) || <span className="text-dim">-</span>}
                        {item.outcomeMessage && <div className="text-dim cell-subline" data-i18n-skip="true">{item.outcomeMessage}</div>}
                      </td>
                      <td className="mono" title={item.path} data-i18n-skip="true"><span className="cell-clip"><span>{item.path}</span></span></td>
                      <td>
                        {vesselId ? <Link to={`/vessels/${vesselId}`} className="mono">{vesselId}</Link> : <span className="text-dim">-</span>}
                      </td>
                    </tr>
                  );
                })}
                {visible.length === 0 && <tr><td colSpan={5} className="text-dim">{t('No items match the current filter.')}</td></tr>}
              </tbody>
            </table>
          </div>
        </>
      )}
      {items.length === 0 && !polling && <p className="text-dim">{t('This batch has no items.')}</p>}
    </div>
  );
}
