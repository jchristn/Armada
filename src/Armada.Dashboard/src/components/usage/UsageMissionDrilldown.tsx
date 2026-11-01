import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { getUsageMissions } from '../../api/client';
import { useLocale } from '../../context/LocaleContext';
import { useUsageRevision } from '../../context/WebSocketContext';
import { accountAlias } from '../../lib/usage';
import type { UsageMissionFilter, UsageMissionResult, UsageMissionRow } from '../../types/models';
import DataTable, { type DataTableColumn } from '../shared/DataTable';
import UsageState from './UsageState';

/** The exact selection the drill-down lists: a half-open UTC range plus provider and optional account/host. */
export interface UsageDrilldownSelection {
  from: string;
  to: string;
  provider: string;
  accountKey?: string | null;
  hostId?: string | null;
}

interface UsageMissionDrilldownProps {
  selection: UsageDrilldownSelection;
  onClose?: () => void;
}

const MAX_PAGE_SIZE = 100;

type SortField = 'totalTokens' | 'name' | 'completion' | 'cost';

/** Formats a cost in its own currency. Null cost is unknown, never zero. */
export function formatUsageCost(cost: number | null | undefined, currency: string | null | undefined): string | null {
  if (cost === null || cost === undefined || Number.isNaN(cost)) return null;
  if (currency && /^[A-Za-z]{3}$/.test(currency)) {
    try {
      return new Intl.NumberFormat(undefined, { style: 'currency', currency: currency.toUpperCase(), maximumFractionDigits: 4 }).format(cost);
    } catch {
      // Fall through to the plain form for a currency code Intl does not know.
    }
  }
  const amount = new Intl.NumberFormat(undefined, { maximumFractionDigits: 4 }).format(cost);
  return currency ? `${amount} ${currency}` : amount;
}

/** Missions completed in exactly the selected bucket, provider, account and host, with coverage and nullable cost. */
export default function UsageMissionDrilldown({ selection, onClose }: UsageMissionDrilldownProps) {
  const { t, formatDateTime } = useLocale();
  const revision = useUsageRevision();
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [sortField, setSortField] = useState<SortField>('totalTokens');
  const [sortDir, setSortDir] = useState<'asc' | 'desc'>('desc');
  const [attempt, setAttempt] = useState(0);
  const [result, setResult] = useState<UsageMissionResult | null>(null);
  const [status, setStatus] = useState<'loading' | 'error' | 'ready'>('loading');
  const [error, setError] = useState('');
  const tokens = useMemo(() => new Intl.NumberFormat(), []);

  const selectionKey = `${selection.from}|${selection.to}|${selection.provider}|${selection.accountKey ?? ''}|${selection.hostId ?? ''}`;
  const [shownKey, setShownKey] = useState(selectionKey);
  if (shownKey !== selectionKey) {
    setShownKey(selectionKey);
    setPageNumber(1);
  }

  useEffect(() => {
    let cancelled = false;
    const filter: UsageMissionFilter = {
      from: selection.from,
      to: selection.to,
      provider: selection.provider,
      sortBy: sortField,
      sortDirection: sortDir === 'asc' ? 'ascending' : 'descending',
      pageNumber,
      pageSize,
    };
    if (selection.accountKey) filter.accountKey = selection.accountKey;
    if (selection.hostId) filter.hostId = selection.hostId;
    setStatus('loading');
    getUsageMissions(filter)
      .then((data) => {
        if (cancelled) return;
        setResult(data);
        setError('');
        setStatus('ready');
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : String(err));
        setStatus('error');
      });
    return () => { cancelled = true; };
    // selectionKey captures every selection field.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectionKey, sortField, sortDir, pageNumber, pageSize, revision, attempt]);

  const rows = status === 'ready' ? result?.objects ?? [] : [];
  const coverage = result?.coverage ?? null;

  const costTotals = useMemo(() => {
    const totals = new Map<string, number>();
    for (const row of rows) {
      if (row.reportedCost === null || row.reportedCost === undefined) continue;
      const currency = row.costCurrency ?? '';
      totals.set(currency, (totals.get(currency) ?? 0) + row.reportedCost);
    }
    return Array.from(totals.entries()).map(([currency, total]) => formatUsageCost(total, currency || null) as string);
  }, [rows]);

  const onSort = (field: string) => {
    if (field === sortField) setSortDir((d) => (d === 'asc' ? 'desc' : 'asc'));
    else { setSortField(field as SortField); setSortDir(field === 'name' ? 'asc' : 'desc'); }
    setPageNumber(1);
  };

  const columns: DataTableColumn<UsageMissionRow>[] = [
    {
      key: 'mission',
      label: t('Mission'),
      sortKey: 'name',
      required: true,
      interactive: true,
      render: (row) => (row.title === null && row.status === null
        ? <span className="mono" title={t('This mission was deleted; only its ID is kept.')}>{row.missionId}</span>
        : <Link to={`/missions/${encodeURIComponent(row.missionId)}`}>{row.title || row.missionId}</Link>),
    },
    { key: 'status', label: t('Status'), render: (row) => row.status ?? '-' },
    { key: 'vessel', label: t('Vessel'), render: (row) => row.vesselName ?? row.vesselId ?? '-' },
    { key: 'captain', label: t('Captain'), render: (row) => row.captainName ?? row.captainId ?? '-' },
    { key: 'completed', label: t('Completed'), sortKey: 'completion', render: (row) => formatDateTime(row.completedUtc) },
    { key: 'input', label: t('Input'), className: 'usage-num', render: (row) => tokens.format(row.inputTokens) },
    { key: 'output', label: t('Output'), className: 'usage-num', render: (row) => tokens.format(row.outputTokens) },
    { key: 'cached', label: t('Cached'), className: 'usage-num', render: (row) => tokens.format(row.cachedTokens) },
    { key: 'total', label: t('Total'), sortKey: 'totalTokens', className: 'usage-num', render: (row) => tokens.format(row.totalTokens) },
    {
      key: 'cost',
      label: t('Cost'),
      sortKey: 'cost',
      className: 'usage-num',
      cellTitle: (row) => t('Cost known for {{percent}}% of this mission\'s token records', { percent: Math.round((row.costCoverage ?? 0) * 100) }),
      render: (row) => {
        const text = formatUsageCost(row.reportedCost, row.costCurrency);
        return (
          <span>
            {text ?? <span className="text-dim">{t('Unknown')}</span>}
            {row.estimated && <span className="usage-chart-flag">{t('Estimated')}</span>}
          </span>
        );
      },
    },
  ];

  return (
    <section className="usage-drilldown card" aria-labelledby="usage-drilldown-title">
      <div className="usage-drilldown-head">
        <div>
          <h3 id="usage-drilldown-title">
            {t('Missions completed {{start}} to {{end}}', { start: formatDateTime(selection.from), end: formatDateTime(selection.to) })}
          </h3>
          <p className="text-dim usage-drilldown-scope">
            {t('Provider {{provider}}, account {{account}}, host {{host}}', {
              provider: selection.provider,
              account: selection.accountKey ? accountAlias(selection.accountKey) : t('all accounts'),
              host: selection.hostId || t('all hosts'),
            })}
          </p>
        </div>
        {onClose && <button type="button" className="btn btn-sm" onClick={onClose} title={t('Clear the selected bucket.')}>{t('Close')}</button>}
      </div>

      <p className="text-dim usage-coverage-note">
        {t('Limit utilization can change without any Armada mission completing, so the listed missions need not explain the percentage line.')}
      </p>
      {coverage && status === 'ready' && (
        <ul className="usage-coverage-list" aria-label={t('Coverage')}>
          {coverage.excludedUnattributedRecords > 0 && (
            <li>{t('{{count}} unattributed token records are excluded from this account-specific view.', { count: tokens.format(coverage.excludedUnattributedRecords) })}</li>
          )}
          {coverage.nonMissionRecords > 0 && (
            <li>{t('{{count}} non-mission token records (chat, planning, other work) are not listed as missions.', { count: tokens.format(coverage.nonMissionRecords) })}</li>
          )}
          <li>{t('Cost known for {{known}} of {{total}} mission token records.', { known: tokens.format(coverage.knownCostRecords), total: tokens.format(coverage.missionRecords) })}</li>
          {costTotals.length > 0 && <li>{t('Known cost on this page, per currency: {{totals}}', { totals: costTotals.join('; ') })}</li>}
          {coverage.warning && <li className="usage-coverage-warning">{coverage.warning}</li>}
        </ul>
      )}

      {status === 'error' && (
        <div className="usage-drilldown-error">
          <UsageState kind="error" onRetry={() => setAttempt((n) => n + 1)} />
          {error && <p className="text-dim">{error}</p>}
        </div>
      )}
      {status !== 'error' && (
        <DataTable
          tableKey="usage-missions"
          ariaLabel={t('Missions completed in the selected bucket')}
          columns={columns}
          rows={rows}
          rowKey={(row) => `${row.missionId}-${row.captainId ?? ''}`}
          sort={{ field: sortField, dir: sortDir, onSort }}
          placeholder={status === 'loading' ? <UsageState kind="loading" /> : undefined}
          emptyMessage={t('No completed missions in this bucket')}
          wrapClassName="usage-table-scroll"
          pagination={{
            pageNumber,
            pageSize,
            totalPages: result?.totalPages ?? 1,
            totalRecords: result?.totalRecords ?? 0,
            onPageChange: setPageNumber,
            onPageSizeChange: (size) => { setPageSize(Math.min(MAX_PAGE_SIZE, size)); setPageNumber(1); },
          }}
        />
      )}
    </section>
  );
}
