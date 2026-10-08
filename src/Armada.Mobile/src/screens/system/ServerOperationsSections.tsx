import { useRouter, type Href } from 'expo-router';
import { useCallback, useEffect, useRef, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import {
  downloadBackup,
  getHealth,
  getRebuildStatus,
  getVesselBranches,
  rebuildServer,
  resetServer,
  restartServer,
  restoreBackup,
  rollbackServer,
  stopServer,
  type BranchInfo,
  type RebuildStatus,
} from '@dashboard/api/client';
import {
  getMcpConfigHttp,
  getMcpConfigStdio,
  getMcpRpcUrl,
  getRemoteTunnelIndicator,
  healthStatusKey,
  isKnownHealthStatus,
  isRebuildTerminal,
  MCP_CLIENTS,
  type HealthInfo,
} from '@dashboard/lib/serverSettings';
import { Field, FieldCard } from '../../components/resource/DetailParts';
import { useConfirm } from '../../components/resource/ResourceRow';
import { StatRow } from '../../components/resource/ResourceList';
import { AppText } from '../../components/ui/AppText';
import { Disclosure } from '../../components/ui/Disclosure';
import { Banner } from '../../components/ui/Banner';
import { BottomSheet } from '../../components/ui/BottomSheet';
import { Button } from '../../components/ui/Button';
import { ConfirmDialog } from '../../components/ui/ConfirmDialog';
import { SelectField } from '../../components/ui/SelectSheet';
import { TextField } from '../../components/ui/TextField';
import { useAuth } from '../../auth/AuthContext';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { ensureNativePlatform, pickBackupFile, reauthenticateForExport, type PickedBackup } from '../../platform/files';
import { errorText } from '../../resource/useLoad';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing } from '../../theme/typography';
import type { MobileServerSettings } from './settingsModel';
import { SettingsSection } from './settingsParts';

/** Health, uptime, connection, and remote tunnel cards plus the server detail fields. */
export function ServerStatusSection({ health, settings, connected, serverUrl }: { health: HealthInfo | null; settings: MobileServerSettings | null; connected: boolean; serverUrl: string }) {
  const { t, formatDateTime } = useLocale();
  const abs = (utc: string | null | undefined) => formatDateTime(utc) || '-';
  const healthText = (status: string | null | undefined) => (isKnownHealthStatus(status) ? t(healthStatusKey(status)) : healthStatusKey(status));
  const tunnelEnabled = settings?.remoteControl.enabled ?? health?.remoteTunnel?.enabled ?? false;
  const tunnelState = t(getRemoteTunnelIndicator(tunnelEnabled, health?.remoteTunnel?.state).labelKey);
  return (
    <>
      <StatRow stats={[
        { label: t('Health'), value: health ? healthText(health.status) : t('Loading...') },
        { label: t('Uptime'), value: health?.uptime || '-' },
        { label: t('Connection'), value: t(connected ? 'Live (WebSocket)' : 'Online (HTTP)') },
        { label: t('Remote Tunnel'), value: tunnelState },
      ]} />
      <FieldCard>
        {health ? <Field label={t('Checked: {{timestamp}}', { timestamp: abs(health.timestamp) })} value={health.startUtc ? t('Started: {{timestamp}}', { timestamp: abs(health.startUtc) }) : '-'} /> : null}
        <Field label={t('Version')} value={health?.version || '-'} mono />
        <Field label={t('API URL')} value={serverUrl} mono />
        <Field label={t('Admiral Port')} value={String(health?.ports?.admiral || '-')} mono />
        <Field label={t('MCP Port')} value={String(health?.ports?.mcp || '-')} mono />
        <Field label={t('WebSocket Port')} value={String(health?.ports?.webSocket || '-')} mono />
        <Field label={t('Tunnel State')} value={tunnelState} />
        <Field label={t('Remote Tunnel')} value={health?.remoteTunnel?.tunnelUrl || t('No tunnel URL configured')} mono />
        <Field label={t('Tunnel Instance')} value={health?.remoteTunnel?.instanceId || '-'} mono />
        <Field label={t('Tunnel Latency')} value={health?.remoteTunnel?.latencyMs != null ? `${health.remoteTunnel.latencyMs.toLocaleString()} ms` : '-'} />
        <Field label={t('Tunnel Last Heartbeat')} value={health?.remoteTunnel?.lastHeartbeatUtc ? abs(health.remoteTunnel.lastHeartbeatUtc) : '-'} />
      </FieldCard>
    </>
  );
}

function CodeBlock({ title, text }: { title: string; text: string }) {
  const { colors } = useTheme();
  return (
    <View style={styles.code}>
      <AppText variant="caption" muted>{title}</AppText>
      <View style={[styles.codeBox, { backgroundColor: colors.background, borderColor: colors.border }]}>
        <AppText variant="mono" selectable>{text}</AppText>
      </View>
    </View>
  );
}

/** MCP Configuration: per-client HTTP and STDIO snippets (selectable to copy). Not valid through Armada.Proxy. */
export function McpSection({ health, settings, proxyMode }: { health: HealthInfo | null; settings: MobileServerSettings; proxyMode: boolean }) {
  const { t } = useLocale();
  const rpcUrl = getMcpRpcUrl(health?.ports?.mcp, settings.mcpPort);
  return (
    <SettingsSection title={t('MCP Configuration')} description={t('Client-specific MCP references for Claude, Codex, Gemini, and Cursor.')}>
      {proxyMode ? (
        <Banner tone="warning" title={t('MCP bootstrap commands are only valid when connected directly to an Armada server origin. Armada.Proxy does not relay the MCP endpoint.')} />
      ) : MCP_CLIENTS.map((client) => (
        // Collapsed per client: four clients' snippets would otherwise fill several phone screens.
        <View key={client.key} style={styles.client}>
          <Disclosure title={client.title} testID={`settings-mcp-${client.key}`}>
            <AppText variant="mono" muted selectable>{client.location}</AppText>
            <CodeBlock title="HTTP" text={getMcpConfigHttp(client.key, rpcUrl)} />
            <CodeBlock title="STDIO" text={getMcpConfigStdio(client.key)} />
          </Disclosure>
        </View>
      ))}
    </SettingsSection>
  );
}

/** System Paths (selectable to copy). */
export function PathsSection({ settings }: { settings: MobileServerSettings }) {
  const { t } = useLocale();
  return (
    <FieldCard title={t('System Paths')}>
      <Field label={t('Data Directory')} value={settings.dataDirectory || '-'} mono />
      <Field label={t('Database Path')} value={settings.databasePath || '-'} mono />
      <Field label={t('Log Directory')} value={settings.logDirectory || '-'} mono />
      <Field label={t('Docks Directory')} value={settings.docksDirectory || '-'} mono />
      <Field label={t('Repos Directory')} value={settings.reposDirectory || '-'} mono />
    </FieldCard>
  );
}

/**
 * Database Backup (global admins, as the server enforces). A backup is the whole database and settings, secrets
 * included, so (security review F-56) Backup Now first warns that the share target receives them, then asks the
 * device owner to re-authenticate (biometrics or passcode), then shares the ZIP and deletes it from the cache when
 * the share sheet closes. Restore picks a ZIP with the document picker and needs the word `restore` typed into a
 * confirmation that names the server; the picker's copy is deleted afterwards.
 */
export function BackupSection({ proxyMode, onRestored }: { proxyMode: boolean; onRestored: () => void }) {
  const { t } = useLocale();
  const { pushToast } = useNotifications();
  const { activeProfile } = useAuth();
  const [backingUp, setBackingUp] = useState(false);
  const [restoring, setRestoring] = useState(false);
  const [exportWarning, setExportWarning] = useState(false);
  const [pending, setPending] = useState<PickedBackup | null>(null);
  const serverName = activeProfile ? `${activeProfile.name} (${activeProfile.url})` : t('this server');

  async function backup() {
    setExportWarning(false);
    const verified = await reauthenticateForExport(t('Confirm it is you to export the Armada backup'), t('Cancel'));
    if (!verified) {
      pushToast('warning', t('Backup cancelled: the device owner was not verified.'));
      return;
    }
    setBackingUp(true);
    try {
      ensureNativePlatform();
      await downloadBackup();
      pushToast('success', t('Backup downloaded'));
    } catch (e: unknown) {
      pushToast('error', t('Backup failed: {{message}}', { message: errorText(e, t('Unknown error')) }));
    } finally {
      setBackingUp(false);
    }
  }

  async function pick() {
    let picked: PickedBackup | null;
    try {
      picked = await pickBackupFile();
    } catch (e: unknown) {
      pushToast('error', t('Restore failed: {{message}}', { message: errorText(e, t('Unknown error')) }));
      return;
    }
    if (picked) setPending(picked);
  }

  function cancelRestore() {
    pending?.dispose();
    setPending(null);
  }

  async function restore(picked: PickedBackup) {
    setPending(null);
    setRestoring(true);
    try {
      await restoreBackup(picked.file);
      pushToast('success', t('Restore completed successfully. Server restart recommended.'));
      onRestored();
    } catch (e: unknown) {
      pushToast('error', t('Restore failed: {{message}}', { message: errorText(e, t('Unknown error')) }));
    } finally {
      picked.dispose();
      setRestoring(false);
    }
  }

  return (
    <SettingsSection title={t('Database Backup')}>
      {proxyMode ? <Banner tone="warning" title={t('Backup download remains available through the proxy relay, but restore is blocked remotely by proxy policy.')} /> : null}
      <View style={styles.row}>
        <Button label={backingUp ? t('Backing up...') : t('Backup Now')} icon="share-outline" busy={backingUp} onPress={() => setExportWarning(true)} accessibilityHint={t('Create a backup ZIP of the database and download it')} style={styles.button} testID="settings-backup" />
        <Button
          label={t('Restore from Backup')}
          variant="danger"
          busy={restoring}
          disabled={proxyMode}
          onPress={() => void pick()}
          accessibilityHint={proxyMode ? t('Restore from Backup is blocked in proxy mode') : t('Restore the database from a backup ZIP file')}
          style={styles.button}
          testID="settings-restore"
        />
      </View>
      <ConfirmDialog
        open={exportWarning}
        title={t('Backup Now')}
        message={t('The backup contains the whole database and settings, including password hashes, credential tokens, push tokens, Ask history, and server secrets. Whatever you share it with (an app, a cloud folder, a person) receives all of that. You will be asked to confirm it is you.')}
        confirmLabel={t('Continue')}
        cancelLabel={t('Cancel')}
        onConfirm={() => void backup()}
        onCancel={() => setExportWarning(false)}
        testID="settings-backup-confirm"
      />
      <ConfirmDialog
        open={pending !== null}
        title={t('Restore from Backup')}
        message={t('Restore the database of {{server}} from "{{name}}"? The current data is replaced by the backup.', { server: serverName, name: pending?.file.name ?? '' })}
        confirmLabel={t('Restore from Backup')}
        cancelLabel={t('Cancel')}
        danger
        typedConfirmation="restore"
        typedLabel={t('Type `restore` into the confirmation box to continue.')}
        onConfirm={() => { if (pending) void restore(pending); }}
        onCancel={cancelRestore}
        testID="settings-restore-confirm"
      />
    </SettingsSection>
  );
}

/** Server Actions (admins): setup wizard, health check, restart, rebuild with log and rollback, stop, factory reset. */
export function ServerActionsSection({ proxyMode, selfVesselId, onHealth }: { proxyMode: boolean; selfVesselId: string | null; onHealth: (h: HealthInfo) => void }) {
  const { t } = useLocale();
  const router = useRouter();
  const { pushToast } = useNotifications();
  const { confirm, dialog } = useConfirm('settings-action-confirm');
  const [branches, setBranches] = useState<BranchInfo[]>([]);
  const [buildRef, setBuildRef] = useState('');
  const [rebuild, setRebuild] = useState<RebuildStatus | null>(null);
  const [logOpen, setLogOpen] = useState(false);
  const pollRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const stopPoll = useCallback(() => {
    if (pollRef.current !== null) {
      clearInterval(pollRef.current);
      pollRef.current = null;
    }
  }, []);
  useEffect(() => stopPoll, [stopPoll]);

  const refreshRebuild = useCallback(async () => {
    try {
      const s = await getRebuildStatus();
      setRebuild(s);
      // Once the server cuts over it stops responding; stop polling and let the health check show the new build.
      if (isRebuildTerminal(s) || s.status === 'CuttingOver') stopPoll();
    } catch {
      stopPoll();
    }
  }, [stopPoll]);

  // The last rebuild (so Build Log and Roll Back are offered after reopening the screen).
  useEffect(() => { void getRebuildStatus().then(setRebuild).catch(() => undefined); }, []);

  useEffect(() => {
    let cancelled = false;
    if (!selfVesselId) return undefined;
    getVesselBranches(selfVesselId)
      .then((r) => {
        if (cancelled) return;
        setBranches(r.branches || []);
        const preferred = r.defaultBranch || r.branches?.find((b) => b.isDefault)?.name;
        if (preferred) setBuildRef((current) => current || preferred);
      })
      .catch(() => { if (!cancelled) setBranches([]); });
    return () => { cancelled = true; };
  }, [selfVesselId]);

  const failed = (key: string) => (e: unknown) => pushToast('error', t(key, { message: errorText(e, t('Unknown error')) }));

  async function healthCheck() {
    try {
      const result = (await getHealth()) as unknown as HealthInfo;
      onHealth(result);
      const status = isKnownHealthStatus(result.status) ? t(healthStatusKey(result.status)) : healthStatusKey(result.status);
      pushToast('info', t('Health: {{status}} | Uptime: {{uptime}}', { status, uptime: result.uptime }));
    } catch (e: unknown) {
      failed('Health check failed: {{message}}')(e);
    }
  }

  function restart() {
    confirm({
      title: t('Restart Server'),
      message: t('Restart the Admiral server? A replacement process starts and this instance shuts down; the dashboard will be briefly unavailable while it comes back up.'),
      confirmLabel: t('Yes'),
      onConfirm: async () => {
        try { await restartServer(); pushToast('warning', t('Server restarting... the dashboard will reconnect shortly.')); } catch (e: unknown) { failed('Failed: {{message}}')(e); }
      },
    });
  }

  function stop() {
    confirm({
      title: t('Stop Server'),
      message: t('Stop the Admiral server? This will shut down everything.'),
      confirmLabel: t('Yes'),
      onConfirm: async () => {
        try { await stopServer(); pushToast('warning', t('Server shutting down...')); } catch (e: unknown) { failed('Failed: {{message}}')(e); }
      },
    });
  }

  function factoryReset() {
    confirm({
      title: t('Factory Reset'),
      message: t('WARNING: Factory reset will delete ALL data including the database, logs, docks, and repos. Settings will be preserved. This cannot be undone. Continue?'),
      confirmLabel: t('Yes'),
      danger: true,
      onConfirm: async () => {
        try { await resetServer(); pushToast('success', t('Factory reset complete')); } catch (e: unknown) { failed('Factory reset failed: {{message}}')(e); }
      },
    });
  }

  function startRebuild() {
    const ref = buildRef.trim();
    confirm({
      title: t('Rebuild Armada'),
      message: t('Rebuild the Admiral from source at {{ref}}? The new build is published into a fresh slot while this instance keeps running; on success the database is backed up and the server cuts over to the new build. A failed build will not disturb the running server.', { ref: ref || t('current HEAD') }),
      confirmLabel: t('Yes'),
      onConfirm: async () => {
        try {
          const started = await rebuildServer(ref ? { Ref: ref } : {});
          setRebuild(started);
          setLogOpen(true);
          pushToast('warning', t('Rebuild started; building slot {{slot}}...', { slot: started.slot || t('(pending)') }));
          stopPoll();
          pollRef.current = setInterval(() => { void refreshRebuild(); }, 1500);
        } catch (e: unknown) {
          failed('Rebuild failed to start: {{message}}')(e);
        }
      },
    });
  }

  function rollback() {
    confirm({
      title: t('Roll Back Rebuild'),
      message: t('Roll back to the previous build ({{slot}})? If the last rebuild changed the database schema, the pre-rebuild backup is restored and any data written since the cutover is permanently lost. The server then restarts on the previous build.', { slot: rebuild?.previousSlot || t('previous slot') }),
      confirmLabel: t('Yes'),
      danger: true,
      onConfirm: async () => {
        try {
          const s = await rollbackServer();
          setRebuild(s);
          pushToast('warning', t('Rolling back to {{slot}}; the dashboard will reconnect shortly.', { slot: s.previousSlot || t('previous slot') }));
        } catch (e: unknown) {
          failed('Rollback failed: {{message}}')(e);
        }
      },
    });
  }

  const blocked = proxyMode;
  return (
    <SettingsSection title={t('Server Actions')} testID="settings-actions">
      {proxyMode ? <Banner tone="warning" title={t('Setup, shutdown, and factory reset are local-only server actions and are disabled when this dashboard is opened through Armada.Proxy.')} /> : null}
      <View style={styles.row}>
        <Button label={t('Setup Wizard')} variant="secondary" disabled={blocked} onPress={() => router.push('/setup' as Href)} accessibilityHint={blocked ? t('Setup Wizard is only available when connected directly to Armada.Server') : t('Re-open the first-run setup guide')} style={styles.button} testID="settings-open-setup" />
        <Button label={t('Health Check')} variant="secondary" onPress={() => void healthCheck()} accessibilityHint={t('Run a health check and display the result')} style={styles.button} testID="settings-health-check" />
        <Button label={t('Restart Server')} variant="secondary" disabled={blocked} onPress={restart} style={styles.button} />
      </View>
      {branches.length > 0 ? (
        <SelectField
          label={t('Branch to build. Choose a branch or type a tag/commit in the box.')}
          value={branches.some((b) => b.name === buildRef) ? buildRef : ''}
          options={[{ value: '', label: t('current HEAD') }, ...branches.map((b) => ({ value: b.name, label: `${b.name}${b.isDefault ? t(' (default)') : ''}` }))]}
          onChange={setBuildRef}
          closeLabel={t('Close')}
          disabled={blocked}
          testID="settings-rebuild-branch"
        />
      ) : null}
      <TextField
        label={t('Branch, tag, or commit to build. Leave blank to build the current HEAD.')}
        value={buildRef}
        onChangeText={setBuildRef}
        placeholder={t('ref (blank = HEAD)')}
        autoCapitalize="none"
        autoCorrect={false}
        editable={!blocked}
        testID="settings-rebuild-ref"
      />
      <View style={styles.row}>
        <Button label={t('Rebuild Armada')} variant="secondary" disabled={blocked} onPress={startRebuild} style={styles.button} testID="settings-rebuild" />
        {rebuild && rebuild.status != null ? <Button label={t('Build Log')} variant="ghost" onPress={() => { setLogOpen(true); void refreshRebuild(); }} style={styles.button} testID="settings-build-log" /> : null}
        {rebuild && rebuild.status === 'Succeeded' && rebuild.previousSlot ? <Button label={t('Roll Back')} variant="danger" disabled={blocked} onPress={rollback} style={styles.button} testID="settings-rollback" /> : null}
      </View>
      <View style={styles.row}>
        <Button label={t('Stop Server')} variant="danger" disabled={blocked} onPress={stop} style={styles.button} testID="settings-stop" />
        <Button label={t('Factory Reset')} variant="danger" disabled={blocked} onPress={factoryReset} style={styles.button} testID="settings-factory-reset" />
      </View>
      <BottomSheet open={logOpen} title={t('Rebuild Armada{{slot}}', { slot: rebuild?.slot ? ' - ' + rebuild.slot : '' })} onClose={() => setLogOpen(false)} closeLabel={t('Close')} testID="settings-rebuild-log">
        {rebuild?.status ? <AppText variant="label">{t(rebuild.status)}</AppText> : null}
        {rebuild?.error ? <Banner tone="danger" title={rebuild.error} /> : null}
        <AppText variant="mono" selectable>{rebuild?.log || ''}</AppText>
        {!(isRebuildTerminal(rebuild) || rebuild?.status === 'CuttingOver') ? <Button label={t('Refresh')} variant="ghost" onPress={() => void refreshRebuild()} /> : null}
      </BottomSheet>
      {dialog}
    </SettingsSection>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm, marginBottom: spacing.md },
  button: { marginBottom: 0 },
  client: { marginBottom: spacing.lg, gap: spacing.xs },
  code: { gap: spacing.xs },
  codeBox: { borderWidth: StyleSheet.hairlineWidth, borderRadius: radius.sm, padding: spacing.sm },
});
