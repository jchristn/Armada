import { useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { createPlaybook, deletePlaybook, getPlaybook, updatePlaybook } from '../api/client';
import type { Playbook } from '../types/models';
import { useAuth } from '../context/AuthContext';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import CopyButton from '../components/shared/CopyButton';
import ErrorModal from '../components/shared/ErrorModal';
import JsonViewer from '../components/shared/JsonViewer';
import StatusBadge from '../components/shared/StatusBadge';

export default function PlaybookDetail() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { t, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();

  const createMode = id === 'new';
  const canManage = isAdmin || isTenantAdmin;

  const [playbook, setPlaybook] = useState<Playbook | null>(null);
  const [fileName, setFileName] = useState('NEW_PLAYBOOK.md');
  const [description, setDescription] = useState('');
  const [content, setContent] = useState('# Playbook\n\nDescribe the rules the model must follow.\n');
  const [active, setActive] = useState(true);
  const [loading, setLoading] = useState(!createMode);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({
    open: false,
    title: '',
    message: '',
    onConfirm: () => {},
  });

  useEffect(() => {
    if (createMode || !id) return;
    const playbookId = id;

    let mounted = true;

    async function load() {
      try {
        setLoading(true);
        const result = await getPlaybook(playbookId);
        if (!mounted) return;
        setPlaybook(result);
        setFileName(result.fileName);
        setDescription(result.description || '');
        setContent(result.content);
        setActive(result.active);
        setError('');
      } catch (err: unknown) {
        if (!mounted) return;
        setError(err instanceof Error ? err.message : t('Failed to load playbook.'));
      } finally {
        if (mounted) setLoading(false);
      }
    }

    load();
    return () => { mounted = false; };
  }, [createMode, id, t]);

  const stats = useMemo(() => {
    const lines = content.length === 0 ? 0 : content.split(/\r?\n/).length;
    const headings = (content.match(/^#+\s/gm) || []).length;
    return {
      chars: content.length,
      lines,
      headings,
    };
  }, [content]);

  async function handleSave() {
    if (!canManage) return;

    try {
      setSaving(true);
      const payload = {
        fileName,
        description: description.trim() || null,
        content,
        active,
      };

      if (createMode) {
        const created = await createPlaybook(payload);
        pushToast('success', t('Playbook "{{name}}" created.', { name: created.fileName }));
        navigate(`/playbooks/${created.id}`);
        return;
      }

      if (!id) return;
      const updated = await updatePlaybook(id, payload);
      setPlaybook(updated);
      setFileName(updated.fileName);
      setDescription(updated.description || '');
      setContent(updated.content);
      setActive(updated.active);
      pushToast('success', t('Playbook "{{name}}" saved.', { name: updated.fileName }));
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Save failed.'));
    } finally {
      setSaving(false);
    }
  }

  function handleDelete() {
    if (!playbook || !canManage) return;

    setConfirm({
      open: true,
      title: t('Delete Playbook'),
      message: t('Delete "{{name}}"? Existing mission snapshots remain immutable, but future dispatches will no longer be able to select it.', { name: playbook.fileName }),
      onConfirm: async () => {
        setConfirm((current) => ({ ...current, open: false }));
        try {
          await deletePlaybook(playbook.id);
          pushToast('warning', t('Playbook "{{name}}" deleted.', { name: playbook.fileName }));
          navigate('/playbooks');
        } catch (err: unknown) {
          setError(err instanceof Error ? err.message : t('Delete failed.'));
        }
      },
    });
  }

  if (loading) return <p className="text-dim">{t('Loading...')}</p>;

  return (
    <div>
      <div className="breadcrumb">
        <Link to="/playbooks">{t('Playbooks')}</Link> <span className="breadcrumb-sep">&gt;</span> <span>{createMode ? t('New Playbook') : fileName}</span>
      </div>

      <div className="detail-header">
        <h2>{createMode ? t('Create Playbook') : fileName}</h2>
        <div className="inline-actions">
          {!createMode && <StatusBadge status={active ? 'Active' : 'Inactive'} />}
          {!createMode && (
            <button className="btn btn-sm" onClick={() => setJsonData({ open: true, title: fileName, data: playbook })}>
              {t('View JSON')}
            </button>
          )}
          {!createMode && canManage && (
            <button className="btn btn-sm btn-danger" onClick={handleDelete}>
              {t('Delete')}
            </button>
          )}
        </div>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />
      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <ConfirmDialog
        open={confirm.open}
        title={confirm.title}
        message={confirm.message}
        onConfirm={confirm.onConfirm}
        onCancel={() => setConfirm((current) => ({ ...current, open: false }))}
      />

      {!canManage && (
        <div className="alert alert-error" style={{ marginBottom: '1rem' }}>
          {t('You can view this playbook, but only tenant administrators can change it.')}
        </div>
      )}

      {!createMode && playbook && (
        <div className="detail-grid" style={{ marginBottom: '1rem' }}>
          <div className="detail-field">
            <span className="detail-label">{t('ID')}</span>
            <span className="id-display">
              <span className="mono">{playbook.id}</span>
              <CopyButton text={playbook.id} />
            </span>
          </div>
          <div className="detail-field">
            <span className="detail-label">{t('Created')}</span>
            <span>{formatDateTime(playbook.createdUtc)}</span>
          </div>
          <div className="detail-field">
            <span className="detail-label">{t('Last Updated')}</span>
            <span>{formatDateTime(playbook.lastUpdateUtc)}</span>
          </div>
          <div className="detail-field">
            <span className="detail-label">{t('Status')}</span>
            <StatusBadge status={active ? 'Active' : 'Inactive'} />
          </div>
        </div>
      )}

      <div className="card" style={{ marginBottom: '1rem' }}>
        <div className="playbook-side-stats">
          <div>
            <span>{t('Characters')}</span>
            <strong>{stats.chars.toLocaleString()}</strong>
          </div>
          <div>
            <span>{t('Lines')}</span>
            <strong>{stats.lines.toLocaleString()}</strong>
          </div>
          <div>
            <span>{t('Headings')}</span>
            <strong>{stats.headings.toLocaleString()}</strong>
          </div>
        </div>
      </div>

      <div className="card playbook-editor-card">
        <label className="playbook-editor-field">
          <span>{t('File Name')}</span>
          <input
            type="text"
            value={fileName}
            disabled={!canManage}
            onChange={(event) => setFileName(event.target.value)}
            placeholder={t('CSHARP_BACKEND_ARCHITECTURE.md')}
          />
        </label>

        <label className="playbook-editor-field">
          <span>{t('Description')}</span>
          <input
            type="text"
            value={description}
            disabled={!canManage}
            onChange={(event) => setDescription(event.target.value)}
            placeholder={t('Optional summary shown during playbook selection')}
          />
        </label>

        <label className="playbook-toggle">
          <input type="checkbox" checked={active} disabled={!canManage} onChange={(event) => setActive(event.target.checked)} />
          <span>{t('Active and selectable during dispatch')}</span>
        </label>

        <label className="playbook-editor-field">
          <span>{t('Markdown Content')}</span>
          <textarea
            className="playbook-editor-textarea"
            value={content}
            disabled={!canManage}
            onChange={(event) => setContent(event.target.value)}
            spellCheck={false}
            rows={24}
          />
        </label>

        <div className="playbook-editor-actions">
          <button className="btn btn-primary" disabled={!canManage || saving} onClick={handleSave}>
            {saving ? t('Saving...') : createMode ? t('Create Playbook') : t('Save Changes')}
          </button>
          <button className="btn" onClick={() => navigate('/playbooks')}>
            {t('Back')}
          </button>
        </div>
      </div>
    </div>
  );
}
