import { Link } from 'react-router-dom';
import type { Captain, Fleet, Harbor, Pipeline, SelectedPlaybook, Vessel } from '../../types/models';
import { canCaptainStartPlanning } from '../../lib/captains';
import PlaybookSelector from '../shared/PlaybookSelector';

interface PlanningStartCardProps {
  t: (value: string, vars?: Record<string, string | number>) => string;
  loading: boolean;
  captains: Captain[];
  fleets: Fleet[];
  vessels: Vessel[];
  pipelines: Pipeline[];
  title: string;
  captainId: string;
  fleetId: string;
  vesselId: string;
  pipelineId: string;
  availableVessels: Vessel[];
  selectedCaptain: Captain | null;
  selectedPlaybooks: SelectedPlaybook[];
  pendingInitialPrompt?: string;
  creating: boolean;
  canStartSession: boolean;
  onTitleChange: (value: string) => void;
  onCaptainChange: (value: string) => void;
  onFleetChange: (value: string) => void;
  onVesselChange: (value: string) => void;
  onPipelineChange: (value: string) => void;
  onSelectedPlaybooksChange: (value: SelectedPlaybook[]) => void;
  onStart: () => void;
  /** Harbors the session can run on; the picker is shown when there is at least one. */
  harbors?: Harbor[];
  /** The chosen Harbor ('' lets routing choose). */
  harborId?: string;
  onHarborChange?: (value: string) => void;
}

export default function PlanningStartCard(props: PlanningStartCardProps) {
  const {
    t,
    loading,
    captains,
    fleets,
    vessels,
    pipelines,
    title,
    captainId,
    fleetId,
    vesselId,
    pipelineId,
    availableVessels,
    selectedCaptain,
    selectedPlaybooks,
    pendingInitialPrompt,
    creating,
    canStartSession,
    onTitleChange,
    onCaptainChange,
    onFleetChange,
    onVesselChange,
    onPipelineChange,
    onSelectedPlaybooksChange,
    onStart,
    harbors,
    harborId,
    onHarborChange,
  } = props;
  const selectedVessel = vessels.find((vessel) => vessel.id === vesselId) ?? null;
  const preferredHarbor = selectedVessel?.preferredHarborId ? (harbors ?? []).find((harbor) => harbor.id === selectedVessel.preferredHarborId) ?? null : null;

  return (
    <div className="card" style={{ padding: '1rem', marginBottom: '1rem' }}>
      <h3 style={{ marginBottom: '0.35rem' }} title={t('Open a chat with one captain on one vessel. The transcript is saved, and you can dispatch work straight from any reply.')}>{t('Start Session')}</h3>
      <p className="text-muted" style={{ marginBottom: '1rem' }}>
        {t('Reserve a captain on a vessel, then use the transcript as the source of truth for a later dispatch.')}
      </p>
      <div className="alert" style={{ marginBottom: '1rem' }}>
        {t('Planning sessions reserve the selected captain and dock for the duration of the session. The captain can inspect and modify the repository while you plan.')}
      </div>

      {loading ? (
        <p className="text-muted">{t('Loading planning catalog...')}</p>
      ) : (
        <div className="dispatch-form">
          <div className="form-group planning-start-title-group">
            <label title={t('Name for this planning session, shown in the session list. Optional; leave blank and Armada names it for you.')}>{t('Title')}</label>
            <input aria-label={t('Title')}
              title={t('Name for this planning session, shown in the session list. Optional; leave blank and Armada names it for you.')}
              value={title}
              disabled={creating}
              onChange={(event) => onTitleChange(event.target.value)}
              placeholder={t('Optional planning session title')}
            />
          </div>

          <div className="planning-start-grid">
            <div className="form-group">
              <label title={t('Captain you will chat with. It is reserved for the whole session, so it takes no missions until you end it. Only idle captains whose runtime supports planning can be chosen.')}>{t('Captain')}</label>
              <select aria-label={t('Captain')} title={t('Captain you will chat with. It is reserved for the whole session, so it takes no missions until you end it. Only idle captains whose runtime supports planning can be chosen.')} value={captainId} disabled={creating} onChange={(event) => onCaptainChange(event.target.value)}>
                <option value="">{t('Select a captain...')}</option>
                {captains.map((captain) => (
                  <option
                    key={captain.id}
                    value={captain.id}
                    disabled={!canCaptainStartPlanning(captain)}
                  >
                    {`${captain.name} (${captain.runtime}) - ${captain.state}${captain.supportsPlanningSessions ? '' : ' - planning unsupported'}`}
                  </option>
                ))}
              </select>
            </div>

            <div className="form-group">
              <label title={t('Narrows the Vessel list to one fleet. Any fleet lists every vessel. This does not change the session itself.')}>{t('Fleet')}</label>
              <select aria-label={t('Fleet')} title={t('Narrows the Vessel list to one fleet. Any fleet lists every vessel. This does not change the session itself.')} value={fleetId} disabled={creating} onChange={(event) => onFleetChange(event.target.value)}>
                <option value="">{t('Any fleet')}</option>
                {fleets.map((fleet) => (
                  <option key={fleet.id} value={fleet.id}>
                    {fleet.name}
                  </option>
                ))}
              </select>
            </div>

            <div className="form-group">
              <div className="form-label-row">
                <label title={t('Repository the captain plans against. Armada gives the captain its own dock (worktree) on this vessel for the session. Required.')}>{t('Vessel')}</label>
                <Link to="/vessels" className="form-label-link" title={t('Open Vessels to add, import or configure the repositories offered here.')}>{t('Manage vessels')}</Link>
              </div>
              <select aria-label={t('Vessel')} title={t('Repository the captain plans against. Armada gives the captain its own dock (worktree) on this vessel for the session. Required.')} value={vesselId} disabled={creating} onChange={(event) => onVesselChange(event.target.value)}>
                <option value="">{t('Select a vessel...')}</option>
                {availableVessels.map((vessel) => (
                  <option key={vessel.id} value={vessel.id}>
                    {vessel.name}
                  </option>
                ))}
              </select>
            </div>

            {onHarborChange && (harbors ?? []).length > 0 && (
              <div className="form-group">
                <div className="form-label-row">
                  <label title={t('Where the captain runs: a Harbor runs the session on that machine (its dock and every turn), so it sees that machine\'s checkout and tools. Automatic uses the vessel\'s preferred Harbor, then any connected Harbor that can serve the vessel, then the Armada server.')}>{t('Run on')}</label>
                  <Link to="/configuration?tab=harbors" className="form-label-link" title={t('Open Harbors to see which machines are connected and what they can run.')}>{t('Manage Harbors')}</Link>
                </div>
                <select aria-label={t('Run on')} value={harborId ?? ''} disabled={creating} onChange={(event) => onHarborChange(event.target.value)}>
                  <option value="">{preferredHarbor ? t('Automatic (preferred: {{name}})', { name: preferredHarbor.name }) : t('Automatic')}</option>
                  {(harbors ?? []).map((harbor) => {
                    const connected = String(harbor.connectionStatus) === 'Connected';
                    return (
                      <option key={harbor.id} value={harbor.id} disabled={!connected}>
                        {t('Harbor')}: {harbor.name}{connected ? '' : ` (${String(harbor.connectionStatus ?? 'offline').toLowerCase()})`}
                      </option>
                    );
                  })}
                </select>
              </div>
            )}

            <div className="form-group">
              <div className="form-label-row">
                <label title={t('Pipeline to use when you dispatch from this session. Inherit leaves it to the vessel or fleet setting at dispatch time.')}>{t('Pipeline')}</label>
                <Link to="/pipelines" className="form-label-link" title={t('Open Pipelines to create or edit the stage sequences offered here.')}>{t('Manage pipelines')}</Link>
              </div>
              <select aria-label={t('Pipeline')} title={t('Pipeline to use when you dispatch from this session. Inherit leaves it to the vessel or fleet setting at dispatch time.')} value={pipelineId} disabled={creating} onChange={(event) => onPipelineChange(event.target.value)}>
                <option value="">{t('Inherit later during dispatch')}</option>
                {pipelines.map((pipeline) => (
                  <option key={pipeline.id} value={pipeline.id}>
                    {pipeline.name}
                  </option>
                ))}
              </select>
            </div>
          </div>

          {selectedCaptain && !selectedCaptain.supportsPlanningSessions && (
            <div className="alert alert-warning">
              {selectedCaptain.planningSessionSupportReason || t('This captain runtime is not currently supported for planning sessions.')}
            </div>
          )}

          {selectedCaptain?.supportsPlanningSessions && (
            <div className="text-muted" style={{ marginTop: '-0.25rem', marginBottom: '0.25rem' }}>
              {t("Planning runs this captain's CLI through transcript-backed turn relaunches.")}
            </div>
          )}

          {!!pendingInitialPrompt?.trim() && (
            <div className="alert" style={{ marginBottom: '1rem' }}>
              {t('Workspace prefilled an initial planning brief. Start the session and review the draft message in the transcript composer before sending it to the captain.')}
            </div>
          )}

          <PlaybookSelector value={selectedPlaybooks} onChange={onSelectedPlaybooksChange} disabled={creating} />

          {creating && (
            <div className="alert alert-warning" role="status" aria-live="polite" style={{ marginTop: '0.75rem', marginBottom: '0.5rem' }}>
              <div style={{ fontWeight: 700, marginBottom: '0.4rem' }}>
                {t('Starting planning session...')}
              </div>
              <div className="progress-bar" aria-hidden="true">
                <div className="progress-fill progress-fill-indeterminate" />
              </div>
              <div>
                {t('Armada is reserving the captain, provisioning the dock, and preparing the worktree. First-time repository setup can take a few minutes.')}
              </div>
            </div>
          )}

          <div className="form-actions">
            <button
              type="button"
              className="btn-primary"
              disabled={!canStartSession}
              onClick={onStart}
              aria-busy={creating}
              title={canStartSession ? t('Reserve the captain, set up its dock on the vessel and open the chat. First-time repository setup can take a few minutes.') : t('Choose an idle captain that supports planning and a vessel first.')}
            >
              {creating ? t('Starting Planning Session...') : t('Start Planning Session')}
            </button>
            {creating && (
              <div className="text-muted" style={{ marginTop: '0.5rem' }}>
                {t('You can stay on this page while Armada finishes setup. The session will appear below as soon as provisioning completes.')}
              </div>
            )}
          </div>

          {vessels.length === 0 && (
            <div className="text-muted">
              {t('Create a vessel first so Armada has a repository context for planning.')}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
