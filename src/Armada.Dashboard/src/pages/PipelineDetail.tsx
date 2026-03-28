import { useState, useEffect, useCallback } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import { getPipeline, updatePipeline, deletePipeline, listPersonas } from '../api/client';
import type { Pipeline } from '../types/models';
import ActionMenu from '../components/shared/ActionMenu';
import JsonViewer from '../components/shared/JsonViewer';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import { copyToClipboard } from '../components/shared/CopyButton';
import ErrorModal from '../components/shared/ErrorModal';

function formatTimeAbsolute(utc: string | null): string {
  if (!utc) return '-';
  return new Date(utc).toLocaleString();
}

interface StageForm {
  personaName: string;
  isOptional: boolean;
  description: string;
}

export default function PipelineDetail() {
  const { name } = useParams<{ name: string }>();
  const navigate = useNavigate();
  const [pipeline, setPipeline] = useState<Pipeline | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Edit modal
  const [showForm, setShowForm] = useState(false);
  const [form, setForm] = useState<{ description: string; stages: StageForm[] }>({ description: '', stages: [] });
  const [personaNames, setPersonaNames] = useState<string[]>([]);

  // JSON viewer
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });

  // Confirm
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

  const load = useCallback(async () => {
    if (!name) return;
    try {
      setLoading(true);
      const found = await getPipeline(name);
      setPipeline(found);
      const personaResult = await listPersonas({ pageSize: 9999 });
      setPersonaNames(personaResult.objects.map(p => p.name));
      setError('');
    } catch {
      setError('Failed to load pipeline.');
    } finally {
      setLoading(false);
    }
  }, [name]);

  useEffect(() => { load(); }, [load]);

  function openEdit() {
    if (!pipeline) return;
    setForm({
      description: pipeline.description ?? '',
      stages: pipeline.stages.map(s => ({
        personaName: s.personaName,
        isOptional: s.isOptional,
        description: s.description ?? '',
      })),
    });
    setShowForm(true);
  }

  function addStage() {
    setForm(f => ({ ...f, stages: [...f.stages, { personaName: '', isOptional: false, description: '' }] }));
  }

  function moveStage(index: number, direction: number) {
    setForm(f => {
      const stages = [...f.stages];
      const target = index + direction;
      if (target < 0 || target >= stages.length) return f;
      const temp = stages[index];
      stages[index] = stages[target];
      stages[target] = temp;
      return { ...f, stages };
    });
  }

  function removeStage(index: number) {
    setForm(f => ({ ...f, stages: f.stages.filter((_, i) => i !== index) }));
  }

  function updateStage(index: number, field: keyof StageForm, value: string | boolean) {
    setForm(f => ({
      ...f,
      stages: f.stages.map((s, i) => i === index ? { ...s, [field]: value } : s),
    }));
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!pipeline) return;
    try {
      const stagesPayload = form.stages
        .filter(s => s.personaName.trim() !== '')
        .map((s, i) => ({ personaName: s.personaName.trim(), isOptional: s.isOptional, description: s.description || null, order: i + 1 }));
      const payload = { description: form.description || null, stages: stagesPayload } as Partial<Pipeline>;
      await updatePipeline(pipeline.name, payload);
      setShowForm(false);
      load();
    } catch { setError('Save failed.'); }
  }

  function handleDelete() {
    if (!pipeline) return;
    if (pipeline.isBuiltIn) {
      setError('Built-in pipelines cannot be deleted.');
      return;
    }
    setConfirm({
      open: true,
      title: 'Delete Pipeline',
      message: `Delete pipeline "${pipeline.name}"? This cannot be undone.`,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await deletePipeline(pipeline.name);
          navigate('/pipelines');
        } catch { setError('Delete failed.'); }
      },
    });
  }

  if (loading) return <p className="text-dim">Loading...</p>;
  if (error && !pipeline) return <ErrorModal error={error} onClose={() => setError('')} />;
  if (!pipeline) return <p className="text-dim">Pipeline not found.</p>;

  return (
    <div>
      {/* Breadcrumb */}
      <div className="breadcrumbs">
        <Link to="/pipelines">Pipelines</Link> <span className="bc-sep">&gt;</span> <span>{pipeline.name}</span>
      </div>

      <div className="detail-header">
        <h2>{pipeline.name}</h2>
        <div className="inline-actions">
          <ActionMenu id={`pipeline-${pipeline.name}`} items={[
            { label: 'View JSON', onClick: () => setJsonData({ open: true, title: `Pipeline: ${pipeline.name}`, data: pipeline }) },
            { label: 'Edit', onClick: openEdit },
            { label: 'Delete', danger: true, onClick: handleDelete, disabled: pipeline.isBuiltIn },
          ]} />
        </div>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />

      {/* Edit Modal */}
      {showForm && (
        <div className="modal-overlay" onClick={() => setShowForm(false)}>
          <form className="modal" onClick={e => e.stopPropagation()} onSubmit={handleSubmit} style={{ maxWidth: '720px', width: '90%' }}>
            <h3>Edit Pipeline</h3>
            <label>Description<textarea value={form.description} onChange={e => setForm({ ...form, description: e.target.value })} rows={3} /></label>
            <div style={{ marginTop: '1rem' }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '0.5rem' }}>
                <strong>Stages</strong>
                <button type="button" className="btn" onClick={addStage}>+ Add Stage</button>
              </div>
              {form.stages.map((stage, i) => (
                <div key={i} style={{ display: 'flex', gap: '0.5rem', alignItems: 'center', marginBottom: '0.5rem' }}>
                  <span className="text-dim" style={{ minWidth: '1.5rem' }}>{i + 1}.</span>
                  <select
                    value={stage.personaName}
                    onChange={e => updateStage(i, 'personaName', e.target.value)}
                    required
                    style={{ flex: '0 1 200px', minWidth: '120px' }}
                  >
                    <option value="">Select persona...</option>
                    {personaNames.map(name => (
                      <option key={name} value={name}>{name}</option>
                    ))}
                  </select>
                  <label style={{ display: 'inline-flex', alignItems: 'center', gap: '0.35rem', margin: 0, whiteSpace: 'nowrap', lineHeight: 1, cursor: 'pointer' }}>
                    <input type="checkbox" checked={stage.isOptional} onChange={e => updateStage(i, 'isOptional', e.target.checked)} style={{ width: 'auto', margin: 0, verticalAlign: 'middle' }} />
                    <span style={{ verticalAlign: 'middle' }}>Optional</span>
                  </label>
                  <span style={{ width: '0.75rem', flexShrink: 0 }} />
                  <button type="button" className="btn btn-sm" onClick={() => moveStage(i, -1)} disabled={i === 0} title="Move up" style={{ padding: '0.15rem 0.4rem', fontSize: '0.75rem' }}>{'\u25B2'}</button>
                  <button type="button" className="btn btn-sm" onClick={() => moveStage(i, 1)} disabled={i === form.stages.length - 1} title="Move down" style={{ padding: '0.15rem 0.4rem', fontSize: '0.75rem' }}>{'\u25BC'}</button>
                  <span style={{ width: '0.5rem', flexShrink: 0 }} />
                  <button type="button" className="btn btn-sm btn-danger" onClick={() => removeStage(i)} title="Remove stage" style={{ flexShrink: 0 }}>X</button>
                </div>
              ))}
              {form.stages.length === 0 && <p className="text-dim">No stages defined.</p>}
            </div>
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

      {/* Pipeline Info */}
      <div className="detail-grid">
        <div className="detail-field">
          <span className="detail-label">ID</span>
          <span className="id-display">
            <span className="mono">{pipeline.id}</span>
            <button className="copy-btn" onClick={() => copyToClipboard(pipeline.id)} title="Copy ID" />
          </span>
        </div>
        <div className="detail-field"><span className="detail-label">Name</span><span>{pipeline.name}</span></div>
        <div className="detail-field"><span className="detail-label">Description</span><span>{pipeline.description || '-'}</span></div>
        <div className="detail-field"><span className="detail-label">Built-in</span><span>{pipeline.isBuiltIn ? <span className="badge badge-info">Yes</span> : <span className="badge">No</span>}</span></div>
        <div className="detail-field"><span className="detail-label">Active</span><span>{pipeline.active !== false ? <span className="badge badge-success">Yes</span> : <span className="badge badge-dim">No</span>}</span></div>
        <div className="detail-field"><span className="detail-label">Created</span><span title={pipeline.createdUtc}>{formatTimeAbsolute(pipeline.createdUtc)}</span></div>
        <div className="detail-field"><span className="detail-label">Last Updated</span><span>{formatTimeAbsolute(pipeline.lastUpdateUtc)}</span></div>
      </div>

      {/* Stages */}
      <div>
        <h3>Stages</h3>
        {pipeline.stages.length > 0 ? (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th title="Execution order of the stage">Order</th>
                  <th title="Name of the persona assigned to this stage">Persona Name</th>
                  <th title="Whether this stage can be skipped">Optional</th>
                  <th title="Description of this stage">Description</th>
                </tr>
              </thead>
              <tbody>
                {pipeline.stages
                  .slice()
                  .sort((a, b) => a.order - b.order)
                  .map(stage => (
                    <tr key={stage.id}>
                      <td>{stage.order}</td>
                      <td><strong>{stage.personaName}</strong></td>
                      <td>{stage.isOptional ? <span className="badge badge-info">Yes</span> : <span className="badge">No</span>}</td>
                      <td className="text-dim">{stage.description || '-'}</td>
                    </tr>
                  ))}
              </tbody>
            </table>
          </div>
        ) : (
          <p className="text-dim" style={{ marginTop: '1rem' }}>No stages defined.</p>
        )}
      </div>
    </div>
  );
}
