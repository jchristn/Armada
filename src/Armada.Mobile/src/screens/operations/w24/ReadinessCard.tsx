import { StyleSheet, View } from 'react-native';
import { formatInputProvider, readinessLabel, readinessTone } from '@dashboard/lib/readiness';
import type { VesselReadinessResult } from '@dashboard/types/models';
import { AppText, Section, StatusBadge } from '../../../components/ui';
import { useLocale } from '../../../i18n/LocaleContext';
import { spacing } from '../../../theme/typography';

/** The dashboard's compact ReadinessPanel (as on Dispatch): pill, resolved profile, summary, and issues. */
export function ReadinessCard({ title, readiness, loading, emptyMessage }: {
  title: string;
  readiness: VesselReadinessResult | null;
  loading: boolean;
  emptyMessage: string;
}) {
  const { t } = useLocale();
  const tone = readinessTone(readiness);
  return (
    <Section title={title}>
      <View style={styles.pad} testID="readiness-card">
        <View style={styles.row}>
          <AppText variant="caption" muted style={styles.flex}>
            {readiness?.workflowProfileName
              ? `${t('Resolved profile')}: ${readiness.workflowProfileName}${readiness.workflowProfileScope ? ` (${readiness.workflowProfileScope})` : ''}`
              : ''}
          </AppText>
          <StatusBadge label={t(readinessLabel(readiness))} tone={tone === 'ready' ? 'success' : tone === 'error' ? 'failed' : 'warning'} />
        </View>
        {loading ? <AppText muted>{t('Checking readiness...')}</AppText> : !readiness ? <AppText muted>{emptyMessage}</AppText> : (
          <>
            <AppText variant="caption" muted>{readiness.hasWorkingDirectory ? t('Working directory available') : t('Working directory unavailable')}</AppText>
            <AppText variant="caption" muted>{readiness.hasRepositoryContext ? t('Repository context available') : t('Repository context unavailable')}</AppText>
            {readiness.availableCheckTypes.length > 0 ? (
              <AppText variant="caption" muted>{t('{{count}} check type(s) available', { count: readiness.availableCheckTypes.length })}</AppText>
            ) : null}
            {readiness.issues.length > 0 ? readiness.issues.map((issue, index) => (
              <View key={`${issue.code}-${index}`} style={styles.issue} testID={`readiness-issue-${index}`}>
                <View style={styles.row}>
                  <AppText variant="label" style={styles.flex}>{issue.title}</AppText>
                  <StatusBadge label={issue.severity} tone={issue.severity.toLowerCase() === 'error' ? 'failed' : 'warning'} />
                </View>
                <AppText variant="caption" muted>{issue.message}</AppText>
                {issue.relatedValue ? (
                  <AppText variant="mono" selectable>
                    {issue.relatedValue}{issue.inputProvider ? ` (${t(formatInputProvider(issue.inputProvider))})` : ''}
                  </AppText>
                ) : null}
              </View>
            )) : <AppText color="success">{t('This vessel looks ready for the currently selected workflow surface.')}</AppText>}
          </>
        )}
      </View>
    </Section>
  );
}

const styles = StyleSheet.create({
  pad: { paddingHorizontal: spacing.lg, paddingVertical: spacing.sm, gap: spacing.xs },
  row: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  flex: { flex: 1 },
  issue: { gap: 2, marginTop: spacing.sm },
});
