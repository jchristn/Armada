import { useState, useEffect, useCallback } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import { createPersona, deletePersona, getPersona, getPromptTemplate, listCaptains, listPromptTemplates, resetPromptTemplate, updatePersona, updatePromptTemplate } from '../api/client';
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

export default function PersonaDetail() {
  const { t, formatDateTime } = useLocale();
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
      const templateResult = await listPromptTemplates({ pageSize: 9999 });
      setTemplateNames(templateResult.objects.map(t => t.name));
      const captainResult = await listCaptains({ pageSize: 9999 });
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
    setShowForm(true);
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!persona) return;
    try {
      await updatePersona(persona.name, { description: form.description, promptTemplateName: form.promptTemplateName, defaultCaptainId: form.defaultCaptainId });
      setShowForm(false);
      pushToast('success', t('Persona "{{name}}" saved.', { name: persona.name }));
      load();
    } catch { setError(t('Save failed.')); }
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
        try {
          await deletePersona(persona.name);
          pushToast('warning', t('Persona "{{name}}" deleted.', { name: persona.name }));
          navigate('/personas');
        } catch { setError(t('Delete failed.')); }
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
        breadcrumb={
          <>
            <Link to="/personas">{t('Personas')}</Link> <span className="breadcrumb-sep">&gt;</span> <span>{persona.name}</span>
          </>
        }
        title={persona.name}
        actions={
          <>
            <ActionMenu id={`persona-${persona.name}`} items={[
              { label: 'View JSON', onClick: () => setJsonData({ open: true, title: t('Persona: {{name}}', { name: persona.name }), data: persona }) },
              ...(canEditScoped(viewer, persona) ? [{ label: 'Edit', onClick: openEdit }] : []),
              { label: 'Duplicate', onClick: () => void handleDuplicate() },
              ...(persona.promptTemplateName ? [{ label: 'Open Backing Prompt', onClick: () => navigate(`/prompt-templates/${encodeURIComponent(persona.promptTemplateName)}`) }] : []),
              ...(canEditScoped(viewer, persona) ? [{ label: 'Delete', danger: true as const, onClick: handleDelete }] : []),
            ]} />
          </>
        }
      />

      <ErrorModal error={error} onClose={() => setError('')} />

      {/* Edit Modal */}
      {showForm && (
        <div className="modal-overlay" onClick={() => setShowForm(false)}>
          <form className="modal" onClick={e => e.stopPropagation()} onSubmit={handleSubmit}>
            <h3>{t('Edit Persona')}</h3>
            <label>{t('Description')}<textarea value={form.description} onChange={e => setForm({ ...form, description: e.target.value })} rows={4} /></label>
            <label>{t('Prompt Template Name')}
              <select value={form.promptTemplateName} onChange={e => setForm({ ...form, promptTemplateName: e.target.value })} required>
                <option value="">{t('Select a template...')}</option>
                {templateNames.map(templateName => (
                  <option key={templateName} value={templateName}>{templateName}</option>
                ))}
              </select>
            </label>
            <label>{t('Default Captain')}
              <CaptainPicker
                captains={captains}
                value={form.defaultCaptainId}
                onChange={(captainId) => setForm({ ...form, defaultCaptainId: captainId })}
                autoLabel={t('None (default routing)')}
              />
              <span className="text-dim" style={{ fontSize: '0.75rem', display: 'block', marginTop: '0.3rem' }}>
                {t('Pre-fills the per-step captain at dispatch and becomes the preferred captain for missions of this persona.')}
              </span>
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

      {/* Persona Info */}
      <div className="detail-grid">
        <div className="detail-field">
          <span className="detail-label">{t('ID')}</span>
          <span className="id-display">
            <span className="mono">{persona.id}</span>
            <CopyButton text={persona.id} />
          </span>
        </div>
        <div className="detail-field"><span className="detail-label">{t('Name')}</span><span>{persona.name}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Description')}</span><span>{persona.description || '-'}</span></div>
        <div className="detail-field">
          <span className="detail-label">{t('Prompt Template Name')}</span>
          <span>
            {persona.promptTemplateName ? (
              <Link to={`/prompt-templates/${encodeURIComponent(persona.promptTemplateName)}`}>{persona.promptTemplateName}</Link>
            ) : '-'}
          </span>
        </div>
        <div className="detail-field">
          <span className="detail-label">{t('Default Captain')}</span>
          <span><CaptainRef captainId={persona.defaultCaptainId} captains={captains} autoLabel={t('None (default routing)')} /></span>
        </div>
        <div className="detail-field"><span className="detail-label">{t('Built-in')}</span>{persona.isBuiltIn ? <StatusBadge status="Built-in" /> : <span className="text-dim">{t('No')}</span>}</div>
        <div className="detail-field"><span className="detail-label">{t('Active')}</span><StatusBadge status={persona.active ? 'Active' : 'Inactive'} /></div>
        <div className="detail-field"><span className="detail-label">{t('Created')}</span><span title={persona.createdUtc}>{formatDateTime(persona.createdUtc)}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Last Updated')}</span><span>{formatDateTime(persona.lastUpdateUtc)}</span></div>
      </div>

      <div className="card detail-section" style={{ marginTop: '1.5rem' }}>
        <div className="detail-section-header">
          <div>
            <h3>{t('Backing Prompt')}</h3>
            <div className="text-dim backlog-section-note">
              {t('This is the prompt template Armada resolves when this persona is assigned to a mission. Edit it here to change future mission instructions.')}
            </div>
          </div>
          <div className="inline-actions">
            {promptTemplate ? <StatusBadge status={promptTemplate.category} /> : null}
            {promptTemplate?.isBuiltIn ? <StatusBadge status="Built-in" /> : null}
          </div>
        </div>

        {promptLoading ? (
          <p className="text-dim">{t('Loading backing prompt...')}</p>
        ) : promptTemplate ? (
          <>
            <div className="detail-form-grid">
              <div className="form-field">
                <label>{t('Template Name')}</label>
                <div>
                  <Link to={`/prompt-templates/${encodeURIComponent(promptTemplate.name)}`}>{promptTemplate.name}</Link>
                </div>
              </div>
              <div className="form-field">
                <label>{t('Template Type')}</label>
                <div>{promptTemplate.isBuiltIn ? <StatusBadge status="Built-in" /> : <span className="text-dim">{t('Custom')}</span>}</div>
              </div>
              <div className="form-field detail-field-full">
                <label>{t('Template Description')}</label>
                <input
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
                  <label style={{ marginBottom: 0 }}>{t('Prompt Content')}</label>
                  <span className="text-dim">{t('{{count}} characters', { count: promptContent.length.toLocaleString() })}</span>
                </div>
                <textarea
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
              <button className="btn btn-primary" disabled={promptSaving || !promptDirty} onClick={() => void handleSavePrompt()}>
                {promptSaving ? t('Saving...') : t('Save Prompt')}
              </button>
              {promptTemplate.isBuiltIn ? (
                <button className="btn" disabled={promptSaving} onClick={handleResetPrompt}>
                  {t('Reset to Default')}
                </button>
              ) : null}
              <button className="btn" onClick={() => navigate(`/prompt-templates/${encodeURIComponent(promptTemplate.name)}`)}>
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
