import { useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import {
  createProjectProfile,
  deleteProjectProfile,
  getProjectProfile,
  listFleets,
  listVessels,
  previewPersonaPrompt,
  updateProjectProfile,
} from '../api/client';
import type { Fleet, PersonaOverride, PersonaPromptPreview, ProjectProfile, Vessel } from '../types/models';
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

const KNOWN_PERSONAS = ['Product Manager', 'Architect', 'Worker', 'Test Engineer', 'Judge', 'Usability Engineer'];

function splitList(value: string): string[] {
  return value.split(/\r?\n|,/).map((item) => item.trim()).filter(Boolean);
}

function blankOverride(): PersonaOverride {
  return { personaName: 'Architect', promptTemplateName: null, additionalInstructions: null, enabled: true };
}

export default function ProjectProfileDetail() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { isAdmin, isTenantAdmin, user } = useAuth();
  const viewer: ScopeViewer = { isAdmin, isTenantAdmin, tenantId: user?.user?.tenantId, userId: user?.user?.id };
  const { t, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();

  const createMode = id === 'new';

  const [profile, setProfile] = useState<ProjectProfile | null>(null);
  const canManage = createMode ? true : (profile ? canEditScoped(viewer, { scope: profile.ownershipScope, tenantId: profile.tenantId, userId: profile.userId }) : (isAdmin || isTenantAdmin));
  const [fleets, setFleets] = useState<Fleet[]>([]);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [name, setName] = useState('Default Project Profile');
  const [description, setDescription] = useState('');
  const [scope, setScope] = useState<'Global' | 'Fleet' | 'Vessel'>('Global');
  const [fleetId, setFleetId] = useState('');
  const [vesselId, setVesselId] = useState('');
  const [isDefault, setIsDefault] = useState(false);
  const [active, setActive] = useState(true);
  const [defaultPipelineId, setDefaultPipelineId] = useState('');
  const [workflowProfileId, setWorkflowProfileId] = useState('');
  const [overrides, setOverrides] = useState<PersonaOverride[]>([]);
  const [skills, setSkills] = useState('');
  const [loading, setLoading] = useState(!createMode);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [jsonOpen, setJsonOpen] = useState(false);

  const [previewPersona, setPreviewPersona] = useState('Architect');
  const [preview, setPreview] = useState<PersonaPromptPreview | null>(null);
  const [previewLoading, setPreviewLoading] = useState(false);

  useEffect(() => {
    void listFleets().then((r) => setFleets(r.objects || [])).catch(() => {});
    void listVessels().then((r) => setVessels(r.objects || [])).catch(() => {});
    if (createMode) return;
    setLoading(true);
    getProjectProfile(id!)
      .then((p) => {
        setProfile(p);
        setName(p.name);
        setDescription(p.description || '');
        setScope(p.scope);
        setFleetId(p.fleetId || '');
        setVesselId(p.vesselId || '');
        setIsDefault(p.isDefault);
        setActive(p.active);
        setDefaultPipelineId(p.defaultPipelineId || '');
        setWorkflowProfileId(p.workflowProfileId || '');
        setOverrides(p.personaOverrides || []);
        setSkills((p.skills || []).join('\n'));
        setError('');
      })
      .catch((err: unknown) => setError(err instanceof Error ? err.message : t('Failed to load project profile.')))
      .finally(() => setLoading(false));
  }, [id, createMode]);

  function buildPayload(): Partial<ProjectProfile> {
    return {
      name,
      description: description || null,
      scope,
      fleetId: scope === 'Fleet' ? (fleetId || null) : null,
      vesselId: scope === 'Vessel' ? (vesselId || null) : null,
      isDefault,
      active,
      defaultPipelineId: defaultPipelineId || null,
      workflowProfileId: workflowProfileId || null,
      personaOverrides: overrides.map((o) => ({
        personaName: o.personaName.trim(),
        promptTemplateName: o.promptTemplateName || null,
        additionalInstructions: o.additionalInstructions || null,
        enabled: o.enabled,
      })),
      skills: splitList(skills),
    };
  }

  async function handleSave() {
    setSaving(true);
    try {
      if (createMode) {
        const created = await createProjectProfile(buildPayload());
        pushToast('success', t('Project profile "{{name}}" created.', { name: created.name }));
        navigate(`/project-profiles/${created.id}`);
      } else {
        const updated = await updateProjectProfile(id!, buildPayload());
        setProfile(updated);
        pushToast('success', t('Project profile "{{name}}" saved.', { name: updated.name }));
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
      await deleteProjectProfile(id!);
      pushToast('warning', t('Project profile deleted.'));
      navigate('/project-profiles');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Delete failed.'));
    }
  }

  async function loadPreview() {
    if (createMode) return;
    setPreviewLoading(true);
    try {
      const data = await previewPersonaPrompt(id!, previewPersona);
      setPreview(data);
      setError('');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Preview failed.'));
    } finally {
      setPreviewLoading(false);
    }
  }

  function updateOverride(index: number, patch: Partial<PersonaOverride>) {
    setOverrides((current) => current.map((o, i) => (i === index ? { ...o, ...patch } : o)));
  }

  if (loading) return <p className="text-dim">{t('Loading...')}</p>;

  return (
    <div>
      <PageHeader
        breadcrumb={
          <>
            <Link to="/project-profiles">{t('Project Profiles')}</Link> <span className="breadcrumb-sep">&gt;</span> <span>{createMode ? t('New Project Profile') : name}</span>
          </>
        }
        title={createMode ? t('Create Project Profile') : name}
        actions={
          <>
            {!createMode && <StatusBadge status={active ? 'Active' : 'Inactive'} />}
            {!createMode && profile && (
              <button className="btn btn-sm" onClick={() => setJsonOpen(true)}>{t('View JSON')}</button>
            )}
            {!createMode && canManage && (
              <button className="btn btn-sm btn-danger" onClick={() => setConfirmOpen(true)}>{t('Delete')}</button>
            )}
          </>
        }
      />

      <ErrorModal error={error} onClose={() => setError('')} />
      <JsonViewer open={jsonOpen} title={profile?.name || t('Project Profile')} data={profile} onClose={() => setJsonOpen(false)} />
      <ConfirmDialog
        open={confirmOpen}
        title={t('Delete Project Profile')}
        message={t('Delete this project profile? This cannot be undone.')}
        onConfirm={handleDelete}
        onCancel={() => setConfirmOpen(false)}
      />

      {!canManage && (
        <div className="alert alert-error" style={{ marginBottom: '1rem' }}>
          {t('You can view this project profile, but only tenant administrators can change it.')}
        </div>
      )}

      {!createMode && profile && (
        <div className="detail-grid" style={{ marginBottom: '1rem' }}>
          <div className="detail-field">
            <span className="detail-label">{t('ID')}</span>
            <span className="id-display">
              <span className="mono">{profile.id}</span>
              <CopyButton text={profile.id} />
            </span>
          </div>
          <div className="detail-field">
            <span className="detail-label">{t('Created')}</span>
            <span>{formatDateTime(profile.createdUtc)}</span>
          </div>
          <div className="detail-field">
            <span className="detail-label">{t('Last Updated')}</span>
            <span>{formatDateTime(profile.lastUpdateUtc)}</span>
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
            <span>{t('Description')}</span>
            <input type="text" value={description} onChange={(e) => setDescription(e.target.value)} disabled={!canManage} />
          </label>
          <label>
            <span>{t('Scope')}</span>
            <select value={scope} onChange={(e) => setScope(e.target.value as typeof scope)} disabled={!canManage}>
              <option value="Global">{t('Global')}</option>
              <option value="Fleet">{t('Fleet')}</option>
              <option value="Vessel">{t('Vessel')}</option>
            </select>
          </label>
          {scope === 'Fleet' && (
            <label>
              <span>{t('Fleet')}</span>
              <select value={fleetId} onChange={(e) => setFleetId(e.target.value)} disabled={!canManage}>
                <option value="">{t('Select a fleet...')}</option>
                {fleets.map((f) => <option key={f.id} value={f.id}>{f.name}</option>)}
              </select>
            </label>
          )}
          {scope === 'Vessel' && (
            <label>
              <span>{t('Vessel')}</span>
              <select value={vesselId} onChange={(e) => setVesselId(e.target.value)} disabled={!canManage}>
                <option value="">{t('Select a vessel...')}</option>
                {vessels.map((v) => <option key={v.id} value={v.id}>{v.name}</option>)}
              </select>
            </label>
          )}
          <label>
            <span>{t('Default Pipeline ID')}</span>
            <input type="text" value={defaultPipelineId} onChange={(e) => setDefaultPipelineId(e.target.value)} placeholder="ppl_..." disabled={!canManage} />
          </label>
          <label>
            <span>{t('Workflow Profile ID')}</span>
            <input type="text" value={workflowProfileId} onChange={(e) => setWorkflowProfileId(e.target.value)} placeholder="wfp_..." disabled={!canManage} />
          </label>
          <label className="checkbox-row">
            <input type="checkbox" checked={isDefault} onChange={(e) => setIsDefault(e.target.checked)} disabled={!canManage} />
            <span>{t('Default for scope')}</span>
          </label>
          <label className="checkbox-row">
            <input type="checkbox" checked={active} onChange={(e) => setActive(e.target.checked)} disabled={!canManage} />
            <span>{t('Active')}</span>
          </label>
        </div>
      </div>

      <div className="card" style={{ padding: '1rem', marginBottom: '1rem' }}>
        <div className="view-header" style={{ marginBottom: '0.5rem' }}>
          <h3>{t('Persona Overrides')}</h3>
          {canManage && (
            <button className="btn" onClick={() => setOverrides((c) => [...c, blankOverride()])}>+ {t('Override')}</button>
          )}
        </div>
        <p className="text-dim" style={{ marginTop: 0 }}>
          {t('Swap a persona prompt template and/or append per-project instructions. Applied to this project\'s pipeline personas at dispatch.')}
        </p>
        {overrides.length === 0 ? (
          <p className="text-dim">{t('No persona overrides. Personas use their built-in prompts.')}</p>
        ) : (
          overrides.map((o, index) => (
            <div key={index} className="card" style={{ padding: '0.75rem', marginBottom: '0.5rem' }}>
              <div className="form-grid">
                <label>
                  <span>{t('Persona')}</span>
                  <input list="known-personas" type="text" value={o.personaName} onChange={(e) => updateOverride(index, { personaName: e.target.value })} disabled={!canManage} />
                </label>
                <label>
                  <span>{t('Prompt Template Name')}</span>
                  <input type="text" value={o.promptTemplateName || ''} onChange={(e) => updateOverride(index, { promptTemplateName: e.target.value || null })} placeholder="persona.architect" disabled={!canManage} />
                </label>
                <label className="checkbox-row">
                  <input type="checkbox" checked={o.enabled} onChange={(e) => updateOverride(index, { enabled: e.target.checked })} disabled={!canManage} />
                  <span>{t('Enabled')}</span>
                </label>
                {canManage && (
                  <button className="btn btn-danger" onClick={() => setOverrides((c) => c.filter((_, i) => i !== index))}>{t('Remove')}</button>
                )}
              </div>
              <label style={{ display: 'block', marginTop: '0.5rem' }}>
                <span>{t('Additional Instructions')}</span>
                <textarea rows={3} value={o.additionalInstructions || ''} onChange={(e) => updateOverride(index, { additionalInstructions: e.target.value || null })} disabled={!canManage} />
              </label>
            </div>
          ))
        )}
        <datalist id="known-personas">
          {KNOWN_PERSONAS.map((p) => <option key={p} value={p} />)}
        </datalist>
      </div>

      <div className="card" style={{ padding: '1rem', marginBottom: '1rem' }}>
        <h3>{t('Skills')}</h3>
        <p className="text-dim" style={{ marginTop: 0 }}>{t('One skill per line. Attached to this project.')}</p>
        <textarea rows={4} value={skills} onChange={(e) => setSkills(e.target.value)} disabled={!canManage} placeholder={'dotnet\ntdd'} />
      </div>

      {!createMode && (
        <div className="card" style={{ padding: '1rem' }}>
          <div className="view-header" style={{ marginBottom: '0.5rem' }}>
            <h3>{t('Persona Prompt Diff')}</h3>
            <div className="view-actions">
              <input list="known-personas" type="text" value={previewPersona} onChange={(e) => setPreviewPersona(e.target.value)} />
              <button className="btn" onClick={loadPreview} disabled={previewLoading}>
                {previewLoading ? t('Loading...') : t('Preview')}
              </button>
            </div>
          </div>
          <p className="text-dim" style={{ marginTop: 0 }}>
            {t('Save the profile first, then preview the base vs. effective persona prompt for the current overrides.')}
          </p>
          {preview && (
            <>
              <p className="text-dim">
                {preview.isOverridden
                  ? t('Overridden: {{base}} to {{eff}}', { base: preview.baseTemplateName, eff: preview.effectiveTemplateName })
                  : t('No override applied for {{persona}}.', { persona: preview.personaName })}
              </p>
              <div className="diff-two-col" style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '1rem' }}>
                <div>
                  <strong>{t('Base')}</strong>
                  <pre className="code-block" style={{ whiteSpace: 'pre-wrap', maxHeight: '360px', overflow: 'auto' }}>{preview.basePrompt}</pre>
                </div>
                <div>
                  <strong>{t('Effective')}</strong>
                  <pre className="code-block" style={{ whiteSpace: 'pre-wrap', maxHeight: '360px', overflow: 'auto' }}>{preview.effectivePrompt}</pre>
                </div>
              </div>
            </>
          )}
        </div>
      )}

      <div className="inline-actions" style={{ marginTop: '1rem' }}>
        {canManage && (
          <button className="btn btn-primary" onClick={handleSave} disabled={saving}>
            {saving ? t('Saving...') : createMode ? t('Create Project Profile') : t('Save Changes')}
          </button>
        )}
        <button className="btn" onClick={() => navigate('/project-profiles')}>{t('Back')}</button>
      </div>
    </div>
  );
}
