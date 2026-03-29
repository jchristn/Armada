import { useEffect, useState, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { listVessels, listVoyages, listPipelines, createMission, createVoyage } from '../api/client';
import type { Vessel, Voyage, Pipeline } from '../types/models';

/**
 * Parse a natural-language prompt into discrete tasks.
 * Supports numbered lists, semicolon-separated, or single tasks.
 */
function parseTasks(prompt: string): string[] {
  if (!prompt || !prompt.trim()) return [];

  // Try numbered list: "1. foo\n2. bar"
  const numbered: string[] = [];
  const re = /(?:^|\n)\s*(\d+)\.\s+(.+?)(?=\n\s*\d+\.\s|$)/gs;
  let m: RegExpExecArray | null;
  while ((m = re.exec(prompt)) !== null) {
    numbered.push(m[2].trim());
  }
  if (numbered.length >= 2) return numbered;

  // Try semicolon-separated
  if (prompt.includes(';')) {
    const parts = prompt
      .split(';')
      .map((s) => s.trim())
      .filter(Boolean);
    if (parts.length >= 2) return parts;
  }

  // Try newline-separated with bullet points
  const lines = prompt
    .split('\n')
    .map((l) => l.replace(/^[\s\-*]+/, '').trim())
    .filter(Boolean);
  if (lines.length >= 2) return lines;

  // Single task
  return [prompt.trim()];
}

interface QuickDispatchState {
  vesselId: string;
  prompt: string;
}

interface AdvancedDispatchState {
  title: string;
  description: string;
  vesselId: string;
  priority: number;
  voyageId: string;
}

export default function Dispatch() {
  const navigate = useNavigate();
  const [mode, setMode] = useState<'quick' | 'advanced'>('quick');

  // Shared data
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [voyages, setVoyages] = useState<Voyage[]>([]);
  const [pipelines, setPipelines] = useState<Pipeline[]>([]);
  const [selectedPipeline, setSelectedPipeline] = useState('');

  // Quick mode state
  const [quick, setQuick] = useState<QuickDispatchState>({ vesselId: '', prompt: '' });
  const [parsedTasks, setParsedTasks] = useState<string[]>([]);
  const [quickDispatching, setQuickDispatching] = useState(false);
  const [quickResult, setQuickResult] = useState<{ ok: boolean; message: string } | null>(null);

  // Advanced mode state
  const [advanced, setAdvanced] = useState<AdvancedDispatchState>({
    title: '',
    description: '',
    vesselId: '',
    priority: 100,
    voyageId: '',
  });
  const [dispatching, setDispatching] = useState(false);
  const [dispatchResult, setDispatchResult] = useState<{ ok: boolean; message: string } | null>(
    null,
  );

  useEffect(() => {
    Promise.all([
      listVessels({ pageSize: 9999 }).catch(() => null),
      listVoyages({ pageSize: 9999 }).catch(() => null),
      listPipelines({ pageSize: 9999 }).catch(() => null),
    ]).then(([vRes, voyRes, pRes]) => {
      if (vRes) setVessels(vRes.objects);
      if (voyRes) setVoyages(voyRes.objects.filter((v) => v.status === 'Open' || v.status === 'InProgress'));
      if (pRes) setPipelines(pRes.objects);
    });
  }, []);

  const previewQuickTasks = useCallback(
    (prompt: string) => {
      setQuick((prev) => ({ ...prev, prompt }));
      setParsedTasks(parseTasks(prompt));
    },
    [],
  );

  const handleQuickDispatch = async () => {
    if (!quick.prompt.trim()) return;
    if (!quick.vesselId) {
      setQuickResult({ ok: false, message: 'Please select a vessel.' });
      return;
    }

    // Determine if the selected pipeline has multiple stages (Architect, Judge, etc.)
    // If so, send the entire prompt as a single mission -- let the pipeline stages handle decomposition
    const selectedPipelineObj = pipelines.find((p) => p.name === selectedPipeline);
    const isMultiStage = selectedPipelineObj != null && selectedPipelineObj.stages.length > 1;

    let tasks: string[];
    if (isMultiStage) {
      // Multi-stage pipeline: entire prompt is one mission
      tasks = [quick.prompt.trim()];
    } else {
      // WorkerOnly or no pipeline: parse into discrete tasks
      let parsed = parsedTasks;
      if (!parsed.length) {
        parsed = parseTasks(quick.prompt);
        setParsedTasks(parsed);
      }
      tasks = parsed;
    }
    if (!tasks.length) return;

    setQuickDispatching(true);
    setQuickResult(null);
    try {
      // For multi-stage pipelines: short title, full prompt in description only
      // For single tasks: title = first 80 chars, description = full text
      const voyageTitle = tasks.length > 1 ? 'Multi-task voyage' : tasks[0].substring(0, 80);
      const missions = tasks.map((t) => ({
        vesselId: quick.vesselId,
        title: t.substring(0, 80),
        description: t,
      }));
      const voyage = await createVoyage({ title: voyageTitle, vesselId: quick.vesselId, missions, ...(selectedPipeline ? { pipeline: selectedPipeline } : {}) });
      const missionCount = isMultiStage
        ? `${selectedPipelineObj!.stages.length} pipeline stages`
        : `${missions.length} mission${missions.length !== 1 ? 's' : ''}`;
      setQuickResult({
        ok: true,
        message: `Dispatched voyage with ${missionCount}`,
      });
      setQuick({ vesselId: quick.vesselId, prompt: '' });
      setParsedTasks([]);
      // Navigate to the created voyage after a short delay
      setTimeout(() => {
        navigate(`/voyages/${voyage.id}`);
      }, 1500);
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : 'Unknown error';
      setQuickResult({ ok: false, message: `Failed: ${msg}` });
    } finally {
      setQuickDispatching(false);
    }
  };

  const handleAdvancedDispatch = async (e: React.FormEvent) => {
    e.preventDefault();
    setDispatching(true);
    setDispatchResult(null);
    try {
      const body: Record<string, unknown> = {
        title: advanced.title,
        description: advanced.description,
        vesselId: advanced.vesselId,
        priority: advanced.priority || 100,
      };
      if (advanced.voyageId) body.voyageId = advanced.voyageId;

      const mission = await createMission(body);
      setDispatchResult({
        ok: true,
        message: `Mission dispatched: ${mission.id || 'OK'}`,
      });
      setAdvanced({ title: '', description: '', vesselId: '', priority: 100, voyageId: '' });
      // Navigate to mission detail after a short delay
      setTimeout(() => {
        if (mission.id) navigate(`/missions/${mission.id}`);
        else navigate('/missions');
      }, 1500);
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : 'Unknown error';
      setDispatchResult({ ok: false, message: `Failed: ${msg}` });
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
            Create and dispatch missions to AI captains. Use quick mode for natural language or
            advanced mode for full control.
          </p>
        </div>
      </div>

      <div className="card" style={{ marginBottom: '1rem' }}>
        {/* Mode Tabs */}
        <div className="dispatch-tabs">
          <button
            className={`dispatch-tab ${mode === 'quick' ? 'active' : ''}`}
            onClick={() => setMode('quick')}
            title="Natural language task input with smart splitting"
          >
            Quick
          </button>
          <button
            className={`dispatch-tab ${mode === 'advanced' ? 'active' : ''}`}
            onClick={() => setMode('advanced')}
            title="Manual mission fields with full control"
          >
            Advanced
          </button>
        </div>

        {/* Quick Mode */}
        {mode === 'quick' && (
          <div className="dispatch-form">
            <div className="form-group">
              <label>Vessel</label>
              <select
                value={quick.vesselId}
                onChange={(e) => setQuick({ ...quick, vesselId: e.target.value })}
                required
                title="The repository where missions will be executed"
              >
                <option value="">Select a vessel...</option>
                {vessels.map((v) => (
                  <option key={v.id} value={v.id}>
                    {v.name} ({v.id})
                  </option>
                ))}
              </select>
            </div>
            <div className="form-group">
              <label>Pipeline</label>
              <select
                value={selectedPipeline}
                onChange={(e) => setSelectedPipeline(e.target.value)}
                title="Override the default pipeline for this dispatch"
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
              <label>Tasks</label>
              <textarea
                value={quick.prompt}
                onChange={(e) => previewQuickTasks(e.target.value)}
                rows={10}
                placeholder={
                  'Describe your tasks in natural language.\n\nUse numbered lists, semicolons, or just describe a single task:\n1. Add user authentication\n2. Create dashboard page\n3. Write unit tests'
                }
                title="Enter tasks as a numbered list, semicolon-separated, or as a single task"
              />
            </div>

            {/* Task Preview */}
            {parsedTasks.length > 0 && (
              <div className="dispatch-preview">
                <div className="dispatch-preview-header">
                  <span>
                    {parsedTasks.length} task{parsedTasks.length !== 1 ? 's' : ''} detected
                  </span>
                </div>
                <ol className="dispatch-preview-list">
                  {parsedTasks.map((task, i) => (
                    <li key={i}>{task}</li>
                  ))}
                </ol>
              </div>
            )}

            <div className="form-actions">
              <button
                type="button"
                className="btn-primary"
                disabled={quickDispatching || !quick.vesselId || !quick.prompt.trim()}
                onClick={handleQuickDispatch}
                title="Create a voyage with the parsed tasks"
              >
                {quickDispatching ? 'Dispatching...' : 'Dispatch'}
              </button>
              <button
                type="button"
                className="btn-sm"
                onClick={() => navigate('/missions')}
                title="Close and go to missions"
              >
                Cancel
              </button>
            </div>

            {quickResult && (
              <div className={`alert ${quickResult.ok ? 'alert-success' : 'alert-error'}`}>
                {quickResult.message}
              </div>
            )}
          </div>
        )}

        {/* Advanced Mode */}
        {mode === 'advanced' && (
          <form className="dispatch-form" onSubmit={handleAdvancedDispatch}>
            <div className="form-group">
              <label>Title</label>
              <input
                type="text"
                value={advanced.title}
                onChange={(e) => setAdvanced({ ...advanced, title: e.target.value })}
                placeholder="What needs to be done?"
                required
                title="Short summary of what the mission should accomplish"
              />
            </div>
            <div className="form-group">
              <label>Description</label>
              <textarea
                value={advanced.description}
                onChange={(e) => setAdvanced({ ...advanced, description: e.target.value })}
                rows={3}
                placeholder="Detailed instructions for the AI captain..."
                title="Detailed instructions and context for the AI captain"
              />
            </div>
            <div className="form-row">
              <div className="form-group" style={{ flex: 2 }}>
                <label>Vessel</label>
                <select
                  value={advanced.vesselId}
                  onChange={(e) => setAdvanced({ ...advanced, vesselId: e.target.value })}
                  required
                  title="The repository where the mission will be executed"
                >
                  <option value="">Select a vessel...</option>
                  {vessels.map((v) => (
                    <option key={v.id} value={v.id}>
                      {v.name} ({v.id})
                    </option>
                  ))}
                </select>
              </div>
              <div className="form-group" style={{ flex: 1 }}>
                <label>Priority</label>
                <input
                  type="number"
                  value={advanced.priority}
                  onChange={(e) =>
                    setAdvanced({ ...advanced, priority: parseInt(e.target.value) || 100 })
                  }
                  min={0}
                  max={1000}
                  placeholder="100"
                  title="Higher priority missions are assigned first (default 100)"
                />
              </div>
            </div>
            <div className="form-group">
              <label>Voyage (optional)</label>
              <select
                value={advanced.voyageId}
                onChange={(e) => setAdvanced({ ...advanced, voyageId: e.target.value })}
                title="Optionally group this mission into an existing voyage"
              >
                <option value="">No voyage</option>
                {voyages.map((v) => (
                  <option key={v.id} value={v.id}>
                    {v.title} ({v.id})
                  </option>
                ))}
              </select>
            </div>
            <div className="form-actions">
              <button
                type="submit"
                className="btn-primary"
                disabled={dispatching}
                title="Create the mission and assign it to an available captain"
              >
                {dispatching ? 'Dispatching...' : 'Dispatch Mission'}
              </button>
              <button
                type="button"
                className="btn-sm"
                onClick={() => navigate('/missions')}
                title="Close and go to missions"
              >
                Cancel
              </button>
            </div>

            {dispatchResult && (
              <div className={`alert ${dispatchResult.ok ? 'alert-success' : 'alert-error'}`}>
                {dispatchResult.message}
              </div>
            )}
          </form>
        )}
      </div>
    </div>
  );
}
