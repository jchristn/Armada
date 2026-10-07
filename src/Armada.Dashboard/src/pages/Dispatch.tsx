import { useEffect, useRef, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { Link } from 'react-router-dom';
import { listVessels, listPipelines, listCaptains, listPersonas, createVoyage, getVesselReadiness } from '../api/client';
import type { Vessel, Pipeline, SelectedPlaybook, VesselReadinessResult, Captain, Persona } from '../types/models';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import PlaybookSelector from '../components/shared/PlaybookSelector';
import ReadinessPanel from '../components/shared/ReadinessPanel';
import PageHeader from '../components/shared/PageHeader';
import CaptainPicker from '../components/shared/CaptainPicker';
import FallbackTierSelect from '../components/shared/FallbackTierSelect';
import { sortByName } from '../lib/sortByName';
import {
  buildDispatchVoyageRequest,
  dispatchPrefillNotice,
  effectiveStepPersonas,
  hasDispatchPrefill,
  parseDispatchPriority,
  pipelineStepPersonas,
  seedStepAssignments,
  type DispatchPrefillState,
  type StepAssignment,
} from '../lib/dispatchRequest';

export default function Dispatch() {
  const { t } = useLocale();
  const { pushToast } = useNotifications();
  const navigate = useNavigate();
  const location = useLocation();

  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [pipelines, setPipelines] = useState<Pipeline[]>([]);
  const [captains, setCaptains] = useState<Captain[]>([]);
  const [personas, setPersonas] = useState<Persona[]>([]);
  const [selectedPipeline, setSelectedPipeline] = useState('');
  const [stepAssignments, setStepAssignments] = useState<Record<string, StepAssignment>>({});
  const [voyageTitle, setVoyageTitle] = useState('');

  const [vesselId, setVesselId] = useState('');
  const [objectiveId, setObjectiveId] = useState('');
  const [prompt, setPrompt] = useState('');
  const [priority, setPriority] = useState(100);
  const [selectedPlaybooks, setSelectedPlaybooks] = useState<SelectedPlaybook[]>([]);
  const [dispatching, setDispatching] = useState(false);
  const [result, setResult] = useState<{ ok: boolean; message: string } | null>(null);
  const [readiness, setReadiness] = useState<VesselReadinessResult | null>(null);
  const [loadingReadiness, setLoadingReadiness] = useState(false);
  const prefillAppliedRef = useRef(false);

  useEffect(() => {
    Promise.all([
      listVessels({ pageSize: 9999 }).catch(() => null),
      listPipelines({ pageSize: 9999 }).catch(() => null),
      listCaptains({ pageSize: 9999 }).catch(() => null),
      listPersonas({ pageSize: 9999 }).catch(() => null),
    ]).then(([vRes, pRes, cRes, prRes]) => {
      if (vRes) setVessels(sortByName(vRes.objects));
      if (pRes) setPipelines(pRes.objects);
      if (cRes) setCaptains(cRes.objects);
      if (prRes) setPersonas(prRes.objects);
    });
  }, []);

  // The distinct personas (steps) of the selected pipeline, in stage order, de-duplicated.
  const selectedPipelineObj = pipelines.find((p) => p.name === selectedPipeline) ?? null;
  const stepPersonas: string[] = pipelineStepPersonas(selectedPipelineObj);

  // With a specific pipeline, offer a captain per step. With "Inherit" (no explicit pipeline) the resolved
  // stages are not known at dispatch time, so offer a single wildcard ("*") picker that applies to every
  // step -- letting an operator pin a captain (e.g. an API-endpoint captain) regardless of pipeline.
  const effectivePersonas: string[] = effectiveStepPersonas(stepPersonas);

  // Seed each step's preferred captain from that persona's default whenever the pipeline (or personas) change.
  useEffect(() => {
    setStepAssignments((current) => seedStepAssignments(current, effectivePersonas, personas));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedPipeline, personas]);

  useEffect(() => {
    if (prefillAppliedRef.current) return;

    const prefill = location.state as DispatchPrefillState | null;
    if (!prefill || !hasDispatchPrefill(prefill)) return;

    if (prefill.vesselId) setVesselId(prefill.vesselId);
    if (prefill.pipelineName) setSelectedPipeline(prefill.pipelineName);
    if (prefill.prompt) setPrompt(prefill.prompt);
    if (prefill.selectedPlaybooks?.length) setSelectedPlaybooks(prefill.selectedPlaybooks);
    if (prefill.voyageTitle) setVoyageTitle(prefill.voyageTitle);
    if (prefill.objectiveId) setObjectiveId(prefill.objectiveId);

    prefillAppliedRef.current = true;
  }, [location.state]);

  useEffect(() => {
    if (!vesselId) {
      setReadiness(null);
      setLoadingReadiness(false);
      return;
    }

    let cancelled = false;
    setLoadingReadiness(true);
    getVesselReadiness(vesselId)
      .then((value) => {
        if (!cancelled) setReadiness(value);
      })
      .catch(() => {
        if (!cancelled) setReadiness(null);
      })
      .finally(() => {
        if (!cancelled) setLoadingReadiness(false);
      });

    return () => {
      cancelled = true;
    };
  }, [vesselId]);

  const handleDispatch = async () => {
    if (!prompt.trim()) return;
    if (!vesselId) {
      setResult({ ok: false, message: t('Please select a vessel.') });
      return;
    }

    const isMultiStage = selectedPipelineObj != null && selectedPipelineObj.stages.length > 1;

    setDispatching(true);
    setResult(null);
    try {
      const request = buildDispatchVoyageRequest({
        vesselId,
        prompt,
        priority,
        voyageTitle,
        objectiveId,
        pipeline: selectedPipeline,
        selectedPlaybooks,
        stepAssignments,
        multiTaskTitle: t('Multi-task voyage'),
      });
      const missions = request.missions;
      const voyage = await createVoyage(request);
      const missionCount = isMultiStage
        ? t('{{count}} pipeline stages', { count: selectedPipelineObj!.stages.length })
        : t('{{count}} mission(s)', { count: missions.length });
      const successMessage = t('Dispatched voyage with {{missionCount}}', { missionCount });
      setResult({ ok: true, message: successMessage });
      pushToast('success', successMessage);
      setVoyageTitle('');
      setPrompt('');
      setTimeout(() => {
        navigate(`/voyages/${voyage.id}`);
      }, 1500);
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : t('Unknown error');
      setResult({ ok: false, message: t('Failed: {{message}}', { message: msg }) });
    } finally {
      setDispatching(false);
    }
  };

  return (
    <div>
      <PageHeader
        title={t('Dispatch')}
        subtitle={t('Describe the work you want Armada to dispatch through the selected vessel and pipeline.')}
      />

      <div className="card" style={{ marginBottom: '1rem' }}>
        <div className="dispatch-form">
          {dispatchPrefillNotice(location.state as DispatchPrefillState | null) && (
            <div className="alert" style={{ marginBottom: '1rem' }}>
              {t(dispatchPrefillNotice(location.state as DispatchPrefillState | null) ?? '')}
              {objectiveId && (
                <button type="button" className="btn btn-sm" style={{ marginLeft: '0.75rem' }} onClick={() => navigate(`/backlog/${objectiveId}`)}>
                  {t('Open Backlog Item')}
                </button>
              )}
            </div>
          )}

          {vesselId && (
            <ReadinessPanel
              title={t('Vessel Readiness')}
              readiness={readiness}
              loading={loadingReadiness}
              emptyMessage={t('Select a vessel to inspect readiness.')}
              compact
            />
          )}

          {/* Row 1: Vessel + Pipeline + Priority + Optional Voyage Title */}
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: '0 1.5rem' }}>
            <div className="form-group">
              <label>{t('Vessel')}</label>
              <select aria-label={t('Vessel')}
                value={vesselId}
                onChange={(e) => setVesselId(e.target.value)}
                required
              >
                <option value="">{t('Select a vessel...')}</option>
                {vessels.map((v) => (
                  <option key={v.id} value={v.id}>
                    {v.name}
                  </option>
                ))}
              </select>
            </div>
            <div className="form-group">
              <div className="form-label-row">
                <label>{t('Pipeline')}</label>
                <Link to="/pipelines" className="form-label-link">{t('Manage pipelines')}</Link>
              </div>
              <select aria-label={t('Pipeline')}
                value={selectedPipeline}
                onChange={(e) => setSelectedPipeline(e.target.value)}
              >
                <option value="">{t('Inherit (vessel, then fleet, then WorkerOnly)')}</option>
                {pipelines.map((p) => (
                  <option key={p.id} value={p.name}>
                    {p.name} ({p.stages.map((s) => s.personaName).join(' -> ')})
                  </option>
                ))}
              </select>
            </div>
            <div className="form-group">
              <label>{t('Priority')}</label>
              <input aria-label={t('Higher priority missions are assigned first (default 100)')}
                type="number"
                value={priority}
                onChange={(e) => setPriority(parseDispatchPriority(e.target.value))}
                min={0}
                max={1000}
                title={t('Higher priority missions are assigned first (default 100)')}
              />
            </div>
            <div className="form-group">
              <label>{t('Voyage Title')}</label>
              <input aria-label={t('Voyage Title')}
                value={voyageTitle}
                onChange={(e) => setVoyageTitle(e.target.value)}
                placeholder={t('Optional override for the voyage title')}
              />
            </div>
          </div>

          {/* Prompt */}
          <div className="form-group">
            <label>{t('Description')}</label>
            <textarea aria-label={t('Description')}
              value={prompt}
              onChange={(e) => setPrompt(e.target.value)}
              rows={12}
              placeholder={t(`Describe what you need done.

Armada will dispatch this request as a voyage on the selected vessel.`)}
            />
          </div>

          <PlaybookSelector value={selectedPlaybooks} onChange={setSelectedPlaybooks} disabled={dispatching} />

          {effectivePersonas.length > 0 && (
            <div className="form-group">
              <div className="form-label-row">
                <label>{t('Captain Assignments')}</label>
                <Link to="/personas" className="form-label-link">{t('Manage persona defaults')}</Link>
              </div>
              <p className="text-dim" style={{ fontSize: '0.8rem', margin: '0 0 0.5rem' }}>
                {stepPersonas.length > 0
                  ? t('Pick a preferred captain per pipeline step. When it is busy, Armada falls back to an idle captain at or above the fallback tier. Defaults come from each persona.')
                  : t('No specific pipeline selected. This captain applies to every step of the mission; leave blank to let Armada auto-assign an idle captain.')}
              </p>
              <div className="card" style={{ padding: '0.5rem 0.85rem' }}>
                <div className="captain-assignment-row" style={{ fontSize: '0.72rem', fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.04em', color: 'var(--text-dim)' }}>
                  <span>{t('Step')}</span>
                  <span>{t('Preferred Captain')}</span>
                  <span>{t('Fallback Tier')}</span>
                </div>
                {effectivePersonas.map((persona) => {
                  const assignment = stepAssignments[persona] ?? { captainId: null, fallbackTier: null };
                  return (
                    <div className="captain-assignment-row" key={persona}>
                      <span>{persona === '*' ? t('All steps') : persona}</span>
                      <CaptainPicker
                        captains={captains}
                        value={assignment.captainId}
                        onChange={(captainId) => setStepAssignments((cur) => ({ ...cur, [persona]: { ...assignment, captainId } }))}
                        disabled={dispatching}
                        ariaLabel={t('Preferred captain for {{persona}}', { persona })}
                      />
                      <FallbackTierSelect
                        value={assignment.fallbackTier}
                        onChange={(fallbackTier) => setStepAssignments((cur) => ({ ...cur, [persona]: { ...assignment, fallbackTier } }))}
                        disabled={dispatching}
                        ariaLabel={t('Fallback tier for {{persona}}', { persona })}
                      />
                    </div>
                  );
                })}
              </div>
            </div>
          )}

          <div className="form-actions">
            <button
              type="button"
              className="btn-primary"
              disabled={dispatching || !vesselId || !prompt.trim()}
              onClick={handleDispatch}
            >
              {dispatching ? t('Dispatching...') : t('Dispatch')}
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
