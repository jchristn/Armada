import { Stack, useRouter, type Href } from 'expo-router';
import { useState } from 'react';
import { RefreshControl, StyleSheet, View } from 'react-native';
import { deleteEventsBatch, getEvent } from '@dashboard/api/client';
import { formatEventPayload } from '@dashboard/lib/eventPayload';
import { entityRoute } from '@dashboard/lib/routing';
import { JsonSheet } from '../../components/app/JsonSheet';
import { useConfirm } from '../../components/app/useConfirm';
import { AppText, Button, CodeBlock, KeyValueRow, Screen, Section } from '../../components/ui';
import { errorMessage } from '../../data/errors';
import { useNameLookups } from '../../data/useNameLookups';
import { useQuery } from '../../data/useQuery';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { appPathFor } from '../../navigation/navItems';
import { useTheme } from '../../theme/ThemeContext';
import { spacing } from '../../theme/typography';
import type { OperationsDetailProps } from './listTypes';
import { DetailActions, DetailHeading, DetailState, useWhen } from './w24/DetailParts';

/** An event (the dashboard's EventDetail): fields with links to the entity and related items, payload, JSON, delete. */
export function EventDetail({ id, embedded }: OperationsDetailProps) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const router = useRouter();
  const { pushToast } = useNotifications();
  const when = useWhen();
  const lookups = useNameLookups({ vessels: true, captains: true });
  const [confirmElement, ask] = useConfirm('event-detail-confirm');
  const [json, setJson] = useState(false);
  const query = useQuery(() => getEvent(id), [id], t('Failed to load event.'));
  const event = query.data;
  const title = t('Event Details');
  const open = (path: string) => router.push(appPathFor(path) as Href);

  if (!event) {
    return (
      <View style={styles.fill} testID="event-detail">
        {embedded ? null : <Stack.Screen options={{ title }} />}
        <DetailState loading={query.loading} error={query.error} missing={!query.loading} missingTitle={t('Event not found.')}
          onRetry={() => void query.refresh()} onBack={embedded ? undefined : () => router.back()} backLabel={t('Back to Events')} />
      </View>
    );
  }
  const entRoute = entityRoute(event.entityType, event.entityId);
  const payload = formatEventPayload(event.payload);
  return (
    <Screen testID="event-detail" refreshControl={<RefreshControl refreshing={query.refreshing} onRefresh={() => void query.refresh()} tintColor={colors.primary} />}>
      {embedded ? null : <Stack.Screen options={{ title }} />}
      <View style={styles.head}>
        <AppText variant="heading" accessibilityRole="header" selectable testID="event-type">{event.eventType}</AppText>
        {event.message ? <AppText selectable>{event.message}</AppText> : null}
      </View>
      <DetailActions>
        <Button label={t('View JSON')} variant="secondary" icon="code-outline" onPress={() => setJson(true)} testID="event-json" />
        <Button label={t('Delete')} variant="danger" icon="trash-outline" testID="event-delete" onPress={() => ask({
          title: t('Delete'),
          message: t('Delete {{entity}} {{name}}?', { entity: t('Event').toLowerCase(), name: event.id }),
          confirmLabel: t('Delete'),
          danger: true,
          onConfirm: async () => {
            try {
              await deleteEventsBatch([event.id]);
              pushToast('warning', t('Event {{id}} deleted.', { id: event.id }));
              if (embedded) void query.reload(); else router.back();
            } catch (e) {
              pushToast('error', errorMessage(e, t('Delete failed.')));
            }
          },
        })} />
      </DetailActions>
      <Section>
        <KeyValueRow label={t('ID')} value={event.id} mono testID="event-id" />
        <KeyValueRow label={t('Event Type')} value={event.eventType} />
        <KeyValueRow label={t('Message')} value={event.message} />
        {event.entityType ? <KeyValueRow label={t('Entity Type')} value={event.entityType} /> : null}
        {event.entityId ? (
          <KeyValueRow label={t('Entity ID')} value={event.entityId} mono onPress={entRoute ? () => open(entRoute) : undefined} testID="event-entity" />
        ) : null}
        {event.captainId ? <KeyValueRow label={t('Captain')} value={lookups.captainName(event.captainId)} onPress={() => open(`/captains/${event.captainId}`)} /> : null}
        {event.missionId ? <KeyValueRow label={t('Mission')} value={event.missionId} mono onPress={() => open(`/missions/${event.missionId}`)} /> : null}
        {event.vesselId ? <KeyValueRow label={t('Vessel')} value={lookups.vesselName(event.vesselId)} onPress={() => open(`/vessels/${event.vesselId}`)} /> : null}
        {event.voyageId ? <KeyValueRow label={t('Voyage')} value={event.voyageId} mono onPress={() => open(`/voyages/${event.voyageId}`)} /> : null}
        <KeyValueRow label={t('Tenant ID')} value={event.tenantId} mono />
        <KeyValueRow label={t('Created')} value={when(event.createdUtc)} />
      </Section>
      {payload ? (
        <View style={styles.block}>
          <DetailHeading>{t('Payload')}</DetailHeading>
          <View style={styles.pad}><CodeBlock text={payload} testID="event-payload" /></View>
        </View>
      ) : null}
      {confirmElement}
      <JsonSheet open={json} title={t('Event: {{id}}', { id: event.id })} data={event} onClose={() => setJson(false)} />
    </Screen>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  head: { paddingHorizontal: spacing.lg, gap: spacing.sm, marginBottom: spacing.md },
  pad: { paddingHorizontal: spacing.lg },
  block: { marginBottom: spacing.xl },
});
