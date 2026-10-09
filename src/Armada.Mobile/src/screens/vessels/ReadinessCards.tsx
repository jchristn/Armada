import { useRouter, type Href } from 'expo-router';
import type { ReactNode } from 'react';
import { ActivityIndicator, StyleSheet, View } from 'react-native';
import type { LandingPreviewResult, ReadinessSeverity, VesselReadinessResult, VesselSetupChecklistItem } from '@dashboard/types/models';
import {
  formatInputProvider,
  readinessLabel,
  readinessTone,
  readinessBranchSummary,
  readinessDriftSummary,
  readinessCheckout,
  readinessCheckoutText,
} from '@dashboard/lib/readiness';
import { AppText, Button, Section, StatusBadge } from '../../components/ui';
import type { StatusTone } from '../../components/ui/StatusBadge';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing, typography } from '../../theme/typography';
import { vesselLinks } from './vesselLinks';

export function severityTone(severity: ReadinessSeverity | string): StatusTone {
  if (severity === 'Error') return 'failed';
  if (severity === 'Warning') return 'warning';
  return 'info';
}

function Chips({ items }: { items: string[] }) {
  const { colors } = useTheme();
  return (
    <View style={styles.chips}>
      {items.map((item) => (
        <View key={item} style={[styles.chip, { borderColor: colors.border, backgroundColor: colors.background }]}>
          <AppText variant="caption">{item}</AppText>
        </View>
      ))}
    </View>
  );
}

function Line({ label, children }: { label: string; children: ReactNode }) {
  return (
    <View style={styles.line}>
      <AppText variant="caption" muted>{label}</AppText>
      {typeof children === 'string' ? <AppText>{children}</AppText> : children}
    </View>
  );
}

export function IssueBox({ title, severity, message, related, testID }: { title: string; severity: string; message: string; related?: string | null; testID?: string }) {
  const { colors } = useTheme();
  return (
    <View style={[styles.issue, { borderColor: colors.border, backgroundColor: colors.background }]} testID={testID}>
      <View style={styles.issueHead}>
        <AppText variant="label" style={styles.flex}>{title}</AppText>
        <StatusBadge label={severity} tone={severityTone(severity)} />
      </View>
      <AppText variant="caption" muted>{message}</AppText>
      {related ? <AppText selectable style={[typography.mono, styles.small]}>{related}</AppText> : null}
    </View>
  );
}

/** One onboarding checklist item with its action (the server gives a dashboard route; the app opens the same path). */
export function ChecklistItem({ item }: { item: VesselSetupChecklistItem }) {
  const { t } = useLocale();
  const router = useRouter();
  const { colors } = useTheme();
  return (
    <View style={[styles.issue, { borderColor: colors.border, backgroundColor: colors.background, opacity: item.isSatisfied ? 0.8 : 1 }]} testID={`checklist-${item.code}`}>
      <View style={styles.issueHead}>
        <AppText variant="label" style={styles.flex}>{item.title}</AppText>
        <StatusBadge label={item.isSatisfied ? t('Done') : item.severity} tone={item.isSatisfied ? 'success' : severityTone(item.severity)} />
      </View>
      <AppText variant="caption" muted>{item.message}</AppText>
      {!item.isSatisfied && item.actionLabel && item.actionRoute ? (
        <Button label={item.actionLabel} variant="secondary" onPress={() => router.push(item.actionRoute as Href)} style={styles.inlineButton} />
      ) : null}
    </View>
  );
}

/**
 * Vessel readiness (the dashboard's ReadinessPanel): the overall verdict, resolved workflow profile, onboarding
 * progress, working directory / context / check types, where the checkout lives (Harbor with its selectable ID, the
 * Admiral, or the typed reason none is available), branch and drift, toolchains and probes, environments,
 * delivery coverage, the setup checklist, and the issues with their related values.
 */
export function ReadinessCard({ title, readiness, loading = false, emptyMessage, testID }: {
  title: string; readiness: VesselReadinessResult | null; loading?: boolean; emptyMessage: string; testID?: string;
}) {
  const { t } = useLocale();
  const router = useRouter();
  const { colors } = useTheme();
  const tone = readinessTone(readiness);
  const branchSummary = readinessBranchSummary(readiness);
  const drift = readinessDriftSummary(readiness);
  const checkout = readinessCheckout(readiness);
  const checkoutText = checkout ? readinessCheckoutText(checkout) : null;
  return (
    <Section title={title}>
      <View style={styles.body} testID={testID}>
        <View style={styles.head}>
          <View style={styles.flex}>
            {readiness?.workflowProfileName ? (
              <AppText variant="caption" muted>
                {`${t('Resolved profile')}: ${readiness.workflowProfileName}${readiness.workflowProfileScope ? ` (${readiness.workflowProfileScope})` : ''}`}
              </AppText>
            ) : null}
            {readiness && readiness.setupChecklistTotalCount > 0 ? (
              <AppText variant="caption" muted>
                {t('Onboarding: {{done}}/{{total}} steps complete', { done: readiness.setupChecklistSatisfiedCount, total: readiness.setupChecklistTotalCount })}
              </AppText>
            ) : null}
          </View>
          <StatusBadge label={t(readinessLabel(readiness))} tone={tone === 'ready' ? 'success' : tone === 'error' ? 'failed' : 'warning'} />
        </View>
        {loading ? (
          <View style={styles.row}><ActivityIndicator color={colors.primary} /><AppText muted>{t('Checking readiness...')}</AppText></View>
        ) : !readiness ? (
          <AppText muted>{emptyMessage}</AppText>
        ) : (
          <>
            <AppText variant="caption">
              {[
                readiness.hasWorkingDirectory ? t('Working directory available') : t('Working directory unavailable'),
                readiness.hasRepositoryContext ? t('Repository context available') : t('Repository context unavailable'),
                readiness.availableCheckTypes.length > 0 ? t('{{count}} check type(s) available', { count: readiness.availableCheckTypes.length }) : null,
              ].filter(Boolean).join(' \u00B7 ')}
            </AppText>
            {checkout && checkoutText ? (
              <Line label={t('Checkout')}>
                <AppText selectable color={checkout.kind === 'unavailable' ? 'warning' : 'text'} testID="readiness-checkout">
                  {t(checkoutText.template, checkoutText.params)}
                </AppText>
                {checkout.kind === 'harbor' ? (
                  <AppText variant="caption" muted>
                    {`${t('Harbor ID')}: `}
                    <AppText selectable variant="mono" testID="readiness-checkout-harbor-id">{checkout.harborId}</AppText>
                  </AppText>
                ) : null}
              </Line>
            ) : null}
            {readiness.availableCheckTypes.length > 0 ? <Chips items={readiness.availableCheckTypes} /> : null}
            {branchSummary ? <Line label={t('Branch')}><AppText style={typography.mono}>{branchSummary}</AppText></Line> : null}
            {drift ? <Line label={t('Remote drift')}>{drift}</Line> : null}
            {readiness.hasUncommittedChanges != null ? (
              <Line label={t('Working tree')}>{readiness.hasUncommittedChanges ? t('Uncommitted changes present') : t('Clean working tree')}</Line>
            ) : null}
            {readiness.detectedToolchains.length > 0 ? <Line label={t('Detected toolchains')}><Chips items={readiness.detectedToolchains} /></Line> : null}
            {readiness.toolchainProbes.length > 0 ? (
              <Line label={t('Toolchain probes')}>
                {readiness.toolchainProbes.map((probe) => (
                  <AppText key={`${probe.name}-${probe.command}`} variant="caption" color={probe.available ? 'text' : 'warning'}>
                    {`${probe.name}: ${probe.version || (probe.available ? t('available') : t('missing'))}${probe.expected ? ` (${t('expected')})` : ''}`}
                  </AppText>
                ))}
              </Line>
            ) : null}
            {readiness.deploymentEnvironments.length > 0 ? <Line label={t('Environments')}><Chips items={readiness.deploymentEnvironments} /></Line> : null}
            {readiness.deploymentMetadata ? (
              <Line label={t('Delivery coverage')}>
                <Chips items={[
                  t('{{count}} env(s)', { count: readiness.deploymentMetadata.environmentCount }),
                  ...(readiness.deploymentMetadata.hasDeployCommand ? [t('Deploy')] : []),
                  ...(readiness.deploymentMetadata.hasRollbackCommand ? [t('Rollback')] : []),
                  ...(readiness.deploymentMetadata.hasSmokeTestCommand ? [t('Smoke')] : []),
                  ...(readiness.deploymentMetadata.hasHealthCheckCommand ? [t('Health')] : []),
                  ...(readiness.deploymentMetadata.hasDeploymentVerificationCommand ? [t('Deploy Verify')] : []),
                  ...(readiness.deploymentMetadata.hasRollbackVerificationCommand ? [t('Rollback Verify')] : []),
                ]} />
              </Line>
            ) : null}
            {readiness.setupChecklist.length > 0 ? (
              <View style={styles.block}>
                <View style={styles.row}>
                  <AppText variant="label" style={styles.flex}>
                    {`${t('Setup checklist')} (${readiness.setupChecklistSatisfiedCount}/${readiness.setupChecklistTotalCount})`}
                  </AppText>
                  {readiness.setupChecklistSatisfiedCount < readiness.setupChecklistTotalCount ? (
                    <Button label={t('Open Onboarding')} variant="ghost" onPress={() => router.push(vesselLinks.onboarding(readiness.vesselId) as Href)} style={styles.inlineButton} />
                  ) : null}
                </View>
                {readiness.setupChecklist.map((item) => <ChecklistItem key={item.code} item={item} />)}
              </View>
            ) : null}
            {readiness.issues.length > 0 ? (
              <View style={styles.block}>
                {readiness.issues.map((issue, index) => (
                  <IssueBox
                    key={`${issue.code}-${index}`}
                    title={issue.title}
                    severity={issue.severity}
                    message={issue.message}
                    related={issue.relatedValue ? `${issue.relatedValue}${issue.inputProvider ? ` (${formatInputProvider(issue.inputProvider)})` : ''}` : null}
                  />
                ))}
              </View>
            ) : (
              <AppText color="success">{t('This vessel looks ready for the currently selected workflow surface.')}</AppText>
            )}
          </>
        )}
      </View>
    </Section>
  );
}

/** The vessel page's Landing Preview card: what landing the default branch would do now, and what blocks it. */
export function LandingPreviewCard({ preview, loading }: { preview: LandingPreviewResult | null; loading: boolean }) {
  const { t } = useLocale();
  const { colors } = useTheme();
  return (
    <Section title={t('Landing Preview')}>
      <View style={styles.body} testID="vessel-landing-preview">
        <View style={styles.head}>
          <AppText variant="caption" muted style={styles.flex}>
            {preview?.sourceBranch ? `${preview.sourceBranch} -> ${preview.targetBranch}` : preview?.targetBranch || t('No branch selected')}
          </AppText>
          <StatusBadge label={preview?.isReadyToLand ? t('Ready To Land') : t('Needs Review')} tone={preview?.isReadyToLand ? 'success' : 'warning'} />
        </View>
        {loading ? (
          <View style={styles.row}><ActivityIndicator color={colors.primary} /><AppText muted>{t('Calculating landing preview...')}</AppText></View>
        ) : !preview ? (
          <AppText muted>{t('Landing preview is not available for this vessel yet.')}</AppText>
        ) : (
          <>
            <AppText variant="caption">
              {[
                `${t('Branch category')}: ${preview.branchCategory}`,
                `${t('Landing mode')}: ${preview.landingMode || t('Inherited')}`,
                `${t('Cleanup')}: ${preview.branchCleanupPolicy || t('Inherited')}`,
                preview.expectedLandingAction ? `${t('Action')}: ${preview.expectedLandingAction}` : null,
                preview.requirePassingChecksToLand ? t('Passing checks required') : t('Passing checks optional'),
              ].filter(Boolean).join(' \u00B7 ')}
            </AppText>
            <AppText variant="caption">
              {[
                preview.targetBranchProtected ? t('Protected target branch') : t('Target branch not protected'),
                preview.protectedBranchMatch ? `${t('Policy')}: ${preview.protectedBranchMatch}` : null,
                preview.requirePullRequestForProtectedBranches ? t('PR required for protected branches') : null,
                preview.requireMergeQueueForReleaseBranches ? t('Merge queue required for release branches') : null,
              ].filter(Boolean).join(' \u00B7 ')}
            </AppText>
            {preview.latestCheckSummary ? <Line label={t('Latest check')}>{preview.latestCheckSummary}</Line> : null}
            {preview.issues.length > 0 ? (
              <View style={styles.block}>
                {preview.issues.map((issue, index) => (
                  <IssueBox key={`${issue.code}-${index}`} title={issue.title} severity={issue.severity} message={issue.message} />
                ))}
              </View>
            ) : (
              <AppText color="success">{t('No landing blockers are currently predicted for this vessel.')}</AppText>
            )}
          </>
        )}
      </View>
    </Section>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  body: { padding: spacing.md, gap: spacing.sm },
  head: { flexDirection: 'row', alignItems: 'flex-start', gap: spacing.sm },
  row: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  line: { gap: 2 },
  block: { gap: spacing.sm, marginTop: spacing.xs },
  chips: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs },
  chip: { borderWidth: StyleSheet.hairlineWidth, borderRadius: radius.pill, paddingHorizontal: spacing.sm, paddingVertical: 2 },
  issue: { borderWidth: StyleSheet.hairlineWidth, borderRadius: radius.md, padding: spacing.sm, gap: 2 },
  issueHead: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  small: { fontSize: 12 },
  inlineButton: { marginBottom: 0, alignSelf: 'flex-start' },
});
