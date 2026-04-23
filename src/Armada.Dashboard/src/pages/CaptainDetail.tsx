import { useEffect, useState, useCallback } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import {
  getCaptain,
  getCaptainLog,
  stopCaptain,
  recallCaptain,
  getMission,
  listMissions,
  updateCaptain,
  deleteCaptain,
} from '../api/client';
import type { Captain, Mission, LogResult } from '../types/models';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import ErrorModal from '../components/shared/ErrorModal';
import JsonViewer from '../components/shared/JsonViewer';
import StatusBadge from '../components/shared/StatusBadge';
import CopyButton from '../components/shared/CopyButton';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';

const RUNTIMES = ['ClaudeCode', 'Codex', 'Gemini', 'Cursor', 'Custom'];

export default function CaptainDetail() {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [captain, setCaptain] = useState<Captain | null>(null);
  const [currentMission, setCurrentMission] = useState<Mission | null>(null);
  const [missions, setMissions] = useState<Mission[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Edit
  const [showForm, setShowForm] = useState(false);
  const [form, setForm] = useState({ name: '', runtime: 'ClaudeCode', systemInstructions: '', model: '', allowedPersonas: '', preferredPersona: '' });

  // Log viewer
  const [logText, setLogText] = useState<string | null>(null);
  const [logLoading, setLogLoading] = useState(false);
  const [showLog, setShowLog] = useState(false);
  const [logInfo, setLogInfo] = useState('');

  // JSON viewer
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });

  // Confirm
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

  const load = useCallback(async () => {
    if (!id) return;
    try {
      setLoading(true);
      const isInitialLoad = !captain;
      const cap = await getCaptain(id);
      setCaptain(cap);
      // Load current mission if set
      if (cap.currentMissionId) {
        try {
          const m = await getMission(cap.currentMissionId);
          setCurrentMission(m);
        } catch { setCurrentMission(null); }
      } else {
        setCurrentMission(null);
      }
      // Load missions assigned to this captain
      try {
        const mResult = await listMissions({ pageSize: 100, filters: { captainId: id } });
        setMissions(mResult.objects || []);
      } catch { setMissions([]); }
      if (isInitialLoad) setError('');
    } catch {
      setError(t('Failed to load captain.'));
    } finally {
      setLoading(false);
    }
  }, [id, t]);

  useEffect(() => { load(); }, [load]);

  function openEdit() {
    if (!captain) return;
    setForm({ name: captain.name, runtime: captain.runtime || 'ClaudeCode', systemInstructions: captain.systemInstructions ?? '', model: captain.model ?? '', allowedPersonas: captain.allowedPersonas ?? '', preferredPersona: captain.preferredPersona ?? '' });
    setShowForm(true);
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!captain) return;
    try {
      const payload = { ...form } as Record<string, unknown>;
      if (!payload.systemInstructions) delete payload.systemInstructions;
      payload.model = form.model.trim() ? form.model.trim() : null;
      if (!payload.allowedPersonas) delete payload.allowedPersonas;
      if (!payload.preferredPersona) delete payload.preferredPersona;
      await updateCaptain(captain.id, payload);
      setShowForm(false);
      pushToast('success', t('Captain "{{name}}" saved.', { name: form.name }));
      load();
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : t('Save failed.'));
    }
  }

  async function handleViewLog() {
    if (!id) return;
    setLogLoading(true);
    setShowLog(true);
    try {
      const result: LogResult = await getCaptainLog(id);
      setLogText(result.log || t('(empty log)'));
      setLogInfo(t('({{lines}} of {{totalLines}} lines)', { lines: result.lines || 0, totalLines: result.totalLines || 0 }));
    } catch {
      setLogText(t('Failed to load log.'));
      setLogInfo('');
    } finally {
      setLogLoading(false);
    }
  }

  function handleStop() {
    if (!captain) return;
    setConfirm({
      open: true,
      title: t('Stop Captain'),
      message: t('Stop captain "{{name}}"? This will halt the current mission.', { name: captain.name }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await stopCaptain(captain.id);
          pushToast('warning', t('Captain "{{name}}" stopped.', { name: captain.name }));
          load();
        } catch { setError(t('Failed to stop captain.')); }
      },
    });
  }

  function handleRecall() {
    if (!captain) return;
    setConfirm({
      open: true,
      title: t('Recall Captain'),
      message: t('Recall captain "{{name}}"? The captain will finish current work and return to idle.', { name: captain.name }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await recallCaptain(captain.id);
          pushToast('warning', t('Captain "{{name}}" recalled.', { name: captain.name }));
          load();
        } catch { setError(t('Failed to recall captain.')); }
      },
    });
  }

  function handleRemove() {
    if (!captain) return;
    setConfirm({
      open: true,
      title: t('Remove Captain'),
      message: t('Remove captain "{{name}}"? This cannot be undone.', { name: captain.name }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await deleteCaptain(captain.id);
          pushToast('warning', t('Captain "{{name}}" removed.', { name: captain.name }));
          navigate('/captains');
        } catch { setError(t('Remove failed.')); }
      },
    });
  }

  function getActionItems() {
    if (!captain) return [];
    const items: { label: string; danger?: boolean; onClick: () => void }[] = [
      { label: 'Edit', onClick: openEdit },
      { label: 'View Log', onClick: handleViewLog },
      { label: 'View JSON', onClick: () => setJsonData({ open: true, title: t('Captain: {{name}}', { name: captain.name }), data: captain }) },
    ];
    if (captain.state === 'Working' || captain.state === 'Stalled') {
      items.push({ label: 'Recall Captain', onClick: handleRecall });
      items.push({ label: 'Stop Captain', danger: true, onClick: handleStop });
    }
    items.push({ label: 'Remove', danger: true, onClick: handleRemove });
    return items;
  }

  if (loading) return <p className="text-dim">{t('Loading...')}</p>;
  if (error && !captain) return <ErrorModal error={error} onClose={() => setError('')} />;
  if (!captain) return <p className="text-dim">{t('Captain not found.')}</p>;

  return (
    <div>
      {/* Breadcrumb */}
      <div className="breadcrumb">
        <Link to="/captains">{t('Captains')}</Link> <span className="breadcrumb-sep">&gt;</span> <span>{captain.name}</span>
      </div>

      <div className="detail-header">
        <h2>{captain.name}</h2>
        <div className="inline-actions">
          <ActionMenu id={`captain-${captain.id}`} items={getActionItems()} />
        </div>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />

      {/* Edit Modal */}
      {showForm && (
        <div className="modal-overlay" onClick={() => setShowForm(false)}>
          <form className="modal" onClick={e => e.stopPropagation()} onSubmit={handleSubmit}>
            <h3>{t('Edit Captain')}</h3>
            <label>{t('Name')}<input value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} required /></label>
            <label>{t('Runtime')}
              <select value={form.runtime} onChange={e => setForm({ ...form, runtime: e.target.value })} required>
                {RUNTIMES.map(r => <option key={r} value={r}>{r}</option>)}
              </select>
            </label>
            <label title={t('Optional instructions injected into every mission prompt for this captain. Use this to specialize behavior, add guardrails, or provide persistent context.')}>
              {t('System Instructions')}
              <textarea value={form.systemInstructions} onChange={e => setForm({ ...form, systemInstructions: e.target.value })} rows={4} placeholder={t('e.g., You are a testing specialist. Always run tests before committing...')} />
            </label>
            <label title={t('Optional AI model identifier. Leave blank to let the runtime choose its default model.')}>
              {t('Model')}
              <input value={form.model} onChange={e => setForm({ ...form, model: e.target.value })} placeholder={t('e.g., gpt-5.4-mini')} />
            </label>
            <label>
              {t('Allowed Personas (JSON array)')}
              <textarea value={form.allowedPersonas} onChange={e => setForm({ ...form, allowedPersonas: e.target.value })} rows={2} placeholder={t('["Worker", "Judge"]')} />
            </label>
            <label>
              {t('Preferred Persona')}
              <input value={form.preferredPersona} onChange={e => setForm({ ...form, preferredPersona: e.target.value })} placeholder={t('e.g., Worker')} />
            </label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary">{t('Save')}</button>
              <button type="button" className="btn" onClick={() => setShowForm(false)}>{t('Cancel')}</button>
            </div>
          </form>
        </div>
      )}

      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message}
        onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />

      {/* Captain Info */}
      <div className="detail-grid">
        <div className="detail-field">
          <span className="detail-label">{t('ID')}</span>
          <span className="id-display">
            <span className="mono">{captain.id}</span>
            <CopyButton text={captain.id} />
          </span>
        </div>
        <div className="detail-field"><span className="detail-label">{t('Name')}</span><span>{captain.name}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Tenant ID')}</span><span className="mono">{captain.tenantId || '-'}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Runtime')}</span><span>{captain.runtime || 'ClaudeCode'}</span></div>
      </div>
      {captain.systemInstructions && (
        <div className="detail-context-section">
          <h4>{t('System Instructions')}</h4>
          <pre className="detail-context-block">{captain.systemInstructions}</pre>
        </div>
      )}
      <div className="detail-grid">
        <div className="detail-field">
          <span className="detail-label">{t('Allowed Personas')}</span>
          <span>{captain.allowedPersonas || <span className="text-dim">{t('Any (no restriction)')}</span>}</span>
        </div>
        <div className="detail-field">
          <span className="detail-label">{t('Model')}</span>
          <span>{captain.model || <span className="text-dim">{t('Runtime default')}</span>}</span>
        </div>
        <div className="detail-field">
          <span className="detail-label">{t('Preferred Persona')}</span>
          <span>{captain.preferredPersona || <span className="text-dim">{t('None')}</span>}</span>
        </div>
        <div className="detail-field">
          <span className="detail-label">{t('State')}</span>
          <StatusBadge status={captain.state} />
        </div>
        <div className="detail-field">
          <span className="detail-label">{t('Current Mission')}</span>
          {captain.currentMissionId ? (
            <span className="id-display">
              <Link className="mono" to={`/missions/${captain.currentMissionId}`}>{captain.currentMissionId}</Link>
              <CopyButton text={captain.currentMissionId!} />
            </span>
          ) : <span>-</span>}
        </div>
        <div className="detail-field">
          <span className="detail-label">{t('Current Dock')}</span>
          {captain.currentDockId ? (
            <span className="id-display">
              <Link className="mono" to={`/docks/${captain.currentDockId}`}>{captain.currentDockId}</Link>
              <CopyButton text={captain.currentDockId!} />
            </span>
          ) : <span>-</span>}
        </div>
        <div className="detail-field"><span className="detail-label">{t('Process ID')}</span><span>{captain.processId || '-'}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Recovery Attempts')}</span><span>{captain.recoveryAttempts ?? 0}</span></div>
        <div className="detail-field">
          <span className="detail-label">{t('Last Heartbeat')}</span>
          <span title={formatDateTime(captain.lastHeartbeatUtc)}>
            {formatRelativeTime(captain.lastHeartbeatUtc) || '-'}
            {captain.lastHeartbeatUtc && <span className="text-dim"> ({formatDateTime(captain.lastHeartbeatUtc)})</span>}
          </span>
        </div>
        <div className="detail-field">
          <span className="detail-label">{t('Created')}</span>
          <span title={captain.createdUtc}>
            {formatRelativeTime(captain.createdUtc)}
            <span className="text-dim"> ({formatDateTime(captain.createdUtc)})</span>
          </span>
        </div>
        <div className="detail-field">
          <span className="detail-label">{t('Last Updated')}</span>
          <span title={captain.lastUpdateUtc}>
            {formatRelativeTime(captain.lastUpdateUtc)}
            <span className="text-dim"> ({formatDateTime(captain.lastUpdateUtc)})</span>
          </span>
        </div>
      </div>

      {/* Current Mission Details */}
      {currentMission && (
        <div style={{ marginTop: '1rem' }}>
          <h3>{t('Current Mission')}</h3>
          <div className="card" style={{ padding: '1rem' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 8 }}>
              <strong>{currentMission.title}</strong>
              <StatusBadge status={currentMission.status} />
            </div>
            {currentMission.description && <p className="text-dim" style={{ marginBottom: 8 }}>{currentMission.description}</p>}
            <div style={{ display: 'flex', gap: 16, fontSize: 13 }}>
              <span>{t('Branch')}: <span className="mono">{currentMission.branchName || '-'}</span></span>
              <span>{t('Priority')}: {currentMission.priority}</span>
            </div>
            <div style={{ marginTop: 8 }}>
              <Link to={`/missions/${currentMission.id}`} className="btn btn-sm">{t('View Mission')}</Link>
            </div>
          </div>
        </div>
      )}

      {/* Log Viewer */}
      {showLog && (
        <div style={{ marginTop: '1rem' }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <h3>{t('Captain Log')} {logInfo && <span className="text-dim">{logInfo}</span>}</h3>
            <button className="btn" onClick={() => setShowLog(false)}>{t('Hide Log')}</button>
          </div>
          {logLoading ? (
            <p className="text-dim">{t('Loading log...')}</p>
          ) : (
            <pre style={{
              background: '#1a1a2e',
              color: '#e0e0e0',
              padding: 16,
              borderRadius: 'var(--radius)',
              overflow: 'auto',
              fontSize: 12,
              fontFamily: 'var(--mono)',
              maxHeight: '60vh',
              whiteSpace: 'pre-wrap',
              wordBreak: 'break-word',
            }}>
              {logText}
            </pre>
          )}
        </div>
      )}

      {/* Recent Missions */}
      <div style={{ marginTop: '1rem' }}>
        <h3>{t('Recent Missions')}</h3>
        {missions.length > 0 ? (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th title={t('Mission name and unique identifier')}>{t('Mission')}</th>
                  <th title={t('Current mission lifecycle state')}>{t('Status')}</th>
                  <th title={t('Git branch for this mission\'s work')}>{t('Branch')}</th>
                  <th title={t('When the mission was completed or created')}>{t('Date')}</th>
                </tr>
              </thead>
              <tbody>
                {missions.map(m => (
                  <tr key={m.id} className="clickable" onClick={() => navigate(`/missions/${m.id}`)}>
                    <td>
                      <strong>{m.title}</strong>
                      <div className="text-dim id-display">
                        <span className="mono">{m.id}</span>
                        <CopyButton text={m.id} />
                      </div>
                    </td>
                    <td><StatusBadge status={m.status} /></td>
                    <td className="text-dim">{m.branchName || '-'}</td>
                    <td className="text-dim" title={formatDateTime(m.completedUtc || m.createdUtc)}>
                      {formatRelativeTime(m.completedUtc || m.createdUtc)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <p className="text-dim" style={{ marginTop: '0.5rem' }}>{t('No missions yet')}</p>
        )}
      </div>
    </div>
  );
}
