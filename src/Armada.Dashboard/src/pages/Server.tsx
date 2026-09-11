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
  downloadBackup,
  restoreBackup,
  getProxySessionContext,
  type ProxySessionContext,
  type RebuildStatus,
  type BranchInfo,
} from '../api/client';
import LogViewer from '../components/shared/LogViewer';
import RefreshButton from '../components/shared/RefreshButton';
import AutoRefreshSelect from '../components/shared/AutoRefreshSelect';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import PageHeader from '../components/shared/PageHeader';
import { useWebSocket } from '../context/WebSocketContext';
import { useNotifications, type Severity } from '../context/NotificationContext';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import CopyButton, { copyToClipboard } from '../components/shared/CopyButton';
import { useAuth } from '../context/AuthContext';
import { useLocale } from '../context/LocaleContext';
import ErrorModal from '../components/shared/ErrorModal';

interface HealthInfo {
  status: string;
  timestamp: string;
  startUtc: string;
  uptime: string;
  version: string;
  ports: {
    admiral: number;
    mcp: number;
    webSocket: number;
  };
  remoteTunnel?: RemoteTunnelStatus;
}

interface RemoteTunnelCapabilityManifest {
  protocolVersion: string;
  armadaVersion: string;
  features: string[];
}

interface RemoteTunnelStatus {
  enabled: boolean;
  state: string;
  tunnelUrl: string | null;
  instanceId: string | null;
  lastConnectAttemptUtc: string | null;
  connectedUtc: string | null;
  lastHeartbeatUtc: string | null;
  lastDisconnectUtc: string | null;
  lastError: string | null;
  reconnectAttempts: number;
  latencyMs: number | null;
  capabilityManifest: RemoteTunnelCapabilityManifest;
}

interface RemoteControlSettings {
  enabled: boolean;
  tunnelUrl: string | null;
  instanceId: string | null;
  enrollmentToken: string | null;
  password: string | null;
  connectTimeoutSeconds: number;
  heartbeatIntervalSeconds: number;
  reconnectBaseDelaySeconds: number;
  reconnectMaxDelaySeconds: number;
  allowInvalidCertificates: boolean;
}

interface ServerSettings {
  admiralPort: number;
  mcpPort: number;
  maxCaptains: number;
  heartbeatIntervalSeconds: number;
  stallThresholdMinutes: number;
  idleCaptainTimeoutSeconds: number;
  planningSessionInactivityTimeoutMinutes: number;
  planningSessionAbandonmentTimeoutMinutes: number;
  planningSessionRetentionDays: number;
  autoCreatePr: boolean;
  dataDirectory: string;
  databasePath: string;
  logDirectory: string;
  docksDirectory: string;
  reposDirectory: string;
  selfVesselId?: string | null;
  rebuildSlotRetentionCount?: number;
  remoteControl: RemoteControlSettings;
}

type McpClientKey = 'claude' | 'codex' | 'gemini' | 'cursor';

interface McpClientReference {
  key: McpClientKey;
  title: string;
  location: string;
}

type RemoteSecretField = 'enrollmentToken' | 'password';

const DEFAULT_REMOTE_TUNNEL_URL = 'http://proxy.armadago.ai:7893/tunnel';

const MCP_CLIENTS: McpClientReference[] = [
  { key: 'claude', title: 'Claude Code', location: '~/.claude.json -> mcpServers.armada' },
  { key: 'codex', title: 'Codex', location: '~/.codex/config.json -> mcpServers.armada' },
  { key: 'gemini', title: 'Gemini', location: '~/.gemini/settings.json -> mcpServers.armada' },
  { key: 'cursor', title: 'Cursor', location: '.cursor/mcp.json -> mcpServers.armada' },
];

function getDefaultRemoteControlSettings(): RemoteControlSettings {
  return {
    enabled: false,
    tunnelUrl: DEFAULT_REMOTE_TUNNEL_URL,
    instanceId: null,
    enrollmentToken: null,
    password: 'armadaadmin',
    connectTimeoutSeconds: 15,
    heartbeatIntervalSeconds: 30,
    reconnectBaseDelaySeconds: 5,
    reconnectMaxDelaySeconds: 60,
    allowInvalidCertificates: false,
  };
}

function mergeServerSettings(data: ServerSettings | null): ServerSettings | null {
  if (!data) return null;
  return {
    ...data,
    remoteControl: {
      ...getDefaultRemoteControlSettings(),
      ...(data.remoteControl ?? {}),
    },
  };
}

function getRemoteTunnelIndicator(
  enabled: boolean,
  state: string | null | undefined,
): { labelKey: string; dotClass: 'connected' | 'warning' | 'disconnected' } {
  if (!enabled) {
    return { labelKey: 'Disabled', dotClass: 'disconnected' };
  }

  switch ((state || '').toLowerCase()) {
    case 'connected':
      return { labelKey: 'Connected', dotClass: 'connected' };
    case 'connecting':
      return { labelKey: 'Connecting', dotClass: 'warning' };
    case 'stopping':
      return { labelKey: 'Stopping', dotClass: 'warning' };
    case 'error':
      return { labelKey: 'Error', dotClass: 'disconnected' };
    case 'disconnected':
      return { labelKey: 'Disconnected', dotClass: 'disconnected' };
    default:
      return { labelKey: 'Checking status', dotClass: 'warning' };
  }
}

export default function Server() {
  const { isAdmin } = useAuth();
  const { connected } = useWebSocket();
  const { pushToast } = useNotifications();
  const { t, formatDateTime } = useLocale();

  const [health, setHealth] = useState<HealthInfo | null>(null);
  const [settings, setSettings] = useState<ServerSettings | null>(null);
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
  const [rebuildLogOpen, setRebuildLogOpen] = useState(false);
  const [rebuild, setRebuild] = useState<RebuildStatus | null>(null);
  const rebuildPollRef = useRef<number | null>(null);

  const rebuildTerminal = (s?: RebuildStatus | null) =>
    !!s && (s.status === 'Succeeded' || s.status === 'Failed' || s.status === 'RolledBack');

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
    switch ((status || '').toLowerCase()) {
      case 'healthy':
        return t('Healthy');
      case 'degraded':
        return t('Degraded');
      case 'unhealthy':
        return t('Unhealthy');
      case 'error':
        return t('Error');
      case 'unknown':
        return t('Unknown');
      default:
        return status || '-';
    }
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

  const loadData = useCallback(async () => {
    try {
      setError('');
      const [h, s, proxy] = await Promise.all([
        getHealth().catch(() => null),
        getSettings().catch(() => null),
        getProxySessionContext().catch(() => null),
      ]);
      if (h) setHealth(h as unknown as HealthInfo);
      if (s) setSettings(mergeServerSettings(s as unknown as ServerSettings));
      setProxyContext(proxy);
      if (!h && !s) setError(t('Failed to load server data.'));
      else if (!s) setError(t('Failed to load server settings. Health data is available, but configuration and backup sections could not be loaded.'));
    } catch {
      setError(t('Failed to load server data.'));
    } finally {
      setLoading(false);
    }
  }, [t]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('server', loadData);

  const handleSaveServerConfig = async () => {
    if (!settings) return;
    try {
      const updated = await updateSettings({
        admiralPort: settings.admiralPort,
        mcpPort: settings.mcpPort,
        maxCaptains: settings.maxCaptains,
      });
      setSettings(mergeServerSettings(updated as unknown as ServerSettings));
      showToast('success', t('Server configuration saved'));
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : t('Unknown error');
      showToast('error', t('Failed: {{message}}', { message: msg }));
    }
  };

  const handleSaveRebuildSettings = async () => {
    if (!settings) return;
    try {
      const updated = await updateSettings({
        selfVesselId: settings.selfVesselId ?? '',
        rebuildSlotRetentionCount: settings.rebuildSlotRetentionCount ?? 3,
      });
      setSettings(mergeServerSettings(updated as unknown as ServerSettings));
      showToast('success', t('Rebuild settings saved'));
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : t('Unknown error');
      showToast('error', t('Failed: {{message}}', { message: msg }));
    }
  };

  const handleSaveAgentSettings = async () => {
    if (!settings) return;
    try {
      const updated = await updateSettings({
        heartbeatIntervalSeconds: settings.heartbeatIntervalSeconds,
        stallThresholdMinutes: settings.stallThresholdMinutes,
        idleCaptainTimeoutSeconds: settings.idleCaptainTimeoutSeconds,
        autoCreatePr: settings.autoCreatePr,
      });
      setSettings(mergeServerSettings(updated as unknown as ServerSettings));
      showToast('success', t('Agent settings saved'));
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : t('Unknown error');
      showToast('error', t('Failed: {{message}}', { message: msg }));
    }
  };

  const handleSavePlanningSessionSettings = async () => {
    if (!settings) return;
    try {
      const updated = await updateSettings({
        planningSessionInactivityTimeoutMinutes: settings.planningSessionInactivityTimeoutMinutes,
        planningSessionAbandonmentTimeoutMinutes: settings.planningSessionAbandonmentTimeoutMinutes,
        planningSessionRetentionDays: settings.planningSessionRetentionDays,
      });
      setSettings(mergeServerSettings(updated as unknown as ServerSettings));
      showToast('success', t('Planning session settings saved'));
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : t('Unknown error');
      showToast('error', t('Failed: {{message}}', { message: msg }));
    }
  };

  const handleSaveRemoteControlSettings = async () => {
    if (!settings) return;
    try {
      const updated = await updateSettings({
        remoteControl: settings.remoteControl,
      });
      setSettings(mergeServerSettings(updated as unknown as ServerSettings));
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

  const getMcpRpcUrl = (): string => {
    const port = health?.ports?.mcp || settings?.mcpPort || 7891;
    return `http://localhost:${port}/rpc`;
  };

  const getMcpConfigHttp = (client: McpClientKey): string => {
    const rpcUrl = getMcpRpcUrl();

    switch (client) {
      case 'claude':
      case 'codex':
        return JSON.stringify(
          {
            mcpServers: {
              armada: {
                type: 'http',
                url: rpcUrl,
              },
            },
          },
          null,
          2,
        );
      case 'gemini':
        return JSON.stringify(
          {
            mcpServers: {
              armada: {
                httpUrl: rpcUrl,
              },
            },
          },
          null,
          2,
        );
      case 'cursor':
        return JSON.stringify(
          {
            mcpServers: {
              armada: {
                url: rpcUrl,
              },
            },
          },
          null,
          2,
        );
    }
  };

  const getMcpConfigStdio = (client: McpClientKey): string => {
    switch (client) {
      case 'claude':
        return 'claude mcp add --scope user armada -- armada mcp stdio';
      case 'cursor':
        return JSON.stringify(
          {
            mcpServers: {
              armada: {
                command: 'armada',
                args: ['mcp', 'stdio'],
              },
            },
          },
          null,
          2,
        );
      case 'codex':
      case 'gemini':
        return JSON.stringify(
          {
            mcpServers: {
              armada: {
                type: 'stdio',
                command: 'armada',
                args: ['mcp', 'stdio'],
              },
            },
          },
          null,
          2,
        );
    }
  };

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
      help: 'Currently running Armada server build version.',
      value: health?.version || '-',
    },
    {
      key: 'api-url',
      label: 'API URL',
      help: 'Base URL of the Armada REST API serving this dashboard.',
      value: window.location.origin,
    },
    {
      key: 'admiral-port',
      label: 'Admiral Port',
      help: 'REST API port currently serving the Armada dashboard and API.',
      value: String(health?.ports?.admiral || window.location.port || '-'),
    },
    {
      key: 'mcp-port',
      label: 'MCP Port',
      help: 'Port currently serving the Armada MCP HTTP endpoint.',
      value: String(health?.ports?.mcp || '-'),
    },
    {
      key: 'websocket-port',
      label: 'WebSocket Port',
      help: 'Port currently serving the Armada WebSocket endpoint.',
      value: String(health?.ports?.webSocket || '-'),
    },
    {
      key: 'tunnel-state',
      label: 'Tunnel State',
      help: 'Current outbound tunnel connection state reported by the server.',
      value: translateRemoteTunnelState(remoteTunnelEnabled, health?.remoteTunnel?.state),
    },
    {
      key: 'tunnel-instance',
      label: 'Tunnel Instance',
      help: 'Remote tunnel instance identifier currently advertised to Armada.Proxy.',
      value: health?.remoteTunnel?.instanceId || '-',
    },
    {
      key: 'tunnel-latency',
      label: 'Tunnel Latency',
      help: 'Most recent measured round-trip latency for the remote tunnel.',
      value: health?.remoteTunnel?.latencyMs != null ? `${health.remoteTunnel.latencyMs.toLocaleString()} ms` : '-',
    },
    {
      key: 'tunnel-last-heartbeat',
      label: 'Tunnel Last Heartbeat',
      help: 'Timestamp of the most recent remote tunnel heartbeat.',
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
            <RefreshButton onRefresh={loadData} title={t('Refresh server data')} />
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
        <div className="card" title={t('Current health status of the admiral server')}>
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

        <div className="card" title={t('How long the server has been running')}>
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

        <div className="card" title={t('Current connection method to the server')}>
          <div className="card-label">{t('Connection')}</div>
          <div className="card-value" style={{ fontSize: '1.2rem' }}>
            <span
              className={`status-dot ${connected ? 'connected' : 'disconnected'}`}
              style={{ display: 'inline-block', verticalAlign: 'middle', marginRight: '0.5rem' }}
            />
            <span>{t(connected ? 'Live (WebSocket)' : 'Online (HTTP)')}</span>
          </div>
        </div>

        <div className="card" title={t('Current outbound remote-control tunnel status')}>
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
                <label title={t('REST API port used by the Armada server and dashboard.')}>{t('Admiral Port')}</label>
                <input
                  type="number"
                  value={settings.admiralPort}
                  onChange={(e) =>
                    setSettings({ ...settings, admiralPort: parseInt(e.target.value) || 0 })
                  }
                  min={1}
                  max={65535}
                  title={t('REST API port (1-65535)')}
                />
              </div>
              <div className="form-group">
                <label title={t('Port used by the Armada MCP HTTP endpoint.')}>{t('MCP Port')}</label>
                <input
                  type="number"
                  value={settings.mcpPort}
                  onChange={(e) =>
                    setSettings({ ...settings, mcpPort: parseInt(e.target.value) || 0 })
                  }
                  min={1}
                  max={65535}
                  title={t('MCP server port (1-65535)')}
                />
              </div>
              <div className="form-group">
                <label title={t('Maximum number of captains Armada may keep registered at once. Set to 0 for no fixed limit.')}>{t('Max Captains')}</label>
                <input
                  type="number"
                  value={settings.maxCaptains}
                  onChange={(e) =>
                    setSettings({ ...settings, maxCaptains: parseInt(e.target.value) || 0 })
                  }
                  min={0}
                  title={t('Maximum captains (0 = unlimited)')}
                />
              </div>
            </div>
            <button
              className="btn-primary btn-sm"
              onClick={handleSaveServerConfig}
              title={t('Save server configuration changes')}
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
          <p className="settings-hint">
            {t('Designate the vessel that holds Armada\'s own source so the "Rebuild Armada" button knows what to build. See docs/SERVER_REBUILD.md.')}
          </p>
          <fieldset disabled={remoteSettingsLocked} style={{ border: 'none', margin: 0, padding: 0 }}>
            <div className="settings-grid">
              <div className="form-group">
                <label title={t('Identifier (vsl_ prefix) of the vessel holding Armada source. Leave blank to disable rebuild.')}>{t('Self Vessel ID')}</label>
                <input
                  type="text"
                  value={settings.selfVesselId ?? ''}
                  onChange={(e) => setSettings({ ...settings, selfVesselId: e.target.value })}
                  placeholder="vsl_..."
                  title={t('Identifier of the vessel that holds Armada source')}
                />
              </div>
              <div className="form-group">
                <label title={t('Number of published build slots to keep on disk for rollback.')}>{t('Slot Retention')}</label>
                <input
                  type="number"
                  value={settings.rebuildSlotRetentionCount ?? 3}
                  onChange={(e) => setSettings({ ...settings, rebuildSlotRetentionCount: parseInt(e.target.value) || 1 })}
                  min={1}
                  title={t('Number of build slots to retain (minimum 1)')}
                />
              </div>
            </div>
            <button
              className="btn-primary btn-sm"
              onClick={handleSaveRebuildSettings}
              title={t('Save rebuild settings')}
            >
              {t('Save Rebuild Settings')}
            </button>
          </fieldset>
        </div>
      )}

      {/* Agent Settings */}
      {settings && (
        <div className="settings-section" style={{ marginTop: '1.5rem' }}>
          <h3 title={t('Settings that control captain monitoring, stalling, cleanup, and pull-request automation.')}>{t('Agent Settings')}</h3>
          <fieldset disabled={remoteSettingsLocked} style={{ border: 'none', margin: 0, padding: 0 }}>
            <div className="settings-grid">
              <div className="form-group">
                <label title={t('How often Armada runs its health and dispatch checks.')}>{t('Heartbeat Interval (seconds)')}</label>
                <input
                  type="number"
                  value={settings.heartbeatIntervalSeconds}
                  onChange={(e) =>
                    setSettings({
                      ...settings,
                      heartbeatIntervalSeconds: parseInt(e.target.value) || 5,
                    })
                  }
                  min={5}
                  title={t('Health check interval, minimum 5 seconds')}
                />
              </div>
              <div className="form-group">
                <label title={t('How long a captain may go without meaningful progress before Armada considers it stalled.')}>{t('Stall Threshold (minutes)')}</label>
                <input
                  type="number"
                  value={settings.stallThresholdMinutes}
                  onChange={(e) =>
                    setSettings({
                      ...settings,
                      stallThresholdMinutes: parseInt(e.target.value) || 1,
                    })
                  }
                  min={1}
                  title={t('Minutes before a captain is considered stalled')}
                />
              </div>
              <div className="form-group">
                <label title={t('How long an idle captain may sit unused before Armada automatically removes it. Set to 0 to disable auto-removal.')}>{t('Idle Captain Timeout (seconds)')}</label>
                <input
                  type="number"
                  value={settings.idleCaptainTimeoutSeconds}
                  onChange={(e) =>
                    setSettings({
                      ...settings,
                      idleCaptainTimeoutSeconds: parseInt(e.target.value) || 0,
                    })
                  }
                  min={0}
                  title={t('Auto-remove idle captains after this many seconds (0 = disabled)')}
                />
              </div>
              <div className="form-group">
                <label className="settings-checkbox-label" title={t('Automatically open pull requests when supported by the mission and vessel configuration.')}>
                  <input
                    type="checkbox"
                    checked={settings.autoCreatePr}
                    onChange={(e) => setSettings({ ...settings, autoCreatePr: e.target.checked })}
                  />
                  <span>{t('Auto-Create Pull Requests')}</span>
                </label>
              </div>
            </div>
            <button
              className="btn-primary btn-sm"
              onClick={handleSaveAgentSettings}
              title={t('Save agent settings changes')}
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
                <label title={t('How long an idle planning session may sit with no running process before Armada automatically ends it.')}>{t('Idle Session Timeout (minutes)')}</label>
                <input
                  type="number"
                  value={settings.planningSessionInactivityTimeoutMinutes}
                  onChange={(e) =>
                    setSettings({
                      ...settings,
                      planningSessionInactivityTimeoutMinutes: parseInt(e.target.value) || 0,
                    })
                  }
                  min={0}
                  title={t('Minutes before an idle planning session is automatically ended (0 = disabled)')}
                />
              </div>
              <div className="form-group">
                <label title={t('Safety-net timeout for stale planning sessions with no running process, even when the normal idle timeout is disabled.')}>{t('Abandonment Timeout (minutes)')}</label>
                <input
                  type="number"
                  value={settings.planningSessionAbandonmentTimeoutMinutes}
                  onChange={(e) =>
                    setSettings({
                      ...settings,
                      planningSessionAbandonmentTimeoutMinutes: parseInt(e.target.value) || 0,
                    })
                  }
                  min={0}
                  title={t('Minutes before a stale planning session is force-ended (0 = disabled)')}
                />
              </div>
              <div className="form-group">
                <label title={t('How long ended or failed planning sessions are retained before Armada deletes them automatically.')}>{t('Transcript Retention (days)')}</label>
                <input
                  type="number"
                  value={settings.planningSessionRetentionDays}
                  onChange={(e) =>
                    setSettings({
                      ...settings,
                      planningSessionRetentionDays: parseInt(e.target.value) || 0,
                    })
                  }
                  min={0}
                  title={t('Days to keep stopped or failed planning sessions before deleting them (0 = disabled)')}
                />
              </div>
            </div>
            <button
              className="btn-primary btn-sm"
              onClick={handleSavePlanningSessionSettings}
              title={t('Save planning session lifecycle settings')}
            >
              {t('Save Planning Session Settings')}
            </button>
          </fieldset>
        </div>
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
                <label className="settings-checkbox-label" title={t('Open an outbound remote-management tunnel from this Armada instance to Armada.Proxy.')}>
                  <input
                    type="checkbox"
                    checked={settings.remoteControl.enabled}
                    onChange={(e) => handleRemoteTunnelEnabledChange(e.target.checked)}
                  />
                  <span>{t('Enable Remote Tunnel')}</span>
                </label>
              </div>
              <div className="form-group">
                <label title={t('Armada.Proxy base URL or explicit /tunnel endpoint used for remote management.')}>{t('Tunnel URL')}</label>
                <input
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
                  title={t('Proxy base URL or tunnel endpoint. http/https will be normalized to ws/wss and /tunnel will be added automatically when needed.')}
                />
              </div>
              <div className="form-group">
                <label title={t('Optional stable deployment identifier advertised to Armada.Proxy. Leave blank to let Armada derive one automatically.')}>{t('Instance ID Override')}</label>
                <input
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
                  title={t('Optional stable deployment identifier advertised to Armada.Proxy. Leave blank to let Armada derive one automatically.')}
                />
              </div>
              <div className="form-group">
                <label title={t('Optional extra admission token used only when Armada.Proxy requires instance enrollment tokens.')}>{t('Instance Enrollment Token')}</label>
                <div className="settings-secret-field">
                  <input
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
                    title={t('Optional extra admission token used only when Armada.Proxy requires instance enrollment tokens.')}
                  />
                  <button
                    type="button"
                    className="settings-secret-toggle"
                    aria-label={t(revealedRemoteField === 'enrollmentToken' ? 'Hide enrollment token' : 'Show enrollment token')}
                    title={t(revealedRemoteField === 'enrollmentToken' ? 'Hide enrollment token' : 'Show enrollment token')}
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
                <label title={t('Shared secret used to authenticate this Armada instance to Armada.Proxy and to unlock Armada.Proxy browser access.')}>{t('Proxy Shared Password')}</label>
                <div className="settings-secret-field">
                  <input
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
                    title={t('Shared secret used to authenticate this Armada instance to Armada.Proxy and to unlock Armada.Proxy browser access.')}
                  />
                  <button
                    type="button"
                    className="settings-secret-toggle"
                    aria-label={t(revealedRemoteField === 'password' ? 'Hide shared password' : 'Show shared password')}
                    title={t(revealedRemoteField === 'password' ? 'Hide shared password' : 'Show shared password')}
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
                <label title={t('How long Armada waits for the proxy tunnel connection to open before treating the attempt as failed.')}>{t('Connect Timeout (seconds)')}</label>
                <input
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
                  title={t('How long Armada waits for the proxy tunnel connection to open before treating the attempt as failed.')}
                />
              </div>
              <div className="form-group">
                <label title={t('How often Armada sends tunnel heartbeats to keep the connection alive and measure latency.')}>{t('Heartbeat Interval (seconds)')}</label>
                <input
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
                  title={t('How often Armada sends tunnel heartbeats to keep the connection alive and measure latency.')}
                />
              </div>
              <div className="form-group">
                <label title={t('Initial reconnect backoff after a tunnel failure. Later retries grow from this base delay.')}>{t('Reconnect Base Delay (seconds)')}</label>
                <input
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
                  title={t('Initial reconnect backoff after a tunnel failure. Later retries grow from this base delay.')}
                />
              </div>
              <div className="form-group">
                <label title={t('Maximum reconnect backoff between tunnel retry attempts.')}>{t('Reconnect Max Delay (seconds)')}</label>
                <input
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
                  title={t('Maximum reconnect backoff between tunnel retry attempts.')}
                />
              </div>
              <div className="form-group">
                <label className="settings-checkbox-label" title={t('Allow self-signed or otherwise invalid TLS certificates for https/wss tunnel endpoints. Use only in trusted environments.')}>
                  <input
                    type="checkbox"
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
                <span className="text-muted">{t('Tunnel Status')}</span>
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
              title={t('Save remote-control tunnel configuration')}
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
                  <span className="text-muted">HTTP</span>
                  <button
                    className="btn-sm"
                    onClick={() => copyAndToast(getMcpConfigHttp(client.key))}
                    title={t('Copy {{title}} HTTP config to clipboard', { title: client.title })}
                  >
                    {t('Copy')}
                  </button>
                </div>
                <pre className="settings-config-code">{getMcpConfigHttp(client.key)}</pre>
              </div>
              <div className="settings-config-block" style={{ margin: '0 0.75rem 0.75rem' }}>
                <div className="settings-config-header">
                  <span className="text-muted">STDIO</span>
                  <button
                    className="btn-sm"
                    onClick={() => copyAndToast(getMcpConfigStdio(client.key))}
                    title={t('Copy {{title}} STDIO config to clipboard', { title: client.title })}
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
          <h3>{t('System Paths')}</h3>
          <div className="detail-grid">
            <div className="detail-field">
              <span className="detail-label" title={t('Path to the Armada data directory.')}>{t('Data Directory')}</span>
              <span className="id-display">
                <span className="mono">{settings.dataDirectory || '-'}</span>
                {settings.dataDirectory && <CopyButton text={settings.dataDirectory} title={t('Copy path')} />}
              </span>
            </div>
            <div className="detail-field">
              <span className="detail-label" title={t('Path to the Armada SQLite database file.')}>{t('Database Path')}</span>
              <span className="id-display">
                <span className="mono">{settings.databasePath || '-'}</span>
                {settings.databasePath && <CopyButton text={settings.databasePath} title={t('Copy path')} />}
              </span>
            </div>
            <div className="detail-field">
              <span className="detail-label" title={t('Path where Armada writes server and captain logs.')}>{t('Log Directory')}</span>
              <span className="id-display">
                <span className="mono">{settings.logDirectory || '-'}</span>
                {settings.logDirectory && <CopyButton text={settings.logDirectory} title={t('Copy path')} />}
              </span>
            </div>
            <div className="detail-field">
              <span className="detail-label" title={t('Path where Armada stores captain worktrees and docks.')}>{t('Docks Directory')}</span>
              <span className="id-display">
                <span className="mono">{settings.docksDirectory || '-'}</span>
                {settings.docksDirectory && <CopyButton text={settings.docksDirectory} title={t('Copy path')} />}
              </span>
            </div>
            <div className="detail-field">
              <span className="detail-label" title={t('Path where Armada stores cloned repositories.')}>{t('Repos Directory')}</span>
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
              title={t('Create a backup ZIP of the database and download it')}
            >
              {backupLoading ? t('Backing up...') : t('Backup Now')}
            </button>
            <button
              className="btn btn-danger btn-sm"
              onClick={handleRestoreClick}
              disabled={remoteProxyMode}
              title={remoteProxyMode ? t('Restore from Backup is blocked in proxy mode') : t('Restore the database from a backup ZIP file')}
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
              title={remoteProxyMode ? t('Setup Wizard is only available when connected directly to Armada.Server') : t('Re-open the first-run setup guide')}
            >
              {t('Setup Wizard')}
            </button>
            <button
              className="btn btn-sm"
              onClick={handleHealthCheck}
              title={t('Run a health check and display the result')}
            >
              {t('Health Check')}
            </button>
            <button
              className="btn btn-sm"
              disabled={remoteProxyMode}
              onClick={handleRestartServer}
              title={remoteProxyMode ? t('Restart Server is blocked in proxy mode') : t('Restart the admiral server process')}
            >
              {t('Restart Server')}
            </button>
            {branches.length > 0 && (
              <select
                value={branches.some((b) => b.name === buildRef) ? buildRef : ''}
                onChange={(e) => setBuildRef(e.target.value)}
                disabled={remoteProxyMode}
                title={t('Branch to build. Choose a branch or type a tag/commit in the box.')}
              >
                <option value="">{t('current HEAD')}</option>
                {branches.map((b) => (
                  <option key={b.name} value={b.name}>
                    {b.name}{b.isDefault ? t(' (default)') : ''}
                  </option>
                ))}
              </select>
            )}
            <input
              type="text"
              value={buildRef}
              onChange={(e) => setBuildRef(e.target.value)}
              placeholder={t('ref (blank = HEAD)')}
              disabled={remoteProxyMode}
              style={{ width: 160 }}
              title={t('Branch, tag, or commit to build. Leave blank to build the current HEAD.')}
            />
            <button
              className="btn btn-sm"
              disabled={remoteProxyMode}
              onClick={handleRebuild}
              title={remoteProxyMode ? t('Rebuild Armada is blocked in proxy mode') : t('Rebuild the admiral from source and cut over to the new build')}
            >
              {t('Rebuild Armada')}
            </button>
            {rebuild && rebuild.status !== 'none' && (
              <button
                className="btn btn-sm"
                onClick={() => { setRebuildLogOpen(true); refreshRebuildStatus(); }}
                title={t('View the latest rebuild log')}
              >
                {t('Build Log')}
              </button>
            )}
            {rebuild && rebuild.status === 'Succeeded' && rebuild.previousSlot && (
              <button
                className="btn btn-danger btn-sm"
                disabled={remoteProxyMode}
                onClick={handleRollback}
                title={remoteProxyMode ? t('Rollback is blocked in proxy mode') : t('Roll back to the previous build')}
              >
                {t('Roll Back')}
              </button>
            )}
            <button
              className="btn btn-danger btn-sm"
              disabled={remoteProxyMode}
              onClick={handleStopServer}
              title={remoteProxyMode ? t('Stop Server is blocked in proxy mode') : t('Shut down the admiral server process')}
            >
              {t('Stop Server')}
            </button>
            <button
              className="btn btn-danger btn-sm"
              disabled={remoteProxyMode}
              onClick={handleFactoryReset}
              title={remoteProxyMode ? t('Factory Reset is blocked in proxy mode') : t('Delete all data and reset to factory defaults')}
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
