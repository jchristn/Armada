import { useCallback, useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { getUsageLatest } from '../../api/client';
import { useLocale } from '../../context/LocaleContext';
import { useUsageRevision } from '../../context/WebSocketContext';
import { captainUsagePath, viewKind } from '../../lib/usage';
import type { EnumerationResult, UsageSnapshotView } from '../../types/models';
import DataTable, { type DataTableColumn } from '../shared/DataTable';
import UsageState from './UsageState';
import { useAutoRefresh } from '../../lib/useAutoRefresh';
import { usePersistedPageSize } from '../../lib/usePersistedPageSize';
import CopyButton from '../shared/CopyButton';
import UsageWindowBar from './UsageWindowBar';

interface AccountLimitsTableProps {
  provider?: string;
  accountKey?: string;
  hostId?: string;
}

const MAX_PAGE_SIZE = 100;

/** Limits grouped by account and host: one row per account observation, member captains beneath it. */
export default function AccountLimitsTable({ provider, accountKey, hostId }: AccountLimitsTableProps) {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const revision = useUsageRevision();
  const [pageNumber, setPageNumber] = useState(1);
  const [storedPageSize, setPageSize] = usePersistedPageSize('usage-account-limits');
  const pageSize = Math.min(MAX_PAGE_SIZE, storedPageSize);
  const requestVersion = useRef(0);
  const [sort, setSort] = useState<{ field: string; dir: 'asc' | 'desc' }>({ field: 'updated', dir: 'desc' });
  const [attempt, setAttempt] = useState(0);
  const [result, setResult] = useState<EnumerationResult<UsageSnapshotView> | null>(null);
  const [status, setStatus] = useState<'loading' | 'error' | 'ready'>('loading');
  const [error, setError] = useState('');

  const filterKey = `${provider ?? ''}|${accountKey ?? ''}|${hostId ?? ''}`;
  const [shownKey, setShownKey] = useState(filterKey);
  if (shownKey !== filterKey) {
    setShownKey(filterKey);
    setPageNumber(1);
  }

  const load = useCallback(async () => {
    const version = ++requestVersion.current;
    setStatus('loading');
    try {
      const data = await getUsageLatest({ provider: provider || undefined, accountKey: accountKey || undefined, hostId: hostId || undefined, pageNumber, pageSize, sortBy: sort.field, sortDirection: sort.dir === 'asc' ? 'ascending' : 'descending' });
      if (version !== requestVersion.current) return;
      setResult(data);
      setError('');
      setStatus('ready');
    } catch (err: unknown) {
      if (version !== requestVersion.current) return;
      setError(err instanceof Error ? err.message : String(err));
      setStatus('error');
    }
  }, [provider, accountKey, hostId, pageNumber, pageSize, sort]);
  useEffect(() => {
    void load();
    return () => { requestVersion.current++; };
  }, [load, revision, attempt]);
  const { seconds, setSeconds } = useAutoRefresh('usage-account-limits', () => { void load(); });

  const rows = result?.objects ?? [];

  const columns: DataTableColumn<UsageSnapshotView>[] = [
    {
      key: 'captains', sortKey: 'captainId',
      label: t('Captain ID'),
      required: true,
      interactive: true,
      render: (view) => (
        <ul className="usage-member-list">
          {(view.captainIds ?? []).map((id) => (
            <li key={id}><span className="id-display"><Link to={captainUsagePath(id)} className="mono" title={t('Open this captain\'s usage section.')}>{id}</Link><CopyButton text={id} /></span></li>
          ))}
        </ul>
      ),
    },
    {
      key: 'host', sortKey: 'hostId', label: t('Host ID'), interactive: true,
      render: (view) => {
        const host = view.harborId ?? view.hostId;
        return host ? <span className="id-display"><span className="mono" title={host}>{host}</span><CopyButton text={host} /></span> : '-';
      },
    },
    { key: 'runtime', sortKey: 'runtime', label: t('Runtime'), render: (view) => view.runtime },
    {
      key: 'limits', sortKey: 'limits',
      label: t('Limits'),
      className: 'usage-limits-cell',
      render: (view) => {
        const kind = viewKind(view);
        if (view.windows.length === 0) {
          return <span className="text-dim">{kind === 'unsupported' ? (view.capabilityReason || t('Not available for this runtime')) : kind === 'authMissing' ? t('Sign-in needed') : t('No limit windows reported')}</span>;
        }
        return (
          <div className="usage-limits-bars">
            {view.windows.map((window) => (
              <UsageWindowBar key={`${window.name}-${window.model ?? ''}`} window={window} stale={kind === 'stale' || kind === 'failed'} />
            ))}
          </div>
        );
      },
    },
    {
      key: 'observed', sortKey: 'updated',
      label: t('Updated'),
      cellTitle: (view) => (view.observedUtc ? formatDateTime(view.observedUtc) : undefined),
      render: (view) => (view.observedUtc ? formatRelativeTime(view.observedUtc) : <span className="text-dim">{t('Never')}</span>),
    },
    {
      key: 'interval', sortKey: 'interval',
      label: t('Interval'),
      headerTitle: t('Requested and effective capture interval for this account. Captains that share the account share one schedule.'),
      cellTitle: (view) => t('Every {{effective}}s (requested {{requested}}s)', { effective: view.effectiveIntervalSeconds, requested: view.requestedIntervalSeconds }),
      render: (view) => `${view.effectiveIntervalSeconds}s`,
    },
  ];


  return (
    <>
    {status === 'error' && <div role="alert"><UsageState kind="error" onRetry={() => setAttempt((n) => n + 1)} /><p className="text-dim">{error}</p></div>}
    <DataTable
      tableKey="usage-account-limits"
      className="request-history-table"
      autoRefresh={{ seconds, onChange: setSeconds }}
      onRefresh={load}
      sort={{ ...sort, onSort: (field) => { setSort((previous) => ({ field, dir: previous.field === field && previous.dir === 'asc' ? 'desc' : 'asc' })); setPageNumber(1); } }}
      ariaLabel={t('Limits grouped by account and host')}
      columns={columns}
      rows={rows}
      rowKey={(view) => view.accountId ?? view.id ?? `${view.hostId}|${view.runtime}|${view.provider}|${view.accountKey}`}
      placeholder={status === 'loading' && !result ? <UsageState kind="loading" /> : undefined}
      emptyMessage={status === 'error' ? <UsageState kind="error" compact /> :
        <div className="usage-limits-empty">
          <strong>{t('No account usage has been captured yet')}</strong>
          <p className="text-dim">{t('Turn on usage capture in Settings and on each captain. Until an observation is stored, consumption is unknown, not zero.')}</p>
        </div>
      }
      wrapClassName="request-history-table-wrap"
      pagination={{
        pageNumber,
        pageSize,
        totalMs: result?.totalMs,
        totalPages: result?.totalPages ?? 1,
        totalRecords: result?.totalRecords ?? 0,
        onPageChange: setPageNumber,
        onPageSizeChange: (size) => { setPageSize(Math.min(MAX_PAGE_SIZE, size)); setPageNumber(1); },
      }}
    />
    </>
  );
}
