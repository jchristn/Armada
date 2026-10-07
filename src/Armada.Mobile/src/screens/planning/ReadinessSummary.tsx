import { StyleSheet, View } from 'react-native';
import type { VesselReadinessResult } from '@dashboard/types/models';
import { readinessLabel, readinessTone } from '@dashboard/lib/readiness';
import { AppText, StatusBadge } from '../../components/ui';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing } from '../../theme/typography';

/** The compact Vessel Readiness panel shown under the Planning start form (the dashboard's ReadinessPanel compact). */
export function ReadinessSummary({ readiness, loading }: { readiness: VesselReadinessResult | null; loading: boolean }) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const tone = readinessTone(readiness);
  return (
    <View style={[styles.box, { borderColor: colors.border, backgroundColor: colors.surface }]} testID="planning-readiness">
      <View style={styles.head}>
        <AppText variant="label" style={styles.flex}>{t('Vessel Readiness')}</AppText>
        {readiness ? <StatusBadge label={t(readinessLabel(readiness))} tone={tone === 'ready' ? 'success' : tone === 'warning' ? 'warning' : 'failed'} /> : null}
      </View>
      {loading ? <AppText variant="caption" muted>{t('Checking readiness...')}</AppText> : null}
      {!loading && !readiness ? <AppText variant="caption" muted>{t('Select a vessel to inspect readiness.')}</AppText> : null}
      {!loading && readiness ? (
        <>
          <AppText variant="caption" muted>
            {[
              readiness.hasWorkingDirectory ? t('Working directory available') : t('Working directory unavailable'),
              readiness.hasRepositoryContext ? t('Repository context available') : t('Repository context unavailable'),
            ].join(' \u00b7 ')}
          </AppText>
          {readiness.issues.map((issue) => (
            <View key={`${issue.code}-${issue.relatedValue ?? ''}`} style={styles.issue}>
              <AppText variant="caption" color={issue.severity === 'Error' ? 'danger' : issue.severity === 'Warning' ? 'warning' : 'info'}>{issue.title}</AppText>
              <AppText variant="caption" muted>{issue.message}</AppText>
            </View>
          ))}
        </>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  box: { borderWidth: StyleSheet.hairlineWidth, borderRadius: radius.md, padding: spacing.md, gap: spacing.xs, marginBottom: spacing.lg },
  head: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  issue: { marginTop: spacing.xs },
});
