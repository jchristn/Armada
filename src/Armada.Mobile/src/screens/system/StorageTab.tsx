import { useCallback, useEffect, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { getStorage, listAllStorageHosts, refreshStorage } from '@dashboard/api/client';
import { formatStorageBytes } from '@dashboard/lib/storageFormatting';
import type { StorageHostReport, StorageSnapshot } from '@dashboard/types/storage';
import { useAuth } from '../../auth/AuthContext';
import { ActionBar, DetailBody } from '../../components/resource/DetailParts';
import { StatRow } from '../../components/resource/ResourceList';
import { resourceStyles } from '../../components/resource/styles';
import { AppText } from '../../components/ui/AppText';
import { Banner } from '../../components/ui/Banner';
import { Button } from '../../components/ui/Button';
import { Pager } from '../../components/ui/Pager';
import { EmptyState } from '../../components/ui/States';
import { StatusBadge, type StatusTone } from '../../components/ui/StatusBadge';
import { TabStrip } from '../../components/ui/TabStrip';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing } from '../../theme/typography';

const PAGE_SIZE = 10;
const GROUPS = ['locations', 'database', 'hosts', 'volumes', 'files', 'history', 'retention'];

interface StorageRow { title: string; lines: string[]; badge?: { label: string; tone: StatusTone } | null }

function statusTone(status: string, stale?: boolean): StatusTone {
  if (stale) return 'warning';
  if (status === 'Complete') return 'success';
  if (status === 'Unavailable') return 'failed';
  if (status === 'Pending') return 'pending';
  if (status === 'Skipped') return 'skipped';
  return 'warning';
}

/** Cached read-only administrator inventory, shared with the dashboard API and message catalog. */
export function StorageTab() {
  const { isAdmin } = useAuth();
  const { t, locale, formatDateTime } = useLocale();
  const { colors } = useTheme();
  const [data, setData] = useState<StorageSnapshot | null>(null);
  // Host reports are not in the snapshot; they are read in explicit bounded pages (100 hosts per page).
  const [hosts, setHosts] = useState<StorageHostReport[]>([]);
  const [error, setError] = useState(false);
  const [busy, setBusy] = useState(false);
  const [group, setGroup] = useState('locations');
  const [page, setPage] = useState(0);
  const read = useCallback(async () => {
    if (!isAdmin) return;
    try {
      const [snapshot, reports] = await Promise.all([getStorage(), listAllStorageHosts()]);
      setData(snapshot); setHosts(reports); setError(false);
    } catch { setError(true); }
  }, [isAdmin]);
  useEffect(() => {
    let active = true;
    if (isAdmin) Promise.all([getStorage(), listAllStorageHosts()]).then(([result, reports]) => { if (active) { setData(result); setHosts(reports); } }).catch(() => { if (active) setError(true); });
    return () => { active = false; };
  }, [isAdmin]);
  useEffect(() => {
    if (!data?.collecting || !isAdmin) return;
    const timer = setInterval(() => { void read(); }, 5000);
    return () => clearInterval(timer);
  }, [data?.collecting, isAdmin, read]);
  const collect = async (exact: boolean) => {
    setBusy(true);
    try { setData(await refreshStorage(exact)); setHosts(await listAllStorageHosts()); setError(false); } catch { setError(true); }
    finally { setBusy(false); }
  };
  if (!isAdmin) {
    return (
      <View style={[styles.fill, { backgroundColor: colors.background }]} accessibilityRole="alert">
        <EmptyState icon="lock-closed-outline" title={t('storage.denied')} />
      </View>
    );
  }
  const number = (n: number) => new Intl.NumberFormat(locale).format(n);
  const bytes = (n: number | null | undefined) => formatStorageBytes(n, locale, t('storage.unavailable'));
  const state = (s: string) => t(`storage.${s}`);
  const badge = (status: string, stale?: boolean) => ({ label: stale ? state('Stale') : state(status), tone: statusTone(status, stale) });
  const rows: StorageRow[] = [];
  const locations = hosts.flatMap((host) => host.locations);
  for (const host of hosts) {
    if (group === 'hosts') rows.push({ title: host.name, lines: [formatDateTime(host.measuredUtc), ...host.warnings.map(state)], badge: badge(host.status, host.stale) });
    if (group === 'volumes') for (const volume of host.volumes) rows.push({ title: `${host.name}: ${volume.path}`, lines: [`${t('storage.capacity')}: ${bytes(volume.totalBytes)}`, `${t('storage.available')}: ${bytes(volume.availableBytes)}`], badge: badge(volume.status) });
    for (const location of host.locations) {
      if (group === 'locations') rows.push({ title: `${host.name}: ${location.path}`, lines: [`${t(`storage.category.${location.category}`)} · ${bytes(location.bytes)}`, ...location.warnings.map(state)], badge: badge(location.status, host.stale) });
      if (group === 'files') for (const file of location.largestFiles) rows.push({ title: file.path, lines: [`${host.name} · ${bytes(file.bytes)}`, formatDateTime(file.lastWriteUtc)] });
    }
  }
  if (group === 'database') for (const table of data?.database.tables ?? []) rows.push({ title: `${table.schema}.${table.name}`, lines: [
    `${t('storage.records')}: ${table.rows == null ? state('Unavailable') : number(table.rows)} (${state(table.countKind)})`,
    `${t('storage.logicalBytes')}: ${bytes(table.logicalBytes)}`,
    `${t('storage.dataBytes')}: ${bytes(table.dataBytes)}`,
    `${t('storage.indexBytes')}: ${bytes(table.indexBytes)}`,
    ...(table.warning ? [state(table.warning)] : []),
  ] });
  if (group === 'retention') for (const [key, value] of Object.entries(data?.retention ?? {})) rows.push({ title: t(`storage.retention.${key}`), lines: [key === 'BuildSlots' ? String(value) : value ? t('storage.days', { count: value }) : t('storage.never')] });
  if (group === 'history') for (const sample of data?.history ?? []) rows.push({ title: formatDateTime(sample.measuredUtc), lines: [`${t('storage.filesKpi')}: ${bytes(sample.observedFileBytes)}`, `${t('storage.databaseKpi')}: ${bytes(sample.databaseBytes)}`] });
  const pages = Math.max(1, Math.ceil(rows.length / PAGE_SIZE));
  const currentPage = Math.min(page, pages - 1);
  const filesTotal = data?.observedFileBytes ?? locations.reduce((n, l) => n + (l.bytes ?? 0), 0);
  const logs = locations.filter((l) => l.category.startsWith('Logs/')).reduce((n, l) => n + (l.bytes ?? 0), 0);
  const fresh = data?.freshHostCount ?? hosts.filter((h) => !h.stale && h.status === 'Complete').length;
  const hostCount = data?.hostCount ?? hosts.length;
  const collecting = busy || data?.collecting === true;
  const help = group === 'history' ? t('storage.historyHelp', { count: data?.historyCapacity ?? 288 }) : group === 'volumes' ? t('storage.volumeHelp') : group === 'files' ? t('storage.filesHelp') : null;
  return <DetailBody embedded onRefresh={() => { void read(); }} testID="storage">
    <View style={resourceStyles.pad}>
      <AppText variant="heading" accessibilityRole="header">{t('storage.title')}</AppText>
      <AppText variant="caption" muted>{t('storage.subtitle')}</AppText>
    </View>
    {error ? <Banner tone="danger" title={t('storage.error')} /> : null}
    {data?.lastError ? <Banner tone="warning" title={state(data.lastError)} /> : null}
    <ActionBar>
      <Button label={t(data?.collecting ? 'storage.collecting' : 'storage.collect')} icon="scan-outline" busy={busy} disabled={collecting} onPress={() => { void collect(false); }} accessibilityHint={t('storage.collectHelp')} style={resourceStyles.action} />
      <Button label={t('storage.exact')} variant="secondary" disabled={collecting} onPress={() => { void collect(true); }} accessibilityHint={t('storage.exactHelp')} style={resourceStyles.action} />
      <Button label={t('storage.reload')} icon="refresh" variant="ghost" onPress={() => { void read(); }} accessibilityHint={t('storage.reloadHelp')} style={resourceStyles.action} />
    </ActionBar>
    {data ? <>
      <StatRow stats={[
        { label: t('storage.filesKpi'), value: bytes(filesTotal) },
        { label: t('storage.databaseKpi'), value: bytes(data.database.allocatedBytes) },
        { label: t('storage.logsKpi'), value: bytes(logs) },
        { label: t('storage.coverageKpi'), value: `${number(fresh)} / ${number(hostCount)}`, tone: fresh < hostCount ? 'warning' : undefined },
      ]} />
      <AppText variant="caption" muted style={styles.note}>
        {data.measuredUtc ? t('storage.measured', { time: formatDateTime(data.measuredUtc), seconds: data.intervalSeconds }) : t('storage.pending')}
      </AppText>
      <AppText variant="caption" muted style={styles.note}>{t('storage.accounting')}</AppText>
    </> : null}
    <TabStrip label={t('storage.sections')} value={group} onChange={(key) => { setGroup(key); setPage(0); }} options={GROUPS.map((key) => ({ value: key, label: t(`storage.${key}`) }))} />
    <View style={styles.list}>
      {help ? <AppText variant="caption" muted style={styles.note}>{help}</AppText> : null}
      {rows.length === 0 ? (
        <AppText muted style={styles.note} accessibilityLiveRegion="polite">{t(data ? 'storage.empty' : 'storage.loading')}</AppText>
      ) : (
        <View style={[styles.card, { backgroundColor: colors.surface, borderColor: colors.border }]}>
          {rows.slice(currentPage * PAGE_SIZE, currentPage * PAGE_SIZE + PAGE_SIZE).map((row, index) => (
            <View key={`${currentPage}-${index}`} style={[styles.row, index > 0 ? { borderTopColor: colors.border, borderTopWidth: StyleSheet.hairlineWidth } : null]}
              accessible accessibilityLabel={[row.title, row.badge?.label, ...row.lines].filter(Boolean).join(', ')}>
              <View style={styles.rowHead}>
                <AppText variant="label" selectable style={styles.flex}>{row.title}</AppText>
                {row.badge ? <StatusBadge label={row.badge.label} tone={row.badge.tone} /> : null}
              </View>
              {row.lines.filter(Boolean).map((line, i) => <AppText key={i} variant="caption" muted selectable>{line}</AppText>)}
            </View>
          ))}
        </View>
      )}
    </View>
    <Pager page={currentPage + 1} pages={pages} onChange={(next) => setPage(next - 1)} testID="storage-pager" />
  </DetailBody>;
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  flex: { flex: 1 },
  note: { marginHorizontal: spacing.lg, marginBottom: spacing.md },
  list: { paddingTop: spacing.md, marginBottom: spacing.lg },
  card: { borderRadius: radius.md, borderWidth: StyleSheet.hairlineWidth, overflow: 'hidden', marginHorizontal: spacing.md },
  row: { paddingHorizontal: spacing.lg, paddingVertical: spacing.md, gap: 2 },
  rowHead: { flexDirection: 'row', alignItems: 'flex-start', gap: spacing.sm },
});
