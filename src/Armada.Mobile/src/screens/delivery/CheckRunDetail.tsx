import { Stack, useLocalSearchParams, useRouter, type Href } from 'expo-router';
import { useState } from 'react';
import { Linking } from 'react-native';
import { deleteCheckRun, getCheckRun, getVessel, getWorkflowProfile, listCheckRuns, retryCheckRun } from '@dashboard/api/client';
import type { CheckRun, Vessel, WorkflowProfile } from '@dashboard/types/models';
import { formatCheckDuration, formatCoverageMetric } from '@dashboard/lib/deliveryForms';
import {
  buildCheckRunComparison, formatCheckRunComparisonScope, formatCheckRunComparisonSummary, formatSignedCountDelta, formatSignedDurationDelta,
  formatSignedPercentageDelta, type CheckRunComparison,
} from '@dashboard/lib/checkRunComparison';
import { ActionBar, DetailBody, DetailHeader, DetailPending, Field, FieldCard, JsonSheet, TextBlock } from '../../components/resource/DetailParts';
import { ResourceRow, useConfirm } from '../../components/resource/ResourceRow';
import { resourceStyles } from '../../components/resource/styles';
import { AppText } from '../../components/ui/AppText';
import { Button } from '../../components/ui/Button';
import { StatusBadge } from '../../components/ui/StatusBadge';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { statusBadge } from '../../resource/status';
import { errorText, useLoad, useReloadOnFocus } from '../../resource/useLoad';
import { draftReleaseLink } from './checkLinks';

export interface CheckRunDetailViewProps {
  id: string;
  embedded?: boolean;
  onDeleted?: () => void;
  onChanged?: () => void;
}

interface CheckRunData {
  run: CheckRun;
  vessel: Vessel | null;
  profile: WorkflowProfile | null;
  comparison: CheckRunComparison | null;
}

/**
 * One check run (the dashboard's /checks/:id): status, provenance, command, summary, the comparison to the previous
 * comparable run, test results, coverage, artifacts, and output, with Retry, Draft Release, View JSON, and Delete.
 */
export function CheckRunDetailView({ id, embedded, onDeleted, onChanged }: CheckRunDetailViewProps) {
  const { t, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();
  const router = useRouter();
  const { confirm, dialog } = useConfirm('check-confirm');
  const [jsonOpen, setJsonOpen] = useState(false);
  const [retrying, setRetrying] = useState(false);

  const { data, loading, refreshing, error, reload, refresh } = useLoad<CheckRunData>(async () => {
    const run = await getCheckRun(id);
    const [vessel, profile, related] = await Promise.all([
      run.vesselId ? getVessel(run.vesselId).catch(() => null) : Promise.resolve(null),
      run.workflowProfileId ? getWorkflowProfile(run.workflowProfileId).catch(() => null) : Promise.resolve(null),
      run.vesselId ? listCheckRuns({ pageSize: 1000, filters: { vesselId: run.vesselId, type: run.type } }).catch(() => null) : Promise.resolve(null),
    ]);
    return { run, vessel, profile, comparison: related?.objects ? buildCheckRunComparison(run, related.objects) : null };
  }, [id], { live: ['check-run.'], fallbackError: t('Failed to load check run.') });
  useReloadOnFocus(reload);

  if (!data) return <DetailPending loading={loading} error={error || (loading ? '' : t('Check run not found.'))} onRetry={() => void reload()} />;
  const { run, vessel, profile, comparison } = data;
  const go = (href: string) => router.push(href as Href);

  async function retry() {
    setRetrying(true);
    try {
      const retried = await retryCheckRun(run.id);
      pushToast(retried.status === 'Passed' ? 'success' : 'warning', t('Retry completed with status {{status}}.', { status: retried.status }));
      onChanged?.();
      go(`/checks/${retried.id}`);
    } catch (err: unknown) {
      pushToast('error', errorText(err, t('Retry failed.')));
    } finally {
      setRetrying(false);
    }
  }

  function remove() {
    confirm({
      title: t('Delete Check Run'),
      message: t('Delete check run "{{id}}"?', { id: run.id }),
      confirmLabel: t('Delete'),
      danger: true,
      onConfirm: async () => {
        try {
          await deleteCheckRun(run.id);
          pushToast('warning', t('Check run deleted.'));
          if (onDeleted) onDeleted();
          else router.back();
        } catch (err: unknown) {
          pushToast('error', errorText(err, t('Delete failed.')));
        }
      },
    });
  }

  const title = run.label || run.id;
  return (
    <DetailBody embedded={embedded} refreshing={refreshing} onRefresh={() => void refresh()} testID="check-detail">
      {!embedded ? <Stack.Screen options={{ title }} /> : null}
      <DetailHeader title={title} subtitle={run.id} testID="check-title" badges={<StatusBadge {...statusBadge(t, run.status)} />} />
      <ActionBar>
        <Button label={retrying ? t('Retrying...') : t('Retry')} busy={retrying} style={resourceStyles.action} onPress={() => void retry()} testID="check-retry" />
        <Button label={t('Draft Release')} variant="secondary" style={resourceStyles.action} onPress={() => go(draftReleaseLink(run))} />
        <Button label={t('View JSON')} variant="ghost" style={resourceStyles.action} onPress={() => setJsonOpen(true)} />
        <Button label={t('Delete')} variant="danger" style={resourceStyles.action} onPress={remove} testID="check-delete" />
      </ActionBar>

      <FieldCard title={t('Overview')}>
        <Field label={t('Type')} value={run.type} />
        <Field label={t('Status')} value={t(run.status)} />
        <Field label={t('Exit Code')} value={run.exitCode ?? '-'} />
        <Field label={t('Vessel')} value={run.vesselId ? (vessel?.name || run.vesselId) : null} onPress={run.vesselId ? () => go(`/vessels/${run.vesselId}`) : undefined} />
        <Field label={t('Workflow Profile')} value={run.workflowProfileId ? (profile?.name || run.workflowProfileId) : t('Resolved override only')} onPress={run.workflowProfileId ? () => go(`/workflow-profiles/${run.workflowProfileId}`) : undefined} />
        <Field label={t('Source')} value={run.source} />
        <Field label={t('Provider')} value={run.providerName} />
        <Field label={t('External ID')} value={run.externalId} mono />
        <Field label={t('External URL')} value={run.externalUrl} onPress={run.externalUrl ? () => void Linking.openURL(run.externalUrl!) : undefined} />
        <Field label={t('Environment')} value={run.environmentName} />
        <Field label={t('Duration')} value={formatCheckDuration(run.durationMs)} />
        <Field label={t('Mission ID')} value={run.missionId} mono onPress={run.missionId ? () => go(`/missions/${run.missionId}`) : undefined} />
        <Field label={t('Voyage ID')} value={run.voyageId} mono onPress={run.voyageId ? () => go(`/voyages/${run.voyageId}`) : undefined} />
        <Field label={t('Deployment ID')} value={run.deploymentId} mono onPress={run.deploymentId ? () => go(`/deployments/${run.deploymentId}`) : undefined} />
        <Field label={t('Branch')} value={run.branchName} mono />
        <Field label={t('Commit')} value={run.commitHash} mono />
        <Field label={t('Created')} value={formatDateTime(run.createdUtc)} />
        <Field label={t('Started')} value={run.startedUtc ? formatDateTime(run.startedUtc) : null} />
        <Field label={t('Completed')} value={run.completedUtc ? formatDateTime(run.completedUtc) : null} />
      </FieldCard>
      <TextBlock title={t('Command')} text={run.command} mono />
      {run.summary ? <TextBlock title={t('Summary')} text={run.summary} /> : null}

      {comparison ? (
        <FieldCard title={t('Compare to Previous Run')} testID="check-comparison">
          <ResourceRow
            title={`${t('Compared to')} ${comparison.baseline.label || comparison.baseline.id}`}
            subtitle={`${t(formatCheckRunComparisonScope(comparison.scope))} \u2022 ${formatCheckRunComparisonSummary(comparison)}`}
            badge={{ label: comparison.hasRegression ? t('Regression detected') : comparison.hasImprovement ? t('Improvement detected') : t('No significant change'), tone: comparison.hasRegression ? 'failed' : comparison.hasImprovement ? 'success' : 'info' }}
            onPress={() => go(`/checks/${comparison.baseline.id}`)}
          />
          <Field label={t('Status')} value={`${comparison.baseline.status} -> ${run.status}`} />
          <Field label={t('Duration Delta')} value={formatSignedDurationDelta(comparison.durationDeltaMs) || '-'} />
          <Field label={t('Artifact Delta')} value={formatSignedCountDelta(comparison.artifactCountDelta, 'artifacts') || '-'} />
          <Field label={t('Passed Delta')} value={formatSignedCountDelta(comparison.testDelta.passed, 'passed') || '-'} />
          <Field label={t('Failed Delta')} value={formatSignedCountDelta(comparison.testDelta.failed, 'failed') || '-'} />
          <Field label={t('Skipped Delta')} value={formatSignedCountDelta(comparison.testDelta.skipped, 'skipped') || '-'} />
          <Field label={t('Total Delta')} value={formatSignedCountDelta(comparison.testDelta.total, 'tests') || '-'} />
          <Field label={t('Line Coverage Delta')} value={formatSignedPercentageDelta(comparison.coverageDelta.linesPct) || '-'} />
          <Field label={t('Branch Coverage Delta')} value={formatSignedPercentageDelta(comparison.coverageDelta.branchesPct) || '-'} />
          <Field label={t('Function Coverage Delta')} value={formatSignedPercentageDelta(comparison.coverageDelta.functionsPct) || '-'} />
          <Field label={t('Statement Coverage Delta')} value={formatSignedPercentageDelta(comparison.coverageDelta.statementsPct) || '-'} />
        </FieldCard>
      ) : null}
      {run.testSummary ? (
        <FieldCard title={t('Test Results')}>
          <Field label={t('Format')} value={run.testSummary.format} />
          <Field label={t('Passed')} value={run.testSummary.passed ?? '-'} />
          <Field label={t('Failed')} value={run.testSummary.failed ?? '-'} />
          <Field label={t('Skipped')} value={run.testSummary.skipped ?? '-'} />
          <Field label={t('Total')} value={run.testSummary.total ?? '-'} />
          <Field label={t('Test Duration')} value={formatCheckDuration(run.testSummary.durationMs)} />
        </FieldCard>
      ) : null}
      {run.coverageSummary ? (
        <FieldCard title={t('Coverage')}>
          <Field label={t('Format')} value={run.coverageSummary.format} />
          <Field label={t('Source')} value={run.coverageSummary.sourcePath} mono />
          <Field label={t('Lines')} value={formatCoverageMetric(run.coverageSummary.lines) || '-'} />
          <Field label={t('Branches')} value={formatCoverageMetric(run.coverageSummary.branches) || '-'} />
          <Field label={t('Functions')} value={formatCoverageMetric(run.coverageSummary.functions) || '-'} />
          <Field label={t('Statements')} value={formatCoverageMetric(run.coverageSummary.statements) || '-'} />
        </FieldCard>
      ) : null}
      <FieldCard title={t('Artifacts')}>
        {run.artifacts.length === 0 ? <AppText muted style={resourceStyles.pad}>{t('No artifacts were collected for this run.')}</AppText> : run.artifacts.map((a) => (
          <ResourceRow key={a.path} title={a.path} subtitle={`${a.sizeBytes.toLocaleString()} ${t('bytes')} \u2022 ${formatDateTime(a.lastWriteUtc)}`} />
        ))}
      </FieldCard>
      <TextBlock title={t('Output')} text={run.output} mono emptyText={t('No output captured.')} testID="check-output" />
      <JsonSheet open={jsonOpen} title={title} data={run} onClose={() => setJsonOpen(false)} />
      {dialog}
    </DetailBody>
  );
}

/** The /checks/:id route. */
export function CheckRunDetailRoute() {
  const { id } = useLocalSearchParams<{ id: string }>();
  return <CheckRunDetailView id={id} />;
}
