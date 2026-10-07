import { Stack } from 'expo-router';
import { useEffect, useState } from 'react';
import { SafeAreaView } from 'react-native-safe-area-context';
import { StyleSheet } from 'react-native';
import { cancelJob, getJob, listJobs } from '@dashboard/api/client';
import type { Job } from '@dashboard/types/models';
import { isJobTerminal } from '@dashboard/lib/jobs';
import { Field, FieldCard, TextBlock } from '../../components/resource/DetailParts';
import { ResourceList } from '../../components/resource/ResourceList';
import { ResourceRow } from '../../components/resource/ResourceRow';
import { resourceStyles } from '../../components/resource/styles';
import { BottomSheet } from '../../components/ui/BottomSheet';
import { Button } from '../../components/ui/Button';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { statusBadge } from '../../resource/status';
import { errorText, useLoad, useReloadOnFocus } from '../../resource/useLoad';
import { useTheme } from '../../theme/ThemeContext';

/** How often the list refreshes while a job is still running (the dashboard's auto-refresh, scoped to live work). */
export const JOBS_ACTIVE_REFRESH_MS = 3000;

/**
 * Background jobs (/jobs): name, kind, status, progress, and error, newest first, with Cancel for jobs that have not
 * finished. A job opens a sheet with its full record (refreshed with getJob). The list refreshes itself while any
 * job is active.
 */
export function JobsScreen() {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { colors } = useTheme();
  const { pushToast } = useNotifications();
  const [openId, setOpenId] = useState<string | null>(null);
  const { data, loading, refreshing, error, reload, refresh } = useLoad(async () => (await listJobs()).objects ?? [], [], { fallbackError: t('Failed to load jobs.') });
  useReloadOnFocus(reload);
  const jobs = data ?? [];
  const anyActive = jobs.some((j) => !isJobTerminal(j.status));

  useEffect(() => {
    if (!anyActive) return undefined;
    const timer = setInterval(() => { void reload(); }, JOBS_ACTIVE_REFRESH_MS);
    return () => clearInterval(timer);
  }, [anyActive, reload]);

  const job = useLoad(() => getJob(openId ?? ''), [openId], { enabled: openId !== null });

  async function cancel(j: Job) {
    try {
      await cancelJob(j.id);
      pushToast('warning', t('Job "{{name}}" cancelled.', { name: j.name }));
      await reload();
      if (openId === j.id) await job.reload();
    } catch (err: unknown) {
      pushToast('error', errorText(err, t('Failed to cancel job.')));
    }
  }

  const shown = job.data && job.data.id === openId ? job.data : jobs.find((j) => j.id === openId) ?? null;

  return (
    <SafeAreaView edges={['left', 'right']} style={[styles.fill, { backgroundColor: colors.background }]} testID="jobs">
      <Stack.Screen options={{ title: t('Jobs') }} />
      <ResourceList
        testID="jobs-list"
        items={jobs}
        keyOf={(j) => j.id}
        loading={loading}
        error={error}
        onRetry={() => void reload()}
        refreshing={refreshing}
        onRefresh={() => void refresh()}
        emptyTitle={t('No background jobs.')}
        emptyMessage={t('Background jobs and their status.')}
        renderItem={(j) => (
          <ResourceRow
            testID={`job-row-${j.id}`}
            title={j.name}
            subtitle={[j.kind, `${j.progress}%`, j.errorReason].filter(Boolean).join(' \u2022 ')}
            badge={statusBadge(t, j.status)}
            meta={formatRelativeTime(j.lastUpdateUtc)}
            onPress={() => setOpenId(j.id)}
            actions={isJobTerminal(j.status) ? [] : [{ key: 'cancel', label: t('Cancel'), icon: 'stop-circle-outline', tone: 'danger', onPress: () => void cancel(j) }]}
          />
        )}
      />
      <BottomSheet open={openId !== null} title={shown?.name ?? t('Jobs')} onClose={() => setOpenId(null)} closeLabel={t('Close')} testID="job-sheet">
        {shown ? (
          <>
            <FieldCard>
              <Field label={t('ID')} value={shown.id} mono />
              <Field label={t('Kind')} value={shown.kind} />
              <Field label={t('Status')} value={t(shown.status)} />
              <Field label={t('Progress')} value={`${shown.progress}%`} />
              <Field label={t('Created')} value={formatDateTime(shown.createdUtc)} />
              <Field label={t('Started')} value={shown.startedUtc ? formatDateTime(shown.startedUtc) : null} />
              <Field label={t('Completed')} value={shown.completedUtc ? formatDateTime(shown.completedUtc) : null} />
              <Field label={t('Updated')} value={formatDateTime(shown.lastUpdateUtc)} />
            </FieldCard>
            {shown.errorReason ? <TextBlock title={t('Error')} text={shown.errorReason} /> : null}
            {shown.resultJson ? <TextBlock title={t('Result')} text={shown.resultJson} mono /> : null}
            {!isJobTerminal(shown.status) ? <Button label={t('Cancel')} variant="danger" style={resourceStyles.create} onPress={() => void cancel(shown)} testID="job-cancel" /> : null}
          </>
        ) : null}
      </BottomSheet>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
});
