import { useEffect, useState, useCallback } from 'react';
import { getSettings, updateSettings, getHealth } from '../api/client';
import RefreshButton from '../components/shared/RefreshButton';
import AutoRefreshSelect from '../components/shared/AutoRefreshSelect';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import PageHeader from '../components/shared/PageHeader';
import ErrorModal from '../components/shared/ErrorModal';
import { useLocale } from '../context/LocaleContext';
import { useProxySessionContext } from '../lib/useProxySessionContext';
import DefaultLandingModeField from '../components/settings/DefaultLandingModeField';
import UsageCaptureSettingsPanel from '../components/usage/UsageCaptureSettingsPanel';
import { useAuth } from '../context/AuthContext';
import { DEFAULT_GLOBAL_LANDING_MODE } from '../lib/vesselForm';

interface ServerSettings {
  admiralPort: number;
  mcpPort: number;
  maxCaptains: number;
  heartbeatIntervalSeconds: number;
  stallThresholdMinutes: number;
  idleCaptainTimeoutSeconds: number;
  landingMode?: string | null;
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
  const { t } = useLocale();
  const { isAdmin } = useAuth();
  const proxyContext = useProxySessionContext();
  const [settings, setSettings] = useState<ServerSettings | null>(null);
  const [health, setHealth] = useState<HealthInfo | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [toast, setToast] = useState('');
  const [saving, setSaving] = useState(false);
  const remoteProxyMode = Boolean(proxyContext?.selectedInstanceId);

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
      if (!s) setError(t('Failed to load settings.'));
    } catch {
      setError(t('Failed to load settings.'));
    } finally {
      setLoading(false);
    }
  }, [t]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('settings', loadData);

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
        landingMode: settings.landingMode || DEFAULT_GLOBAL_LANDING_MODE,
      });
      setSettings(updated as unknown as ServerSettings);
      showToast(t('Settings saved successfully'));
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : t('Unknown error');
      showToast(t('Failed to save settings: {{message}}', { message: msg }));
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return (
      <div>
        <div className="page-header">
          <h2>{t('Settings')}</h2>
        </div>
        <p className="text-muted">{t('Loading settings...')}</p>
      </div>
    );
  }

  return (
    <div>
      <PageHeader
        title={t('Settings')}
        subtitle={t('View and modify server configuration.')}
        actions={(
          <>
            <AutoRefreshSelect seconds={refreshSeconds} onChange={setRefreshSeconds} />
            <RefreshButton onRefresh={loadData} title={t('Reload the settings and server info from the Admiral. Unsaved edits on this page are replaced.')} />
          </>
        )}
      />

      <ErrorModal error={error} onClose={() => setError('')} />
      {toast && (
        <div className="alert alert-success" style={{ marginBottom: '1rem' }}>
          {toast}
        </div>
      )}
      {remoteProxyMode && (
        <div className="alert alert-warning" style={{ marginBottom: '1rem' }}>
          {t(
            'This page is connected through Armada.Proxy for {{instanceId}}. Editing general settings is blocked in remote mode.',
            { instanceId: proxyContext?.selectedInstanceId ?? t('the selected deployment') },
          )}
        </div>
      )}

      {/* Server Info */}
      {health && (
        <div className="card" style={{ marginBottom: '1.5rem' }}>
          <h3>{t('Server Info')}</h3>
          <div className="detail-grid">
            <div className="detail-field">
              <span className="detail-label" title={t('Armada version the Admiral server is running.')}>{t('Version')}</span>
              <span className="mono" title={t('Armada version the Admiral server is running.')}>{health.version || '-'}</span>
            </div>
            <div className="detail-field">
              <span className="detail-label" title={t('Health reported by the Admiral health endpoint. Anything other than healthy means a core check failed; run Doctor for details.')}>{t('Status')}</span>
              <span className={`status ${health.status === 'healthy' ? 'status-active' : 'status-stopped'}`} title={t('Health reported by the Admiral health endpoint. Anything other than healthy means a core check failed; run Doctor for details.')}>
                {t(health.status)}
              </span>
            </div>
            <div className="detail-field">
              <span className="detail-label" title={t('How long the Admiral process has been running since its last start or restart.')}>{t('Uptime')}</span>
              <span className="mono" title={t('How long the Admiral process has been running since its last start or restart.')}>{health.uptime || '-'}</span>
            </div>
          </div>
        </div>
      )}

      {settings && (
        <>
          <fieldset disabled={remoteProxyMode} style={{ border: 'none', margin: 0, padding: 0 }}>
          {/* Server Configuration */}
          <div className="settings-section">
            <h3>{t('Server Configuration')}</h3>
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
          </div>

          {/* Agent Settings */}
          <div className="settings-section" style={{ marginTop: '1.5rem' }}>
            <h3 title={t('Timing for the health loop that watches running captains, plus the default landing mode. Changes apply to the running Admiral when saved.')}>{t('Agent Settings')}</h3>
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
            </div>
          </div>
          </fieldset>

          <UsageCaptureSettingsPanel canEdit={isAdmin} />

          {/* System Paths (read-only) */}
          <div className="settings-section" style={{ marginTop: '1.5rem' }}>
            <h3 title={t('Where the Admiral stores its files on the server. Read-only here; change them in settings.json and restart the Admiral.')}>{t('System Paths')}</h3>
            <div className="detail-grid">
              <div className="detail-field">
                <span className="detail-label" title={t('Root folder for Armada data on the Admiral host. The other paths default to folders inside it.')}>{t('Data Directory')}</span>
                <span className="mono">{settings.dataDirectory || '-'}</span>
              </div>
              <div className="detail-field">
                <span className="detail-label" title={t('SQLite database file the Admiral uses when the database provider is SQLite.')}>{t('Database Path')}</span>
                <span className="mono">{settings.databasePath || '-'}</span>
              </div>
              <div className="detail-field">
                <span className="detail-label" title={t('Folder for Admiral and captain log files.')}>{t('Log Directory')}</span>
                <span className="mono">{settings.logDirectory || '-'}</span>
              </div>
              <div className="detail-field">
                <span className="detail-label" title={t('Folder where git worktrees (docks) are created for captains to work in, one per mission.')}>{t('Docks Directory')}</span>
                <span className="mono">{settings.docksDirectory || '-'}</span>
              </div>
              <div className="detail-field">
                <span className="detail-label" title={t('Folder holding the bare clones of each vessel repository that docks are created from.')}>{t('Repos Directory')}</span>
                <span className="mono">{settings.reposDirectory || '-'}</span>
              </div>
            </div>
          </div>

          {/* Save Button */}
          <div style={{ marginTop: '1.5rem' }}>
            <button
              className="btn-primary"
              onClick={handleSaveAll}
              disabled={saving || remoteProxyMode}
              title={remoteProxyMode ? t('Editing settings is blocked while connected through Armada.Proxy. Connect to the Admiral directly to change them.') : t('Save every field on this page to the Admiral and settings.json. Most apply at once; port changes need a restart.')}
            >
              {saving ? t('Saving...') : t('Save All Settings')}
            </button>
          </div>
        </>
      )}
    </div>
  );
}
