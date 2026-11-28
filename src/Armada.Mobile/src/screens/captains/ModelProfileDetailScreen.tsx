import { Stack, useRouter } from 'expo-router';
import { useState } from 'react';
import { RefreshControl, StyleSheet, View } from 'react-native';
import { getModelProfile } from '@dashboard/api/client';
import { RECOVERY_FIELDS, TIMEOUT_FIELDS, capabilityLabel, servedContextLabel } from '@dashboard/lib/modelProfiles';
import type { ModelProfile, RecoveryPolicy, TimeoutPolicy, TimeoutSource } from '@dashboard/types/models';
import { JsonSheet } from '../../components/app/JsonSheet';
import { AppText, Button, KeyValueRow, Screen, Section, StatusBadge } from '../../components/ui';
import { useQuery } from '../../data/useQuery';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { spacing } from '../../theme/typography';
import { DetailActions, DetailState, useWhen } from '../operations/w24/DetailParts';
import { modelProfileName } from './ModelProfilesTab';

type TimeoutKey = (typeof TIMEOUT_FIELDS)[number]['key'];

const TIMEOUT_SOURCE_KEYS: Record<TimeoutKey, keyof TimeoutPolicy> = {
  stallThresholdMinutes: 'stallThresholdSource',
  firstOutputMinutes: 'firstOutputSource',
  toolWaitHardLimitMinutes: 'toolWaitHardLimitSource',
  maxRuntimeMinutes: 'maxRuntimeSource',
};

/** Pure: "value in effect (override N)" for a recovery field, the effective value alone when there is no override. */
export function recoveryValue(profile: ModelProfile, key: (typeof RECOVERY_FIELDS)[number]['key'], t: (k: string, v?: Record<string, string | number>) => string): string {
  const effective = profile.effectiveRecovery ? (profile.effectiveRecovery as RecoveryPolicy)[key] : null;
  const override = profile.recovery?.[key];
  const shown = effective ?? override;
  if (shown == null) return t('Global setting');
  return override != null ? t('{{value}} (override)', { value: shown }) : String(shown);
}

/** Pure: "value (source)" for a timeout field, from the timeouts in effect, else the override, else the global setting. */
export function timeoutValue(profile: ModelProfile, key: TimeoutKey, t: (k: string, v?: Record<string, string | number>) => string): string {
  const effective = profile.effectiveTimeouts;
  if (effective) {
    const source = effective[TIMEOUT_SOURCE_KEYS[key]] as TimeoutSource;
    return `${effective[key]} (${t(source)})`;
  }
  const override = profile.timeouts?.[key];
  return override != null ? t('{{value}} (override)', { value: override }) : t('Global setting');
}

/**
 * A model profile, read-only (the dashboard's ModelProfileDetail; editing stays on the dashboard): identity and served
 * context with its source, configured versus probed capabilities, tier and the outcome suggestion, rendering, the
 * recovery policy and timeouts in effect, and measured speed.
 */
export function ModelProfileDetailScreen({ id, embedded }: { id: string; embedded?: boolean }) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const router = useRouter();
  const when = useWhen();
  const [json, setJson] = useState(false);
  const query = useQuery(() => getModelProfile(id), [id], t('Failed to load model profile.'));
  const profile = query.data;
  const title = t('Model Profile');
  if (!profile) {
    return (
      <View style={styles.fill} testID="model-profile-detail">
        {embedded ? null : <Stack.Screen options={{ title }} />}
        <DetailState loading={query.loading} error={query.error} missing={!query.loading} missingTitle={t('Model profile not found.')}
          onRetry={() => void query.refresh()} onBack={embedded ? undefined : () => router.back()} backLabel={t('Back')} />
      </View>
    );
  }
  const tiering = profile.tiering;
  return (
    <Screen testID="model-profile-detail" refreshControl={<RefreshControl refreshing={query.refreshing} onRefresh={() => void query.refresh()} tintColor={colors.primary} />}>
      {embedded ? null : <Stack.Screen options={{ title }} />}
      <View style={styles.head}>
        <AppText variant="heading" accessibilityRole="header" selectable>{modelProfileName(profile)}</AppText>
        {tiering?.tier ? <StatusBadge label={t(tiering.tier)} tone="info" /> : null}
      </View>
      <DetailActions>
        <Button label={t('View JSON')} variant="secondary" icon="code-outline" onPress={() => setJson(true)} testID="model-profile-json" />
      </DetailActions>
      <Section>
        <KeyValueRow label={t('ID')} value={profile.id} mono testID="model-profile-id" />
        <KeyValueRow label={t('Runtime')} value={profile.identity.runtime} />
        <KeyValueRow label={t('Provider')} value={profile.identity.provider} />
        <KeyValueRow label={t('Model')} value={profile.identity.model} mono />
        <KeyValueRow label={t('Served context')} value={servedContextLabel(profile.identity, t)} testID="model-profile-context" />
        <KeyValueRow label={t('Tools')} value={capabilityLabel(profile.capabilities.toolsAvailable, profile.capabilities.probedToolsAvailable, t)} />
        <KeyValueRow label={t('MCP')} value={capabilityLabel(profile.capabilities.mcpAvailable, profile.capabilities.probedMcpAvailable, t)} />
        <KeyValueRow label={t('Last Updated')} value={when(profile.lastUpdateUtc)} />
      </Section>
      <Section title={t('Tier')} footer={tiering?.suggestedReason ?? undefined}>
        <KeyValueRow label={t('Tier')} value={tiering?.tier ? t(tiering.tier) : t('Not set')} testID="model-profile-tier" />
        <KeyValueRow label={t('Suggested tier')} value={tiering?.suggestedTier
          ? t('{{tier}} from {{count}} attempts', { tier: t(tiering.suggestedTier), count: tiering.suggestedSamples ?? 0 })
          : t('None')} testID="model-profile-suggested-tier" />
      </Section>
      <Section title={t('Recovery Policy')} footer={t('Values in effect: the global settings with this profile\'s overrides applied. Edit them on the dashboard.')}>
        <View testID="model-profile-recovery">
          {RECOVERY_FIELDS.map(f => <KeyValueRow key={f.key} label={t(f.label)} value={recoveryValue(profile, f.key, t)} testID={`model-profile-recovery-${f.key}`} />)}
        </View>
      </Section>
      <Section title={t('Timeouts')}>
        <View testID="model-profile-timeouts">
          {TIMEOUT_FIELDS.map(f => <KeyValueRow key={f.key} label={t(f.label)} value={timeoutValue(profile, f.key, t)} testID={`model-profile-timeout-${f.key}`} />)}
        </View>
      </Section>
      <Section title={t('Rendering')}>
        <KeyValueRow label={t('Prompt variant')} value={profile.rendering?.template ?? t('Automatic')} />
        <KeyValueRow label={t('Repeat contract at end')} value={profile.rendering?.repeatContractAtEnd == null ? t('Automatic') : profile.rendering.repeatContractAtEnd ? t('Yes') : t('No')} />
        <KeyValueRow label={t('Files per mission')} value={profile.rendering?.maxFilesPerMission ?? t('No limit')} />
      </Section>
      {profile.speed?.tokensPerSecond != null ? (
        <Section title={t('Measured Speed')}>
          <KeyValueRow label={t('Tokens per second')} value={profile.speed.tokensPerSecond} />
          <KeyValueRow label={t('First output (ms)')} value={profile.speed.firstOutputMs} />
          <KeyValueRow label={t('Measured')} value={profile.speed.measuredUtc ? when(profile.speed.measuredUtc) : null} />
        </Section>
      ) : null}
      <JsonSheet open={json} title={`${t('Model Profile')}: ${profile.id}`} data={profile} onClose={() => setJson(false)} />
    </Screen>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  head: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, flexWrap: 'wrap', paddingHorizontal: spacing.lg, paddingTop: spacing.md },
});
