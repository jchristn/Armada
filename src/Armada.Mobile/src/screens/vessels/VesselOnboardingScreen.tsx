import { Stack, useRouter, type Href } from 'expo-router';
import { RefreshControl, ScrollView, StyleSheet, View } from 'react-native';
import { getVessel, getVesselReadiness } from '@dashboard/api/client';
import type { Vessel, VesselReadinessResult } from '@dashboard/types/models';
import { groupSetupChecklist, nextChecklistItem } from '@dashboard/lib/readiness';
import { ActionRow } from '../../build/fields';
import { useLiveResource } from '../../build/useLiveResource';
import { AppText, Button, ErrorState, LoadingState, Section } from '../../components/ui';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing } from '../../theme/typography';
import { ChecklistItem, IssueBox, ReadinessCard } from './ReadinessCards';
import { vesselLinks } from './vesselLinks';

interface OnboardingData {
  vessel: Vessel;
  readiness: VesselReadinessResult;
}

function Stat({ label, value }: { label: string; value: string | number }) {
  const { colors } = useTheme();
  return (
    <View style={[styles.stat, { backgroundColor: colors.surface, borderColor: colors.border }]} accessible accessibilityLabel={`${label}: ${value}`}>
      <AppText variant="caption" muted>{label}</AppText>
      <AppText variant="heading">{String(value)}</AppText>
    </View>
  );
}

/**
 * Vessel onboarding (the dashboard's VesselOnboarding page): progress counts, the next recommended step, current
 * readiness, the setup checklist grouped into Repository Basics, Workflow Profile, and Delivery Readiness (each item
 * with its action), and the open issues.
 */
export function VesselOnboardingScreen({ id }: { id: string }) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const router = useRouter();
  const resource = useLiveResource<OnboardingData>(async () => {
    const [vessel, readiness] = await Promise.all([getVessel(id), getVesselReadiness(id)]);
    return { vessel, readiness };
  }, [id]);

  const title = <Stack.Screen options={{ title: t('Vessel Onboarding') }} />;
  if (resource.loading) return <>{title}<LoadingState label={t('Loading...')} /></>;
  if (!resource.data) {
    return (
      <>
        {title}
        <ErrorState title={t('Vessel not found.')} message={resource.error || t('Failed to load vessel onboarding.')} retryLabel={t('Retry')} onRetry={() => void resource.refresh()} />
      </>
    );
  }
  const { vessel, readiness } = resource.data;
  const groups = groupSetupChecklist(readiness);
  const next = nextChecklistItem(readiness);

  return (
    <View style={[styles.fill, { backgroundColor: colors.background }]} testID="vessel-onboarding">
      {title}
      <ScrollView
        contentContainerStyle={styles.content}
        refreshControl={<RefreshControl refreshing={resource.refreshing} onRefresh={() => void resource.refresh()} tintColor={colors.primary} />}
      >
        <View style={styles.column}>
          <AppText variant="heading" accessibilityRole="header" style={styles.pad}>{vessel.name}</AppText>
          <AppText muted style={styles.pad}>{t('Use this checklist to take the vessel from registration through workflow-ready onboarding.')}</AppText>
          <ActionRow>
            <Button label={t('Open Workspace')} variant="secondary" onPress={() => router.push(vesselLinks.workspace(vessel.id) as Href)} />
            <Button label={t('Run Check')} variant="secondary" onPress={() => router.push(vesselLinks.runCheck(vessel) as Href)} />
            <Button label={t('Back To Vessel')} variant="secondary" onPress={() => router.push(vesselLinks.detail(vessel.id) as Href)} testID="vessel-onboarding-back" />
          </ActionRow>
          <View style={styles.stats}>
            <Stat label={t('Completed')} value={`${readiness.setupChecklistSatisfiedCount || 0}/${readiness.setupChecklistTotalCount || 0}`} />
            <Stat label={t('Blocking Issues')} value={readiness.errorCount || 0} />
            <Stat label={t('Warnings')} value={readiness.warningCount || 0} />
            <Stat label={t('Environments')} value={readiness.deploymentEnvironments.length || 0} />
          </View>
          {next ? (
            <Section title={t('Next Recommended Step')}>
              <View style={styles.body} testID="vessel-onboarding-next">
                <AppText variant="label">{next.title}</AppText>
                <AppText muted>{next.message}</AppText>
                {next.actionLabel && next.actionRoute ? (
                  <Button label={next.actionLabel} onPress={() => router.push(next.actionRoute as Href)} style={styles.inline} />
                ) : null}
              </View>
            </Section>
          ) : null}
          <ReadinessCard title={t('Current Readiness')} readiness={readiness} emptyMessage={t('Readiness data is not available for this vessel yet.')} />
          {groups.map((group) => (
            <Section key={group.key} title={t(group.title)}>
              <View style={styles.body}>
                {group.items.map((item) => <ChecklistItem key={item.code} item={item} />)}
              </View>
            </Section>
          ))}
          {readiness.issues.length > 0 ? (
            <Section title={t('Open Issues')}>
              <View style={styles.body}>
                {readiness.issues.map((issue, index) => (
                  <IssueBox key={`${issue.code}-${index}`} title={issue.title} severity={issue.severity} message={issue.message} related={issue.relatedValue} />
                ))}
              </View>
            </Section>
          ) : null}
        </View>
      </ScrollView>
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  content: { paddingVertical: spacing.lg, flexGrow: 1 },
  column: { width: '100%', maxWidth: 820, alignSelf: 'center' },
  pad: { paddingHorizontal: spacing.lg, marginBottom: spacing.sm },
  stats: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm, marginHorizontal: spacing.md, marginBottom: spacing.lg },
  stat: { flexGrow: 1, minWidth: 140, borderWidth: StyleSheet.hairlineWidth, borderRadius: radius.md, padding: spacing.md },
  body: { padding: spacing.md, gap: spacing.sm },
  inline: { alignSelf: 'flex-start', marginBottom: 0 },
});
