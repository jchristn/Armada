import { useEffect, useState, useCallback, useRef } from 'react';
import {
  getHealth,
  getSettings,
  updateSettings,
  stopServer,
  resetServer,
  downloadBackup,
  restoreBackup,
} from '../api/client';
import RefreshButton from '../components/shared/RefreshButton';
import { useWebSocket } from '../context/WebSocketContext';
import { useNotifications, type Severity } from '../context/NotificationContext';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import CopyButton, { copyToClipboard } from '../components/shared/CopyButton';
import { useAuth } from '../context/AuthContext';
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
  autoCreatePr: boolean;
  dataDirectory: string;
  databasePath: string;
  logDirectory: string;
  docksDirectory: string;
  reposDirectory: string;
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

function formatTimeAbsolute(utc: string | null | undefined): string {
  if (!utc) return '';
  return new Date(utc).toLocaleString();
}

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
): { label: string; dotClass: 'connected' | 'warning' | 'disconnected' } {
  if (!enabled) {
    return { label: 'Disabled', dotClass: 'disconnected' };
  }

  switch ((state || '').toLowerCase()) {
    case 'connected':
      return { label: 'Connected', dotClass: 'connected' };
    case 'connecting':
    case 'stopping':
      return { label: state || 'Connecting', dotClass: 'warning' };
    case 'error':
      return { label: 'Error', dotClass: 'disconnected' };
    case 'disconnected':
      return { label: 'Disconnected', dotClass: 'disconnected' };
    default:
      return { label: 'Checking status', dotClass: 'warning' };
  }
}

export default function Server() {
  const { isAdmin } = useAuth();
  const { connected } = useWebSocket();
  const { pushToast } = useNotifications();

  const [health, setHealth] = useState<HealthInfo | null>(null);
  const [settings, setSettings] = useState<ServerSettings | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [backupLoading, setBackupLoading] = useState(false);
  const [revealedRemoteField, setRevealedRemoteField] = useState<RemoteSecretField | null>(null);
  const [confirmDialog, setConfirmDialog] = useState<{
    open: boolean;
    message: string;
    onConfirm: () => void;
  }>({ open: false, message: '', onConfirm: () => {} });

  const restoreFileRef = useRef<HTMLInputElement>(null);

  const showToast = useCallback((severity: Severity, msg: string) => {
    pushToast(severity, msg);
  }, [pushToast]);

  function beginRevealRemoteField(field: RemoteSecretField) {
    setRevealedRemoteField(field);
  }

  function endRevealRemoteField() {
    setRevealedRemoteField(null);
  }

  const loadData = useCallback(async () => {
    try {
      setError('');
      const [h, s] = await Promise.all([
        getHealth().catch(() => null),
        getSettings().catch(() => null),
      ]);
      if (h) setHealth(h as unknown as HealthInfo);
      if (s) setSettings(mergeServerSettings(s as unknown as ServerSettings));
      if (!h && !s) setError('Failed to load server data.');
      else if (!s) setError('Failed to load server settings. Health data is available, but configuration and backup sections could not be loaded.');
    } catch {
      setError('Failed to load server data.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const handleSaveServerConfig = async () => {
    if (!settings) return;
    try {
      const updated = await updateSettings({
        admiralPort: settings.admiralPort,
        mcpPort: settings.mcpPort,
        maxCaptains: settings.maxCaptains,
      });
      setSettings(mergeServerSettings(updated as unknown as ServerSettings));
      showToast('success', 'Server configuration saved');
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : 'Unknown error';
      showToast('error', `Failed: ${msg}`);
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
      showToast('success', 'Agent settings saved');
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : 'Unknown error';
      showToast('error', `Failed: ${msg}`);
    }
  };

  const handleSaveRemoteControlSettings = async () => {
    if (!settings) return;
    try {
      const updated = await updateSettings({
        remoteControl: settings.remoteControl,
      });
      setSettings(mergeServerSettings(updated as unknown as ServerSettings));
      showToast('success', 'Remote control settings saved');
      const refreshedHealth = (await getHealth()) as unknown as HealthInfo;
      setHealth(refreshedHealth);
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : 'Unknown error';
      showToast('error', `Failed: ${msg}`);
    }
  };

  const handleHealthCheck = async () => {
    try {
      const result = (await getHealth()) as unknown as HealthInfo;
      setHealth(result);
      showToast('info', `Health: ${result.status} | Uptime: ${result.uptime}`);
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : 'Unknown error';
      showToast('error', `Health check failed: ${msg}`);
    }
  };

  const handleBackup = async () => {
    setBackupLoading(true);
    try {
      await downloadBackup();
      showToast('success', 'Backup downloaded');
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : 'Unknown error';
      showToast('error', `Backup failed: ${msg}`);
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
      showToast('success', 'Restore completed successfully. Server restart recommended.');
      loadData();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Unknown error';
      showToast('error', `Restore failed: ${msg}`);
    }
    if (restoreFileRef.current) restoreFileRef.current.value = '';
  };

  const handleStopServer = () => {
    setConfirmDialog({
      open: true,
      message: 'Stop the Admiral server? This will shut down everything.',
      onConfirm: async () => {
        try {
          await stopServer();
          showToast('warning', 'Server shutting down...');
        } catch (e: unknown) {
          const msg = e instanceof Error ? e.message : 'Unknown error';
          showToast('error', `Failed: ${msg}`);
        }
        setConfirmDialog({ open: false, message: '', onConfirm: () => {} });
      },
    });
  };

  const handleFactoryReset = () => {
    setConfirmDialog({
      open: true,
      message:
        'WARNING: Factory reset will delete ALL data including the database, logs, docks, and repos. Settings will be preserved. This cannot be undone. Continue?',
      onConfirm: async () => {
        try {
          await resetServer();
          showToast('success', 'Factory reset complete');
        } catch (e: unknown) {
          const msg = e instanceof Error ? e.message : 'Unknown error';
          showToast('error', `Factory reset failed: ${msg}`);
        }
        setConfirmDialog({ open: false, message: '', onConfirm: () => {} });
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
    copyToClipboard(text).then(() => showToast('success', 'Copied to clipboard')).catch(() => {});
  };

  const remoteTunnelIndicator = getRemoteTunnelIndicator(
    settings?.remoteControl.enabled ?? false,
    health?.remoteTunnel?.state,
  );

  if (loading) {
    return (
      <div>
        <h2>Server Settings</h2>
        <p className="text-muted">Loading...</p>
      </div>
    );
  }

  return (
    <div>
      <div className="page-header">
        <div>
          <h2>Server Settings</h2>
          <p className="text-muted">
            Admiral server health, configuration, and operational controls.
          </p>
        </div>
        <div className="page-actions">
          <RefreshButton onRefresh={loadData} title="Refresh server data" />
        </div>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />

      {/* Health Status Cards */}
      <div className="card-grid">
        <div className="card" title="Current health status of the admiral server">
          <div className="card-label">Health</div>
          <div className="card-value" style={{ fontSize: '1.2rem' }}>
            {health ? health.status : 'Loading...'}
          </div>
          {health && (
            <div className="card-detail text-muted">
              Checked: {formatTimeAbsolute(health.timestamp)}
            </div>
          )}
        </div>

        <div className="card" title="How long the server has been running">
          <div className="card-label">Uptime</div>
          <div className="card-value" style={{ fontSize: '1.2rem' }}>
            {health?.uptime || '-'}
          </div>
          {health?.startUtc && (
            <div className="card-detail text-muted">
              Started: {formatTimeAbsolute(health.startUtc)}
            </div>
          )}
        </div>

        <div className="card" title="Current connection method to the server">
          <div className="card-label">Connection</div>
          <div className="card-value" style={{ fontSize: '1.2rem' }}>
            <span
              className={`status-dot ${connected ? 'connected' : 'disconnected'}`}
              style={{ display: 'inline-block', verticalAlign: 'middle', marginRight: '0.5rem' }}
            />
            <span>{connected ? 'Live (WebSocket)' : 'Online (HTTP)'}</span>
          </div>
        </div>

        <div className="card" title="Current outbound remote-control tunnel status">
          <div className="card-label">Remote Tunnel</div>
          <div className="card-value" style={{ fontSize: '1.2rem' }}>
            {health?.remoteTunnel?.state || 'Disabled'}
          </div>
          {health?.remoteTunnel && (
            <div className="card-detail text-muted">
              {health.remoteTunnel.tunnelUrl || 'No tunnel URL configured'}
            </div>
          )}
        </div>
      </div>

      {/* Detail Fields */}
      <div className="detail-grid server-detail-grid" style={{ marginTop: '1rem' }}>
        <div className="detail-field">
          <span className="detail-label">Version</span>
          <span className="mono">{health?.version || '-'}</span>
        </div>
        <div className="detail-field">
          <span className="detail-label">API URL</span>
          <span className="mono">{window.location.origin}</span>
        </div>
        <div className="detail-field">
          <span className="detail-label">Admiral Port</span>
          <span className="mono">{health?.ports?.admiral || window.location.port}</span>
        </div>
        <div className="detail-field">
          <span className="detail-label">MCP Port</span>
          <span className="mono">{health?.ports?.mcp || '-'}</span>
        </div>
        <div className="detail-field">
          <span className="detail-label">WebSocket Port</span>
          <span className="mono">{health?.ports?.webSocket || '-'}</span>
        </div>
        <div className="detail-field">
          <span className="detail-label">Tunnel State</span>
          <span className="mono">{health?.remoteTunnel?.state || '-'}</span>
        </div>
        <div className="detail-field">
          <span className="detail-label">Tunnel Instance</span>
          <span className="mono">{health?.remoteTunnel?.instanceId || '-'}</span>
        </div>
        <div className="detail-field">
          <span className="detail-label">Tunnel Latency</span>
          <span className="mono">{health?.remoteTunnel?.latencyMs != null ? `${health.remoteTunnel.latencyMs} ms` : '-'}</span>
        </div>
        <div className="detail-field">
          <span className="detail-label">Tunnel Last Heartbeat</span>
          <span className="mono">{formatTimeAbsolute(health?.remoteTunnel?.lastHeartbeatUtc)}</span>
        </div>
      </div>

      {/* Server Configuration */}
      {settings && (
        <div className="settings-section" style={{ marginTop: '1.5rem' }}>
          <h3>Server Configuration</h3>
          <div className="settings-grid">
            <div className="form-group">
              <label>Admiral Port</label>
              <input
                type="number"
                value={settings.admiralPort}
                onChange={(e) =>
                  setSettings({ ...settings, admiralPort: parseInt(e.target.value) || 0 })
                }
                min={1}
                max={65535}
                title="REST API port (1-65535)"
              />
            </div>
            <div className="form-group">
              <label>MCP Port</label>
              <input
                type="number"
                value={settings.mcpPort}
                onChange={(e) =>
                  setSettings({ ...settings, mcpPort: parseInt(e.target.value) || 0 })
                }
                min={1}
                max={65535}
                title="MCP server port (1-65535)"
              />
            </div>
            <div className="form-group">
              <label>Max Captains</label>
              <input
                type="number"
                value={settings.maxCaptains}
                onChange={(e) =>
                  setSettings({ ...settings, maxCaptains: parseInt(e.target.value) || 0 })
                }
                min={0}
                title="Maximum captains (0 = unlimited)"
              />
            </div>
          </div>
          <button
            className="btn-primary btn-sm"
            onClick={handleSaveServerConfig}
            title="Save server configuration changes"
          >
            Save Server Config
          </button>
        </div>
      )}

      {/* Agent Settings */}
      {settings && (
        <div className="settings-section" style={{ marginTop: '1.5rem' }}>
          <h3>Agent Settings</h3>
          <div className="settings-grid">
            <div className="form-group">
              <label>Heartbeat Interval (seconds)</label>
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
                title="Health check interval, minimum 5 seconds"
              />
            </div>
            <div className="form-group">
              <label>Stall Threshold (minutes)</label>
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
                title="Minutes before a captain is considered stalled"
              />
            </div>
            <div className="form-group">
              <label>Idle Captain Timeout (seconds)</label>
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
                title="Auto-remove idle captains after this many seconds (0 = disabled)"
              />
            </div>
            <div className="form-group">
              <label className="settings-checkbox-label">
                <input
                  type="checkbox"
                  checked={settings.autoCreatePr}
                  onChange={(e) => setSettings({ ...settings, autoCreatePr: e.target.checked })}
                />
                <span>Auto-Create Pull Requests</span>
              </label>
            </div>
          </div>
          <button
            className="btn-primary btn-sm"
            onClick={handleSaveAgentSettings}
            title="Save agent settings changes"
          >
            Save Agent Settings
          </button>
        </div>
      )}

      {settings && (
        <div className="settings-section" style={{ marginTop: '1.5rem' }}>
          <h3>Remote Control</h3>
          <p className="text-muted" style={{ marginBottom: '0.75rem' }}>
            Outbound tunnel settings for connecting this Armada server to Armada.Proxy.
          </p>
          <div className="settings-grid">
            <div className="form-group">
              <label className="settings-checkbox-label">
                <input
                  type="checkbox"
                  checked={settings.remoteControl.enabled}
                  onChange={(e) =>
                    setSettings({
                      ...settings,
                      remoteControl: { ...settings.remoteControl, enabled: e.target.checked },
                    })
                  }
                />
                <span>Enable Remote Tunnel</span>
              </label>
            </div>
            <div className="form-group">
              <label>Tunnel URL</label>
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
                title="Proxy base URL or tunnel endpoint. http/https will be normalized to ws/wss and /tunnel will be added automatically when needed."
              />
            </div>
            <div className="form-group">
              <label>Instance ID Override</label>
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
                placeholder="Leave blank for auto-generated"
              />
            </div>
            <div className="form-group">
              <label>Enrollment Token</label>
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
                  placeholder="Optional bootstrap token"
                />
                <button
                  type="button"
                  className="settings-secret-toggle"
                  aria-label={revealedRemoteField === 'enrollmentToken' ? 'Hide enrollment token' : 'Show enrollment token'}
                  title={revealedRemoteField === 'enrollmentToken' ? 'Hide enrollment token' : 'Show enrollment token'}
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
              <label>Shared Password</label>
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
                  placeholder="Defaults to armadaadmin"
                  title="Shared password used by Armada.Proxy browser login and the outbound remote tunnel."
                />
                <button
                  type="button"
                  className="settings-secret-toggle"
                  aria-label={revealedRemoteField === 'password' ? 'Hide shared password' : 'Show shared password'}
                  title={revealedRemoteField === 'password' ? 'Hide shared password' : 'Show shared password'}
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
              <label>Connect Timeout (seconds)</label>
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
              />
            </div>
            <div className="form-group">
              <label>Heartbeat Interval (seconds)</label>
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
              />
            </div>
            <div className="form-group">
              <label>Reconnect Base Delay (seconds)</label>
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
              />
            </div>
            <div className="form-group">
              <label>Reconnect Max Delay (seconds)</label>
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
              />
            </div>
            <div className="form-group">
              <label className="settings-checkbox-label">
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
                <span>Allow Invalid Certificates</span>
              </label>
            </div>
          </div>
          {settings.remoteControl.enabled && (
            <div className="settings-config-block tunnel-status-card">
              <div className="settings-config-header tunnel-status-card-header">
                <span className="text-muted">Tunnel Status</span>
                <span
                  className="mono"
                  style={{ display: 'inline-flex', alignItems: 'center', gap: '0.5rem' }}
                >
                  <span className={`status-dot ${remoteTunnelIndicator.dotClass}`} />
                  <span>{remoteTunnelIndicator.label}</span>
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
            title="Save remote-control tunnel configuration"
          >
            Save Remote Control Settings
          </button>
        </div>
      )}

      {/* MCP Configuration */}
      {settings && (
        <div className="settings-section" style={{ marginTop: '1.5rem' }}>
          <h3>MCP Configuration</h3>
          <p className="text-muted" style={{ marginBottom: '0.75rem' }}>
            Client-specific MCP references for Claude, Codex, Gemini, and Cursor.
          </p>
          {MCP_CLIENTS.map((client) => (
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
                    title={`Copy ${client.title} HTTP config to clipboard`}
                  >
                    Copy
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
                    title={`Copy ${client.title} STDIO config to clipboard`}
                  >
                    Copy
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
          <h3>System Paths</h3>
          <div className="detail-grid">
            <div className="detail-field">
              <span className="detail-label">Data Directory</span>
              <span className="id-display">
                <span className="mono">{settings.dataDirectory || '-'}</span>
                {settings.dataDirectory && <CopyButton text={settings.dataDirectory} title="Copy path" />}
              </span>
            </div>
            <div className="detail-field">
              <span className="detail-label">Database Path</span>
              <span className="id-display">
                <span className="mono">{settings.databasePath || '-'}</span>
                {settings.databasePath && <CopyButton text={settings.databasePath} title="Copy path" />}
              </span>
            </div>
            <div className="detail-field">
              <span className="detail-label">Log Directory</span>
              <span className="id-display">
                <span className="mono">{settings.logDirectory || '-'}</span>
                {settings.logDirectory && <CopyButton text={settings.logDirectory} title="Copy path" />}
              </span>
            </div>
            <div className="detail-field">
              <span className="detail-label">Docks Directory</span>
              <span className="id-display">
                <span className="mono">{settings.docksDirectory || '-'}</span>
                {settings.docksDirectory && <CopyButton text={settings.docksDirectory} title="Copy path" />}
              </span>
            </div>
            <div className="detail-field">
              <span className="detail-label">Repos Directory</span>
              <span className="id-display">
                <span className="mono">{settings.reposDirectory || '-'}</span>
                {settings.reposDirectory && <CopyButton text={settings.reposDirectory} title="Copy path" />}
              </span>
            </div>
          </div>
        </div>
      )}

      {isAdmin && settings && (
        <div className="settings-section" style={{ marginTop: '1.5rem' }}>
          <h3>Database Backup</h3>
          <div className="settings-actions">
            <button
              className="btn btn-sm"
              onClick={handleBackup}
              disabled={backupLoading}
              title="Create a backup ZIP of the database and download it"
            >
              {backupLoading ? 'Backing up...' : 'Backup Now'}
            </button>
            <button
              className="btn btn-danger btn-sm"
              onClick={handleRestoreClick}
              title="Restore the database from a backup ZIP file"
            >
              Restore from Backup
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
          <h3>Server Actions</h3>
          <div className="settings-actions">
            <button
              className="btn btn-sm"
              onClick={handleHealthCheck}
              title="Run a health check and display the result"
            >
              Health Check
            </button>
            <button
              className="btn btn-danger btn-sm"
              onClick={handleStopServer}
              title="Shut down the admiral server process"
            >
              Stop Server
            </button>
            <button
              className="btn btn-danger btn-sm"
              onClick={handleFactoryReset}
              title="Delete all data and reset to factory defaults"
            >
              Factory Reset
            </button>
          </div>
        </div>
      )}

      {/* Confirm Dialog */}
      <ConfirmDialog
        open={confirmDialog.open}
        message={confirmDialog.message}
        onConfirm={confirmDialog.onConfirm}
        onCancel={() => setConfirmDialog({ open: false, message: '', onConfirm: () => {} })}
      />
    </div>
  );
}
