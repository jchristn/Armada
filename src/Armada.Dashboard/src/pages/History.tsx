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
import { useAutoRefresh } from '../lib/useAutoRefresh';
import { canDeleteHistoryEntry } from '../lib/history';

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

function escapeCsvValue(value: string | null | undefined) {
  const normalized = value || '';
  if (!/[",\r\n]/.test(normalized)) return normalized;
  return `"${normalized.replace(/"/g, '""')}"`;
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

function buildCsv(entries: HistoricalTimelineEntry[]) {
  const header = [
    'id',
    'sourceType',
    'title',
    'status',
    'severity',
    'occurredUtc',
    'actorDisplay',
    'vesselId',
    'missionId',
    'voyageId',
    'route',
    'description',
  ];
  const rows = entries.map((entry) => [
    escapeCsvValue(entry.id),
    escapeCsvValue(entry.sourceType),
    escapeCsvValue(entry.title),
    escapeCsvValue(entry.status),
    escapeCsvValue(entry.severity),
    escapeCsvValue(entry.occurredUtc),
    escapeCsvValue(entry.actorDisplay),
    escapeCsvValue(entry.vesselId),
    escapeCsvValue(entry.missionId),
    escapeCsvValue(entry.voyageId),
    escapeCsvValue(entry.route),
    escapeCsvValue(entry.description),
  ].join(','));
  return [header.join(','), ...rows].join('\r\n');
}

function buildMarkdown(query: HistoricalTimelineQuery, entries: HistoricalTimelineEntry[]) {
  const activeFilters: string[] = [];
  if (query.objectiveId) activeFilters.push(`objective=\`${query.objectiveId}\``);
  if (query.text) activeFilters.push(`text=\`${query.text}\``);
  if (query.actor) activeFilters.push(`actor=\`${query.actor}\``);
  if (query.vesselId) activeFilters.push(`vessel=\`${query.vesselId}\``);
  if (query.postmortemOnly) activeFilters.push('postmortemOnly=`true`');
  if (query.excludeReadRequests) activeFilters.push('excludeReadRequests=`true`');
  if (query.sourceTypes && query.sourceTypes.length > 0) activeFilters.push(`sourceTypes=\`${query.sourceTypes.join(', ')}\``);

  const lines: string[] = [
    '# Armada History Export',
    '',
    `Exported: ${new Date().toISOString()}`,
    `Entries: ${entries.length}`,
  ];

  if (activeFilters.length > 0) {
    lines.push(`Filters: ${activeFilters.join(', ')}`);
  }

  lines.push('');
  for (const entry of entries) {
    lines.push(`## ${entry.title}`);
    lines.push(`- Source: ${entry.sourceType}`);
    lines.push(`- Time: ${entry.occurredUtc}`);
    if (entry.status) lines.push(`- Status: ${entry.status}`);
    if (entry.severity) lines.push(`- Severity: ${entry.severity}`);
    if (entry.actorDisplay) lines.push(`- Actor: ${entry.actorDisplay}`);
    if (entry.vesselId) lines.push(`- Vessel: ${entry.vesselId}`);
    if (entry.route) lines.push(`- Route: ${entry.route}`);
    if (entry.description) {
      lines.push('');
      lines.push(entry.description);
    }
    lines.push('');
  }

  return lines.join('\n');
}

export default function History() {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const navigate = useNavigate();
  const [confirmDelete, setConfirmDelete] = useState<HistoricalTimelineEntry | null>(null);
  const location = useLocation();
  const initialQuery = new URLSearchParams(location.search);
  const [entries, setEntries] = useState<HistoricalTimelineEntry[]>([]);
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

  function buildQuery(pageSize = 250): HistoricalTimelineQuery {
    return {
      pageNumber: 1,
      pageSize,
      objectiveId: objectiveFilter === 'all' ? null : objectiveFilter,
      text: textFilter || null,
      actor: actorFilter || null,
      vesselId: vesselFilter === 'all' ? null : vesselFilter,
      sourceTypes: sourceTypeFilter === 'all' ? [] : [sourceTypeFilter],
      postmortemOnly: postmortemOnly || undefined,
      excludeReadRequests: !showReadRequests,
    };
  }

  async function load() {
    try {
      setLoading(true);
      const query = buildQuery();

      const [historyResult, vesselResult, objectiveResult] = await Promise.all([
        enumerateHistoryTimeline(query),
        listVessels({ pageSize: 9999 }),
        listObjectives({ pageSize: 9999 }),
      ]);

      setEntries(historyResult.objects || []);
      setVessels(vesselResult.objects || []);
      setObjectives(objectiveResult.objects || []);
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

  useEffect(() => {
    if (!showReadRequestsInitialized.current) {
      showReadRequestsInitialized.current = true;
      return;
    }
    void load();
  }, [showReadRequests]);

  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('history', load);

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
    try {
      await deleteRequestHistoryEntry(entry.sourceId);
      pushToast('warning', t('History entry deleted.'));
      await load();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to delete history entry.'));
    }
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
      const query = buildQuery(5000);
      const result = await enumerateHistoryTimeline(query);
      const allEntries = result.objects || [];
      const timestamp = new Date().toISOString().slice(0, 19).replace(/[:T]/g, '-');

      if (format === 'json') {
        const content = JSON.stringify({
          exportedUtc: new Date().toISOString(),
          query,
          totalCount: allEntries.length,
          entries: allEntries,
        }, null, 2);
        downloadTextFile(`armada-history-${timestamp}.json`, content, 'application/json');
        return;
      }

      if (format === 'csv') {
        downloadTextFile(`armada-history-${timestamp}.csv`, buildCsv(allEntries), 'text/csv');
        return;
      }

      downloadTextFile(`armada-history-${timestamp}.md`, buildMarkdown(query, allEntries), 'text/markdown');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to export history.'));
    } finally {
      setExporting(null);
    }
  }

  const columns: DataTableColumn<HistoricalTimelineEntry>[] = [
    {
      key: 'when', label: t('When'), cellClassName: 'text-dim cell-nowrap',
      cellTitle: (entry) => formatDateTime(entry.occurredUtc),
      render: (entry) => formatRelativeTime(entry.occurredUtc),
    },
    {
      key: 'title', label: t('Title'), required: true,
      // One line: the description is in the tooltip and in the optional Description column.
      cellTitle: (entry) => [entry.title, entry.description].filter(Boolean).join('\n'),
      render: (entry) => <strong className="cell-one-line">{entry.title}</strong>,
    },
    {
      key: 'description', label: t('Description'), defaultHidden: true, cellClassName: 'text-dim',
      render: (entry) => <span className="cell-one-line" title={entry.description || undefined}>{entry.description || '-'}</span>,
    },
    { key: 'source', label: t('Source'), cellClassName: 'cell-nowrap', render: (entry) => <span className="tag">{entry.sourceType}</span> },
    {
      key: 'status', label: t('Status'), cellClassName: 'cell-nowrap',
      render: (entry) => (entry.status ? <span className={`tag ${severityClass(entry.severity)}`.trim()}>{entry.status}</span> : <span className="text-dim">-</span>),
    },
    { key: 'actor', label: t('Actor'), cellClassName: 'text-dim', render: (entry) => entry.actorDisplay || '-' },
    { key: 'vessel', label: t('Vessel'), cellClassName: 'text-dim', render: (entry) => (entry.vesselId ? (vesselMap.get(entry.vesselId) || entry.vesselId) : '-') },
    {
      key: 'actions', label: t('Actions'), fixed: true, interactive: true, className: 'text-right',
      render: (entry) => (
        <ActionMenu
          id={`history-${entry.id}`}
          items={[
            ...(entry.route ? [{ label: 'View', onClick: () => navigate(entry.route as string) }] : []),
            ...(entry.metadataJson ? [{ label: 'View JSON', onClick: () => openMetadata(entry) }] : []),
            ...(entry.vesselId ? [{ label: 'Open Workspace', onClick: () => navigate(`/workspace/${entry.vesselId}`) }] : []),
            ...(canDeleteEntry(entry) ? [{ label: 'Delete', danger: true, onClick: () => setConfirmDelete(entry) }] : []),
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
            <button className="btn btn-sm" onClick={() => setSaveViewOpen(true)}>
              {t('Save View')}
            </button>
            <button className="btn btn-sm" disabled={exporting !== null} onClick={() => exportCurrentView('json')}>
              {exporting === 'json' ? t('Exporting...') : t('Export JSON')}
            </button>
            <button className="btn btn-sm" disabled={exporting !== null} onClick={() => exportCurrentView('csv')}>
              {exporting === 'csv' ? t('Exporting...') : t('Export CSV')}
            </button>
            <button className="btn btn-sm" disabled={exporting !== null} onClick={() => exportCurrentView('md')}>
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
            <label>
              {t('View Name')}
              <input value={saveViewName} onChange={(event) => setSaveViewName(event.target.value)} placeholder={t('Staging failures')} />
            </label>
            <div className="modal-actions">
              <button type="button" className="btn btn-primary" onClick={saveCurrentView} disabled={!saveViewName.trim()}>
                {t('Save')}
              </button>
              <button type="button" className="btn" onClick={() => setSaveViewOpen(false)}>
                {t('Cancel')}
              </button>
            </div>
          </div>
        </div>
      )}

      <div className="playbook-overview-grid">
        <div className="card playbook-overview-card">
          <span>{t('Visible Entries')}</span>
          <strong>{entries.length}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Errors')}</span>
          <strong>{errorCount}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Warnings')}</span>
          <strong>{warningCount}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Source Types')}</span>
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
          />
          <select aria-label={t('All backlog items')} value={objectiveFilter} onChange={(event) => setObjectiveFilter(event.target.value)}>
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
          />
          <select aria-label={t('All vessels')} value={vesselFilter} onChange={(event) => setVesselFilter(event.target.value)}>
            <option value="all">{t('All vessels')}</option>
            {vessels.map((vessel) => (
              <option key={vessel.id} value={vessel.id}>{vessel.name}</option>
            ))}
          </select>
          <select aria-label={t('All source types')} value={sourceTypeFilter} onChange={(event) => setSourceTypeFilter(event.target.value)}>
            <option value="all">{t('All source types')}</option>
            {distinctSourceTypes.map((sourceType) => (
              <option key={sourceType} value={sourceType}>{sourceType}</option>
            ))}
          </select>
          <label className="checkbox-label" style={{ marginBottom: 0, whiteSpace: 'nowrap' }}>
            <input
              type="checkbox"
              checked={postmortemOnly}
              onChange={(event) => setPostmortemOnly(event.target.checked)}
            />
            {t('Postmortem context only')}
          </label>
          <label className="checkbox-label" style={{ marginBottom: 0, whiteSpace: 'nowrap' }} title={t('GET requests are mostly dashboard and client polling, so they are hidden by default.')}>
            <input
              type="checkbox"
              checked={showReadRequests}
              onChange={(event) => setShowReadRequests(event.target.checked)}
            />
            {t('Show GET requests')}
          </label>
          <button className="btn btn-primary" onClick={load}>{t('Apply')}</button>
        </div>

        {savedViews.length > 0 && (
          <div style={{ marginTop: '0.9rem' }}>
            <div className="readiness-section-title" style={{ marginBottom: '0.5rem' }}>{t('Saved Views')}</div>
            <div className="readiness-inline-list">
              {savedViews.map((view) => (
                <span key={view.id} className="readiness-type-pill" style={{ display: 'inline-flex', alignItems: 'center', gap: '0.45rem' }}>
                  <button type="button" className="btn-link" onClick={() => applySavedView(view)}>
                    {view.name}
                  </button>
                  <button type="button" className="btn-link text-danger" onClick={() => deleteSavedView(view.id)}>
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
              <span key={sourceType} className="readiness-type-pill">{`${sourceType} (${count})`}</span>
            ))}
          </div>
        )}
      </div>

      <DataTable
        tableKey="history"
        columns={columns}
        rows={entries}
        rowKey={(entry) => entry.id}
        onRowClick={(entry) => { if (entry.route) navigate(entry.route); }}
        isRowClickable={(entry) => Boolean(entry.route)}
        autoRefresh={{ seconds: refreshSeconds, onChange: setRefreshSeconds }}
        onRefresh={load}
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
    </div>
  );
}
