import { useState } from 'react';
import { Link } from 'react-router-dom';
import { getVesselBlackBoxDoctor } from '../../api/client';
import type { BlackBoxDoctorFinding, BlackBoxDoctorPage } from '../../types/blackbox';
import { useLocale } from '../../context/LocaleContext';
import { usePagedInsight } from '../../lib/usePagedInsight';
import DataTable, { type DataTableColumn } from '../shared/DataTable';
import { EmptyState, ErrorState, LoadingState } from '../shared/StateBlocks';
import BlackBoxActionDialog, { type BlackBoxActionTarget } from './BlackBoxActionDialog';
import './BlackBox.css';

export const DOCTOR_WINDOWS = [7, 30, 90, 365];

/**
 * Black box doctor card on the vessel page: configuration recommendations that keep recurring across the vessel's
 * entries in a window, most frequent first, each with its newest entry as the example. Apply runs the example
 * entry's recommendation through the usual dry run and confirmation.
 */
export default function VesselBlackBoxDoctorCard({ vesselId, onApplied }: { vesselId: string; onApplied?: () => void }) {
  const { t, formatDateTime } = useLocale();
  const [windowDays, setWindowDays] = useState(30);
  const [action, setAction] = useState<{ entryId: string; target: BlackBoxActionTarget } | null>(null);
  const paged = usePagedInsight<BlackBoxDoctorFinding, BlackBoxDoctorPage>(
    `${vesselId}|${windowDays}`,
    (pageNumber, pageSize) => getVesselBlackBoxDoctor(vesselId, { windowDays, pageNumber, pageSize }),
    10,
  );
  const page = paged.result;

  const columns: DataTableColumn<BlackBoxDoctorFinding>[] = [
    {
      key: 'recommendation', label: t('Recommendation'), required: true, cellClassName: 'blackbox-wrap-cell',
      render: (r) => (
        <div>
          <strong className="mono">{r.setting}</strong>{r.suggestedValue != null && <> {r.currentValue != null ? <span className="mono text-dim">{r.currentValue}</span> : null} {'->'} <span className="mono">{r.suggestedValue}</span></>}
          {r.rationale && <div className="text-dim" style={{ fontSize: '0.8rem' }}>{r.rationale}</div>}
        </div>
      ),
    },
    { key: 'rule', label: t('Rule'), cellClassName: 'mono cell-nowrap', render: (r) => r.ruleId ?? '-' },
    { key: 'count', label: t('Times'), headerTitle: t('How many entries in the window recommended this.'), render: (r) => r.count },
    { key: 'lastSeen', label: t('Last seen'), cellClassName: 'cell-nowrap', render: (r) => <span title={t('First seen {{time}}', { time: formatDateTime(r.firstSeenUtc) })}>{formatDateTime(r.lastSeenUtc)}</span> },
    {
      key: 'example', label: t('Example'), interactive: true,
      render: (r) => (
        <span>
          <Link to={`/blackbox/${encodeURIComponent(r.exampleEntryId)}`} className="mono" title={t('Open the newest entry that recommended this.')}>{r.exampleEntryId}</Link>
          {r.exampleMissionId && <> · <Link to={`/missions/${encodeURIComponent(r.exampleMissionId)}`} className="mono text-dim">{r.exampleMissionId}</Link></>}
        </span>
      ),
    },
    {
      key: 'apply', label: t('Actions'), interactive: true,
      render: (r) => r.applicable && r.recommendationId
        ? <button type="button" className="btn btn-sm" onClick={() => setAction({ entryId: r.exampleEntryId, target: { kind: 'recommendation', id: r.recommendationId!, title: r.setting } })} title={t('Dry run this change from the example entry, then confirm to apply it.')}>{t('Apply...')}</button>
        : <span className="text-dim" title={t('This recommendation has no automatic action; change the setting by hand.')}>-</span>,
    },
  ];

  return (
    <div className="card" style={{ marginTop: '1rem' }} data-testid="vessel-blackbox-doctor">
      <div style={{ display: 'flex', justifyContent: 'space-between', gap: '0.75rem', flexWrap: 'wrap', alignItems: 'center' }}>
        <h3 title={t('Configuration recommendations that keep recurring across this vessel\'s black box entries.')}>{t('Black Box doctor')}</h3>
        <label style={{ fontSize: '0.85rem' }}>{t('Window')}{' '}
          <select value={windowDays} onChange={(e) => setWindowDays(Number(e.target.value))} aria-label={t('Doctor window')}>
            {DOCTOR_WINDOWS.map((d) => <option key={d} value={d}>{t('Last {{count}} days', { count: d })}</option>)}
          </select>
        </label>
      </div>
      {page && <p className="text-dim" style={{ margin: '0 0 0.5rem', fontSize: '0.85rem' }}>{t('{{scanned}} of {{total}} entries in the window were read.', { scanned: page.entriesScanned, total: page.entriesInWindow })}</p>}
      {paged.error ? <ErrorState message={t('The doctor could not be loaded: {{message}}', { message: paged.error })} onRetry={() => void paged.reload()} /> : (
        <DataTable
          tableKey="vessel-blackbox-doctor"
          columns={columns}
          rows={paged.rows}
          rowKey={(r) => r.key}
          placeholder={paged.loading && paged.rows.length === 0 ? <LoadingState /> : paged.rows.length === 0 ? <EmptyState title={t('No recurring recommendations')}>{t('No configuration recommendation recurred on this vessel in the window.')}</EmptyState> : undefined}
          pagination={{ pageNumber: paged.pageNumber, pageSize: paged.pageSize, totalPages: paged.totalPages, totalRecords: paged.totalRecords, onPageChange: paged.setPageNumber, onPageSizeChange: paged.setPageSize }}
          onRefresh={paged.reload}
          refreshTitle="Reload the doctor findings from the server now."
          ariaLabel={t('Black Box doctor')}
        />
      )}
      <BlackBoxActionDialog
        open={!!action}
        entryId={action?.entryId ?? ''}
        target={action?.target ?? null}
        onClose={() => setAction(null)}
        onRan={() => { void paged.reload(); onApplied?.(); }}
      />
    </div>
  );
}
