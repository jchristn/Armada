import { useEffect, useState } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { listVessels, listPipelines, createVoyage } from '../api/client';
import type { Vessel, Pipeline } from '../types/models';
import ErrorModal from '../components/shared/ErrorModal';

interface MissionRow {
  title: string;
  description: string;
  priority: number;
}

export default function VoyageCreate() {
  const navigate = useNavigate();
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [pipelines, setPipelines] = useState<Pipeline[]>([]);
  const [selectedPipeline, setSelectedPipeline] = useState('');

  // Form state
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [vesselId, setVesselId] = useState('');
  const [autoPush, setAutoPush] = useState(false);
  const [autoCreatePRs, setAutoCreatePRs] = useState(false);
  const [autoMergePRs, setAutoMergePRs] = useState(false);

  // Mission rows
  const [missions, setMissions] = useState<MissionRow[]>([
    { title: '', description: '', priority: 100 },
  ]);

  const [error, setError] = useState('');
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    listVessels({ pageSize: 1000 }).then(r => setVessels(r.objects || [])).catch(() => {});
    listPipelines({ pageSize: 1000 }).then(r => setPipelines(r.objects || [])).catch(() => {});
  }, []);

  function addMission() {
    setMissions(m => [...m, { title: '', description: '', priority: 100 }]);
  }

  function removeMission(index: number) {
    setMissions(m => m.filter((_, i) => i !== index));
  }

  function updateMission(index: number, field: keyof MissionRow, value: string | number) {
    setMissions(m => m.map((row, i) => i === index ? { ...row, [field]: value } : row));
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError('');

    if (!title.trim()) { setError('Voyage title is required.'); return; }
    if (!vesselId) { setError('Please select a vessel.'); return; }

    const validMissions = missions.filter(m => m.title.trim());
    if (validMissions.length === 0) { setError('At least one mission with a title is required.'); return; }

    setSubmitting(true);
    try {
      const missionPayloads = validMissions.map(m => ({
        title: m.title.trim(),
        description: m.description.trim() || m.title.trim(),
        vesselId,
        priority: m.priority || 100,
      }));

      // The server API accepts vesselId at the top level for convenience
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const voyage = await createVoyage({
        title: title.trim(),
        description: description.trim() || undefined,
        vesselId,
        ...(selectedPipeline ? { pipeline: selectedPipeline } : {}),
        missions: missionPayloads,
      });

      navigate(`/voyages/${voyage.id}`);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to create voyage.');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div>
      {/* Breadcrumb */}
      <div style={{ display: 'flex', gap: 8, alignItems: 'center', marginBottom: 16, fontSize: 13 }}>
        <Link to="/voyages">Voyages</Link>
        <span style={{ color: 'var(--text-muted)' }}>/</span>
        <span>Create Voyage</span>
      </div>

      <h2 style={{ marginBottom: 20 }}>Create Voyage</h2>

      <ErrorModal error={error} onClose={() => setError('')} />

      <form onSubmit={handleSubmit}>
        {/* Voyage metadata */}
        <div className="card" style={{ marginBottom: 16, padding: 20 }}>
          <h3 style={{ fontSize: 14, fontWeight: 600, color: 'var(--text-muted)', textTransform: 'uppercase', letterSpacing: 0.5, marginBottom: 16 }}>Voyage Details</h3>

          <label style={{ display: 'block', marginBottom: 14 }}>
            <span style={{ fontSize: 13, fontWeight: 600, color: 'var(--text-muted)' }}>Title</span>
            <input value={title} onChange={e => setTitle(e.target.value)} required
              placeholder="Name for this batch of missions" style={{ marginTop: 4 }} />
          </label>

          <label style={{ display: 'block', marginBottom: 14 }}>
            <span style={{ fontSize: 13, fontWeight: 600, color: 'var(--text-muted)' }}>Description</span>
            <textarea value={description} onChange={e => setDescription(e.target.value)} rows={3}
              placeholder="Optional description for the voyage..." style={{ marginTop: 4 }} />
          </label>

          <label style={{ display: 'block', marginBottom: 14 }}>
            <span style={{ fontSize: 13, fontWeight: 600, color: 'var(--text-muted)' }}>Vessel</span>
            <select value={vesselId} onChange={e => setVesselId(e.target.value)} required style={{ marginTop: 4 }}>
              <option value="">Select a vessel...</option>
              {vessels.map(v => <option key={v.id} value={v.id}>{v.name} ({v.id})</option>)}
            </select>
          </label>

          <label style={{ display: 'block', marginBottom: 14 }}>
            <span style={{ fontSize: 13, fontWeight: 600, color: 'var(--text-muted)' }}>Pipeline</span>
            <select value={selectedPipeline} onChange={e => setSelectedPipeline(e.target.value)} style={{ marginTop: 4 }}>
              <option value="">Inherit (vessel, then fleet, then WorkerOnly)</option>
              {pipelines.map(p => (
                <option key={p.id} value={p.name}>
                  {p.name} ({p.stages.map(s => s.personaName).join(' -> ')})
                </option>
              ))}
            </select>
          </label>

          {/* Checkboxes */}
          <div style={{ display: 'flex', gap: 20, flexWrap: 'wrap' }}>
            <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 13, cursor: 'pointer' }}>
              <input type="checkbox" checked={autoPush} onChange={e => setAutoPush(e.target.checked)}
                style={{ width: 'auto', margin: 0 }} />
              Auto-Push
            </label>
            <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 13, cursor: 'pointer' }}>
              <input type="checkbox" checked={autoCreatePRs} onChange={e => setAutoCreatePRs(e.target.checked)}
                style={{ width: 'auto', margin: 0 }} />
              Auto-Create PRs
            </label>
            <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 13, cursor: 'pointer' }}>
              <input type="checkbox" checked={autoMergePRs} onChange={e => setAutoMergePRs(e.target.checked)}
                style={{ width: 'auto', margin: 0 }} />
              Auto-Merge PRs
            </label>
          </div>
        </div>

        {/* Mission rows */}
        <div style={{ marginBottom: 16 }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 }}>
            <h3 style={{ fontSize: 14, fontWeight: 600, color: 'var(--text-muted)', textTransform: 'uppercase', letterSpacing: 0.5 }}>
              Missions ({missions.length})
            </h3>
            <button type="button" className="btn-sm" onClick={addMission}>+ Add Mission</button>
          </div>

          {missions.map((m, i) => (
            <div key={i} className="card" style={{ marginBottom: 10, padding: 16 }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 10 }}>
                <strong style={{ fontSize: 13 }}>Mission {i + 1}</strong>
                {missions.length > 1 && (
                  <button type="button" className="btn-sm btn-danger" onClick={() => removeMission(i)} style={{ fontSize: 11 }}>Remove</button>
                )}
              </div>

              <div style={{ display: 'grid', gridTemplateColumns: '1fr auto', gap: 12, marginBottom: 8 }}>
                <label style={{ display: 'block' }}>
                  <span style={{ fontSize: 12, fontWeight: 600, color: 'var(--text-muted)' }}>Title</span>
                  <input value={m.title} onChange={e => updateMission(i, 'title', e.target.value)}
                    required placeholder="What needs to be done?" style={{ marginTop: 2, fontSize: 13 }} />
                </label>
                <label style={{ display: 'block', width: 80 }}>
                  <span style={{ fontSize: 12, fontWeight: 600, color: 'var(--text-muted)' }}>Priority</span>
                  <input type="number" value={m.priority} onChange={e => updateMission(i, 'priority', Number(e.target.value))}
                    min={0} max={1000} style={{ marginTop: 2, fontSize: 13 }} />
                </label>
              </div>

              <label style={{ display: 'block' }}>
                <span style={{ fontSize: 12, fontWeight: 600, color: 'var(--text-muted)' }}>Description</span>
                <textarea value={m.description} onChange={e => updateMission(i, 'description', e.target.value)}
                  rows={2} placeholder="Detailed instructions for the AI captain..." style={{ marginTop: 2, fontSize: 13 }} />
              </label>
            </div>
          ))}

          {missions.length === 0 && (
            <p className="text-muted" style={{ padding: 20, textAlign: 'center' }}>
              No missions added. Click "+ Add Mission" to add one.
            </p>
          )}
        </div>

        {/* Submit */}
        <div style={{ display: 'flex', gap: 10, justifyContent: 'flex-end' }}>
          <button type="button" onClick={() => navigate('/voyages')} style={{ background: '#f0f0f5', color: 'var(--text)', padding: '8px 16px', borderRadius: 'var(--radius)', border: 'none', cursor: 'pointer' }}>
            Cancel
          </button>
          <button type="submit" className="btn-primary" disabled={submitting}>
            {submitting ? 'Creating...' : 'Create Voyage'}
          </button>
        </div>
      </form>
    </div>
  );
}
