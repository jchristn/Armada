import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { createPlaybook, deletePlaybook, getPlaybook, updatePlaybook } from '../api/client';
import type { Playbook } from '../types/models';
import { useAuth } from '../context/AuthContext';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import CopyButton from '../components/shared/CopyButton';
import ErrorModal from '../components/shared/ErrorModal';
import JsonViewer from '../components/shared/JsonViewer';
import PageHeader from '../components/shared/PageHeader';
import StatusBadge from '../components/shared/StatusBadge';
import { buildPlaybookDuplicatePayload } from '../lib/duplicates';
import { canEdit as canEditScoped, type ScopeViewer } from '../lib/scoping';
import { useDeleteProgress } from '../lib/useDeleteProgress';

const PLAYBOOK_FILE_TIP = 'File name for the playbook, which must end in .md, for example CSHARP_BACKEND_ARCHITECTURE.md. Captains see this name when the playbook is attached as a file. Required.';
const PLAYBOOK_DESCRIPTION_TIP = 'Optional one-line summary shown when people pick playbooks during dispatch. It is not sent to the captain.';
const PLAYBOOK_CONTENT_TIP = 'The guidance itself, in markdown: rules, standards or checklists the captain should follow. Depending on how it is attached, it is pasted into the prompt or given as a file.';
const PLAYBOOK_ACTIVE_TIP = 'On: the playbook can be selected during dispatch. Off: it is hidden from selection and rejected if requested; missions that already used it are not affected.';

export default function PlaybookDetail() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { isAdmin, isTenantAdmin, user } = useAuth();
  const viewer: ScopeViewer = { isAdmin, isTenantAdmin, tenantId: user?.user?.tenantId, userId: user?.user?.id };
  const { t, formatDateTime } = useLocale();
  const deletion = useDeleteProgress();
  const { pushToast } = useNotifications();

  const createMode = id === 'new';

  const [playbook, setPlaybook] = useState<Playbook | null>(null);
  const canManage = createMode ? true : (playbook ? canEditScoped(viewer, playbook) : (isAdmin || isTenantAdmin));
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
        if (!(await deletion.run({ kind: 'playbook', action: () => deletePlaybook(playbook.id), errorMessage: (err) => err instanceof Error ? err.message : t('Delete failed.') }))) return;
        pushToast('warning', t('Playbook "{{name}}" deleted.', { name: playbook.fileName }));
        navigate('/playbooks');
      },
    });
  }

  async function handleDuplicate() {
    if (!playbook || !canManage) return;
    try {
      setSaving(true);
      const created = await createPlaybook(buildPlaybookDuplicatePayload(playbook));
      pushToast('success', t('Playbook "{{name}}" duplicated.', { name: created.fileName }));
      navigate(`/playbooks/${created.id}`);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Duplicate failed.'));
    } finally {
      setSaving(false);
    }
  }

  if (loading) return <p className="text-dim">{t('Loading...')}</p>;

  return (
    <div>
      <PageHeader
        breadcrumb={[
          { label: t('Playbooks'), to: '/playbooks' },
          { label: createMode ? t('New Playbook') : fileName },
        ]}
        title={createMode ? t('Create Playbook') : fileName}
        actions={
          <>
            {!createMode && <StatusBadge status={active ? 'Active' : 'Inactive'} />}
            {!createMode && (
              <button className="btn btn-sm" onClick={() => setJsonData({ open: true, title: fileName, data: playbook })} title={t('Show this playbook\'s full record as JSON, as the API returns it.')}>
                {t('View JSON')}
              </button>
            )}
            {!createMode && canManage && (
              <button className="btn btn-sm" disabled={saving} onClick={() => void handleDuplicate()} title={t('Create a copy of this playbook under a new file name, then open it. Unsaved edits here are not copied.')}>
                {t('Duplicate')}
              </button>
            )}
            {!createMode && canManage && (
              <button className="btn btn-sm btn-danger" onClick={handleDelete} title={t('Permanently delete this playbook. Missions that already used it keep their copy. You confirm first.')}>
                {t('Delete')}
              </button>
            )}
          </>
        }
      />

      <ErrorModal error={error} onClose={() => setError('')} />
      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      {deletion.dialog}
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
            <span className="detail-label" title={t('Unique playbook ID, used when selecting playbooks through the API or MCP tools.')}>{t('ID')}</span>
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
            <span className="detail-label" title={t('Active playbooks can be selected during dispatch. Inactive ones are hidden from selection and rejected if requested.')}>{t('Status')}</span>
            <StatusBadge status={active ? 'Active' : 'Inactive'} />
          </div>
        </div>
      )}

      <div className="card" style={{ marginBottom: '1rem' }}>
        <div className="playbook-side-stats">
          <div title={t('Length of the markdown below, counting unsaved edits. Inlined playbooks add this much to each mission prompt.')}>
            <span>{t('Characters')}</span>
            <strong>{stats.chars.toLocaleString()}</strong>
          </div>
          <div title={t('Number of lines in the markdown below, counting unsaved edits.')}>
            <span>{t('Lines')}</span>
            <strong>{stats.lines.toLocaleString()}</strong>
          </div>
          <div title={t('Number of markdown headings (lines starting with #) below, counting unsaved edits.')}>
            <span>{t('Headings')}</span>
            <strong>{stats.headings.toLocaleString()}</strong>
          </div>
        </div>
      </div>

      <div className="card playbook-editor-card">
        <label className="playbook-editor-field" title={t(PLAYBOOK_FILE_TIP)}>
          <span>{t('File Name')}</span>
          <input
            type="text"
            title={t(PLAYBOOK_FILE_TIP)}
            value={fileName}
            disabled={!canManage}
            onChange={(event) => setFileName(event.target.value)}
            placeholder={t('CSHARP_BACKEND_ARCHITECTURE.md')}
          />
        </label>

        <label className="playbook-editor-field" title={t(PLAYBOOK_DESCRIPTION_TIP)}>
          <span>{t('Description')}</span>
          <input
            type="text"
            title={t(PLAYBOOK_DESCRIPTION_TIP)}
            value={description}
            disabled={!canManage}
            onChange={(event) => setDescription(event.target.value)}
            placeholder={t('Optional summary shown during playbook selection')}
          />
        </label>

        <label className="playbook-toggle" title={t(PLAYBOOK_ACTIVE_TIP)}>
          <input type="checkbox" title={t(PLAYBOOK_ACTIVE_TIP)} checked={active} disabled={!canManage} onChange={(event) => setActive(event.target.checked)} />
          <span>{t('Active and selectable during dispatch')}</span>
        </label>

        <label className="playbook-editor-field" title={t(PLAYBOOK_CONTENT_TIP)}>
          <span>{t('Markdown Content')}</span>
          <textarea
            className="playbook-editor-textarea"
            title={t(PLAYBOOK_CONTENT_TIP)}
            value={content}
            disabled={!canManage}
            onChange={(event) => setContent(event.target.value)}
            spellCheck={false}
            rows={24}
          />
        </label>

        <div className="playbook-editor-actions">
          <button className="btn btn-primary" disabled={!canManage || saving} onClick={handleSave} title={createMode ? t('Create this playbook so it can be selected during dispatch.') : t('Save changes to this playbook. Missions dispatched after saving get the new text; earlier missions keep their copy.')}>
            {saving ? t('Saving...') : createMode ? t('Create Playbook') : t('Save Changes')}
          </button>
          <button className="btn" onClick={() => navigate('/playbooks')} title={t('Return to the playbook list. Unsaved changes are discarded.')}>
            {t('Back')}
          </button>
        </div>
      </div>
    </div>
  );
}
