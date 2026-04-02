import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { listVessels, listPipelines, createVoyage } from '../api/client';
import type { Vessel, Pipeline } from '../types/models';

export default function Dispatch() {
  const navigate = useNavigate();

  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [pipelines, setPipelines] = useState<Pipeline[]>([]);
  const [selectedPipeline, setSelectedPipeline] = useState('');

  const [vesselId, setVesselId] = useState('');
  const [prompt, setPrompt] = useState('');
  const [priority, setPriority] = useState(100);
  const [dispatching, setDispatching] = useState(false);
  const [result, setResult] = useState<{ ok: boolean; message: string } | null>(null);

  useEffect(() => {
    Promise.all([
      listVessels({ pageSize: 9999 }).catch(() => null),
      listPipelines({ pageSize: 9999 }).catch(() => null),
    ]).then(([vRes, pRes]) => {
      if (vRes) setVessels(vRes.objects);
      if (pRes) setPipelines(pRes.objects);
    });
  }, []);

  const handleDispatch = async () => {
    if (!prompt.trim()) return;
    if (!vesselId) {
      setResult({ ok: false, message: 'Please select a vessel.' });
      return;
    }

    const selectedPipelineObj = pipelines.find((p) => p.name === selectedPipeline);
    const isMultiStage = selectedPipelineObj != null && selectedPipelineObj.stages.length > 1;

    const tasks = [prompt.trim()];
    if (!tasks.length) return;

    setDispatching(true);
    setResult(null);
    try {
      const voyageTitle = tasks.length > 1 ? 'Multi-task voyage' : tasks[0].substring(0, 80);
      const missions = tasks.map((t) => ({
        vesselId,
        title: t.substring(0, 80),
        description: t,
        priority,
      }));
      const voyage = await createVoyage({
        title: voyageTitle,
        vesselId,
        missions,
        ...(selectedPipeline ? { pipeline: selectedPipeline } : {}),
      });
      const missionCount = isMultiStage
        ? `${selectedPipelineObj!.stages.length} pipeline stages`
        : `${missions.length} mission${missions.length !== 1 ? 's' : ''}`;
      setResult({ ok: true, message: `Dispatched voyage with ${missionCount}` });
      setPrompt('');
      setTimeout(() => {
        navigate(`/voyages/${voyage.id}`);
      }, 1500);
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : 'Unknown error';
      setResult({ ok: false, message: `Failed: ${msg}` });
    } finally {
      setDispatching(false);
    }
  };

  return (
    <div>
      <div className="page-header">
        <div>
          <h2>Dispatch</h2>
          <p className="text-muted">
            Describe the work you want Armada to dispatch through the selected vessel and pipeline.
          </p>
        </div>
      </div>

      <div className="card" style={{ marginBottom: '1rem' }}>
        <div className="dispatch-form">
          {/* Row 1: Vessel + Pipeline + Priority */}
          <div style={{ display: 'grid', gridTemplateColumns: '2fr 2fr 1fr', gap: '0 1.5rem' }}>
            <div className="form-group">
              <label>Vessel</label>
              <select
                value={vesselId}
                onChange={(e) => setVesselId(e.target.value)}
                required
              >
                <option value="">Select a vessel...</option>
                {vessels.map((v) => (
                  <option key={v.id} value={v.id}>
                    {v.name}
                  </option>
                ))}
              </select>
            </div>
            <div className="form-group">
              <label>Pipeline</label>
              <select
                value={selectedPipeline}
                onChange={(e) => setSelectedPipeline(e.target.value)}
              >
                <option value="">Inherit (vessel, then fleet, then WorkerOnly)</option>
                {pipelines.map((p) => (
                  <option key={p.id} value={p.name}>
                    {p.name} ({p.stages.map((s) => s.personaName).join(' -> ')})
                  </option>
                ))}
              </select>
            </div>
            <div className="form-group">
              <label>Priority</label>
              <input
                type="number"
                value={priority}
                onChange={(e) => setPriority(parseInt(e.target.value) || 100)}
                min={0}
                max={1000}
                title="Higher priority missions are assigned first (default 100)"
              />
            </div>
          </div>

          {/* Prompt */}
          <div className="form-group">
            <label>Description</label>
            <textarea
              value={prompt}
              onChange={(e) => setPrompt(e.target.value)}
              rows={12}
              placeholder={'Describe what you need done.\n\nArmada will dispatch this request as a voyage on the selected vessel.'}
            />
          </div>

          <div className="form-actions">
            <button
              type="button"
              className="btn-primary"
              disabled={dispatching || !vesselId || !prompt.trim()}
              onClick={handleDispatch}
            >
              {dispatching ? 'Dispatching...' : 'Dispatch'}
            </button>
          </div>

          {result && (
            <div className={`alert ${result.ok ? 'alert-success' : 'alert-error'}`}>
              {result.message}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
