import { Stack, useRouter, type Href } from 'expo-router';
import { useCallback, useMemo, useState } from 'react';
import { Pressable, RefreshControl, StyleSheet, View } from 'react-native';
import {
  deleteMission,
  enumerateFleetActionRuns,
  getHealth,
  getStatus,
  getVesselHealthSummary,
  listMissionSummaries,
  restartMission,
} from '@dashboard/api/client';
import {
  activeFleetActionRunsLink,
  canRestartFromHome,
  dashboardAlerts,
  filterRecentMissions,
  RECENT_MISSION_STATUSES,
  serverHealthIndicator,
  totalMissionCount,
  voyagePercent,
  type DashboardStatusData,
  type VoyageProgress,
} from '@dashboard/lib/dashboardStatus';
import { HEALTH_KPI_LINKS } from '@dashboard/lib/health/healthKpiLinks';
import type { MissionSummary, VesselHealthSummary } from '@dashboard/types/models';
import { useAuth } from '../../auth/AuthContext';
import { DefaultCredentialsBanner } from '../../components/app/DefaultCredentialsBanner';
import { EntityStatusBadge } from '../../components/app/EntityStatusBadge';
import { activeFilterCount, FilterButton } from '../../components/app/FilterSheet';
import { JsonSheet } from '../../components/app/JsonSheet';
import { MissionHistoryChart } from '../../components/app/MissionHistoryChart';
import { useConfirm } from '../../components/app/useConfirm';
import {
  ActionSheet,
  AppText,
  Button,
  ErrorState,
  Icon,
  KpiCard,
  KpiGrid,
  ListRow,
  LoadingState,
  ProgressBar,
  Screen,
  Section,
  SelectField,
  StatusBadge,
  type SheetAction,
} from '../../components/ui';
import { useInterval } from '../../data/useInterval';
import { useLiveRefresh } from '../../data/useLiveRefresh';
import { useNameLookups } from '../../data/useNameLookups';
import { useQuery } from '../../data/useQuery';
import { useLocale } from '../../i18n/LocaleContext';
import { statusTone } from '../../lib/statusTone';
import { useLayout } from '../../navigation/useLayout';
import { useNotifications } from '../../notifications/NotificationContext';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing } from '../../theme/typography';
import { NavSections } from '../NavMenuScreen';

/** The dashboard's Home polling fallback (the socket is the primary update source). */
export const HOME_POLL_MS = 30000;

interface HomeData {
  status: DashboardStatusData | null;
  missions: MissionSummary[] | null;
  activeRuns: { pending: number; running: number } | null;
  health: Record<string, unknown> | null;
  vesselHealth: VesselHealthSummary | null;
}

async function loadHome(): Promise<HomeData> {
  const [status, missions, pending, running, health, vesselHealth] = await Promise.all([
    getStatus().catch(() => null),
    listMissionSummaries({ pageSize: 10 }).catch(() => null),
    enumerateFleetActionRuns({ status: 'Pending', pageSize: 1 }).catch(() => null),
    enumerateFleetActionRuns({ status: 'Running', pageSize: 1 }).catch(() => null),
    getHealth().catch(() => null),
    getVesselHealthSummary().catch(() => null),
  ]);
  if (!status && !missions) throw new Error('Failed to load dashboard data.');
  return {
    status: status as unknown as DashboardStatusData | null,
    missions: missions?.objects ?? null,
    activeRuns: pending && running ? { pending: pending.totalRecords, running: running.totalRecords } : null,
    health,
    vesselHealth,
  };
}

/**
 * Home (the dashboard's System Status page, W2.1): server health, quick actions, alert banners, KPI cards, vessel
 * health tiles, the mission history chart, voyage progress, recent missions (filters and row actions), and recent
 * signals. Live: reloads on WebSocket activity (debounced), on reconnect, and every 30 s in the foreground. On
 * phones it is the Work tab's root, so the OPERATIONS, DELIVERY, and BUILD destinations follow the overview.
 */
export function HomeScreen() {
  const { t, formatRelativeTime } = useLocale();
  const { colors } = useTheme();
  const { isTablet } = useLayout();
  const { isTenantAdmin } = useAuth();
  const { pushToast } = useNotifications();
  const router = useRouter();
  const lookups = useNameLookups({ vessels: true, captains: true, fleets: true });
  const [refreshToken, setRefreshToken] = useState(0);
  const query = useQuery(loadHome, [], t('Failed to load dashboard data.'));
  const [confirmDialog, confirm] = useConfirm('home-confirm');
  const [json, setJson] = useState<{ title: string; data: unknown } | null>(null);
  const [rowMenu, setRowMenu] = useState<MissionSummary | null>(null);
  const [filters, setFilters] = useState({ status: '', vesselId: '', captainId: '' });

  useLiveRefresh([], () => { void query.reload(); });
  useInterval(() => { void query.reload(); }, HOME_POLL_MS);

  const go = useCallback((path: string) => router.push(path as Href), [router]);
  const data = query.data;
  const status = data?.status ?? null;
  const alerts = useMemo(() => dashboardAlerts(status), [status]);
  const recent = useMemo(() => filterRecentMissions(data?.missions ?? [], filters), [data, filters]);

  const refresh = async () => {
    lookups.reload();
    setRefreshToken((n) => n + 1);
    await query.refresh();
  };

  const missionActions = (m: MissionSummary): SheetAction[] => [
    { key: 'view', label: t('View Detail'), icon: 'open-outline', onPress: () => go(`/missions/${m.id}`) },
    { key: 'json', label: t('View JSON'), icon: 'code-slash-outline', onPress: () => setJson({ title: `${t('Mission')}: ${m.title}`, data: m }) },
    ...(canRestartFromHome(m.status) ? [{
      key: 'restart', label: t('Restart'), icon: 'refresh' as const,
      onPress: () => { void restartMission(m.id).then(() => query.reload()).catch(() => undefined); },
    }] : []),
    {
      key: 'delete', label: t('Delete'), icon: 'trash-outline', danger: true,
      onPress: () => confirm({
        title: t('Delete Mission'),
        message: t('Delete this mission?'),
        confirmLabel: t('Delete'),
        danger: true,
        onConfirm: async () => {
          try {
            await deleteMission(m.id);
            void query.reload();
          } catch {
            pushToast('error', t('Failed to delete mission.'));
          }
        },
      }),
    },
  ];

  if (query.loading && !data) return <Screen scroll={false}><Stack.Screen options={{ title: t('Dashboard') }} /><LoadingState label={t('Loading dashboard...')} /></Screen>;

  const healthState = data ? serverHealthIndicator(data.health) : 'error';
  const healthLabel = healthState === 'healthy' ? t('Healthy') : healthState === 'warning' ? t('Degraded') : t('Unreachable');
  const version = data?.health ? String(data.health.version ?? data.health.Version ?? '') : '';
  const uptime = data?.health ? String(data.health.uptime ?? data.health.Uptime ?? '') : '';
  const ms = status?.missionsByStatus ?? {};

  return (
    <Screen
      testID="home-screen"
      maxWidth={isTablet ? 1100 : 720}
      refreshControl={<RefreshControl refreshing={query.refreshing} onRefresh={() => void refresh()} tintColor={colors.primary} />}
    >
      <Stack.Screen options={{ title: t('Dashboard') }} />
      <DefaultCredentialsBanner />
      <View style={styles.header}>
        <View style={styles.flex}>
          <AppText variant="title" accessibilityRole="header">{t('System Status')}</AppText>
          <AppText muted>{t('Overview of fleet health, active missions, and recent activity.')}</AppText>
        </View>
      </View>
      <View style={styles.healthRow} testID="home-health" accessible accessibilityLabel={[`${t('Server')}: ${healthLabel}`, version ? `v${version}` : null, uptime ? t('Uptime: {{uptime}}', { uptime }) : null].filter(Boolean).join(', ')}>
        <StatusBadge label={healthLabel} tone={healthState === 'healthy' ? 'success' : healthState === 'warning' ? 'warning' : 'failed'} />
        {version ? <AppText variant="caption" muted>{`v${version}`}</AppText> : null}
        {uptime ? <AppText variant="caption" muted>{t('Uptime: {{uptime}}', { uptime })}</AppText> : null}
      </View>
      {query.error && !data ? <ErrorState title={t('Something went wrong')} message={query.error} retryLabel={t('Retry')} onRetry={() => void query.refresh()} /> : null}

      <View style={[styles.hero, { backgroundColor: colors.surface, borderColor: colors.border }]}>
        <AppText variant="heading" accessibilityRole="header">{t('Ask Armada')}</AppText>
        <AppText muted>{t('Ask about fleet state in plain language and dispatch work straight from the conversation.')}</AppText>
        <View style={styles.wrapRow}>
          <Button label={t('Ask Armada')} icon="chatbubbles-outline" onPress={() => go('/ask')} testID="home-ask" />
          <Button label={t('Needs You')} variant="secondary" onPress={() => go('/inbox')} testID="home-needs-you" />
          <Button label={t('Dispatch')} variant="secondary" icon="paper-plane-outline" onPress={() => go('/dispatch')} testID="home-dispatch" />
          <Button label={t('Voyage')} variant="secondary" icon="add" onPress={() => go('/voyages/create')} testID="home-voyage" />
          <Button label={t('Diagnostics')} variant="ghost" onPress={() => go('/server?tab=diagnostics')} testID="home-diagnostics" />
        </View>
      </View>

      {alerts.length > 0 ? (
        <View style={styles.alerts} testID="home-alerts">
          {alerts.map((alert, i) => (
            <Pressable
              key={i}
              disabled={!alert.link}
              onPress={() => alert.link && go(alert.link)}
              accessibilityRole={alert.link ? 'button' : 'alert'}
              accessibilityLabel={[alert.message, alert.action].filter(Boolean).join(' ')}
              style={[styles.alert, { borderColor: alert.level === 'error' ? colors.danger : colors.warning, backgroundColor: colors.warningSurface }]}
            >
              <Icon name={alert.level === 'error' ? 'alert-circle' : 'warning-outline'} color={alert.level === 'error' ? 'danger' : 'warning'} />
              <View style={styles.flex}>
                <AppText variant="label">{alert.message}</AppText>
                {alert.action ? <AppText variant="caption" muted>{alert.action}</AppText> : null}
              </View>
              {/* The role already says it opens something; the word is for sighted users. */}
              {alert.link ? <AppText variant="label" color="primary" accessibilityElementsHidden importantForAccessibility="no-hide-descendants">{t('View')}</AppText> : null}
            </Pressable>
          ))}
        </View>
      ) : null}

      <KpiGrid>
        <KpiCard label={t('Captains')} value={status?.totalCaptains ?? 0} onPress={() => go('/captains')} testID="home-kpi-captains"
          accessibilityValue={[`${status?.idleCaptains ?? 0} idle`, `${status?.workingCaptains ?? 0} working`, (status?.stalledCaptains ?? 0) > 0 ? `${status?.stalledCaptains ?? 0} stalled` : null].filter(Boolean).join(', ')}
        >
          <StatusBadge label={`${status?.idleCaptains ?? 0} idle`} tone="success" />
          <StatusBadge label={`${status?.workingCaptains ?? 0} working`} tone="running" />
          {(status?.stalledCaptains ?? 0) > 0 ? <StatusBadge label={`${status?.stalledCaptains ?? 0} stalled`} tone="warning" /> : null}
        </KpiCard>
        <KpiCard label={t('Active Voyages')} value={status?.activeVoyages ?? 0} onPress={() => go('/missions?tab=voyages')} testID="home-kpi-voyages"
          accessibilityValue={(status?.memoryPressureDeferrals ?? 0) > 0 ? t('{{count}} deferred for memory pressure', { count: status?.memoryPressureDeferrals ?? 0 }) : undefined}
        >
          {(status?.memoryPressureDeferrals ?? 0) > 0 ? (
            <StatusBadge label={t('{{count}} deferred for memory pressure', { count: status?.memoryPressureDeferrals ?? 0 })} tone="warning" />
          ) : null}
        </KpiCard>
        <KpiCard label={t('Missions')} value={totalMissionCount(status)} onPress={() => go('/missions')} testID="home-kpi-missions"
          accessibilityValue={Object.entries(ms).map(([key, value]) => `${value} ${t(key)}`).join(', ')}
        >
          {Object.entries(ms).map(([key, value]) => <EntityStatusBadgeCount key={key} status={key} count={value} />)}
        </KpiCard>
        <KpiCard
          label={t('Active fleet action runs')}
          value={data?.activeRuns ? (data.activeRuns.pending + data.activeRuns.running).toLocaleString() : '-'}
          onPress={() => go(activeFleetActionRunsLink(data?.activeRuns ?? null))}
          testID="home-kpi-fleet-actions"
          accessibilityValue={data?.activeRuns ? [t('{{count}} running', { count: data.activeRuns.running.toLocaleString() }), t('{{count}} pending', { count: data.activeRuns.pending.toLocaleString() })].join(', ') : undefined}
        >
          {data?.activeRuns ? (
            <>
              <StatusBadge label={t('{{count}} running', { count: data.activeRuns.running.toLocaleString() })} tone="running" />
              <StatusBadge label={t('{{count}} pending', { count: data.activeRuns.pending.toLocaleString() })} tone="pending" />
            </>
          ) : null}
        </KpiCard>
      </KpiGrid>

      {isTenantAdmin ? (
        <Section>
          <ListRow testID="home-import" icon="download-outline" title={t('Import repositories')} subtitle={t('Discover local git repositories and onboard them as vessels in bulk.')} onPress={() => go('/vessels/import')} />
          <ListRow testID="home-fleet-action" icon="flash-outline" title={t('Run fleet action')} subtitle={t('Run a command or a captain mission across many vessels at once.')} onPress={() => go('/fleet-actions?tab=actions&run=new')} />
        </Section>
      ) : null}

      {data?.vesselHealth && data.vesselHealth.totalVessels > 0 ? (
        <KpiGrid>
          <KpiCard label={t('Vessels failing health')} value={data.vesselHealth.fail} onPress={() => go(HEALTH_KPI_LINKS.failing)} testID="home-health-failing"
            accessibilityValue={t('{{count}} warn, {{unknown}} not evaluated', { count: data.vesselHealth.warn, unknown: data.vesselHealth.notEvaluated })}
          >
            <AppText variant="caption" muted>{t('{{count}} warn, {{unknown}} not evaluated', { count: data.vesselHealth.warn, unknown: data.vesselHealth.notEvaluated })}</AppText>
          </KpiCard>
          <KpiCard label={t('Outdated majors')} value={data.vesselHealth.outdatedMajorVessels} onPress={() => go(HEALTH_KPI_LINKS.outdatedMajors)} testID="home-health-outdated" accessibilityValue={t('Vessels with a dependency a major version behind')}>
            <AppText variant="caption" muted>{t('Vessels with a dependency a major version behind')}</AppText>
          </KpiCard>
          <KpiCard label={t('High/critical vulnerabilities')} value={data.vesselHealth.highOrCriticalVulnerabilityVessels} onPress={() => go(HEALTH_KPI_LINKS.vulnerable)} testID="home-health-vulnerable" accessibilityValue={t('Vessels with a high or critical advisory')}>
            <AppText variant="caption" muted>{t('Vessels with a high or critical advisory')}</AppText>
          </KpiCard>
        </KpiGrid>
      ) : null}

      <MissionHistoryChart vessels={lookups.vessels} fleets={lookups.fleets} refreshToken={refreshToken} />

      {status?.voyages && status.voyages.length > 0 ? (
        <Section title={t('Voyage Progress')}>
          {status.voyages.map((vp) => (
            <VoyageProgressRow
              key={vp.voyage?.id}
              vp={vp}
              vesselNames={(vp.vesselIds ?? []).map((id) => lookups.vesselName(id)).join(', ') || '-'}
              onPress={() => go(`/voyages/${vp.voyage?.id}`)}
              onLongPress={() => setJson({ title: `${t('Voyage')}: ${vp.voyage?.title || vp.voyage?.id}`, data: vp.voyage })}
            />
          ))}
        </Section>
      ) : null}

      <View style={styles.sectionHead}>
        <AppText variant="subheading" muted accessibilityRole="header" style={styles.upper}>{t('Recent Missions')}</AppText>
      </View>
      <View style={styles.toolbar}>
        <FilterButton count={activeFilterCount(filters)} onClear={() => setFilters({ status: '', vesselId: '', captainId: '' })} testID="home-mission-filters">
          <SelectField label={t('Status')} value={filters.status} onChange={(status) => setFilters((f) => ({ ...f, status }))} allowEmpty placeholder={t('All Statuses')} closeLabel={t('Close')} options={RECENT_MISSION_STATUSES.map((s) => ({ value: s, label: t(s) }))} testID="home-filter-status" />
          <SelectField label={t('Vessel')} value={filters.vesselId} onChange={(vesselId) => setFilters((f) => ({ ...f, vesselId }))} allowEmpty placeholder={t('All Vessels')} closeLabel={t('Close')} options={lookups.vessels.map((v) => ({ value: v.id, label: v.name }))} testID="home-filter-vessel" />
          <SelectField label={t('Captain')} value={filters.captainId} onChange={(captainId) => setFilters((f) => ({ ...f, captainId }))} allowEmpty placeholder={t('All Captains')} closeLabel={t('Close')} options={lookups.captains.map((c) => ({ value: c.id, label: c.name }))} testID="home-filter-captain" />
        </FilterButton>
        <Button label={t('View All')} variant="ghost" icon="arrow-forward" onPress={() => go('/missions')} testID="home-missions-all" style={styles.flex} />
      </View>
      <Section>
        {recent.length === 0 ? <ListRow title={t('No missions found.')} /> : recent.map((m) => (
          <ListRow
            key={m.id}
            testID={`home-mission-${m.id}`}
            title={m.title}
            subtitle={`${lookups.vesselName(m.vesselId)} - ${lookups.captainName(m.captainId)} - ${formatRelativeTime(m.createdUtc)}`}
            accessory={<EntityStatusBadge status={m.status} />}
            accessibilityValue={t(m.status)}
            menu={{ label: t('Actions'), onPress: () => setRowMenu(m), testID: `home-mission-${m.id}-menu` }}
            onPress={() => go(`/missions/${m.id}`)}
            onLongPress={() => setRowMenu(m)}
          />
        ))}
      </Section>

      {status?.recentSignals && status.recentSignals.length > 0 ? (
        <Section title={t('Recent Signals')}>
          {status.recentSignals.slice(0, 5).map((sig) => (
            <ListRow
              key={sig.id}
              testID={`home-signal-${sig.id}`}
              title={sig.payload || sig.message || sig.type}
              subtitle={formatRelativeTime(sig.createdUtc)}
              accessory={<EntityStatusBadge status={sig.type} />}
              accessibilityValue={t(sig.type)}
              onPress={() => go(`/signals/${sig.id}`)}
            />
          ))}
        </Section>
      ) : null}

      {!isTablet ? <View testID="work-menu"><NavSections tab="work" /></View> : null}

      <ActionSheet
        open={rowMenu !== null}
        title={rowMenu?.title ?? ''}
        actions={rowMenu ? missionActions(rowMenu) : []}
        onClose={() => setRowMenu(null)}
        closeLabel={t('Close')}
        testID="home-mission-menu"
      />
      <JsonSheet open={json !== null} title={json?.title ?? ''} data={json?.data} onClose={() => setJson(null)} />
      {confirmDialog}
    </Screen>
  );
}

function EntityStatusBadgeCount({ status, count }: { status: string; count: number }) {
  const { t } = useLocale();
  return <View accessible accessibilityLabel={`${count} ${t(status)}`}><StatusBadge label={`${count} ${t(status)}`} tone={statusTone(status)} /></View>;
}

function VoyageProgressRow({ vp, vesselNames, onPress, onLongPress }: { vp: VoyageProgress; vesselNames: string; onPress: () => void; onLongPress: () => void }) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const percent = voyagePercent(vp);
  return (
    <Pressable
      testID={`home-voyage-${vp.voyage?.id}`}
      accessibilityRole="button"
      accessibilityLabel={`${vp.voyage?.title || vp.voyage?.id}, ${t(vp.voyage?.status ?? '')}, ${percent}%`}
      accessibilityValue={{ text: [vesselNames, `${vp.completedMissions}/${vp.totalMissions} ${t('done')}${vp.failedMissions > 0 ? `, ${vp.failedMissions} ${t('failed')}` : ''}`].filter(Boolean).join(', ') }}
      onPress={onPress}
      onLongPress={onLongPress}
      style={({ pressed }) => [styles.voyage, { borderBottomColor: colors.border, opacity: pressed ? 0.7 : 1 }]}
    >
      <View style={styles.voyageHead}>
        <AppText variant="label" style={styles.flex} numberOfLines={2}>{vp.voyage?.title || vp.voyage?.id}</AppText>
        <EntityStatusBadge status={vp.voyage?.status} />
      </View>
      <AppText variant="caption" muted numberOfLines={1}>{vesselNames}</AppText>
      <View style={styles.progressRow}>
        <ProgressBar percent={percent} label={t('Progress')} />
        <AppText variant="caption" muted>{`${percent}%`}</AppText>
      </View>
      <AppText variant="caption">
        {`${vp.completedMissions}/${vp.totalMissions} ${t('done')}${vp.failedMissions > 0 ? `, ${vp.failedMissions} ${t('failed')}` : ''}`}
      </AppText>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  header: { flexDirection: 'row', alignItems: 'center', marginHorizontal: spacing.lg, marginBottom: spacing.sm },
  healthRow: { flexDirection: 'row', alignItems: 'center', flexWrap: 'wrap', gap: spacing.sm, marginHorizontal: spacing.lg, marginBottom: spacing.lg },
  flex: { flex: 1 },
  hero: { borderWidth: StyleSheet.hairlineWidth, borderRadius: radius.md, padding: spacing.md, marginHorizontal: spacing.md, marginBottom: spacing.xl, gap: spacing.sm },
  wrapRow: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm },
  alerts: { gap: spacing.sm, marginHorizontal: spacing.md, marginBottom: spacing.xl },
  alert: { flexDirection: 'row', alignItems: 'center', gap: spacing.md, borderWidth: 1, borderLeftWidth: 4, borderRadius: radius.md, padding: spacing.md },
  sectionHead: { marginHorizontal: spacing.lg, marginBottom: spacing.sm },
  upper: { textTransform: 'uppercase' },
  toolbar: { flexDirection: 'row', gap: spacing.sm, marginHorizontal: spacing.md },
  voyage: { paddingHorizontal: spacing.lg, paddingVertical: spacing.md, gap: spacing.xs, borderBottomWidth: StyleSheet.hairlineWidth },
  voyageHead: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  progressRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
});
