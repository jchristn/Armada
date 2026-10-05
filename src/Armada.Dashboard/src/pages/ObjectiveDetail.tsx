import { useEffect, useMemo, useRef, useState } from 'react';
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom';
import {
  applyObjectiveRefinementSummary,
  createBacklogItem,
  createBacklogRefinementSession,
  deleteBacklogItem,
  deleteObjectiveRefinementSession,
  getBacklogItem,
  getObjectiveRefinementSession,
  importObjectiveFromGitHub,
  listBacklog,
  listBacklogRefinementSessions,
  listCaptains,
  listFleets,
  listPipelines,
  listVessels,
  sendObjectiveRefinementMessage,
  stopObjectiveRefinementSession,
  summarizeObjectiveRefinementSession,
  updateBacklogItem,
} from '../api/client';
import type {
  Captain,
  Fleet,
  Objective,
  ObjectiveBacklogState,
  ObjectiveEffort,
  ObjectiveKind,
  ObjectivePriority,
  ObjectiveRefinementSession,
  ObjectiveRefinementSessionDetail,
  ObjectiveRefinementSummaryResponse,
  ObjectiveStatus,
  ObjectiveUpsertRequest,
  Pipeline,
  SelectedPlaybook,
  Vessel,
  WebSocketMessage,
} from '../types/models';
import { useAuth } from '../context/AuthContext';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import { useWebSocket } from '../context/WebSocketContext';
import BacklogRefinementSessionList from '../components/backlog/BacklogRefinementSessionList';
import {
  buildObjectiveDispatchPrompt,
  buildObjectivePlanningPrompt,
  buildObjectiveReleaseNotes,
  joinList,
  joinSuggestedPlaybooks,
  OBJECTIVE_BACKLOG_STATES,
  OBJECTIVE_EFFORTS,
  OBJECTIVE_KINDS,
  OBJECTIVE_PRIORITIES,
  OBJECTIVE_STATUSES,
  parseSuggestedPlaybooks,
  splitList,
} from '../components/backlog/backlogUtils';
import {
  getLatestAssistantRefinementMessage,
  mergeCaptainState,
  removeRefinementSession,
  upsertRefinementMessage,
  upsertRefinementSession,
} from '../components/backlog/refinementUtils';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import ErrorModal from '../components/shared/ErrorModal';
import JsonViewer from '../components/shared/JsonViewer';
import PageHeader from '../components/shared/PageHeader';
import StatusBadge from '../components/shared/StatusBadge';
import { buildObjectiveDuplicatePayload } from '../lib/duplicates';
import { refinementSummaryFromEvent } from '../lib/refinementSummary';
import { mergeSessionDetail, newerOf } from '../lib/liveMerge';
import { sortByName } from '../lib/sortByName';

function toDateTimeLocalValue(value: string | null | undefined): string {
  if (!value) return '';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  const year = date.getFullYear();
  const month = `${date.getMonth() + 1}`.padStart(2, '0');
  const day = `${date.getDate()}`.padStart(2, '0');
  const hours = `${date.getHours()}`.padStart(2, '0');
  const minutes = `${date.getMinutes()}`.padStart(2, '0');
  return `${year}-${month}-${day}T${hours}:${minutes}`;
}

function toIsoOrNull(value: string): string | null {
  if (!value.trim()) return null;
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? null : parsed.toISOString();
}

interface TagEntry {
  key: string;
  value: string;
}

function createEmptyTagEntry(): TagEntry {
  return { key: '', value: '' };
}

function parseTagEntry(raw: string): TagEntry {
  const trimmed = raw.trim();
  if (!trimmed) return createEmptyTagEntry();

  const separatorIndex = [trimmed.indexOf(':'), trimmed.indexOf('=')]
    .filter((index) => index > 0)
    .sort((left, right) => left - right)[0] ?? -1;
  if (separatorIndex < 1) {
    return { key: trimmed, value: '' };
  }

  return {
    key: trimmed.slice(0, separatorIndex).trim(),
    value: trimmed.slice(separatorIndex + 1).trim(),
  };
}

function parseTagEntries(tags: string[]): TagEntry[] {
  const rows = tags
    .map(parseTagEntry)
    .filter((entry) => entry.key || entry.value);

  return rows.length > 0 ? rows : [createEmptyTagEntry()];
}

function serializeTagEntries(entries: TagEntry[]): string[] {
  return entries
    .map((entry) => {
      const key = entry.key.trim();
      const value = entry.value.trim();
      if (!key && !value) return '';
      if (!value) return key;
      return `${key}:${value}`;
    })
    .filter((entry): entry is string => entry.length > 0);
}

function replacePrimaryLinkedId(currentValue: string, nextId: string): string {
  if (!nextId) return '';

  const existing = splitList(currentValue).filter((id, index) => index > 0 && id !== nextId);
  return joinList([nextId, ...existing]);
}

function renderRouteLinks(ids: string[], prefix: string | ((id: string) => string), labelMap?: Map<string, string>) {
  if (ids.length < 1) return <span className="text-dim">None</span>;
  return (
    <div style={{ display: 'grid', gap: '0.35rem' }}>
      {ids.map((id) => (
        <div key={`${prefix}-${id}`}>
          <Link to={typeof prefix === 'function' ? prefix(id) : `${prefix}${id}`}>{labelMap?.get(id) || id}</Link>
        </div>
      ))}
    </div>
  );
}

export default function ObjectiveDetail() {
  const { id } = useParams<{ id: string }>();
  const location = useLocation();
  const navigate = useNavigate();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { subscribe } = useWebSocket();
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();

  const createMode = id === 'new';
  const canManage = isAdmin || isTenantAdmin;
  const canonicalListPath = '/backlog';
  const canonicalItemPath = (objectiveId: string) => `/backlog/${objectiveId}`;
  const isBacklogPath = location.pathname.startsWith('/backlog');

  const [objective, setObjective] = useState<Objective | null>(null);
  const [fleets, setFleets] = useState<Fleet[]>([]);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [captains, setCaptains] = useState<Captain[]>([]);
  const [pipelines, setPipelines] = useState<Pipeline[]>([]);
  const [availableObjectives, setAvailableObjectives] = useState<Objective[]>([]);
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [status, setStatus] = useState<ObjectiveStatus>('Draft');
  const [kind, setKind] = useState<ObjectiveKind>('Feature');
  const [category, setCategory] = useState('');
  const [priority, setPriority] = useState<ObjectivePriority>('P2');
  const [rank, setRank] = useState('');
  const [backlogState, setBacklogState] = useState<ObjectiveBacklogState>('Inbox');
  const [effort, setEffort] = useState<ObjectiveEffort>('M');
  const [owner, setOwner] = useState('');
  const [targetVersion, setTargetVersion] = useState('');
  const [dueUtc, setDueUtc] = useState('');
  const [parentObjectiveId, setParentObjectiveId] = useState('');
  const [blockedByObjectiveIds, setBlockedByObjectiveIds] = useState<string[]>([]);
  const [refinementSummary, setRefinementSummary] = useState('');
  const [suggestedPipelineId, setSuggestedPipelineId] = useState('');
  const [suggestedPlaybooks, setSuggestedPlaybooks] = useState('');
  const [tagEntries, setTagEntries] = useState<TagEntry[]>([createEmptyTagEntry()]);
  const [acceptanceCriteria, setAcceptanceCriteria] = useState('');
  const [nonGoals, setNonGoals] = useState('');
  const [rolloutConstraints, setRolloutConstraints] = useState('');
  const [evidenceLinks, setEvidenceLinks] = useState('');
  const [fleetIds, setFleetIds] = useState('');
  const [vesselIds, setVesselIds] = useState('');
  const [planningSessionIds, setPlanningSessionIds] = useState('');
  const [refinementSessionIds, setRefinementSessionIds] = useState('');
  const [voyageIds, setVoyageIds] = useState('');
  const [missionIds, setMissionIds] = useState('');
  const [checkRunIds, setCheckRunIds] = useState('');
  const [releaseIds, setReleaseIds] = useState('');
  const [deploymentIds, setDeploymentIds] = useState('');
  const [incidentIds, setIncidentIds] = useState('');
  const [loading, setLoading] = useState(!createMode);
  const [saving, setSaving] = useState(false);
  const [refreshingGitHub, setRefreshingGitHub] = useState(false);
  const [error, setError] = useState('');
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({
    open: false,
    title: '',
    message: '',
    onConfirm: () => {},
  });

  const [refinementSessions, setRefinementSessions] = useState<ObjectiveRefinementSession[]>([]);
  const [selectedRefinementSessionId, setSelectedRefinementSessionId] = useState('');
  const [refinementDetail, setRefinementDetail] = useState<ObjectiveRefinementSessionDetail | null>(null);
  const [refinementCaptainId, setRefinementCaptainId] = useState('');
  const [refinementFleetId, setRefinementFleetId] = useState('');
  const [refinementVesselId, setRefinementVesselId] = useState('');
  const [refinementTitle, setRefinementTitle] = useState('');
  const [refinementInitialMessage, setRefinementInitialMessage] = useState('');
  const [refinementComposer, setRefinementComposer] = useState('');
  const [selectedRefinementMessageId, setSelectedRefinementMessageId] = useState('');
  const [refinementSummaryDraft, setRefinementSummaryDraft] = useState<ObjectiveRefinementSummaryResponse | null>(null);
  const [creatingRefinement, setCreatingRefinement] = useState(false);
  const [sendingRefinement, setSendingRefinement] = useState(false);
  const [summarizingRefinement, setSummarizingRefinement] = useState(false);
  const [applyingRefinement, setApplyingRefinement] = useState(false);
  const [stoppingRefinement, setStoppingRefinement] = useState(false);
  const [deletingRefinement, setDeletingRefinement] = useState(false);

  const refinementTranscriptRef = useRef<HTMLDivElement | null>(null);

  const fleetMap = useMemo(() => new Map(fleets.map((fleet) => [fleet.id, fleet.name])), [fleets]);
  const vesselMap = useMemo(() => new Map(vessels.map((vessel) => [vessel.id, vessel.name])), [vessels]);
  const captainNameById = useMemo(() => new Map(captains.map((captain) => [captain.id, captain.name])), [captains]);
  const linkedFleetIds = useMemo(() => splitList(fleetIds), [fleetIds]);
  const linkedVesselIds = useMemo(() => splitList(vesselIds), [vesselIds]);
  const primaryFleetId = linkedFleetIds[0] || objective?.fleetIds[0] || '';
  const primaryVesselId = linkedVesselIds[0] || objective?.vesselIds[0] || '';
  const currentObjectiveId = objective?.id || (createMode ? '' : id || '');
  const selectableObjectives = useMemo(() => (
    availableObjectives
      .filter((item) => item.id !== currentObjectiveId)
      .slice()
      .sort((left, right) => {
        if (left.rank !== right.rank) return left.rank - right.rank;
        return left.title.localeCompare(right.title);
      })
  ), [availableObjectives, currentObjectiveId]);
  const selectableObjectiveIdSet = useMemo(
    () => new Set(selectableObjectives.map((item) => item.id)),
    [selectableObjectives],
  );
  const missingBlockedObjectiveIds = useMemo(
    () => blockedByObjectiveIds.filter((blockedId) => !selectableObjectiveIdSet.has(blockedId)),
    [blockedByObjectiveIds, selectableObjectiveIdSet],
  );
  const requestedRefinementSessionId = useMemo(
    () => new URLSearchParams(location.search).get('refinementSessionId') || '',
    [location.search],
  );
  const gitHubSourceNumber = objective?.sourceNumber ?? null;
  const gitHubSourceType = objective?.sourceType === 'PullRequest' ? 'PullRequest' : 'Issue';
  const primaryVesselName = primaryVesselId ? vesselMap.get(primaryVesselId) || primaryVesselId : '';
  const hasArmadaActivityLinks = useMemo(() => (
    splitList(planningSessionIds).length > 0
    || splitList(refinementSessionIds).length > 0
    || splitList(voyageIds).length > 0
    || splitList(missionIds).length > 0
    || splitList(checkRunIds).length > 0
    || splitList(releaseIds).length > 0
    || splitList(deploymentIds).length > 0
    || splitList(incidentIds).length > 0
  ), [
    checkRunIds,
    deploymentIds,
    incidentIds,
    missionIds,
    planningSessionIds,
    refinementSessionIds,
    releaseIds,
    voyageIds,
  ]);
  const selectedRefinementCaptain = useMemo(
    () => captains.find((captain) => captain.id === refinementCaptainId) || null,
    [captains, refinementCaptainId],
  );
  const fieldHelp = useMemo(() => ({
    title: t('Short backlog headline. Armada reuses this in backlog lists, planning, dispatch, and release drafting.'),
    vessel: t('Primary vessel this backlog item targets. Armada uses the selected vessel to infer fleet context and unlock repository-aware planning and dispatch.'),
    status: t('High-level lifecycle state for the work item, such as Draft, Planned, InProgress, or Completed.'),
    owner: t('Person responsible for driving or approving the work. This is for human ownership and reporting.'),
    description: t('Longer problem statement or implementation summary. Captains and operators use this as the core work description.'),
    tagKey: t('Tag name or category, such as area, team, or source.'),
    tagValue: t('Tag value paired with the key. Leave blank if the tag only needs a single label.'),
    refinementSummary: t('Condensed summary of what refinement sessions discovered. Useful as the handoff into planning or dispatch.'),
    acceptanceCriteria: t('Concrete conditions that must be true for the backlog item to count as done.'),
    nonGoals: t('Things this work explicitly should not do. Use this to keep planning and implementation bounded.'),
    rolloutConstraints: t('Operational, release, or sequencing constraints Armada should preserve while implementing or shipping this work.'),
    evidenceLinks: t('URLs or references that support the scope, rollout, verification, or release notes for this work item.'),
    kind: t('Work classification, such as Feature, Bug, Refactor, or Research.'),
    category: t('Optional grouping label for reporting or backlog organization.'),
    priority: t('Relative urgency. Higher priority items should generally be planned and dispatched sooner.'),
    backlogState: t('Backlog workflow phase used to track whether the item is inbox triage, refinement, planning-ready, or dispatch-ready.'),
    effort: t('Rough size estimate used to communicate complexity and planning weight.'),
    rank: t('Manual backlog ordering value. Lower ranks appear earlier in the backlog.'),
    targetVersion: t('Optional target release or milestone name for this work item.'),
    dueUtc: t('Optional due date used for urgency tracking and delivery planning.'),
    parentObjectiveId: t('Optional parent backlog item. Use this when the current work is a child of a larger backlog item or initiative.'),
    suggestedPipeline: t('Pipeline Armada should prefer when this backlog item turns into planning or dispatch.'),
    blockedByObjectiveIds: t('Other backlog items that must be resolved before this work can move forward. Use Ctrl or Cmd-click to select multiple blockers.'),
    suggestedPlaybooks: t('Playbooks to carry into planning or dispatch. Use playbook-id:delivery-mode entries.'),
    refinementCaptain: t('Captain that will run the backlog refinement conversation for this item.'),
    refinementVessel: t('Optional vessel context for refinement. Use this when repository-specific context will help the captain refine scope.'),
    refinementFleet: t('Optional fleet context for refinement when the work spans multiple vessels in the same fleet.'),
    refinementTitle: t('Optional human-readable title for the refinement session transcript.'),
    refinementInitialMessage: t('Optional kickoff prompt that tells the captain what to focus on first in the refinement session.'),
    refinementComposer: t('Follow-up refinement message to the selected captain. Use this to sharpen scope, acceptance criteria, rollout, or non-goals.'),
  }), [t]);
  const actionHelp = useMemo(() => ({
    planning: t('Open a planning session for this backlog item using the linked vessel and suggested context.'),
    dispatch: t('Create dispatch-ready implementation work from this backlog item.'),
    release: t('Draft release notes and release metadata from this backlog item.'),
    deleteBacklog: t('Delete this backlog item while leaving downstream linked records intact.'),
    back: t('Return to the backlog list without changing the current page.'),
    save: t('Save the current backlog item changes.'),
    startRefinement: t('Start a new captain-backed refinement session for this backlog item.'),
    stopRefinement: t('Ask Armada to stop the current refinement session.'),
    deleteRefinement: t('Delete the current refinement session and transcript.'),
    sendRefinement: t('Send the drafted refinement message to the active captain session.'),
    summarizeRefinement: t('Generate a structured summary from the selected transcript message.'),
    applyRefinement: t('Apply the structured refinement summary back into this backlog item.'),
  }), [t]);

  function hydrateObjectiveForm(next: Objective) {
    setObjective(next);
    setTitle(next.title);
    setDescription(next.description || '');
    setStatus(next.status);
    setKind(next.kind);
    setCategory(next.category || '');
    setPriority(next.priority);
    setRank(String(next.rank));
    setBacklogState(next.backlogState);
    setEffort(next.effort);
    setOwner(next.owner || '');
    setTargetVersion(next.targetVersion || '');
    setDueUtc(toDateTimeLocalValue(next.dueUtc));
    setParentObjectiveId(next.parentObjectiveId || '');
    setBlockedByObjectiveIds(next.blockedByObjectiveIds || []);
    setRefinementSummary(next.refinementSummary || '');
    setSuggestedPipelineId(next.suggestedPipelineId || '');
    setSuggestedPlaybooks(joinSuggestedPlaybooks(next.suggestedPlaybooks));
    setTagEntries(parseTagEntries(next.tags));
    setAcceptanceCriteria(joinList(next.acceptanceCriteria));
    setNonGoals(joinList(next.nonGoals));
    setRolloutConstraints(joinList(next.rolloutConstraints));
    setEvidenceLinks(joinList(next.evidenceLinks));
    setFleetIds(joinList(next.fleetIds));
    setVesselIds(joinList(next.vesselIds));
    setPlanningSessionIds(joinList(next.planningSessionIds));
    setRefinementSessionIds(joinList(next.refinementSessionIds));
    setVoyageIds(joinList(next.voyageIds));
    setMissionIds(joinList(next.missionIds));
    setCheckRunIds(joinList(next.checkRunIds));
    setReleaseIds(joinList(next.releaseIds));
    setDeploymentIds(joinList(next.deploymentIds));
    setIncidentIds(joinList(next.incidentIds));
    setRefinementFleetId(next.fleetIds[0] || '');
    setRefinementVesselId(next.vesselIds[0] || '');
  }

  function buildPayload(): ObjectiveUpsertRequest {
    const parsedRank = Number.parseInt(rank, 10);
    return {
      title: title.trim() || null,
      description: description.trim() || null,
      status,
      kind,
      category: category.trim() || null,
      priority,
      rank: Number.isFinite(parsedRank) ? parsedRank : null,
      backlogState,
      effort,
      owner: owner.trim() || null,
      targetVersion: targetVersion.trim() || null,
      dueUtc: toIsoOrNull(dueUtc),
      parentObjectiveId: parentObjectiveId.trim() || null,
      blockedByObjectiveIds,
      refinementSummary: refinementSummary.trim() || null,
      suggestedPipelineId: suggestedPipelineId.trim() || null,
      suggestedPlaybooks: parseSuggestedPlaybooks(suggestedPlaybooks),
      tags: serializeTagEntries(tagEntries),
      acceptanceCriteria: splitList(acceptanceCriteria),
      nonGoals: splitList(nonGoals),
      rolloutConstraints: splitList(rolloutConstraints),
      evidenceLinks: splitList(evidenceLinks),
      fleetIds: splitList(fleetIds),
      vesselIds: splitList(vesselIds),
      planningSessionIds: splitList(planningSessionIds),
      refinementSessionIds: splitList(refinementSessionIds),
      voyageIds: splitList(voyageIds),
      missionIds: splitList(missionIds),
      checkRunIds: splitList(checkRunIds),
      releaseIds: splitList(releaseIds),
      deploymentIds: splitList(deploymentIds),
      incidentIds: splitList(incidentIds),
    };
  }

  function updateTagEntry(index: number, field: keyof TagEntry, value: string) {
    setTagEntries((current) => current.map((entry, entryIndex) => (
      entryIndex === index ? { ...entry, [field]: value } : entry
    )));
  }

  function addTagEntry() {
    setTagEntries((current) => [...current, createEmptyTagEntry()]);
  }

  function formatObjectiveOptionLabel(item: Objective): string {
    const linkedVesselId = item.vesselIds[0] || '';
    const linkedVesselName = linkedVesselId ? vesselMap.get(linkedVesselId) || linkedVesselId : '';
    return linkedVesselName
      ? `${item.title} - ${linkedVesselName} (${item.id})`
      : `${item.title} (${item.id})`;
  }

  function removeTagEntry(index: number) {
    setTagEntries((current) => {
      const next = current.filter((_entry, entryIndex) => entryIndex !== index);
      return next.length > 0 ? next : [createEmptyTagEntry()];
    });
  }

  function handleVesselScopeChange(nextVesselId: string) {
    if (!nextVesselId) {
      setVesselIds('');
      setFleetIds('');
      return;
    }

    setVesselIds(replacePrimaryLinkedId(vesselIds, nextVesselId));
    const selectedVessel = vessels.find((vessel) => vessel.id === nextVesselId);
    if (selectedVessel?.fleetId) {
      setFleetIds(replacePrimaryLinkedId(fleetIds, selectedVessel.fleetId));
      return;
    }

    setFleetIds('');
  }

  async function loadRefinementSessions(objectiveId: string, preferredSessionId?: string) {
    const sessions = await listBacklogRefinementSessions(objectiveId);
    setRefinementSessions(sessions);
    const selectedId = preferredSessionId
      || (sessions.some((session) => session.id === selectedRefinementSessionId) ? selectedRefinementSessionId : '')
      || sessions[0]?.id
      || '';
    setSelectedRefinementSessionId(selectedId);
    if (selectedId) {
      const detail = await getObjectiveRefinementSession(selectedId);
      setRefinementDetail(detail);
    } else {
      setRefinementDetail(null);
    }
  }

  useEffect(() => {
    let cancelled = false;

    Promise.all([
      listFleets({ pageSize: 9999 }),
      listVessels({ pageSize: 9999 }),
      listCaptains({ pageSize: 9999 }),
      listPipelines({ pageSize: 9999 }),
      listBacklog({ pageSize: 9999 }),
    ]).then(([fleetResult, vesselResult, captainResult, pipelineResult, objectiveResult]) => {
      if (cancelled) return;
      setFleets(fleetResult.objects || []);
      setVessels(sortByName(vesselResult.objects));
      setCaptains(captainResult.objects || []);
      setPipelines(pipelineResult.objects || []);
      setAvailableObjectives(objectiveResult.objects || []);
    }).catch((err: unknown) => {
      if (!cancelled) setError(err instanceof Error ? err.message : t('Failed to load backlog reference data.'));
    });

    return () => { cancelled = true; };
  }, [t]);

  useEffect(() => {
    if (!createMode) return;

    const params = new URLSearchParams(location.search);
    const prefillVesselId = params.get('vesselId') || '';
    const prefillFleetId = vessels.find((vessel) => vessel.id === prefillVesselId)?.fleetId || '';

    if (prefillVesselId) {
      setVesselIds((current) => current || replacePrimaryLinkedId('', prefillVesselId));
    }

    if (prefillFleetId) {
      setFleetIds((current) => current || replacePrimaryLinkedId('', prefillFleetId));
    }
  }, [createMode, location.search, vessels]);

  useEffect(() => {
    if (createMode || !id) return;
    const backlogId = id;
    let mounted = true;

    async function load() {
      try {
        setLoading(true);
        const result = await getBacklogItem(backlogId);
        if (!mounted) return;
        hydrateObjectiveForm(result);
        await loadRefinementSessions(result.id, requestedRefinementSessionId || undefined);
        setError('');
      } catch (err: unknown) {
        if (mounted) setError(err instanceof Error ? err.message : t('Failed to load backlog item.'));
      } finally {
        if (mounted) setLoading(false);
      }
    }

    void load();
    return () => { mounted = false; };
  }, [createMode, id, requestedRefinementSessionId, t]);

  useEffect(() => {
    if (!selectedRefinementSessionId) return;
    let cancelled = false;
    getObjectiveRefinementSession(selectedRefinementSessionId)
      .then((detail) => {
        if (!cancelled) setRefinementDetail(detail);
      })
      .catch(() => {
        if (!cancelled) setRefinementDetail(null);
      });
    return () => { cancelled = true; };
  }, [selectedRefinementSessionId]);

  useEffect(() => {
    const latestAssistant = refinementDetail ? getLatestAssistantRefinementMessage(refinementDetail.messages) : null;
    if (!latestAssistant) {
      setSelectedRefinementMessageId('');
      return;
    }

    const selectedExists = refinementDetail?.messages.some((message) => message.id === selectedRefinementMessageId);
    if (!selectedRefinementMessageId || !selectedExists) {
      setSelectedRefinementMessageId(latestAssistant.id);
    }
  }, [refinementDetail, selectedRefinementMessageId]);

  useEffect(() => {
    if (!refinementTranscriptRef.current) return;
    refinementTranscriptRef.current.scrollTop = refinementTranscriptRef.current.scrollHeight;
  }, [refinementDetail?.messages]);

  useEffect(() => {
    const unsubscribe = subscribe((msg: WebSocketMessage) => {
      if (msg.type === 'objective.changed') {
        const payload = msg.data as Objective | undefined;
        if (!payload || payload.id !== objective?.id) return;
        hydrateObjectiveForm(payload);
        return;
      }

      if (msg.type === 'captain.changed') {
        const payload = msg.data as { id?: string; name?: string; state?: string } | undefined;
        if (!payload?.id || !payload.state) return;
        setCaptains((current) => mergeCaptainState(current, { id: payload.id!, state: payload.state!, name: payload.name }));
        setRefinementDetail((current) => {
          if (!current?.captain || current.captain.id !== payload.id) return current;
          return {
            ...current,
            captain: {
              ...current.captain,
              state: payload.state!,
              name: payload.name ?? current.captain.name,
            },
          };
        });
        return;
      }

      if (msg.type === 'objective-refinement-session.changed') {
        const payload = msg.data as { session?: ObjectiveRefinementSession } | undefined;
        if (!payload?.session || payload.session.objectiveId !== objective?.id) return;
        setRefinementSessions((current) => upsertRefinementSession(current, payload.session!));
        setRefinementDetail((current) => current && current.session.id === payload.session!.id
          ? { ...current, session: payload.session! }
          : current);
        return;
      }

      if (msg.type === 'objective-refinement-session.message.created' || msg.type === 'objective-refinement-session.message.updated') {
        const payload = msg.data as { sessionId?: string; objectiveId?: string; message?: ObjectiveRefinementSessionDetail['messages'][number] } | undefined;
        if (!payload?.sessionId || payload.objectiveId !== objective?.id || !payload.message) return;
        setRefinementDetail((current) => {
          if (!current || current.session.id !== payload.sessionId) return current;
          return {
            ...current,
            messages: upsertRefinementMessage(current.messages, payload.message!),
          };
        });
        return;
      }

      if (msg.type === 'objective-refinement-session.summary.created') {
        const draft = refinementSummaryFromEvent(msg.data);
        if (!draft || draft.sessionId !== selectedRefinementSessionId) return;
        setRefinementSummaryDraft(draft);
        if (draft.messageId) setSelectedRefinementMessageId(draft.messageId);
        return;
      }

      if (msg.type === 'objective-refinement-session.applied') {
        const payload = msg.data as { objective?: Objective; summary?: ObjectiveRefinementSummaryResponse } | undefined;
        if (!payload?.objective || payload.objective.id !== objective?.id) return;
        hydrateObjectiveForm(payload.objective);
        if (payload.summary) setRefinementSummaryDraft(payload.summary);
        return;
      }

      if (msg.type === 'objective-refinement-session.deleted') {
        const payload = msg.data as { sessionId?: string; objectiveId?: string } | undefined;
        if (!payload?.sessionId || payload.objectiveId !== objective?.id) return;
        setRefinementSessions((current) => removeRefinementSession(current, payload.sessionId!));
        if (selectedRefinementSessionId === payload.sessionId) {
          setSelectedRefinementSessionId('');
          setRefinementDetail(null);
          setRefinementSummaryDraft(null);
        }
      }
    });

    return unsubscribe;
  }, [objective?.id, refinementDetail, selectedRefinementSessionId, subscribe]);

  async function handleSave() {
    if (!canManage) return;
    if (!title.trim()) {
      setError(t('Backlog item title is required.'));
      return;
    }

    try {
      setSaving(true);
      const payload = buildPayload();
      if (createMode) {
        const created = await createBacklogItem(payload);
        pushToast('success', t('Backlog item "{{title}}" created.', { title: created.title }));
        navigate(canonicalItemPath(created.id));
        return;
      }

      if (!id) return;
      const updated = await updateBacklogItem(id, payload);
      hydrateObjectiveForm(updated);
      pushToast('success', t('Backlog item "{{title}}" saved.', { title: updated.title }));
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Save failed.'));
    } finally {
      setSaving(false);
    }
  }

  async function handleRefreshGitHub() {
    if (!objective || !primaryVesselId || !gitHubSourceNumber) return;

    try {
      setRefreshingGitHub(true);
      const refreshed = await importObjectiveFromGitHub({
        objectiveId: objective.id,
        vesselId: primaryVesselId,
        sourceType: gitHubSourceType,
        number: gitHubSourceNumber,
      });
      hydrateObjectiveForm(refreshed);
      pushToast('success', t('Backlog item "{{title}}" refreshed from GitHub.', { title: refreshed.title }));
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('GitHub refresh failed.'));
    } finally {
      setRefreshingGitHub(false);
    }
  }

  function handleDelete() {
    if (!objective || !canManage) return;
    setConfirm({
      open: true,
      title: t('Delete Backlog Item'),
      message: t('Delete "{{title}}"? This removes the backlog item and its objective snapshot history only.', { title: objective.title }),
      onConfirm: async () => {
        setConfirm((current) => ({ ...current, open: false }));
        try {
          await deleteBacklogItem(objective.id);
          pushToast('warning', t('Backlog item "{{title}}" deleted.', { title: objective.title }));
          navigate(canonicalListPath);
        } catch (err: unknown) {
          setError(err instanceof Error ? err.message : t('Delete failed.'));
        }
      },
    });
  }

  async function handleDuplicate() {
    if (!objective || !canManage) return;
    try {
      setSaving(true);
      const created = await createBacklogItem(buildObjectiveDuplicatePayload(objective));
      pushToast('success', t('Backlog item "{{title}}" duplicated.', { title: created.title }));
      navigate(canonicalItemPath(created.id));
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Duplicate failed.'));
    } finally {
      setSaving(false);
    }
  }

  async function handleCreateRefinementSession() {
    if (!objective || !refinementCaptainId) return;

    try {
      setCreatingRefinement(true);
      const detail = await createBacklogRefinementSession(objective.id, {
        captainId: refinementCaptainId,
        fleetId: refinementFleetId || undefined,
        vesselId: refinementVesselId || undefined,
        title: refinementTitle.trim() || undefined,
        initialMessage: refinementInitialMessage.trim() || undefined,
      });
      setRefinementSessions((current) => upsertRefinementSession(current, detail.session));
      setSelectedRefinementSessionId(detail.session.id);
      setRefinementDetail(detail);
      setRefinementInitialMessage('');
      setRefinementTitle('');
      setRefinementSummaryDraft(null);
      pushToast('success', t('Refinement session started with {{captain}}.', { captain: detail.captain?.name || detail.session.captainId }));
      await loadRefinementSessions(objective.id, detail.session.id);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to start refinement session.'));
    } finally {
      setCreatingRefinement(false);
    }
  }

  async function handleSendRefinementMessage() {
    if (!refinementDetail || !refinementComposer.trim()) return;

    try {
      setSendingRefinement(true);
      const detail = await sendObjectiveRefinementMessage(refinementDetail.session.id, { content: refinementComposer.trim() });
      setRefinementComposer('');
      // A fast captain can finish the turn (over the WebSocket) before this response arrives; keep the newer state.
      setRefinementDetail((current) => mergeSessionDetail(current, detail));
      setRefinementSessions((current) => {
        const existing = current.find((item) => item.id === detail.session.id);
        return upsertRefinementSession(current, existing ? newerOf(existing, detail.session) : detail.session);
      });
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to send refinement message.'));
    } finally {
      setSendingRefinement(false);
    }
  }

  async function handleSummarizeRefinement() {
    if (!refinementDetail) return;

    try {
      setSummarizingRefinement(true);
      const summary = await summarizeObjectiveRefinementSession(refinementDetail.session.id, {
        messageId: selectedRefinementMessageId || undefined,
      });
      setRefinementSummaryDraft(summary);
      if (summary.messageId) setSelectedRefinementMessageId(summary.messageId);
      pushToast('success', t('Refinement summary generated.'));
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to summarize refinement session.'));
    } finally {
      setSummarizingRefinement(false);
    }
  }

  async function handleApplyRefinementSummary() {
    if (!refinementDetail || !objective) return;

    try {
      setApplyingRefinement(true);
      const result = await applyObjectiveRefinementSummary(refinementDetail.session.id, {
        messageId: selectedRefinementMessageId || undefined,
        markMessageSelected: true,
        promoteBacklogState: true,
      });
      hydrateObjectiveForm(result.objective);
      setRefinementSummaryDraft(result.summary);
      pushToast('success', t('Applied refinement summary back to the backlog item.'));
      await loadRefinementSessions(objective.id, refinementDetail.session.id);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to apply refinement summary.'));
    } finally {
      setApplyingRefinement(false);
    }
  }

  async function handleStopRefinementSession() {
    if (!refinementDetail) return;

    try {
      setStoppingRefinement(true);
      const detail = await stopObjectiveRefinementSession(refinementDetail.session.id);
      setRefinementDetail(detail);
      setRefinementSessions((current) => upsertRefinementSession(current, detail.session));
      pushToast('warning', t('Refinement session is stopping.'));
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to stop refinement session.'));
    } finally {
      setStoppingRefinement(false);
    }
  }

  async function handleDeleteRefinementSession() {
    if (!refinementDetail || !objective) return;

    try {
      setDeletingRefinement(true);
      await deleteObjectiveRefinementSession(refinementDetail.session.id);
      setRefinementSessions((current) => removeRefinementSession(current, refinementDetail.session.id));
      setSelectedRefinementSessionId('');
      setRefinementDetail(null);
      setRefinementSummaryDraft(null);
      pushToast('warning', t('Refinement session deleted.'));
      await loadRefinementSessions(objective.id);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to delete refinement session.'));
    } finally {
      setDeletingRefinement(false);
    }
  }

  if (loading) {
    return (
      <div className="playbook-empty-state">
        <strong>{t('Loading backlog item...')}</strong>
        <span>{t('Refreshing backlog metadata, linked delivery records, and captain-backed refinement transcripts.')}</span>
      </div>
    );
  }

  return (
    <div>
      <PageHeader
        breadcrumb={
          <>
            <Link to={canonicalListPath}>{t('Backlog')}</Link> <span className="breadcrumb-sep">&gt;</span> <span>{createMode ? t('New Backlog Item') : title}</span>
            {!isBacklogPath && !createMode && objective && (
              <>
                <span className="breadcrumb-sep">&gt;</span>
                <Link to={canonicalItemPath(objective.id)}>{t('Canonical Backlog Route')}</Link>
              </>
            )}
          </>
        }
        title={createMode ? t('Create Backlog Item') : title}
        subtitle={
          !createMode && objective ? (
            <div className="backlog-chip-row" style={{ marginTop: '0.35rem' }}>
              <span className={`tag ${objective.kind.toLowerCase()}`}>{objective.kind}</span>
              <span className={`tag ${objective.priority.toLowerCase()}`}>{objective.priority}</span>
              <span className={`tag ${objective.effort.toLowerCase()}`}>{objective.effort}</span>
              <StatusBadge status={objective.status} />
              <span className={`tag ${objective.backlogState.toLowerCase()}`}>{objective.backlogState}</span>
            </div>
          ) : undefined
        }
        actions={
          <>
          {!createMode && (
            <button className="btn btn-sm" onClick={() => setJsonData({ open: true, title, data: objective })} title={t('Open the raw backlog item JSON payload.')}>
              {t('View JSON')}
            </button>
          )}
          {!createMode && objective && (
            <button className="btn btn-sm" onClick={() => navigate(`/history?objectiveId=${encodeURIComponent(objective.id)}`)} title={t('Open the historical timeline filtered to this backlog item.')}>
              {t('History')}
            </button>
          )}
          {!createMode && objective?.sourceProvider === 'GitHub' && primaryVesselId && gitHubSourceNumber && (
            <button className="btn btn-sm" disabled={refreshingGitHub} onClick={() => void handleRefreshGitHub()} title={t('Refresh this backlog item from its GitHub source issue or pull request.')}>
              {refreshingGitHub ? t('Refreshing...') : t('Refresh GitHub')}
            </button>
          )}
          {!createMode && objective && (
            <button
              className="btn btn-sm"
              disabled={!primaryVesselId}
              title={actionHelp.planning}
              onClick={() => navigate('/planning', {
                state: {
                  fromObjective: true,
                  objectiveId: objective.id,
                  title: `${objective.title} Planning`,
                  fleetId: primaryFleetId || undefined,
                  vesselId: primaryVesselId || undefined,
                  pipelineId: objective.suggestedPipelineId || undefined,
                  initialPrompt: buildObjectivePlanningPrompt(objective),
                },
              })}
            >
              {t('Start Planning')}
            </button>
          )}
          {!createMode && objective && (
            <button
              className="btn btn-sm"
              disabled={!primaryVesselId}
              title={actionHelp.dispatch}
              onClick={() => navigate('/dispatch', {
                state: {
                  fromObjective: true,
                  objectiveId: objective.id,
                  vesselId: primaryVesselId || undefined,
                  pipelineName: pipelines.find((pipeline) => pipeline.id === objective.suggestedPipelineId)?.name,
                  selectedPlaybooks: objective.suggestedPlaybooks || [],
                  prompt: buildObjectiveDispatchPrompt(objective),
                  voyageTitle: objective.title,
                },
              })}
            >
              {t('Open In Dispatch')}
            </button>
          )}
          {!createMode && objective && (
            <button
              className="btn btn-sm"
              disabled={!primaryVesselId}
              title={actionHelp.release}
              onClick={() => navigate('/releases/new', {
                state: {
                  prefill: {
                    vesselId: primaryVesselId || null,
                    title: `${objective.title} Release`,
                    summary: objective.description || null,
                    notes: buildObjectiveReleaseNotes(objective),
                    status: 'Draft',
                  },
                  objectiveIds: [objective.id],
                },
              })}
            >
              {t('Draft Release')}
            </button>
          )}
          {!createMode && canManage && (
            <button className="btn btn-sm" disabled={saving} onClick={() => void handleDuplicate()} title={t('Create a fresh backlog item by copying this item’s reusable planning metadata and scope.')}>
              {t('Duplicate')}
            </button>
          )}
          {!createMode && canManage && (
            <button className="btn btn-sm btn-danger" onClick={handleDelete} title={actionHelp.deleteBacklog}>
              {t('Delete')}
            </button>
          )}
          </>
        }
      />

      <ErrorModal error={error} onClose={() => setError('')} />
      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <ConfirmDialog
        open={confirm.open}
        title={confirm.title}
        message={confirm.message}
        onConfirm={confirm.onConfirm}
        onCancel={() => setConfirm((current) => ({ ...current, open: false }))}
      />

      {!canManage && (
        <div className="alert alert-warning" style={{ marginBottom: '1rem' }}>
          {t('You can view backlog items, but only tenant administrators can create or change them.')}
        </div>
      )}

      {!createMode && objective && (
        <div className="detail-grid" style={{ marginBottom: '1rem' }}>
          <div className="info-card">
            <span>{t('Rank')}</span>
            <strong>{objective.rank}</strong>
          </div>
          <div className="info-card">
            <span>{t('Owner')}</span>
            <strong>{objective.owner || t('Unassigned')}</strong>
          </div>
          <div className="info-card">
            <span>{t('Refinement Sessions')}</span>
            <strong>{objective.refinementSessionIds.length}</strong>
          </div>
          <div className="info-card">
            <span>{t('Linked Releases')}</span>
            <strong>{objective.releaseIds.length}</strong>
          </div>
          <div className="info-card">
            <span>{t('Linked Incidents')}</span>
            <strong>{objective.incidentIds.length}</strong>
          </div>
          <div className="info-card">
            <span>{t('Last Updated')}</span>
            <strong title={formatDateTime(objective.lastUpdateUtc)}>{formatRelativeTime(objective.lastUpdateUtc)}</strong>
          </div>
        </div>
      )}

      {!createMode && objective?.sourceProvider === 'GitHub' && (
        <div className="card" style={{ marginBottom: '1rem' }}>
          <div className="detail-header" style={{ marginBottom: '0.75rem' }}>
            <h3>{t('GitHub Source')}</h3>
            <div className="text-dim">{objective.sourceType || t('Unknown source')}</div>
          </div>
          <div className="detail-grid">
            <div className="detail-field">
              <span className="detail-label">{t('Provider')}</span>
              <span>{objective.sourceProvider}</span>
            </div>
            <div className="detail-field">
              <span className="detail-label">{t('Source')}</span>
              <span>{objective.sourceId || '-'}</span>
            </div>
            <div className="detail-field">
              <span className="detail-label">{t('Last Source Update')}</span>
              <span>{objective.sourceUpdatedUtc ? formatDateTime(objective.sourceUpdatedUtc) : '-'}</span>
            </div>
            <div className="detail-field">
              <span className="detail-label">{t('Source Link')}</span>
              {objective.sourceUrl ? (
                <a href={objective.sourceUrl} target="_blank" rel="noreferrer">{objective.sourceUrl}</a>
              ) : (
                <span>-</span>
              )}
            </div>
          </div>
        </div>
      )}

      {!createMode && objective && (
        <>
          <div className="alert" style={{ marginBottom: '1rem' }}>
            {t('Refinement is lighter than planning: it uses a selected captain to sharpen the backlog item, but it does not imply repository mutation, dock provisioning, or dispatch by itself.')}
          </div>
          {primaryVesselId ? (
            <div className="alert alert-success backlog-action-note" style={{ marginBottom: '1rem' }}>
              {t('Primary vessel {{vessel}} is linked, so planning, dispatch, and release drafting can start from this backlog item.', { vessel: primaryVesselName })}
            </div>
          ) : null}
          {!primaryVesselId && (
            <div className="alert alert-warning backlog-action-note" style={{ marginBottom: '1rem' }}>
              {t('This backlog item can be refined now, but it still needs a vessel before repository-aware planning or dispatch can start.')}
            </div>
          )}
        </>
      )}

      <div className="detail-sections">
        <div className="card detail-section">
          <div className="detail-section-header">
            <h3>{t('Backlog Item')}</h3>
          </div>
          <div className="detail-form-grid backlog-item-grid">
            <div className="form-field detail-field-full">
              <label title={fieldHelp.title}>{t('Title')}</label>
              <input aria-label={fieldHelp.title}
                value={title}
                onChange={(event) => setTitle(event.target.value)}
                disabled={!canManage}
                placeholder={t('Add feature to improve login')}
                title={fieldHelp.title}
              />
            </div>
            <div className="form-field">
              <label title={fieldHelp.vessel}>{t('Vessel')}</label>
              <select aria-label={fieldHelp.vessel} value={primaryVesselId} onChange={(event) => handleVesselScopeChange(event.target.value)} disabled={!canManage} title={fieldHelp.vessel}>
                <option value="">{t('No vessel selected')}</option>
                {vessels.map((vessel) => (
                  <option key={vessel.id} value={vessel.id}>
                    {vessel.fleetId ? `${vessel.name} (${fleetMap.get(vessel.fleetId) || vessel.fleetId})` : vessel.name}
                  </option>
                ))}
              </select>
            </div>
            <div className="form-field">
              <label title={fieldHelp.status}>{t('Status')}</label>
              <select aria-label={fieldHelp.status} value={status} onChange={(event) => setStatus(event.target.value as ObjectiveStatus)} disabled={!canManage} title={fieldHelp.status}>
                {OBJECTIVE_STATUSES.map((value) => (
                  <option key={value} value={value}>{value}</option>
                ))}
              </select>
            </div>
            <div className="form-field">
              <label title={fieldHelp.owner}>{t('Owner')}</label>
              <input aria-label={fieldHelp.owner} value={owner} onChange={(event) => setOwner(event.target.value)} disabled={!canManage} title={fieldHelp.owner} />
            </div>
            <div className="form-field detail-field-full">
              <label title={fieldHelp.description}>{t('Description')}</label>
              <textarea aria-label={fieldHelp.description} rows={5} value={description} onChange={(event) => setDescription(event.target.value)} disabled={!canManage} title={fieldHelp.description} />
            </div>
            <div className="form-field detail-field-full">
              <label title={`${fieldHelp.tagKey} ${fieldHelp.tagValue}`}>{t('Tags')}</label>
              <div className="tag-entry-list">
                {tagEntries.map((entry, index) => (
                  <div key={`tag-entry-${index}`} className="tag-entry-row">
                    <input aria-label={fieldHelp.tagKey}
                      value={entry.key}
                      onChange={(event) => updateTagEntry(index, 'key', event.target.value)}
                      disabled={!canManage}
                      placeholder={t('Key')}
                      title={fieldHelp.tagKey}
                    />
                    <input aria-label={fieldHelp.tagValue}
                      value={entry.value}
                      onChange={(event) => updateTagEntry(index, 'value', event.target.value)}
                      disabled={!canManage}
                      placeholder={t('Value')}
                      title={fieldHelp.tagValue}
                    />
                    <div className="tag-entry-actions">
                      {index === tagEntries.length - 1 ? (
                        <button
                          type="button"
                          className="icon-btn icon-btn-add"
                          onClick={addTagEntry}
                          disabled={!canManage}
                          aria-label={t('Add tag')}
                          title={t('Add tag')}
                        />
                      ) : (
                        <span className="icon-btn icon-btn-placeholder" aria-hidden="true" />
                      )}
                      <button
                        type="button"
                        className="icon-btn icon-btn-delete"
                        onClick={() => removeTagEntry(index)}
                        disabled={!canManage}
                        aria-label={t('Delete tag')}
                        title={t('Delete tag')}
                      />
                    </div>
                  </div>
                ))}
              </div>
            </div>
          </div>
        </div>

        <div className="card detail-section">
          <div className="detail-section-header">
            <h3>{t('Scope')}</h3>
          </div>
          <div className="text-dim backlog-section-note">
            {t('Attach a fleet or vessel above to unlock repository-aware planning, dispatch, and release drafting from this backlog item. Armada adds downstream planning, mission, release, deployment, and incident links later.')}
          </div>
          <div className="detail-form-grid">
            {!createMode && objective && (
              <>
                <div>
                  <h4>{t('Linked Fleets')}</h4>
                  {renderRouteLinks(linkedFleetIds, '/fleets/', fleetMap)}
                </div>
                <div>
                  <h4>{t('Linked Vessels')}</h4>
                  {renderRouteLinks(linkedVesselIds, '/vessels/', vesselMap)}
                </div>
              </>
            )}
            <div className="form-field detail-field-full">
              <label title={fieldHelp.refinementSummary}>{t('Refinement Summary')}</label>
              <textarea aria-label={fieldHelp.refinementSummary} rows={4} value={refinementSummary} onChange={(event) => setRefinementSummary(event.target.value)} disabled={!canManage} title={fieldHelp.refinementSummary} />
            </div>
            <div className="form-field">
              <label title={fieldHelp.acceptanceCriteria}>{t('Acceptance Criteria')}</label>
              <textarea aria-label={fieldHelp.acceptanceCriteria} rows={6} value={acceptanceCriteria} onChange={(event) => setAcceptanceCriteria(event.target.value)} disabled={!canManage} title={fieldHelp.acceptanceCriteria} />
            </div>
            <div className="form-field">
              <label title={fieldHelp.nonGoals}>{t('Non-Goals')}</label>
              <textarea aria-label={fieldHelp.nonGoals} rows={6} value={nonGoals} onChange={(event) => setNonGoals(event.target.value)} disabled={!canManage} title={fieldHelp.nonGoals} />
            </div>
            <div className="form-field detail-field-full">
              <label title={fieldHelp.rolloutConstraints}>{t('Rollout Constraints')}</label>
              <textarea aria-label={fieldHelp.rolloutConstraints} rows={4} value={rolloutConstraints} onChange={(event) => setRolloutConstraints(event.target.value)} disabled={!canManage} title={fieldHelp.rolloutConstraints} />
            </div>
            <div className="form-field detail-field-full">
              <label title={fieldHelp.evidenceLinks}>{t('Evidence Links')}</label>
              <textarea aria-label={fieldHelp.evidenceLinks} rows={4} value={evidenceLinks} onChange={(event) => setEvidenceLinks(event.target.value)} disabled={!canManage} title={fieldHelp.evidenceLinks} />
            </div>
          </div>
        </div>

        <div className="card detail-section">
          <div className="detail-section-header">
            <h3>{t('Workflow Metadata')}</h3>
          </div>
          <div className="detail-form-grid">
            <div className="form-field">
              <label title={fieldHelp.kind}>{t('Kind')}</label>
              <select aria-label={fieldHelp.kind} value={kind} onChange={(event) => setKind(event.target.value as ObjectiveKind)} disabled={!canManage} title={fieldHelp.kind}>
                {OBJECTIVE_KINDS.map((value) => (
                  <option key={value} value={value}>{value}</option>
                ))}
              </select>
            </div>
            <div className="form-field">
              <label title={fieldHelp.category}>{t('Category')}</label>
              <input aria-label={fieldHelp.category} value={category} onChange={(event) => setCategory(event.target.value)} disabled={!canManage} title={fieldHelp.category} />
            </div>
            <div className="form-field">
              <label title={fieldHelp.priority}>{t('Priority')}</label>
              <select aria-label={fieldHelp.priority} value={priority} onChange={(event) => setPriority(event.target.value as ObjectivePriority)} disabled={!canManage} title={fieldHelp.priority}>
                {OBJECTIVE_PRIORITIES.map((value) => (
                  <option key={value} value={value}>{value}</option>
                ))}
              </select>
            </div>
            <div className="form-field">
              <label title={fieldHelp.backlogState}>{t('Backlog State')}</label>
              <select aria-label={fieldHelp.backlogState} value={backlogState} onChange={(event) => setBacklogState(event.target.value as ObjectiveBacklogState)} disabled={!canManage} title={fieldHelp.backlogState}>
                {OBJECTIVE_BACKLOG_STATES.map((value) => (
                  <option key={value} value={value}>{value}</option>
                ))}
              </select>
            </div>
            <div className="form-field">
              <label title={fieldHelp.effort}>{t('Effort')}</label>
              <select aria-label={fieldHelp.effort} value={effort} onChange={(event) => setEffort(event.target.value as ObjectiveEffort)} disabled={!canManage} title={fieldHelp.effort}>
                {OBJECTIVE_EFFORTS.map((value) => (
                  <option key={value} value={value}>{value}</option>
                ))}
              </select>
            </div>
            <div className="form-field">
              <label title={fieldHelp.rank}>{t('Rank')}</label>
              <input aria-label={fieldHelp.rank} type="number" value={rank} onChange={(event) => setRank(event.target.value)} disabled={!canManage} title={fieldHelp.rank} />
            </div>
            <div className="form-field">
              <label title={fieldHelp.targetVersion}>{t('Target Version')}</label>
              <input aria-label={fieldHelp.targetVersion} value={targetVersion} onChange={(event) => setTargetVersion(event.target.value)} disabled={!canManage} title={fieldHelp.targetVersion} />
            </div>
            <div className="form-field">
              <label title={fieldHelp.dueUtc}>{t('Due UTC')}</label>
              <input aria-label={fieldHelp.dueUtc} type="datetime-local" value={dueUtc} onChange={(event) => setDueUtc(event.target.value)} disabled={!canManage} title={fieldHelp.dueUtc} />
            </div>
            <div className="form-field">
              <label title={fieldHelp.parentObjectiveId}>{t('Parent Objective')}</label>
              <select aria-label={fieldHelp.parentObjectiveId} value={parentObjectiveId} onChange={(event) => setParentObjectiveId(event.target.value)} disabled={!canManage} title={fieldHelp.parentObjectiveId}>
                <option value="">{t('No parent objective')}</option>
                {parentObjectiveId && !selectableObjectiveIdSet.has(parentObjectiveId) && (
                  <option value={parentObjectiveId}>{t('Unavailable backlog item ({{id}})', { id: parentObjectiveId })}</option>
                )}
                {selectableObjectives.map((item) => (
                  <option key={item.id} value={item.id}>{formatObjectiveOptionLabel(item)}</option>
                ))}
              </select>
            </div>
            <div className="form-field">
              <label title={fieldHelp.suggestedPipeline}>{t('Suggested Pipeline')}</label>
              <select aria-label={fieldHelp.suggestedPipeline} value={suggestedPipelineId} onChange={(event) => setSuggestedPipelineId(event.target.value)} disabled={!canManage} title={fieldHelp.suggestedPipeline}>
                <option value="">{t('None')}</option>
                {pipelines.map((pipeline) => (
                  <option key={pipeline.id} value={pipeline.id}>{pipeline.name}</option>
                ))}
              </select>
            </div>
            <div className="form-field detail-field-full">
              <label title={fieldHelp.blockedByObjectiveIds}>{t('Blocked By Objectives')}</label>
              <select aria-label={fieldHelp.blockedByObjectiveIds}
                multiple
                size={Math.min(Math.max(selectableObjectives.length + missingBlockedObjectiveIds.length, 4), 8)}
                value={blockedByObjectiveIds}
                onChange={(event) => setBlockedByObjectiveIds(Array.from(event.target.selectedOptions, (option) => option.value))}
                disabled={!canManage || (selectableObjectives.length === 0 && missingBlockedObjectiveIds.length === 0)}
                title={fieldHelp.blockedByObjectiveIds}
              >
                {missingBlockedObjectiveIds.map((blockedId) => (
                  <option key={`blocked-missing-${blockedId}`} value={blockedId}>
                    {t('Unavailable backlog item ({{id}})', { id: blockedId })}
                  </option>
                ))}
                {selectableObjectives.map((item) => (
                  <option key={item.id} value={item.id}>{formatObjectiveOptionLabel(item)}</option>
                ))}
              </select>
            </div>
            <div className="form-field detail-field-full">
              <label title={fieldHelp.suggestedPlaybooks}>{t('Suggested Playbooks')}</label>
              <textarea aria-label={fieldHelp.suggestedPlaybooks} rows={4} value={suggestedPlaybooks} onChange={(event) => setSuggestedPlaybooks(event.target.value)} disabled={!canManage} placeholder={t('playbook-id:InlineFullContent')} title={fieldHelp.suggestedPlaybooks} />
            </div>
          </div>
        </div>

        {!createMode && objective && hasArmadaActivityLinks && (
          <div className="card detail-section">
            <div className="detail-section-header">
              <h3>{t('Armada Links')}</h3>
            </div>
            <div className="text-dim backlog-section-note">
              {t('Armada records these automatically as planning, execution, release, deployment, and incident work references this backlog item.')}
            </div>
            <div className="detail-form-grid">
              {objective.planningSessionIds.length > 0 && (
                <div>
                  <h4>{t('Planning Sessions')}</h4>
                  {renderRouteLinks(objective.planningSessionIds, '/planning/')}
                </div>
              )}
              {objective.refinementSessionIds.length > 0 && (
                <div>
                  <h4>{t('Refinement Sessions')}</h4>
                  {renderRouteLinks(
                    objective.refinementSessionIds,
                    (sessionId) => `/backlog/${objective.id}?refinementSessionId=${encodeURIComponent(sessionId)}`,
                  )}
                </div>
              )}
              {objective.voyageIds.length > 0 && (
                <div>
                  <h4>{t('Voyages')}</h4>
                  {renderRouteLinks(objective.voyageIds, '/voyages/')}
                </div>
              )}
              {objective.missionIds.length > 0 && (
                <div>
                  <h4>{t('Missions')}</h4>
                  {renderRouteLinks(objective.missionIds, '/missions/')}
                </div>
              )}
              {objective.checkRunIds.length > 0 && (
                <div>
                  <h4>{t('Checks')}</h4>
                  {renderRouteLinks(objective.checkRunIds, '/checks/')}
                </div>
              )}
              {objective.releaseIds.length > 0 && (
                <div>
                  <h4>{t('Releases')}</h4>
                  {renderRouteLinks(objective.releaseIds, '/releases/')}
                </div>
              )}
              {objective.deploymentIds.length > 0 && (
                <div>
                  <h4>{t('Deployments')}</h4>
                  {renderRouteLinks(objective.deploymentIds, '/deployments/')}
                </div>
              )}
              {objective.incidentIds.length > 0 && (
                <div>
                  <h4>{t('Incidents')}</h4>
                  {renderRouteLinks(objective.incidentIds, '/incidents/')}
                </div>
              )}
            </div>
          </div>
        )}

        {!createMode && objective && (
          <div className="card detail-section">
            <div className="detail-section-header">
              <h3>{t('Backlog Refinement')}</h3>
            </div>
            <div className="backlog-refinement-layout">
              <div className="backlog-refinement-column">
                <div className="backlog-refinement-note">
                  {t('Choose the captain explicitly here. Refinement is a backlog workflow, separate from repository-aware planning and dispatch.')}
                </div>
                <div className="detail-form-grid">
                  <div className="form-field">
                    <label title={fieldHelp.refinementCaptain}>{t('Captain')}</label>
                    <select aria-label={fieldHelp.refinementCaptain} value={refinementCaptainId} onChange={(event) => setRefinementCaptainId(event.target.value)} disabled={!canManage || creatingRefinement} title={fieldHelp.refinementCaptain}>
                      <option value="">{t('Select a captain')}</option>
                      {captains.map((captain) => (
                        <option key={captain.id} value={captain.id}>
                          {captain.name} ({captain.state})
                        </option>
                      ))}
                    </select>
                  </div>
                  <div className="form-field">
                    <label title={fieldHelp.refinementVessel}>{t('Vessel Context')}</label>
                    <select aria-label={fieldHelp.refinementVessel} value={refinementVesselId} onChange={(event) => setRefinementVesselId(event.target.value)} disabled={!canManage || creatingRefinement} title={fieldHelp.refinementVessel}>
                      <option value="">{t('No vessel context')}</option>
                      {vessels.map((vessel) => (
                        <option key={vessel.id} value={vessel.id}>{vessel.name}</option>
                      ))}
                    </select>
                  </div>
                  <div className="form-field">
                    <label title={fieldHelp.refinementFleet}>{t('Fleet Context')}</label>
                    <select aria-label={fieldHelp.refinementFleet} value={refinementFleetId} onChange={(event) => setRefinementFleetId(event.target.value)} disabled={!canManage || creatingRefinement} title={fieldHelp.refinementFleet}>
                      <option value="">{t('No fleet context')}</option>
                      {fleets.map((fleet) => (
                        <option key={fleet.id} value={fleet.id}>{fleet.name}</option>
                      ))}
                    </select>
                  </div>
                  <div className="form-field">
                    <label title={fieldHelp.refinementTitle}>{t('Session Title')}</label>
                    <input aria-label={fieldHelp.refinementTitle} value={refinementTitle} onChange={(event) => setRefinementTitle(event.target.value)} disabled={!canManage || creatingRefinement} placeholder={t('Optional refinement session title')} title={fieldHelp.refinementTitle} />
                  </div>
                  <div className="form-field" style={{ gridColumn: '1 / -1' }}>
                    <label title={fieldHelp.refinementInitialMessage}>{t('Initial Refinement Prompt')}</label>
                    <textarea aria-label={fieldHelp.refinementInitialMessage} rows={4} value={refinementInitialMessage} onChange={(event) => setRefinementInitialMessage(event.target.value)} disabled={!canManage || creatingRefinement} placeholder={t('Optional kickoff prompt for the selected captain')} title={fieldHelp.refinementInitialMessage} />
                  </div>
                </div>
                {selectedRefinementCaptain && (
                  <div className="backlog-refinement-helper text-dim">
                    {t('Selected captain {{captain}} is currently {{state}}. Armada will fail fast if that captain cannot accept a refinement session.', {
                      captain: selectedRefinementCaptain.name,
                      state: selectedRefinementCaptain.state,
                    })}
                  </div>
                )}
                {canManage && (
                  <div className="form-actions">
                    <button
                      type="button"
                      className="btn btn-primary"
                      disabled={creatingRefinement || !refinementCaptainId}
                      title={actionHelp.startRefinement}
                      onClick={() => void handleCreateRefinementSession()}
                    >
                      {creatingRefinement ? t('Starting...') : t('Start Refinement')}
                    </button>
                  </div>
                )}

                <BacklogRefinementSessionList
                  sessions={refinementSessions}
                  activeSessionId={selectedRefinementSessionId}
                  captainNameById={captainNameById}
                  formatRelativeTime={formatRelativeTime}
                  onSelect={setSelectedRefinementSessionId}
                />
              </div>

              <div className="backlog-refinement-column backlog-refinement-main">
                {!refinementDetail ? (
                  <div className="playbook-empty-state">
                    <strong>{t('No active refinement transcript selected.')}</strong>
                    <span>{t('Start a session with a selected captain or choose an existing transcript from the left.')}</span>
                  </div>
                ) : (
                  <>
                    <div className="card backlog-refinement-card">
                      <div className="detail-header" style={{ marginBottom: '0.75rem' }}>
                        <div>
                          <h3 style={{ marginTop: 0 }}>{refinementDetail.session.title}</h3>
                          <p className="text-dim">
                            {t('Captain {{captain}} {{vesselClause}}', {
                              captain: refinementDetail.captain?.name || refinementDetail.session.captainId,
                              vesselClause: refinementDetail.vessel ? t('with optional vessel context {{vessel}}', { vessel: refinementDetail.vessel.name }) : t('without vessel context'),
                            })}
                          </p>
                        </div>
                        <div className="inline-actions">
                          <StatusBadge status={refinementDetail.session.status} />
                          <button type="button" className="btn btn-sm" disabled={stoppingRefinement} onClick={() => void handleStopRefinementSession()} title={actionHelp.stopRefinement}>
                            {stoppingRefinement ? t('Stopping...') : t('Stop Session')}
                          </button>
                          <button type="button" className="btn btn-sm" disabled={deletingRefinement} onClick={() => void handleDeleteRefinementSession()} title={actionHelp.deleteRefinement}>
                            {deletingRefinement ? t('Deleting...') : t('Delete Session')}
                          </button>
                        </div>
                      </div>

                      <div className="detail-grid" style={{ marginBottom: '1rem' }}>
                        <div className="detail-field">
                          <span className="detail-label">{t('Selected Captain')}</span>
                          <span>{refinementDetail.captain?.name || refinementDetail.session.captainId}</span>
                        </div>
                        <div className="detail-field">
                          <span className="detail-label">{t('Captain State')}</span>
                          <span>{refinementDetail.captain?.state || '-'}</span>
                        </div>
                        <div className="detail-field">
                          <span className="detail-label">{t('Vessel Context')}</span>
                          <span>{refinementDetail.vessel?.name || t('None')}</span>
                        </div>
                        <div className="detail-field">
                          <span className="detail-label">{t('Updated')}</span>
                          <span title={formatDateTime(refinementDetail.session.lastUpdateUtc)}>{formatRelativeTime(refinementDetail.session.lastUpdateUtc)}</span>
                        </div>
                      </div>

                      <div ref={refinementTranscriptRef} className="backlog-refinement-transcript">
                        {refinementDetail.messages.map((message) => (
                          <button
                            key={message.id}
                            type="button"
                            className={`backlog-refinement-message${selectedRefinementMessageId === message.id ? ' selected' : ''}`}
                            onClick={() => setSelectedRefinementMessageId(message.id)}
                            title={t('Select this transcript message for summarizing or applying refinement back to the backlog item.')}
                          >
                            <div className="backlog-refinement-message-head">
                              <strong>{message.role}</strong>
                              <span className="text-dim">{formatRelativeTime(message.lastUpdateUtc)}</span>
                            </div>
                            <div className="backlog-refinement-message-body">{message.content || t('Waiting for content...')}</div>
                          </button>
                        ))}
                      </div>

                      <div className="form-field" style={{ marginTop: '1rem' }}>
                        <label title={fieldHelp.refinementComposer}>{t('Send Refinement Message')}</label>
                        <textarea aria-label={fieldHelp.refinementComposer} rows={4} value={refinementComposer} onChange={(event) => setRefinementComposer(event.target.value)} disabled={!canManage || sendingRefinement} placeholder={t('Ask the selected captain to sharpen scope, acceptance criteria, non-goals, or rollout constraints.')} title={fieldHelp.refinementComposer} />
                      </div>

                      <div className="form-actions">
                        <button type="button" className="btn btn-primary" disabled={!canManage || sendingRefinement || !refinementComposer.trim()} onClick={() => void handleSendRefinementMessage()} title={actionHelp.sendRefinement}>
                          {sendingRefinement ? t('Sending...') : t('Send')}
                        </button>
                        <button type="button" className="btn" disabled={!canManage || summarizingRefinement || !selectedRefinementMessageId} onClick={() => void handleSummarizeRefinement()} title={actionHelp.summarizeRefinement}>
                          {summarizingRefinement ? t('Summarizing...') : t('Summarize')}
                        </button>
                        <button type="button" className="btn" disabled={!canManage || applyingRefinement || !selectedRefinementMessageId} onClick={() => void handleApplyRefinementSummary()} title={actionHelp.applyRefinement}>
                          {applyingRefinement ? t('Applying...') : t('Apply To Backlog Item')}
                        </button>
                      </div>
                      {!selectedRefinementMessageId && (
                        <div className="backlog-refinement-helper text-dim">
                          {t('Select a transcript message before summarizing or applying refinement back to the backlog item.')}
                        </div>
                      )}

                      {refinementDetail.session.failureReason && (
                        <div className="alert alert-error" style={{ marginTop: '1rem' }}>
                          {refinementDetail.session.failureReason}
                        </div>
                      )}
                    </div>

                    <div className="card backlog-refinement-card">
                      <div className="detail-section-header">
                        <h3>{t('Refinement Summary Draft')}</h3>
                      </div>
                      {!refinementSummaryDraft ? (
                        <p className="text-dim">{t('Generate a summary from a selected assistant message, then apply it back into the backlog item.')}</p>
                      ) : (
                        <div className="detail-form-grid">
                          <div className="detail-field" style={{ gridColumn: '1 / -1' }}>
                            <span className="detail-label">{t('Summary')}</span>
                            <span>{refinementSummaryDraft.summary}</span>
                          </div>
                          <div className="detail-field">
                            <span className="detail-label">{t('Suggested Pipeline')}</span>
                            <span>{pipelines.find((pipeline) => pipeline.id === refinementSummaryDraft.suggestedPipelineId)?.name || refinementSummaryDraft.suggestedPipelineId || '-'}</span>
                          </div>
                          <div className="detail-field">
                            <span className="detail-label">{t('Method')}</span>
                            <span>{refinementSummaryDraft.method}</span>
                          </div>
                          <div>
                            <h4>{t('Acceptance Criteria')}</h4>
                            <ul className="backlog-summary-list">
                              {refinementSummaryDraft.acceptanceCriteria.map((item) => <li key={item}>{item}</li>)}
                            </ul>
                          </div>
                          <div>
                            <h4>{t('Non-Goals')}</h4>
                            <ul className="backlog-summary-list">
                              {refinementSummaryDraft.nonGoals.map((item) => <li key={item}>{item}</li>)}
                            </ul>
                          </div>
                          <div style={{ gridColumn: '1 / -1' }}>
                            <h4>{t('Rollout Constraints')}</h4>
                            <ul className="backlog-summary-list">
                              {refinementSummaryDraft.rolloutConstraints.map((item) => <li key={item}>{item}</li>)}
                            </ul>
                          </div>
                        </div>
                      )}
                    </div>
                  </>
                )}
              </div>
            </div>
          </div>
        )}
      </div>

      <div className="detail-footer">
        <button className="btn btn-secondary" onClick={() => navigate(canonicalListPath)} title={actionHelp.back}>
          {t('Back')}
        </button>
        {canManage && (
          <button className="btn btn-primary" disabled={saving} onClick={() => void handleSave()} title={actionHelp.save}>
            {saving ? t('Saving...') : createMode ? t('Create Backlog Item') : t('Save Changes')}
          </button>
        )}
      </div>
    </div>
  );
}
