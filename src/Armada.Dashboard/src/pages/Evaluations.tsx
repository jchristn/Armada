import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { listCaptains, listModelProbeRuns } from '../api/client';
import type { Captain, ModelProbeRun } from '../types/models';
import PageHeader from '../components/shared/PageHeader';
import StatusBadge from '../components/shared/StatusBadge';
import { useLocale } from '../context/LocaleContext';
import { evaluationResultsPath } from '../lib/evaluationLinks';

/** Rows per page of the Evaluations list. */
export const EVALUATIONS_PAGE_SIZE = 25;

/** Target column text: the captain (linked) for captain runs, the endpoint id for model endpoint runs. */
function TargetCell({ run }: { run: ModelProbeRun }) {
  const { t } = useLocale();
  if (run.targetKind === 'Captain' && run.captainId) {
    return <Link to={`/captains/${encodeURIComponent(run.captainId)}`}>{run.captainName || run.captainId}</Link>;
  }
  return <span title={t('Model endpoint')}>{run.modelEndpointId || '-'}</span>;
}

/**
 * Evaluations: every model probe run (Quick and Full Evaluate) the caller can see, newest first, filterable by
 * captain through ?captainId=. Each row opens the run's results page.
 */
export default function Evaluations() {
  const { t, formatDateTime } = useLocale();
  const [searchParams, setSearchParams] = useSearchParams();
  const captainId = searchParams.get('captainId') ?? '';
  const pageNumber = Math.max(1, Number.parseInt(searchParams.get('page') ?? '1', 10) || 1);
  const [runs, setRuns] = useState<ModelProbeRun[]>([]);
  const [totalPages, setTotalPages] = useState(1);
  const [captains, setCaptains] = useState<Captain[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  useEffect(() => {
    let disposed = false;
    listCaptains({ pageSize: 1000 })
      .then((page) => { if (!disposed) setCaptains(page.objects ?? []); })
      // The filter still works by id when the captain list cannot be read.
      .catch(() => undefined);
    return () => { disposed = true; };
  }, []);

  useEffect(() => {
    let disposed = false;
    setLoading(true);
    setError('');
    listModelProbeRuns({ captainId: captainId || null, pageNumber, pageSize: EVALUATIONS_PAGE_SIZE })
      .then((page) => {
        if (disposed) return;
        setRuns(page.objects ?? []);
        setTotalPages(Math.max(1, page.totalPages ?? 1));
      })
      .catch((err: unknown) => {
        if (disposed) return;
        setRuns([]);
        setError(err instanceof Error ? err.message : t('Failed to load evaluations.'));
      })
      .finally(() => { if (!disposed) setLoading(false); });
    return () => { disposed = true; };
    // t is stable for a locale; reloading on its identity would refetch needlessly.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [captainId, pageNumber]);

  function update(next: { captainId?: string; page?: number }) {
    const params = new URLSearchParams(searchParams);
    if (next.captainId !== undefined) {
      if (next.captainId) params.set('captainId', next.captainId); else params.delete('captainId');
      params.delete('page');
    }
    if (next.page !== undefined) {
      if (next.page > 1) params.set('page', String(next.page)); else params.delete('page');
    }
    setSearchParams(params);
  }

  const knownCaptain = !captainId || captains.some((c) => c.id === captainId);

  return (
    <div>
      <PageHeader
        title={t('Evaluations')}
        subtitle={t('Quick and Full evaluations of captains and model endpoints, newest first. Open a run to see its probe results, the Full report and the battery results.')}
      />
      <div style={{ display: 'flex', gap: '0.5rem', alignItems: 'center', flexWrap: 'wrap', marginBottom: '0.75rem' }}>
        <label htmlFor="evaluations-captain-filter">{t('Captain')}</label>
        <select
          id="evaluations-captain-filter"
          value={captainId}
          onChange={(e) => update({ captainId: e.target.value })}
          title={t('Show only the evaluations of one captain.')}
        >
          <option value="">{t('All captains')}</option>
          {!knownCaptain && <option value={captainId}>{captainId}</option>}
          {captains.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
        </select>
      </div>
      {error && <p role="alert" className="text-danger">{error}</p>}
      {loading ? (
        <p className="text-dim">{t('Loading...')}</p>
      ) : runs.length === 0 && !error ? (
        <p className="text-dim">{t('No evaluations yet. Start one with Evaluate on a captain or in the Endpoint Health dialog.')}</p>
      ) : runs.length > 0 && (
        <table className="data-table" aria-label={t('Evaluations')}>
          <thead>
            <tr>
              {['Started', 'Captain / endpoint', 'Model', 'Depth', 'Status', 'Passed', 'Failed', 'Could not run'].map((label) => <th key={label}>{t(label)}</th>)}
            </tr>
          </thead>
          <tbody>
            {runs.map((run) => (
              <tr key={run.id}>
                <td><Link to={evaluationResultsPath(run.id)}>{formatDateTime(run.startedUtc ?? run.createdUtc)}</Link></td>
                <td><TargetCell run={run} /></td>
                <td>{run.model || '-'}</td>
                <td>{t(run.depth)}</td>
                <td><StatusBadge status={run.status} /></td>
                <td>{run.passedCount}</td>
                <td>{run.failedCount}</td>
                <td>{run.couldNotRunCount}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {totalPages > 1 && (
        <div style={{ display: 'flex', gap: '0.5rem', alignItems: 'center', marginTop: '0.75rem' }}>
          <button type="button" className="btn btn-sm" disabled={pageNumber <= 1 || loading} onClick={() => update({ page: pageNumber - 1 })}>{t('Previous')}</button>
          <span className="text-dim">{t('Page {{page}} of {{total}}', { page: pageNumber, total: totalPages })}</span>
          <button type="button" className="btn btn-sm" disabled={pageNumber >= totalPages || loading} onClick={() => update({ page: pageNumber + 1 })}>{t('Next')}</button>
        </div>
      )}
    </div>
  );
}
