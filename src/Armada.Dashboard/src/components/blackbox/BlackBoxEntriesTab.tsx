import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { enumerateBlackBox, searchBlackBox } from '../../api/client';
import type { BlackBoxEntrySummary, BlackBoxEnumerateQuery } from '../../types/blackbox';
import type { Vessel } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import {
  BLACKBOX_CAPTURE_MODES,
  BLACKBOX_KINDS,
  BLACKBOX_PHASES,
  BLACKBOX_RESOLUTIONS,
  BLACKBOX_STATES,
  BLACKBOX_STEPS,
  BLACKBOX_SUBJECT_TYPES,
  BLACKBOX_TRIAGES,
  activeBlackBoxFilters,
  loadBlackBoxSavedFilters,
  storeBlackBoxSavedFilters,
  type BlackBoxSavedFilter,
} from '../../lib/blackbox';
import { useBlackBoxLive } from '../../lib/useBlackBoxLive';
import DataTable, { type DataTableColumn } from '../shared/DataTable';
import { EmptyState, ErrorState, LoadingState } from '../shared/StateBlocks';

type Filters = BlackBoxEnumerateQuery & { search?: string };

/** Filters that can arrive on the URL (links from the panel, fingerprints and hotspots). */
const URL_FILTERS = ['vesselId', 'voyageId', 'missionId', 'captainId', 'fingerprint', 'path', 'phase', 'step', 'kind', 'triage', 'resolution'] as const;

function filtersFromUrl(params: URLSearchParams): Filters {
  const result: Record<string, string> = {};
  for (const key of URL_FILTERS) {
    const value = params.get(key);
    if (value) result[key] = value;
  }
  return result as Filters;
}

function dateToUtc(value: string | undefined, endOfDay: boolean): string | undefined {
  if (!value) return undefined;
  if (value.includes('T')) return value;
  return `${value}T${endOfDay ? '23:59:59' : '00:00:00'}Z`;
}

function boolFilter(value: string): boolean | undefined {
  if (value === 'true') return true;
  if (value === 'false') return false;
  return undefined;
}

/**
 * Entries tab of the Black Box page: one page of entries at a time with every filter of the enumerate route
 * (vessel, voyage, mission, captain, phase, step, kind, triage, resolution, state, capture mode, subject type,
 * fingerprint, pinned, current, path, created after and before) plus full-text search, and named filters saved in this
 * browser. Follows the blackbox.* events live.
 */
export default function BlackBoxEntriesTab({ vessels }: { vessels: Vessel[] }) {
  const { t, formatDateTime } = useLocale();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const initial = useMemo(() => filtersFromUrl(searchParams), [searchParams]);
  const [draft, setDraft] = useState<Filters>(initial);
  const [applied, setApplied] = useState<Filters>(initial);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [rows, setRows] = useState<BlackBoxEntrySummary[]>([]);
  const [totalRecords, setTotalRecords] = useState(0);
  const [totalPages, setTotalPages] = useState(1);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [saved, setSaved] = useState<BlackBoxSavedFilter[]>(() => loadBlackBoxSavedFilters());
  const [saveName, setSaveName] = useState('');

  // A link with new URL filters (a fingerprint from the Fingerprints tab) replaces the current filters.
  useEffect(() => { setDraft(initial); setApplied(initial); setPageNumber(1); }, [initial]);

  const vesselName = useMemo(() => new Map(vessels.map((v) => [v.id, v.name])), [vessels]);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const query: BlackBoxEnumerateQuery = {
        ...activeBlackBoxFilters(applied),
        createdAfter: dateToUtc(applied.createdAfter, false),
        createdBefore: dateToUtc(applied.createdBefore, true),
        pageNumber,
        pageSize,
      };
      delete (query as Filters).search;
      const search = applied.search?.trim();
      const result = search ? await searchBlackBox({ ...query, query: search }) : await enumerateBlackBox(query);
      setRows(result.objects ?? []);
      setTotalRecords(result.totalRecords ?? 0);
      setTotalPages(Math.max(1, result.totalPages ?? 1));
      setError('');
    } catch (err: unknown) {
      setError(t('Black box entries could not be loaded: {{message}}', { message: err instanceof Error ? err.message : String(err) }));
    } finally {
      setLoading(false);
    }
  }, [applied, pageNumber, pageSize, t]);

  useEffect(() => { void load(); }, [load]);
  useBlackBoxLive([], () => { void load(); });

  function set<K extends keyof Filters>(key: K, value: Filters[K] | '') {
    setDraft((d) => {
      const next = { ...d } as Record<string, unknown>;
      if (value === '' || value === undefined) delete next[key as string];
      else next[key as string] = value;
      return next as Filters;
    });
  }

  function apply(next?: Filters) {
    const value = next ?? draft;
    setDraft(value);
    setApplied(activeBlackBoxFilters(value));
    setPageNumber(1);
  }

  function saveCurrent() {
    const name = saveName.trim();
    if (!name) return;
    const next = [...saved.filter((f) => f.name !== name), { name, query: activeBlackBoxFilters(draft) }];
    setSaved(next);
    storeBlackBoxSavedFilters(next);
    setSaveName('');
  }

  function removeSaved(name: string) {
    const next = saved.filter((f) => f.name !== name);
    setSaved(next);
    storeBlackBoxSavedFilters(next);
  }

  const text = (key: keyof Filters, label: string, placeholder?: string) => (
    <label>
      {label}
      <input value={(draft[key] as string | undefined) ?? ''} placeholder={placeholder} onChange={(e) => set(key, e.target.value)} onKeyDown={(e) => { if (e.key === 'Enter') apply(); }} aria-label={label} />
    </label>
  );
  const select = (key: keyof Filters, label: string, values: readonly string[]) => (
    <label>
      {label}
      <select value={(draft[key] as string | undefined) ?? ''} onChange={(e) => set(key, e.target.value as never)} aria-label={label}>
        <option value="">{t('Any')}</option>
        {values.map((v) => <option key={v} value={v}>{t(v)}</option>)}
      </select>
    </label>
  );
  const yesNo = (key: 'pinned' | 'isCurrent', label: string) => (
    <label>
      {label}
      <select value={draft[key] === undefined ? '' : String(draft[key])} onChange={(e) => set(key, boolFilter(e.target.value) ?? '')} aria-label={label}>
        <option value="">{t('Any')}</option>
        <option value="true">{t('Yes')}</option>
        <option value="false">{t('No')}</option>
      </select>
    </label>
  );

  const columns: DataTableColumn<BlackBoxEntrySummary>[] = [
    { key: 'id', label: t('ID'), required: true, render: (r) => <Link to={`/blackbox/${encodeURIComponent(r.id)}`} className="mono">{r.id}</Link>, interactive: true },
    { key: 'subject', label: t('Subject'), render: (r) => r.missionId
      ? <Link to={`/missions/${r.missionId}`}>{r.missionTitle || r.missionId}</Link>
      : r.voyageId ? <Link to={`/voyages/${r.voyageId}`} className="mono">{r.voyageId}</Link> : '-', interactive: true },
    { key: 'vessel', label: t('Vessel'), render: (r) => r.vesselName || (r.vesselId ? vesselName.get(r.vesselId) ?? r.vesselId : '-') },
    { key: 'where', label: t('Phase / step'), render: (r) => `${t(r.phase)} / ${t(r.step)}` },
    { key: 'kind', label: t('Kind'), render: (r) => r.failureKind ? t(r.failureKind) : '-' },
    { key: 'triage', label: t('Triage'), render: (r) => t(r.triage) },
    { key: 'resolution', label: t('Resolution'), render: (r) => t(r.resolution) },
    { key: 'state', label: t('State'), render: (r) => t(r.state) },
    { key: 'attempt', label: t('Attempt'), render: (r) => r.attempt },
    { key: 'created', label: t('Created'), render: (r) => formatDateTime(r.createdUtc) },
    { key: 'reason', label: t('Reason'), render: (r) => r.reasonExcerpt || '-', defaultHidden: true },
    { key: 'fingerprint', label: t('Fingerprint'), render: (r) => r.fingerprint ? <span className="mono" title={r.fingerprint}>{r.fingerprint.slice(0, 12)}</span> : '-', defaultHidden: true },
    { key: 'captureMode', label: t('Capture mode'), render: (r) => t(r.captureMode), defaultHidden: true },
    { key: 'pinned', label: t('Pinned'), render: (r) => r.pinned ? t('Yes') : t('No'), defaultHidden: true },
  ];

  return (
    <div>
      <div className="blackbox-filters" role="search" aria-label={t('Black box filters')}>
        {text('search', t('Search'), t('Reason, summary or notes'))}
        <label>
          {t('Vessel')}
          <select value={draft.vesselId ?? ''} onChange={(e) => set('vesselId', e.target.value)} aria-label={t('Vessel')}>
            <option value="">{t('Any')}</option>
            {vessels.map((v) => <option key={v.id} value={v.id}>{v.name}</option>)}
          </select>
        </label>
        {text('voyageId', t('Voyage ID'), 'vyg_')}
        {text('missionId', t('Mission ID'), 'msn_')}
        {text('captainId', t('Captain ID'), 'cpt_')}
        {select('phase', t('Phase'), BLACKBOX_PHASES)}
        {select('step', t('Step'), BLACKBOX_STEPS)}
        {select('kind', t('Kind'), BLACKBOX_KINDS)}
        {select('triage', t('Triage'), BLACKBOX_TRIAGES)}
        {select('resolution', t('Resolution'), BLACKBOX_RESOLUTIONS)}
        {select('state', t('State'), BLACKBOX_STATES)}
        {select('captureMode', t('Capture mode'), BLACKBOX_CAPTURE_MODES)}
        {select('subjectType', t('Subject type'), BLACKBOX_SUBJECT_TYPES)}
        {text('fingerprint', t('Fingerprint'))}
        {text('path', t('File path'), 'src/')}
        {yesNo('pinned', t('Pinned'))}
        {yesNo('isCurrent', t('Current attempt'))}
        <label>{t('Created after')}<input type="date" value={draft.createdAfter ?? ''} onChange={(e) => set('createdAfter', e.target.value)} aria-label={t('Created after')} /></label>
        <label>{t('Created before')}<input type="date" value={draft.createdBefore ?? ''} onChange={(e) => set('createdBefore', e.target.value)} aria-label={t('Created before')} /></label>
      </div>
      <div className="blackbox-saved">
        <button type="button" className="btn btn-sm btn-primary" onClick={() => apply()} title={t('Show the entries that match these filters.')}>{t('Apply filters')}</button>
        <button type="button" className="btn btn-sm" onClick={() => apply({})} title={t('Remove every filter.')}>{t('Clear')}</button>
        <span className="text-dim">|</span>
        <select aria-label={t('Saved filters')} value="" onChange={(e) => { const f = saved.find((s) => s.name === e.target.value); if (f) apply(f.query); }} title={t('Filters saved in this browser.')}>
          <option value="">{t('Saved filters ({{count}})', { count: saved.length })}</option>
          {saved.map((f) => <option key={f.name} value={f.name}>{f.name}</option>)}
        </select>
        <input value={saveName} onChange={(e) => setSaveName(e.target.value)} placeholder={t('Name for these filters')} aria-label={t('Name for these filters')} style={{ maxWidth: '14rem' }} />
        <button type="button" className="btn btn-sm" disabled={!saveName.trim()} onClick={saveCurrent} title={t('Save the current filters in this browser under this name.')}>{t('Save filters')}</button>
        {saved.map((f) => (
          <button key={f.name} type="button" className="btn btn-sm" onClick={() => removeSaved(f.name)} title={t('Delete the saved filters {{name}}.', { name: f.name })} aria-label={t('Delete the saved filters {{name}}.', { name: f.name })}>&times; {f.name}</button>
        ))}
      </div>

      {error ? <ErrorState message={error} onRetry={() => void load()} /> : (
        <DataTable
          tableKey="blackbox-entries"
          columns={columns}
          rows={rows}
          rowKey={(r) => r.id}
          onRowClick={(r) => navigate(`/blackbox/${encodeURIComponent(r.id)}`)}
          placeholder={loading && rows.length === 0 ? <LoadingState /> : rows.length === 0 ? (
            <EmptyState title={t('No black box entries')}>{Object.keys(applied).length ? t('No entry matches these filters.') : t('Armada records an entry here whenever a mission or voyage fails.')}</EmptyState>
          ) : undefined}
          pagination={{
            pageNumber, pageSize, totalPages, totalRecords,
            onPageChange: (p) => setPageNumber(p),
            onPageSizeChange: (s) => { setPageSize(s); setPageNumber(1); },
          }}
          onRefresh={load}
          refreshTitle="Reload the black box entries from the server now."
          ariaLabel={t('Black box entries')}
        />
      )}
    </div>
  );
}
