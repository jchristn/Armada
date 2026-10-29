import RunApprovalFields, { DEFAULT_RUN_APPROVALS } from '../components/shared/RunApprovalFields';
import { useEffect, useRef, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { Link } from 'react-router-dom';
import { listVessels, listPipelines, listCaptains, listPersonas, createVoyage, getVesselReadiness } from '../api/client';
import { listAll } from '../api/paging';
import type { Vessel, Pipeline, SelectedPlaybook, VesselReadinessResult, Captain, Persona } from '../types/models';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import PlaybookSelector from '../components/shared/PlaybookSelector';
import ReadinessPanel from '../components/shared/ReadinessPanel';
import DispatchPreview from '../components/blackbox/DispatchWarnings';
import { dispatchWarningsFrom } from '../lib/dispatchPreview';
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
  const [approvalOptions, setApprovalOptions] = useState({ ...DEFAULT_RUN_APPROVALS });
  const [selectedPlaybooks, setSelectedPlaybooks] = useState<SelectedPlaybook[]>([]);
  const [dispatching, setDispatching] = useState(false);
  const [result, setResult] = useState<{ ok: boolean; message: string } | null>(null);
  const [readiness, setReadiness] = useState<VesselReadinessResult | null>(null);
  const [loadingReadiness, setLoadingReadiness] = useState(false);
  const prefillAppliedRef = useRef(false);

  useEffect(() => {
    Promise.all([
      listAll(listVessels).catch(() => null),
      listAll(listPipelines).catch(() => null),
      listAll(listCaptains).catch(() => null),
      listAll(listPersonas).catch(() => null),
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
    if (!Number.isInteger(approvalOptions.timeoutSeconds) || approvalOptions.timeoutSeconds < 10 || approvalOptions.timeoutSeconds > 3600) {
      setResult({ ok: false, message: t('Approval timeout must be a whole number between 10 and 3600 seconds.') });
      return;
    }
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
      const voyage = await createVoyage({ ...request, approvalOptions });
      const missionCount = isMultiStage
        ? t('{{count}} pipeline stages', { count: selectedPipelineObj!.stages.length })
        : t('{{count}} mission(s)', { count: missions.length });
      const warnings = dispatchWarningsFrom(voyage);
      const successMessage = warnings
        ? t('Dispatched voyage with {{missionCount}}, with {{count}} warning(s)', { missionCount, count: warnings.overlapWarnings.length + warnings.briefLintWarnings.length })
        : t('Dispatched voyage with {{missionCount}}', { missionCount });
      setResult({ ok: true, message: successMessage });
      pushToast('success', successMessage);
      setVoyageTitle('');
      setPrompt('');
      setTimeout(() => {
        navigate(`/voyages/${voyage.id}`, { state: { dispatchWarnings: warnings } });
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
                <button type="button" className="btn btn-sm" style={{ marginLeft: '0.75rem' }} onClick={() => navigate(`/backlog/${objectiveId}`)} title={t('Open the backlog item this dispatch was started from. The voyage you create is linked to it.')}>
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
              <label title={t('Repository the work runs in. The request becomes a voyage on this vessel, and its missions get their own branches there. Required.')}>{t('Vessel')}</label>
              <select aria-label={t('Vessel')}
                title={t('Repository the work runs in. The request becomes a voyage on this vessel, and its missions get their own branches there. Required.')}
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
                <label title={t('Sequence of persona stages the work runs through, such as Architect then Worker then Judge. Inherit uses the vessel\'s pipeline, then the fleet\'s, then Worker only.')}>{t('Pipeline')}</label>
                <Link to="/pipelines" className="form-label-link" title={t('Open Pipelines to create or edit the stage sequences offered here.')}>{t('Manage pipelines')}</Link>
              </div>
              <select aria-label={t('Pipeline')}
                title={t('Sequence of persona stages the work runs through, such as Architect then Worker then Judge. Inherit uses the vessel\'s pipeline, then the fleet\'s, then Worker only.')}
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
              <label title={t('Dispatch order from 0 to 1000. Lower numbers are assigned to a captain first; the default is 100.')}>{t('Priority')}</label>
              <input aria-label={t('Priority')}
                type="number"
                value={priority}
                onChange={(e) => setPriority(parseDispatchPriority(e.target.value))}
                min={0}
                max={1000}
                title={t('Dispatch order from 0 to 1000. Lower numbers are assigned to a captain first; the default is 100.')}
              />
            </div>
            <div className="form-group">
              <label title={t('Name for the voyage this creates. Leave blank to use the first 80 characters of the description.')}>{t('Voyage Title')}</label>
              <input aria-label={t('Voyage Title')}
                title={t('Name for the voyage this creates. Leave blank to use the first 80 characters of the description.')}
                value={voyageTitle}
                onChange={(e) => setVoyageTitle(e.target.value)}
                placeholder={t('Optional override for the voyage title')}
              />
            </div>
          </div>

          {/* Prompt */}
          <div className="form-group">
            <label title={t('What you want done, in plain language. This becomes the mission\'s instructions, so include where to look and how to tell it is finished. Required.')}>{t('Description')}</label>
            <textarea aria-label={t('Description')}
              title={t('What you want done, in plain language. This becomes the mission\'s instructions, so include where to look and how to tell it is finished. Required.')}
              value={prompt}
              onChange={(e) => setPrompt(e.target.value)}
              rows={12}
              placeholder={t(`Describe what you need done.

Armada will dispatch this request as a voyage on the selected vessel.`)}
            />
          </div>
          <DispatchPreview vesselId={vesselId} title={voyageTitle} description={prompt} enabled={!dispatching} />

          <RunApprovalFields value={approvalOptions} onChange={setApprovalOptions} />
          <PlaybookSelector value={selectedPlaybooks} onChange={setSelectedPlaybooks} disabled={dispatching} />

          {effectivePersonas.length > 0 && (
            <div className="form-group">
              <div className="form-label-row">
                <label title={t('Optional: pick which captain should run each pipeline step. Blank leaves the choice to normal routing by persona and tier.')}>{t('Captain Assignments')}</label>
                <Link to="/personas" className="form-label-link" title={t('Open Personas to change the default captain and fallback tier each persona uses.')}>{t('Manage persona defaults')}</Link>
              </div>
              <p className="text-dim" style={{ fontSize: '0.8rem', margin: '0 0 0.5rem' }}>
                {stepPersonas.length > 0
                  ? t('A captain chosen for a step overrides the persona\'s captain priorities. When it is busy, the step falls back to another idle captain at or above the fallback tier (Auto: the chosen captain\'s own tier).')
                  : t('No specific pipeline selected. A captain chosen here runs every step and overrides the persona\'s captain priorities; when it is busy, another idle captain at or above the fallback tier (Auto: its own tier) is used. Leave on Auto for normal routing.')}
              </p>
              <div className="card" style={{ padding: '0.5rem 0.85rem' }}>
                <div className="captain-assignment-row" style={{ fontSize: '0.72rem', fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.04em', color: 'var(--text-dim)' }}>
                  <span title={t('Pipeline step, named by the persona that runs it.')}>{t('Step')}</span>
                  <span title={t('Captain to use for this step; choosing one overrides the persona\'s captain priorities. Auto uses normal routing.')}>{t('Preferred Captain')}</span>
                  <span title={t('When the chosen captain is busy, the step uses another idle captain at this tier or higher. Auto means the chosen captain\'s own tier.')}>{t('Fallback Tier')}</span>
                </div>
                {effectivePersonas.map((persona) => {
                  const assignment = stepAssignments[persona] ?? { captainId: null, fallbackTier: null };
                  return (
                    <div className="captain-assignment-row" key={persona}>
                      <span>{persona === '*' ? t('All steps') : persona}</span>
                      <div>
                        <CaptainPicker
                          captains={captains}
                          value={assignment.captainId}
                          onChange={(captainId) => setStepAssignments((cur) => ({ ...cur, [persona]: { ...assignment, captainId, strictCaptain: captainId ? assignment.strictCaptain : false } }))}
                          disabled={dispatching}
                          ariaLabel={t('Preferred captain for {{persona}}', { persona })}
                          title={t('Captain to use for this step; choosing one overrides the persona\'s captain priorities. Auto uses normal routing.')}
                        />
                        {assignment.captainId ? <label title={t('Wait when busy; do not substitute another captain or automatically escalate.')} style={{ display: 'flex', alignItems: 'center', gap: 6, marginTop: 6, fontSize: 12 }}>
                          <input type="checkbox" checked={!!assignment.strictCaptain} disabled={dispatching}
                            onChange={(event) => setStepAssignments((cur) => ({ ...cur, [persona]: { ...assignment, strictCaptain: event.target.checked } }))} />
                          {t('Wait for selected captain')}
                        </label> : null}
                      </div>
                      <FallbackTierSelect
                        value={assignment.fallbackTier}
                        onChange={(fallbackTier) => setStepAssignments((cur) => ({ ...cur, [persona]: { ...assignment, fallbackTier } }))}
                        disabled={dispatching || !!assignment.strictCaptain}
                        ariaLabel={t('Fallback tier for {{persona}}', { persona })}
                        title={t('When the chosen captain is busy, the step uses another idle captain at this tier or higher. Auto means the chosen captain\'s own tier.')}
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
              title={!vesselId || !prompt.trim() ? t('Choose a vessel and write a description first.') : t('Create a voyage from this request and queue its missions for idle captains on the selected vessel.')}
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
