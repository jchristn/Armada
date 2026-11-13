import { useEffect, useMemo, useRef, useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { enumerateHistoryTimeline, listObjectives, listVessels, deleteRequestHistoryEntry } from '../api/client';
import type { HistoricalTimelineEntry, HistoricalTimelineQuery, Objective, Vessel } from '../types/models';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import ErrorModal from '../components/shared/ErrorModal';
import JsonViewer from '../components/shared/JsonViewer';
import DataTable, { type DataTableColumn } from '../components/shared/DataTable';
import PageHeader from '../components/shared/PageHeader';
import { fetchAllPages, MAX_PAGE_SIZE } from '../api/paging';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import { usePersistedPageSize } from '../lib/usePersistedPageSize';
import { canDeleteHistoryEntry } from '../lib/history';
import { buildHistoryCsv, buildHistoryJson, buildHistoryMarkdown, buildHistoryTimelineQuery } from '../lib/historyExport';
import { useDeleteProgress } from '../lib/useDeleteProgress';

interface SavedHistoryView {
  id: string;
  name: string;
  query: HistoricalTimelineQuery;
  createdUtc: string;
}

const HISTORY_SAVED_VIEWS_KEY = 'armada_history_saved_views';

function severityClass(value: string | null | undefined) {
  const normalized = (value || '').toLowerCase();
  if (normalized === 'error') return 'failed';
  if (normalized === 'warning') return 'warning';
  if (normalized === 'success' || normalized === 'passed' || normalized === 'healthy') return 'passed';
  return '';
}

function groupedDateLabel(utc: string) {
  return new Date(utc).toLocaleDateString(undefined, {
    weekday: 'short',
    month: 'short',
    day: 'numeric',
    year: 'numeric',
  });
}

function loadSavedViews(): SavedHistoryView[] {
  try {
    const raw = localStorage.getItem(HISTORY_SAVED_VIEWS_KEY);
    if (!raw) return [];
    const parsed = JSON.parse(raw) as SavedHistoryView[];
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}

function persistSavedViews(savedViews: SavedHistoryView[]) {
  try {
    localStorage.setItem(HISTORY_SAVED_VIEWS_KEY, JSON.stringify(savedViews));
  } catch {
    // ignore storage failures
  }
}

function downloadTextFile(fileName: string, content: string, mimeType: string) {
  const blob = new Blob([content], { type: mimeType });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  document.body.appendChild(anchor);
  anchor.click();
  document.body.removeChild(anchor);
  URL.revokeObjectURL(url);
}

/** Most pages an export reads (EXPORT_MAX_PAGES x MAX_PAGE_SIZE entries); the rest is reported, never fetched. */
const EXPORT_MAX_PAGES = 10;

export default function History() {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const deletion = useDeleteProgress();
  const navigate = useNavigate();
  const [confirmDelete, setConfirmDelete] = useState<HistoricalTimelineEntry | null>(null);
  const location = useLocation();
  const initialQuery = new URLSearchParams(location.search);
  const [entries, setEntries] = useState<HistoricalTimelineEntry[]>([]);
  // Paged and ordered on the server (POST /history/enumerate, 1 to 1000 per page); totals come from the route, not the page.
  const [pageNumber, setPageNumber] = useState(1);
  const [sortDir, setSortDir] = useState<'asc' | 'desc'>('desc');
  const [pageSize, setPageSize] = usePersistedPageSize('history', 100);
  const [totals, setTotals] = useState({ totalRecords: 0, totalPages: 1, totalMs: 0 });
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [objectives, setObjectives] = useState<Objective[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [objectiveFilter, setObjectiveFilter] = useState(initialQuery.get('objectiveId') || 'all');
  const [textFilter, setTextFilter] = useState(initialQuery.get('text') || '');
  const [actorFilter, setActorFilter] = useState(initialQuery.get('actor') || '');
  const [vesselFilter, setVesselFilter] = useState(initialQuery.get('vesselId') || 'all');
  const [sourceTypeFilter, setSourceTypeFilter] = useState(initialQuery.get('sourceType') || 'all');
  const [postmortemOnly, setPostmortemOnly] = useState(initialQuery.get('postmortemOnly') === 'true');
  // GET/HEAD/OPTIONS request entries are mostly the dashboard's own polling; hidden unless asked for.
  const [showReadRequests, setShowReadRequests] = useState(initialQuery.get('showReadRequests') === 'true');
  const showReadRequestsInitialized = useRef(false);
  const [savedViews, setSavedViews] = useState<SavedHistoryView[]>(() => loadSavedViews());
  const [saveViewOpen, setSaveViewOpen] = useState(false);
  const [saveViewName, setSaveViewName] = useState('');
  const [exporting, setExporting] = useState<string | null>(null);
  const [metadataViewer, setMetadataViewer] = useState<{ open: boolean; title: string; data: unknown }>({
    open: false,
    title: '',
    data: null,
  });

  function buildQuery(size = pageSize, page = 1, dir = sortDir): HistoricalTimelineQuery {
    const filters = buildHistoryTimelineQuery({
      objectiveId: objectiveFilter,
      text: textFilter,
      actor: actorFilter,
      vesselId: vesselFilter,
      sourceType: sourceTypeFilter,
      postmortemOnly,
      showReadRequests,
    }, size, page);
    return { ...filters, order: dir === 'asc' ? 'CreatedAscending' : 'CreatedDescending' };
  }

  async function load(page = pageNumber, size = pageSize, dir = sortDir) {
    try {
      setLoading(true);
      const historyResult = await enumerateHistoryTimeline(buildQuery(size, page, dir));
      const objects = historyResult.objects || [];
      const lastPage = Math.max(1, historyResult.totalPages || 1);
      // The page asked for is past the end (entries were deleted or filters narrowed): show the last page instead.
      if (objects.length === 0 && page > lastPage) {
        setPageNumber(lastPage);
        await load(lastPage, size, dir);
        return;
      }
      setEntries(objects);
      setTotals({
        totalRecords: typeof historyResult.totalRecords === 'number' ? historyResult.totalRecords : objects.length,
        totalPages: Math.max(1, historyResult.totalPages || 1),
        totalMs: historyResult.totalMs ?? 0,
      });
      setError('');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to load history.'));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    const query = new URLSearchParams(location.search);
    const objectiveId = query.get('objectiveId');
    const vesselId = query.get('vesselId');
    const sourceType = query.get('sourceType');
    const postmortemOnlyValue = query.get('postmortemOnly');
    const text = query.get('text');
    const actor = query.get('actor');

    if (objectiveId) setObjectiveFilter(objectiveId);
    if (vesselId) setVesselFilter(vesselId);
    if (sourceType) setSourceTypeFilter(sourceType);
    if (postmortemOnlyValue !== null) setPostmortemOnly(postmortemOnlyValue === 'true');
    if (text) setTextFilter(text);
    if (actor) setActorFilter(actor);
  }, [location.search]);

  useEffect(() => {
    void load();
  }, []);

  // Filter pickers read one bounded page each (never every page); an id from the URL that is not on it still filters.
  useEffect(() => {
    let cancelled = false;
    Promise.all([
      listVessels({ pageNumber: 1, pageSize: MAX_PAGE_SIZE }),
      listObjectives({ pageNumber: 1, pageSize: MAX_PAGE_SIZE }),
    ]).then(([vesselResult, objectiveResult]) => {
      if (cancelled) return;
      setVessels(vesselResult.objects || []);
      setObjectives(objectiveResult.objects || []);
    }).catch(() => { /* non-fatal: the filters fall back to "All" */ });
    return () => { cancelled = true; };
  }, []);

  function applyFilters() {
    setPageNumber(1);
    void load(1, pageSize);
  }

  function toggleSort() {
    const next = sortDir === 'desc' ? 'asc' : 'desc';
    setSortDir(next);
    setPageNumber(1);
    void load(1, pageSize, next);
  }

  function changePage(page: number) {
    setPageNumber(page);
    void load(page, pageSize);
  }

  function changePageSize(size: number) {
    setPageSize(size);
    setPageNumber(1);
    void load(1, size);
  }

  useEffect(() => {
    if (!showReadRequestsInitialized.current) {
      showReadRequestsInitialized.current = true;
      return;
    }
    setPageNumber(1);
    void load(1, pageSize);
  }, [showReadRequests]);

  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('history', () => load());

  const groupedEntries = useMemo(() => {
    const groups = new Map<string, HistoricalTimelineEntry[]>();
    for (const entry of entries) {
      const label = groupedDateLabel(entry.occurredUtc);
      const existing = groups.get(label);
      if (existing) existing.push(entry);
      else groups.set(label, [entry]);
    }
    return Array.from(groups.entries());
  }, [entries]);

  const vesselMap = useMemo(() => new Map(vessels.map((vessel) => [vessel.id, vessel.name])), [vessels]);
  const objectiveMap = useMemo(() => new Map(objectives.map((objective) => [objective.id, objective.title])), [objectives]);
  const sourceTypeCounts = useMemo(() => {
    const counts = new Map<string, number>();
    for (const entry of entries) {
      counts.set(entry.sourceType, (counts.get(entry.sourceType) || 0) + 1);
    }
    return counts;
  }, [entries]);
  const distinctSourceTypes = useMemo(() => {
    const types = new Set<string>();
    for (const entry of entries) {
      types.add(entry.sourceType);
    }
    return Array.from(types).sort();
  }, [entries]);
  const errorCount = useMemo(() => entries.filter((entry) => (entry.severity || '').toLowerCase() === 'error').length, [entries]);
  const warningCount = useMemo(() => entries.filter((entry) => (entry.severity || '').toLowerCase() === 'warning').length, [entries]);

  function openMetadata(entry: HistoricalTimelineEntry) {
    if (!entry.metadataJson) return;
    try {
      setMetadataViewer({
        open: true,
        title: `${entry.sourceType}: ${entry.title}`,
        data: JSON.parse(entry.metadataJson),
      });
    } catch {
      setMetadataViewer({
        open: true,
        title: `${entry.sourceType}: ${entry.title}`,
        data: entry.metadataJson,
      });
    }
  }

  const canDeleteEntry = canDeleteHistoryEntry;

  async function handleDeleteEntry(entry: HistoricalTimelineEntry) {
    setConfirmDelete(null);
    if (!(await deletion.run({ kind: 'requestHistoryEntry', action: () => deleteRequestHistoryEntry(entry.sourceId), errorMessage: (err) => (err instanceof Error ? err.message : t('Failed to delete history entry.')) }))) return;
    pushToast('warning', t('History entry deleted.'));
    await load();
  }

  function saveCurrentView() {
    const trimmedName = saveViewName.trim();
    if (!trimmedName) return;

    const nextSavedViews = [
      {
        id: `hsv_${Date.now()}`,
        name: trimmedName,
        query: buildQuery(),
        createdUtc: new Date().toISOString(),
      },
      ...savedViews,
    ];
    setSavedViews(nextSavedViews);
    persistSavedViews(nextSavedViews);
    setSaveViewName('');
    setSaveViewOpen(false);
  }

  function applySavedView(view: SavedHistoryView) {
    setObjectiveFilter(view.query.objectiveId || 'all');
    setTextFilter(view.query.text || '');
    setActorFilter(view.query.actor || '');
    setVesselFilter(view.query.vesselId || 'all');
    setSourceTypeFilter(view.query.sourceTypes && view.query.sourceTypes.length > 0 ? view.query.sourceTypes[0] : 'all');
    setPostmortemOnly(view.query.postmortemOnly === true);
    setShowReadRequests(view.query.excludeReadRequests === false);
  }

  function deleteSavedView(id: string) {
    const nextSavedViews = savedViews.filter((view) => view.id !== id);
    setSavedViews(nextSavedViews);
    persistSavedViews(nextSavedViews);
  }

  async function exportCurrentView(format: 'json' | 'csv' | 'md') {
    try {
      setExporting(format);
      // Export the filtered view page by page, explicitly bounded to EXPORT_MAX_PAGES pages of MAX_PAGE_SIZE.
      const query = buildQuery(MAX_PAGE_SIZE, 1);
      const result = await fetchAllPages((page, size) => enumerateHistoryTimeline({ ...query, pageNumber: page, pageSize: size }), { pageSize: MAX_PAGE_SIZE, maxPages: EXPORT_MAX_PAGES });
      const allEntries = result.objects || [];
      if (result.totalRecords > allEntries.length) {
        pushToast('warning', t('Exported the first {{count}} of {{total}} entries. Narrow the filters to export the rest.', { count: allEntries.length, total: result.totalRecords }));
      }
      const timestamp = new Date().toISOString().slice(0, 19).replace(/[:T]/g, '-');

      if (format === 'json') {
        const content = buildHistoryJson(query, allEntries);
        downloadTextFile(`armada-history-${timestamp}.json`, content, 'application/json');
        return;
      }

      if (format === 'csv') {
        downloadTextFile(`armada-history-${timestamp}.csv`, buildHistoryCsv(allEntries), 'text/csv');
        return;
      }

      downloadTextFile(`armada-history-${timestamp}.md`, buildHistoryMarkdown(query, allEntries), 'text/markdown');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to export history.'));
    } finally {
      setExporting(null);
    }
  }

  const columns: DataTableColumn<HistoricalTimelineEntry>[] = [
    {
      key: 'when', label: t('When'), sortKey: 'occurredUtc', headerTitle: t('When the activity happened. Hover a cell for the exact time.'), cellClassName: 'text-dim cell-nowrap',
      cellTitle: (entry) => formatDateTime(entry.occurredUtc),
      render: (entry) => formatRelativeTime(entry.occurredUtc),
    },
    {
      key: 'title', label: t('Title'), required: true, headerTitle: t('What happened. Hover a row for its description; click a row to open the related record.'),
      // One line: the description is in the tooltip and in the optional Description column.
      cellTitle: (entry) => [entry.title, entry.description].filter(Boolean).join('\n'),
      render: (entry) => <strong className="cell-one-line">{entry.title}</strong>,
    },
    {
      key: 'description', label: t('Description'), headerTitle: t('Extra detail recorded with the entry.'), defaultHidden: true, cellClassName: 'text-dim',
      render: (entry) => <span className="cell-one-line" title={entry.description || undefined}>{entry.description || '-'}</span>,
    },
    { key: 'source', label: t('Source'), headerTitle: t('Kind of record the entry comes from, such as Mission, Deployment, Incident, Request, or Event.'), cellClassName: 'cell-nowrap', render: (entry) => <span className="tag" title={t('Kind of record the entry comes from, such as Mission, Deployment, Incident, Request, or Event.')}>{entry.sourceType}</span> },
    {
      key: 'status', label: t('Status'), headerTitle: t('Status of the record at that moment. Red marks errors and amber marks warnings.'), cellClassName: 'cell-nowrap',
      render: (entry) => (entry.status ? <span className={`tag ${severityClass(entry.severity)}`.trim()}>{entry.status}</span> : <span className="text-dim">-</span>),
    },
    { key: 'actor', label: t('Actor'), headerTitle: t('User, captain, or system process that caused the entry.'), cellClassName: 'text-dim', render: (entry) => entry.actorDisplay || '-' },
    { key: 'vessel', label: t('Vessel'), headerTitle: t('Repository the entry relates to, if any.'), cellClassName: 'text-dim', render: (entry) => (entry.vesselId ? (vesselMap.get(entry.vesselId) || entry.vesselId) : '-') },
    {
      key: 'actions', label: t('Actions'), fixed: true, interactive: true, className: 'text-right',
      render: (entry) => (
        <ActionMenu
          id={`history-${entry.id}`}
          items={[
            ...(entry.route ? [{ label: 'View', title: 'Open the record this entry refers to.', onClick: () => navigate(entry.route as string) }] : []),
            ...(entry.metadataJson ? [{ label: 'View JSON', title: 'Show the extra data recorded with this entry.', onClick: () => openMetadata(entry) }] : []),
            ...(entry.vesselId ? [{ label: 'Open Workspace', title: 'Open the workspace of the vessel this entry relates to.', onClick: () => navigate(`/workspace/${entry.vesselId}`) }] : []),
            ...(canDeleteEntry(entry) ? [{ label: 'Delete', title: 'Delete the underlying API request record. Only request entries can be deleted. Cannot be undone.', danger: true, onClick: () => setConfirmDelete(entry) }] : []),
          ]}
        />
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title={t('History')}
        subtitle={t('Unified operational memory across backlog refinement, planning, dispatch, releases, deployments, incidents, requests, and events.')}
        actions={(
          <>
            <button className="btn btn-sm" onClick={() => setSaveViewOpen(true)} title={t('Save the current filters under a name so you can reapply them later. Saved views live in this browser only.')}>
              {t('Save View')}
            </button>
            <button className="btn btn-sm" disabled={exporting !== null} onClick={() => exportCurrentView('json')} title={t('Download every entry matching the applied filters, across all pages, as a JSON file.')}>
              {exporting === 'json' ? t('Exporting...') : t('Export JSON')}
            </button>
            <button className="btn btn-sm" disabled={exporting !== null} onClick={() => exportCurrentView('csv')} title={t('Download every entry matching the applied filters, across all pages, as a CSV spreadsheet.')}>
              {exporting === 'csv' ? t('Exporting...') : t('Export CSV')}
            </button>
            <button className="btn btn-sm" disabled={exporting !== null} onClick={() => exportCurrentView('md')} title={t('Download every entry matching the applied filters, across all pages, as a Markdown report.')}>
              {exporting === 'md' ? t('Exporting...') : t('Export Markdown')}
            </button>
          </>
        )}
      />

      <ErrorModal error={error} onClose={() => setError('')} />
      <JsonViewer open={metadataViewer.open} title={metadataViewer.title} data={metadataViewer.data} onClose={() => setMetadataViewer({ open: false, title: '', data: null })} />

      {saveViewOpen && (
        <div className="modal-overlay" onClick={() => setSaveViewOpen(false)}>
          <div className="modal modal-sm" onClick={(event) => event.stopPropagation()}>
            <h3>{t('Save History View')}</h3>
            <label title={t('Name for this set of filters, such as Staging failures.')}>
              {t('View Name')}
              <input title={t('Name for this set of filters, such as Staging failures.')} value={saveViewName} onChange={(event) => setSaveViewName(event.target.value)} placeholder={t('Staging failures')} />
            </label>
            <div className="modal-actions">
              <button type="button" className="btn btn-primary" onClick={saveCurrentView} disabled={!saveViewName.trim()} title={t('Save the current filters under this name in this browser.')}>
                {t('Save')}
              </button>
              <button type="button" className="btn" onClick={() => setSaveViewOpen(false)} title={t('Close without saving.')}>
                {t('Cancel')}
              </button>
            </div>
          </div>
        </div>
      )}

      <div className="playbook-overview-grid">
        <div className="card playbook-overview-card" title={t('Entries matching the applied filters, across all pages.')}>
          <span>{t('Total Entries')}</span>
          <strong>{totals.totalRecords.toLocaleString()}</strong>
          {totals.totalRecords > entries.length && (
            <span className="text-dim">{t('Showing {{shown}} of {{total}}', { shown: entries.length.toLocaleString(), total: totals.totalRecords.toLocaleString() })}</span>
          )}
        </div>
        <div className="card playbook-overview-card" title={t('Entries with error severity among those shown on this page.')}>
          <span>{t('Errors on this page')}</span>
          <strong>{errorCount}</strong>
        </div>
        <div className="card playbook-overview-card" title={t('Entries with warning severity among those shown on this page.')}>
          <span>{t('Warnings on this page')}</span>
          <strong>{warningCount}</strong>
        </div>
        <div className="card playbook-overview-card" title={t('Distinct kinds of records among the entries shown on this page.')}>
          <span>{t('Source types on this page')}</span>
          <strong>{sourceTypeCounts.size}</strong>
        </div>
      </div>

      <div className="card" style={{ padding: '1rem', marginBottom: '1rem' }}>
        <div className="playbook-filter-row">
          <input
            type="text"
            value={textFilter}
            onChange={(event) => setTextFilter(event.target.value)}
            placeholder={t('Search title, status, route, or metadata...')}
            title={t('Text to find in an entry\'s title, description, status, route, or metadata, case-insensitive. Click Apply to search.')}
          />
          <select aria-label={t('All backlog items')} title={t('Show only activity linked to this backlog item. Click Apply to filter.')} value={objectiveFilter} onChange={(event) => setObjectiveFilter(event.target.value)}>
            <option value="all">{t('All backlog items')}</option>
            {objectives.map((objective) => (
              <option key={objective.id} value={objective.id}>{objective.title}</option>
            ))}
          </select>
          <input
            type="text"
            value={actorFilter}
            onChange={(event) => setActorFilter(event.target.value)}
            placeholder={t('Filter by actor or principal...')}
            title={t('Show only entries whose actor name or ID contains this text, such as a user email or captain name. Click Apply to filter.')}
          />
          <select aria-label={t('All vessels')} title={t('Show only activity for this vessel. Click Apply to filter.')} value={vesselFilter} onChange={(event) => setVesselFilter(event.target.value)}>
            <option value="all">{t('All vessels')}</option>
            {vessels.map((vessel) => (
              <option key={vessel.id} value={vessel.id}>{vessel.name}</option>
            ))}
          </select>
          <select aria-label={t('All source types')} title={t('Show only entries of this kind. Lists the kinds on the current page. Click Apply to filter.')} value={sourceTypeFilter} onChange={(event) => setSourceTypeFilter(event.target.value)}>
            <option value="all">{t('All source types')}</option>
            {distinctSourceTypes.map((sourceType) => (
              <option key={sourceType} value={sourceType}>{sourceType}</option>
            ))}
          </select>
          <label className="checkbox-label" style={{ marginBottom: 0, whiteSpace: 'nowrap' }} title={t('Show only incidents that have postmortem notes and the deployments, releases, and work linked to them. Click Apply to filter.')}>
            <input
              type="checkbox"
              title={t('Show only incidents that have postmortem notes and the deployments, releases, and work linked to them. Click Apply to filter.')}
              checked={postmortemOnly}
              onChange={(event) => setPostmortemOnly(event.target.checked)}
            />
            {t('Postmortem context only')}
          </label>
          <label className="checkbox-label" style={{ marginBottom: 0, whiteSpace: 'nowrap' }} title={t('Include read-only API requests (GET, HEAD, OPTIONS). They are mostly dashboard and client polling, so they are hidden by default. Applies at once.')}>
            <input
              type="checkbox"
              title={t('Include read-only API requests (GET, HEAD, OPTIONS). They are mostly dashboard and client polling, so they are hidden by default. Applies at once.')}
              checked={showReadRequests}
              onChange={(event) => setShowReadRequests(event.target.checked)}
            />
            {t('Show GET requests')}
          </label>
          <button className="btn btn-primary" onClick={applyFilters} title={t('Reload the list with the filters above, starting from page 1.')}>{t('Apply')}</button>
        </div>

        {savedViews.length > 0 && (
          <div style={{ marginTop: '0.9rem' }}>
            <div className="readiness-section-title" style={{ marginBottom: '0.5rem' }} title={t('Filter sets saved in this browser. Click one to load its filters, then Apply.')}>{t('Saved Views')}</div>
            <div className="readiness-inline-list">
              {savedViews.map((view) => (
                <span key={view.id} className="readiness-type-pill" style={{ display: 'inline-flex', alignItems: 'center', gap: '0.45rem' }}>
                  <button type="button" className="btn-link" onClick={() => applySavedView(view)} title={t('Load this saved view\'s filters. Click Apply to run them.')}>
                    {view.name}
                  </button>
                  <button type="button" className="btn-link text-danger" onClick={() => deleteSavedView(view.id)} title={t('Remove this saved view from this browser.')} aria-label={t('Remove this saved view from this browser.')}>
                    &times;
                  </button>
                </span>
              ))}
            </div>
          </div>
        )}

        {sourceTypeCounts.size > 0 && (
          <div className="readiness-inline-list" style={{ marginTop: '0.9rem' }}>
            {Array.from(sourceTypeCounts.entries()).map(([sourceType, count]) => (
              <span key={sourceType} className="readiness-type-pill" title={t('Entries of this kind on the current page.')}>{`${sourceType} (${count})`}</span>
            ))}
          </div>
        )}
      </div>

      <DataTable
        tableKey="history"
        columns={columns}
        rows={entries}
        rowKey={(entry) => entry.id}
        sort={{ field: 'occurredUtc', dir: sortDir, onSort: toggleSort }}
        pagination={{
          pageNumber,
          pageSize,
          totalPages: totals.totalPages,
          totalRecords: totals.totalRecords,
          totalMs: totals.totalMs,
          onPageChange: changePage,
          onPageSizeChange: changePageSize,
        }}
        onRowClick={(entry) => { if (entry.route) navigate(entry.route); }}
        isRowClickable={(entry) => Boolean(entry.route)}
        autoRefresh={{ seconds: refreshSeconds, onChange: setRefreshSeconds }}
        onRefresh={() => load()}
        refreshTitle={t('Refresh history')}
        placeholder={loading ? <p className="text-dim">{t('Loading...')}</p> : entries.length === 0 ? (
          <div className="playbook-empty-state">
            <strong>{t('No history entries match the current filters.')}</strong>
            <span>{t('Try broadening the filters or refresh after running additional work in Armada.')}</span>
          </div>
        ) : undefined}
      />

      <ConfirmDialog
        open={!!confirmDelete}
        title={t('Delete History Entry')}
        message={t('Delete this request-history entry? This cannot be undone.')}
        confirmLabel={t('Delete')}
        danger
        onConfirm={() => confirmDelete && void handleDeleteEntry(confirmDelete)}
        onCancel={() => setConfirmDelete(null)}
      />
      {deletion.dialog}
    </div>
  );
}
