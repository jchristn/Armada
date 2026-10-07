import { Stack, useRouter, type Href } from 'expo-router';
import { useState } from 'react';
import { RefreshControl, ScrollView, StyleSheet, View } from 'react-native';
import { getCaptain, getMission, listMissionSummaries, setCaptainCliPermissionPolicy } from '@dashboard/api/client';
import { captainDetailActions } from '@dashboard/lib/captainForm';
import { canCaptainStartPlanning } from '@dashboard/lib/captains';
import { isMuxRuntime, parseMuxCaptainOptions } from '@dashboard/lib/mux';
import type { Captain, CliPermissionPolicy, Mission, MissionSummary } from '@dashboard/types/models';
import { ActionRow, InfoRow, useActionRunner } from '../../build/fields';
import { useLiveResource } from '../../build/useLiveResource';
import { useAuth } from '../../auth/AuthContext';
import { CodeBlock } from '../../components/ask/CodeBlock';
import { statusTone } from '../../components/ask/statusTone';
import { CliPermissionPolicyField } from '../../components/cliPermissions/CliPermissionPolicyField';
import { AppText, Banner, Button, ErrorState, ListRow, LoadingState, Section, StatusBadge } from '../../components/ui';
import { Disclosure } from '../../components/ui/Disclosure';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { spacing, typography } from '../../theme/typography';
import { CaptainChat } from './CaptainChat';
import { CaptainFormSheet } from './CaptainFormSheet';
import { CaptainLogView } from './CaptainLogView';
import { CaptainToolsView } from './CaptainToolsView';
import { TierBadge } from './TierBadge';
import { useCaptainActions } from './useCaptainActions';

interface CaptainDetailData {
  captain: Captain;
  currentMission: Mission | null;
  missions: MissionSummary[];
}

async function loadDetail(id: string): Promise<CaptainDetailData> {
  const captain = await getCaptain(id);
  let currentMission: Mission | null = null;
  if (captain.currentMissionId) {
    try { currentMission = await getMission(captain.currentMissionId); } catch { currentMission = null; }
  }
  let missions: MissionSummary[] = [];
  try {
    const result = await listMissionSummaries({ pageSize: 100, filters: { captainId: id } });
    missions = result.objects || [];
  } catch { missions = []; }
  return { captain, currentMission, missions };
}

export interface CaptainDetailScreenProps {
  id: string;
  /** Shown beside the list on tablets: no header title of its own; `onRemoved` returns to the list. */
  embedded?: boolean;
  onRemoved?: () => void;
}

/**
 * A captain (the dashboard's CaptainDetail, /captains/:id): state and quarantine, identity and routing, the CLI tool
 * permission policy (admins change it in place), Mux options, current mission and dock, a direct chat with the
 * captain, tool access, the captain log, and recent missions. Actions: edit, duplicate, start planning, recall, stop,
 * lift quarantine, and remove, with the dashboard's confirmations. Live: reloads on captain and mission events.
 */
export function CaptainDetailScreen({ id, embedded, onRemoved }: CaptainDetailScreenProps) {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { colors } = useTheme();
  const router = useRouter();
  const { isAdmin, isTenantAdmin } = useAuth();
  const canManageCliPolicy = isAdmin || isTenantAdmin;
  const resource = useLiveResource(() => loadDetail(id), [id], { live: ['captain.', 'mission.'] });
  const [editing, setEditing] = useState(false);
  const runner = useActionRunner();
  const actions = useCaptainActions('detail', (action) => {
    if (action === 'delete') {
      if (onRemoved) onRemoved();
      else router.replace('/captains' as Href);
      return;
    }
    void resource.reload();
  });

  const data = resource.data;
  const title = data?.captain.name ?? t('Captain');

  if (resource.loading && !data) return <LoadingState label={t('Loading...')} />;
  if (!data) {
    return (
      <>
        {!embedded ? <Stack.Screen options={{ title }} /> : null}
        <ErrorState title={t('Failed to load captain.')} message={resource.error} retryLabel={t('Retry')} onRetry={() => void resource.refresh()} />
      </>
    );
  }

  const { captain, currentMission, missions } = data;
  const lifecycle = captainDetailActions(captain.state);
  const mux = isMuxRuntime(captain.runtime) ? parseMuxCaptainOptions(captain.runtimeOptionsJson) : null;

  async function changePolicy(policy: CliPermissionPolicy | null) {
    const saved = await runner.run('policy', () => setCaptainCliPermissionPolicy(captain.id, policy), t('Captain "{{name}}" saved.', { name: captain.name }));
    if (saved !== undefined) void resource.reload();
  }

  async function duplicate() {
    const created = await actions.duplicate(captain);
    if (created) router.push(`/captains/${created.id}` as Href);
  }

  return (
    <View style={[styles.fill, { backgroundColor: colors.background }]} testID="captain-detail">
      {!embedded ? <Stack.Screen options={{ title }} /> : null}
      <ScrollView
        contentContainerStyle={styles.content}
        keyboardShouldPersistTaps="handled"
        refreshControl={<RefreshControl refreshing={resource.refreshing} onRefresh={() => void resource.refresh()} tintColor={colors.primary} />}
      >
        <View style={styles.column}>
          <View style={styles.header}>
            <AppText variant="title" accessibilityRole="header" style={styles.flex}>{captain.name}</AppText>
            <TierBadge tier={captain.tier} />
            <StatusBadge label={captain.state} tone={statusTone(captain.state)} />
          </View>
          {resource.error ? <Banner tone="danger" title={resource.error} /> : null}
          {captain.state === 'Quarantined' ? (
            <View>
              <Banner
                tone="warning"
                title={t('Quarantine')}
                message={`${captain.quarantineReason || t('quarantined')}${captain.quarantineUntilUtc ? ` (${t('until')} ${formatDateTime(captain.quarantineUntilUtc)})` : ''}`}
                testID="captain-quarantine"
              />
              <ActionRow>
                <Button label={t('Lift Quarantine')} variant="secondary" onPress={() => void actions.unquarantine(captain)} testID="captain-unquarantine" />
              </ActionRow>
            </View>
          ) : null}
          <ActionRow>
            <Button label={t('Edit')} icon="create-outline" variant="secondary" onPress={() => setEditing(true)} testID="captain-edit" />
            <Button label={t('Duplicate')} icon="copy-outline" variant="secondary" onPress={() => void duplicate()} testID="captain-duplicate" />
            {canCaptainStartPlanning(captain) ? (
              <Button label={t('Start Planning')} icon="bulb-outline" variant="secondary" onPress={() => router.push(`/planning?captainId=${encodeURIComponent(captain.id)}` as Href)} testID="captain-start-planning" />
            ) : null}
            {lifecycle.recall ? <Button label={t('Recall Captain')} variant="secondary" onPress={() => actions.request('recall', captain)} testID="captain-recall" /> : null}
            {lifecycle.stop ? <Button label={t('Stop Captain')} variant="danger" onPress={() => actions.request('stop', captain)} testID="captain-stop" /> : null}
            <Button label={t('Remove')} icon="trash-outline" variant="danger" onPress={() => actions.request('delete', captain)} testID="captain-remove" />
          </ActionRow>

          <Section title={t('Captain')}>
            <InfoRow label={t('ID')} value={captain.id} mono />
            <InfoRow label={t('Tenant ID')} value={captain.tenantId} mono />
            <InfoRow label={t('Runtime')} value={captain.runtime || 'ClaudeCode'} />
            <InfoRow label={t('Model')} value={captain.model || t('Runtime default')} />
            <InfoRow label={t('Reasoning effort')} value={captain.reasoningEffort ? t(captain.reasoningEffort) : t('Runtime default')} />
            <InfoRow label={t('Capability tier')} value={captain.tier ? t(captain.tier) : t('Auto (classify from model)')} />
            <InfoRow label={t('Allowed Personas')} value={captain.allowedPersonas || t('Any (no restriction)')} />
            <InfoRow label={t('Preferred Persona')} value={captain.preferredPersona || t('None')} />
          </Section>

          <Section title={t('CLI tool permissions')}>
            <View style={styles.inner}>
              <CliPermissionPolicyField
                label={t('CLI tool permissions')}
                value={captain.cliPermissionPolicy ?? null}
                onChange={(policy) => void changePolicy(policy)}
                allowBypass={canManageCliPolicy}
                disabled={!canManageCliPolicy || runner.busy === 'policy'}
                hint={captain.cliPermissionPolicy ? null : `${t('Inherit (auto-approve option, then server default)')}${canManageCliPolicy ? '' : ` ${t('Only admins can change this.')}`}`}
                testID="captain-cli-policy"
              />
            </View>
          </Section>

          {isMuxRuntime(captain.runtime) ? (
            <Section title={t('Mux')}>
              <InfoRow label={t('Mux Endpoint')} value={mux?.endpoint || t('Not configured')} />
              <InfoRow label={t('Mux Config Directory')} value={mux?.configDirectory || t('Mux default')} mono />
              <InfoRow label={t('Mux Adapter')} value={mux?.adapterType || t('Endpoint default')} />
              <InfoRow label={t('Mux Base URL')} value={mux?.baseUrl || t('Endpoint default')} mono />
            </Section>
          ) : null}

          {captain.systemInstructions ? (
            <Section title={t('System Instructions')}>
              <View style={styles.inner}><AppText selectable style={typography.mono}>{captain.systemInstructions}</AppText></View>
            </Section>
          ) : null}

          <Section title={t('Activity')}>
            {captain.currentMissionId ? (
              <ListRow title={t('Current Mission')} subtitle={captain.currentMissionId} onPress={() => router.push(`/missions/${captain.currentMissionId}` as Href)} testID="captain-current-mission" />
            ) : <InfoRow label={t('Current Mission')} value={null} />}
            {captain.currentDockId ? (
              <ListRow title={t('Current Dock')} subtitle={captain.currentDockId} onPress={() => router.push(`/docks/${captain.currentDockId}` as Href)} testID="captain-current-dock" />
            ) : <InfoRow label={t('Current Dock')} value={null} />}
            <InfoRow label={t('Process ID')} value={captain.processId} />
            <InfoRow label={t('Recovery Attempts')} value={captain.recoveryAttempts ?? 0} />
            <InfoRow label={t('Last Heartbeat')} value={captain.lastHeartbeatUtc ? `${formatRelativeTime(captain.lastHeartbeatUtc)} (${formatDateTime(captain.lastHeartbeatUtc)})` : null} />
            <InfoRow label={t('Created')} value={`${formatRelativeTime(captain.createdUtc)} (${formatDateTime(captain.createdUtc)})`} />
            <InfoRow label={t('Last Updated')} value={`${formatRelativeTime(captain.lastUpdateUtc)} (${formatDateTime(captain.lastUpdateUtc)})`} />
          </Section>

          {currentMission ? (
            <Section title={t('Current Mission')}>
              <ListRow
                title={currentMission.title}
                subtitle={[currentMission.description, `${t('Branch')}: ${currentMission.branchName || '-'}`, `${t('Priority')}: ${currentMission.priority}`].filter(Boolean).join('\n')}
                accessory={<StatusBadge label={currentMission.status} tone={statusTone(currentMission.status)} />}
                onPress={() => router.push(`/missions/${currentMission.id}` as Href)}
              />
            </Section>
          ) : null}

          <AppText variant="subheading" muted accessibilityRole="header" style={styles.sectionTitle}>{t('Chat')}</AppText>
          <CaptainChat captainId={captain.id} captainName={captain.name} />

          <AppText variant="subheading" muted accessibilityRole="header" style={styles.sectionTitle}>{t('Available Tools')}</AppText>
          <CaptainToolsView captainId={captain.id} />

          <AppText variant="subheading" muted accessibilityRole="header" style={styles.sectionTitle}>{t('Captain Log')}</AppText>
          <CaptainLogView captainId={captain.id} />

          <Section title={t('Recent Missions')}>
            {missions.length === 0 ? <ListRow title={t('No missions yet')} /> : missions.map((m) => (
              <ListRow
                key={m.id}
                title={m.title}
                subtitle={[m.branchName, formatRelativeTime(m.completedUtc || m.createdUtc)].filter(Boolean).join(' \u00b7 ')}
                accessory={<StatusBadge label={m.status} tone={statusTone(m.status)} />}
                onPress={() => router.push(`/missions/${m.id}` as Href)}
              />
            ))}
          </Section>

          <View style={styles.inner}>
            <Disclosure title={t('View JSON')} testID="captain-json">
              <CodeBlock text={JSON.stringify(captain, null, 2)} />
            </Disclosure>
          </View>
        </View>
      </ScrollView>
      <CaptainFormSheet
        open={editing}
        captain={captain}
        onClose={() => setEditing(false)}
        onSaved={() => { setEditing(false); void resource.reload(); }}
      />
      {actions.dialog}
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  flex: { flex: 1 },
  content: { paddingVertical: spacing.lg, flexGrow: 1 },
  column: { width: '100%', maxWidth: 820, alignSelf: 'center' },
  header: { flexDirection: 'row', alignItems: 'center', flexWrap: 'wrap', gap: spacing.sm, paddingHorizontal: spacing.lg, marginBottom: spacing.md },
  inner: { padding: spacing.lg },
  sectionTitle: { marginHorizontal: spacing.lg, marginBottom: spacing.sm, textTransform: 'uppercase' },
});
