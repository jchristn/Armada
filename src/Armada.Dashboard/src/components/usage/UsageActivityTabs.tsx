import { useEffect, useState, type FormEvent } from 'react';
import { useSearchParams } from 'react-router-dom';
import { getUsageSeries } from '../../api/client';
import { useLocale } from '../../context/LocaleContext';
import { useUsageRevision } from '../../context/WebSocketContext';
import type { UsageCoverage, UsageSeries, UsageSeriesFilter } from '../../types/models';
import AccountLimitsTable from './AccountLimitsTable';
import UsageMissionDrilldown from './UsageMissionDrilldown';
import UsageOverTimeChart, { type UsageBucketSelection } from './UsageOverTimeChart';
import UsageState from './UsageState';

/** Overview ranges and their display buckets (the server accepts 5, 60 and 360 minutes only). */
export const USAGE_RANGES = [
  { value: '24h', label: 'Last 24 hours', hours: 24, bucketMinutes: 5 },
  { value: '7d', label: 'Last 7 days', hours: 168, bucketMinutes: 60 },
  { value: '30d', label: 'Last 30 days', hours: 720, bucketMinutes: 360 },
] as const;

type UsageRange = typeof USAGE_RANGES[number];
type MetricView = 'both' | 'limits' | 'tokens';

function rangeFor(value: string | null): UsageRange {
  return USAGE_RANGES.find((r) => r.value === value) ?? USAGE_RANGES[0];
}

function metricFor(value: string | null): MetricView {
  return value === 'limits' || value === 'tokens' ? value : 'both';
}

interface FilterField {
  param: string;
  label: string;
  title: string;
}

/** Provider/account/host (and optionally window) filters kept in the URL. Draft values are never reset by a failed request. */
function UsageFilterForm({ fields }: { fields: FilterField[] }) {
  const { t } = useLocale();
  const [params, setParams] = useSearchParams();
  const urlKey = fields.map((f) => params.get(f.param) ?? '').join('|');
  const [shownKey, setShownKey] = useState(urlKey);
  const [draft, setDraft] = useState<Record<string, string>>(() => Object.fromEntries(fields.map((f) => [f.param, params.get(f.param) ?? ''])));
  if (shownKey !== urlKey) {
    setShownKey(urlKey);
    setDraft(Object.fromEntries(fields.map((f) => [f.param, params.get(f.param) ?? ''])));
  }

  const apply = (event: FormEvent) => {
    event.preventDefault();
    const next = new URLSearchParams(params);
    for (const field of fields) {
      const value = (draft[field.param] ?? '').trim();
      if (value) next.set(field.param, value); else next.delete(field.param);
    }
    setParams(next);
  };

  const clear = () => {
    const next = new URLSearchParams(params);
    for (const field of fields) next.delete(field.param);
    setParams(next);
  };

  return (
    <form className="usage-filter-form" onSubmit={apply} aria-label={t('Usage filters')}>
      {fields.map((field) => (
        <label key={field.param} className="usage-filter-field" title={t(field.title)}>
          <span>{t(field.label)}</span>
          <input
            type="text"
            name={field.param}
            value={draft[field.param] ?? ''}
            onChange={(e) => setDraft((prev) => ({ ...prev, [field.param]: e.target.value }))}
          />
        </label>
      ))}
      <div className="usage-filter-actions">
        <button type="submit" className="btn btn-sm" title={t('Apply these filters.')}>{t('Apply')}</button>
        <button type="button" className="btn btn-sm" onClick={clear} title={t('Remove the provider, account and host filters.')}>{t('Clear filters')}</button>
      </div>
    </form>
  );
}

const IDENTITY_FIELDS: FilterField[] = [
  { param: 'provider', label: 'Provider', title: 'Provider name, for example anthropic or openai.' },
  { param: 'accountKey', label: 'Account', title: 'Pseudonymous account key from a usage row or the captain usage card.' },
  { param: 'hostId', label: 'Host', title: 'Admiral or Harbor host id.' },
];

const OVERVIEW_FIELDS: FilterField[] = [
  ...IDENTITY_FIELDS,
  { param: 'window', label: 'Window', title: 'Limit window name, for example 5h or weekly.' },
];

interface SeriesState {
  status: 'loading' | 'error' | 'ready';
  limits: UsageSeries[];
  tokens: UsageSeries[];
  limitCoverage: UsageCoverage | null;
  tokenCoverage: UsageCoverage | null;
  fromUtc: string;
  toUtc: string;
  error: string;
}

/** Overview tab: limit lines and token bars on one time scale, plus the exact-selection mission drill-down. */
export function UsageOverviewTab() {
  const { t } = useLocale();
  const [params, setParams] = useSearchParams();
  const revision = useUsageRevision();
  const [attempt, setAttempt] = useState(0);
  const range = rangeFor(params.get('range'));
  const metric = metricFor(params.get('metric'));
  const provider = params.get('provider') || '';
  const accountKey = params.get('accountKey') || '';
  const hostId = params.get('hostId') || '';
  const windowName = params.get('window') || '';
  const [state, setState] = useState<SeriesState>({ status: 'loading', limits: [], tokens: [], limitCoverage: null, tokenCoverage: null, fromUtc: '', toUtc: '', error: '' });

  const selFrom = params.get('from') || '';
  const selTo = params.get('to') || '';
  const selProvider = params.get('bucketProvider') || '';
  const selAccount = params.get('bucketAccountKey') || null;
  const selHost = params.get('bucketHostId') || null;
  const selected: UsageBucketSelection | null = selFrom && selTo && selProvider
    ? { bucketStartUtc: selFrom, bucketEndUtc: selTo, provider: selProvider, accountKey: selAccount, hostId: selHost }
    : null;

  useEffect(() => {
    let cancelled = false;
    const bucketMs = range.bucketMinutes * 60000;
    const toMs = Math.ceil(Date.now() / bucketMs) * bucketMs;
    const fromMs = toMs - range.hours * 3600000;
    const base = {
      from: new Date(fromMs).toISOString(),
      to: new Date(toMs).toISOString(),
      bucketMinutes: range.bucketMinutes,
      provider: provider || undefined,
      accountKey: accountKey || undefined,
      hostId: hostId || undefined,
    };
    const limitsFilter: UsageSeriesFilter = { ...base, metric: 'limits', groupBy: 'account', window: windowName || undefined };
    const tokensFilter: UsageSeriesFilter = { ...base, metric: 'tokens', groupBy: 'provider' };
    setState((prev) => ({ ...prev, status: 'loading' }));
    Promise.all([
      metric === 'tokens' ? Promise.resolve(null) : getUsageSeries(limitsFilter),
      metric === 'limits' ? Promise.resolve(null) : getUsageSeries(tokensFilter),
    ])
      .then(([limits, tokens]) => {
        if (cancelled) return;
        setState({
          status: 'ready',
          limits: limits?.series ?? [],
          tokens: tokens?.series ?? [],
          limitCoverage: limits?.coverage ?? null,
          tokenCoverage: tokens?.coverage ?? null,
          fromUtc: base.from,
          toUtc: base.to,
          error: '',
        });
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        setState((prev) => ({ ...prev, status: 'error', error: err instanceof Error ? err.message : String(err) }));
      });
    return () => { cancelled = true; };
  }, [range.bucketMinutes, range.hours, metric, provider, accountKey, hostId, windowName, revision, attempt]);

  const setParam = (name: string, value: string | null) => {
    const next = new URLSearchParams(params);
    if (value) next.set(name, value); else next.delete(name);
    setParams(next);
  };

  const select = (selection: UsageBucketSelection) => {
    const next = new URLSearchParams(params);
    next.set('from', selection.bucketStartUtc);
    next.set('to', selection.bucketEndUtc);
    next.set('bucketProvider', selection.provider);
    if (selection.accountKey) next.set('bucketAccountKey', selection.accountKey); else next.delete('bucketAccountKey');
    if (selection.hostId) next.set('bucketHostId', selection.hostId); else next.delete('bucketHostId');
    setParams(next);
  };

  const clearSelection = () => {
    const next = new URLSearchParams(params);
    for (const name of ['from', 'to', 'bucketProvider', 'bucketAccountKey', 'bucketHostId']) next.delete(name);
    setParams(next);
  };

  const truncated = !!(state.limitCoverage?.seriesTruncated || state.tokenCoverage?.seriesTruncated);
  const warnings = [state.limitCoverage?.warning, state.tokenCoverage?.warning].filter((w): w is string => !!w);

  return (
    <div className="usage-overview">
      <div className="usage-toolbar">
        <label className="usage-filter-field" title={t('Time range of both panels. 24 hours uses 5-minute buckets, 7 days hourly and 30 days 6-hour buckets.')}>
          <span>{t('Range')}</span>
          <select value={range.value} onChange={(e) => setParam('range', e.target.value === USAGE_RANGES[0].value ? null : e.target.value)}>
            {USAGE_RANGES.map((r) => <option key={r.value} value={r.value}>{t(r.label)}</option>)}
          </select>
        </label>
        <label className="usage-filter-field" title={t('Which panels to show.')}>
          <span>{t('Show')}</span>
          <select value={metric} onChange={(e) => setParam('metric', e.target.value === 'both' ? null : e.target.value)}>
            <option value="both">{t('Limits and tokens')}</option>
            <option value="limits">{t('Limits only')}</option>
            <option value="tokens">{t('Tokens only')}</option>
          </select>
        </label>
      </div>
      <UsageFilterForm fields={OVERVIEW_FIELDS} />

      {state.status === 'error' && (
        <div className="usage-overview-error">
          <UsageState kind="error" onRetry={() => setAttempt((n) => n + 1)} />
          {state.error && <p className="text-dim">{state.error}</p>}
        </div>
      )}
      {state.status === 'loading' && !state.fromUtc && <UsageState kind="loading" />}
      {state.fromUtc && state.status !== 'error' && (
        <>
          {truncated && <p className="usage-table-notice text-dim" role="status">{t('More series exist than the chart shows. Narrow the provider, account or host filter.')}</p>}
          {warnings.map((warning) => <p key={warning} className="usage-table-notice text-dim">{warning}</p>)}
          <UsageOverTimeChart
            limits={state.limits}
            tokens={state.tokens}
            fromUtc={state.fromUtc}
            toUtc={state.toUtc}
            selected={selected}
            onSelectBucket={select}
            show={metric}
          />
        </>
      )}
      {selected && (
        <UsageMissionDrilldown
          selection={{ from: selected.bucketStartUtc, to: selected.bucketEndUtc, provider: selected.provider, accountKey: selected.accountKey, hostId: selected.hostId }}
          onClose={clearSelection}
        />
      )}
    </div>
  );
}

/** Limits tab: filters plus the account and host table. */
export function UsageLimitsTab() {
  const [params] = useSearchParams();
  return (
    <div className="usage-limits">
      <UsageFilterForm fields={IDENTITY_FIELDS} />
      <AccountLimitsTable
        provider={params.get('provider') || undefined}
        accountKey={params.get('accountKey') || undefined}
        hostId={params.get('hostId') || undefined}
      />
    </div>
  );
}
