import { useState, useEffect, useCallback } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import { getPersona, updatePersona, deletePersona, listPromptTemplates } from '../api/client';
import type { Persona } from '../types/models';
import ActionMenu from '../components/shared/ActionMenu';
import JsonViewer from '../components/shared/JsonViewer';
import StatusBadge from '../components/shared/StatusBadge';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import { copyToClipboard } from '../components/shared/CopyButton';
import ErrorModal from '../components/shared/ErrorModal';

function formatTimeAbsolute(utc: string | null): string {
  if (!utc) return '-';
  return new Date(utc).toLocaleString();
}

export default function PersonaDetail() {
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
      const found = await getPersona(name);
      if (!found) { setError('Persona not found.'); setLoading(false); return; }
      setPersona(found);
      const templateResult = await listPromptTemplates({ pageSize: 9999 });
      setTemplateNames(templateResult.objects.map(t => t.name));
      setError('');
    } catch {
      setError('Failed to load persona.');
    } finally {
      setLoading(false);
    }
  }, [name]);

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
      load();
    } catch { setError('Save failed.'); }
  }

  function handleDelete() {
    if (!persona) return;
    if (persona.isBuiltIn) {
      setError('Built-in personas cannot be deleted.');
      return;
    }
    setConfirm({
      open: true,
      title: 'Delete Persona',
      message: `Delete persona "${persona.name}"? This cannot be undone.`,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await deletePersona(persona.name);
          navigate('/personas');
        } catch { setError('Delete failed.'); }
      },
    });
  }

  if (loading) return <p className="text-dim">Loading...</p>;
  if (error && !persona) return <ErrorModal error={error} onClose={() => setError('')} />;
  if (!persona) return <p className="text-dim">Persona not found.</p>;

  return (
    <div>
      {/* Breadcrumb */}
      <div className="breadcrumbs">
        <Link to="/personas">Personas</Link> <span className="bc-sep">&gt;</span> <span>{persona.name}</span>
      </div>

      <div className="detail-header">
        <h2>{persona.name}</h2>
        <div className="inline-actions">
          <ActionMenu id={`persona-${persona.name}`} items={[
            { label: 'View JSON', onClick: () => setJsonData({ open: true, title: `Persona: ${persona.name}`, data: persona }) },
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
            <h3>Edit Persona</h3>
            <label>Description<textarea value={form.description} onChange={e => setForm({ ...form, description: e.target.value })} rows={4} /></label>
            <label>Prompt Template Name
              <select value={form.promptTemplateName} onChange={e => setForm({ ...form, promptTemplateName: e.target.value })} required>
                <option value="">Select a template...</option>
                {templateNames.map(name => (
                  <option key={name} value={name}>{name}</option>
                ))}
              </select>
            </label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary">Save</button>
              <button type="button" className="btn" onClick={() => setShowForm(false)}>Cancel</button>
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
          <span className="detail-label">ID</span>
          <span className="id-display">
            <span className="mono">{persona.id}</span>
            <button className="copy-btn" onClick={() => copyToClipboard(persona.id)} title="Copy ID" />
          </span>
        </div>
        <div className="detail-field"><span className="detail-label">Name</span><span>{persona.name}</span></div>
        <div className="detail-field"><span className="detail-label">Description</span><span>{persona.description || '-'}</span></div>
        <div className="detail-field">
          <span className="detail-label">Prompt Template Name</span>
          <span>
            {persona.promptTemplateName ? (
              <Link to={`/prompt-templates/${encodeURIComponent(persona.promptTemplateName)}`}>{persona.promptTemplateName}</Link>
            ) : '-'}
          </span>
        </div>
        <div className="detail-field"><span className="detail-label">Built-in</span>{persona.isBuiltIn ? <StatusBadge status="Built-in" /> : <span className="text-dim">No</span>}</div>
        <div className="detail-field"><span className="detail-label">Active</span><StatusBadge status={persona.active ? 'Active' : 'Inactive'} /></div>
        <div className="detail-field"><span className="detail-label">Created</span><span title={persona.createdUtc}>{formatTimeAbsolute(persona.createdUtc)}</span></div>
        <div className="detail-field"><span className="detail-label">Last Updated</span><span>{formatTimeAbsolute(persona.lastUpdateUtc)}</span></div>
      </div>
    </div>
  );
}
