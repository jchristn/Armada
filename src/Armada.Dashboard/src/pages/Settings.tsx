import { useEffect, useState, useCallback } from 'react';
import { getSettings, updateSettings, getHealth } from '../api/client';
import RefreshButton from '../components/shared/RefreshButton';
import ErrorModal from '../components/shared/ErrorModal';

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

interface HealthInfo {
  status: string;
  uptime: string;
  version: string;
}

export default function Settings() {
  const [settings, setSettings] = useState<ServerSettings | null>(null);
  const [health, setHealth] = useState<HealthInfo | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [toast, setToast] = useState('');
  const [saving, setSaving] = useState(false);

  const showToast = (msg: string) => {
    setToast(msg);
    setTimeout(() => setToast(''), 4000);
  };

  const loadData = useCallback(async () => {
    try {
      const [s, h] = await Promise.all([
        getSettings().catch(() => null),
        getHealth().catch(() => null),
      ]);
      if (s) setSettings(s as unknown as ServerSettings);
      if (h) setHealth(h as unknown as HealthInfo);
      if (!s) setError('Failed to load settings.');
    } catch {
      setError('Failed to load settings.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const handleSaveAll = async () => {
    if (!settings) return;
    setSaving(true);
    try {
      const updated = await updateSettings({
        admiralPort: settings.admiralPort,
        mcpPort: settings.mcpPort,
        maxCaptains: settings.maxCaptains,
        heartbeatIntervalSeconds: settings.heartbeatIntervalSeconds,
        stallThresholdMinutes: settings.stallThresholdMinutes,
        idleCaptainTimeoutSeconds: settings.idleCaptainTimeoutSeconds,
        autoCreatePr: settings.autoCreatePr,
      });
      setSettings(updated as unknown as ServerSettings);
      showToast('Settings saved successfully');
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : 'Unknown error';
      showToast(`Failed to save settings: ${msg}`);
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return (
      <div>
        <div className="page-header">
          <h2>Settings</h2>
        </div>
        <p className="text-muted">Loading settings...</p>
      </div>
    );
  }

  return (
    <div>
      <div className="page-header">
        <div>
          <h2>Settings</h2>
          <p className="text-muted">View and modify server configuration.</p>
        </div>
        <div className="page-actions">
          <RefreshButton onRefresh={loadData} title="Refresh settings" />
        </div>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />
      {toast && (
        <div className="alert alert-success" style={{ marginBottom: '1rem' }}>
          {toast}
        </div>
      )}

      {/* Server Info */}
      {health && (
        <div className="card" style={{ marginBottom: '1.5rem' }}>
          <h3>Server Info</h3>
          <div className="detail-grid">
            <div className="detail-field">
              <span className="detail-label">Version</span>
              <span className="mono">{health.version || '-'}</span>
            </div>
            <div className="detail-field">
              <span className="detail-label">Status</span>
              <span className={`status ${health.status === 'healthy' ? 'status-active' : 'status-stopped'}`}>
                {health.status}
              </span>
            </div>
            <div className="detail-field">
              <span className="detail-label">Uptime</span>
              <span className="mono">{health.uptime || '-'}</span>
            </div>
          </div>
        </div>
      )}

      {settings && (
        <>
          {/* Server Configuration */}
          <div className="settings-section">
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
          </div>

          {/* Agent Settings */}
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
          </div>

          {/* System Paths (read-only) */}
          <div className="settings-section" style={{ marginTop: '1.5rem' }}>
            <h3>System Paths</h3>
            <div className="detail-grid">
              <div className="detail-field">
                <span className="detail-label">Data Directory</span>
                <span className="mono">{settings.dataDirectory || '-'}</span>
              </div>
              <div className="detail-field">
                <span className="detail-label">Database Path</span>
                <span className="mono">{settings.databasePath || '-'}</span>
              </div>
              <div className="detail-field">
                <span className="detail-label">Log Directory</span>
                <span className="mono">{settings.logDirectory || '-'}</span>
              </div>
              <div className="detail-field">
                <span className="detail-label">Docks Directory</span>
                <span className="mono">{settings.docksDirectory || '-'}</span>
              </div>
              <div className="detail-field">
                <span className="detail-label">Repos Directory</span>
                <span className="mono">{settings.reposDirectory || '-'}</span>
              </div>
            </div>
          </div>

          {/* Save Button */}
          <div style={{ marginTop: '1.5rem' }}>
            <button
              className="btn-primary"
              onClick={handleSaveAll}
              disabled={saving}
              title="Save all settings"
            >
              {saving ? 'Saving...' : 'Save All Settings'}
            </button>
          </div>
        </>
      )}
    </div>
  );
}
