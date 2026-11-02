import { useEffect, useState, useCallback, useRef } from 'react';
import {
  getHealth,
  getSettings,
  updateSettings,
  stopServer,
  restartServer,
  resetServer,
  rebuildServer,
  getRebuildStatus,
  rollbackServer,
  getVesselBranches,
  listVessels,
  downloadBackup,
  restoreBackup,
  getProxySessionContext,
  type ProxySessionContext,
  type RebuildStatus,
  type BranchInfo,
} from '../api/client';
import { listAll } from '../api/paging';
import type { Vessel } from '../types/models';
import ImportFleetActionSettings from '../components/settings/ImportFleetActionSettings';
import RetentionSettings from '../components/settings/RetentionSettings';
import AskSettings from '../components/settings/AskSettings';
import BlackBoxSettings from '../components/settings/BlackBoxSettings';
import CliPermissionSettings from '../components/settings/CliPermissionSettings';
import DefaultLandingModeField from '../components/settings/DefaultLandingModeField';
import { DEFAULT_GLOBAL_LANDING_MODE } from '../lib/vesselForm';
import {
  DEFAULT_REMOTE_TUNNEL_URL,
  MCP_CLIENTS,
  getMcpConfigHttp as buildMcpConfigHttp,
  getMcpConfigStdio,
  getMcpRpcUrl as buildMcpRpcUrl,
  getRemoteTunnelIndicator,
  healthStatusKey,
  isKnownHealthStatus,
  isRebuildTerminal,
  mergeServerSettings,
  REQUIRE_HARBOR_FOR_LAUNCH_TOOLTIP,
  type HealthInfo,
  type McpClientKey,
  type ServerSettings,
} from '../lib/serverSettings';

type RemoteSecretField = 'enrollmentToken' | 'password';
import LogViewer from '../components/shared/LogViewer';
import RefreshButton from '../components/shared/RefreshButton';
import AutoRefreshSelect from '../components/shared/AutoRefreshSelect';
import { baseAfterSave, mergeDraft } from '../lib/settingsDraft';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import PageHeader from '../components/shared/PageHeader';
import { useWebSocket } from '../context/WebSocketContext';
import { useNotifications, type Severity } from '../context/NotificationContext';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import CopyButton, { copyToClipboard } from '../components/shared/CopyButton';
import { useAuth } from '../context/AuthContext';
import { useLocale } from '../context/LocaleContext';
import ErrorModal from '../components/shared/ErrorModal';
import RepositoryHealthSettingsSection from '../components/vessels/health/RepositoryHealthSettingsSection';

export default function Server() {
  const { isAdmin } = useAuth();
  const { connected } = useWebSocket();
  const { pushToast } = useNotifications();
  const { t, formatDateTime } = useLocale();

  const [health, setHealth] = useState<HealthInfo | null>(null);
  const [settings, setSettings] = useState<ServerSettings | null>(null);
  // Server copy the form was last merged against. Edited fields differ from this copy and are kept.
  const savedSettingsRef = useRef<ServerSettings | null>(null);
  const [proxyContext, setProxyContext] = useState<ProxySessionContext | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [backupLoading, setBackupLoading] = useState(false);
  const [revealedRemoteField, setRevealedRemoteField] = useState<RemoteSecretField | null>(null);
  const [confirmDialog, setConfirmDialog] = useState<{
    open: boolean;
    title?: string;
    message: string;
    onConfirm: () => void;
  }>({ open: false, message: '', onConfirm: () => {} });

  const restoreFileRef = useRef<HTMLInputElement>(null);

  const [buildRef, setBuildRef] = useState('');
  const [branches, setBranches] = useState<BranchInfo[]>([]);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [rebuildLogOpen, setRebuildLogOpen] = useState(false);
  const [rebuild, setRebuild] = useState<RebuildStatus | null>(null);
  const rebuildPollRef = useRef<number | null>(null);

  const rebuildTerminal = isRebuildTerminal;

  const showToast = useCallback((severity: Severity, msg: string) => {
    pushToast(severity, msg);
  }, [pushToast]);

  const stopRebuildPoll = useCallback(() => {
    if (rebuildPollRef.current !== null) {
      window.clearInterval(rebuildPollRef.current);
      rebuildPollRef.current = null;
    }
  }, []);

  const refreshRebuildStatus = useCallback(async () => {
    try {
      const s = await getRebuildStatus();
      setRebuild(s);
      // Once the server begins cutting over it stops responding; stop polling and let the health check
      // surface when the new build is back up.
      if (rebuildTerminal(s) || s.status === 'CuttingOver') stopRebuildPoll();
    } catch {
      // Server may be down mid-cutover; keep the last-known log and stop polling.
      stopRebuildPoll();
    }
  }, [stopRebuildPoll]);

  useEffect(() => stopRebuildPoll, [stopRebuildPoll]);

  // Load vessels so the Armada source vessel can be picked from a dropdown.
  useEffect(() => {
    let cancelled = false;
    listAll(listVessels)
      .then((r) => { if (!cancelled) setVessels(r.objects || []); })
      .catch(() => { if (!cancelled) setVessels([]); });
    return () => { cancelled = true; };
  }, []);

  // Load the branches of the Armada source vessel so the rebuild ref can be picked from a dropdown.
  const selfVesselId = settings?.selfVesselId ?? null;
  useEffect(() => {
    if (!selfVesselId) { setBranches([]); return; }
    let cancelled = false;
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

  const closeConfirmDialog = useCallback(() => {
    setConfirmDialog({ open: false, message: '', onConfirm: () => {} });
  }, []);

  const formatAbsoluteTime = useCallback((utc: string | null | undefined) => {
    const formatted = formatDateTime(utc);
    return formatted || '-';
  }, [formatDateTime]);

  const translateHealthStatus = useCallback((status: string | null | undefined) => {
    // Known statuses are catalog keys; anything else is shown as the server sent it.
    const key = healthStatusKey(status);
    return isKnownHealthStatus(status) ? t(key) : key;
  }, [t]);

  const translateRemoteTunnelState = useCallback((enabled: boolean, state: string | null | undefined) => {
    return t(getRemoteTunnelIndicator(enabled, state).labelKey);
  }, [t]);

  function beginRevealRemoteField(field: RemoteSecretField) {
    setRevealedRemoteField(field);
  }

  function endRevealRemoteField() {
    setRevealedRemoteField(null);
  }

  const applyIncomingSettings = useCallback((data: ServerSettings, sent?: Partial<ServerSettings>, replaceDraft = false) => {
    const next = mergeServerSettings(data);
    if (!next) return;
    const previous = savedSettingsRef.current;
    if (replaceDraft || previous === null) {
      savedSettingsRef.current = next;
      setSettings(next);
      return;
    }
    const base = sent ? baseAfterSave(previous, sent) : previous;
    savedSettingsRef.current = next;
    setSettings((current) => (current ? mergeDraft(base, current, next) : next));
  }, []);

  const loadData = useCallback(async (replaceDraft = false) => {
    try {
      setError('');
      const [h, s, proxy] = await Promise.all([
        getHealth().catch(() => null),
        getSettings().catch(() => null),
        getProxySessionContext().catch(() => null),
      ]);
      if (h) setHealth(h as unknown as HealthInfo);
      if (s) applyIncomingSettings(s as unknown as ServerSettings, undefined, replaceDraft);
      setProxyContext(proxy);
      if (!h && !s) setError(t('Failed to load server data.'));
      else if (!s) setError(t('Failed to load server settings. Health data is available, but configuration and backup sections could not be loaded.'));
    } catch {
      setError(t('Failed to load server data.'));
    } finally {
      setLoading(false);
    }
  }, [t, applyIncomingSettings]);

  useEffect(() => {
    void loadData(true);
  }, [loadData]);

  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('server', () => { void loadData(false); });

  const handleSaveServerConfig = async () => {
    if (!settings) return;
    try {
      const sent = {
        admiralPort: settings.admiralPort,
        mcpPort: settings.mcpPort,
        maxCaptains: settings.maxCaptains,
      };
      const updated = await updateSettings(sent);
      applyIncomingSettings(updated as unknown as ServerSettings, sent);
      showToast('success', t('Server configuration saved'));
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : t('Unknown error');
      showToast('error', t('Failed: {{message}}', { message: msg }));
    }
  };

  const handleSaveRebuildSettings = async () => {
    if (!settings) return;
    try {
      const sent = {
        selfVesselId: settings.selfVesselId ?? '',
        rebuildSlotRetentionCount: settings.rebuildSlotRetentionCount ?? 3,
      };
      const updated = await updateSettings(sent);
      applyIncomingSettings(updated as unknown as ServerSettings, sent);
      showToast('success', t('Rebuild settings saved'));
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : t('Unknown error');
      showToast('error', t('Failed: {{message}}', { message: msg }));
    }
  };

  const handleSaveAgentSettings = async () => {
    if (!settings) return;
    try {
      const sent = {
        heartbeatIntervalSeconds: settings.heartbeatIntervalSeconds,
        stallThresholdMinutes: settings.stallThresholdMinutes,
        ...(settings.stageCheckTimeoutMinutes !== undefined ? { stageCheckTimeoutMinutes: settings.stageCheckTimeoutMinutes } : {}),
        idleCaptainTimeoutSeconds: settings.idleCaptainTimeoutSeconds,
        landingMode: settings.landingMode || DEFAULT_GLOBAL_LANDING_MODE,
        ...(settings.requireHarborForLaunch !== undefined ? { requireHarborForLaunch: settings.requireHarborForLaunch } : {}),
        ...(settings.blockDependentsOfFailedStages !== undefined ? { blockDependentsOfFailedStages: settings.blockDependentsOfFailedStages } : {}),
      };
      const updated = await updateSettings(sent);
      applyIncomingSettings(updated as unknown as ServerSettings, sent);
      showToast('success', t('Agent settings saved'));
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : t('Unknown error');
      showToast('error', t('Failed: {{message}}', { message: msg }));
    }
  };

  const handleSavePlanningSessionSettings = async () => {
    if (!settings) return;
    try {
      const sent = {
        planningSessionInactivityTimeoutMinutes: settings.planningSessionInactivityTimeoutMinutes,
        planningSessionAbandonmentTimeoutMinutes: settings.planningSessionAbandonmentTimeoutMinutes,
        planningSessionRetentionDays: settings.planningSessionRetentionDays,
      };
      const updated = await updateSettings(sent);
      applyIncomingSettings(updated as unknown as ServerSettings, sent);
      showToast('success', t('Planning session settings saved'));
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : t('Unknown error');
      showToast('error', t('Failed: {{message}}', { message: msg }));
    }
  };

  const handleSaveRemoteControlSettings = async () => {
    if (!settings) return;
    try {
      const sent = {
        remoteControl: settings.remoteControl,
      };
      const updated = await updateSettings(sent);
      applyIncomingSettings(updated as unknown as ServerSettings, sent);
      showToast('success', t('Remote control settings saved'));
      const refreshedHealth = (await getHealth()) as unknown as HealthInfo;
      setHealth(refreshedHealth);
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : t('Unknown error');
      showToast('error', t('Failed: {{message}}', { message: msg }));
    }
  };

  const handleRemoteTunnelEnabledChange = (enabled: boolean) => {
    if (!settings) return;

    if (!enabled) {
      setSettings({
        ...settings,
        remoteControl: { ...settings.remoteControl, enabled: false },
      });
      return;
    }

    setConfirmDialog({
      open: true,
      title: t('Enable Remote Tunnel'),
      message: t('Enabling remote tunnel will enable remote connectivity to this Armada instance. Are you sure?'),
      onConfirm: () => {
        setSettings(current =>
          current
            ? {
                ...current,
                remoteControl: { ...current.remoteControl, enabled: true },
              }
            : current,
        );
        closeConfirmDialog();
      },
    });
  };

  const handleHealthCheck = async () => {
    try {
      const result = (await getHealth()) as unknown as HealthInfo;
      setHealth(result);
      showToast('info', t('Health: {{status}} | Uptime: {{uptime}}', {
        status: translateHealthStatus(result.status),
        uptime: result.uptime,
      }));
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : t('Unknown error');
      showToast('error', t('Health check failed: {{message}}', { message: msg }));
    }
  };

  const handleBackup = async () => {
    setBackupLoading(true);
    try {
      await downloadBackup();
      showToast('success', t('Backup downloaded'));
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : t('Unknown error');
      showToast('error', t('Backup failed: {{message}}', { message: msg }));
    } finally {
      setBackupLoading(false);
    }
  };

  const handleRestoreClick = () => {
    restoreFileRef.current?.click();
  };

  const handleRestoreFile = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    try {
      await restoreBackup(file);
      showToast('success', t('Restore completed successfully. Server restart recommended.'));
      loadData();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : t('Unknown error');
      showToast('error', t('Restore failed: {{message}}', { message: msg }));
    }
    if (restoreFileRef.current) restoreFileRef.current.value = '';
  };

  const handleStopServer = () => {
    setConfirmDialog({
      open: true,
      title: t('Stop Server'),
      message: t('Stop the Admiral server? This will shut down everything.'),
      onConfirm: async () => {
        try {
          await stopServer();
          showToast('warning', t('Server shutting down...'));
        } catch (e: unknown) {
          const msg = e instanceof Error ? e.message : t('Unknown error');
          showToast('error', t('Failed: {{message}}', { message: msg }));
        }
        closeConfirmDialog();
      },
    });
  };

  const handleRestartServer = () => {
    setConfirmDialog({
      open: true,
      title: t('Restart Server'),
      message: t('Restart the Admiral server? A replacement process starts and this instance shuts down; the dashboard will be briefly unavailable while it comes back up.'),
      onConfirm: async () => {
        try {
          await restartServer();
          showToast('warning', t('Server restarting... the dashboard will reconnect shortly.'));
        } catch (e: unknown) {
          const msg = e instanceof Error ? e.message : t('Unknown error');
          showToast('error', t('Failed: {{message}}', { message: msg }));
        }
        closeConfirmDialog();
      },
    });
  };

  const handleRebuild = () => {
    const trimmedRef = buildRef.trim();
    const refLabel = trimmedRef || t('current HEAD');
    setConfirmDialog({
      open: true,
      title: t('Rebuild Armada'),
      message: t('Rebuild the Admiral from source at {{ref}}? The new build is published into a fresh slot while this instance keeps running; on success the database is backed up and the server cuts over to the new build. A failed build will not disturb the running server.', { ref: refLabel }),
      onConfirm: async () => {
        closeConfirmDialog();
        try {
          const started = await rebuildServer(trimmedRef ? { Ref: trimmedRef } : {});
          setRebuild(started);
          setRebuildLogOpen(true);
          showToast('warning', t('Rebuild started; building slot {{slot}}...', { slot: started.slot || t('(pending)') }));
          stopRebuildPoll();
          rebuildPollRef.current = window.setInterval(refreshRebuildStatus, 1500);
        } catch (e: unknown) {
          const msg = e instanceof Error ? e.message : t('Unknown error');
          showToast('error', t('Rebuild failed to start: {{message}}', { message: msg }));
        }
      },
    });
  };

  const handleRollback = () => {
    setConfirmDialog({
      open: true,
      title: t('Roll Back Rebuild'),
      message: t('Roll back to the previous build ({{slot}})? If the last rebuild changed the database schema, the pre-rebuild backup is restored and any data written since the cutover is permanently lost. The server then restarts on the previous build.', { slot: rebuild?.previousSlot || t('previous slot') }),
      onConfirm: async () => {
        closeConfirmDialog();
        try {
          const s = await rollbackServer();
          setRebuild(s);
          showToast('warning', t('Rolling back to {{slot}}; the dashboard will reconnect shortly.', { slot: s.previousSlot || t('previous slot') }));
        } catch (e: unknown) {
          const msg = e instanceof Error ? e.message : t('Unknown error');
          showToast('error', t('Rollback failed: {{message}}', { message: msg }));
        }
      },
    });
  };

  const handleFactoryReset = () => {
    setConfirmDialog({
      open: true,
      title: t('Factory Reset'),
      message: t('WARNING: Factory reset will delete ALL data including the database, logs, docks, and repos. Settings will be preserved. This cannot be undone. Continue?'),
      onConfirm: async () => {
        try {
          await resetServer();
          showToast('success', t('Factory reset complete'));
        } catch (e: unknown) {
          const msg = e instanceof Error ? e.message : t('Unknown error');
          showToast('error', t('Factory reset failed: {{message}}', { message: msg }));
        }
        closeConfirmDialog();
      },
    });
  };

  const getMcpRpcUrl = (): string => buildMcpRpcUrl(health?.ports?.mcp, settings?.mcpPort);

  const getMcpConfigHttp = (client: McpClientKey): string => buildMcpConfigHttp(client, getMcpRpcUrl());

  const copyAndToast = (text: string) => {
    copyToClipboard(text).then(() => showToast('success', t('Copied to clipboard'))).catch(() => {});
  };

  const remoteTunnelEnabled = settings?.remoteControl.enabled ?? health?.remoteTunnel?.enabled ?? false;
  const remoteTunnelIndicator = getRemoteTunnelIndicator(
    remoteTunnelEnabled,
    health?.remoteTunnel?.state,
  );
  const remoteProxyMode = !!proxyContext?.selectedInstanceId;
  const remoteProxyInstanceId = proxyContext?.selectedInstance?.instanceId || proxyContext?.selectedInstanceId || '';
  const remoteSettingsLocked = remoteProxyMode;
  const serverDetailFields = [
    {
      key: 'version',
      label: 'Version',
      help: 'Armada version the Admiral server is running.',
      value: health?.version || '-',
    },
    {
      key: 'api-url',
      label: 'API URL',
      help: 'Address this dashboard was loaded from; REST API calls go to the same origin under /api/v1.',
      value: window.location.origin,
    },
    {
      key: 'admiral-port',
      label: 'Admiral Port',
      help: 'Port the running Admiral is listening on for the REST API and dashboard. A saved change below shows here only after a restart.',
      value: String(health?.ports?.admiral || window.location.port || '-'),
    },
    {
      key: 'mcp-port',
      label: 'MCP Port',
      help: 'Port the running Admiral is listening on for MCP clients. A saved change below shows here only after a restart.',
      value: String(health?.ports?.mcp || '-'),
    },
    {
      key: 'websocket-port',
      label: 'WebSocket Port',
      help: 'Port serving the /ws WebSocket that pushes live updates to the dashboard and clients. It is the same as the Admiral port.',
      value: String(health?.ports?.webSocket || '-'),
    },
    {
      key: 'tunnel-state',
      label: 'Tunnel State',
      help: 'Current state of the outbound tunnel to Armada.Proxy, such as Connected, Connecting, or Disabled.',
      value: translateRemoteTunnelState(remoteTunnelEnabled, health?.remoteTunnel?.state),
    },
    {
      key: 'tunnel-instance',
      label: 'Tunnel Instance',
      help: 'Name this Admiral is listed under in Armada.Proxy. Set Instance ID Override below to choose it.',
      value: health?.remoteTunnel?.instanceId || '-',
    },
    {
      key: 'tunnel-latency',
      label: 'Tunnel Latency',
      help: 'Round-trip time of the latest tunnel heartbeat to Armada.Proxy. High values make remote pages slow.',
      value: health?.remoteTunnel?.latencyMs != null ? `${health.remoteTunnel.latencyMs.toLocaleString()} ms` : '-',
    },
    {
      key: 'tunnel-last-heartbeat',
      label: 'Tunnel Last Heartbeat',
      help: 'When the last tunnel heartbeat was answered. An old time while connected suggests the link has stalled.',
      value: health?.remoteTunnel?.lastHeartbeatUtc ? formatAbsoluteTime(health.remoteTunnel.lastHeartbeatUtc) : '-',
    },
  ];

  if (loading) {
    return (
      <div>
        <h2>{t('Server Settings')}</h2>
        <p className="text-muted">{t('Loading...')}</p>
      </div>
    );
  }

  return (
    <div>
      <PageHeader
        title={t('Server Settings')}
        subtitle={t('Admiral server health, configuration, and operational controls.')}
        actions={(
          <>
            <AutoRefreshSelect seconds={refreshSeconds} onChange={setRefreshSeconds} />
            <RefreshButton onRefresh={() => loadData(true)} title={t('Reload health, settings, and tunnel status from the Admiral. Unsaved edits on this page are replaced.')} />
          </>
        )}
      />

      <ErrorModal error={error} onClose={() => setError('')} />

      {remoteProxyMode && (
        <div className="alert alert-warning" style={{ marginBottom: '1rem' }}>
          {t(
            'This page is connected through Armada.Proxy for {{instanceId}}. Local server settings, tunnel settings, setup, restore, shutdown, and factory reset are blocked in remote mode.',
            { instanceId: remoteProxyInstanceId || t('the selected deployment') },
          )}
        </div>
      )}

      {/* Health Status Cards */}
      <div className="card-grid">
        <div className="card" title={t('Health reported by the Admiral health endpoint. Anything other than Healthy means a core check failed; run Doctor for details.')}>
          <div className="card-label">{t('Health')}</div>
          <div className="card-value" style={{ fontSize: '1.2rem' }}>
            {health ? translateHealthStatus(health.status) : t('Loading...')}
          </div>
          {health && (
            <div className="card-detail text-muted">
              {t('Checked: {{timestamp}}', { timestamp: formatAbsoluteTime(health.timestamp) })}
            </div>
          )}
        </div>

        <div className="card" title={t('How long the Admiral process has been running since its last start or restart.')}>
          <div className="card-label">{t('Uptime')}</div>
          <div className="card-value" style={{ fontSize: '1.2rem' }}>
            {health?.uptime || '-'}
          </div>
          {health?.startUtc && (
            <div className="card-detail text-muted">
              {t('Started: {{timestamp}}', { timestamp: formatAbsoluteTime(health.startUtc) })}
            </div>
          )}
        </div>

        <div className="card" title={t('How this dashboard talks to the Admiral. Live means a WebSocket pushes updates instantly; Online (HTTP) means pages poll on their refresh interval.')}>
          <div className="card-label">{t('Connection')}</div>
          <div className="card-value" style={{ fontSize: '1.2rem' }}>
            <span
              className={`status-dot ${connected ? 'connected' : 'disconnected'}`}
              style={{ display: 'inline-block', verticalAlign: 'middle', marginRight: '0.5rem' }}
            />
            <span>{t(connected ? 'Live (WebSocket)' : 'Online (HTTP)')}</span>
          </div>
        </div>

        <div className="card" title={t('State of the outbound tunnel to Armada.Proxy that allows remote access to this Admiral. Disabled unless Enable Remote Tunnel is on below.')}>
          <div className="card-label">{t('Remote Tunnel')}</div>
          <div className="card-value" style={{ fontSize: '1.2rem' }}>
            {translateRemoteTunnelState(remoteTunnelEnabled, health?.remoteTunnel?.state)}
          </div>
          <div className="card-detail text-muted">
            {health?.remoteTunnel?.tunnelUrl || t('No tunnel URL configured')}
          </div>
        </div>
      </div>

      {/* Detail Fields */}
      <div className="detail-grid server-detail-grid" style={{ marginTop: '1rem' }}>
        {serverDetailFields.map((field) => (
          <div key={field.key} className="detail-field" title={t(field.help)}>
            <span className="detail-label" title={t(field.help)}>{t(field.label)}</span>
            <span className="mono" title={t(field.help)}>{field.value}</span>
          </div>
        ))}
      </div>

      {/* Server Configuration */}
      {settings && (
        <div className="settings-section" style={{ marginTop: '1.5rem' }}>
          <h3>{t('Server Configuration')}</h3>
          <fieldset disabled={remoteSettingsLocked} style={{ border: 'none', margin: 0, padding: 0 }}>
            <div className="settings-grid">
              <div className="form-group">
                <label title={t('TCP port for the REST API, dashboard and WebSocket (default 7890). Takes effect after the Admiral restarts; clients and Harbors must then use the new port.')}>{t('Admiral Port')}</label>
                <input aria-label={t('REST API port (1-65535)')}
                  type="number"
                  value={settings.admiralPort}
                  onChange={(e) =>
                    setSettings({ ...settings, admiralPort: parseInt(e.target.value) || 0 })
                  }
                  min={1}
                  max={65535}
                  title={t('TCP port for the REST API, dashboard and WebSocket (default 7890). Takes effect after the Admiral restarts; clients and Harbors must then use the new port.')}
                />
              </div>
              <div className="form-group">
                <label title={t('TCP port for the MCP server that agents and tools connect to (default 7891). Takes effect after the Admiral restarts; update MCP client configs to match.')}>{t('MCP Port')}</label>
                <input aria-label={t('MCP server port (1-65535)')}
                  type="number"
                  value={settings.mcpPort}
                  onChange={(e) =>
                    setSettings({ ...settings, mcpPort: parseInt(e.target.value) || 0 })
                  }
                  min={1}
                  max={65535}
                  title={t('TCP port for the MCP server that agents and tools connect to (default 7891). Takes effect after the Admiral restarts; update MCP client configs to match.')}
                />
              </div>
              <div className="form-group">
                <label title={t('Cap on total captains when Armada auto-creates captains to keep its idle pool filled (default 5). 0 means no cap. Captains you add by hand are not blocked.')}>{t('Max Captains')}</label>
                <input aria-label={t('Maximum captains (0 = unlimited)')}
                  type="number"
                  value={settings.maxCaptains}
                  onChange={(e) =>
                    setSettings({ ...settings, maxCaptains: parseInt(e.target.value) || 0 })
                  }
                  min={0}
                  title={t('Cap on total captains when Armada auto-creates captains to keep its idle pool filled (default 5). 0 means no cap. Captains you add by hand are not blocked.')}
                />
              </div>
            </div>
            <button
              className="btn-primary btn-sm"
              onClick={handleSaveServerConfig}
              title={t('Save the ports and captain cap above. Port changes take effect after the Admiral restarts.')}
            >
              {t('Save Server Config')}
            </button>
          </fieldset>
        </div>
      )}

      {/* Rebuild (self-update) */}
      {settings && (
        <div className="settings-section" style={{ marginTop: '1.5rem' }}>
          <h3>{t('Rebuild Armada')}</h3>
          <p className="settings-hint" style={{ marginBottom: '1rem' }}>
            {t('Designate the vessel that holds Armada\'s own source so the "Rebuild Armada" button knows what to build. Rebuilding forces a server restart; if you are running Harbor, restart it manually afterward.')}
          </p>
          <fieldset disabled={remoteSettingsLocked} style={{ border: 'none', margin: 0, padding: 0 }}>
            <div className="settings-grid">
              <div className="form-group">
                <label title={t('The vessel whose repository holds Armada source code. Rebuild Armada builds from it. Choose none to turn off rebuilding.')}>{t('Self Vessel ID')}</label>
                <select aria-label={t('The vessel that holds Armada source')}
                  value={settings.selfVesselId ?? ''}
                  onChange={(e) => setSettings({ ...settings, selfVesselId: e.target.value })}
                  title={t('The vessel whose repository holds Armada source code. Rebuild Armada builds from it. Choose none to turn off rebuilding.')}
                >
                  <option value="">{t('-- none --')}</option>
                  {vessels.map((v) => (
                    <option key={v.id} value={v.id}>{v.name} ({v.id})</option>
                  ))}
                </select>
              </div>
              <div className="form-group">
                <label title={t('How many published builds to keep on disk so you can roll back (default 3, minimum 1). Older builds are deleted after a successful rebuild.')}>{t('Slot Retention')}</label>
                <input aria-label={t('Number of build slots to retain (minimum 1)')}
                  type="number"
                  value={settings.rebuildSlotRetentionCount ?? 3}
                  onChange={(e) => setSettings({ ...settings, rebuildSlotRetentionCount: parseInt(e.target.value) || 1 })}
                  min={1}
                  title={t('How many published builds to keep on disk so you can roll back (default 3, minimum 1). Older builds are deleted after a successful rebuild.')}
                />
              </div>
            </div>
            <button
              className="btn-primary btn-sm"
              onClick={handleSaveRebuildSettings}
              title={t('Save the source vessel and slot retention used by Rebuild Armada. Does not start a rebuild.')}
            >
              {t('Save Rebuild Settings')}
            </button>
          </fieldset>
        </div>
      )}

      {/* Agent Settings */}
      {settings && (
        <div className="settings-section" style={{ marginTop: '1.5rem' }}>
          <h3 title={t('Settings that control captain monitoring, stalling, cleanup, and how finished missions land.')}>{t('Agent Settings')}</h3>
          <fieldset disabled={remoteSettingsLocked} style={{ border: 'none', margin: 0, padding: 0 }}>
            <div className="settings-grid">
              <div className="form-group">
                <label title={t('Seconds between Admiral health checks of captains and missions (default 10, minimum 5). Lower reacts faster to stalls but does more work.')}>{t('Heartbeat Interval (seconds)')}</label>
                <input aria-label={t('Health check interval, minimum 5 seconds')}
                  type="number"
                  value={settings.heartbeatIntervalSeconds}
                  onChange={(e) =>
                    setSettings({
                      ...settings,
                      heartbeatIntervalSeconds: parseInt(e.target.value) || 5,
                    })
                  }
                  min={5}
                  title={t('Seconds between Admiral health checks of captains and missions (default 10, minimum 5). Lower reacts faster to stalls but does more work.')}
                />
              </div>
              <div className="form-group">
                <label title={t('Minutes a working captain may go without a heartbeat before it is treated as stalled, stopped and recovered (default 10, minimum 1).')}>{t('Stall Threshold (minutes)')}</label>
                <input aria-label={t('Minutes before a captain is considered stalled')}
                  type="number"
                  value={settings.stallThresholdMinutes}
                  onChange={(e) =>
                    setSettings({
                      ...settings,
                      stallThresholdMinutes: parseInt(e.target.value) || 1,
                    })
                  }
                  min={1}
                  title={t('Minutes a working captain may go without a heartbeat before it is treated as stalled, stopped and recovered (default 10, minimum 1).')}
                />
              </div>
              <div className="form-group">
                <label title={t('Minutes each stage-check command (a vessel\'s build and unit-test commands, run after a stage) may take before it is reported as timed out (default 15, 1 to 240). Set it above the slowest command, or give the workflow profile a narrower test command.')}>{t('Stage Check Timeout (minutes)')}</label>
                <input aria-label={t('Minutes each stage-check command may run')}
                  type="number"
                  value={settings.stageCheckTimeoutMinutes ?? 15}
                  onChange={(e) =>
                    setSettings({
                      ...settings,
                      stageCheckTimeoutMinutes: Math.min(240, Math.max(1, parseInt(e.target.value) || 1)),
                    })
                  }
                  min={1}
                  max={240}
                  title={t('Minutes each stage-check command (a vessel\'s build and unit-test commands, run after a stage) may take before it is reported as timed out (default 15, 1 to 240). Set it above the slowest command, or give the workflow profile a narrower test command.')}
                />
              </div>
              <div className="form-group">
                <label title={t('Seconds an idle captain is kept before it is removed automatically. 0 (the default) keeps idle captains indefinitely.')}>{t('Idle Captain Timeout (seconds)')}</label>
                <input aria-label={t('Auto-remove idle captains after this many seconds (0 = disabled)')}
                  type="number"
                  value={settings.idleCaptainTimeoutSeconds}
                  onChange={(e) =>
                    setSettings({
                      ...settings,
                      idleCaptainTimeoutSeconds: parseInt(e.target.value) || 0,
                    })
                  }
                  min={0}
                  title={t('Seconds an idle captain is kept before it is removed automatically. 0 (the default) keeps idle captains indefinitely.')}
                />
              </div>
              <DefaultLandingModeField value={settings.landingMode} onChange={(landingMode) => setSettings({ ...settings, landingMode })} />
              {settings.requireHarborForLaunch !== undefined && (
                <div className="form-group">
                  <label className="settings-checkbox-label" title={t(REQUIRE_HARBOR_FOR_LAUNCH_TOOLTIP)}>
                    <input
                      type="checkbox"
                      title={t(REQUIRE_HARBOR_FOR_LAUNCH_TOOLTIP)}
                      checked={settings.requireHarborForLaunch}
                      onChange={(e) => setSettings({ ...settings, requireHarborForLaunch: e.target.checked })}
                    />
                    <span>{t('Require Harbor for Launch')}</span>
                  </label>
                  <small className="text-dim">{t('On: missions wait for a Harbor. Off: they run on the Admiral host when no Harbor can take them.')}</small>
                </div>
              )}
              {settings.blockDependentsOfFailedStages !== undefined && (
                <div className="form-group">
                  <label className="settings-checkbox-label" title={t('When a stage fails, the stages waiting on it show Blocked and the voyage stays open until you restart the failed stage. Off (the default): they are cancelled and the voyage ends Failed; restarting the failed stage still restores them.')}>
                    <input
                      type="checkbox"
                      title={t('When a stage fails, the stages waiting on it show Blocked and the voyage stays open until you restart the failed stage. Off (the default): they are cancelled and the voyage ends Failed; restarting the failed stage still restores them.')}
                      checked={settings.blockDependentsOfFailedStages}
                      onChange={(e) => setSettings({ ...settings, blockDependentsOfFailedStages: e.target.checked })}
                    />
                    <span>{t('Block Stages After a Failure')}</span>
                  </label>
                  <small className="text-dim">{t('On: stages after a failed one show Blocked until it is restarted. Off: they are cancelled.')}</small>
                </div>
              )}
            </div>
            <button
              className="btn-primary btn-sm"
              onClick={handleSaveAgentSettings}
              title={t('Save the agent timing, default landing mode, Harbor requirement and failure blocking. They apply to the running Admiral immediately.')}
            >
              {t('Save Agent Settings')}
            </button>
          </fieldset>
        </div>
      )}

      {settings && (
        <div className="settings-section" style={{ marginTop: '1.5rem' }}>
          <h3 title={t('Controls how long planning sessions remain open when they are idle and how long ended transcripts are retained.')}>{t('Planning Session Settings')}</h3>
          <p className="text-muted" style={{ marginBottom: '0.75rem' }}>
            {t('Idle planning sessions are only auto-ended when there is no running planning process. Set a value to 0 to disable the corresponding cleanup rule.')}
          </p>
          <fieldset disabled={remoteSettingsLocked} style={{ border: 'none', margin: 0, padding: 0 }}>
            <div className="settings-grid">
              <div className="form-group">
                <label title={t('Minutes an idle planning session with no running process stays open before it is ended automatically (default 60). 0 turns this off.')}>{t('Idle Session Timeout (minutes)')}</label>
                <input aria-label={t('Minutes before an idle planning session is automatically ended (0 = disabled)')}
                  type="number"
                  value={settings.planningSessionInactivityTimeoutMinutes}
                  onChange={(e) =>
                    setSettings({
                      ...settings,
                      planningSessionInactivityTimeoutMinutes: parseInt(e.target.value) || 0,
                    })
                  }
                  min={0}
                  title={t('Minutes an idle planning session with no running process stays open before it is ended automatically (default 60). 0 turns this off.')}
                />
              </div>
              <div className="form-group">
                <label title={t('Safety net: minutes before a stale planning session with no running process is ended even when the idle timeout is off (default 240). 0 turns this off.')}>{t('Abandonment Timeout (minutes)')}</label>
                <input aria-label={t('Minutes before a stale planning session is force-ended (0 = disabled)')}
                  type="number"
                  value={settings.planningSessionAbandonmentTimeoutMinutes}
                  onChange={(e) =>
                    setSettings({
                      ...settings,
                      planningSessionAbandonmentTimeoutMinutes: parseInt(e.target.value) || 0,
                    })
                  }
                  min={0}
                  title={t('Safety net: minutes before a stale planning session with no running process is ended even when the idle timeout is off (default 240). 0 turns this off.')}
                />
              </div>
              <div className="form-group">
                <label title={t('Days to keep stopped or failed planning sessions and their transcripts before they are deleted (default 0, which keeps them forever).')}>{t('Transcript Retention (days)')}</label>
                <input aria-label={t('Days to keep stopped or failed planning sessions before deleting them (0 = disabled)')}
                  type="number"
                  value={settings.planningSessionRetentionDays}
                  onChange={(e) =>
                    setSettings({
                      ...settings,
                      planningSessionRetentionDays: parseInt(e.target.value) || 0,
                    })
                  }
                  min={0}
                  title={t('Days to keep stopped or failed planning sessions and their transcripts before they are deleted (default 0, which keeps them forever).')}
                />
              </div>
            </div>
            <button
              className="btn-primary btn-sm"
              onClick={handleSavePlanningSessionSettings}
              title={t('Save the three planning session timers above. They apply immediately.')}
            >
              {t('Save Planning Session Settings')}
            </button>
          </fieldset>
        </div>
      )}

      {/* Vessel health settings: loads and saves its own RepositoryHealth section. */}
      {settings && <RepositoryHealthSettingsSection locked={remoteSettingsLocked} />}

      {settings && (
        <ImportFleetActionSettings
          importSettings={settings.import}
          fleetActionSettings={settings.fleetActions}
          locked={remoteSettingsLocked}
          onSaved={(updated) => applyIncomingSettings(updated as unknown as ServerSettings)}
          notify={showToast}
        />
      )}

      {settings && isAdmin && (
        <CliPermissionSettings
          permissions={settings.permissions}
          locked={remoteSettingsLocked}
          onSaved={(updated) => applyIncomingSettings(updated as unknown as ServerSettings)}
          notify={showToast}
        />
      )}

      {settings && (
        <RetentionSettings
          retention={settings.retention}
          locked={remoteSettingsLocked}
          onSaved={(updated) => applyIncomingSettings(updated as unknown as ServerSettings)}
          notify={showToast}
        />
      )}

      {settings && isAdmin && settings.blackBox && (
        <BlackBoxSettings
          blackBox={settings.blackBox}
          locked={remoteSettingsLocked}
          onSaved={(updated) => setSettings(mergeServerSettings(updated as unknown as ServerSettings))}
          notify={showToast}
        />
      )}

      {settings && (
        <AskSettings
          ask={settings.ask}
          locked={remoteSettingsLocked}
          onSaved={(updated) => applyIncomingSettings(updated as unknown as ServerSettings)}
          notify={showToast}
        />
      )}

      {settings && (
        <div className="settings-section" style={{ marginTop: '1.5rem' }}>
          <h3>{t('Remote Control')}</h3>
          <p className="text-muted" style={{ marginBottom: '0.75rem' }}>
            {t('Outbound tunnel settings for connecting this Armada server to Armada.Proxy.')}
          </p>
          <fieldset disabled={remoteSettingsLocked} style={{ border: 'none', margin: 0, padding: 0 }}>
            <div className="settings-grid">
              <div className="form-group">
                <label className="settings-checkbox-label" title={t('Open an outbound tunnel from this Admiral to Armada.Proxy so it can be managed remotely. Turning it on or off asks for confirmation.')}>
                  <input
                    type="checkbox"
                    title={t('Open an outbound tunnel from this Admiral to Armada.Proxy so it can be managed remotely. Turning it on or off asks for confirmation.')}
                    checked={settings.remoteControl.enabled}
                    onChange={(e) => handleRemoteTunnelEnabledChange(e.target.checked)}
                  />
                  <span>{t('Enable Remote Tunnel')}</span>
                </label>
              </div>
              <div className="form-group">
                <label title={t('Armada.Proxy address this Admiral connects out to, for example https://proxy.example.com. http and https become ws and wss, and /tunnel is added if missing.')}>{t('Tunnel URL')}</label>
                <input aria-label={t('Proxy base URL or tunnel endpoint. http/https will be normalized to ws/wss and /tunnel will be added automatically when needed.')}
                  type="text"
                  value={settings.remoteControl.tunnelUrl ?? ''}
                  onChange={(e) =>
                    setSettings({
                      ...settings,
                      remoteControl: {
                        ...settings.remoteControl,
                        tunnelUrl: e.target.value || null,
                      },
                    })
                  }
                  placeholder={DEFAULT_REMOTE_TUNNEL_URL}
                  title={t('Armada.Proxy address this Admiral connects out to, for example https://proxy.example.com. http and https become ws and wss, and /tunnel is added if missing.')}
                />
              </div>
              <div className="form-group">
                <label title={t('Name this deployment shows under in Armada.Proxy, for example home-lab. Leave blank to let Armada derive a stable one from this machine.')}>{t('Instance ID Override')}</label>
                <input aria-label={t('Optional stable deployment identifier advertised to Armada.Proxy. Leave blank to let Armada derive one automatically.')}
                  type="text"
                  value={settings.remoteControl.instanceId ?? ''}
                  onChange={(e) =>
                    setSettings({
                      ...settings,
                      remoteControl: {
                        ...settings.remoteControl,
                        instanceId: e.target.value || null,
                      },
                    })
                  }
                  placeholder={t('Leave blank for auto-generated')}
                  title={t('Name this deployment shows under in Armada.Proxy, for example home-lab. Leave blank to let Armada derive a stable one from this machine.')}
                />
              </div>
              <div className="form-group">
                <label title={t('Extra admission token, needed only when the proxy requires instance enrollment. Leave blank otherwise. Stored as a secret.')}>{t('Instance Enrollment Token')}</label>
                <div className="settings-secret-field">
                  <input aria-label={t('Optional extra admission token used only when Armada.Proxy requires instance enrollment tokens.')}
                    type={revealedRemoteField === 'enrollmentToken' ? 'text' : 'password'}
                    value={settings.remoteControl.enrollmentToken ?? ''}
                    onChange={(e) =>
                      setSettings({
                        ...settings,
                        remoteControl: {
                          ...settings.remoteControl,
                          enrollmentToken: e.target.value || null,
                        },
                      })
                    }
                    placeholder={t('Optional bootstrap token')}
                    title={t('Extra admission token, needed only when the proxy requires instance enrollment. Leave blank otherwise. Stored as a secret.')}
                  />
                  <button
                    type="button"
                    className="settings-secret-toggle"
                    aria-label={t(revealedRemoteField === 'enrollmentToken' ? 'Hide enrollment token' : 'Show enrollment token')}
                    title={t('Press and hold to show the enrollment token; it hides again when you let go.')}
                    onPointerDown={(e) => {
                      e.preventDefault();
                      beginRevealRemoteField('enrollmentToken');
                    }}
                    onPointerUp={endRevealRemoteField}
                    onPointerLeave={endRevealRemoteField}
                    onPointerCancel={endRevealRemoteField}
                    onBlur={endRevealRemoteField}
                  >
                    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                      <path d="M2 12s3.5-6 10-6 10 6 10 6-3.5 6-10 6-10-6-10-6z" />
                      <circle cx="12" cy="12" r="3" />
                    </svg>
                  </button>
                </div>
              </div>
              <div className="form-group">
                <label title={t('Shared secret this Admiral uses to sign in to Armada.Proxy; the same password unlocks browser access through the proxy. Blank means armadaadmin, so change it.')}>{t('Proxy Shared Password')}</label>
                <div className="settings-secret-field">
                  <input aria-label={t('Shared secret used to authenticate this Armada instance to Armada.Proxy and to unlock Armada.Proxy browser access.')}
                    type={revealedRemoteField === 'password' ? 'text' : 'password'}
                    value={settings.remoteControl.password ?? ''}
                    onChange={(e) =>
                      setSettings({
                        ...settings,
                        remoteControl: {
                          ...settings.remoteControl,
                          password: e.target.value,
                        },
                      })
                    }
                    placeholder={t('Defaults to armadaadmin')}
                    title={t('Shared secret this Admiral uses to sign in to Armada.Proxy; the same password unlocks browser access through the proxy. Blank means armadaadmin, so change it.')}
                  />
                  <button
                    type="button"
                    className="settings-secret-toggle"
                    aria-label={t(revealedRemoteField === 'password' ? 'Hide shared password' : 'Show shared password')}
                    title={t('Press and hold to show the shared password; it hides again when you let go.')}
                    onPointerDown={(e) => {
                      e.preventDefault();
                      beginRevealRemoteField('password');
                    }}
                    onPointerUp={endRevealRemoteField}
                    onPointerLeave={endRevealRemoteField}
                    onPointerCancel={endRevealRemoteField}
                    onBlur={endRevealRemoteField}
                  >
                    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                      <path d="M2 12s3.5-6 10-6 10 6 10 6-3.5 6-10 6-10-6-10-6z" />
                      <circle cx="12" cy="12" r="3" />
                    </svg>
                  </button>
                </div>
              </div>
              <div className="form-group">
                <label title={t('Seconds to wait for the tunnel connection to open before the attempt counts as failed and a retry is scheduled (default 15, 5-300).')}>{t('Connect Timeout (seconds)')}</label>
                <input aria-label={t('How long Armada waits for the proxy tunnel connection to open before treating the attempt as failed.')}
                  type="number"
                  value={settings.remoteControl.connectTimeoutSeconds}
                  onChange={(e) =>
                    setSettings({
                      ...settings,
                      remoteControl: {
                        ...settings.remoteControl,
                        connectTimeoutSeconds: parseInt(e.target.value) || 15,
                      },
                    })
                  }
                  min={5}
                  max={300}
                  title={t('Seconds to wait for the tunnel connection to open before the attempt counts as failed and a retry is scheduled (default 15, 5-300).')}
                />
              </div>
              <div className="form-group">
                <label title={t('Seconds between tunnel heartbeats that keep the connection alive and measure latency (default 30, 5-300).')}>{t('Heartbeat Interval (seconds)')}</label>
                <input aria-label={t('How often Armada sends tunnel heartbeats to keep the connection alive and measure latency.')}
                  type="number"
                  value={settings.remoteControl.heartbeatIntervalSeconds}
                  onChange={(e) =>
                    setSettings({
                      ...settings,
                      remoteControl: {
                        ...settings.remoteControl,
                        heartbeatIntervalSeconds: parseInt(e.target.value) || 30,
                      },
                    })
                  }
                  min={5}
                  max={300}
                  title={t('Seconds between tunnel heartbeats that keep the connection alive and measure latency (default 30, 5-300).')}
                />
              </div>
              <div className="form-group">
                <label title={t('Seconds to wait before the first reconnect after a tunnel failure; later retries back off from here (default 5).')}>{t('Reconnect Base Delay (seconds)')}</label>
                <input aria-label={t('Initial reconnect backoff after a tunnel failure. Later retries grow from this base delay.')}
                  type="number"
                  value={settings.remoteControl.reconnectBaseDelaySeconds}
                  onChange={(e) =>
                    setSettings({
                      ...settings,
                      remoteControl: {
                        ...settings.remoteControl,
                        reconnectBaseDelaySeconds: parseInt(e.target.value) || 5,
                      },
                    })
                  }
                  min={1}
                  max={300}
                  title={t('Seconds to wait before the first reconnect after a tunnel failure; later retries back off from here (default 5).')}
                />
              </div>
              <div className="form-group">
                <label title={t('Longest wait between tunnel reconnect attempts, however many have failed (default 60).')}>{t('Reconnect Max Delay (seconds)')}</label>
                <input aria-label={t('Maximum reconnect backoff between tunnel retry attempts.')}
                  type="number"
                  value={settings.remoteControl.reconnectMaxDelaySeconds}
                  onChange={(e) =>
                    setSettings({
                      ...settings,
                      remoteControl: {
                        ...settings.remoteControl,
                        reconnectMaxDelaySeconds: parseInt(e.target.value) || 60,
                      },
                    })
                  }
                  min={1}
                  max={3600}
                  title={t('Longest wait between tunnel reconnect attempts, however many have failed (default 60).')}
                />
              </div>
              <div className="form-group">
                <label className="settings-checkbox-label" title={t('Accept self-signed or invalid TLS certificates from the proxy. Anyone on the network path could then impersonate it; use only for local testing.')}>
                  <input
                    type="checkbox"
                    title={t('Accept self-signed or invalid TLS certificates from the proxy. Anyone on the network path could then impersonate it; use only for local testing.')}
                    checked={settings.remoteControl.allowInvalidCertificates}
                    onChange={(e) =>
                      setSettings({
                        ...settings,
                        remoteControl: {
                          ...settings.remoteControl,
                          allowInvalidCertificates: e.target.checked,
                        },
                      })
                    }
                  />
                  <span>{t('Allow Invalid Certificates')}</span>
                </label>
              </div>
            </div>
          {settings.remoteControl.enabled && (
            <div className="settings-config-block tunnel-status-card">
              <div className="settings-config-header tunnel-status-card-header">
                <span className="text-muted" title={t('Live state of the tunnel to Armada.Proxy, as last reported by the Admiral.')}>{t('Tunnel Status')}</span>
                <span
                  className="mono"
                  style={{ display: 'inline-flex', alignItems: 'center', gap: '0.5rem' }}
                >
                  <span className={`status-dot ${remoteTunnelIndicator.dotClass}`} />
                  <span>{t(remoteTunnelIndicator.labelKey)}</span>
                </span>
              </div>
            </div>
          )}
              {health?.remoteTunnel?.lastError && (
              <div className="alert alert-error" style={{ marginTop: '0.75rem' }}>
                {health.remoteTunnel.lastError}
              </div>
          )}
            <button
              className="btn-primary btn-sm"
              onClick={handleSaveRemoteControlSettings}
              title={t('Save the tunnel settings. The Admiral reconnects to the proxy with the new values right away.')}
            >
              {t('Save Remote Control Settings')}
            </button>
          </fieldset>
        </div>
      )}

      {/* MCP Configuration */}
      {settings && (
        <div className="settings-section" style={{ marginTop: '1.5rem' }}>
          <h3>{t('MCP Configuration')}</h3>
          <p className="text-muted" style={{ marginBottom: '0.75rem' }}>
            {t('Client-specific MCP references for Claude, Codex, Gemini, and Cursor.')}
          </p>
          {remoteProxyMode ? (
            <div className="alert alert-warning">
              {t('MCP bootstrap commands are only valid when connected directly to an Armada server origin. Armada.Proxy does not relay the MCP endpoint.')}
            </div>
          ) : MCP_CLIENTS.map((client) => (
            <div key={client.key} className="settings-config-block" style={{ marginTop: '0.75rem' }}>
              <div className="settings-config-header">
                <div>
                  <div>{client.title}</div>
                  <div className="text-muted mono" style={{ marginTop: '0.2rem' }}>
                    {client.location}
                  </div>
                </div>
              </div>
              <div className="settings-config-block" style={{ margin: '0.75rem' }}>
                <div className="settings-config-header">
                  <span className="text-muted" title={t('Config for clients that connect to the running Admiral over HTTP. The Admiral must be reachable at this URL.')}>HTTP</span>
                  <button
                    className="btn-sm"
                    onClick={() => copyAndToast(getMcpConfigHttp(client.key))}
                    title={t('Copy the {{title}} HTTP setup. Use it where shown above (a config file, or a command to run) so {{title}} can use Armada tools.', { title: client.title })}
                  >
                    {t('Copy')}
                  </button>
                </div>
                <pre className="settings-config-code">{getMcpConfigHttp(client.key)}</pre>
              </div>
              <div className="settings-config-block" style={{ margin: '0 0.75rem 0.75rem' }}>
                <div className="settings-config-header">
                  <span className="text-muted" title={t('Config for clients that launch Armada as a local process and talk over standard input and output. Needs the armada CLI installed on that machine.')}>STDIO</span>
                  <button
                    className="btn-sm"
                    onClick={() => copyAndToast(getMcpConfigStdio(client.key))}
                    title={t('Copy the {{title}} STDIO setup. Use it where shown above (a config file, or a command to run) so {{title}} can use Armada tools.', { title: client.title })}
                  >
                    {t('Copy')}
                  </button>
                </div>
                <pre className="settings-config-code">{getMcpConfigStdio(client.key)}</pre>
              </div>
            </div>
          ))}
        </div>
      )}

      {/* System Paths */}
      {settings && (
        <div className="settings-section" style={{ marginTop: '1.5rem' }}>
          <h3 title={t('Where the Admiral stores its files on the server. Read-only here; change them in settings.json and restart the Admiral.')}>{t('System Paths')}</h3>
          <div className="detail-grid">
            <div className="detail-field">
              <span className="detail-label" title={t('Root folder for Armada data on the Admiral host. The other paths default to folders inside it.')}>{t('Data Directory')}</span>
              <span className="id-display">
                <span className="mono">{settings.dataDirectory || '-'}</span>
                {settings.dataDirectory && <CopyButton text={settings.dataDirectory} title={t('Copy path')} />}
              </span>
            </div>
            <div className="detail-field">
              <span className="detail-label" title={t('SQLite database file the Admiral uses when the database provider is SQLite.')}>{t('Database Path')}</span>
              <span className="id-display">
                <span className="mono">{settings.databasePath || '-'}</span>
                {settings.databasePath && <CopyButton text={settings.databasePath} title={t('Copy path')} />}
              </span>
            </div>
            <div className="detail-field">
              <span className="detail-label" title={t('Folder for Admiral and captain log files.')}>{t('Log Directory')}</span>
              <span className="id-display">
                <span className="mono">{settings.logDirectory || '-'}</span>
                {settings.logDirectory && <CopyButton text={settings.logDirectory} title={t('Copy path')} />}
              </span>
            </div>
            <div className="detail-field">
              <span className="detail-label" title={t('Folder where git worktrees (docks) are created for captains to work in, one per mission.')}>{t('Docks Directory')}</span>
              <span className="id-display">
                <span className="mono">{settings.docksDirectory || '-'}</span>
                {settings.docksDirectory && <CopyButton text={settings.docksDirectory} title={t('Copy path')} />}
              </span>
            </div>
            <div className="detail-field">
              <span className="detail-label" title={t('Folder holding the bare clones of each vessel repository that docks are created from.')}>{t('Repos Directory')}</span>
              <span className="id-display">
                <span className="mono">{settings.reposDirectory || '-'}</span>
                {settings.reposDirectory && <CopyButton text={settings.reposDirectory} title={t('Copy path')} />}
              </span>
            </div>
          </div>
        </div>
      )}

      {isAdmin && settings && (
        <div className="settings-section" style={{ marginTop: '1.5rem' }}>
          <h3>{t('Database Backup')}</h3>
          {remoteProxyMode && (
            <div className="alert alert-warning" style={{ marginBottom: '0.75rem' }}>
              {t('Backup download remains available through the proxy relay, but restore is blocked remotely by proxy policy.')}
            </div>
          )}
          <div className="settings-actions">
            <button
              className="btn btn-sm"
              onClick={handleBackup}
              disabled={backupLoading}
              title={t('Download a ZIP with a snapshot of the database and settings.json. The file includes secrets such as tokens, so store it safely.')}
            >
              {backupLoading ? t('Backing up...') : t('Backup Now')}
            </button>
            <button
              className="btn btn-danger btn-sm"
              onClick={handleRestoreClick}
              disabled={remoteProxyMode}
              title={remoteProxyMode ? t('Restore from Backup is blocked in proxy mode') : t('Replace the current database and settings with a backup ZIP you choose. Data written since that backup is lost.')}
            >
              {t('Restore from Backup')}
            </button>
            <input
              type="file"
              ref={restoreFileRef}
              accept=".zip"
              style={{ display: 'none' }}
              onChange={handleRestoreFile}
            />
          </div>
        </div>
      )}

      {isAdmin && (
        <div className="settings-section" style={{ marginTop: '1.5rem' }}>
          <h3>{t('Server Actions')}</h3>
          {remoteProxyMode && (
            <div className="alert alert-warning" style={{ marginBottom: '0.75rem' }}>
              {t('Setup, shutdown, and factory reset are local-only server actions and are disabled when this dashboard is opened through Armada.Proxy.')}
            </div>
          )}
          <div className="settings-actions">
            <button
              type="button"
              className="btn btn-sm"
              disabled={remoteProxyMode}
              onClick={() => {
                window.dispatchEvent(new CustomEvent('armada:open-setup-wizard'));
              }}
              title={remoteProxyMode ? t('Setup Wizard is only available when connected directly to Armada.Server') : t('Reopen the first-run guide that walks through adding a fleet, vessels, and captains. Changes nothing until you complete a step.')}
            >
              {t('Setup Wizard')}
            </button>
            <button
              className="btn btn-sm"
              onClick={handleHealthCheck}
              title={t('Ask the Admiral for its health now and show the result. Read-only.')}
            >
              {t('Health Check')}
            </button>
            <button
              className="btn btn-sm"
              disabled={remoteProxyMode}
              onClick={handleRestartServer}
              title={remoteProxyMode ? t('Restart Server is blocked in proxy mode') : t('Start a replacement Admiral process and stop this one; the dashboard and API are unavailable for a moment while it comes back. Asks first.')}
            >
              {t('Restart Server')}
            </button>
            {branches.length > 0 && (
              <select aria-label={t('Branch to build. Choose a branch or type a tag/commit in the box.')}
                value={branches.some((b) => b.name === buildRef) ? buildRef : ''}
                onChange={(e) => setBuildRef(e.target.value)}
                disabled={remoteProxyMode}
                title={t('Branch of the source vessel to build. current HEAD builds whatever is checked out; for a tag or commit, type it in the box next to this.')}
              >
                <option value="">{t('current HEAD')}</option>
                {branches.map((b) => (
                  <option key={b.name} value={b.name}>
                    {b.name}{b.isDefault ? t(' (default)') : ''}
                  </option>
                ))}
              </select>
            )}
            <input aria-label={t('Branch, tag, or commit to build. Leave blank to build the current HEAD.')}
              type="text"
              value={buildRef}
              onChange={(e) => setBuildRef(e.target.value)}
              placeholder={t('ref (blank = HEAD)')}
              disabled={remoteProxyMode}
              style={{ width: 160 }}
              title={t('Git ref to build, for example main, v1.2.0, or a commit hash. Blank builds the current HEAD of the source vessel.')}
            />
            <button
              className="btn btn-sm"
              disabled={remoteProxyMode}
              onClick={handleRebuild}
              title={remoteProxyMode ? t('Rebuild Armada is blocked in proxy mode') : t('Build the Admiral from the source vessel at the chosen ref into a new slot, back up the database, then switch over. A failed build leaves the server running. Asks first.')}
            >
              {t('Rebuild Armada')}
            </button>
            {rebuild && rebuild.status != null && (
              <button
                className="btn btn-sm"
                onClick={() => { setRebuildLogOpen(true); refreshRebuildStatus(); }}
                title={t('Open the output of the latest rebuild, including build errors.')}
              >
                {t('Build Log')}
              </button>
            )}
            {rebuild && rebuild.status === 'Succeeded' && rebuild.previousSlot && (
              <button
                className="btn btn-danger btn-sm"
                disabled={remoteProxyMode}
                onClick={handleRollback}
                title={remoteProxyMode ? t('Rollback is blocked in proxy mode') : t('Go back to the previous build and restart. If the rebuild changed the database schema, the pre-rebuild backup is restored and newer data is lost. Asks first.')}
              >
                {t('Roll Back')}
              </button>
            )}
            <button
              className="btn btn-danger btn-sm"
              disabled={remoteProxyMode}
              onClick={handleStopServer}
              title={remoteProxyMode ? t('Stop Server is blocked in proxy mode') : t('Shut down the Admiral. Captains stop, and the dashboard and API stay offline until someone starts it again on the host. Asks first.')}
            >
              {t('Stop Server')}
            </button>
            <button
              className="btn btn-danger btn-sm"
              disabled={remoteProxyMode}
              onClick={handleFactoryReset}
              title={remoteProxyMode ? t('Factory Reset is blocked in proxy mode') : t('Permanently delete the database, logs, docks, and repo clones. Settings are kept. Cannot be undone; download a backup first. Asks first.')}
            >
              {t('Factory Reset')}
            </button>
          </div>
        </div>
      )}

      {/* Rebuild build log */}
      <LogViewer
        open={rebuildLogOpen}
        title={t('Rebuild Armada{{slot}}', { slot: rebuild?.slot ? ' - ' + rebuild.slot : '' })}
        content={rebuild?.log || ''}
        completed={rebuildTerminal(rebuild) || rebuild?.status === 'CuttingOver'}
        onClose={() => setRebuildLogOpen(false)}
        onRefresh={refreshRebuildStatus}
      />

      {/* Confirm Dialog */}
      <ConfirmDialog
        open={confirmDialog.open}
        title={confirmDialog.title}
        message={confirmDialog.message}
        onConfirm={confirmDialog.onConfirm}
        onCancel={closeConfirmDialog}
      />
    </div>
  );
}
