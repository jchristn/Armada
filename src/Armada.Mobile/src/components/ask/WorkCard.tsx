import { useRouter, type Href } from 'expo-router';
import type { ReactNode } from 'react';
import { Linking, StyleSheet, View } from 'react-native';
import type { AskMissionSnapshot, AskTargetSnapshot, AskTrackedWork, AskWorkSnapshot } from '@dashboard/types/models';
import { entityTypeLabel, isFailedChildStatus, isWorkActive, statusCounts, workProgress, workRoute } from '@dashboard/lib/askWork';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing } from '../../theme/typography';
import { AppText } from '../ui/AppText';
import { StatusBadge } from '../ui/StatusBadge';
import { statusTone } from './statusTone';

interface WorkCardProps {
  work: AskTrackedWork | null;
  snapshot: AskWorkSnapshot | null;
  /** Emphasize the card briefly after the work strip jumps to it. */
  highlighted?: boolean;
}

function Link({ label, path, mono }: { label: string; path: string; mono?: boolean }) {
  const router = useRouter();
  return (
    <AppText variant={mono ? 'mono' : 'caption'} color="primary" accessibilityRole="link" numberOfLines={1} onPress={() => router.push(path as Href)}>
      {label}
    </AppText>
  );
}

function Fact({ label, children }: { label: string; children: ReactNode }) {
  return (
    <View style={styles.fact}>
      <AppText variant="caption" muted>{label}</AppText>
      {children}
    </View>
  );
}

function MissionRow({ mission }: { mission: AskMissionSnapshot }) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const failed = isFailedChildStatus(mission.status);
  return (
    <View style={[styles.row, { borderTopColor: colors.border }, failed ? { backgroundColor: colors.warningSurface } : null]}>
      <View style={styles.rowMain}>
        <StatusBadge label={t(mission.status)} tone={statusTone(mission.status)} />
        <View style={styles.flex}><Link label={mission.title || mission.id} path={`/missions/${encodeURIComponent(mission.id)}`} /></View>
      </View>
      <View style={styles.facts}>
        {mission.captainName || mission.captainId ? (
          <Fact label={t('Captain')}>
            {mission.captainId
              ? <Link label={mission.captainName || mission.captainId} path={`/captains/${encodeURIComponent(mission.captainId)}`} />
              : <AppText variant="caption">{mission.captainName}</AppText>}
          </Fact>
        ) : null}
        {mission.pipelineStage || mission.persona ? <Fact label={t('Stage')}><AppText variant="caption">{mission.pipelineStage || mission.persona}</AppText></Fact> : null}
        {mission.checkRunStatus ? (
          <Fact label={t('Checks')}>
            {mission.checkRunId
              ? <Link label={t(mission.checkRunStatus)} path={`/checks/${encodeURIComponent(mission.checkRunId)}`} />
              : <AppText variant="caption">{t(mission.checkRunStatus)}</AppText>}
          </Fact>
        ) : null}
        {mission.mergeQueueStatus ? (
          <Fact label={t('Merge')}>
            {mission.mergeEntryId
              ? <Link label={t(mission.mergeQueueStatus)} path={`/merge-queue/${encodeURIComponent(mission.mergeEntryId)}`} />
              : <AppText variant="caption">{t(mission.mergeQueueStatus)}</AppText>}
          </Fact>
        ) : null}
        {mission.landingOutcome ? <Fact label={t('Landing')}><AppText variant="caption">{t(mission.landingOutcome)}</AppText></Fact> : null}
        {mission.branchName ? <Fact label={t('Branch')}><AppText variant="mono" numberOfLines={1}>{mission.branchName}</AppText></Fact> : null}
        {mission.prUrl ? (
          <Fact label={t('PR')}>
            <AppText variant="caption" color="primary" accessibilityRole="link" onPress={() => void Linking.openURL(mission.prUrl as string).catch(() => undefined)}>
              {t('Open pull request')}
            </AppText>
          </Fact>
        ) : null}
      </View>
      {mission.failureReason ? <AppText variant="caption" color="danger">{mission.failureReason}</AppText> : null}
    </View>
  );
}

function TargetRow({ target }: { target: AskTargetSnapshot }) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const failed = isFailedChildStatus(target.status);
  return (
    <View style={[styles.row, { borderTopColor: colors.border }, failed ? { backgroundColor: colors.warningSurface } : null]}>
      <View style={styles.rowMain}>
        <StatusBadge label={t(target.status)} tone={statusTone(target.status)} />
        <View style={styles.flex}>
          {target.vesselId
            ? <Link label={target.vesselName || target.vesselId} path={`/vessels/${encodeURIComponent(target.vesselId)}`} />
            : <AppText variant="caption">{target.vesselName || target.id}</AppText>}
        </View>
      </View>
      {target.missionId || target.voyageId ? (
        <View style={styles.facts}>
          {target.voyageId ? <Fact label={t('Voyage')}><Link label={target.voyageId} path={`/voyages/${encodeURIComponent(target.voyageId)}`} mono /></Fact> : null}
          {target.missionId ? <Fact label={t('Mission')}><Link label={target.missionId} path={`/missions/${encodeURIComponent(target.missionId)}`} mono /></Fact> : null}
        </View>
      ) : null}
      {target.reason ? <AppText variant="caption" color="danger">{target.reason}</AppText> : null}
    </View>
  );
}

/**
 * The live card for one tracked item (voyage, mission, fleet action run, job, import batch), the dashboard's
 * AskWorkCard: status, a progress bar, counts by status, and per-mission / per-target rows linking to the detail
 * screens. Updated in place from `ask.work` events.
 */
export function WorkCard({ work, snapshot, highlighted }: WorkCardProps) {
  const { t, formatRelativeTime } = useLocale();
  const { colors } = useTheme();
  const router = useRouter();
  const entityType = snapshot?.entityType ?? work?.entityType ?? '';
  const entityId = snapshot?.entityId ?? work?.entityId ?? '';
  const title = work?.title || snapshot?.title || entityId;
  const status = snapshot?.status ?? work?.status ?? '';
  const active = isWorkActive(snapshot ?? work);
  const progress = workProgress(snapshot);
  const counts = statusCounts(snapshot);
  const missions = snapshot?.missions ?? [];
  const targets = snapshot?.targets ?? [];
  const route = entityId ? workRoute(entityType, entityId) : null;
  const updated = snapshot?.capturedUtc ?? work?.lastChangeUtc ?? null;
  const typeLabel = entityTypeLabel(t, entityType);
  const donePart = progress && progress.total > 0 ? (progress.done - progress.failed) / progress.total : 0;
  const failedPart = progress && progress.total > 0 ? progress.failed / progress.total : 0;

  return (
    <View
      testID={work ? `work-card-${work.id}` : undefined}
      accessibilityLabel={t('{{type}}: {{title}}', { type: typeLabel, title })}
      style={[styles.card, { borderColor: highlighted ? colors.focus : active ? colors.info : colors.border, backgroundColor: colors.surface }, highlighted ? styles.highlighted : null]}
    >
      <View style={styles.header}>
        <View style={styles.flex}>
          <AppText variant="caption" muted>{typeLabel}</AppText>
          {route
            ? <AppText variant="label" color="primary" accessibilityRole="link" onPress={() => router.push(route as Href)}>{title}</AppText>
            : <AppText variant="label">{title}</AppText>}
        </View>
        {status ? <StatusBadge label={t(status)} tone={statusTone(status)} /> : <AppText variant="caption" muted>{t('Waiting for status...')}</AppText>}
      </View>

      {progress ? (
        <View style={styles.progress}>
          <View
            accessible
            accessibilityRole="progressbar"
            accessibilityLabel={t('Progress')}
            accessibilityValue={{ min: 0, max: 100, now: progress.percent }}
            style={[styles.bar, { backgroundColor: colors.border }]}
          >
            <View style={{ flex: donePart, backgroundColor: colors.success }} />
            <View style={{ flex: failedPart, backgroundColor: colors.danger }} />
            <View style={{ flex: Math.max(0, 1 - donePart - failedPart) }} />
          </View>
          <AppText variant="caption" muted>
            {t('{{done}} of {{total}} finished', { done: progress.done, total: progress.total })}
            {progress.failed > 0 ? ` · ${t('{{count}} failed', { count: progress.failed })}` : ''}
          </AppText>
        </View>
      ) : null}

      {counts.length > 1 ? (
        <View style={styles.counts} accessibilityLabel={t('Counts by status')}>
          {counts.map((c) => <AppText key={c.status} variant="caption" muted>{t(c.status)} <AppText variant="caption" style={styles.bold}>{c.count}</AppText></AppText>)}
        </View>
      ) : null}

      {missions.length > 0 ? <View accessibilityLabel={t('Missions')}>{missions.map((m) => <MissionRow key={m.id} mission={m} />)}</View> : null}
      {missions.length === 0 && targets.length > 0 ? <View accessibilityLabel={t('Targets')}>{targets.map((x) => <TargetRow key={x.id} target={x} />)}</View> : null}

      {snapshot?.errorText ? <AppText variant="caption" color="danger">{snapshot.errorText}</AppText> : null}
      {!snapshot ? <AppText variant="caption" muted>{t('Loading live status...')}</AppText> : null}

      <View style={styles.footer}>
        {updated ? <AppText variant="caption" muted>{t('Updated {{time}}', { time: formatRelativeTime(updated) })}</AppText> : <View />}
        {route ? <AppText variant="caption" color="primary" accessibilityRole="link" onPress={() => router.push(route as Href)}>{t('Open details')}</AppText> : null}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  card: { borderWidth: 1, borderRadius: radius.md, padding: spacing.md, gap: spacing.sm, marginTop: spacing.sm },
  highlighted: { borderWidth: 2 },
  header: { flexDirection: 'row', alignItems: 'flex-start', gap: spacing.sm },
  flex: { flex: 1 },
  progress: { gap: spacing.xs },
  bar: { height: 8, borderRadius: 4, overflow: 'hidden', flexDirection: 'row' },
  counts: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.md },
  bold: { fontWeight: '700' },
  row: { borderTopWidth: StyleSheet.hairlineWidth, paddingVertical: spacing.sm, gap: spacing.xs },
  rowMain: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  facts: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.md },
  fact: { gap: 1, maxWidth: '100%' },
  footer: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
});
