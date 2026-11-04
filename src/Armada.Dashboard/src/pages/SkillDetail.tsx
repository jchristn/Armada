import { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
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
import { ErrorState } from '../components/shared/StateBlocks';
import StatusBadge from '../components/shared/StatusBadge';
import { canEdit as canEditScoped, type ScopeViewer } from '../lib/scoping';
import { useDeleteProgress } from '../lib/useDeleteProgress';

const SKILL_NAME_TIP = 'Unique skill name, for example Conventional Commits. Project profiles list skills by this name, so renaming one can break those references. Required.';
const SKILL_CATEGORY_TIP = 'Optional group for browsing the skill directory, for example engineering or testing. It does not change how the skill is used.';
const SKILL_DESCRIPTION_TIP = 'Optional one-line summary shown in the skill list. It is not sent to the captain.';
const SKILL_CONTENT_TIP = 'The instruction text, in markdown or plain text. It is added under a Skills heading to the prompt of every mission on a vessel whose project profile lists this skill.';
const SKILL_ACTIVE_TIP = 'On: the skill is added to mission prompts where a project profile lists it. Off: it is skipped, but stays listed on those profiles.';

export default function SkillDetail() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { isAdmin, isTenantAdmin, user } = useAuth();
  const viewer: ScopeViewer = { isAdmin, isTenantAdmin, tenantId: user?.user?.tenantId, userId: user?.user?.id };
  const { t, formatDateTime } = useLocale();
  const deletion = useDeleteProgress();
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
  // A failed entity load (for example a stale link) renders an error state instead of an empty editor.
  const [loadFailure, setLoadFailure] = useState<{ id: string | undefined; message: string } | null>(null);
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
      .catch((err: unknown) => setLoadFailure({ id, message: err instanceof Error ? err.message : t('Failed to load skill.') }))
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
    if (!(await deletion.run({ kind: 'skill', action: () => deleteSkill(id!), errorMessage: (err) => err instanceof Error ? err.message : t('Delete failed.') }))) return;
    pushToast('warning', t('Skill deleted.'));
    navigate('/skills');
  }

  if (loading) return <p className="text-dim">{t('Loading...')}</p>;

  if (!createMode && loadFailure && loadFailure.id === id) {
    return (
      <div>
        <PageHeader breadcrumb={[{ label: t('Skills'), to: '/skills' }, { label: id || '' }]} title={id || ''} />
        <ErrorState message={loadFailure.message} />
      </div>
    );
  }

  return (
    <div>
      <PageHeader
        breadcrumb={[
          { label: t('Skills'), to: '/skills' },
          { label: createMode ? t('New Skill') : name },
        ]}
        title={createMode ? t('Create Skill') : name}
        actions={
          <>
            {!createMode && <StatusBadge status={active ? 'Active' : 'Inactive'} />}
            {!createMode && skill && (
              <button className="btn btn-sm" onClick={() => setJsonOpen(true)} title={t('Show this skill\'s full record as JSON, as the API returns it.')}>{t('View JSON')}</button>
            )}
            {!createMode && canManage && (
              <button className="btn btn-sm btn-danger" onClick={() => setConfirmOpen(true)} title={t('Permanently delete this skill. Project profiles that list it simply skip it. You confirm first.')}>{t('Delete')}</button>
            )}
          </>
        }
      />

      <ErrorModal error={error} onClose={() => setError('')} />
      <JsonViewer open={jsonOpen} title={skill?.name || t('Skill')} data={skill} onClose={() => setJsonOpen(false)} />
      {deletion.dialog}
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
            <span className="detail-label" title={t('Unique skill ID (skl_ prefix). Project profiles can refer to a skill by this ID or by its name.')}>{t('ID')}</span>
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
            <span className="detail-label" title={t('Only active skills are added to mission prompts. Inactive skills stay listed on project profiles but are skipped.')}>{t('Status')}</span>
            <StatusBadge status={active ? 'Active' : 'Inactive'} />
          </div>
        </div>
      )}

      <div className="card" style={{ padding: '1rem', marginBottom: '1rem' }}>
        <div className="detail-form-grid">
          <label className="form-field" title={t(SKILL_NAME_TIP)}>
            <span>{t('Name')}</span>
            <input type="text" title={t(SKILL_NAME_TIP)} value={name} onChange={(e) => setName(e.target.value)} disabled={!canManage} />
          </label>
          <label className="form-field" title={t(SKILL_CATEGORY_TIP)}>
            <span>{t('Category')}</span>
            <input type="text" title={t(SKILL_CATEGORY_TIP)} value={category} onChange={(e) => setCategory(e.target.value)} placeholder="engineering" disabled={!canManage} />
          </label>
          <label className="form-field" title={t(SKILL_DESCRIPTION_TIP)}>
            <span>{t('Description')}</span>
            <input type="text" title={t(SKILL_DESCRIPTION_TIP)} value={description} onChange={(e) => setDescription(e.target.value)} disabled={!canManage} />
          </label>
          <label className="checkbox-row" title={t(SKILL_ACTIVE_TIP)}>
            <input type="checkbox" title={t(SKILL_ACTIVE_TIP)} checked={active} onChange={(e) => setActive(e.target.checked)} disabled={!canManage} />
            <span>{t('Active')}</span>
          </label>
        </div>
      </div>

      <div className="card playbook-editor-card">
        <label className="playbook-editor-field" title={t(SKILL_CONTENT_TIP)}>
          <span>{t('Content')}</span>
          <p className="text-dim" style={{ margin: '0 0 0.35rem' }}>{t('Markdown or plain text injected into mission prompts for projects that attach this skill.')}</p>
          <textarea
            className="playbook-editor-textarea"
            title={t(SKILL_CONTENT_TIP)}
            rows={16}
            value={content}
            onChange={(e) => setContent(e.target.value)}
            disabled={!canManage}
            spellCheck={false}
          />
        </label>

        <div className="playbook-editor-actions">
          <button className="btn btn-primary" disabled={!canManage || saving} onClick={handleSave} title={createMode ? t('Create this skill. Add it to a project profile to use it.') : t('Save changes to this skill. Missions started after saving get the new text.')}>
            {saving ? t('Saving...') : createMode ? t('Create Skill') : t('Save Changes')}
          </button>
          <button className="btn" onClick={() => navigate('/skills')} title={t('Return to the skill list. Unsaved changes are discarded.')}>
            {t('Back')}
          </button>
        </div>
      </div>
    </div>
  );
}
