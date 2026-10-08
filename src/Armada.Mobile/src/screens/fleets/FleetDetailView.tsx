import { Stack, useRouter, type Href } from 'expo-router';
import { useState } from 'react';
import { RefreshControl, ScrollView, StyleSheet, View } from 'react-native';
import { createFleet, deleteFleet } from '@dashboard/api/client';
import { buildFleetDuplicatePayload } from '@dashboard/lib/duplicates';
import { ActionRow, InfoRow, useActionRunner } from '../../build/fields';
import { useLiveResource } from '../../build/useLiveResource';
import { AppText, Button, ConfirmDialog, EmptyState, ErrorState, ListRow, LoadingState, Section } from '../../components/ui';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { useTheme } from '../../theme/ThemeContext';
import { spacing } from '../../theme/typography';
import { JsonSheet } from '../fleetActions/common';
import { FleetFormSheet } from './FleetFormSheet';
import { loadFleetData, vesselsOfFleet } from './fleetData';

export interface FleetDetailViewProps {
  id: string;
  /** Rendered beside the list on tablets: no navigation title, and the callbacks below replace navigation. */
  embedded?: boolean;
  onDeleted?: () => void;
  onChanged?: () => void;
  /** Open another fleet (after Duplicate) in place. */
  onOpenFleet?: (id: string) => void;
}

/**
 * One fleet (the dashboard's FleetDetail page): its fields, its vessels (tap to open), and Edit, Duplicate, View
 * JSON, and Delete with the dashboard's confirmation.
 */
export function FleetDetailView({ id, embedded, onDeleted, onChanged, onOpenFleet }: FleetDetailViewProps) {
  const { t, formatDateTime } = useLocale();
  const { colors } = useTheme();
  const router = useRouter();
  const { pushToast } = useNotifications();
  const { busy, run } = useActionRunner();
  const data = useLiveResource(loadFleetData, [id]);
  const [editing, setEditing] = useState(false);
  const [json, setJson] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);

  const fleet = data.data?.fleets.find((f) => f.id === id) ?? null;
  const vessels = data.data ? vesselsOfFleet(data.data.vessels, id) : [];
  const pipeline = fleet?.defaultPipelineId ? data.data?.pipelines.find((p) => p.id === fleet.defaultPipelineId) : null;
  const title = fleet?.name ?? t('Fleet');

  async function duplicate() {
    if (!fleet) return;
    const created = await run('duplicate', () => createFleet(buildFleetDuplicatePayload(fleet)));
    if (!created) return;
    pushToast('success', t('Fleet "{{name}}" duplicated.', { name: created.name }));
    onChanged?.();
    if (onOpenFleet) onOpenFleet(created.id);
    else router.replace(`/fleets/${created.id}` as Href);
  }

  async function remove() {
    setConfirmDelete(false);
    if (!fleet) return;
    const ok = await run('delete', async () => { await deleteFleet(fleet.id); return true; });
    if (!ok) return;
    pushToast('warning', t('Fleet "{{name}}" deleted.', { name: fleet.name }));
    if (onDeleted) onDeleted();
    else if (router.canGoBack()) router.back();
    else router.replace('/vessels?tab=fleets' as Href);
  }

  let body;
  if (data.loading) body = <LoadingState label={t('Loading...')} />;
  else if (!fleet && data.error) body = <ErrorState title={t('Failed to load fleet.')} message={data.error} retryLabel={t('Retry')} onRetry={() => void data.refresh()} />;
  else if (!fleet) body = <EmptyState icon="albums-outline" title={t('Fleet not found.')} />;
  else {
    body = (
      <ScrollView
        contentContainerStyle={styles.content}
        refreshControl={<RefreshControl refreshing={data.refreshing} onRefresh={() => void data.refresh()} tintColor={colors.primary} />}
      >
        <AppText variant="title" accessibilityRole="header" style={styles.title}>{fleet.name}</AppText>
        <ActionRow>
          <Button label={t('Edit')} icon="create-outline" variant="secondary" onPress={() => setEditing(true)} testID="fleet-detail-edit" />
          <Button label={t('Duplicate')} icon="copy-outline" variant="secondary" busy={busy === 'duplicate'} onPress={() => void duplicate()} testID="fleet-detail-duplicate" />
          <Button label={t('View JSON')} icon="code-slash-outline" variant="ghost" onPress={() => setJson(true)} testID="fleet-detail-json" />
          <Button label={t('Delete')} icon="trash-outline" variant="danger" busy={busy === 'delete'} onPress={() => setConfirmDelete(true)} testID="fleet-detail-delete" />
        </ActionRow>
        <Section>
          <InfoRow label={t('ID')} value={fleet.id} mono />
          <InfoRow label={t('Name')} value={fleet.name} />
          <InfoRow label={t('Description')} value={fleet.description} />
          <InfoRow label={t('Default Pipeline')} value={pipeline?.name ?? fleet.defaultPipelineId ?? t('None (WorkerOnly)')} testID="fleet-detail-pipeline" />
          <InfoRow label={t('Active')} value={fleet.active !== false ? t('Yes') : t('No')} />
          <InfoRow label={t('Created')} value={formatDateTime(fleet.createdUtc)} />
          <InfoRow label={t('Last Updated')} value={formatDateTime(fleet.lastUpdateUtc)} />
        </Section>
        <Section title={t('Vessels')}>
          {vessels.length === 0 ? (
            <ListRow title={t('No vessels in this fleet.')} />
          ) : vessels.map((v) => (
            <ListRow
              key={v.id}
              testID={`fleet-vessel-${v.name}`}
              icon="git-branch-outline"
              title={v.name}
              subtitle={[v.repoUrl, v.defaultBranch || 'main'].filter(Boolean).join(' \u00b7 ')}
              onPress={() => router.push(`/vessels/${v.id}` as Href)}
            />
          ))}
        </Section>
      </ScrollView>
    );
  }

  return (
    <View style={[styles.fill, { backgroundColor: colors.background }]} testID="fleet-detail">
      {!embedded ? <Stack.Screen options={{ title }} /> : null}
      {body}
      <FleetFormSheet
        open={editing}
        fleet={fleet}
        pipelines={data.data?.pipelines ?? []}
        onClose={() => setEditing(false)}
        onSaved={(_saved, name) => {
          setEditing(false);
          pushToast('success', t('Fleet "{{name}}" saved.', { name }));
          void data.reload();
          onChanged?.();
        }}
      />
      <JsonSheet open={json} title={fleet ? t('Fleet: {{name}}', { name: fleet.name }) : ''} data={fleet} onClose={() => setJson(false)} />
      <ConfirmDialog
        open={confirmDelete}
        title={t('Delete Fleet')}
        message={fleet ? t('Delete fleet "{{name}}"? This cannot be undone.', { name: fleet.name }) : ''}
        confirmLabel={t('Delete')}
        cancelLabel={t('Cancel')}
        danger
        onConfirm={() => void remove()}
        onCancel={() => setConfirmDelete(false)}
        testID="fleet-delete-confirm"
      />
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  content: { paddingVertical: spacing.lg, width: '100%', maxWidth: 820, alignSelf: 'center' },
  title: { marginHorizontal: spacing.lg, marginBottom: spacing.md },
});
