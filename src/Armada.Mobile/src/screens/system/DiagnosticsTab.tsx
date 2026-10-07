import { useCallback, useEffect, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { getDoctor } from '@dashboard/api/client';
import type { DoctorCheck } from '@dashboard/types/models';
import { DetailBody } from '../../components/resource/DetailParts';
import { StatRow } from '../../components/resource/ResourceList';
import { ResourceRow } from '../../components/resource/ResourceRow';
import { AppText } from '../../components/ui/AppText';
import { Button } from '../../components/ui/Button';
import { LoadingState } from '../../components/ui/States';
import { StatusBadge } from '../../components/ui/StatusBadge';
import { useLocale } from '../../i18n/LocaleContext';
import { errorText } from '../../resource/useLoad';
import { statusBadge } from '../../resource/status';
import { spacing } from '../../theme/typography';

/** Overall verdict of a set of diagnostic checks: Unhealthy with any failure, Healthy when all pass, else Warnings. */
export function diagnosticsSummary(results: DoctorCheck[]): { pass: number; warn: number; fail: number; verdict: 'Healthy' | 'Warnings' | 'Unhealthy' | null } {
  const pass = results.filter((c) => c.status === 'Pass').length;
  const warn = results.filter((c) => c.status === 'Warn').length;
  const fail = results.filter((c) => c.status === 'Fail').length;
  let verdict: 'Healthy' | 'Warnings' | 'Unhealthy' | null = null;
  if (results.length > 0) verdict = fail > 0 ? 'Unhealthy' : warn === 0 ? 'Healthy' : 'Warnings';
  return { pass, warn, fail, verdict };
}

/** Settings > Diagnostics (the dashboard's Doctor page): runs every health check on open and on Run Checks. */
export function DiagnosticsTab() {
  const { t } = useLocale();
  const [results, setResults] = useState<DoctorCheck[]>([]);
  const [running, setRunning] = useState(false);

  const runChecks = useCallback(async () => {
    setRunning(true);
    setResults([]);
    try {
      setResults(await getDoctor());
    } catch (e: unknown) {
      setResults([{ name: t('Error'), status: 'Fail', message: t('Failed to run health checks: {{message}}', { message: errorText(e, t('Unknown error')) }) }]);
    } finally {
      setRunning(false);
    }
  }, [t]);

  // Run once on open, as the dashboard does.
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void runChecks(); }, [runChecks]);

  const summary = diagnosticsSummary(results);
  return (
    <DetailBody embedded refreshing={false} onRefresh={() => void runChecks()} testID="diagnostics">
      <View style={styles.head}>
        <AppText muted style={styles.flex}>{t('System health diagnostics and checks.')}</AppText>
        {summary.verdict ? <StatusBadge label={t(summary.verdict)} tone={summary.verdict === 'Unhealthy' ? 'failed' : summary.verdict === 'Healthy' ? 'success' : 'warning'} /> : null}
      </View>
      <Button label={running ? t('Running...') : t('Run Checks')} onPress={() => void runChecks()} disabled={running} accessibilityHint={t('Run all health checks')} style={styles.run} testID="diagnostics-run" />
      {running ? <LoadingState label={t('Running health checks...')} /> : null}
      {!running && results.length > 0 ? (
        <>
          <StatRow stats={[
            { label: t('Passed'), value: summary.pass, tone: 'success' },
            { label: t('Warnings'), value: summary.warn, tone: 'warning' },
            { label: t('Failed'), value: summary.fail, tone: 'danger' },
          ]} />
          <View>
            {results.map((check, index) => (
              <ResourceRow key={`${check.name}-${index}`} title={check.name} subtitle={check.message} badge={statusBadge(t, check.status)} testID={`diagnostics-check-${index}`} />
            ))}
          </View>
        </>
      ) : null}
      {!running && results.length === 0 ? <AppText muted style={styles.empty}>{t('No results yet. Click "Run Checks" to start diagnostics.')}</AppText> : null}
    </DetailBody>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  head: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, marginHorizontal: spacing.lg, marginBottom: spacing.md },
  run: { marginHorizontal: spacing.md },
  empty: { textAlign: 'center', padding: spacing.xl },
});
