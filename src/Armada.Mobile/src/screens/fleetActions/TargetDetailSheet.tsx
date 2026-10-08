import { useRouter, type Href } from 'expo-router';
import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { getFleetActionRunTarget } from '@dashboard/api/client';
import { formatBytes } from '@dashboard/lib/format';
import { formatDurationMs, reasonLabel } from '@dashboard/lib/fleetActionLabels';
import { CodeBlock } from '../../components/ask/CodeBlock';
import { InfoRow } from '../../build/fields';
import { useLiveResource } from '../../build/useLiveResource';
import { AppText, Banner, BottomSheet, Button, ErrorState, LoadingState } from '../../components/ui';
import { useLocale } from '../../i18n/LocaleContext';
import { spacing } from '../../theme/typography';
import { TargetStatusBadge } from './common';

/** Lines of each stream shown before "Open full output". */
export const PREVIEW_LINES = 30;

/** The last `lines` lines of `text`. */
export function tailLines(text: string, lines: number): string {
  const all = text.split(/\r?\n/);
  return all.length <= lines ? text : all.slice(all.length - lines).join('\n');
}

/**
 * One Command target's detail (the dashboard's TargetDetailDrawer): status and reason, exit code, duration, the
 * rendered command, and the tail of stdout and stderr with "Open full output". This is the only place captured
 * output is fetched, because output may contain secrets.
 */
export function TargetDetailSheet({ runId, targetId, onClose }: { runId: string; targetId: string | null; onClose: () => void }) {
  const { t } = useLocale();
  return (
    <BottomSheet open={targetId !== null} title={t('Target')} onClose={onClose} closeLabel={t('Close')} testID="fleet-action-target">
      {targetId ? <TargetBody runId={runId} targetId={targetId} onClose={onClose} /> : null}
    </BottomSheet>
  );
}

function TargetBody({ runId, targetId, onClose }: { runId: string; targetId: string; onClose: () => void }) {
  const { t, locale, formatDateTime } = useLocale();
  const router = useRouter();
  const target = useLiveResource(() => getFleetActionRunTarget(runId, targetId), [runId, targetId]);
  const [full, setFull] = useState<{ stdout: boolean; stderr: boolean }>({ stdout: false, stderr: false });
  const go = (href: string) => { onClose(); router.push(href as Href); };

  if (target.loading) return <LoadingState label={t('Loading...')} />;
  const d = target.data;
  if (!d) return <ErrorState title={t('Failed to load the target.')} message={target.error} retryLabel={t('Retry')} onRetry={() => void target.refresh()} />;
  const reason = reasonLabel(t, d.skipReason, d.failureReason);
  const out = d.outputText ?? '';
  const err = d.errorText ?? '';

  return (
    <View testID="fleet-action-target-body">
      <AppText variant="heading">{d.vesselName}</AppText>
      <AppText variant="mono" muted selectable style={styles.gap}>{d.id}</AppText>
      {target.error ? <Banner tone="danger" title={target.error} /> : null}
      <View style={styles.gap}><TargetStatusBadge status={d.status} /></View>
      {reason ? <InfoRow label={t('Reason')} value={`${reason} (${d.skipReason ?? d.failureReason})`} /> : null}
      <InfoRow label={t('Vessel')}>
        <AppText color="primary" accessibilityRole="link" onPress={() => go(`/vessels/${d.vesselId}`)}>{d.vesselName}</AppText>
      </InfoRow>
      <InfoRow label={t('Exit code')} value={d.exitCode ?? '-'} mono />
      <InfoRow label={t('Duration')} value={formatDurationMs(t, locale, d.durationMs)} />
      <InfoRow label={t('Started')} value={d.startedUtc ? formatDateTime(d.startedUtc) : '-'} />
      <InfoRow label={t('Completed')} value={d.completedUtc ? formatDateTime(d.completedUtc) : '-'} />
      {d.voyageId ? (
        <InfoRow label={t('Voyage')}>
          <AppText color="primary" variant="mono" accessibilityRole="link" onPress={() => go(`/voyages/${d.voyageId}`)}>{d.voyageId}</AppText>
        </InfoRow>
      ) : null}
      {d.outputTruncated ? <View style={styles.top}><Banner tone="warning" title={t('Output was truncated. Only the end of each stream was kept (FleetActions.MaxOutputBytes).')} /></View> : null}

      <AppText variant="label" style={styles.section}>{t('Rendered command')}</AppText>
      <CodeBlock text={d.renderedText || t('(not rendered)')} testID="fleet-action-target-command" />

      <AppText variant="label" style={styles.section}>{`${t('Standard output')} (${formatBytes(out.length)})`}</AppText>
      {out ? (
        <>
          <CodeBlock text={full.stdout ? out : tailLines(out, PREVIEW_LINES)} maxHeight={full.stdout ? 520 : 320} testID="fleet-action-target-stdout" />
          {!full.stdout && out !== tailLines(out, PREVIEW_LINES) ? <Button label={t('Open full output')} variant="ghost" onPress={() => setFull((f) => ({ ...f, stdout: true }))} /> : null}
        </>
      ) : <AppText muted>{t('No output captured.')}</AppText>}

      <AppText variant="label" style={styles.section}>{`${t('Standard error')} (${formatBytes(err.length)})`}</AppText>
      {err ? (
        <>
          <CodeBlock text={full.stderr ? err : tailLines(err, PREVIEW_LINES)} maxHeight={full.stderr ? 520 : 320} testID="fleet-action-target-stderr" />
          {!full.stderr && err !== tailLines(err, PREVIEW_LINES) ? <Button label={t('Open full error output')} variant="ghost" onPress={() => setFull((f) => ({ ...f, stderr: true }))} /> : null}
        </>
      ) : <AppText muted>{t('No error output captured.')}</AppText>}

      <View style={styles.top}>
        <Button label={t('Refresh')} variant="secondary" onPress={() => void target.refresh()} busy={target.refreshing} />
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  gap: { marginBottom: spacing.sm },
  top: { marginTop: spacing.md },
  section: { marginTop: spacing.lg, marginBottom: spacing.xs },
});
