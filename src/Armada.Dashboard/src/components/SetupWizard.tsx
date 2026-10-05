import { useCallback, useEffect, useMemo, useState, type FormEvent, type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  createCaptain,
  createFleet,
  createVessel,
  dispatchMission,
  getVesselReadiness,
  getMission,
  listCaptains,
  listEnvironments,
  listFleets,
  listVessels,
  listWorkflowProfiles,
} from '../api/client';
import MuxRuntimeFields from './captains/MuxRuntimeFields';
import { buildMuxRuntimeOptionsJson, EMPTY_MUX_CAPTAIN_FORM, isMuxRuntime, type MuxCaptainFormFields } from '../lib/mux';
import type { Captain, DeploymentEnvironment, Fleet, Mission, Vessel, VesselReadinessResult, WorkflowProfile } from '../types/models';
import { useLocale } from '../context/LocaleContext';
import MissionFailureDetails, { FAILED_MISSION_STATUSES } from './shared/MissionFailureDetails';
import CopyButton from './shared/CopyButton';

const STORAGE_KEY = 'armada_setup_completed';

export function isSetupComplete(): boolean {
  try {
    return localStorage.getItem(STORAGE_KEY) === 'true';
  } catch {
    return false;
  }
}

export function markSetupComplete(): void {
  try {
    localStorage.setItem(STORAGE_KEY, 'true');
  } catch {
    // storage unavailable
  }
}

/**
 * Clear the "setup complete" flag. Used when the deployment is found to be empty (e.g. after a
 * factory-reset), so a browser that finished setup against a previous deployment does not permanently
 * suppress the wizard on the fresh one.
 */
export function clearSetupComplete(): void {
  try {
    localStorage.removeItem(STORAGE_KEY);
  } catch {
    // storage unavailable
  }
}

export interface SetupWizardProps {
  onClose: () => void;
  onHighlightChange?: (paths: string[]) => void;
}

type ResourceMode = 'existing' | 'new';
type ResultKind = 'success' | 'error' | 'info';

interface WizardStep {
  title: string;
  summary: string;
}

interface StepResult {
  kind: ResultKind;
  message: string;
}

interface FleetForm {
  name: string;
  description: string;
}

interface VesselForm {
  name: string;
  repoUrl: string;
  defaultBranch: string;
  workingDirectory: string;
  projectContext: string;
  styleGuide: string;
  landingMode: string;
  enableModelContext: boolean;
  allowConcurrentMissions: boolean;
}

interface CaptainForm extends MuxCaptainFormFields {
  name: string;
  runtime: string;
  model: string;
  tier: string;
  systemInstructions: string;
}

interface DispatchForm {
  title: string;
  description: string;
  priority: number;
}

const steps: WizardStep[] = [
  { title: 'Objective', summary: 'Configure Armada to dispatch one safe first mission.' },
  { title: 'Fleet', summary: 'Create or choose the group that owns your repository.' },
  { title: 'Vessel', summary: 'Register the git repository that captains will work in.' },
  { title: 'Captain', summary: 'Create or choose an AI runtime so dispatch has capacity.' },
  { title: 'Dispatch', summary: 'Send a low-risk onboarding mission directly to Armada.' },
  { title: 'Handoff', summary: 'Refresh the mission and continue into onboarding, backlog, planning, and delivery setup.' },
];

const tooltips = {
  fleetSelect: 'Choose an existing fleet to group this setup vessel under.',
  fleetName: 'Name for the fleet that will organize one or more related repositories.',
  fleetDescription: 'Optional notes describing what repositories belong in this fleet.',
  vesselSelect: 'Choose the existing git repository Armada should dispatch the setup mission to.',
  vesselName: 'Display name for this repository inside Armada.',
  defaultBranch: 'Default branch Armada should branch from when creating mission worktrees.',
  repoUrl: 'Git clone URL or local repository path for the repository Armada will manage.',
  workingDirectory: 'Optional path to your local checkout, used for local landing and git status checks.',
  landingMode: 'Controls how completed mission work is landed. None keeps the work on a branch for you to review; Local Merge merges it into the working directory and pushes it to that checkout\'s origin remote.',
  enableModelContext: 'Allow captains to save useful repository knowledge back onto the vessel for future missions.',
  allowConcurrentMissions: 'Allow more than one mission to run on this vessel at the same time.',
  projectContext: 'Optional architecture, build, test, and dependency notes injected into captain prompts.',
  styleGuide: 'Optional coding conventions, naming rules, and library preferences for captains.',
  captainSelect: 'Choose an idle captain that is currently available for mission assignment.',
  captainName: 'Display name for the AI agent runtime registered with Armada.',
  runtime: 'AI agent runtime Armada should launch for missions assigned to this captain.',
  model: 'Optional runtime-specific model override. Leave blank to use the runtime default.',
  systemInstructions: 'Optional instructions injected into every mission handled by this captain.',
  muxConfigDirectory: 'Optional mux config directory override for loading saved endpoints.',
  muxEndpoint: 'Required named mux endpoint when the captain runtime is Mux.',
  missionTitle: 'Short title for the direct setup mission created by dispatch.',
  missionDescription: 'Full task instructions sent to the captain for this setup dispatch.',
  priority: 'Scheduling priority for the mission. Lower values are higher priority in Armada.',
};

const SETTLED_MISSION_STATUSES = new Set(['Complete', 'Failed', 'Cancelled', 'WorkProduced', 'LandingFailed', 'PullRequestOpen']);

const landingModeHints: Record<string, string> = {
  '': 'Uses the Admiral-wide landing settings.',
  None: 'Finished work stays on a branch for you to review. Choose Local Merge to land it automatically.',
  LocalMerge: 'Finished work is merged into the working directory and pushed to its origin remote, so the checkout needs one.',
  PullRequest: 'Finished work is pushed and opened as a pull request (needs the GitHub CLI).',
  MergeQueue: 'Finished work is queued; processing the merge queue tests and merges it.',
};

function upsertById<T extends { id: string }>(items: T[], item: T): T[] {
  const found = items.some((existing) => existing.id === item.id);
  if (found) return items.map((existing) => (existing.id === item.id ? item : existing));
  return [item, ...items];
}

function normalizeError(error: unknown): string {
  return error instanceof Error ? error.message : 'Request failed.';
}

function normalizeMissionResponse(value: unknown): { mission: Mission | null; warning?: string } {
  const maybeWrapped = value as { mission?: Mission; warning?: string } | null;
  if (maybeWrapped?.mission?.id) {
    return { mission: maybeWrapped.mission, warning: maybeWrapped.warning };
  }

  const maybeMission = value as Mission | null;
  return maybeMission?.id ? { mission: maybeMission } : { mission: null };
}

function idShort(id?: string | null): string {
  return id ? id.slice(0, 12) : '-';
}

function WizardExplanation({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className="wizard-explanation">
      <h3>{title}</h3>
      <div className="wizard-explanation-body">{children}</div>
    </div>
  );
}

function ResultPanel({ result }: { result: StepResult | null }) {
  if (!result) return null;
  return <div className={`wizard-result wizard-result-${result.kind}`}>{result.message}</div>;
}

function SummaryItem({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div className="wizard-summary-item">
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

export default function SetupWizard({ onClose, onHighlightChange }: SetupWizardProps) {
  const navigate = useNavigate();
  const { t } = useLocale();
  const [current, setCurrent] = useState(0);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<StepResult | null>(null);

  const [fleets, setFleets] = useState<Fleet[]>([]);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [captains, setCaptains] = useState<Captain[]>([]);

  const [fleetMode, setFleetMode] = useState<ResourceMode>('new');
  const [vesselMode, setVesselMode] = useState<ResourceMode>('new');
  const [captainMode, setCaptainMode] = useState<ResourceMode>('new');

  const [selectedFleetId, setSelectedFleetId] = useState('');
  const [selectedVesselId, setSelectedVesselId] = useState('');
  const [selectedCaptainId, setSelectedCaptainId] = useState('');

  const [fleetForm, setFleetForm] = useState<FleetForm>(() => ({
    name: t('Armada Starter Fleet'),
    description: t('Created from the setup wizard.'),
  }));
  const [vesselForm, setVesselForm] = useState<VesselForm>(() => ({
    name: '',
    repoUrl: '',
    defaultBranch: 'main',
    workingDirectory: '',
    projectContext: '',
    styleGuide: '',
    landingMode: 'None',
    enableModelContext: true,
    allowConcurrentMissions: false,
  }));
  const [captainForm, setCaptainForm] = useState<CaptainForm>(() => ({
    name: t('Setup Captain'),
    runtime: 'ClaudeCode',
    model: '',
    tier: 'Standard',
    systemInstructions: t('For setup missions, prefer read-only repository inspection unless the mission explicitly asks for code changes.'),
    ...EMPTY_MUX_CAPTAIN_FORM,
  }));
  const [dispatchForm, setDispatchForm] = useState<DispatchForm>(() => ({
    title: t('Repository onboarding survey'),
    description: t('Inspect this repository and report a concise onboarding summary. Do not modify files. Identify the project type, important directories, build/test commands, and one safe follow-up task.'),
    priority: 100,
  }));

  const [activeFleetId, setActiveFleetId] = useState('');
  const [activeVesselId, setActiveVesselId] = useState('');
  const [activeCaptainId, setActiveCaptainId] = useState('');
  const [dispatchedMission, setDispatchedMission] = useState<Mission | null>(null);
  const [dispatchWarning, setDispatchWarning] = useState('');
  const [readiness, setReadiness] = useState<VesselReadinessResult | null>(null);
  const [matchingProfiles, setMatchingProfiles] = useState<WorkflowProfile[]>([]);
  const [matchingEnvironments, setMatchingEnvironments] = useState<DeploymentEnvironment[]>([]);
  const [nextSetupLoading, setNextSetupLoading] = useState(false);

  const activeFleet = useMemo(
    () => fleets.find((fleet) => fleet.id === activeFleetId) ?? null,
    [fleets, activeFleetId],
  );
  const activeVessel = useMemo(
    () => vessels.find((vessel) => vessel.id === activeVesselId) ?? null,
    [vessels, activeVesselId],
  );
  const activeCaptain = useMemo(
    () => captains.find((captain) => captain.id === activeCaptainId) ?? null,
    [captains, activeCaptainId],
  );
  const idleCaptains = useMemo(
    () => captains.filter((captain) => String(captain.state || '').toLowerCase() === 'idle'),
    [captains],
  );

  const selectedFleet = useMemo(
    () => fleets.find((fleet) => fleet.id === selectedFleetId) ?? null,
    [fleets, selectedFleetId],
  );
  const selectedVessel = useMemo(
    () => vessels.find((vessel) => vessel.id === selectedVesselId) ?? null,
    [vessels, selectedVesselId],
  );
  const selectedCaptain = useMemo(
    () => idleCaptains.find((captain) => captain.id === selectedCaptainId) ?? null,
    [idleCaptains, selectedCaptainId],
  );
  const nextChecklistItem = useMemo(
    () => (readiness?.setupChecklist || []).find((item) => !item.isSatisfied) || null,
    [readiness],
  );

  const workflowProfileCount = matchingProfiles.length;
  const environmentCount = matchingEnvironments.length;
  const handoffFleetId = activeFleetId || activeVessel?.fleetId || '';
  const handoffBacklogPath = useMemo(() => {
    const params = new URLSearchParams();
    if (handoffFleetId) params.set('fleetId', handoffFleetId);
    if (activeVesselId) params.set('vesselId', activeVesselId);
    const search = params.toString();
    return search ? `/backlog?${search}` : '/backlog';
  }, [activeVesselId, handoffFleetId]);
  const handoffPlanningState = useMemo(() => {
    if (!activeVesselId) return null;
    return {
      fromSetupWizard: true,
      fleetId: handoffFleetId || undefined,
      vesselId: activeVesselId,
      title: t('Repository onboarding follow-up'),
      initialPrompt: t('Use the onboarding findings for this vessel to produce a safe first execution plan. Summarize the setup gaps, pick one small follow-up task, and outline how Armada should approach it.'),
    };
  }, [activeVesselId, handoffFleetId, t]);

  const loadResources = useCallback(async () => {
    try {
      setLoading(true);
      const [fleetResult, vesselResult, captainResult] = await Promise.all([
        listFleets({ pageSize: 9999 }),
        listVessels({ pageSize: 9999 }),
        listCaptains({ pageSize: 9999 }),
      ]);

      setFleets(fleetResult.objects);
      setVessels(vesselResult.objects);
      setCaptains(captainResult.objects);

      if (fleetResult.objects.length > 0) {
        setFleetMode('existing');
        setSelectedFleetId((currentId) => currentId || fleetResult.objects[0].id);
      }
      if (vesselResult.objects.length > 0) {
        setVesselMode('existing');
        setSelectedVesselId((currentId) => currentId || vesselResult.objects[0].id);
      }
      const idleCaptainOptions = captainResult.objects.filter((captain) => String(captain.state || '').toLowerCase() === 'idle');
      if (idleCaptainOptions.length > 0) {
        setCaptainMode('existing');
        setSelectedCaptainId((currentId) => currentId || idleCaptainOptions[0].id);
      }
    } catch (error) {
      setResult({ kind: 'error', message: t('Unable to load existing Armada resources: {{message}}', { message: normalizeError(error) }) });
    } finally {
      setLoading(false);
    }
  }, [t]);

  useEffect(() => {
    loadResources();
  }, [loadResources]);

  useEffect(() => {
    // Highlights map to consolidated nav destinations: Fleets/Workspace fold
    // into Vessels, Backlog into Dispatch, config pages into Configuration, and
    // Environments/Checks into Delivery.
    const highlightsByStep: Record<number, string[]> = {
      1: ['/vessels'],
      2: ['/vessels'],
      3: ['/captains'],
      4: ['/dispatch'],
      5: ['/vessels', '/dispatch', '/planning', '/configuration', '/delivery'],
    };

    onHighlightChange?.(highlightsByStep[current] || []);
    return () => onHighlightChange?.([]);
  }, [current, onHighlightChange]);

  const finish = useCallback(() => {
    markSetupComplete();
    onHighlightChange?.([]);
    onClose();
    // Exiting the wizard (finish, skip, or close) lands the operator on Missions.
    navigate('/missions');
  }, [navigate, onClose, onHighlightChange]);

  const finishAndNavigate = useCallback((to: string, options?: { state?: unknown; replace?: boolean }) => {
    markSetupComplete();
    onHighlightChange?.([]);
    onClose();
    navigate(to, options);
  }, [navigate, onClose, onHighlightChange]);

  const goTo = useCallback((index: number) => {
    setCurrent(index);
    setResult(null);
  }, []);

  useEffect(() => {
    if (current !== steps.length - 1 || !activeVesselId) return;

    let mounted = true;
    async function loadNextSetupState() {
      try {
        setNextSetupLoading(true);
        const [loadedReadiness, profileResult, environmentResult] = await Promise.all([
          getVesselReadiness(activeVesselId),
          listWorkflowProfiles({ pageSize: 9999 }),
          listEnvironments({ pageSize: 9999, vesselId: activeVesselId }),
        ]);

        if (!mounted) return;

        const relevantProfiles = (profileResult.objects || []).filter((profile) => {
          if (profile.scope === 'Global') return true;
          if (profile.scope === 'Fleet' && activeFleetId) return profile.fleetId === activeFleetId;
          if (profile.scope === 'Vessel') return profile.vesselId === activeVesselId;
          return false;
        });

        setReadiness(loadedReadiness);
        setMatchingProfiles(relevantProfiles);
        setMatchingEnvironments(environmentResult.objects || []);
      } catch {
        if (!mounted) return;
        setReadiness(null);
        setMatchingProfiles([]);
        setMatchingEnvironments([]);
      } finally {
        if (mounted) setNextSetupLoading(false);
      }
    }

    loadNextSetupState();
    return () => { mounted = false; };
  }, [activeFleetId, activeVesselId, current]);

  // On the handoff step, follow the dispatched mission until it settles so the operator sees it land
  // without pressing Refresh.
  const dispatchedMissionId = dispatchedMission?.id || '';
  const dispatchedMissionStatus = String(dispatchedMission?.status || '');
  useEffect(() => {
    if (current !== steps.length - 1 || !dispatchedMissionId) return;
    if (SETTLED_MISSION_STATUSES.has(dispatchedMissionStatus)) return;
    const timer = window.setInterval(async () => {
      try {
        const mission = await getMission(dispatchedMissionId);
        setDispatchedMission(mission);
      } catch {
        // transient; the next tick or the Refresh button retries
      }
    }, 5000);
    return () => window.clearInterval(timer);
  }, [current, dispatchedMissionId, dispatchedMissionStatus]);

  const handleFleetSubmit = async (event: FormEvent) => {
    event.preventDefault();
    setResult(null);

    if (fleetMode === 'existing') {
      if (!selectedFleet) {
        setResult({ kind: 'error', message: t('Choose a fleet before continuing.') });
        return;
      }
      setActiveFleetId(selectedFleet.id);
      setResult({ kind: 'success', message: t('Using {{entity}} "{{name}}".', { entity: t('Fleet').toLowerCase(), name: selectedFleet.name }) });
      goTo(2);
      return;
    }

    if (!fleetForm.name.trim()) {
      setResult({ kind: 'error', message: t('Fleet name is required.') });
      return;
    }

    try {
      setBusy(true);
      const fleet = await createFleet({
        name: fleetForm.name.trim(),
        description: fleetForm.description.trim() || null,
      });
      setFleets((items) => upsertById(items, fleet));
      setSelectedFleetId(fleet.id);
      setActiveFleetId(fleet.id);
      setFleetMode('existing');
      setResult({ kind: 'success', message: t('Created {{entity}} "{{name}}".', { entity: t('Fleet').toLowerCase(), name: fleet.name }) });
      goTo(2);
    } catch (error) {
      setResult({ kind: 'error', message: t('{{entity}} creation failed: {{message}}', { entity: t('Fleet'), message: normalizeError(error) }) });
    } finally {
      setBusy(false);
    }
  };

  const handleVesselSubmit = async (event: FormEvent) => {
    event.preventDefault();
    setResult(null);

    if (vesselMode === 'existing') {
      if (!selectedVessel) {
        setResult({ kind: 'error', message: t('Choose a vessel before continuing.') });
        return;
      }
      setActiveVesselId(selectedVessel.id);
      if (selectedVessel.fleetId) setActiveFleetId(selectedVessel.fleetId);
      setResult({ kind: 'success', message: t('Using {{entity}} "{{name}}".', { entity: t('Vessel').toLowerCase(), name: selectedVessel.name }) });
      goTo(3);
      return;
    }

    if (!vesselForm.name.trim()) {
      setResult({ kind: 'error', message: t('Vessel name is required.') });
      return;
    }
    if (!vesselForm.repoUrl.trim()) {
      setResult({ kind: 'error', message: t('Repository URL is required.') });
      return;
    }
    if (vesselForm.landingMode === 'LocalMerge' && !vesselForm.workingDirectory.trim()) {
      setResult({ kind: 'error', message: t('Local Merge needs a working directory to merge into.') });
      return;
    }

    try {
      setBusy(true);
      const payload: Partial<Vessel> = {
        name: vesselForm.name.trim(),
        repoUrl: vesselForm.repoUrl.trim(),
        defaultBranch: vesselForm.defaultBranch.trim() || 'main',
        enableModelContext: vesselForm.enableModelContext,
        allowConcurrentMissions: vesselForm.allowConcurrentMissions,
      };
      if (activeFleetId) payload.fleetId = activeFleetId;
      if (vesselForm.workingDirectory.trim()) payload.workingDirectory = vesselForm.workingDirectory.trim();
      if (vesselForm.projectContext.trim()) payload.projectContext = vesselForm.projectContext.trim();
      if (vesselForm.styleGuide.trim()) payload.styleGuide = vesselForm.styleGuide.trim();
      if (vesselForm.landingMode) payload.landingMode = vesselForm.landingMode;

      const vessel = await createVessel(payload);
      setVessels((items) => upsertById(items, vessel));
      setSelectedVesselId(vessel.id);
      setActiveVesselId(vessel.id);
      setVesselMode('existing');
      setResult({ kind: 'success', message: t('Registered vessel "{{name}}".', { name: vessel.name }) });
      goTo(3);
    } catch (error) {
      setResult({ kind: 'error', message: t('Vessel registration failed: {{message}}', { message: normalizeError(error) }) });
    } finally {
      setBusy(false);
    }
  };

  const handleCaptainSubmit = async (event: FormEvent) => {
    event.preventDefault();
    setResult(null);

    if (captainMode === 'existing') {
      if (!selectedCaptain) {
        setResult({ kind: 'error', message: t('Choose a captain before continuing.') });
        return;
      }
      setActiveCaptainId(selectedCaptain.id);
      setResult({ kind: 'success', message: t('Using {{entity}} "{{name}}".', { entity: t('Captain').toLowerCase(), name: selectedCaptain.name }) });
      goTo(4);
      return;
    }

    if (!captainForm.name.trim()) {
      setResult({ kind: 'error', message: t('Captain name is required.') });
      return;
    }
    if (!captainForm.runtime) {
      setResult({ kind: 'error', message: t('Choose a captain runtime.') });
      return;
    }
    if (isMuxRuntime(captainForm.runtime) && !captainForm.muxEndpoint.trim()) {
      setResult({ kind: 'error', message: t('Mux captains require a named Mux endpoint.') });
      return;
    }

    try {
      setBusy(true);
      const captain = await createCaptain({
        name: captainForm.name.trim(),
        runtime: captainForm.runtime,
        model: captainForm.model.trim() || null,
        tier: captainForm.tier || null,
        systemInstructions: captainForm.systemInstructions.trim() || null,
        runtimeOptionsJson: buildMuxRuntimeOptionsJson(captainForm.runtime, captainForm),
      });
      setCaptains((items) => upsertById(items, captain));
      setSelectedCaptainId(captain.id);
      setActiveCaptainId(captain.id);
      setCaptainMode('existing');
      setResult({ kind: 'success', message: t('Created {{entity}} "{{name}}".', { entity: t('Captain').toLowerCase(), name: captain.name }) });
      goTo(4);
    } catch (error) {
      setResult({ kind: 'error', message: t('{{entity}} creation failed: {{message}}', { entity: t('Captain'), message: normalizeError(error) }) });
    } finally {
      setBusy(false);
    }
  };

  const handleDispatchSubmit = async (event: FormEvent) => {
    event.preventDefault();
    setResult(null);
    setDispatchWarning('');

    if (!activeVessel) {
      setResult({ kind: 'error', message: t('Choose or create a vessel before dispatching.') });
      return;
    }
    if (!activeCaptain) {
      setResult({ kind: 'error', message: t('Choose or create a captain before dispatching.') });
      return;
    }
    if (!dispatchForm.title.trim() || !dispatchForm.description.trim()) {
      setResult({ kind: 'error', message: t('Mission title and description are required.') });
      return;
    }

    try {
      setBusy(true);
      const response = await dispatchMission({
        vesselId: activeVessel.id,
        title: dispatchForm.title.trim(),
        description: dispatchForm.description.trim(),
        priority: dispatchForm.priority,
      });
      const normalized = normalizeMissionResponse(response);
      if (!normalized.mission) {
        setResult({ kind: 'error', message: t('Dispatch succeeded but no mission was returned.') });
        return;
      }

      setDispatchedMission(normalized.mission);
      setDispatchWarning(normalized.warning || '');
      setResult({ kind: normalized.warning ? 'info' : 'success', message: normalized.warning || t('Dispatched mission "{{title}}".', { title: normalized.mission.title }) });
      goTo(5);
    } catch (error) {
      setResult({ kind: 'error', message: t('Dispatch failed: {{message}}', { message: normalizeError(error) }) });
    } finally {
      setBusy(false);
    }
  };

  const refreshMission = async () => {
    if (!dispatchedMission) return;
    try {
      setBusy(true);
      const mission = await getMission(dispatchedMission.id);
      setDispatchedMission(mission);
      setResult({ kind: 'success', message: t('Mission status refreshed: {{status}}.', { status: t(mission.status) }) });
    } catch (error) {
      setResult({ kind: 'error', message: t('Mission refresh failed: {{message}}', { message: normalizeError(error) }) });
    } finally {
      setBusy(false);
    }
  };

  const canAdvance = (() => {
    if (current === 0) return !loading;
    if (current === 1) return Boolean(activeFleet);
    if (current === 2) return Boolean(activeVessel);
    if (current === 3) return Boolean(activeCaptain);
    if (current === 4) return Boolean(dispatchedMission);
    return true;
  })();

  const renderWelcome = () => (
    <>
      <h2 className="wizard-step-heading">{t('Set up Armada by dispatching one first mission')}</h2>
      <WizardExplanation title={t('What this wizard will do')}>
        <div className="wizard-start-list">
          <div>
            <strong>{t('Pick a fleet')}</strong>
            <span>{t('Create or reuse the repository group Armada should organize work under.')}</span>
          </div>
          <div>
            <strong>{t('Register a vessel')}</strong>
            <span>{t('Provide the target git repository and optional project context, without leaving the wizard.')}</span>
          </div>
          <div>
            <strong>{t('Prepare captain capacity')}</strong>
            <span>{t('Create or reuse an AI runtime so Armada has a captain available for assignment.')}</span>
          </div>
          <div>
            <strong>{t('Dispatch directly')}</strong>
            <span>{t('Send a read-only onboarding mission through the dispatch endpoint and monitor the returned mission.')}</span>
          </div>
          <div>
            <strong>{t('Hand off into onboarding')}</strong>
            <span>{t('From there, continue in Vessel Onboarding, Backlog, Planning, Workspace, Workflow Profiles, Environments, and Checks.')}</span>
          </div>
        </div>
      </WizardExplanation>
      <p className="wizard-text">
        {t('The default mission is intentionally low-risk: it asks the captain to inspect and summarize the repository without modifying files. The wizard stops once Armada can dispatch safely, then hands you into the richer onboarding and delivery surfaces.')}
      </p>
    </>
  );

  const renderFleetStep = () => (
    <form onSubmit={handleFleetSubmit} className="wizard-step-form">
      <h2 className="wizard-step-heading">{t('Choose the fleet for this setup')}</h2>
      <p className="wizard-text">
        {t('Fleets group related repositories. Use an existing fleet if Armada is already configured, or create a starter fleet now.')}
      </p>

      <div className="wizard-mode-toggle">
        <button type="button" className={`btn btn-sm${fleetMode === 'existing' ? ' btn-primary' : ''}`} disabled={fleets.length === 0} onClick={() => setFleetMode('existing')}>
          {t('Use Existing')}
        </button>
        <button type="button" className={`btn btn-sm${fleetMode === 'new' ? ' btn-primary' : ''}`} onClick={() => setFleetMode('new')}>
          {t('Create New')}
        </button>
      </div>

      {fleetMode === 'existing' ? (
        <div className="form-group">
          <label title={t(tooltips.fleetSelect)}>{t('Fleet')}</label>
          <select aria-label={t(tooltips.fleetSelect)}
            title={t(tooltips.fleetSelect)}
            value={selectedFleetId}
            onChange={(event) => setSelectedFleetId(event.target.value)}
            disabled={fleets.length === 0}
          >
            {fleets.length === 0 && <option value="">{t('No fleets found')}</option>}
            {fleets.map((fleet) => (
              <option key={fleet.id} value={fleet.id}>
                {fleet.name}
              </option>
            ))}
          </select>
        </div>
      ) : (
        <div className="wizard-form-grid">
          <div className="form-group">
            <label title={t(tooltips.fleetName)}>{t('Fleet Name')}</label>
            <input aria-label={t(tooltips.fleetName)}
              title={t(tooltips.fleetName)}
              value={fleetForm.name}
              onChange={(event) => setFleetForm({ ...fleetForm, name: event.target.value })}
              required
            />
          </div>
          <div className="form-group">
            <label title={t(tooltips.fleetDescription)}>{t('Description')}</label>
            <input aria-label={t(tooltips.fleetDescription)}
              title={t(tooltips.fleetDescription)}
              value={fleetForm.description}
              onChange={(event) => setFleetForm({ ...fleetForm, description: event.target.value })}
            />
          </div>
        </div>
      )}

      <div className="wizard-inline-actions">
        <button type="submit" className="btn btn-primary" disabled={busy || loading}>
          {busy ? t('Saving...') : fleetMode === 'existing' ? t('Use Fleet') : t('Create Fleet')}
        </button>
      </div>
    </form>
  );

  const renderVesselStep = () => (
    <form onSubmit={handleVesselSubmit} className="wizard-step-form">
      <h2 className="wizard-step-heading">{t('Register the vessel Armada will dispatch to')}</h2>
      <p className="wizard-text">
        {t('A vessel is a git repository. The setup mission will run against the vessel you choose here.')}
      </p>

      <div className="wizard-context-strip">
        <span>{t('Fleet')}</span>
        <strong>{activeFleet?.name ?? t('No fleet selected')}</strong>
      </div>

      <div className="wizard-mode-toggle">
        <button type="button" className={`btn btn-sm${vesselMode === 'existing' ? ' btn-primary' : ''}`} disabled={vessels.length === 0} onClick={() => setVesselMode('existing')}>
          {t('Use Existing')}
        </button>
        <button type="button" className={`btn btn-sm${vesselMode === 'new' ? ' btn-primary' : ''}`} onClick={() => setVesselMode('new')}>
          {t('Create New')}
        </button>
      </div>

      {vesselMode === 'existing' ? (
        <div className="form-group">
          <label title={t(tooltips.vesselSelect)}>{t('Vessel')}</label>
          <select aria-label={t(tooltips.vesselSelect)}
            title={t(tooltips.vesselSelect)}
            value={selectedVesselId}
            onChange={(event) => setSelectedVesselId(event.target.value)}
            disabled={vessels.length === 0}
          >
            {vessels.length === 0 && <option value="">{t('No vessels found')}</option>}
            {vessels.map((vessel) => (
              <option key={vessel.id} value={vessel.id}>
                {vessel.name} ({vessel.defaultBranch || 'main'})
              </option>
            ))}
          </select>
        </div>
      ) : (
        <>
          <div className="wizard-form-grid">
            <div className="form-group">
              <label title={t(tooltips.vesselName)}>{t('Vessel Name')}</label>
              <input aria-label={t(tooltips.vesselName)}
                title={t(tooltips.vesselName)}
                value={vesselForm.name}
                onChange={(event) => setVesselForm({ ...vesselForm, name: event.target.value })}
                required
                placeholder={t('e.g., Armada')}
              />
            </div>
            <div className="form-group">
              <label title={t(tooltips.defaultBranch)}>{t('Default Branch')}</label>
              <input aria-label={t(tooltips.defaultBranch)}
                title={t(tooltips.defaultBranch)}
                value={vesselForm.defaultBranch}
                onChange={(event) => setVesselForm({ ...vesselForm, defaultBranch: event.target.value })}
                placeholder={t('main')}
              />
            </div>
          </div>
          <div className="form-group">
            <label title={t(tooltips.repoUrl)}>{t('Repository URL')}</label>
            <input aria-label={t(tooltips.repoUrl)}
              title={t(tooltips.repoUrl)}
              value={vesselForm.repoUrl}
              onChange={(event) => setVesselForm({ ...vesselForm, repoUrl: event.target.value })}
              required
              placeholder={t('https://github.com/org/repo.git or /path/to/repo')}
            />
          </div>
          <div className="wizard-form-grid">
            <div className="form-group">
              <label title={t(tooltips.workingDirectory)}>{t('Working Directory')}</label>
              <input aria-label={t(tooltips.workingDirectory)}
                title={t(tooltips.workingDirectory)}
                value={vesselForm.workingDirectory}
                onChange={(event) => setVesselForm({ ...vesselForm, workingDirectory: event.target.value })}
                placeholder={t('Optional local checkout path')}
              />
            </div>
            <div className="form-group">
              <label title={t(tooltips.landingMode)}>{t('Landing Mode')}</label>
              <select aria-label={t(tooltips.landingMode)}
                title={t(tooltips.landingMode)}
                value={vesselForm.landingMode}
                onChange={(event) => setVesselForm({ ...vesselForm, landingMode: event.target.value })}
              >
                <option value="">{t('Default')}</option>
                <option value="None">{t('None (safest for setup)')}</option>
                <option value="LocalMerge">{t('Local Merge')}</option>
                <option value="PullRequest">{t('Pull Request')}</option>
                <option value="MergeQueue">{t('Merge Queue')}</option>
              </select>
              <small className="text-dim">{t(landingModeHints[vesselForm.landingMode] || landingModeHints[''])}</small>
            </div>
          </div>
          <div className="wizard-form-grid">
            <label className="wizard-checkbox" title={t(tooltips.enableModelContext)}>
              <input
                title={t(tooltips.enableModelContext)}
                type="checkbox"
                checked={vesselForm.enableModelContext}
                onChange={(event) => setVesselForm({ ...vesselForm, enableModelContext: event.target.checked })}
              />
              {t('Enable model context accumulation')}
            </label>
            <label className="wizard-checkbox" title={t(tooltips.allowConcurrentMissions)}>
              <input
                title={t(tooltips.allowConcurrentMissions)}
                type="checkbox"
                checked={vesselForm.allowConcurrentMissions}
                onChange={(event) => setVesselForm({ ...vesselForm, allowConcurrentMissions: event.target.checked })}
              />
              {t('Allow concurrent missions on this vessel')}
            </label>
          </div>
          <div className="wizard-form-grid">
            <div className="form-group">
              <label title={t(tooltips.projectContext)}>{t('Project Context')}</label>
              <textarea aria-label={t(tooltips.projectContext)}
                title={t(tooltips.projectContext)}
                rows={4}
                value={vesselForm.projectContext}
                onChange={(event) => setVesselForm({ ...vesselForm, projectContext: event.target.value })}
                placeholder={t('Optional architecture, build, or repository notes for captains.')}
              />
            </div>
            <div className="form-group">
              <label title={t(tooltips.styleGuide)}>{t('Style Guide')}</label>
              <textarea aria-label={t(tooltips.styleGuide)}
                title={t(tooltips.styleGuide)}
                rows={4}
                value={vesselForm.styleGuide}
                onChange={(event) => setVesselForm({ ...vesselForm, styleGuide: event.target.value })}
                placeholder={t('Optional conventions captains should follow.')}
              />
            </div>
          </div>
        </>
      )}

      <div className="wizard-inline-actions">
        <button type="submit" className="btn btn-primary" disabled={busy || loading}>
          {busy ? t('Saving...') : vesselMode === 'existing' ? t('Use Vessel') : t('Register Vessel')}
        </button>
      </div>
    </form>
  );

  const renderCaptainStep = () => (
    <form onSubmit={handleCaptainSubmit} className="wizard-step-form">
      <h2 className="wizard-step-heading">{t('Prepare a captain for dispatch')}</h2>
      <p className="wizard-text">
        {t('A captain is an AI runtime registered with Armada. Direct dispatch assigns work to an available captain, so this step ensures the pool has capacity.')}
      </p>

      <div className="wizard-context-strip">
        <span>{t('Vessel')}</span>
        <strong>{activeVessel?.name ?? t('No vessel selected')}</strong>
      </div>

      <div className="wizard-mode-toggle">
        <button type="button" className={`btn btn-sm${captainMode === 'existing' ? ' btn-primary' : ''}`} disabled={idleCaptains.length === 0} onClick={() => setCaptainMode('existing')}>
          {t('Use Existing Idle')}
        </button>
        <button type="button" className={`btn btn-sm${captainMode === 'new' ? ' btn-primary' : ''}`} onClick={() => setCaptainMode('new')}>
          {t('Create New')}
        </button>
      </div>

      {captainMode === 'existing' ? (
        <div className="form-group">
          <label title={t(tooltips.captainSelect)}>{t('Captain')}</label>
          <select aria-label={t(tooltips.captainSelect)}
            title={t(tooltips.captainSelect)}
            value={selectedCaptainId}
            onChange={(event) => setSelectedCaptainId(event.target.value)}
            disabled={idleCaptains.length === 0}
          >
            {idleCaptains.length === 0 && <option value="">{t('No idle captains found')}</option>}
            {idleCaptains.map((captain) => (
              <option key={captain.id} value={captain.id}>
                {captain.name} ({captain.runtime}, {captain.state})
              </option>
            ))}
          </select>
        </div>
      ) : (
        <>
          <div className="wizard-form-grid">
            <div className="form-group">
              <label title={t(tooltips.captainName)}>{t('Captain Name')}</label>
              <input aria-label={t(tooltips.captainName)}
                title={t(tooltips.captainName)}
                value={captainForm.name}
                onChange={(event) => setCaptainForm({ ...captainForm, name: event.target.value })}
                required
              />
            </div>
            <div className="form-group">
              <label title={t(tooltips.runtime)}>{t('Runtime')}</label>
              <select aria-label={t(tooltips.runtime)}
                title={t(tooltips.runtime)}
                value={captainForm.runtime}
                onChange={(event) => setCaptainForm({ ...captainForm, runtime: event.target.value })}
                required
              >
                <option value="">{t('Select runtime...')}</option>
                <option value="ClaudeCode">Claude Code</option>
                <option value="Codex">Codex</option>
                <option value="Gemini">Gemini</option>
                <option value="Cursor">Cursor</option>
                <option value="Mux">Mux</option>
              </select>
            </div>
          </div>
          <div className="form-group">
            <label title={t(tooltips.model)}>{t('Model')}</label>
            <input aria-label={t(tooltips.model)}
              title={t(tooltips.model)}
              value={captainForm.model}
              onChange={(event) => setCaptainForm({ ...captainForm, model: event.target.value })}
              placeholder={t('Optional runtime-specific model override')}
            />
          </div>
          <div className="form-group">
            <label title={t('Capability tier for routing. Cheaper tiers handle routine work; stronger tiers handle complex work and serve as fallback when a preferred captain is busy.')}>{t('Capability Tier')}</label>
            <select aria-label={t('Capability Tier')}
              value={captainForm.tier}
              onChange={(event) => setCaptainForm({ ...captainForm, tier: event.target.value })}
            >
              <option value="">{t('Not set')}</option>
              <option value="Economy">{t('Economy')}</option>
              <option value="Standard">{t('Standard')}</option>
              <option value="Premium">{t('Premium')}</option>
            </select>
          </div>
          <div className="form-group">
            <label title={t(tooltips.systemInstructions)}>{t('System Instructions')}</label>
            <textarea aria-label={t(tooltips.systemInstructions)}
              title={t(tooltips.systemInstructions)}
              rows={4}
              value={captainForm.systemInstructions}
              onChange={(event) => setCaptainForm({ ...captainForm, systemInstructions: event.target.value })}
            />
          </div>
          <MuxRuntimeFields
            runtime={captainForm.runtime}
            form={captainForm}
            onChange={(patch) => setCaptainForm((current) => ({ ...current, ...patch }))}
            t={t}
            compact
          />
        </>
      )}

      <div className="wizard-inline-actions">
        <button type="submit" className="btn btn-primary" disabled={busy || loading}>
          {busy ? t('Saving...') : captainMode === 'existing' ? t('Use Captain') : t('Create Captain')}
        </button>
      </div>
    </form>
  );

  const renderDispatchStep = () => (
    <form onSubmit={handleDispatchSubmit} className="wizard-step-form">
      <h2 className="wizard-step-heading">{t('Dispatch the first mission')}</h2>
      <p className="wizard-text">
        {t('This uses Armada\'s direct mission dispatch path. It does not create a voyage from the setup wizard.')}
      </p>

      <div className="wizard-summary-grid">
        <SummaryItem label={t('Fleet')} value={activeFleet?.name ?? '-'} />
        <SummaryItem label={t('Vessel')} value={activeVessel?.name ?? '-'} />
        <SummaryItem label={t('Available Captain')} value={activeCaptain?.name ?? '-'} />
      </div>

      <div className="form-group">
        <label title={t(tooltips.missionTitle)}>{t('Mission Title')}</label>
        <input aria-label={t(tooltips.missionTitle)}
          title={t(tooltips.missionTitle)}
          value={dispatchForm.title}
          onChange={(event) => setDispatchForm({ ...dispatchForm, title: event.target.value })}
          required
        />
      </div>
      <div className="form-group">
        <label title={t(tooltips.missionDescription)}>{t('Mission Description')}</label>
        <textarea aria-label={t(tooltips.missionDescription)}
          title={t(tooltips.missionDescription)}
          rows={7}
          value={dispatchForm.description}
          onChange={(event) => setDispatchForm({ ...dispatchForm, description: event.target.value })}
          required
        />
      </div>
      <div className="form-group">
        <label title={t(tooltips.priority)}>{t('Priority')}</label>
        <input aria-label={t(tooltips.priority)}
          title={t(tooltips.priority)}
          type="number"
          value={dispatchForm.priority}
          min={0}
          max={1000}
          onChange={(event) => {
            const next = parseInt(event.target.value, 10);
            setDispatchForm({ ...dispatchForm, priority: Number.isNaN(next) ? 100 : next });
          }}
        />
      </div>

      <div className="wizard-inline-actions">
        <button type="submit" className="btn btn-primary" disabled={busy || !activeVessel || !activeCaptain}>
          {busy ? t('Dispatching...') : t('Dispatch Mission')}
        </button>
      </div>
    </form>
  );

  const renderMonitorStep = () => (
    <>
      <h2 className="wizard-step-heading">{t('Mission dispatched, handoff ready')}</h2>
      <p className="wizard-text">
        {t('Armada can dispatch safely now. Use the handoff actions below to move this vessel into onboarding, backlog, planning, workspace, workflow-profile, environment, and first-check setup.')}
      </p>

      {dispatchWarning && <div className="wizard-result wizard-result-info">{dispatchWarning}</div>}

      {dispatchedMission ? (
        <div className="wizard-summary-grid wizard-summary-grid-wide">
          <SummaryItem label={t('Mission')} value={dispatchedMission.title} />
          <SummaryItem label={t('Mission ID')} value={<span style={{ display: 'inline-flex', alignItems: 'center', gap: '0.35rem' }}><code>{dispatchedMission.id}</code><CopyButton text={dispatchedMission.id} /></span>} />
          <SummaryItem label={t('Status')} value={t(dispatchedMission.status)} />
          <SummaryItem label={t('Captain ID')} value={<code>{idShort(dispatchedMission.captainId)}</code>} />
          <SummaryItem label={t('Vessel ID')} value={<code>{idShort(dispatchedMission.vesselId)}</code>} />
          <SummaryItem label={t('Branch')} value={dispatchedMission.branchName || '-'} />
        </div>
      ) : (
        <p className="text-dim">{t('No mission has been dispatched yet.')}</p>
      )}

      {dispatchedMission && FAILED_MISSION_STATUSES.has(String(dispatchedMission.status)) && (
        <MissionFailureDetails
          mission={dispatchedMission}
          onOpenMission={(missionId) => finishAndNavigate(`/missions/${missionId}`)}
        />
      )}

      <div className="wizard-summary-grid" style={{ marginTop: '1rem' }}>
        <SummaryItem label={t('Readiness')} value={nextSetupLoading ? t('Loading...') : readiness ? `${readiness.setupChecklistSatisfiedCount}/${readiness.setupChecklistTotalCount}` : '-'} />
        <SummaryItem label={t('Blocking Issues')} value={nextSetupLoading ? t('Loading...') : readiness ? readiness.errorCount : '-'} />
        <SummaryItem label={t('Workflow Profiles')} value={nextSetupLoading ? t('Loading...') : workflowProfileCount} />
        <SummaryItem label={t('Environments')} value={nextSetupLoading ? t('Loading...') : environmentCount} />
      </div>

      {nextChecklistItem && (
        <div className="wizard-explanation" style={{ marginTop: '1rem' }}>
          <h3>{t('Next Recommended Step')}</h3>
          <div className="wizard-explanation-body">
            <strong>{nextChecklistItem.title}</strong>
            <div className="text-dim" style={{ marginTop: '0.35rem' }}>{nextChecklistItem.message}</div>
          </div>
        </div>
      )}

      <div className="wizard-start-list" style={{ marginTop: '1rem' }}>
        <div>
          <strong>{t('Vessel Onboarding')}</strong>
          <span>{t('Review the readiness checklist, fix blocking issues, and follow the recommended onboarding actions for this vessel.')}</span>
        </div>
        <div>
          <strong>{t('Backlog')}</strong>
          <span>{t('Capture the follow-up work uncovered by the onboarding mission and keep it attached to this vessel.')}</span>
        </div>
        <div>
          <strong>{t('Planning')}</strong>
          <span>{t('Turn one of those follow-up tasks into a captain-backed execution plan before you dispatch code-changing work.')}</span>
        </div>
        <div>
          <strong>{t('Workspace')}</strong>
          <span>{t('Inspect the repository directly, review the onboarding findings, and continue setup or debugging without leaving Armada.')}</span>
        </div>
        <div>
          <strong>{t('Workflow Profiles')}</strong>
          <span>{t('Teach Armada how this repository builds, tests, deploys, rolls back, and verifies itself.')}</span>
        </div>
        <div>
          <strong>{t('Environments')}</strong>
          <span>{t('Capture at least one named rollout target with approval, verification, and monitoring metadata.')}</span>
        </div>
        <div>
          <strong>{t('Checks')}</strong>
          <span>{t('Run the first structured check once readiness and profile setup are in place.')}</span>
        </div>
        <div>
          <strong>{t('Playbooks')}</strong>
          <span>{t('Optional: capture reusable dispatch and planning guidance if your team wants consistent execution patterns.')}</span>
        </div>
      </div>

      <div className="wizard-inline-actions">
        <button type="button" className="btn" onClick={refreshMission} disabled={busy || !dispatchedMission}>
          {busy ? t('Refreshing...') : t('Refresh Mission Status')}
        </button>
        {dispatchedMission && (
          <button
            type="button"
            className="btn"
            onClick={() => finishAndNavigate(`/missions/${dispatchedMission.id}`)}
          >
            {t('Open Mission')}
          </button>
        )}
        {activeVessel && (
          <button
            type="button"
            className="btn"
            onClick={() => finishAndNavigate(`/vessels/${activeVessel.id}/onboarding`)}
          >
            {t('Open Vessel Onboarding')}
          </button>
        )}
        {activeVessel && (
          <button
            type="button"
            className="btn"
            onClick={() => finishAndNavigate(handoffBacklogPath)}
          >
            {t('Open Backlog')}
          </button>
        )}
        {activeVessel && handoffPlanningState && (
          <button
            type="button"
            className="btn"
            onClick={() => finishAndNavigate('/planning', { state: handoffPlanningState })}
          >
            {t('Open Planning')}
          </button>
        )}
        {activeVessel && (
          <button
            type="button"
            className="btn"
            onClick={() => finishAndNavigate(`/workspace/${activeVessel.id}`)}
          >
            {t('Open Workspace')}
          </button>
        )}
        {activeVessel && (
          <button
            type="button"
            className="btn"
            onClick={() => finishAndNavigate(
              workflowProfileCount > 0
                ? '/workflow-profiles'
                : `/workflow-profiles/new?scope=Vessel&vesselId=${encodeURIComponent(activeVessel.id)}`,
            )}
          >
            {workflowProfileCount > 0 ? t('Open Workflow Profiles') : t('Create Workflow Profile')}
          </button>
        )}
        {activeVessel && (
          <button
            type="button"
            className="btn"
            onClick={() => finishAndNavigate(
              environmentCount > 0
                ? '/environments'
                : `/environments/new?vesselId=${encodeURIComponent(activeVessel.id)}&kind=Development`,
            )}
          >
            {environmentCount > 0 ? t('Open Environments') : t('Create Environment')}
          </button>
        )}
        {activeVessel && (
          <button
            type="button"
            className="btn"
            onClick={() => finishAndNavigate('/checks', {
              state: {
                prefill: {
                  vesselId: activeVessel.id,
                  branchName: activeVessel.defaultBranch || '',
                },
              },
            })}
          >
            {t('Run First Check')}
          </button>
        )}
        <button
          type="button"
          className="btn"
          onClick={() => finishAndNavigate('/playbooks')}
        >
          {t('Open Playbooks')}
        </button>
        <button type="button" className="btn btn-primary" onClick={finish}>
          {t('Finish Setup')}
        </button>
      </div>
    </>
  );

  const content = (() => {
    if (current === 0) return renderWelcome();
    if (current === 1) return renderFleetStep();
    if (current === 2) return renderVesselStep();
    if (current === 3) return renderCaptainStep();
    if (current === 4) return renderDispatchStep();
    return renderMonitorStep();
  })();

  return (
    <div className="wizard-overlay">
      <div className="wizard-container wizard-container-objective">
        <button type="button" className="wizard-close" onClick={finish} title={t('Close setup wizard')} aria-label={t('Close setup wizard')}>
          &times;
        </button>

        <div className="wizard-body">
          <div className="wizard-header">
            <div>
              <div className="wizard-kicker">{t('Setup Wizard')}</div>
              <h1>{t('Launch Armada With One Mission')}</h1>
            </div>
            <div className="wizard-step-count">
              {t('Step {{current}} of {{total}}', { current: current + 1, total: steps.length })}
            </div>
          </div>

          <div className="wizard-progress" aria-label={t('Setup progress')}>
            {steps.map((step, index) => (
              <div key={step.title} className={`wizard-progress-step${index === current ? ' active' : ''}${index < current ? ' done' : ''}`}>
                <span>{index < current ? '\u2713' : index + 1}</span>
                <div>
                  <strong>{t(step.title)}</strong>
                  <small>{t(step.summary)}</small>
                </div>
              </div>
            ))}
          </div>

          <div className="wizard-content">
            {loading && current === 0 && <div className="wizard-result wizard-result-info">{t('Loading existing Armada resources...')}</div>}
            {content}
            <ResultPanel result={result} />
          </div>

          <div className="wizard-actions">
            <button className="btn" onClick={finish}>
              {t('Skip Setup')}
            </button>
            <div className="wizard-actions-right">
              {current > 0 && (
                <button className="btn" onClick={() => goTo(current - 1)}>
                  {t('Back')}
                </button>
              )}
              {current < steps.length - 1 && (
                <button className="btn btn-primary" onClick={() => goTo(current + 1)} disabled={!canAdvance}>
                  {current === 0 ? t('Start Setup') : t('Next')}
                </button>
              )}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
