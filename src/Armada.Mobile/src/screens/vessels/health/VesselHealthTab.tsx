import { useLocalSearchParams, useRouter, type Href } from 'expo-router';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { enumerateVesselHealth, getVesselHealthSummary, listFleets, listVessels } from '@dashboard/api/client';
import type { Fleet, Job, VesselHealth, VesselHealthStatus, VesselHealthSummary } from '@dashboard/types/models';
import { DEFAULT_HEALTH_FILTERS, buildEnumerateRequest, filtersFromQuery, hasActiveFilters, type HealthFilters } from '@dashboard/lib/health/healthFilters';
import { formatCount, statusLabel } from '@dashboard/lib/health/healthText';
import { describeEvaluationStart, useHealthEvaluation } from '@dashboard/lib/health/useHealthEvaluation';
import { ListPane } from '../../../build/ListPane';
import { useLiveResource, errorMessage } from '../../../build/useLiveResource';
import { usePagedList } from '../../../build/usePagedList';
import { useAuth } from '../../../auth/AuthContext';
import { AppText, Banner, Button, SwipeRow } from '../../../components/ui';
import { useLocale } from '../../../i18n/LocaleContext';
import { useNotifications } from '../../../notifications/NotificationContext';
import { useTheme } from '../../../theme/ThemeContext';
import { MIN_TOUCH, radius, spacing } from '../../../theme/typography';
import { HealthBadge } from './HealthBadge';
import { HealthDetailSheet, type HealthDetailSection } from './HealthDetailSheet';
import { HealthFilterSheet, activeFilterCount } from './HealthFilterSheet';
import { useHardwareBack } from '../../../navigation/useHardwareBack';

const NAME_DEBOUNCE_MS = 350;

/** Health filters from the route query (Home KPI links such as /vessels/health?overall=Fail). */
export function filtersFromParams(params: Record<string, string | string[] | undefined>): HealthFilters {
  const query = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    const v = Array.isArray(value) ? value[0] : value;
    if (typeof v === 'string' && key !== 'tab') query.set(key, v);
  }
  return { ...filtersFromQuery(query), page: 1 };
}

function parseJobResult(job: Job): { evaluated: number; failed: number } | null {
  if (!job.resultJson) return null;
  try {
    const parsed = JSON.parse(job.resultJson) as { evaluated?: number; failed?: number };
    return { evaluated: parsed.evaluated ?? 0, failed: parsed.failed ?? 0 };
  } catch {
    return null;
  }
}

interface Tile { key: string; value: number; status: VesselHealthStatus; text?: string }

/**
 * The Vessels hub Health tab (the dashboard's VesselHealth page, /vessels/health): summary tiles that filter by
 * overall status, a server-filtered, endlessly scrolling list, filters and sort in a sheet, evaluation with live
 * progress (tenant admins), per-vessel detail with findings, dependencies, and overrides, and bulk re-evaluate or
 * run-action on a long-press selection.
 */
export function VesselHealthTab() {
  const { t, locale, formatRelativeTime } = useLocale();
  const { colors } = useTheme();
  const { pushToast } = useNotifications();
  const { isTenantAdmin } = useAuth();
  const router = useRouter();
  const params = useLocalSearchParams<Record<string, string>>();
  const [filters, setFilters] = useState<HealthFilters>(() => filtersFromParams(params));
  const [nameDraft, setNameDraft] = useState(filters.name);
  const [filtersOpen, setFiltersOpen] = useState(false);
  const [detail, setDetail] = useState<{ vesselId: string; vesselName: string; section: HealthDetailSection } | null>(null);
  const [selected, setSelected] = useState<string[]>([]);
  const [refreshToken, setRefreshToken] = useState(0);
  const [fleets, setFleets] = useState<Fleet[]>([]);
  const [defaultBranches, setDefaultBranches] = useState<Record<string, string>>({});

  useEffect(() => {
    const handle = setTimeout(() => {
      const name = nameDraft.trim();
      setFilters((f) => (name === f.name.trim() ? f : { ...f, name, page: 1 }));
    }, NAME_DEBOUNCE_MS);
    return () => clearTimeout(handle);
  }, [nameDraft]);

  useEffect(() => {
    listFleets({ pageSize: 9999 }).then((r) => setFleets(r?.objects ?? [])).catch(() => setFleets([]));
    listVessels({ pageSize: 9999 }).then((r) => {
      const map: Record<string, string> = {};
      for (const v of r?.objects ?? []) if (v.defaultBranch) map[v.id] = v.defaultBranch;
      setDefaultBranches(map);
    }).catch(() => setDefaultBranches({}));
  }, []);

  const filterKey = JSON.stringify(filters);
  const list = usePagedList<VesselHealth>(
    (pageNumber, pageSize) => enumerateVesselHealth(buildEnumerateRequest({ ...filters, page: pageNumber }, pageSize)),
    [filterKey],
  );
  const summary = useLiveResource<VesselHealthSummary>(() => getVesselHealthSummary(), []);
  // Selection never survives a filter or sort change.
  const [selectionKey, setSelectionKey] = useState(filterKey);
  if (selectionKey !== filterKey) {
    setSelectionKey(filterKey);
    setSelected([]);
  }

  const { reload: reloadList } = list;
  const { reload: reloadSummary } = summary;
  const refreshAll = useCallback(async () => { await Promise.all([reloadList(), reloadSummary()]); }, [reloadList, reloadSummary]);

  const evaluation = useHealthEvaluation({
    onFinished: (job) => {
      const result = parseJobResult(job);
      if (job.status === 'Succeeded') {
        pushToast(result && result.failed > 0 ? 'warning' : 'success', result
          ? t('Evaluation finished: {{evaluated}} evaluated, {{failed}} failed.', { evaluated: formatCount(locale, result.evaluated), failed: formatCount(locale, result.failed) })
          : t('Evaluation finished.'));
      } else if (job.status === 'Failed') {
        pushToast('error', t('Evaluation failed: {{reason}}', { reason: job.errorReason ?? t('Unknown error') }));
      } else {
        pushToast('warning', t('Evaluation was cancelled.'));
      }
      setRefreshToken((n) => n + 1);
      void refreshAll();
    },
  });
  const { discover } = evaluation;
  useEffect(() => { void discover(); }, [discover]);

  const startEvaluation = useCallback(async (vesselIds?: string[]) => {
    try {
      const body = vesselIds && vesselIds.length > 0 ? { VesselIds: vesselIds, Force: true } : { Force: true };
      const start = await evaluation.start(body);
      const message = describeEvaluationStart(start);
      const values: Record<string, string> = {};
      for (const [k, v] of Object.entries(message.params)) values[k] = formatCount(locale, v);
      pushToast(message.severity, t(message.key, values));
      return true;
    } catch (e) {
      pushToast('error', t('Could not start the evaluation: {{message}}', { message: errorMessage(e) }));
      return false;
    }
  }, [evaluation, locale, pushToast, t]);

  const s = summary.data;
  const tiles: Tile[] = useMemo(() => (s ? [
    { key: 'fail', value: s.fail, status: 'Fail' as VesselHealthStatus },
    { key: 'warn', value: s.warn, status: 'Warn' as VesselHealthStatus },
    { key: 'pass', value: s.pass, status: 'Pass' as VesselHealthStatus },
    { key: 'unknown', value: s.unknown, status: 'Unknown' as VesselHealthStatus },
    ...(s.notApplicable > 0 ? [{ key: 'notApplicable', value: s.notApplicable, status: 'NotApplicable' as VesselHealthStatus }] : []),
    { key: 'notEvaluated', value: s.notEvaluated, status: 'Unknown' as VesselHealthStatus, text: 'Not evaluated' },
  ] : []), [s]);

  const filtered = hasActiveFilters(filters);
  const noneEvaluated = !!s && s.totalVessels > 0 && s.notEvaluated === s.totalVessels;
  const noVessels = !list.loading && !list.error && list.totalRecords === 0 && !filtered && (s ? s.totalVessels === 0 : true);
  const selecting = selected.length > 0;
  useHardwareBack(selecting, () => setSelected([]));
  const toggleSelected = (id: string) => setSelected((cur) => (cur.includes(id) ? cur.filter((x) => x !== id) : [...cur, id]));
  const openDetail = (row: VesselHealth, section: HealthDetailSection = 'summary') =>
    setDetail({ vesselId: row.vesselId, vesselName: row.vesselName ?? row.vesselId, section });
  const runAction = (ids: string[]) => router.push(`/fleet-actions?run=new&kind=Mission&vessels=${ids.map(encodeURIComponent).join(',')}` as Href);

  const header = (
    <View>
      <View style={styles.tiles} accessibilityLabel={t('Health summary')}>
        {tiles.map((tile) => {
          const active = !tile.text && filters.overall.length === 1 && filters.overall[0] === tile.status;
          const label = tile.text ? t(tile.text) : statusLabel(t, tile.status);
          return (
            <Pressable
              key={tile.key}
              testID={`health-tile-${tile.key}`}
              accessibilityRole="button"
              accessibilityLabel={`${label}: ${formatCount(locale, tile.value)}`}
              accessibilityState={{ selected: active }}
              onPress={() => setFilters((f) => ({ ...f, overall: active ? [] : [tile.status], page: 1 }))}
              style={[styles.tile, { backgroundColor: active ? colors.surfaceRaised : colors.surface, borderColor: active ? colors.primary : colors.border }]}
            >
              <AppText variant="heading">{formatCount(locale, tile.value)}</AppText>
              {tile.text ? <AppText variant="caption" muted>{t(tile.text)}</AppText> : <HealthBadge status={tile.status} />}
            </Pressable>
          );
        })}
      </View>
      {summary.error && !s ? <AppText variant="caption" muted style={styles.meta}>{t('Summary unavailable.')}</AppText> : null}
      <View style={styles.metaRow}>
        {s ? <AppText variant="caption" muted>{t('{{count}} vessels', { count: formatCount(locale, s.totalVessels) })}</AppText> : null}
        {evaluation.running ? (
          <View style={styles.progress} accessibilityRole="progressbar" accessibilityLiveRegion="polite" testID="health-evaluating">
            <AppText variant="caption">{evaluation.activeJob ? t('Evaluating... {{percent}}%', { percent: formatCount(locale, evaluation.activeJob.progress ?? 0) }) : t('Evaluating...')}</AppText>
            <View style={[styles.track, { backgroundColor: colors.border }]}>
              <View style={[styles.bar, { backgroundColor: colors.primary, width: `${Math.max(3, Math.min(100, evaluation.activeJob?.progress ?? 0))}%` }]} />
            </View>
          </View>
        ) : evaluation.lastJob ? (
          <AppText variant="caption" muted>{t('Last evaluation {{when}}', { when: formatRelativeTime(evaluation.lastJob.completedUtc ?? evaluation.lastJob.lastUpdateUtc) })}</AppText>
        ) : null}
      </View>
      {isTenantAdmin ? (
        <View style={styles.pad}>
          <Button
            label={evaluation.running ? t('Evaluating...') : t('Evaluate all')}
            disabled={evaluation.running || evaluation.starting}
            onPress={() => { void startEvaluation(); }}
            icon="pulse-outline"
            testID="health-evaluate-all"
          />
        </View>
      ) : null}
      {noneEvaluated && !noVessels ? (
        <Banner tone="info" title={t('No vessel has been evaluated yet.')} message={t('Run an evaluation to grade every vessel. Dependency checks can take a minute per repository.')} />
      ) : null}
      {selecting ? (
        <View style={[styles.bulk, { borderColor: colors.primary, backgroundColor: colors.surface }]} accessibilityLabel={t('Bulk actions')} testID="health-bulk">
          <AppText variant="label">{t(selected.length === 1 ? '{{count}} vessel selected' : '{{count}} vessels selected', { count: formatCount(locale, selected.length) })}</AppText>
          <View style={styles.bulkActions}>
            {isTenantAdmin ? (
              <>
                <Button label={t('Re-evaluate selected')} disabled={evaluation.running || evaluation.starting} onPress={() => { void startEvaluation([...selected]).then((ok) => { if (ok) setSelected([]); }); }} />
                <Button label={t('Run action...')} variant="secondary" onPress={() => runAction([...selected])} />
              </>
            ) : null}
            <Button label={t('Clear selection')} variant="ghost" onPress={() => setSelected([])} />
          </View>
        </View>
      ) : null}
    </View>
  );

  const renderRow = ({ item: row }: { item: VesselHealth }) => {
    const isSel = selected.includes(row.vesselId);
    const ahead = row.aheadOfDefault ?? 0;
    const behind = row.behindDefault ?? 0;
    const measured = !((row.aheadOfDefault === null || row.aheadOfDefault === undefined) && (row.behindDefault === null || row.behindDefault === undefined));
    const base = defaultBranches[row.vesselId] || t('the default branch');
    const divergence = measured ? t('{{ahead}} ahead and {{behind}} behind {{base}}', { ahead: formatCount(locale, ahead), behind: formatCount(locale, behind), base }) : null;
    const dirty = row.isDirty === null || row.isDirty === undefined ? null
      : row.isDirty ? t('Dirty') : (row.untrackedCount ?? 0) > 0 ? t('{{count}} untracked', { count: formatCount(locale, row.untrackedCount ?? 0) }) : t('Clean');
    const meta = [row.currentBranch, row.fleetName, dirty, row.evaluatedUtc ? t('Evaluated {{when}}', { when: formatRelativeTime(row.evaluatedUtc) }) : t('Never evaluated')]
      .filter(Boolean).join(' - ');
    const actions = [
      { key: 'details', label: t('View details'), icon: 'information-circle-outline' as const, onPress: () => openDetail(row) },
      ...(isTenantAdmin ? [
        { key: 'override', label: t('Override...'), icon: 'create-outline' as const, onPress: () => openDetail(row, 'overrides') },
        { key: 'reevaluate', label: t('Re-evaluate'), icon: 'refresh' as const, onPress: () => { void startEvaluation([row.vesselId]); } },
      ] : []),
      { key: 'open', label: t('Open vessel'), icon: 'open-outline' as const, onPress: () => router.push(`/vessels/${encodeURIComponent(row.vesselId)}` as Href) },
    ];
    return (
      <SwipeRow actions={actions} testID={`health-row-${row.vesselId}`}>
        <Pressable
          accessibilityRole="button"
          accessibilityLabel={[row.vesselName || row.vesselId, statusLabel(t, row.overallStatus), divergence, meta].filter(Boolean).join(', ')}
          accessibilityHint={t('Long press to select')}
          accessibilityState={{ selected: isSel }}
          onPress={() => (selecting ? toggleSelected(row.vesselId) : openDetail(row))}
          onLongPress={() => toggleSelected(row.vesselId)}
          style={({ pressed }) => [styles.row, { backgroundColor: isSel ? colors.surfaceRaised : pressed ? colors.background : colors.surface, borderBottomColor: colors.border }, isSel ? { borderLeftWidth: 3, borderLeftColor: colors.primary } : null]}
          testID={`health-open-${row.vesselName || row.vesselId}`}
        >
          <View style={styles.rowHead}>
            <AppText variant="label" style={styles.flex} numberOfLines={1}>{row.vesselName || row.vesselId}</AppText>
            <HealthBadge status={row.overallStatus} />
          </View>
          {meta ? <AppText variant="caption" muted numberOfLines={2}>{meta}</AppText> : null}
          <View style={styles.badges}>
            {measured ? <AppText variant="caption" accessibilityLabel={divergence ?? undefined}>{`\u2191${formatCount(locale, ahead)} \u2193${formatCount(locale, behind)}`}</AppText> : null}
            <HealthBadge status={row.dependencyStatus} prefix={t('Dependencies')} />
            <HealthBadge status={row.vulnerabilityStatus} prefix={t('Vulnerabilities')} />
            <HealthBadge status={row.testInfraStatus} prefix={t('Tests')} />
            <HealthBadge status={row.ciStatus} prefix={t('CI')} />
          </View>
        </Pressable>
      </SwipeRow>
    );
  };

  return (
    <View style={styles.fill} testID="vessel-health">
      <ListPane<VesselHealth>
        testID="vessel-health-list"
        items={noVessels ? [] : list.items}
        keyOf={(r) => r.vesselId}
        renderItem={renderRow}
        loading={list.loading}
        refreshing={list.refreshing}
        error={list.error ? t('Could not load vessel health: {{message}}', { message: list.error }) : null}
        onRefresh={() => { void Promise.all([list.refresh(), summary.reload()]); }}
        onEndReached={list.loadMore}
        loadingMore={list.loadingMore}
        search={{ value: nameDraft, onChange: setNameDraft, placeholder: t('Vessel name contains') }}
        actions={[{ key: 'filters', icon: 'options-outline', label: t('Health filters'), onPress: () => setFiltersOpen(true), badge: activeFilterCount(filters) }]}
        summary={list.loading ? null : t('{{count}} vessels', { count: formatCount(locale, list.totalRecords) })}
        header={header}
        emptyTitle={noVessels ? t('No vessels yet') : t('No vessels match the current filters.')}
        emptyMessage={noVessels ? t('Import repositories to start tracking their health.') : undefined}
        emptyAction={noVessels
          ? { label: t('Import repositories'), onPress: () => router.push('/vessels/import' as Href) }
          : filtered ? { label: t('Clear filters'), onPress: () => { setNameDraft(''); setFilters({ ...DEFAULT_HEALTH_FILTERS, sortBy: filters.sortBy, sortDesc: filters.sortDesc }); } } : undefined}
      />
      <HealthFilterSheet
        open={filtersOpen}
        filters={filters}
        fleets={fleets}
        onApply={(next) => { setFilters(next); setFiltersOpen(false); }}
        onClose={() => setFiltersOpen(false)}
      />
      {detail ? (
        <HealthDetailSheet
          vesselId={detail.vesselId}
          vesselName={detail.vesselName}
          defaultBranch={defaultBranches[detail.vesselId]}
          initialSection={detail.section}
          canAdmin={isTenantAdmin}
          evaluationRunning={evaluation.running}
          refreshToken={refreshToken}
          onReevaluate={(id) => { void startEvaluation([id]); }}
          onChanged={() => { void refreshAll(); }}
          onClose={() => setDetail(null)}
        />
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  flex: { flex: 1 },
  pad: { paddingHorizontal: spacing.md },
  tiles: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm, paddingHorizontal: spacing.md, marginBottom: spacing.sm },
  tile: { flexGrow: 1, minWidth: 96, minHeight: MIN_TOUCH, borderWidth: 1, borderRadius: radius.md, padding: spacing.sm, gap: spacing.xs },
  meta: { marginHorizontal: spacing.lg },
  metaRow: { flexDirection: 'row', flexWrap: 'wrap', alignItems: 'center', gap: spacing.md, marginHorizontal: spacing.lg, marginBottom: spacing.md },
  progress: { flex: 1, minWidth: 160, gap: spacing.xs },
  track: { height: 6, borderRadius: 3, overflow: 'hidden' },
  bar: { height: 6 },
  bulk: { borderWidth: 1, borderRadius: radius.md, padding: spacing.md, marginHorizontal: spacing.md, marginBottom: spacing.md, gap: spacing.sm },
  bulkActions: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm },
  row: { paddingHorizontal: spacing.lg, paddingVertical: spacing.sm, gap: spacing.xs, borderBottomWidth: StyleSheet.hairlineWidth, minHeight: MIN_TOUCH + 8 },
  rowHead: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  badges: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs, alignItems: 'center' },
});
