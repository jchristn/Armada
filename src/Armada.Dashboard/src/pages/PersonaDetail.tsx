import { useState, useEffect, useCallback } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import { getPersona, updatePersona, deletePersona, listPromptTemplates } from '../api/client';
import type { Persona } from '../types/models';
import ActionMenu from '../components/shared/ActionMenu';
import JsonViewer from '../components/shared/JsonViewer';
import StatusBadge from '../components/shared/StatusBadge';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import CopyButton from '../components/shared/CopyButton';
import ErrorModal from '../components/shared/ErrorModal';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';

export default function PersonaDetail() {
  const { t, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();
  const { name } = useParams<{ name: string }>();
  const navigate = useNavigate();
  const [persona, setPersona] = useState<Persona | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Edit modal
  const [showForm, setShowForm] = useState(false);
  const [form, setForm] = useState({ description: '', promptTemplateName: '' });
  const [templateNames, setTemplateNames] = useState<string[]>([]);

  // JSON viewer
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });

  // Confirm
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

  const load = useCallback(async () => {
    if (!name) return;
    try {
      setLoading(true);
      const isInitialLoad = !persona;
      const found = await getPersona(name);
      if (!found) { setError(t('Persona not found.')); setLoading(false); return; }
      setPersona(found);
      const templateResult = await listPromptTemplates({ pageSize: 9999 });
      setTemplateNames(templateResult.objects.map(t => t.name));
      if (isInitialLoad) setError('');
    } catch {
      setError(t('Failed to load persona.'));
    } finally {
      setLoading(false);
    }
  }, [name, t]);

  useEffect(() => { load(); }, [load]);

  function openEdit() {
    if (!persona) return;
    setForm({ description: persona.description ?? '', promptTemplateName: persona.promptTemplateName ?? '' });
    setShowForm(true);
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!persona) return;
    try {
      await updatePersona(persona.name, { description: form.description, promptTemplateName: form.promptTemplateName });
      setShowForm(false);
      pushToast('success', t('Persona "{{name}}" saved.', { name: persona.name }));
      load();
    } catch { setError(t('Save failed.')); }
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

  if (loading) return <p className="text-dim">{t('Loading...')}</p>;
  if (error && !persona) return <ErrorModal error={error} onClose={() => setError('')} />;
  if (!persona) return <p className="text-dim">{t('Persona not found.')}</p>;

  return (
    <div>
      {/* Breadcrumb */}
      <div className="breadcrumb">
        <Link to="/personas">{t('Personas')}</Link> <span className="breadcrumb-sep">&gt;</span> <span>{persona.name}</span>
      </div>

      <div className="detail-header">
        <h2>{persona.name}</h2>
        <div className="inline-actions">
          <ActionMenu id={`persona-${persona.name}`} items={[
            { label: 'View JSON', onClick: () => setJsonData({ open: true, title: t('Persona: {{name}}', { name: persona.name }), data: persona }) },
            { label: 'Edit', onClick: openEdit },
            { label: 'Delete', danger: true, onClick: handleDelete },
          ]} />
        </div>
      </div>

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
        <div className="detail-field"><span className="detail-label">{t('Built-in')}</span>{persona.isBuiltIn ? <StatusBadge status="Built-in" /> : <span className="text-dim">{t('No')}</span>}</div>
        <div className="detail-field"><span className="detail-label">{t('Active')}</span><StatusBadge status={persona.active ? 'Active' : 'Inactive'} /></div>
        <div className="detail-field"><span className="detail-label">{t('Created')}</span><span title={persona.createdUtc}>{formatDateTime(persona.createdUtc)}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Last Updated')}</span><span>{formatDateTime(persona.lastUpdateUtc)}</span></div>
      </div>
    </div>
  );
}
