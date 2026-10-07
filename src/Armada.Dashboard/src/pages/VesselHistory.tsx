import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { getVessel, getVesselBranches, getVesselCommitActivity, getVesselCommits } from '../api/client';
import type { Vessel, VesselCommit, VesselCommitActivity } from '../types/models';
import { useLocale } from '../context/LocaleContext';
import PageHeader from '../components/shared/PageHeader';
import { EmptyState, ErrorState, LoadingState } from '../components/shared/StateBlocks';
import CommitHeatmap from '../components/vessels/history/CommitHeatmap';
import CommitTimeline from '../components/vessels/history/CommitTimeline';
import {
  appendCommits,
  beforeForDay,
  browserUtcOffsetMinutes,
  formatIsoDay,
  parseIsoDate,
  rangeForYear,
  selectableYears,
  todayIsoDate,
} from '../lib/vesselHistory';
import '../components/vessels/history/vesselHistory.css';

/** Commits requested per page. */
export const HISTORY_PAGE_SIZE = 50;

function errorMessage(err: unknown, fallback: string): string {
  return err instanceof Error && err.message ? err.message : fallback;
}

/**
 * Vessel history: a commit-activity heatmap for the selected branch plus an endless, day-grouped
 * commit timeline. Selecting a heatmap day (or jumping to a date) restarts the list at that day.
 */
export default function VesselHistory() {
  const { id } = useParams<{ id: string }>();
  const vesselId = id ?? '';
  const navigate = useNavigate();
  const { t, locale } = useLocale();
  const today = useMemo(() => todayIsoDate(), []);
  const utcOffsetMinutes = useMemo(() => browserUtcOffsetMinutes(), []);

  const [vessel, setVessel] = useState<Vessel | null>(null);
  const [vesselLoading, setVesselLoading] = useState(true);
  const [vesselError, setVesselError] = useState('');
  const [vesselReloadKey, setVesselReloadKey] = useState(0);
  const [branches, setBranches] = useState<string[]>([]);
  const [branch, setBranch] = useState<string | null>(null);

  const [year, setYear] = useState<number | null>(null);
  const [activity, setActivity] = useState<VesselCommitActivity | null>(null);
  const [activityLoading, setActivityLoading] = useState(false);
  const [activityError, setActivityError] = useState('');
  const [activityReloadKey, setActivityReloadKey] = useState(0);
  const [firstCommitUtc, setFirstCommitUtc] = useState<string | null>(null);

  const [selectedDate, setSelectedDate] = useState<string | null>(null);
  const [jumpValue, setJumpValue] = useState('');

  const [commits, setCommits] = useState<VesselCommit[]>([]);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [listLoading, setListLoading] = useState(false);
  const [listLoaded, setListLoaded] = useState(false);
  const [listError, setListError] = useState('');
  const [listRepoError, setListRepoError] = useState('');
  const [listReloadKey, setListReloadKey] = useState(0);
  const [loadingMore, setLoadingMore] = useState(false);
  const [loadMoreError, setLoadMoreError] = useState('');

  // Each list query (branch / jump target / retry) gets a generation; responses for an older
  // generation are dropped so a slow page from a previous branch never lands in the new list.
  const generationRef = useRef(0);
  const cursorRef = useRef<string | null>(null);
  const loadingMoreRef = useRef(false);
  const scrollOnLoadRef = useRef(false);
  const listTopRef = useRef<HTMLDivElement>(null);
  const sentinelRef = useRef<HTMLDivElement>(null);

  const range = useMemo(() => rangeForYear(year, today), [year, today]);
  const years = useMemo(() => selectableYears(firstCommitUtc, today), [firstCommitUtc, today]);
  const yearOptions = useMemo<Array<number | null>>(() => [null, ...years], [years]);
  const yearIndex = Math.max(0, yearOptions.indexOf(year));

  // Vessel + branches.
  useEffect(() => {
    if (!vesselId) return;
    let cancelled = false;
    setVesselLoading(true);
    setVesselError('');
    Promise.all([
      getVessel(vesselId),
      getVesselBranches(vesselId).catch(() => null),
    ]).then(([loaded, branchResult]) => {
      if (cancelled) return;
      const defaultBranch = loaded.defaultBranch || branchResult?.defaultBranch || 'main';
      const names = (branchResult?.branches ?? []).map((b) => b.name).filter((name) => !!name);
      if (!names.includes(defaultBranch)) names.unshift(defaultBranch);
      setVessel(loaded);
      setBranches(names);
      setBranch((current) => current ?? defaultBranch);
    }).catch((err: unknown) => {
      if (!cancelled) setVesselError(errorMessage(err, t('Failed to load vessel.')));
    }).finally(() => {
      if (!cancelled) setVesselLoading(false);
    });
    return () => { cancelled = true; };
  }, [vesselId, vesselReloadKey, t]);

  // Heatmap.
  useEffect(() => {
    if (!vesselId || !branch) return;
    let cancelled = false;
    setActivityLoading(true);
    setActivityError('');
    getVesselCommitActivity(vesselId, { branch, from: range.from, to: range.to, utcOffsetMinutes })
      .then((result) => {
        if (cancelled) return;
        setActivity(result);
        if (!result.error) setFirstCommitUtc(result.firstCommitUtc ?? null);
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        setActivity(null);
        setActivityError(errorMessage(err, t('Failed to load commit activity.')));
      })
      .finally(() => {
        if (!cancelled) setActivityLoading(false);
      });
    return () => { cancelled = true; };
  }, [vesselId, branch, range.from, range.to, utcOffsetMinutes, activityReloadKey, t]);

  // First page of the commit list.
  useEffect(() => {
    if (!vesselId || !branch) return;
    generationRef.current += 1;
    const generation = generationRef.current;
    cursorRef.current = null;
    loadingMoreRef.current = false;
    setCommits([]);
    setNextCursor(null);
    setListLoaded(false);
    setListError('');
    setListRepoError('');
    setLoadMoreError('');
    setLoadingMore(false);
    setListLoading(true);
    getVesselCommits(vesselId, {
      branch,
      before: selectedDate ? beforeForDay(selectedDate) : null,
      limit: HISTORY_PAGE_SIZE,
    }).then((page) => {
      if (generation !== generationRef.current) return;
      if (page.error) {
        setListRepoError(page.error);
        return;
      }
      cursorRef.current = page.nextCursor ?? null;
      setCommits(page.commits ?? []);
      setNextCursor(page.nextCursor ?? null);
      setListLoaded(true);
      if (scrollOnLoadRef.current) {
        scrollOnLoadRef.current = false;
        listTopRef.current?.scrollIntoView?.({ behavior: 'smooth', block: 'start' });
      }
    }).catch((err: unknown) => {
      if (generation !== generationRef.current) return;
      setListError(errorMessage(err, t('Failed to load commits.')));
    }).finally(() => {
      if (generation === generationRef.current) setListLoading(false);
    });
  }, [vesselId, branch, selectedDate, listReloadKey, t]);

  const loadMore = useCallback(() => {
    const cursor = cursorRef.current;
    if (!cursor || loadingMoreRef.current) return;
    const generation = generationRef.current;
    loadingMoreRef.current = true;
    setLoadingMore(true);
    setLoadMoreError('');
    getVesselCommits(vesselId, { cursor, limit: HISTORY_PAGE_SIZE })
      .then((page) => {
        if (generation !== generationRef.current) return;
        if (page.error) {
          setLoadMoreError(page.error);
          return;
        }
        cursorRef.current = page.nextCursor ?? null;
        setCommits((prev) => appendCommits(prev, page.commits ?? []));
        setNextCursor(page.nextCursor ?? null);
      })
      .catch((err: unknown) => {
        if (generation !== generationRef.current) return;
        setLoadMoreError(errorMessage(err, t('Failed to load more commits.')));
      })
      .finally(() => {
        if (generation !== generationRef.current) return;
        loadingMoreRef.current = false;
        setLoadingMore(false);
      });
  }, [vesselId, t]);

  // Endless scroll: load the next page when the sentinel nears the viewport. The Load more button
  // below is the fallback where IntersectionObserver is unavailable.
  useEffect(() => {
    const sentinel = sentinelRef.current;
    if (!sentinel || !nextCursor || loadMoreError || typeof IntersectionObserver === 'undefined') return;
    const observer = new IntersectionObserver((entries) => {
      if (entries.some((entry) => entry.isIntersecting)) loadMore();
    }, { rootMargin: '400px 0px' });
    observer.observe(sentinel);
    return () => observer.disconnect();
  }, [nextCursor, loadMore, loadMoreError, listLoaded]);

  function selectDate(date: string) {
    scrollOnLoadRef.current = true;
    setSelectedDate(date);
    setJumpValue(date);
    if (date < range.from || date > range.to) {
      const target = Number(date.slice(0, 4));
      const lastYear = rangeForYear(null, today);
      setYear(date >= lastYear.from ? null : target);
    }
  }

  function handleJump(event: FormEvent) {
    event.preventDefault();
    if (!parseIsoDate(jumpValue)) return;
    selectDate(jumpValue > today ? today : jumpValue);
  }

  function resetToLatest() {
    scrollOnLoadRef.current = true;
    setSelectedDate(null);
    setJumpValue('');
  }

  function changeBranch(next: string) {
    setActivity(null);
    setFirstCommitUtc(null);
    setBranch(next);
  }

  if (!vesselId) return <p className="text-dim">{t('Vessel not found.')}</p>;
  if (vesselLoading && !vessel) return <LoadingState />;
  if (!vessel) {
    return <ErrorState message={vesselError || t('Vessel not found.')} onRetry={() => setVesselReloadKey((k) => k + 1)} />;
  }

  const repoError = activity?.error || listRepoError;
  const rangeLabel = year === null ? t('the last year') : String(year);
  const selectedLabel = selectedDate ? formatIsoDay(locale, selectedDate, { year: 'numeric', month: 'long', day: 'numeric' }) : '';

  return (
    <div className="vessel-history">
      <PageHeader
        breadcrumb={
          <>
            <Link to="/vessels">{t('Vessels')}</Link> <span className="breadcrumb-sep">&gt;</span>{' '}
            <Link to={`/vessels/${vessel.id}`}>{vessel.name}</Link> <span className="breadcrumb-sep">&gt;</span>{' '}
            <span>{t('History')}</span>
          </>
        }
        title={t('History')}
        subtitle={t('Commit activity and history for {{name}}.', { name: vessel.name })}
        actions={
          <button type="button" className="btn btn-sm" onClick={() => navigate(`/vessels/${vessel.id}`)}>
            {t('Back To Vessel')}
          </button>
        }
      />

      <div className="vhist-toolbar card">
        <label className="vhist-field">
          <span>{t('Branch')}</span>
          <select value={branch ?? ''} onChange={(e) => changeBranch(e.target.value)} aria-label={t('Branch')}>
            {branches.map((name) => <option key={name} value={name}>{name}</option>)}
          </select>
        </label>
        <div className="vhist-field vhist-year-nav" role="group" aria-label={t('Heatmap range')}>
          <span>{t('Range')}</span>
          <div className="inline-actions">
            <button
              type="button"
              className="btn btn-sm"
              onClick={() => setYear(yearOptions[yearIndex + 1] ?? null)}
              disabled={yearIndex >= yearOptions.length - 1}
              aria-label={t('Previous year')}
              title={t('Previous year')}
            >
              &lt;
            </button>
            <select
              value={year === null ? '' : String(year)}
              onChange={(e) => setYear(e.target.value ? Number(e.target.value) : null)}
              aria-label={t('Year')}
            >
              <option value="">{t('Last year')}</option>
              {years.map((y) => <option key={y} value={String(y)}>{y}</option>)}
            </select>
            <button
              type="button"
              className="btn btn-sm"
              onClick={() => setYear(yearOptions[yearIndex - 1] ?? null)}
              disabled={yearIndex <= 0}
              aria-label={t('Next year')}
              title={t('Next year')}
            >
              &gt;
            </button>
          </div>
        </div>
        <form className="vhist-field vhist-jump" onSubmit={handleJump}>
          <label htmlFor="vhist-jump-date">{t('Jump to date')}</label>
          <div className="inline-actions">
            <input
              id="vhist-jump-date"
              type="date"
              max={today}
              value={jumpValue}
              onChange={(e) => setJumpValue(e.target.value)}
            />
            <button type="submit" className="btn btn-sm" disabled={!parseIsoDate(jumpValue)}>{t('Go')}</button>
            <button type="button" className="btn btn-sm" onClick={resetToLatest} disabled={!selectedDate}>{t('Latest')}</button>
          </div>
        </form>
      </div>

      {repoError ? (
        <EmptyState title={t('History is not available')}>
          <p>{t('The repository for this vessel could not be read. It may not be cloned yet, or the branch may not exist.')}</p>
          <p className="mono">{repoError}</p>
        </EmptyState>
      ) : (
        <>
          <div className="card vhist-heatmap-card">
            {activityError ? (
              <ErrorState message={activityError} onRetry={() => setActivityReloadKey((k) => k + 1)} />
            ) : activity ? (
              <div className={activityLoading ? 'vhist-stale' : undefined} aria-busy={activityLoading}>
                <CommitHeatmap activity={activity} selectedDate={selectedDate} onSelectDate={selectDate} rangeLabel={rangeLabel} />
              </div>
            ) : (
              <LoadingState label={t('Loading activity...')} />
            )}
          </div>

          <div ref={listTopRef} className="vhist-list-top">
            {selectedDate && (
              <div className="vhist-jump-banner">
                <span>{t('Showing commits on or before {{date}}.', { date: selectedLabel })}</span>
                <button type="button" className="btn btn-sm" onClick={resetToLatest}>{t('Latest')}</button>
              </div>
            )}
          </div>

          {listError ? (
            <ErrorState message={listError} onRetry={() => setListReloadKey((k) => k + 1)} />
          ) : listLoading ? (
            <LoadingState label={t('Loading commits...')} />
          ) : listLoaded && commits.length === 0 ? (
            <EmptyState title={selectedDate ? t('No commits on or before this date') : t('No commits yet')}>
              {selectedDate ? t('Pick a later date or return to the latest commits.') : t('This branch has no commits.')}
            </EmptyState>
          ) : (
            <>
              <CommitTimeline commits={commits} selectedDate={selectedDate} />
              <div ref={sentinelRef} className="vhist-sentinel" aria-hidden="true" />
              <div className="vhist-list-footer">
                {loadMoreError && <ErrorState message={loadMoreError} />}
                {nextCursor ? (
                  <button type="button" className="btn btn-sm" onClick={loadMore} disabled={loadingMore}>
                    {loadingMore ? t('Loading...') : t('Load more')}
                  </button>
                ) : listLoaded && commits.length > 0 ? (
                  <span className="text-dim vhist-end">{t('End of history')}</span>
                ) : null}
              </div>
            </>
          )}
        </>
      )}
    </div>
  );
}
