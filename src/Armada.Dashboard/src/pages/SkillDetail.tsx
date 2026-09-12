import { useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { createSkill, deleteSkill, getSkill, updateSkill } from '../api/client';
import type { Skill } from '../types/models';
import { useAuth } from '../context/AuthContext';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import CopyButton from '../components/shared/CopyButton';
import ErrorModal from '../components/shared/ErrorModal';
import JsonViewer from '../components/shared/JsonViewer';
import PageHeader from '../components/shared/PageHeader';
import StatusBadge from '../components/shared/StatusBadge';
import { canEdit as canEditScoped, type ScopeViewer } from '../lib/scoping';

export default function SkillDetail() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { isAdmin, isTenantAdmin, user } = useAuth();
  const viewer: ScopeViewer = { isAdmin, isTenantAdmin, tenantId: user?.user?.tenantId, userId: user?.user?.id };
  const { t, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();

  const createMode = id === 'new';

  const [skill, setSkill] = useState<Skill | null>(null);
  const canManage = createMode ? true : (skill ? canEditScoped(viewer, skill) : (isAdmin || isTenantAdmin));
  const [name, setName] = useState('Untitled Skill');
  const [description, setDescription] = useState('');
  const [category, setCategory] = useState('');
  const [content, setContent] = useState('');
  const [active, setActive] = useState(true);
  const [loading, setLoading] = useState(!createMode);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [jsonOpen, setJsonOpen] = useState(false);

  useEffect(() => {
    if (createMode) return;
    setLoading(true);
    getSkill(id!)
      .then((s) => {
        setSkill(s);
        setName(s.name);
        setDescription(s.description || '');
        setCategory(s.category || '');
        setContent(s.content || '');
        setActive(s.active);
        setError('');
      })
      .catch((err: unknown) => setError(err instanceof Error ? err.message : t('Failed to load skill.')))
      .finally(() => setLoading(false));
  }, [id, createMode]);

  function buildPayload(): Partial<Skill> {
    return {
      name,
      description: description || null,
      category: category || null,
      content,
      active,
    };
  }

  async function handleSave() {
    setSaving(true);
    try {
      if (createMode) {
        const created = await createSkill(buildPayload());
        pushToast('success', t('Skill "{{name}}" created.', { name: created.name }));
        navigate(`/skills/${created.id}`);
      } else {
        const updated = await updateSkill(id!, buildPayload());
        setSkill(updated);
        pushToast('success', t('Skill "{{name}}" saved.', { name: updated.name }));
      }
      setError('');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Save failed.'));
    } finally {
      setSaving(false);
    }
  }

  async function handleDelete() {
    setConfirmOpen(false);
    try {
      await deleteSkill(id!);
      pushToast('warning', t('Skill deleted.'));
      navigate('/skills');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Delete failed.'));
    }
  }

  if (loading) return <p className="text-dim">{t('Loading...')}</p>;

  return (
    <div>
      <PageHeader
        breadcrumb={
          <>
            <Link to="/skills">{t('Skills')}</Link> <span className="breadcrumb-sep">&gt;</span> <span>{createMode ? t('New Skill') : name}</span>
          </>
        }
        title={createMode ? t('Create Skill') : name}
        actions={
          <>
            {!createMode && <StatusBadge status={active ? 'Active' : 'Inactive'} />}
            {!createMode && skill && (
              <button className="btn btn-sm" onClick={() => setJsonOpen(true)}>{t('View JSON')}</button>
            )}
            {!createMode && canManage && (
              <button className="btn btn-sm btn-danger" onClick={() => setConfirmOpen(true)}>{t('Delete')}</button>
            )}
          </>
        }
      />

      <ErrorModal error={error} onClose={() => setError('')} />
      <JsonViewer open={jsonOpen} title={skill?.name || t('Skill')} data={skill} onClose={() => setJsonOpen(false)} />
      <ConfirmDialog
        open={confirmOpen}
        title={t('Delete Skill')}
        message={t('Delete this skill? This cannot be undone.')}
        onConfirm={handleDelete}
        onCancel={() => setConfirmOpen(false)}
      />

      {!canManage && (
        <div className="alert alert-error" style={{ marginBottom: '1rem' }}>
          {t('You can view this skill, but only tenant administrators can change it.')}
        </div>
      )}

      {!createMode && skill && (
        <div className="detail-grid" style={{ marginBottom: '1rem' }}>
          <div className="detail-field">
            <span className="detail-label">{t('ID')}</span>
            <span className="id-display">
              <span className="mono">{skill.id}</span>
              <CopyButton text={skill.id} />
            </span>
          </div>
          <div className="detail-field">
            <span className="detail-label">{t('Created')}</span>
            <span>{formatDateTime(skill.createdUtc)}</span>
          </div>
          <div className="detail-field">
            <span className="detail-label">{t('Last Updated')}</span>
            <span>{formatDateTime(skill.lastUpdateUtc)}</span>
          </div>
          <div className="detail-field">
            <span className="detail-label">{t('Status')}</span>
            <StatusBadge status={active ? 'Active' : 'Inactive'} />
          </div>
        </div>
      )}

      <div className="card" style={{ padding: '1rem', marginBottom: '1rem' }}>
        <div className="form-grid">
          <label>
            <span>{t('Name')}</span>
            <input type="text" value={name} onChange={(e) => setName(e.target.value)} disabled={!canManage} />
          </label>
          <label>
            <span>{t('Category')}</span>
            <input type="text" value={category} onChange={(e) => setCategory(e.target.value)} placeholder="engineering" disabled={!canManage} />
          </label>
          <label>
            <span>{t('Description')}</span>
            <input type="text" value={description} onChange={(e) => setDescription(e.target.value)} disabled={!canManage} />
          </label>
          <label className="checkbox-row">
            <input type="checkbox" checked={active} onChange={(e) => setActive(e.target.checked)} disabled={!canManage} />
            <span>{t('Active')}</span>
          </label>
        </div>
      </div>

      <div className="card playbook-editor-card">
        <label className="playbook-editor-field">
          <span>{t('Content')}</span>
          <p className="text-dim" style={{ margin: '0 0 0.35rem' }}>{t('Markdown or plain text injected into mission prompts for projects that attach this skill.')}</p>
          <textarea
            className="playbook-editor-textarea"
            rows={16}
            value={content}
            onChange={(e) => setContent(e.target.value)}
            disabled={!canManage}
            spellCheck={false}
          />
        </label>

        <div className="playbook-editor-actions">
          <button className="btn btn-primary" disabled={!canManage || saving} onClick={handleSave}>
            {saving ? t('Saving...') : createMode ? t('Create Skill') : t('Save Changes')}
          </button>
          <button className="btn" onClick={() => navigate('/skills')}>
            {t('Back')}
          </button>
        </div>
      </div>
    </div>
  );
}
