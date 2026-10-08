import { useRouter, type Href } from 'expo-router';
import { useEffect, useMemo, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { deleteRequestHistoryEntry, enumerateHistoryTimeline, listObjectives } from '@dashboard/api/client';
import type { HistoricalTimelineEntry, Objective } from '@dashboard/types/models';
import { canDeleteHistoryEntry } from '@dashboard/lib/history';
import {
  buildHistoryCsv, buildHistoryJson, buildHistoryMarkdown, buildHistoryTimelineQuery, countBySourceType, type HistoryFilterState,
} from '@dashboard/lib/historyExport';
import { ActionBar, JsonSheet } from '../../components/resource/DetailParts';
import { FormSheet, str } from '../../components/resource/FormSheet';
import { ResourceList, StatRow } from '../../components/resource/ResourceList';
import { ResourceRow, useConfirm } from '../../components/resource/ResourceRow';
import { resourceStyles } from '../../components/resource/styles';
import { AppText } from '../../components/ui/AppText';
import { Button } from '../../components/ui/Button';
import { SearchField } from '../../components/ui/SearchField';
import { useLocale } from '../../i18n/LocaleContext';
import { appPathFromLink } from '../../navigation/deepLinks';
import { useNotifications } from '../../notifications/NotificationContext';
import { readPref, writePref } from '../../storage/prefs';
import { ALL, useNameMap, useReference, useVessels } from '../../resource/lookups';
import { toneFor } from '../../resource/status';
import { errorText, useReloadOnFocus } from '../../resource/useLoad';
import { spacing } from '../../theme/typography';
import { exportTimestamp, shareTextFile } from './exportFile';
import { usePagedLoad } from './usePagedLoad';

/** Saved History views (the dashboard keeps them in browser storage under the same key). */
export const HISTORY_SAVED_VIEWS_KEY = 'armada_history_saved_views';

export interface SavedHistoryView {
  id: string;
  name: string;
  filters: HistoryFilterState;
  createdUtc: string;
}

/** Timeline events that can add or change history rows. */
export const HISTORY_LIVE_PREFIXES = ['mission.', 'voyage.', 'deployment.', 'incident.', 'objective.', 'planning-session.', 'check-run.', 'runbook-execution.'];

const PAGE_SIZE = 100;
const DEFAULT_FILTERS: HistoryFilterState = { objectiveId: 'all', text: '', actor: '', vesselId: 'all', sourceType: 'all', postmortemOnly: false, showReadRequests: false };

/** Debounces a text value so typing does not reload on every keystroke. */
function useDebounced(value: string, ms: number): string {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), ms);
    return () => clearTimeout(timer);
  }, [value, ms]);
  return debounced;
}

/**
 * Activity > All Activity (the dashboard's History page): the unified timeline across backlog, planning, dispatch,
 * releases, deployments, incidents, requests, and events, with summary counts, text / actor / backlog item / vessel /
 * source type filters, postmortem-only and GET-request toggles, saved views, exports (JSON, CSV, Markdown through the
 * share sheet), and row actions (View JSON, Open Workspace, Delete for request rows). Rows open their route.
 */
export function HistoryTab({ initial }: { initial?: Partial<HistoryFilterState> }) {
  const { t, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const router = useRouter();
  const vessels = useVessels();
  const vesselNames = useNameMap(vessels);
  const objectives = useReference<Objective>(() => listObjectives(ALL));
  const { confirm, dialog } = useConfirm('history-confirm');
  const [filters, setFilters] = useState<HistoryFilterState>({ ...DEFAULT_FILTERS, ...initial });
  const [savedViews, setSavedViews] = useState<SavedHistoryView[]>([]);
  const [saveOpen, setSaveOpen] = useState(false);
  const [json, setJson] = useState<{ title: string; data: unknown } | null>(null);
  const [exporting, setExporting] = useState<string | null>(null);
  const text = useDebounced(filters.text, 400);
  const actor = useDebounced(filters.actor, 400);
  const effective = { ...filters, text, actor };
  const key = JSON.stringify(effective);

  useEffect(() => {
    let cancelled = false;
    void readPref<SavedHistoryView[]>(HISTORY_SAVED_VIEWS_KEY).then((views) => { if (!cancelled && Array.isArray(views)) setSavedViews(views); });
    return () => { cancelled = true; };
  }, []);

  const paged = usePagedLoad(
    (pageNumber) => enumerateHistoryTimeline(buildHistoryTimelineQuery(effective, PAGE_SIZE, pageNumber)),
    [key],
    { live: HISTORY_LIVE_PREFIXES, fallbackError: t('Failed to load history.') },
  );
  useReloadOnFocus(paged.reload);
  const entries = paged.items;

  const counts = useMemo(() => countBySourceType(entries), [entries]);
  const sourceTypes = useMemo(() => Array.from(counts.keys()).sort(), [counts]);
  const errors = entries.filter((e) => (e.severity || '').toLowerCase() === 'error').length;
  const warnings = entries.filter((e) => (e.severity || '').toLowerCase() === 'warning').length;
  const set = (patch: Partial<HistoryFilterState>) => setFilters((f) => ({ ...f, ...patch }));

  function openMetadata(entry: HistoricalTimelineEntry) {
    if (!entry.metadataJson) return;
    let data: unknown = entry.metadataJson;
    try { data = JSON.parse(entry.metadataJson); } catch { /* shown as text */ }
    setJson({ title: `${entry.sourceType}: ${entry.title}`, data });
  }

  function remove(entry: HistoricalTimelineEntry) {
    confirm({
      title: t('Delete History Entry'),
      message: t('Delete this request-history entry? This cannot be undone.'),
      confirmLabel: t('Delete'),
      danger: true,
      onConfirm: async () => {
        try {
          await deleteRequestHistoryEntry(entry.sourceId);
          pushToast('warning', t('History entry deleted.'));
          await paged.reload();
        } catch (err: unknown) {
          pushToast('error', errorText(err, t('Failed to delete history entry.')));
        }
      },
    });
  }

  async function exportView(format: 'json' | 'csv' | 'md') {
    setExporting(format);
    try {
      const query = buildHistoryTimelineQuery(filters, 5000);
      const all = (await enumerateHistoryTimeline(query)).objects || [];
      const stamp = exportTimestamp();
      if (format === 'json') await shareTextFile(`armada-history-${stamp}.json`, buildHistoryJson(query, all), 'application/json');
      else if (format === 'csv') await shareTextFile(`armada-history-${stamp}.csv`, buildHistoryCsv(all), 'text/csv');
      else await shareTextFile(`armada-history-${stamp}.md`, buildHistoryMarkdown(query, all), 'text/markdown');
    } catch (err: unknown) {
      pushToast('error', errorText(err, t('Failed to export history.')));
    } finally {
      setExporting(null);
    }
  }

  function persist(views: SavedHistoryView[]) {
    setSavedViews(views);
    void writePref(HISTORY_SAVED_VIEWS_KEY, views);
  }

  function open(entry: HistoricalTimelineEntry) {
    const path = appPathFromLink(entry.route);
    if (path) router.push(path as Href);
  }

  const header = (
    <>
      <StatRow stats={[
        { label: t('Visible Entries'), value: entries.length },
        { label: t('Errors'), value: errors, tone: errors > 0 ? 'danger' : undefined },
        { label: t('Warnings'), value: warnings, tone: warnings > 0 ? 'warning' : undefined },
        { label: t('Source Types'), value: counts.size },
      ]} />
      <ActionBar>
        <Button label={t('Save View')} variant="secondary" style={resourceStyles.action} onPress={() => setSaveOpen(true)} testID="history-save-view" />
        <Button label={exporting === 'json' ? t('Exporting...') : t('Export JSON')} variant="ghost" disabled={exporting !== null} style={resourceStyles.action} onPress={() => void exportView('json')} testID="history-export-json" />
        <Button label={exporting === 'csv' ? t('Exporting...') : t('Export CSV')} variant="ghost" disabled={exporting !== null} style={resourceStyles.action} onPress={() => void exportView('csv')} />
        <Button label={exporting === 'md' ? t('Exporting...') : t('Export Markdown')} variant="ghost" disabled={exporting !== null} style={resourceStyles.action} onPress={() => void exportView('md')} />
      </ActionBar>
      <View style={styles.actor}>
        <SearchField value={filters.actor} onChangeText={(v) => set({ actor: v })} placeholder={t('Filter by actor or principal...')} clearLabel={t('Clear')} testID="history-actor" />
      </View>
      {savedViews.length > 0 ? (
        <View style={styles.saved}>
          <AppText variant="caption" muted>{t('Saved Views')}</AppText>
          <View style={resourceStyles.row}>
            {savedViews.map((view) => (
              <View key={view.id} style={resourceStyles.row}>
                <Button label={view.name} variant="secondary" style={resourceStyles.action} onPress={() => setFilters({ ...DEFAULT_FILTERS, ...view.filters })} testID={`history-view-${view.id}`} />
                <Button label={'\u00D7'} variant="ghost" style={resourceStyles.action} accessibilityHint={t('Delete')} onPress={() => persist(savedViews.filter((v) => v.id !== view.id))} />
              </View>
            ))}
          </View>
        </View>
      ) : null}
      {counts.size > 0 ? (
        <AppText variant="caption" muted style={styles.saved}>{Array.from(counts.entries()).map(([type, n]) => `${type} (${n})`).join('  ')}</AppText>
      ) : null}
    </>
  );

  return (
    <>
      <ResourceList
        testID="activity-history"
        items={entries}
        keyOf={(e) => e.id}
        loading={paged.loading}
        error={paged.error}
        onRetry={() => void paged.reload()}
        refreshing={paged.refreshing}
        onRefresh={() => void paged.refresh()}
        onEndReached={paged.loadMore}
        hasMore={paged.hasMore}
        loadingMore={paged.loadingMore}
        search={{ value: filters.text, onChange: (v) => set({ text: v }), placeholder: t('Search title, status, route, or metadata...') }}
        filters={[
          { key: 'objective', label: t('Backlog Item'), value: filters.objectiveId, onChange: (v) => set({ objectiveId: v }), options: [{ value: 'all', label: t('All backlog items') }, ...objectives.map((o) => ({ value: o.id, label: o.title }))] },
          { key: 'vessel', label: t('Vessel'), value: filters.vesselId, onChange: (v) => set({ vesselId: v }), options: [{ value: 'all', label: t('All vessels') }, ...vessels.map((v) => ({ value: v.id, label: v.name }))] },
          { key: 'source', label: t('Source'), value: filters.sourceType, onChange: (v) => set({ sourceType: v }), options: [{ value: 'all', label: t('All source types') }, ...(filters.sourceType !== 'all' && !sourceTypes.includes(filters.sourceType) ? [filters.sourceType] : []).concat(sourceTypes).map((s) => ({ value: s, label: s }))] },
          { key: 'postmortem', label: t('Postmortem context only'), value: filters.postmortemOnly ? 'true' : 'all', onChange: (v) => set({ postmortemOnly: v === 'true' }), options: [{ value: 'all', label: t('All') }, { value: 'true', label: t('Postmortem context only') }] },
          { key: 'reads', label: t('Show GET requests'), value: filters.showReadRequests ? 'true' : 'all', onChange: (v) => set({ showReadRequests: v === 'true' }), options: [{ value: 'all', label: t('No') }, { value: 'true', label: t('Show GET requests') }] },
        ]}
        header={header}
        emptyTitle={t('No history entries match the current filters.')}
        emptyMessage={t('Try broadening the filters or refresh after running additional work in Armada.')}
        renderItem={(entry, index) => (
          <ResourceRow
            testID={`history-row-${index}`}
            title={entry.title}
            subtitle={[entry.sourceType, entry.actorDisplay, entry.vesselId ? (vesselNames.get(entry.vesselId) || entry.vesselId) : null, entry.description].filter(Boolean).join(' \u2022 ')}
            badge={entry.status ? { label: entry.status, tone: toneFor(entry.severity || entry.status) } : null}
            meta={formatRelativeTime(entry.occurredUtc)}
            onPress={entry.route ? () => open(entry) : undefined}
            actions={[
              ...(entry.metadataJson ? [{ key: 'json', label: t('View JSON'), icon: 'code-outline' as const, onPress: () => openMetadata(entry) }] : []),
              ...(entry.vesselId ? [{ key: 'workspace', label: t('Open Workspace'), icon: 'folder-open-outline' as const, onPress: () => router.push(`/workspace/${entry.vesselId}` as Href) }] : []),
              ...(canDeleteHistoryEntry(entry) ? [{ key: 'delete', label: t('Delete'), icon: 'trash-outline' as const, tone: 'danger' as const, onPress: () => remove(entry) }] : []),
            ]}
          />
        )}
      />
      <FormSheet
        testID="history-save-form"
        open={saveOpen}
        title={t('Save History View')}
        initial={{ name: '' }}
        fields={() => [{ kind: 'text', key: 'name', label: t('View Name'), placeholder: t('Staging failures'), required: true }]}
        submitLabel={t('Save')}
        onClose={() => setSaveOpen(false)}
        onSubmit={async (values) => {
          persist([{ id: `hsv_${Date.now()}`, name: str(values, 'name').trim(), filters, createdUtc: new Date().toISOString() }, ...savedViews]);
          setSaveOpen(false);
        }}
      />
      <JsonSheet open={json !== null} title={json?.title ?? ''} data={json?.data ?? null} onClose={() => setJson(null)} />
      {dialog}
    </>
  );
}

const styles = StyleSheet.create({
  actor: { marginHorizontal: spacing.md, marginBottom: spacing.sm },
  saved: { marginHorizontal: spacing.md, marginBottom: spacing.sm, gap: spacing.xs },
});
