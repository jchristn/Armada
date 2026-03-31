import { useEffect, useState, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { listVessels, listPipelines, createVoyage } from '../api/client';
import type { Vessel, Pipeline } from '../types/models';

/**
 * Parse a natural-language prompt into discrete tasks.
 * Only splits on explicit delimiters: numbered lists ("1. foo\n2. bar")
 * or semicolons ("foo; bar; baz"). Does NOT split on bare newlines
 * or bullet points -- those are part of a single task description.
 */
function parseTasks(prompt: string): string[] {
  if (!prompt || !prompt.trim()) return [];

  const numbered: string[] = [];
  const re = /(?:^|\n)\s*(\d+)\.\s+(.+?)(?=\n\s*\d+\.\s|$)/gs;
  let m: RegExpExecArray | null;
  while ((m = re.exec(prompt)) !== null) {
    numbered.push(m[2].trim());
  }
  if (numbered.length >= 2) return numbered;

  if (prompt.includes(';')) {
    const parts = prompt.split(';').map((s) => s.trim()).filter(Boolean);
    if (parts.length >= 2) return parts;
  }

  return [prompt.trim()];
}

export default function Dispatch() {
  const navigate = useNavigate();

  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [pipelines, setPipelines] = useState<Pipeline[]>([]);
  const [selectedPipeline, setSelectedPipeline] = useState('');

  const [vesselId, setVesselId] = useState('');
  const [prompt, setPrompt] = useState('');
  const [priority, setPriority] = useState(100);
  const [parsedTasks, setParsedTasks] = useState<string[]>([]);
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

  const handlePromptChange = useCallback(
    (value: string) => {
      setPrompt(value);
      const pipelineObj = pipelines.find((p) => p.name === selectedPipeline);
      const isMultiStage = pipelineObj != null && pipelineObj.stages.length > 1;
      if (isMultiStage) {
        setParsedTasks([]);
      } else {
        const parsed = parseTasks(value);
        const hasNumberedList = /(?:^|\n)\s*\d+\.\s+/.test(value);
        const hasSemicolons = value.includes(';');
        setParsedTasks((hasNumberedList || hasSemicolons) && parsed.length > 1 ? parsed : []);
      }
    },
    [pipelines, selectedPipeline],
  );

  const handleDispatch = async () => {
    if (!prompt.trim()) return;
    if (!vesselId) {
      setResult({ ok: false, message: 'Please select a vessel.' });
      return;
    }

    const selectedPipelineObj = pipelines.find((p) => p.name === selectedPipeline);
    const isMultiStage = selectedPipelineObj != null && selectedPipelineObj.stages.length > 1;

    let tasks: string[];
    if (isMultiStage) {
      tasks = [prompt.trim()];
    } else {
      let parsed = parsedTasks;
      if (!parsed.length) {
        parsed = parseTasks(prompt);
        setParsedTasks(parsed);
      }
      tasks = parsed;
    }
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
      setParsedTasks([]);
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
            Describe what you need done. Use numbered lists or semicolons for multiple tasks.
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
              onChange={(e) => handlePromptChange(e.target.value)}
              rows={12}
              placeholder={'Describe what you need done.\n\nFor multiple tasks, use numbered lists or semicolons:\n1. Add user authentication\n2. Create dashboard page\n3. Write unit tests'}
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
