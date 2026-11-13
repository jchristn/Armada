import { useCallback, useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import type { FleetActionRun } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import CodeStatusBadge from '../shared/CodeStatusBadge';
import Markdown from '../shared/Markdown';
import { EmptyState, ErrorState, LoadingState } from '../shared/StateBlocks';
import { downloadBlob } from '../../lib/chartImage';
import { reasonLabel, targetStatusBadge } from '../../lib/fleetActionLabels';
import {
  REPORT_PAGE_SIZE,
  buildRunReportMarkdown,
  fetchAllReportEntries,
  fetchReportPage,
  reportFileName,
  type ReportEntry,
} from '../../lib/fleetActionReport';

interface RunReportProps {
  run: FleetActionRun;
}

/**
 * Report view of a Mission run: each vessel's final captain report rendered as Markdown under a vessel and status
 * heading, paged with Load more, plus a download of the whole report as one Markdown file.
 */
export default function RunReport({ run }: RunReportProps) {
  const { t } = useLocale();
  const [entries, setEntries] = useState<ReportEntry[]>([]);
  const [pageNumber, setPageNumber] = useState(0);
  const [totalPages, setTotalPages] = useState(1);
  const [totalRecords, setTotalRecords] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [downloading, setDownloading] = useState(false);
  const seq = useRef(0);

  const loadPage = useCallback(async (page: number) => {
    const current = ++seq.current;
    setLoading(true);
    try {
      const result = await fetchReportPage(run.id, page, REPORT_PAGE_SIZE);
      if (current !== seq.current) return;
      setEntries((prev) => (page === 1 ? result.entries : [...prev, ...result.entries.filter((e) => !prev.some((p) => p.id === e.id))]));
      setPageNumber(page);
      setTotalPages(result.totalPages);
      setTotalRecords(result.totalRecords);
      setError('');
    } catch (err: unknown) {
      if (current === seq.current) setError(err instanceof Error ? err.message : t('Failed to load the report.'));
    } finally {
      if (current === seq.current) setLoading(false);
    }
  }, [run.id, t]);

  useEffect(() => { void loadPage(1); }, [loadPage]);

  async function download() {
    setDownloading(true);
    try {
      const all = await fetchAllReportEntries(run.id, run.targetCount);
      const text = buildRunReportMarkdown(t, run, all);
      downloadBlob(new Blob([text], { type: 'text/markdown;charset=utf-8' }), reportFileName(run.id));
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to load the report.'));
    } finally {
      setDownloading(false);
    }
  }

  const hasMore = pageNumber > 0 && pageNumber < totalPages && entries.length < totalRecords;

  return (
    <div className="fleet-run-report">
      <div className="table-toolbar">
        <div className="table-toolbar-left">
          <h3 className="table-toolbar-title">{t('Report')}</h3>
          <span className="text-dim">{t('{count, plural, one {# vessel} other {# vessels}}', { count: totalRecords })}</span>
        </div>
        <div className="table-toolbar-right">
          <button type="button" className="btn btn-sm" onClick={() => void loadPage(1)} disabled={loading} title={t('Reload the vessel reports now.')}>{t('Refresh')}</button>
          <button type="button" className="btn btn-sm" onClick={() => void download()} disabled={downloading || run.targetCount === 0} title={t('Save every vessel report as one Markdown file.')}>
            {downloading ? t('Preparing...') : t('Download report (.md)')}
          </button>
        </div>
      </div>
      {error && <ErrorState message={error} onRetry={() => void loadPage(1)} />}
      {loading && entries.length === 0 && !error && <LoadingState />}
      {!loading && !error && entries.length === 0 && <EmptyState title={t('This run has no targets')} />}
      {entries.map((entry) => {
        const reason = reasonLabel(t, entry.skipReason, entry.failureReason);
        return (
          <section key={entry.id} className="card fleet-run-report-entry" aria-label={entry.vesselName}>
            <h4 className="fleet-run-report-heading">
              <Link to={`/vessels/${entry.vesselId}`}>{entry.vesselName}</Link>
              <CodeStatusBadge {...targetStatusBadge(t, entry.status)} />
              {entry.outputTruncated && <span className="tag review" title={t('The report was longer than the output limit, so only its end was kept.')}>{t('Truncated')}</span>}
            </h4>
            {entry.outputText && entry.outputText.trim()
              ? <div data-i18n-skip="true"><Markdown>{entry.outputText}</Markdown></div>
              : <p className="text-dim">{reason || (entry.status === 'Pending' || entry.status === 'Running' ? t('No report yet.') : t('No report.'))}</p>}
          </section>
        );
      })}
      {hasMore && (
        <div className="fleet-run-report-more">
          <button type="button" className="btn btn-sm" onClick={() => void loadPage(pageNumber + 1)} disabled={loading} title={t('Show the next vessel reports.')}>
            {loading ? t('Loading...') : t('Load more')}
          </button>
        </div>
      )}
    </div>
  );
}
