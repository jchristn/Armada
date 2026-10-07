import { useRouter, type Href } from 'expo-router';
import { useCallback, useState } from 'react';
import { getHealth, getSettings, listCaptains, listFleets, listVessels } from '@dashboard/api/client';
import { mergeServerSettings, type HealthInfo, type ServerSettings } from '@dashboard/lib/serverSettings';
import { useAuth } from '../../auth/AuthContext';
import { DetailBody, DetailPending } from '../../components/resource/DetailParts';
import { Banner } from '../../components/ui/Banner';
import { Button } from '../../components/ui/Button';
import { useLocale } from '../../i18n/LocaleContext';
import { ALL, useVessels } from '../../resource/lookups';
import { useLoad, useReloadOnFocus } from '../../resource/useLoad';
import { useSocket } from '../../socket/SocketContext';
import { resourceStyles } from '../../components/resource/styles';
import { AgentSettingsSection, PlanningSessionSection, RebuildSettingsSection, ServerConfigSection } from './ServerConfigSections';
import { BackupSection, McpSection, PathsSection, ServerActionsSection, ServerStatusSection } from './ServerOperationsSections';
import { CliPermissionSettingsSection, FleetActionSettingsSection, ImportSettingsSection, RepositoryHealthSettingsSection, RetentionSettingsSection } from './ServerPolicySections';
import { PushSettingsSection, RemoteControlSection } from './ServerSecretSections';
import type { MobileServerSettings } from './settingsModel';

interface ServerData {
  health: HealthInfo | null;
  settings: MobileServerSettings | null;
}

/** True when the deployment is missing a fleet, a vessel, or a captain (the dashboard then offers the setup wizard). */
async function setupIncomplete(): Promise<boolean> {
  try {
    const [f, v, c] = await Promise.all([listFleets(ALL), listVessels(ALL), listCaptains(ALL)]);
    return (f.objects ?? []).length === 0 || (v.objects ?? []).length === 0 || (c.objects ?? []).length === 0;
  } catch {
    return false;
  }
}

/**
 * Settings > Server (the dashboard's Server page): health and ports, server configuration, rebuild settings, agent
 * settings with the Default Landing Mode, planning sessions, repository health, vessel import, fleet actions, CLI
 * tool permissions (admins), retention, remote control, push notifications, MCP snippets, system paths, backup and
 * restore, and server actions (admins). Each section saves on its own. Through Armada.Proxy the local settings and
 * actions are locked, as on the dashboard.
 */
export function ServerSettingsTab() {
  const { t } = useLocale();
  const router = useRouter();
  const { isAdmin, activeProfile } = useAuth();
  const { connected } = useSocket();
  const vessels = useVessels();
  const [incomplete, setIncomplete] = useState(false);

  const { data, loading, refreshing, error, reload, refresh, setData } = useLoad<ServerData>(async () => {
    const [h, s] = await Promise.all([getHealth().catch(() => null), getSettings().catch(() => null)]);
    void setupIncomplete().then(setIncomplete);
    if (!h && !s) throw new Error(t('Failed to load server data.'));
    return {
      health: (h as unknown as HealthInfo | null) ?? null,
      settings: s ? (mergeServerSettings(s as unknown as ServerSettings) as MobileServerSettings) : null,
    };
  }, [], { fallbackError: t('Failed to load server data.') });
  useReloadOnFocus(reload);

  const onSaved = useCallback((settings: MobileServerSettings) => {
    setData(data ? { ...data, settings } : { health: null, settings });
  }, [data, setData]);
  const onHealth = useCallback((health: HealthInfo) => {
    setData(data ? { ...data, health } : { health, settings: null });
  }, [data, setData]);

  if (!data) return <DetailPending loading={loading} error={error} onRetry={() => void reload()} />;
  const { health, settings } = data;
  const proxyMode = activeProfile?.kind === 'Proxy';
  const instanceId = activeProfile?.proxyInstanceId || '';
  const section = settings ? { settings, locked: proxyMode, onSaved } : null;

  return (
    <DetailBody embedded refreshing={refreshing} onRefresh={() => void refresh()} testID="settings-server">
      {proxyMode ? (
        <Banner tone="warning" title={t('This page is connected through Armada.Proxy for {{instanceId}}. Local server settings, tunnel settings, setup, restore, shutdown, and factory reset are blocked in remote mode.', { instanceId: instanceId || t('the selected deployment') })} />
      ) : null}
      {!settings ? <Banner tone="danger" title={t('Failed to load server settings. Health data is available, but configuration and backup sections could not be loaded.')} /> : null}
      {incomplete && isAdmin && !proxyMode ? (
        <>
          <Banner tone="info" title={t('Setup Wizard')} message={t('Configure Armada to dispatch one safe first mission.')} />
          <Button label={t('Setup Wizard')} icon="rocket-outline" onPress={() => router.push('/setup' as Href)} style={resourceStyles.create} testID="settings-setup-banner" />
        </>
      ) : null}
      <ServerStatusSection health={health} settings={settings} connected={connected} serverUrl={activeProfile?.url ?? '-'} />
      {section ? (
        <>
          <ServerConfigSection {...section} />
          <RebuildSettingsSection {...section} vessels={vessels} />
          <AgentSettingsSection {...section} />
          <PlanningSessionSection {...section} />
          <RepositoryHealthSettingsSection {...section} isAdmin={isAdmin} />
          <ImportSettingsSection {...section} />
          <FleetActionSettingsSection {...section} />
          {isAdmin ? <CliPermissionSettingsSection {...section} /> : null}
          <RetentionSettingsSection {...section} />
          <RemoteControlSection {...section} health={health} onHealthRefresh={() => { void getHealth().then((h) => onHealth(h as unknown as HealthInfo)).catch(() => undefined); }} />
          <PushSettingsSection {...section} />
          <McpSection health={health} settings={settings!} proxyMode={proxyMode} />
          <PathsSection settings={settings!} />
          {isAdmin ? <BackupSection proxyMode={proxyMode} onRestored={() => void reload()} /> : null}
        </>
      ) : null}
      {isAdmin ? <ServerActionsSection proxyMode={proxyMode} selfVesselId={settings?.selfVesselId ?? null} onHealth={onHealth} /> : null}
    </DetailBody>
  );
}
