import { Stack, useLocalSearchParams, useRouter, type Href } from 'expo-router';
import { StyleSheet, View } from 'react-native';
import { deleteRequestHistoryEntry, getRequestHistoryEntry } from '@dashboard/api/client';
import { formatBytes } from '@dashboard/lib/format';
import { requestDetailMaps } from '@dashboard/lib/requestHistory';
import { ActionBar, DetailBody, DetailHeader, DetailPending, Field, FieldCard, TextBlock } from '../../components/resource/DetailParts';
import { useConfirm } from '../../components/resource/ResourceRow';
import { resourceStyles } from '../../components/resource/styles';
import { AppText } from '../../components/ui/AppText';
import { Button } from '../../components/ui/Button';
import { Disclosure } from '../../components/ui/Disclosure';
import { StatusBadge } from '../../components/ui/StatusBadge';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { param } from '../../resource/links';
import { errorText, useLoad } from '../../resource/useLoad';
import { spacing } from '../../theme/typography';

/** API Explorer link that replays a stored request (API Explorer loads the entry and prefills from it). */
export function replayHref(entryId: string): string {
  return `/api-explorer?replay=${encodeURIComponent(entryId)}`;
}

function block(value: unknown): string {
  const text = typeof value === 'string' ? value : JSON.stringify(value ?? {}, null, 2);
  return text || '(empty)';
}

function CodeBlock({ title, note, value, initiallyOpen = true }: { title: string; note?: string; value: unknown; initiallyOpen?: boolean }) {
  return (
    <View style={styles.block}>
      <Disclosure title={note ? `${title} (${note})` : title} initiallyOpen={initiallyOpen}>
        <AppText variant="mono" selectable>{block(value)}</AppText>
      </Disclosure>
    </View>
  );
}

/**
 * One captured API request (the dashboard's request detail modal at /requests/:id): summary, path and query
 * parameters, request and response headers and bodies (truncation noted), with Replay (API Explorer) and Delete.
 */
export function RequestDetailView({ id, embedded, onDeleted }: { id: string; embedded?: boolean; onDeleted?: () => void }) {
  const { t, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();
  const router = useRouter();
  const { confirm, dialog } = useConfirm('request-confirm');
  const { data: record, loading, refreshing, error, reload, refresh } = useLoad(() => getRequestHistoryEntry(id), [id], { fallbackError: t('Failed to load request detail.') });
  if (!record) return <DetailPending loading={loading} error={error} onRetry={() => void reload()} />;
  const entry = record.entry;
  const maps = requestDetailMaps(record);

  function remove() {
    confirm({
      title: t('Delete Request Entry'),
      message: t('Delete the stored request-history entry for {{route}}?', { route: entry.route }),
      confirmLabel: t('Delete'),
      danger: true,
      onConfirm: async () => {
        try {
          await deleteRequestHistoryEntry(entry.id);
          pushToast('warning', t('Request entry deleted.'));
          if (onDeleted) onDeleted();
          else if (router.canGoBack()) router.back();
          else router.replace('/activity?source=requests' as Href);
        } catch (err: unknown) {
          pushToast('error', errorText(err, t('Failed to delete request history entry.')));
        }
      },
    });
  }

  return (
    <DetailBody embedded={embedded} refreshing={refreshing} onRefresh={() => void refresh()} testID="request-detail">
      {!embedded ? <Stack.Screen options={{ title: t('Request Detail') }} /> : null}
      <DetailHeader
        title={`${entry.method} ${entry.route}`}
        subtitle={entry.id}
        testID="request-title"
        badges={<StatusBadge label={String(entry.statusCode)} tone={entry.isSuccess ? 'success' : 'failed'} />}
      />
      <ActionBar>
        <Button label={t('Replay')} icon="play-outline" style={resourceStyles.action} onPress={() => router.push(replayHref(entry.id) as Href)} testID="request-replay" />
        <Button label={t('Delete')} variant="danger" style={resourceStyles.action} onPress={remove} testID="request-delete" />
      </ActionBar>
      <FieldCard title={t('Request Detail')}>
        <Field label={t('Entry ID')} value={entry.id} mono />
        <Field label={t('Principal')} value={entry.principalDisplay || t('Anonymous')} />
        <Field label={t('Auth Method')} value={entry.authMethod || '-'} />
        <Field label={t('Status')} value={String(entry.statusCode)} />
        <Field label={t('Duration')} value={`${entry.durationMs.toFixed(2)} ms`} />
        <Field label={t('Captured')} value={formatDateTime(entry.createdUtc)} />
        <Field label={t('Payloads')} value={`${formatBytes(entry.requestSizeBytes)} / ${formatBytes(entry.responseSizeBytes)}`} />
      </FieldCard>
      {entry.queryString ? <TextBlock title={t('Query String')} text={entry.queryString} mono /> : null}
      <CodeBlock title={t('Path Parameters')} value={maps.path} />
      <CodeBlock title={t('Query Parameters')} value={maps.query} />
      <CodeBlock title={t('Request Headers')} value={maps.request} />
      <CodeBlock title={t('Response Headers')} value={maps.response} />
      <CodeBlock
        title={t('Request Body')}
        value={record.detail?.requestBodyText || '(empty)'}
        note={record.detail?.requestBodyTruncated ? t('Stored body was truncated') : formatBytes(entry.requestSizeBytes)}
      />
      <CodeBlock
        title={t('Response Body')}
        value={record.detail?.responseBodyText || '(empty)'}
        note={record.detail?.responseBodyTruncated ? t('Stored body was truncated') : formatBytes(entry.responseSizeBytes)}
      />
      {dialog}
    </DetailBody>
  );
}

/** The /requests/:id route. */
export function RequestDetailRoute() {
  const params = useLocalSearchParams<{ id: string }>();
  return <RequestDetailView id={param(params.id)} />;
}

const styles = StyleSheet.create({
  block: { marginHorizontal: spacing.lg, marginBottom: spacing.md },
});
