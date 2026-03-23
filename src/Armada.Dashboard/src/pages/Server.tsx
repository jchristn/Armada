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
}

function formatTimeAbsolute(utc: string | null | undefined): string {
  if (!utc) return '';
  return new Date(utc).toLocaleString();
}

export default function Server() {
  const { isAdmin } = useAuth();
  const { connected } = useWebSocket();

  const [health, setHealth] = useState<HealthInfo | null>(null);
  const [settings, setSettings] = useState<ServerSettings | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [toast, setToast] = useState('');
  const [backupLoading, setBackupLoading] = useState(false);
  const [confirmDialog, setConfirmDialog] = useState<{
    open: boolean;
    message: string;
    onConfirm: () => void;
  }>({ open: false, message: '', onConfirm: () => {} });

  const restoreFileRef = useRef<HTMLInputElement>(null);

  const showToast = (msg: string) => {
    setToast(msg);
    setTimeout(() => setToast(''), 4000);
  };

  const loadData = useCallback(async () => {
    try {
      setError('');
      const [h, s] = await Promise.all([
        getHealth().catch(() => null),
        getSettings().catch(() => null),
      ]);
      if (h) setHealth(h as unknown as HealthInfo);
      if (s) setSettings(s as unknown as ServerSettings);
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
      setSettings(updated as unknown as ServerSettings);
      showToast('Server configuration saved');
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : 'Unknown error';
      showToast(`Failed: ${msg}`);
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
      setSettings(updated as unknown as ServerSettings);
      showToast('Agent settings saved');
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : 'Unknown error';
      showToast(`Failed: ${msg}`);
    }
  };

  const handleHealthCheck = async () => {
    try {
      const result = (await getHealth()) as unknown as HealthInfo;
      setHealth(result);
      showToast(`Health: ${result.status} | Uptime: ${result.uptime}`);
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : 'Unknown error';
      showToast(`Health check failed: ${msg}`);
    }
  };

  const handleBackup = async () => {
    setBackupLoading(true);
    try {
      await downloadBackup();
      showToast('Backup downloaded');
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : 'Unknown error';
      showToast(`Backup failed: ${msg}`);
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
      showToast('Restore completed successfully. Server restart recommended.');
      loadData();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Unknown error';
      showToast(`Restore failed: ${msg}`);
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
          showToast('Server shutting down...');
        } catch (e: unknown) {
          const msg = e instanceof Error ? e.message : 'Unknown error';
          showToast(`Failed: ${msg}`);
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
          showToast('Factory reset complete');
        } catch (e: unknown) {
          const msg = e instanceof Error ? e.message : 'Unknown error';
          showToast(`Factory reset failed: ${msg}`);
        }
        setConfirmDialog({ open: false, message: '', onConfirm: () => {} });
      },
    });
  };

  const getMcpConfigHttp = (): string => {
    const port = health?.ports?.mcp || settings?.mcpPort || 7891;
    return JSON.stringify(
      {
        type: 'streamableHttp',
        url: `http://localhost:${port}/mcp`,
      },
      null,
      2,
    );
  };

  const getMcpConfigStdio = (): string => {
    return JSON.stringify(
      {
        type: 'stdio',
        command: 'armada',
        args: ['mcp'],
      },
      null,
      2,
    );
  };

  const copyAndToast = (text: string) => {
    copyToClipboard(text).then(() => showToast('Copied to clipboard')).catch(() => {});
  };

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
      {toast && (
        <div className="alert alert-success" style={{ marginBottom: '1rem' }}>
          {toast}
        </div>
      )}

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
      </div>

      {/* Detail Fields */}
      <div className="detail-grid" style={{ marginTop: '1rem' }}>
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

      {/* MCP Configuration */}
      {settings && (
        <div className="settings-section" style={{ marginTop: '1.5rem' }}>
          <h3>MCP Configuration</h3>
          <p className="text-muted" style={{ marginBottom: '0.75rem' }}>
            Add to <span className="mono">~/.claude/settings.json</span> under{' '}
            <span className="mono">mcpServers.armada</span>
          </p>
          <div className="settings-config-block">
            <div className="settings-config-header">
              <span className="text-muted">HTTP (Streamable)</span>
              <button
                className="btn-sm"
                onClick={() => copyAndToast(getMcpConfigHttp())}
                title="Copy HTTP config to clipboard"
              >
                Copy
              </button>
            </div>
            <pre className="settings-config-code">{getMcpConfigHttp()}</pre>
          </div>
          <div className="settings-config-block" style={{ marginTop: '0.75rem' }}>
            <div className="settings-config-header">
              <span className="text-muted">STDIO</span>
              <button
                className="btn-sm"
                onClick={() => copyAndToast(getMcpConfigStdio())}
                title="Copy STDIO config to clipboard"
              >
                Copy
              </button>
            </div>
            <pre className="settings-config-code">{getMcpConfigStdio()}</pre>
          </div>
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
