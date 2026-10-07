import { Stack, useRouter, type Href } from 'expo-router';
import { useState } from 'react';
import { RefreshControl, StyleSheet, View } from 'react-native';
import { deleteSignalsBatch, getSignal, markSignalRead } from '@dashboard/api/client';
import { formatSignalPayload } from '@dashboard/lib/signals';
import type { Signal } from '@dashboard/types/models';
import { EntityStatusBadge } from '../../components/app/EntityStatusBadge';
import { JsonSheet } from '../../components/app/JsonSheet';
import { useConfirm } from '../../components/app/useConfirm';
import { AppText, Button, CodeBlock, KeyValueRow, Screen, Section } from '../../components/ui';
import { errorMessage } from '../../data/errors';
import { useNameLookups } from '../../data/useNameLookups';
import { useQuery } from '../../data/useQuery';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { useTheme } from '../../theme/ThemeContext';
import { spacing } from '../../theme/typography';
import type { OperationsDetailProps } from './listTypes';
import { DetailActions, DetailHeading, DetailState, useWhen } from './w24/DetailParts';

/** The API may return the mission a signal belongs to, which the base model leaves out. */
interface SignalWithMission extends Signal {
  missionId?: string | null;
}

/** A signal (the dashboard's SignalDetail): fields, sender and recipient links, payload, mark read, JSON, delete. */
export function SignalDetail({ id, embedded }: OperationsDetailProps) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const router = useRouter();
  const { pushToast } = useNotifications();
  const when = useWhen();
  const lookups = useNameLookups({ captains: true });
  const [confirmElement, ask] = useConfirm('signal-detail-confirm');
  const [json, setJson] = useState(false);
  const query = useQuery<SignalWithMission>(() => getSignal(id), [id], t('Failed to load signal.'));
  const signal = query.data;
  const title = t('Signal Details');
  const party = (captainId: string | null) => (captainId ? lookups.captains.find((c) => c.id === captainId)?.name || captainId : t('Admiral'));

  if (!signal) {
    return (
      <View style={styles.fill} testID="signal-detail">
        {embedded ? null : <Stack.Screen options={{ title }} />}
        <DetailState loading={query.loading} error={query.error} missing={!query.loading} missingTitle={t('Signal not found.')}
          onRetry={() => void query.refresh()} onBack={embedded ? undefined : () => router.back()} backLabel={t('Back to Signals')} />
      </View>
    );
  }
  const { formatted } = formatSignalPayload(signal.payload, t('(empty)'));
  return (
    <Screen testID="signal-detail" refreshControl={<RefreshControl refreshing={query.refreshing} onRefresh={() => void query.refresh()} tintColor={colors.primary} />}>
      {embedded ? null : <Stack.Screen options={{ title }} />}
      <View style={styles.head}>
        <EntityStatusBadge status={signal.type} testID="signal-type" />
        <AppText muted testID="signal-read">{signal.read ? t('Read') : t('Unread')}</AppText>
      </View>
      <DetailActions>
        {!signal.read ? (
          <Button label={t('Mark Read')} icon="mail-open-outline" testID="signal-mark-read" onPress={async () => {
            try {
              await markSignalRead(signal.id);
              const updated = await getSignal(signal.id);
              query.setData(updated);
              pushToast('success', t('Signal marked as read.'));
            } catch (e) {
              pushToast('error', errorMessage(e, t('Failed to mark signal as read.')));
            }
          }} />
        ) : null}
        <Button label={t('View JSON')} variant="secondary" icon="code-outline" onPress={() => setJson(true)} testID="signal-json" />
        <Button label={t('Delete')} variant="danger" icon="trash-outline" testID="signal-delete" onPress={() => ask({
          title: t('Delete'),
          message: t('Delete {{entity}} {{name}}?', { entity: t('Signal').toLowerCase(), name: signal.id }),
          confirmLabel: t('Delete'),
          danger: true,
          onConfirm: async () => {
            try {
              await deleteSignalsBatch([signal.id]);
              pushToast('warning', t('Signal {{id}} deleted.', { id: signal.id }));
              if (embedded) void query.reload(); else router.back();
            } catch (e) {
              pushToast('error', errorMessage(e, t('Delete failed.')));
            }
          },
        })} />
      </DetailActions>
      <Section>
        <KeyValueRow label={t('ID')} value={signal.id} mono testID="signal-id" />
        <KeyValueRow label={t('Type')} value={t(signal.type)} />
        <KeyValueRow label={t('Read')} value={signal.read ? t('Yes') : t('No')} />
        <KeyValueRow label={t('From')} value={party(signal.fromCaptainId)}
          onPress={signal.fromCaptainId ? () => router.push(`/captains/${signal.fromCaptainId}` as Href) : undefined} />
        <KeyValueRow label={t('To')} value={party(signal.toCaptainId)}
          onPress={signal.toCaptainId ? () => router.push(`/captains/${signal.toCaptainId}` as Href) : undefined} />
        {signal.missionId ? (
          <KeyValueRow label={t('Mission')} value={signal.missionId} mono onPress={() => router.push(`/missions/${signal.missionId}` as Href)} />
        ) : null}
        <KeyValueRow label={t('Tenant ID')} value={signal.tenantId} mono />
        <KeyValueRow label={t('Created')} value={when(signal.createdUtc)} />
      </Section>
      <View style={styles.block}>
        <DetailHeading>{t('Payload')}</DetailHeading>
        <View style={styles.pad}><CodeBlock text={formatted} wrap testID="signal-payload" /></View>
      </View>
      {confirmElement}
      <JsonSheet open={json} title={t('Signal: {{id}}', { id: signal.id })} data={signal} onClose={() => setJson(false)} />
    </Screen>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  head: { flexDirection: 'row', alignItems: 'center', gap: spacing.md, paddingHorizontal: spacing.lg, marginBottom: spacing.md },
  pad: { paddingHorizontal: spacing.lg },
  block: { marginBottom: spacing.xl },
});
