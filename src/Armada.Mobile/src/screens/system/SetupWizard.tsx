import { Stack, useRouter, type Href } from 'expo-router';
import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import { ScrollView, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import {
  createCaptain,
  createFleet,
  createVessel,
  dispatchMission,
  getMission,
  getVesselReadiness,
  listCaptains,
  listEnvironments,
  listFleets,
  listMuxEndpoints,
  listVessels,
  listWorkflowProfiles,
} from '@dashboard/api/client';
import type { Captain, DeploymentEnvironment, Fleet, Mission, MuxEndpointInfo, Vessel, VesselReadinessResult, WorkflowProfile } from '@dashboard/types/models';
import { buildMuxRuntimeOptionsJson, EMPTY_MUX_CAPTAIN_FORM, isMuxRuntime, type MuxCaptainFormFields } from '@dashboard/lib/mux';
import { SETUP_LANDING_MODES, setupLandingModeHint, setupLandingWorkingDirectoryError } from '@dashboard/lib/setupLanding';
import { idShort, normalizeMissionResponse, relevantWorkflowProfiles, SETTLED_MISSION_STATUSES, SETUP_STEPS, SETUP_TOOLTIPS, setupBacklogPath, upsertById } from '@dashboard/lib/setupWizard';
import { Field, FieldCard } from '../../components/resource/DetailParts';
import { AppText } from '../../components/ui/AppText';
import { Banner } from '../../components/ui/Banner';
import { Button } from '../../components/ui/Button';
import { Disclosure } from '../../components/ui/Disclosure';
import { SegmentedControl } from '../../components/ui/SegmentedControl';
import { SelectField } from '../../components/ui/SelectSheet';
import { SwitchField } from '../../components/ui/SwitchField';
import { TextField } from '../../components/ui/TextField';
import { useLocale } from '../../i18n/LocaleContext';
import { prefillQuery } from '../../resource/links';
import { ALL } from '../../resource/lookups';
import { errorText } from '../../resource/useLoad';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing } from '../../theme/typography';

type ResourceMode = 'existing' | 'new';
type ResultKind = 'success' | 'error' | 'info';
interface StepResult { kind: ResultKind; message: string }

const FAILED_MISSION_STATUSES = new Set(['Failed', 'LandingFailed']);
const RUNTIMES = [
  { value: 'ClaudeCode', label: 'Claude Code' },
  { value: 'Codex', label: 'Codex' },
  { value: 'Gemini', label: 'Gemini' },
  { value: 'Cursor', label: 'Cursor' },
  { value: 'Mux', label: 'Mux' },
];
/** The handoff step polls the dispatched mission at the dashboard's interval until it settles. */
export const SETUP_MISSION_POLL_MS = 5000;

function ModeToggle({ value, onChange, existingLabel, existingDisabled, testID }: { value: ResourceMode; onChange: (m: ResourceMode) => void; existingLabel: string; existingDisabled: boolean; testID: string }) {
  const { t } = useLocale();
  const options = [
    ...(existingDisabled ? [] : [{ value: 'existing' as const, label: existingLabel, testID: `${testID}-existing` }]),
    { value: 'new' as const, label: t('Create New'), testID: `${testID}-new` },
  ];
  return <SegmentedControl label={t('Setup Wizard')} options={options} value={value} onChange={onChange} />;
}

function Context({ label, value }: { label: string; value: string }) {
  return <Field label={label} value={value} />;
}

/**
 * The setup wizard (the dashboard's SetupWizard dialog) as a screen: choose or create a fleet, a vessel, and an idle
 * captain, dispatch a read-only onboarding mission, then follow it and hand off into onboarding, backlog, planning,
 * workspace, workflow profiles, environments, checks, and playbooks. Skip and Finish open Missions.
 */
export function SetupWizard() {
  const { t } = useLocale();
  const router = useRouter();
  const { colors } = useTheme();
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

  const [fleetForm, setFleetForm] = useState(() => ({ name: t('Armada Starter Fleet'), description: t('Created from the setup wizard.') }));
  const [vesselForm, setVesselForm] = useState({
    name: '', repoUrl: '', defaultBranch: 'main', workingDirectory: '', projectContext: '', styleGuide: '', landingMode: 'None',
    enableModelContext: true, allowConcurrentMissions: false,
  });
  const [captainForm, setCaptainForm] = useState<{ name: string; runtime: string; model: string; tier: string; systemInstructions: string } & MuxCaptainFormFields>(() => ({
    name: t('Setup Captain'),
    runtime: 'ClaudeCode',
    model: '',
    tier: 'Standard',
    systemInstructions: t('For setup missions, prefer read-only repository inspection unless the mission explicitly asks for code changes.'),
    ...EMPTY_MUX_CAPTAIN_FORM,
  }));
  const [dispatchForm, setDispatchForm] = useState(() => ({
    title: t('Repository onboarding survey'),
    description: t('Inspect this repository and report a concise onboarding summary. Do not modify files. Identify the project type, important directories, build/test commands, and one safe follow-up task.'),
    priority: '100',
  }));

  const [activeFleetId, setActiveFleetId] = useState('');
  const [activeVesselId, setActiveVesselId] = useState('');
  const [activeCaptainId, setActiveCaptainId] = useState('');
  const [mission, setMission] = useState<Mission | null>(null);
  const [warning, setWarning] = useState('');
  const [readiness, setReadiness] = useState<VesselReadinessResult | null>(null);
  const [profiles, setProfiles] = useState<WorkflowProfile[]>([]);
  const [environments, setEnvironments] = useState<DeploymentEnvironment[]>([]);
  const [nextLoading, setNextLoading] = useState(false);
  const [muxEndpoints, setMuxEndpoints] = useState<MuxEndpointInfo[]>([]);
  const [muxHint, setMuxHint] = useState('');

  const activeFleet = fleets.find((f) => f.id === activeFleetId) ?? null;
  const activeVessel = vessels.find((v) => v.id === activeVesselId) ?? null;
  const activeCaptain = captains.find((c) => c.id === activeCaptainId) ?? null;
  const idleCaptains = useMemo(() => captains.filter((c) => String(c.state || '').toLowerCase() === 'idle'), [captains]);
  const handoffFleetId = activeFleetId || activeVessel?.fleetId || '';

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const [f, v, c] = await Promise.all([listFleets(ALL), listVessels(ALL), listCaptains(ALL)]);
        if (cancelled) return;
        setFleets(f.objects);
        setVessels(v.objects);
        setCaptains(c.objects);
        if (f.objects.length > 0) { setFleetMode('existing'); setSelectedFleetId((id) => id || f.objects[0].id); }
        if (v.objects.length > 0) { setVesselMode('existing'); setSelectedVesselId((id) => id || v.objects[0].id); }
        const idle = c.objects.filter((x) => String(x.state || '').toLowerCase() === 'idle');
        if (idle.length > 0) { setCaptainMode('existing'); setSelectedCaptainId((id) => id || idle[0].id); }
      } catch (e: unknown) {
        if (!cancelled) setResult({ kind: 'error', message: t('Unable to load existing Armada resources: {{message}}', { message: errorText(e, 'Request failed.') }) });
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [t]);

  // Mux captains: list the saved endpoints of the config directory.
  const muxDir = captainForm.muxConfigDirectory.trim();
  const muxRuntime = isMuxRuntime(captainForm.runtime);
  const loadMux = useCallback(async () => {
    try {
      setMuxHint(t('Loading saved Mux endpoints...'));
      const r = await listMuxEndpoints(muxDir || undefined);
      if (!r.success) throw new Error(r.errorMessage || r.errorCode || t('Mux endpoint discovery failed.'));
      const list = r.endpoints ?? [];
      setMuxEndpoints(list);
      setMuxHint(list.length === 0 ? t('No saved Mux endpoints were found for this config directory.') : t('{{count}} saved Mux endpoint(s) available.', { count: list.length }));
    } catch (e: unknown) {
      setMuxEndpoints([]);
      setMuxHint(errorText(e, t('Mux endpoint discovery failed.')));
    }
  }, [muxDir, t]);
  // Discovering endpoints (and showing that it is loading) is this effect's purpose.
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { if (muxRuntime) void loadMux(); }, [muxRuntime, loadMux]);

  // Handoff: readiness, matching workflow profiles, and environments of the setup vessel.
  useEffect(() => {
    if (current !== SETUP_STEPS.length - 1 || !activeVesselId) return undefined;
    let mounted = true;
    (async () => {
      try {
        setNextLoading(true);
        const [r, p, e] = await Promise.all([getVesselReadiness(activeVesselId), listWorkflowProfiles(ALL), listEnvironments({ pageSize: 9999, vesselId: activeVesselId })]);
        if (!mounted) return;
        setReadiness(r);
        setProfiles(relevantWorkflowProfiles(p.objects || [], activeFleetId, activeVesselId));
        setEnvironments(e.objects || []);
      } catch {
        if (!mounted) return;
        setReadiness(null);
        setProfiles([]);
        setEnvironments([]);
      } finally {
        if (mounted) setNextLoading(false);
      }
    })();
    return () => { mounted = false; };
  }, [current, activeVesselId, activeFleetId]);

  // Handoff: follow the dispatched mission until it settles.
  const missionId = mission?.id || '';
  const missionStatus = String(mission?.status || '');
  useEffect(() => {
    if (current !== SETUP_STEPS.length - 1 || !missionId || SETTLED_MISSION_STATUSES.has(missionStatus)) return undefined;
    const timer = setInterval(() => {
      getMission(missionId).then(setMission).catch(() => undefined);
    }, SETUP_MISSION_POLL_MS);
    return () => clearInterval(timer);
  }, [current, missionId, missionStatus]);

  const goTo = (index: number) => { setCurrent(index); setResult(null); };
  const finishAt = (path: string) => router.replace(path as Href);
  const finish = () => finishAt('/missions');

  async function submitFleet() {
    setResult(null);
    if (fleetMode === 'existing') {
      const f = fleets.find((x) => x.id === selectedFleetId);
      if (!f) { setResult({ kind: 'error', message: t('Choose a fleet before continuing.') }); return; }
      setActiveFleetId(f.id);
      setResult({ kind: 'success', message: t('Using {{entity}} "{{name}}".', { entity: t('Fleet').toLowerCase(), name: f.name }) });
      setCurrent(2);
      return;
    }
    if (!fleetForm.name.trim()) { setResult({ kind: 'error', message: t('Fleet name is required.') }); return; }
    try {
      setBusy(true);
      const fleet = await createFleet({ name: fleetForm.name.trim(), description: fleetForm.description.trim() || null });
      setFleets((items) => upsertById(items, fleet));
      setSelectedFleetId(fleet.id);
      setActiveFleetId(fleet.id);
      setFleetMode('existing');
      setResult({ kind: 'success', message: t('Created {{entity}} "{{name}}".', { entity: t('Fleet').toLowerCase(), name: fleet.name }) });
      setCurrent(2);
    } catch (e: unknown) {
      setResult({ kind: 'error', message: t('{{entity}} creation failed: {{message}}', { entity: t('Fleet'), message: errorText(e, 'Request failed.') }) });
    } finally {
      setBusy(false);
    }
  }

  async function submitVessel() {
    setResult(null);
    if (vesselMode === 'existing') {
      const v = vessels.find((x) => x.id === selectedVesselId);
      if (!v) { setResult({ kind: 'error', message: t('Choose a vessel before continuing.') }); return; }
      setActiveVesselId(v.id);
      if (v.fleetId) setActiveFleetId(v.fleetId);
      setResult({ kind: 'success', message: t('Using {{entity}} "{{name}}".', { entity: t('Vessel').toLowerCase(), name: v.name }) });
      setCurrent(3);
      return;
    }
    if (!vesselForm.name.trim()) { setResult({ kind: 'error', message: t('Vessel name is required.') }); return; }
    if (!vesselForm.repoUrl.trim()) { setResult({ kind: 'error', message: t('Repository URL is required.') }); return; }
    const landingError = setupLandingWorkingDirectoryError(vesselForm.landingMode, vesselForm.workingDirectory);
    if (landingError) { setResult({ kind: 'error', message: t(landingError) }); return; }
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
      setCurrent(3);
    } catch (e: unknown) {
      setResult({ kind: 'error', message: t('Vessel registration failed: {{message}}', { message: errorText(e, 'Request failed.') }) });
    } finally {
      setBusy(false);
    }
  }

  async function submitCaptain() {
    setResult(null);
    if (captainMode === 'existing') {
      const c = idleCaptains.find((x) => x.id === selectedCaptainId);
      if (!c) { setResult({ kind: 'error', message: t('Choose a captain before continuing.') }); return; }
      setActiveCaptainId(c.id);
      setResult({ kind: 'success', message: t('Using {{entity}} "{{name}}".', { entity: t('Captain').toLowerCase(), name: c.name }) });
      setCurrent(4);
      return;
    }
    if (!captainForm.name.trim()) { setResult({ kind: 'error', message: t('Captain name is required.') }); return; }
    if (!captainForm.runtime) { setResult({ kind: 'error', message: t('Choose a captain runtime.') }); return; }
    if (isMuxRuntime(captainForm.runtime) && !captainForm.muxEndpoint.trim()) { setResult({ kind: 'error', message: t('Mux captains require a named Mux endpoint.') }); return; }
    try {
      setBusy(true);
      const captain = await createCaptain({
        name: captainForm.name.trim(),
        runtime: captainForm.runtime,
        model: captainForm.model.trim() || null,
        tier: captainForm.tier || null,
        systemInstructions: captainForm.systemInstructions.trim() || null,
        runtimeOptionsJson: buildMuxRuntimeOptionsJson(captainForm.runtime, captainForm),
      } as Partial<Captain>);
      setCaptains((items) => upsertById(items, captain));
      setSelectedCaptainId(captain.id);
      setActiveCaptainId(captain.id);
      setCaptainMode('existing');
      setResult({ kind: 'success', message: t('Created {{entity}} "{{name}}".', { entity: t('Captain').toLowerCase(), name: captain.name }) });
      setCurrent(4);
    } catch (e: unknown) {
      setResult({ kind: 'error', message: t('{{entity}} creation failed: {{message}}', { entity: t('Captain'), message: errorText(e, 'Request failed.') }) });
    } finally {
      setBusy(false);
    }
  }

  async function submitDispatch() {
    setResult(null);
    setWarning('');
    if (!activeVessel) { setResult({ kind: 'error', message: t('Choose or create a vessel before dispatching.') }); return; }
    if (!activeCaptain) { setResult({ kind: 'error', message: t('Choose or create a captain before dispatching.') }); return; }
    if (!dispatchForm.title.trim() || !dispatchForm.description.trim()) { setResult({ kind: 'error', message: t('Mission title and description are required.') }); return; }
    try {
      setBusy(true);
      const parsed = parseInt(dispatchForm.priority, 10);
      const response = await dispatchMission({ vesselId: activeVessel.id, title: dispatchForm.title.trim(), description: dispatchForm.description.trim(), priority: Number.isNaN(parsed) ? 100 : parsed });
      const normalized = normalizeMissionResponse(response);
      if (!normalized.mission) { setResult({ kind: 'error', message: t('Dispatch succeeded but no mission was returned.') }); return; }
      setMission(normalized.mission);
      setWarning(normalized.warning || '');
      setResult({ kind: normalized.warning ? 'info' : 'success', message: normalized.warning || t('Dispatched mission "{{title}}".', { title: normalized.mission.title }) });
      setCurrent(5);
    } catch (e: unknown) {
      setResult({ kind: 'error', message: t('Dispatch failed: {{message}}', { message: errorText(e, 'Request failed.') }) });
    } finally {
      setBusy(false);
    }
  }

  async function refreshMission() {
    if (!mission) return;
    try {
      setBusy(true);
      const m = await getMission(mission.id);
      setMission(m);
      setResult({ kind: 'success', message: t('Mission status refreshed: {{status}}.', { status: t(m.status) }) });
    } catch (e: unknown) {
      setResult({ kind: 'error', message: t('Mission refresh failed: {{message}}', { message: errorText(e, 'Request failed.') }) });
    } finally {
      setBusy(false);
    }
  }

  const canAdvance = current === 0 ? !loading
    : current === 1 ? !!activeFleet
      : current === 2 ? !!activeVessel
        : current === 3 ? !!activeCaptain
          : current === 4 ? !!mission : true;

  const tip = (key: keyof typeof SETUP_TOOLTIPS) => t(SETUP_TOOLTIPS[key]);
  let content: ReactNode;
  if (current === 0) {
    content = (
      <>
        <AppText variant="heading" accessibilityRole="header">{t('Set up Armada by dispatching one first mission')}</AppText>
        <FieldCard title={t('What this wizard will do')}>
          <Field label={t('Pick a fleet')} value={t('Create or reuse the repository group Armada should organize work under.')} />
          <Field label={t('Register a vessel')} value={t('Provide the target git repository and optional project context, without leaving the wizard.')} />
          <Field label={t('Prepare captain capacity')} value={t('Create or reuse an AI runtime so Armada has a captain available for assignment.')} />
          <Field label={t('Dispatch directly')} value={t('Send a read-only onboarding mission through the dispatch endpoint and monitor the returned mission.')} />
          <Field label={t('Hand off into onboarding')} value={t('From there, continue in Vessel Onboarding, Backlog, Planning, Workspace, Workflow Profiles, Environments, and Checks.')} />
        </FieldCard>
        <AppText muted>{t('The default mission is intentionally low-risk: it asks the captain to inspect and summarize the repository without modifying files. The wizard stops once Armada can dispatch safely, then hands you into the richer onboarding and delivery surfaces.')}</AppText>
      </>
    );
  } else if (current === 1) {
    content = (
      <>
        <AppText variant="heading" accessibilityRole="header">{t('Choose the fleet for this setup')}</AppText>
        <AppText muted>{t('Fleets group related repositories. Use an existing fleet if Armada is already configured, or create a starter fleet now.')}</AppText>
        <ModeToggle value={fleetMode} onChange={setFleetMode} existingLabel={t('Use Existing')} existingDisabled={fleets.length === 0} testID="setup-fleet-mode" />
        {fleetMode === 'existing' ? (
          <SelectField label={t('Fleet')} hint={tip('fleetSelect')} value={selectedFleetId} options={fleets.map((f) => ({ value: f.id, label: f.name }))} placeholder={t('No fleets found')} onChange={setSelectedFleetId} closeLabel={t('Close')} testID="setup-fleet-select" />
        ) : (
          <>
            <TextField label={t('Fleet Name')} hint={tip('fleetName')} value={fleetForm.name} onChangeText={(name) => setFleetForm({ ...fleetForm, name })} testID="setup-fleet-name" />
            <TextField label={t('Description')} hint={tip('fleetDescription')} value={fleetForm.description} onChangeText={(description) => setFleetForm({ ...fleetForm, description })} />
          </>
        )}
        <Button label={busy ? t('Saving...') : fleetMode === 'existing' ? t('Use Fleet') : t('Create Fleet')} busy={busy} disabled={loading} onPress={() => void submitFleet()} testID="setup-fleet-submit" />
      </>
    );
  } else if (current === 2) {
    content = (
      <>
        <AppText variant="heading" accessibilityRole="header">{t('Register the vessel Armada will dispatch to')}</AppText>
        <AppText muted>{t('A vessel is a git repository. The setup mission will run against the vessel you choose here.')}</AppText>
        <Context label={t('Fleet')} value={activeFleet?.name ?? t('No fleet selected')} />
        <ModeToggle value={vesselMode} onChange={setVesselMode} existingLabel={t('Use Existing')} existingDisabled={vessels.length === 0} testID="setup-vessel-mode" />
        {vesselMode === 'existing' ? (
          <SelectField label={t('Vessel')} hint={tip('vesselSelect')} value={selectedVesselId} options={vessels.map((v) => ({ value: v.id, label: `${v.name} (${v.defaultBranch || 'main'})` }))} placeholder={t('No vessels found')} onChange={setSelectedVesselId} closeLabel={t('Close')} testID="setup-vessel-select" />
        ) : (
          <>
            <TextField label={t('Vessel Name')} hint={tip('vesselName')} value={vesselForm.name} onChangeText={(name) => setVesselForm({ ...vesselForm, name })} placeholder={t('e.g., Armada')} testID="setup-vessel-name" />
            <TextField label={t('Default Branch')} hint={tip('defaultBranch')} value={vesselForm.defaultBranch} onChangeText={(defaultBranch) => setVesselForm({ ...vesselForm, defaultBranch })} placeholder={t('main')} autoCapitalize="none" />
            <TextField label={t('Repository URL')} hint={tip('repoUrl')} value={vesselForm.repoUrl} onChangeText={(repoUrl) => setVesselForm({ ...vesselForm, repoUrl })} placeholder={t('https://github.com/org/repo.git or /path/to/repo')} autoCapitalize="none" autoCorrect={false} testID="setup-vessel-repo" />
            <TextField label={t('Working Directory')} hint={tip('workingDirectory')} value={vesselForm.workingDirectory} onChangeText={(workingDirectory) => setVesselForm({ ...vesselForm, workingDirectory })} placeholder={t('Optional local checkout path')} autoCapitalize="none" autoCorrect={false} />
            <SelectField label={t('Landing Mode')} hint={t(setupLandingModeHint(vesselForm.landingMode))} value={vesselForm.landingMode} options={SETUP_LANDING_MODES.map((m) => ({ value: m.value, label: t(m.label) }))} onChange={(landingMode) => setVesselForm({ ...vesselForm, landingMode })} closeLabel={t('Close')} testID="setup-vessel-landing" />
            <SwitchField label={t('Enable model context accumulation')} hint={tip('enableModelContext')} value={vesselForm.enableModelContext} onChange={(enableModelContext) => setVesselForm({ ...vesselForm, enableModelContext })} />
            <SwitchField label={t('Allow concurrent missions on this vessel')} hint={tip('allowConcurrentMissions')} value={vesselForm.allowConcurrentMissions} onChange={(allowConcurrentMissions) => setVesselForm({ ...vesselForm, allowConcurrentMissions })} />
            <TextField label={t('Project Context')} hint={tip('projectContext')} value={vesselForm.projectContext} onChangeText={(projectContext) => setVesselForm({ ...vesselForm, projectContext })} placeholder={t('Optional architecture, build, or repository notes for captains.')} multiline />
            <TextField label={t('Style Guide')} hint={tip('styleGuide')} value={vesselForm.styleGuide} onChangeText={(styleGuide) => setVesselForm({ ...vesselForm, styleGuide })} placeholder={t('Optional conventions captains should follow.')} multiline />
          </>
        )}
        <Button label={busy ? t('Saving...') : vesselMode === 'existing' ? t('Use Vessel') : t('Register Vessel')} busy={busy} disabled={loading} onPress={() => void submitVessel()} testID="setup-vessel-submit" />
      </>
    );
  } else if (current === 3) {
    content = (
      <>
        <AppText variant="heading" accessibilityRole="header">{t('Prepare a captain for dispatch')}</AppText>
        <AppText muted>{t('A captain is an AI runtime registered with Armada. Direct dispatch assigns work to an available captain, so this step ensures the pool has capacity.')}</AppText>
        <Context label={t('Vessel')} value={activeVessel?.name ?? t('No vessel selected')} />
        <ModeToggle value={captainMode} onChange={setCaptainMode} existingLabel={t('Use Existing Idle')} existingDisabled={idleCaptains.length === 0} testID="setup-captain-mode" />
        {captainMode === 'existing' ? (
          <SelectField label={t('Captain')} hint={tip('captainSelect')} value={selectedCaptainId} options={idleCaptains.map((c) => ({ value: c.id, label: `${c.name} (${c.runtime}, ${c.state})` }))} placeholder={t('No idle captains found')} onChange={setSelectedCaptainId} closeLabel={t('Close')} testID="setup-captain-select" />
        ) : (
          <>
            <TextField label={t('Captain Name')} hint={tip('captainName')} value={captainForm.name} onChangeText={(name) => setCaptainForm({ ...captainForm, name })} testID="setup-captain-name" />
            <SelectField label={t('Runtime')} hint={tip('runtime')} value={captainForm.runtime} options={RUNTIMES} placeholder={t('Select runtime...')} onChange={(runtime) => setCaptainForm({ ...captainForm, runtime })} closeLabel={t('Close')} testID="setup-captain-runtime" />
            <TextField label={t('Model')} hint={tip('model')} value={captainForm.model} onChangeText={(model) => setCaptainForm({ ...captainForm, model })} placeholder={t('Optional runtime-specific model override')} autoCapitalize="none" />
            <SelectField
              label={t('Capability Tier')}
              hint={t('Capability tier for routing. Cheaper tiers handle routine work; stronger tiers handle complex work and serve as fallback when a preferred captain is busy.')}
              value={captainForm.tier}
              options={[{ value: '', label: t('Not set') }, { value: 'Economy', label: t('Economy') }, { value: 'Standard', label: t('Standard') }, { value: 'Premium', label: t('Premium') }]}
              onChange={(tier) => setCaptainForm({ ...captainForm, tier })}
              closeLabel={t('Close')}
            />
            <TextField label={t('System Instructions')} hint={tip('systemInstructions')} value={captainForm.systemInstructions} onChangeText={(systemInstructions) => setCaptainForm({ ...captainForm, systemInstructions })} multiline />
            {muxRuntime ? (
              <>
                <TextField label={t('Mux Config Directory')} hint={t('Optional mux config directory override. Leave blank to use mux defaults.')} value={captainForm.muxConfigDirectory} onChangeText={(muxConfigDirectory) => setCaptainForm({ ...captainForm, muxConfigDirectory })} placeholder={t('Optional path, e.g. C:\\Users\\you\\.mux')} autoCapitalize="none" />
                <TextField label={t('Mux Endpoint')} hint={muxHint || t('Named mux endpoint to validate and launch for this captain.')} value={captainForm.muxEndpoint} onChangeText={(muxEndpoint) => setCaptainForm({ ...captainForm, muxEndpoint })} placeholder={t('Required endpoint name')} autoCapitalize="none" testID="setup-captain-mux-endpoint" />
                {muxEndpoints.length > 0 ? (
                  <SelectField label={t('Mux Endpoint')} value={captainForm.muxEndpoint} options={muxEndpoints.map((e) => ({ value: e.name, label: e.name, description: `${e.adapterType} ${e.model}` }))} onChange={(muxEndpoint) => setCaptainForm({ ...captainForm, muxEndpoint })} closeLabel={t('Close')} />
                ) : null}
                <Button label={t('Refresh Mux Endpoints')} variant="ghost" onPress={() => void loadMux()} />
                <Disclosure title={t('Advanced Mux Overrides')}>
                  <TextField label={t('Mux Base URL')} value={captainForm.muxBaseUrl} onChangeText={(muxBaseUrl) => setCaptainForm({ ...captainForm, muxBaseUrl })} placeholder={t('Optional override')} autoCapitalize="none" />
                  <TextField label={t('Mux Adapter Type')} value={captainForm.muxAdapterType} onChangeText={(muxAdapterType) => setCaptainForm({ ...captainForm, muxAdapterType })} placeholder={t('Optional override')} autoCapitalize="none" />
                  <TextField label={t('Mux Temperature')} value={captainForm.muxTemperature} onChangeText={(muxTemperature) => setCaptainForm({ ...captainForm, muxTemperature })} placeholder={t('Optional number')} keyboardType="decimal-pad" />
                  <TextField label={t('Mux Max Tokens')} value={captainForm.muxMaxTokens} onChangeText={(muxMaxTokens) => setCaptainForm({ ...captainForm, muxMaxTokens })} placeholder={t('Optional integer')} keyboardType="number-pad" />
                  <TextField label={t('Mux System Prompt Path')} value={captainForm.muxSystemPromptPath} onChangeText={(muxSystemPromptPath) => setCaptainForm({ ...captainForm, muxSystemPromptPath })} placeholder={t('Optional path')} autoCapitalize="none" />
                  <SelectField
                    label={t('Mux Approval Policy')}
                    value={captainForm.muxApprovalPolicy}
                    options={[{ value: '', label: t('Default (auto)') }, ...['auto', 'autoapprove', 'deny', 'ask'].map((p) => ({ value: p, label: p }))]}
                    onChange={(muxApprovalPolicy) => setCaptainForm({ ...captainForm, muxApprovalPolicy })}
                    closeLabel={t('Close')}
                  />
                </Disclosure>
              </>
            ) : null}
          </>
        )}
        <Button label={busy ? t('Saving...') : captainMode === 'existing' ? t('Use Captain') : t('Create Captain')} busy={busy} disabled={loading} onPress={() => void submitCaptain()} testID="setup-captain-submit" />
      </>
    );
  } else if (current === 4) {
    content = (
      <>
        <AppText variant="heading" accessibilityRole="header">{t('Dispatch the first mission')}</AppText>
        <AppText muted>{t('This uses Armada\'s direct mission dispatch path. It does not create a voyage from the setup wizard.')}</AppText>
        <FieldCard>
          <Field label={t('Fleet')} value={activeFleet?.name ?? '-'} />
          <Field label={t('Vessel')} value={activeVessel?.name ?? '-'} />
          <Field label={t('Available Captain')} value={activeCaptain?.name ?? '-'} />
        </FieldCard>
        <TextField label={t('Mission Title')} hint={tip('missionTitle')} value={dispatchForm.title} onChangeText={(title) => setDispatchForm({ ...dispatchForm, title })} testID="setup-mission-title" />
        <TextField label={t('Mission Description')} hint={tip('missionDescription')} value={dispatchForm.description} onChangeText={(description) => setDispatchForm({ ...dispatchForm, description })} multiline />
        <TextField label={t('Priority')} hint={tip('priority')} value={dispatchForm.priority} onChangeText={(priority) => setDispatchForm({ ...dispatchForm, priority })} keyboardType="number-pad" />
        <Button label={busy ? t('Dispatching...') : t('Dispatch Mission')} busy={busy} disabled={!activeVessel || !activeCaptain} onPress={() => void submitDispatch()} testID="setup-dispatch" />
      </>
    );
  } else {
    const nextItem = (readiness?.setupChecklist || []).find((i) => !i.isSatisfied) || null;
    const wait = (value: string | number) => (nextLoading ? t('Loading...') : String(value));
    content = (
      <>
        <AppText variant="heading" accessibilityRole="header">{t('Mission dispatched, handoff ready')}</AppText>
        <AppText muted>{t('Armada can dispatch safely now. Use the handoff actions below to move this vessel into onboarding, backlog, planning, workspace, workflow-profile, environment, and first-check setup.')}</AppText>
        {warning ? <Banner tone="info" title={warning} /> : null}
        {mission ? (
          <FieldCard testID="setup-mission">
            <Field label={t('Mission')} value={mission.title} />
            <Field label={t('Mission ID')} value={mission.id} mono />
            <Field label={t('Status')} value={t(mission.status)} />
            <Field label={t('Captain ID')} value={idShort(mission.captainId)} mono />
            <Field label={t('Vessel ID')} value={idShort(mission.vesselId)} mono />
            <Field label={t('Branch')} value={mission.branchName || '-'} />
          </FieldCard>
        ) : <AppText muted>{t('No mission has been dispatched yet.')}</AppText>}
        {mission && FAILED_MISSION_STATUSES.has(String(mission.status)) ? (
          <Banner tone="danger" title={t(mission.status)} message={mission.failureReason ?? undefined} />
        ) : null}
        <FieldCard>
          <Field label={t('Readiness')} value={nextLoading ? t('Loading...') : readiness ? `${readiness.setupChecklistSatisfiedCount}/${readiness.setupChecklistTotalCount}` : '-'} />
          <Field label={t('Blocking Issues')} value={nextLoading ? t('Loading...') : readiness ? String(readiness.errorCount) : '-'} />
          <Field label={t('Workflow Profiles')} value={wait(profiles.length)} />
          <Field label={t('Environments')} value={wait(environments.length)} />
        </FieldCard>
        {nextItem ? (
          <FieldCard title={t('Next Recommended Step')}>
            <Field label={nextItem.title} value={nextItem.message} />
          </FieldCard>
        ) : null}
        <View style={styles.actions}>
          <Button label={busy ? t('Refreshing...') : t('Refresh Mission Status')} variant="secondary" busy={busy} disabled={!mission} onPress={() => void refreshMission()} style={styles.inline} />
          {mission ? <Button label={t('Open Mission')} variant="secondary" onPress={() => finishAt(`/missions/${mission.id}`)} style={styles.inline} testID="setup-open-mission" /> : null}
          {activeVessel ? (
            <>
              <Button label={t('Open Vessel Onboarding')} variant="secondary" onPress={() => finishAt(`/vessels/${activeVessel.id}/onboarding`)} style={styles.inline} />
              <Button label={t('Open Backlog')} variant="secondary" onPress={() => finishAt(setupBacklogPath(handoffFleetId, activeVessel.id))} style={styles.inline} />
              <Button
                label={t('Open Planning')}
                variant="secondary"
                style={styles.inline}
                onPress={() => finishAt(`/planning${prefillQuery({
                  fromSetupWizard: '1',
                  fleetId: handoffFleetId,
                  vesselId: activeVessel.id,
                  title: t('Repository onboarding follow-up'),
                  initialPrompt: t('Use the onboarding findings for this vessel to produce a safe first execution plan. Summarize the setup gaps, pick one small follow-up task, and outline how Armada should approach it.'),
                })}`)}
              />
              <Button label={t('Open Workspace')} variant="secondary" onPress={() => finishAt(`/workspace/${activeVessel.id}`)} style={styles.inline} />
              <Button
                label={profiles.length > 0 ? t('Open Workflow Profiles') : t('Create Workflow Profile')}
                variant="secondary"
                style={styles.inline}
                onPress={() => finishAt(profiles.length > 0 ? '/configuration?tab=workflow-profiles' : `/workflow-profiles/new?scope=Vessel&vesselId=${encodeURIComponent(activeVessel.id)}`)}
              />
              <Button
                label={environments.length > 0 ? t('Open Environments') : t('Create Environment')}
                variant="secondary"
                style={styles.inline}
                onPress={() => finishAt(environments.length > 0 ? '/delivery?tab=environments' : `/environments/new?vesselId=${encodeURIComponent(activeVessel.id)}&kind=Development`)}
              />
              <Button label={t('Run First Check')} variant="secondary" style={styles.inline} onPress={() => finishAt(`/delivery${prefillQuery({ tab: 'checks', run: '1', vesselId: activeVessel.id, branchName: activeVessel.defaultBranch || '' })}`)} />
            </>
          ) : null}
          <Button label={t('Open Playbooks')} variant="secondary" onPress={() => finishAt('/configuration?tab=playbooks')} style={styles.inline} />
          <Button label={t('Finish Setup')} onPress={finish} style={styles.inline} testID="setup-finish" />
        </View>
      </>
    );
  }

  return (
    <SafeAreaView edges={['left', 'right', 'bottom']} style={[styles.fill, { backgroundColor: colors.background }]} testID="setup-wizard">
      <Stack.Screen options={{ title: t('Setup Wizard') }} />
      <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
        <View style={styles.column}>
          <AppText variant="title" accessibilityRole="header">{t('Launch Armada With One Mission')}</AppText>
          <AppText variant="label" muted testID="setup-step-count">{t('Step {{current}} of {{total}}', { current: current + 1, total: SETUP_STEPS.length })}</AppText>
          <View style={styles.progress} accessibilityLabel={t('Setup progress')}>
            {SETUP_STEPS.map((step, index) => (
              <View key={step.title} style={[styles.dot, { borderColor: index <= current ? colors.primary : colors.border, backgroundColor: index < current ? colors.primary : 'transparent' }]} accessible accessibilityLabel={`${index + 1}. ${t(step.title)}`} accessibilityState={{ selected: index === current }}>
                <AppText variant="caption" color={index < current ? 'primaryText' : 'text'}>{index < current ? '\u2713' : String(index + 1)}</AppText>
              </View>
            ))}
          </View>
          <AppText variant="label">{t(SETUP_STEPS[current].title)}</AppText>
          <AppText variant="caption" muted>{t(SETUP_STEPS[current].summary)}</AppText>
          {loading && current === 0 ? <Banner tone="info" title={t('Loading existing Armada resources...')} /> : null}
          <View style={[styles.card, { backgroundColor: colors.surface, borderColor: colors.border }]}>{content}</View>
          {result ? (
            result.kind === 'success'
              ? <AppText color="success" accessibilityLiveRegion="polite" testID="setup-result">{result.message}</AppText>
              : <Banner tone={result.kind === 'error' ? 'danger' : 'info'} title={result.message} testID="setup-result" />
          ) : null}
          <View style={styles.footer}>
            <Button label={t('Skip Setup')} variant="ghost" onPress={finish} style={styles.inline} testID="setup-skip" />
            <View style={styles.flex} />
            {current > 0 ? <Button label={t('Back')} variant="secondary" onPress={() => goTo(current - 1)} style={styles.inline} testID="setup-back" /> : null}
            {current < SETUP_STEPS.length - 1 ? (
              <Button label={current === 0 ? t('Start Setup') : t('Next')} onPress={() => goTo(current + 1)} disabled={!canAdvance} style={styles.inline} testID="setup-next" />
            ) : null}
          </View>
        </View>
      </ScrollView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  flex: { flex: 1 },
  content: { padding: spacing.lg },
  column: { width: '100%', maxWidth: 820, alignSelf: 'center', gap: spacing.md },
  progress: { flexDirection: 'row', gap: spacing.sm },
  dot: { width: 30, height: 30, borderRadius: 15, borderWidth: 2, alignItems: 'center', justifyContent: 'center' },
  card: { borderWidth: StyleSheet.hairlineWidth, borderRadius: radius.md, padding: spacing.md, gap: spacing.sm },
  actions: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm },
  inline: { marginBottom: 0 },
  footer: { flexDirection: 'row', flexWrap: 'wrap', alignItems: 'center', gap: spacing.sm },
});
