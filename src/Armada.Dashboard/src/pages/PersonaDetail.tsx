import DialogShell from '../components/shared/DialogShell';
import PersonaPriorityEditor from '../components/shared/PersonaPriorityEditor';
import { personaRoutingForm, validatePersonaPriorities, prioritySummary } from '../lib/personaPriorities';
import { useState, useEffect, useCallback } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import { createPersona, deletePersona, getPersona, getPromptTemplate, listCaptains, listPromptTemplates, resetPromptTemplate, updatePersona, updatePromptTemplate, apiErrorMessage } from '../api/client';
import { listAll } from '../api/paging';
import type { Captain, Persona, PromptTemplate } from '../types/models';
import ActionMenu from '../components/shared/ActionMenu';
import CaptainPicker from '../components/shared/CaptainPicker';
import CaptainRef from '../components/shared/CaptainRef';
import JsonViewer from '../components/shared/JsonViewer';
import PageHeader from '../components/shared/PageHeader';
import StatusBadge from '../components/shared/StatusBadge';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import CopyButton from '../components/shared/CopyButton';
import ErrorModal from '../components/shared/ErrorModal';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import { useAuth } from '../context/AuthContext';
import { canEdit as canEditScoped, type ScopeViewer } from '../lib/scoping';
import { buildPersonaDuplicatePayload } from '../lib/duplicates';
import { useDeleteProgress } from '../lib/useDeleteProgress';

const PERSONA_DESCRIPTION_TIP = 'Optional note on what this persona is for. Shown in lists and pickers; it is not sent to the captain.';
const PERSONA_TEMPLATE_TIP = 'The prompt template whose text tells the captain how to act in this role. Required. Manage templates on the Prompts tab.';
const DEFAULT_CAPTAIN_TIP = 'Captain tried first for this persona\'s missions when Captain priorities is empty; if it is busy, work falls back by tier. None uses normal routing. Ignored once priorities are set.';
const TEMPLATE_DESCRIPTION_TIP = 'Optional note on what this prompt template is for. Shown in template lists; it is not sent to the captain.';
const PROMPT_CONTENT_TIP = 'The instruction text sent to the captain for this role. Placeholders such as {MissionTitle} are filled in per mission. Saving changes the template for every persona that uses it.';

export default function PersonaDetail() {
  const { t, formatDateTime } = useLocale();
  const deletion = useDeleteProgress();
  const { pushToast } = useNotifications();
  const { isAdmin, isTenantAdmin, user } = useAuth();
  const viewer: ScopeViewer = { isAdmin, isTenantAdmin, tenantId: user?.user?.tenantId, userId: user?.user?.id };
  const { name } = useParams<{ name: string }>();
  const navigate = useNavigate();
  const [persona, setPersona] = useState<Persona | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Edit modal
  const [showForm, setShowForm] = useState(false);
  const [form, setForm] = useState<{ description: string; promptTemplateName: string; defaultCaptainId: string | null }>({ description: '', promptTemplateName: '', defaultCaptainId: null });
  const [captains, setCaptains] = useState<Captain[]>([]);
  const [templateNames, setTemplateNames] = useState<string[]>([]);
  const [promptTemplate, setPromptTemplate] = useState<PromptTemplate | null>(null);
  const [promptDescription, setPromptDescription] = useState('');
  const [promptContent, setPromptContent] = useState('');
  const [promptDirty, setPromptDirty] = useState(false);
  const [promptSaving, setPromptSaving] = useState(false);
  const [promptLoading, setPromptLoading] = useState(false);
  const [promptError, setPromptError] = useState('');

  const [routing, setRouting] = useState(personaRoutingForm());

  // JSON viewer
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });

  // Confirm
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

  const clearPromptEditor = useCallback(() => {
    setPromptTemplate(null);
    setPromptDescription('');
    setPromptContent('');
    setPromptDirty(false);
    setPromptError('');
  }, []);

  const loadPromptForPersona = useCallback(async (templateName: string) => {
    try {
      setPromptLoading(true);
      const template = await getPromptTemplate(templateName);
      setPromptTemplate(template);
      setPromptDescription(template.description ?? '');
      setPromptContent(template.content);
      setPromptDirty(false);
      setPromptError('');
    } catch (err) {
      clearPromptEditor();
      setPromptError(err instanceof Error ? err.message : t('Failed to load backing prompt template.'));
    } finally {
      setPromptLoading(false);
    }
  }, [clearPromptEditor, t]);

  const load = useCallback(async () => {
    if (!name) return;
    try {
      setLoading(true);
      const found = await getPersona(name);
      setPersona(found);
      const templateResult = await listAll(listPromptTemplates);
      setTemplateNames(templateResult.objects.map(t => t.name));
      const captainResult = await listAll(listCaptains);
      setCaptains(captainResult.objects);
      if (found.promptTemplateName) {
        await loadPromptForPersona(found.promptTemplateName);
      } else {
        clearPromptEditor();
      }
      setError('');
    } catch (err) {
      setError(err instanceof Error ? err.message : t('Failed to load persona.'));
    } finally {
      setLoading(false);
    }
  }, [clearPromptEditor, loadPromptForPersona, name, t]);

  useEffect(() => { load(); }, [load]);

  function openEdit() {
    if (!persona) return;
    setForm({ description: persona.description ?? '', promptTemplateName: persona.promptTemplateName ?? '', defaultCaptainId: persona.defaultCaptainId ?? null });
    setRouting(personaRoutingForm(persona));
    setShowForm(true);
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!persona) return;
    try {
      const validation = validatePersonaPriorities(routing.captainPriorities);
      if (validation) throw new Error(t(validation));
      await updatePersona(persona.name, { description: form.description, promptTemplateName: form.promptTemplateName, defaultCaptainId: form.defaultCaptainId, ...routing });
      setShowForm(false);
      pushToast('success', t('Persona "{{name}}" saved.', { name: persona.name }));
      load();
    } catch (err: unknown) { setError(apiErrorMessage(err, t('Save failed.'))); }
  }

  async function handleSavePrompt() {
    if (!promptTemplate) return;

    try {
      setPromptSaving(true);
      const result = await updatePromptTemplate(promptTemplate.name, {
        content: promptContent,
        description: promptDescription.trim() || undefined,
      });
      setPromptTemplate(result);
      setPromptDescription(result.description ?? '');
      setPromptContent(result.content);
      setPromptDirty(false);
      setPromptError('');
      pushToast('success', t('Prompt template "{{name}}" saved.', { name: result.name }));
    } catch (err) {
      setError(err instanceof Error ? err.message : t('Prompt save failed.'));
    } finally {
      setPromptSaving(false);
    }
  }

  function handleResetPrompt() {
    if (!promptTemplate?.isBuiltIn) return;

    setConfirm({
      open: true,
      title: t('Reset Backing Prompt'),
      message: t('Reset prompt template "{{name}}" to its built-in default content? Your customizations will be lost.', { name: promptTemplate.name }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          const result = await resetPromptTemplate(promptTemplate.name);
          setPromptTemplate(result);
          setPromptDescription(result.description ?? '');
          setPromptContent(result.content);
          setPromptDirty(false);
          setPromptError('');
          pushToast('success', t('Prompt template "{{name}}" reset to default.', { name: result.name }));
        } catch (err) {
          setError(err instanceof Error ? err.message : t('Prompt reset failed.'));
        }
      },
    });
  }

  function handleDelete() {
    if (!persona) return;
    if (persona.isBuiltIn) {
      setError(t('Built-in personas cannot be deleted.'));
      return;
    }
    setConfirm({
      open: true,
      title: t('Delete Persona'),
      message: t('Delete persona "{{name}}"? This cannot be undone.', { name: persona.name }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        if (!(await deletion.run({ kind: 'persona', action: () => deletePersona(persona.name) }))) return;
        pushToast('warning', t('Persona "{{name}}" deleted.', { name: persona.name }));
        navigate('/personas');
      },
    });
  }

  async function handleDuplicate() {
    if (!persona) return;
    try {
      const created = await createPersona(buildPersonaDuplicatePayload(persona));
      pushToast('success', t('Persona "{{name}}" duplicated.', { name: created.name }));
      navigate(`/personas/${encodeURIComponent(created.name)}`);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Duplicate failed.'));
    }
  }

  if (loading) return <p className="text-dim">{t('Loading...')}</p>;
  if (error && !persona) return <ErrorModal error={error} onClose={() => setError('')} />;
  if (!persona) return <p className="text-dim">{t('Persona not found.')}</p>;

  return (
    <div>
      <PageHeader
        breadcrumb={[
          { label: t('Personas'), to: '/personas' },
          { label: persona.name },
        ]}
        title={persona.name}
        actions={
          <>
            <ActionMenu id={`persona-${persona.name}`} items={[
              { label: 'View JSON', title: 'Show this persona\'s full record as JSON, as the API returns it.', onClick: () => setJsonData({ open: true, title: t('Persona: {{name}}', { name: persona.name }), data: persona }) },
              ...(canEditScoped(viewer, persona) ? [{ label: 'Edit', title: 'Change this persona\'s description, prompt template, default captain and captain priorities.', onClick: openEdit }] : []),
              { label: 'Duplicate', title: 'Create a copy of this persona under a new name, including its captain priorities, then open it.', onClick: () => void handleDuplicate() },
              ...(persona.promptTemplateName ? [{ label: 'Open Backing Prompt', title: 'Open the prompt template this persona uses on its own page. Changes there affect every persona that uses it.', onClick: () => navigate(`/prompt-templates/${encodeURIComponent(persona.promptTemplateName)}`) }] : []),
              ...(canEditScoped(viewer, persona) ? [{ label: 'Delete', danger: true as const, title: 'Permanently delete this persona. Built-in personas cannot be deleted. You confirm first.', onClick: handleDelete }] : []),
            ]} />
          </>
        }
      />

      <ErrorModal error={error} onClose={() => setError('')} />

      {/* Edit Modal */}
      <DialogShell open={showForm} title={t('Edit Persona')} onClose={() => setShowForm(false)} size="lg" className="persona-edit-dialog"
        footer={<><button type="submit" form="persona-edit-form" className="btn btn-primary" title={t('Save changes to this persona. Missions assigned after saving use the new settings.')}>{t('Save')}</button>
          <button type="button" className="btn" onClick={() => setShowForm(false)}>{t('Cancel')}</button></>}>
        <form id="persona-edit-form" className="persona-edit-layout" onSubmit={handleSubmit}>
          <div className="persona-edit-settings">
            <label title={t(PERSONA_DESCRIPTION_TIP)}>{t('Description')}<textarea title={t(PERSONA_DESCRIPTION_TIP)} value={form.description} onChange={e => setForm({ ...form, description: e.target.value })} rows={4} /></label>
            <label title={t(PERSONA_TEMPLATE_TIP)}>{t('Prompt Template Name')}
              <select title={t(PERSONA_TEMPLATE_TIP)} value={form.promptTemplateName} onChange={e => setForm({ ...form, promptTemplateName: e.target.value })} required>
                <option value="">{t('Select a template...')}</option>
                {templateNames.map(templateName => (
                  <option key={templateName} value={templateName}>{templateName}</option>
                ))}
              </select>
            </label>
            <label title={t(DEFAULT_CAPTAIN_TIP)}>{t('Default Captain')}
              <CaptainPicker
                title={t(DEFAULT_CAPTAIN_TIP)}
                captains={captains}
                value={form.defaultCaptainId}
                onChange={(captainId) => setForm({ ...form, defaultCaptainId: captainId })}
                autoLabel={t('None (default routing)')}
              />
              <span className="text-dim" style={{ fontSize: '0.75rem', display: 'block', marginTop: '0.3rem' }}>
                {t('Used only when captain priorities is empty.')}
              </span>
            </label>
          </div>
          <PersonaPriorityEditor value={routing} onChange={setRouting} />
        </form>
      </DialogShell>

      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      {deletion.dialog}
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message}
        onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />

      {/* Persona Info */}
      <div className="detail-grid">
        <div className="detail-field">
          <span className="detail-label" title={t('Unique persona ID. Missions and pipelines refer to personas by name.')}>{t('ID')}</span>
          <span className="id-display">
            <span className="mono">{persona.id}</span>
            <CopyButton text={persona.id} />
          </span>
        </div>
        <div className="detail-field"><span className="detail-label">{t('Name')}</span><span>{persona.name}</span></div>
        <div className="detail-field"><span className="detail-label" title={t(PERSONA_DESCRIPTION_TIP)}>{t('Description')}</span><span>{persona.description || '-'}</span></div>
        <div className="detail-field">
          <span className="detail-label" title={t(PERSONA_TEMPLATE_TIP)}>{t('Prompt Template Name')}</span>
          <span>
            {persona.promptTemplateName ? (
              <Link to={`/prompt-templates/${encodeURIComponent(persona.promptTemplateName)}`}>{persona.promptTemplateName}</Link>
            ) : '-'}
          </span>
        </div>
        <div className="detail-field">
          <span className="detail-label" title={t(DEFAULT_CAPTAIN_TIP)}>{t('Default Captain')}</span>
          <span><CaptainRef captainId={persona.defaultCaptainId} captains={captains} autoLabel={t('None (default routing)')} /></span>
        </div>
        <div className="detail-field">
          <span className="detail-label" title={t('Captain classes tried in order (arrow = then) when assigning this persona\'s missions. None means the default captain and normal routing are used.')}>{t('Captain priorities')}</span>
          <span>{(persona.captainPriorities ?? []).map(prioritySummary).join(' → ') || t('None (default routing)')}</span>
        </div>
        <div className="detail-field"><span className="detail-label" title={t('Try lower priorities moves down the list when a priority has no free captain. Wait for the first priority uses only priority 1 and waits for it.')}>{t('When a priority is unavailable')}</span><span>{t(persona.priorityAvailability === 'WaitForFirstPriority' ? 'Wait for the first priority' : 'Try lower priorities')}</span></div>
        <div className="detail-field"><span className="detail-label" title={t('Wait keeps the mission Pending until a listed captain is free. Use any eligible captain falls back to normal persona and tier routing.')}>{t('When all priorities are unavailable')}</span><span>{t(persona.priorityAvailability === 'WaitForFirstPriority' || persona.priorityExhaustion !== 'UseEligibleCaptain' ? 'Wait' : 'Use any eligible captain')}</span></div>
        <div className="detail-field"><span className="detail-label" title={t('Built-in personas ship with Armada and cannot be deleted.')}>{t('Built-in')}</span>{persona.isBuiltIn ? <span className="tag built-in" title={t('Ships with Armada. It cannot be deleted.')}>{t('Built-in')}</span> : <span className="text-dim">{t('No')}</span>}</div>
        <div className="detail-field"><span className="detail-label" title={t('Marks the persona as in use or retired. It is a label only: dispatch does not skip inactive personas.')}>{t('Active')}</span><StatusBadge status={persona.active ? 'Active' : 'Inactive'} /></div>
        <div className="detail-field"><span className="detail-label">{t('Created')}</span><span title={persona.createdUtc}>{formatDateTime(persona.createdUtc)}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Last Updated')}</span><span>{formatDateTime(persona.lastUpdateUtc)}</span></div>
      </div>

      <div className="card detail-section" style={{ marginTop: '1.5rem' }}>
        <div className="detail-section-header">
          <div>
            <h3 title={t('The prompt template this persona uses. Editing it here changes the template itself, so every persona using it is affected.')}>{t('Backing Prompt')}</h3>
            <div className="text-dim backlog-section-note">
              {t('This is the prompt template Armada resolves when this persona is assigned to a mission. Edit it here to change future mission instructions.')}
            </div>
          </div>
          <div className="inline-actions">
            {promptTemplate ? <span className={`tag ${promptTemplate.category.toLowerCase()}`} title={t('Template category: the part of Armada that uses this template, such as persona, mission, commit or landing.')}>{t(promptTemplate.category)}</span> : null}
            {promptTemplate?.isBuiltIn ? <span className="tag built-in" title={t('Ships with Armada. It cannot be deleted, and Reset to Default restores its original text.')}>{t('Built-in')}</span> : null}
          </div>
        </div>

        {promptLoading ? (
          <p className="text-dim">{t('Loading backing prompt...')}</p>
        ) : promptTemplate ? (
          <>
            <div className="detail-form-grid">
              <div className="form-field">
                <label title={t('Name of the backing prompt template. Click it to open the template\'s own page.')}>{t('Template Name')}</label>
                <div>
                  <Link to={`/prompt-templates/${encodeURIComponent(promptTemplate.name)}`}>{promptTemplate.name}</Link>
                </div>
              </div>
              <div className="form-field">
                <label title={t('Built-in templates ship with Armada and can be reset to their default text; custom templates were created by users.')}>{t('Template Type')}</label>
                <div>{promptTemplate.isBuiltIn ? <span className="tag built-in" title={t('Ships with Armada. It cannot be deleted, and Reset to Default restores its original text.')}>{t('Built-in')}</span> : <span className="text-dim" title={t('Created by a user. It can be edited or deleted.')}>{t('Custom')}</span>}</div>
              </div>
              <div className="form-field detail-field-full">
                <label title={t(TEMPLATE_DESCRIPTION_TIP)}>{t('Template Description')}</label>
                <input aria-label={t('Template Description')}
                  title={t(TEMPLATE_DESCRIPTION_TIP)}
                  value={promptDescription}
                  onChange={e => {
                    setPromptDescription(e.target.value);
                    setPromptDirty(true);
                  }}
                  placeholder={t('Optional prompt template description...')}
                />
              </div>
              <div className="form-field detail-field-full">
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: '0.75rem', marginBottom: '0.4rem' }}>
                  <label style={{ marginBottom: 0 }} title={t(PROMPT_CONTENT_TIP)}>{t('Prompt Content')}</label>
                  <span className="text-dim">{t('{{count}} characters', { count: promptContent.length.toLocaleString() })}</span>
                </div>
                <textarea aria-label={t('Prompt Content')}
                  title={t(PROMPT_CONTENT_TIP)}
                  rows={18}
                  value={promptContent}
                  onChange={e => {
                    setPromptContent(e.target.value);
                    setPromptDirty(true);
                  }}
                  spellCheck={false}
                  style={{
                    fontFamily: "'SF Mono', 'Fira Code', 'Cascadia Code', Consolas, monospace",
                    minHeight: '26rem',
                    lineHeight: 1.5,
                  }}
                />
                {promptDirty ? (
                  <div className="text-dim" style={{ marginTop: '0.35rem' }}>{t('Unsaved prompt changes')}</div>
                ) : null}
              </div>
            </div>

            <div className="detail-footer">
              <button className="btn btn-primary" disabled={promptSaving || !promptDirty} onClick={() => void handleSavePrompt()} title={t('Save the template description and content. Missions started after saving use the new text; running missions keep theirs.')}>
                {promptSaving ? t('Saving...') : t('Save Prompt')}
              </button>
              {promptTemplate.isBuiltIn ? (
                <button className="btn" disabled={promptSaving} onClick={handleResetPrompt} title={t('Replace this built-in template\'s text with the version that ships with Armada. Your edits are lost. You confirm first.')}>
                  {t('Reset to Default')}
                </button>
              ) : null}
              <button className="btn" onClick={() => navigate(`/prompt-templates/${encodeURIComponent(promptTemplate.name)}`)} title={t('Open this template on its own page, with its placeholders and preview.')}>
                {t('Open Full Template')}
              </button>
            </div>
          </>
        ) : (
          <div className="text-dim">
            {promptError || t('No prompt template is linked to this persona.')}
          </div>
        )}
      </div>
    </div>
  );
}
