import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { getRequestHistorySummary } from '../../api/client';
import { useLocale } from '../../context/LocaleContext';
import { useUsageRevision } from '../../context/WebSocketContext';
import { ACTIVITY_RANGE_OPTIONS, buildRequestHistorySummaryQuery, defaultRequestHistoryFilters, type ActivityRangeId } from '../../lib/requestHistory';
import type { RequestHistorySummaryResult } from '../../types/models';
import RequestHistoryChart from './RequestHistoryChart';
import TokenActivityHeatmap from './TokenActivityHeatmap';

/*
 * Usage and activity over time on the Overview: the API request chart (requests per bucket, successes and failures)
 * with its range tabs, and the token usage heatmap. Each links to its full Activity tab. The request chart hides itself
 * when the signed-in user cannot read request history.
 */
export default function OverviewActivityCharts() {
  const { t, formatDateTime } = useLocale();
  const usageRevision = useUsageRevision();
  const [range, setRange] = useState<ActivityRangeId>('lastDay');
  const [summary, setSummary] = useState<RequestHistorySummaryResult | null>(null);
  const [loading, setLoading] = useState(true);
  const [unavailable, setUnavailable] = useState(false);
  const query = useMemo(() => buildRequestHistorySummaryQuery(defaultRequestHistoryFilters(), range), [range]);

  const load = useCallback(async () => {
    try {
      setLoading(true);
      setSummary(await getRequestHistorySummary(query));
      setUnavailable(false);
    } catch {
      setUnavailable(true);
    } finally {
      setLoading(false);
    }
  }, [query]);

  useEffect(() => {
    void load();
  }, [load, usageRevision]);

  return (
    <div className="overview-activity-charts" style={{ display: 'grid', gap: '1rem', marginTop: '1.5rem' }}>
      {!unavailable && (
        <div className="card request-history-activity-card">
          <div className="request-card-header">
            <div>
              <h3 title={t('Requests the Admiral answered over time, split into successes and failures. Open API Requests for every request and its details.')}>{t('API Requests')}</h3>
              <p className="text-dim">
                {t('Bucketed request volume with success and failure breakdown.')}{' '}
                {summary && <span>{t('{{total}} requests, {{failures}} failed', { total: (summary.totalCount || 0).toLocaleString(), failures: (summary.failureCount || 0).toLocaleString() })}</span>}{' '}
                <Link to="/activity?source=requests" title={t('Open API Requests: every request with its status, timing and caller.')}>{t('Open API Requests')}</Link>
              </p>
            </div>
            <div className="request-range-tabs" role="group" aria-label={t('Activity range')}>
              {ACTIVITY_RANGE_OPTIONS.map((option) => (
                <button
                  key={option.id}
                  type="button"
                  className={`request-range-tab${range === option.id ? ' active' : ''}`}
                  aria-pressed={range === option.id}
                  onClick={() => setRange(option.id)}
                  title={t('Time range for the chart.')}
                >
                  {t(option.label)}
                </button>
              ))}
            </div>
          </div>
          <div className="request-card-body">
            {loading && !summary
              ? <div className="request-history-empty">{t('Loading summary...')}</div>
              : <RequestHistoryChart summary={summary} rangeId={range} t={t} formatDateTime={formatDateTime} />}
          </div>
        </div>
      )}
      <div>
        <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: '0.25rem' }}>
          <Link to="/activity?source=tokens" title={t('Open Token Usage: tokens and cost by captain, model and Harbor, with filters.')}>{t('Open Token Usage')}</Link>
        </div>
        <TokenActivityHeatmap />
      </div>
    </div>
  );
}
