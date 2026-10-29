import { useState } from 'react';
import { Link } from 'react-router-dom';
import { getBlackBoxRecoveryStats } from '../../api/client';
import type { BlackBoxRecoveryStat } from '../../types/blackbox';
import type { EnumerationResult, Vessel } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import { usePagedInsight } from '../../lib/usePagedInsight';
import DataTable, { type DataTableColumn } from '../shared/DataTable';
import { EmptyState, ErrorState, LoadingState } from '../shared/StateBlocks';

/** Success rate as a whole percent, or null while no run has settled. */
export function recoveryRatePercent(rate?: number | null): number | null {
  if (rate == null || Number.isNaN(rate)) return null;
  return Math.round(Math.max(0, Math.min(1, rate)) * 100);
}

/**
 * Recovery stats tab: outcomes of confirmed recovery runs grouped by rule and path (runs, succeeded, failed,
 * pending, expired and the success rate over settled runs), most runs first. The advisor uses the rates to rank
 * equally strong paths.
 */
export default function BlackBoxRecoveryStatsTab({ vessels }: { vessels: Vessel[] }) {
  const { t, formatDateTime } = useLocale();
  const [vesselId, setVesselId] = useState('');
  const [ruleInput, setRuleInput] = useState('');
  const [ruleId, setRuleId] = useState('');
  const paged = usePagedInsight<BlackBoxRecoveryStat, EnumerationResult<BlackBoxRecoveryStat>>(
    `${vesselId}|${ruleId}`,
    (pageNumber, pageSize) => getBlackBoxRecoveryStats({ vesselId: vesselId || undefined, ruleId: ruleId || undefined, pageNumber, pageSize }),
    25,
  );

  const columns: DataTableColumn<BlackBoxRecoveryStat>[] = [
    { key: 'rule', label: t('Rule'), cellClassName: 'mono', interactive: true, render: (r) => r.ruleId ? <button type="button" className="btn-link mono" onClick={() => { setRuleInput(r.ruleId ?? ''); setRuleId(r.ruleId ?? ''); }} title={t('Show only this rule.')}>{r.ruleId}</button> : '-' },
    { key: 'path', label: t('Recovery path'), required: true, cellClassName: 'mono', render: (r) => r.pathId },
    { key: 'runs', label: t('Runs'), render: (r) => r.runs },
    { key: 'succeeded', label: t('Succeeded'), render: (r) => r.succeeded },
    { key: 'failed', label: t('Failed'), render: (r) => r.failed },
    { key: 'pending', label: t('Pending'), render: (r) => r.pending },
    { key: 'expired', label: t('Expired'), headerTitle: t('Runs with no terminal mission status within 7 days.'), render: (r) => r.expired },
    {
      key: 'rate', label: t('Success rate'), required: true, headerTitle: t('Succeeded over settled runs (succeeded plus failed).'),
      render: (r) => {
        const pct = recoveryRatePercent(r.successRate);
        if (pct === null) return <span className="text-dim" title={t('No run of this path has settled yet.')}>-</span>;
        return (
          <span aria-label={t('{{rate}}% success', { rate: pct })}>
            <span className="blackbox-rate">{pct}%</span>
            <span className="blackbox-rate-bar" aria-hidden="true"><span style={{ width: `${pct}%` }} /></span>
          </span>
        );
      },
    },
    { key: 'lastRun', label: t('Last run'), cellClassName: 'cell-nowrap', render: (r) => r.lastRunUtc ? formatDateTime(r.lastRunUtc) : '-' },
  ];

  return (
    <div data-testid="blackbox-recovery-stats">
      <p className="text-dim" style={{ margin: '0 0 0.5rem', fontSize: '0.85rem' }}>
        {t('Every confirmed recovery run is followed until the mission\'s next terminal status. Paths with a higher success rate rank first among equally strong ones.')}
        {' '}<Link to="/runbooks" title={t('Add a line "blackbox: <path id>" to a runbook\'s description to have that path start the runbook.')}>{t('Link a runbook to a path')}</Link>
      </p>
      <div className="blackbox-filters">
        <label>{t('Vessel')}
          <select value={vesselId} onChange={(e) => setVesselId(e.target.value)} aria-label={t('Vessel')}>
            <option value="">{t('Any')}</option>
            {vessels.map((v) => <option key={v.id} value={v.id}>{v.name}</option>)}
          </select>
        </label>
        <form onSubmit={(e) => { e.preventDefault(); setRuleId(ruleInput.trim()); }} style={{ display: 'flex', gap: '0.4rem', alignItems: 'flex-end' }}>
          <label>{t('Rule')}
            <input value={ruleInput} onChange={(e) => setRuleInput(e.target.value)} placeholder="BBX-LAND-001" aria-label={t('Rule filter')} className="mono" />
          </label>
          <button type="submit" className="btn btn-sm" title={t('Show only the paths of this rule.')}>{t('Filter')}</button>
          {ruleId && <button type="button" className="btn btn-sm" onClick={() => { setRuleInput(''); setRuleId(''); }} title={t('Show every rule again.')}>{t('Clear')}</button>}
        </form>
      </div>
      {paged.error ? <ErrorState message={t('Recovery statistics could not be loaded: {{message}}', { message: paged.error })} onRetry={() => void paged.reload()} /> : (
        <DataTable
          tableKey="blackbox-recovery-stats"
          columns={columns}
          rows={paged.rows}
          rowKey={(r) => `${r.ruleId ?? ''}|${r.pathId}`}
          placeholder={paged.loading && paged.rows.length === 0 ? <LoadingState /> : paged.rows.length === 0 ? <EmptyState title={t('No recovery runs yet')}>{t('Statistics appear here after recovery paths are run from black box entries.')}</EmptyState> : undefined}
          pagination={{ pageNumber: paged.pageNumber, pageSize: paged.pageSize, totalPages: paged.totalPages, totalRecords: paged.totalRecords, onPageChange: paged.setPageNumber, onPageSizeChange: paged.setPageSize }}
          onRefresh={paged.reload}
          refreshTitle="Reload the recovery statistics from the server now."
          ariaLabel={t('Recovery stats')}
        />
      )}
    </div>
  );
}
