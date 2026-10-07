import { useEffect, useState } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { listVessels, listPipelines, createVoyage } from '../api/client';
import type { Vessel, Pipeline, SelectedPlaybook } from '../types/models';
import ErrorModal from '../components/shared/ErrorModal';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import PlaybookSelector from '../components/shared/PlaybookSelector';
import { sortByName } from '../lib/sortByName';
import { findLandingMode, getVoyageLandingModes } from '../lib/vesselForm';
import { buildVoyageCreateRequest, newVoyageMissionRow, validateVoyageForm, type VoyageMissionRow } from '../lib/voyageForm';

export default function VoyageCreate() {
  const { t } = useLocale();
  const { pushToast } = useNotifications();
  const landingModes = getVoyageLandingModes(t);
  const navigate = useNavigate();
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [pipelines, setPipelines] = useState<Pipeline[]>([]);
  const [selectedPipeline, setSelectedPipeline] = useState('');

  // Form state
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [vesselId, setVesselId] = useState('');
  const [landingMode, setLandingMode] = useState('');
  const [selectedPlaybooks, setSelectedPlaybooks] = useState<SelectedPlaybook[]>([]);

  // Mission rows
  const [missions, setMissions] = useState<VoyageMissionRow[]>([newVoyageMissionRow()]);

  const [error, setError] = useState('');
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    listVessels({ pageSize: 1000 }).then(r => setVessels(sortByName(r.objects))).catch(() => {});
    listPipelines({ pageSize: 1000 }).then(r => setPipelines(r.objects || [])).catch(() => {});
  }, []);

  function addMission() {
    setMissions(m => [...m, newVoyageMissionRow()]);
  }

  function removeMission(index: number) {
    setMissions(m => m.filter((_, i) => i !== index));
  }

  function updateMission(index: number, field: keyof VoyageMissionRow, value: string | number) {
    setMissions(m => m.map((row, i) => i === index ? { ...row, [field]: value } : row));
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError('');

    const form = { title, description, vesselId, pipeline: selectedPipeline, landingMode, selectedPlaybooks, missions };
    const problem = validateVoyageForm(form);
    if (problem) { setError(t(problem)); return; }

    setSubmitting(true);
    try {
      const voyage = await createVoyage(buildVoyageCreateRequest(form));

      pushToast('success', t('Voyage "{{title}}" created.', { title: title.trim() }));
      navigate(`/voyages/${voyage.id}`);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to create voyage.'));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div>
      {/* Breadcrumb */}
      <div style={{ display: 'flex', gap: 8, alignItems: 'center', marginBottom: 16, fontSize: 13 }}>
        <Link to="/voyages">{t('Voyages')}</Link>
        <span style={{ color: 'var(--text-muted)' }}>/</span>
        <span>{t('Create Voyage')}</span>
      </div>

      <h2 style={{ marginBottom: 20 }}>{t('Create Voyage')}</h2>

      <ErrorModal error={error} onClose={() => setError('')} />

      <form onSubmit={handleSubmit}>
        {/* Voyage metadata */}
        <div className="card" style={{ marginBottom: 16, padding: 20 }}>
          <h3 style={{ fontSize: 14, fontWeight: 600, color: 'var(--text-muted)', textTransform: 'uppercase', letterSpacing: 0.5, marginBottom: 16 }}>{t('Voyage Details')}</h3>

          <label style={{ display: 'block', marginBottom: 14 }}>
            <span style={{ fontSize: 13, fontWeight: 600, color: 'var(--text-muted)' }}>{t('Title')}</span>
            <input value={title} onChange={e => setTitle(e.target.value)} required
              placeholder={t('Name for this batch of missions')} style={{ marginTop: 4 }} />
          </label>

          <label style={{ display: 'block', marginBottom: 14 }}>
            <span style={{ fontSize: 13, fontWeight: 600, color: 'var(--text-muted)' }}>{t('Description')}</span>
            <textarea value={description} onChange={e => setDescription(e.target.value)} rows={3}
              placeholder={t('Optional description for the voyage...')} style={{ marginTop: 4 }} />
          </label>

          <label style={{ display: 'block', marginBottom: 14 }}>
            <span style={{ fontSize: 13, fontWeight: 600, color: 'var(--text-muted)' }}>{t('Vessel')}</span>
            <select value={vesselId} onChange={e => setVesselId(e.target.value)} required style={{ marginTop: 4 }}>
              <option value="">{t('Select a vessel...')}</option>
              {vessels.map(v => <option key={v.id} value={v.id}>{v.name} ({v.id})</option>)}
            </select>
          </label>

          <label style={{ display: 'block', marginBottom: 14 }}>
            <span style={{ fontSize: 13, fontWeight: 600, color: 'var(--text-muted)' }}>{t('Pipeline')}</span>
            <select value={selectedPipeline} onChange={e => setSelectedPipeline(e.target.value)} style={{ marginTop: 4 }}>
              <option value="">{t('Inherit (vessel, then fleet, then WorkerOnly)')}</option>
              {pipelines.map(p => (
                <option key={p.id} value={p.name}>
                  {p.name} ({p.stages.map(s => s.personaName).join(' -> ')})
                </option>
              ))}
            </select>
          </label>

          <label style={{ display: 'block', marginBottom: 14 }}>
            <span style={{ fontSize: 13, fontWeight: 600, color: 'var(--text-muted)' }}>{t('Landing Mode')}</span>
            <select value={landingMode} onChange={e => setLandingMode(e.target.value)} style={{ marginTop: 4 }}>
              {landingModes.map(m => <option key={m.value || 'default'} value={m.value}>{m.label}</option>)}
            </select>
            <small className="text-dim">{findLandingMode(landingModes, landingMode).description}</small>
          </label>
        </div>

        <PlaybookSelector value={selectedPlaybooks} onChange={setSelectedPlaybooks} disabled={submitting} />

        {/* Mission rows */}
        <div style={{ marginBottom: 16 }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 }}>
            <h3 style={{ fontSize: 14, fontWeight: 600, color: 'var(--text-muted)', textTransform: 'uppercase', letterSpacing: 0.5 }}>
              {t('Missions')} ({missions.length})
            </h3>
            <button type="button" className="btn-sm" onClick={addMission}>+ {t('Add Mission')}</button>
          </div>

          {missions.map((m, i) => (
            <div key={i} className="card" style={{ marginBottom: 10, padding: 16 }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 10 }}>
                <strong style={{ fontSize: 13 }}>{t('Mission {{index}}', { index: i + 1 })}</strong>
                {missions.length > 1 && (
                  <button type="button" className="btn-sm btn-danger" onClick={() => removeMission(i)} style={{ fontSize: 11 }}>{t('Remove')}</button>
                )}
              </div>

              <div style={{ display: 'grid', gridTemplateColumns: '1fr auto', gap: 12, marginBottom: 8 }}>
                <label style={{ display: 'block' }}>
                  <span style={{ fontSize: 12, fontWeight: 600, color: 'var(--text-muted)' }}>{t('Title')}</span>
                  <input value={m.title} onChange={e => updateMission(i, 'title', e.target.value)}
                    required placeholder={t('What needs to be done?')} style={{ marginTop: 2, fontSize: 13 }} />
                </label>
                <label style={{ display: 'block', width: 80 }}>
                  <span style={{ fontSize: 12, fontWeight: 600, color: 'var(--text-muted)' }}>{t('Priority')}</span>
                  <input type="number" value={m.priority} onChange={e => updateMission(i, 'priority', Number(e.target.value))}
                    min={0} max={1000} style={{ marginTop: 2, fontSize: 13 }} />
                </label>
              </div>

              <label style={{ display: 'block' }}>
                <span style={{ fontSize: 12, fontWeight: 600, color: 'var(--text-muted)' }}>{t('Description')}</span>
                <textarea value={m.description} onChange={e => updateMission(i, 'description', e.target.value)}
                  rows={2} placeholder={t('Detailed instructions for the AI captain...')} style={{ marginTop: 2, fontSize: 13 }} />
              </label>
            </div>
          ))}

          {missions.length === 0 && (
            <p className="text-muted" style={{ padding: 20, textAlign: 'center' }}>
              {t('No missions added. Click "+ Add Mission" to add one.')}
            </p>
          )}
        </div>

        {/* Submit */}
        <div style={{ display: 'flex', gap: 10, justifyContent: 'flex-end' }}>
          <button type="button" onClick={() => navigate('/voyages')} style={{ background: '#f0f0f5', color: 'var(--text)', padding: '8px 16px', borderRadius: 'var(--radius)', border: 'none', cursor: 'pointer' }}>
            {t('Cancel')}
          </button>
          <button type="submit" className="btn-primary" disabled={submitting}>
            {submitting ? t('Creating...') : t('Create Voyage')}
          </button>
        </div>
      </form>
    </div>
  );
}
