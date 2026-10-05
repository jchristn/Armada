import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import PageHeader from '../components/shared/PageHeader';
import {
  createPlanningSession,
  deletePlanningSession,
  dispatchPlanningSession,
  getVesselReadiness,
  getPlanningSession,
  listCaptains,
  listFleets,
  listPipelines,
  listPlanningSessions,
  listVessels,
  sendPlanningSessionMessage,
  stopPlanningSession,
  stopPlanningTurn,
  summarizePlanningSession,
  TimeoutError,
} from '../api/client';
import type {
  Captain,
  Fleet,
  Pipeline,
  PlanningSession,
  PlanningSessionDetail,
  PlanningSessionMessage,
  SelectedPlaybook,
  Vessel,
  VesselReadinessResult,
  WebSocketMessage,
} from '../types/models';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import { useWebSocket } from '../context/WebSocketContext';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import PlanningDispatchCard from '../components/planning/PlanningDispatchCard';
import PlanningSessionListCard from '../components/planning/PlanningSessionListCard';
import PlanningStartCard from '../components/planning/PlanningStartCard';
import PlanningTranscriptCard from '../components/planning/PlanningTranscriptCard';
import ReadinessPanel from '../components/shared/ReadinessPanel';
import { applyToolEvent, type ToolEvent } from '../components/shared/ChatToolChips';
import { randomThinkingMessage } from '../components/askThinkingMessages';
import { canCaptainStartPlanning } from '../lib/captains';
import {
  type DispatchSeedState,
  getLatestAssistantMessage,
  mergeCaptainState,
  removeSession,
  resolveDispatchSeedUpdate,
  upsertMessage,
  upsertSession,
} from './planning/planningUtils';

interface PlanningSummaryEventPayload {
  sessionId?: string;
  messageId?: string;
  draft?: {
    title?: string;
    description?: string;
    method?: string;
  };
}

interface PlanningPrefillState {
  fromWorkspace?: boolean;
  fromIncident?: boolean;
  fromObjective?: boolean;
  fromSetupWizard?: boolean;
  captainId?: string;
  objectiveId?: string;
  vesselId?: string;
  fleetId?: string;
  pipelineId?: string;
  title?: string;
  initialPrompt?: string;
}

export default function Planning() {
  const { id } = useParams<{ id?: string }>();
  const navigate = useNavigate();
  const location = useLocation();
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const { subscribe } = useWebSocket();

  const [sessions, setSessions] = useState<PlanningSession[]>([]);
  const [captains, setCaptains] = useState<Captain[]>([]);
  const [fleets, setFleets] = useState<Fleet[]>([]);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [pipelines, setPipelines] = useState<Pipeline[]>([]);
  const [detail, setDetail] = useState<PlanningSessionDetail | null>(null);
  const [readiness, setReadiness] = useState<VesselReadinessResult | null>(null);
  const [loadingCatalog, setLoadingCatalog] = useState(true);
  const [loadingDetail, setLoadingDetail] = useState(false);
  const [loadingReadiness, setLoadingReadiness] = useState(false);
  const [error, setError] = useState('');

  const [title, setTitle] = useState('');
  const [captainId, setCaptainId] = useState('');
  const [fleetId, setFleetId] = useState('');
  const [vesselId, setVesselId] = useState('');
  const [pipelineId, setPipelineId] = useState('');
  const [objectiveId, setObjectiveId] = useState('');
  const [selectedPlaybooks, setSelectedPlaybooks] = useState<SelectedPlaybook[]>([]);
  const [composer, setComposer] = useState('');
  const [messageTools, setMessageTools] = useState<Record<string, ToolEvent[]>>({});
  const [messageThinking, setMessageThinking] = useState<Record<string, string>>({});
  const [streamingEnabled, setStreamingEnabled] = useState(true);
  const [showThinking, setShowThinking] = useState(false);
  const [selectedMessageId, setSelectedMessageId] = useState('');
  const [dispatchTitle, setDispatchTitle] = useState('');
  const [dispatchDescription, setDispatchDescription] = useState('');

  const [creating, setCreating] = useState(false);
  const [sending, setSending] = useState(false);
  const [summarizing, setSummarizing] = useState(false);
  const [dispatching, setDispatching] = useState(false);
  const [endingSessionId, setEndingSessionId] = useState<string | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [confirmEndOpen, setConfirmEndOpen] = useState(false);
  const [confirmDeleteOpen, setConfirmDeleteOpen] = useState(false);
  const [confirmDeleteAllOpen, setConfirmDeleteAllOpen] = useState(false);
  const [deletingAll, setDeletingAll] = useState(false);
  const [pendingEndSession, setPendingEndSession] = useState<PlanningSession | null>(null);

  const transcriptRef = useRef<HTMLDivElement | null>(null);
  const scrollToSessionRef = useRef(false);
  const [thinking, setThinking] = useState('');
  const dispatchSeedRef = useRef<DispatchSeedState | null>(null);
  const planningPrefillAppliedRef = useRef(false);

  const loadCatalog = useCallback(async () => {
    try {
      setLoadingCatalog(true);
      setError('');
      const [sessionItems, captainResult, fleetResult, vesselResult, pipelineResult] = await Promise.all([
        listPlanningSessions().catch(() => []),
        listCaptains({ pageSize: 9999 }).catch(() => null),
        listFleets({ pageSize: 9999 }).catch(() => null),
        listVessels({ pageSize: 9999 }).catch(() => null),
        listPipelines({ pageSize: 9999 }).catch(() => null),
      ]);

      setSessions(sessionItems);
      setCaptains(captainResult?.objects || []);
      setFleets(fleetResult?.objects || []);
      setVessels(vesselResult?.objects || []);
      setPipelines(pipelineResult?.objects || []);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to load planning data.'));
    } finally {
      setLoadingCatalog(false);
    }
  }, [t]);

  const loadDetail = useCallback(async (sessionId: string) => {
    try {
      setLoadingDetail(true);
      setError('');
      const result = await getPlanningSession(sessionId);
      setDetail(result);
      setSessions((current) => upsertSession(current, result.session));
    } catch (err: unknown) {
      setDetail(null);
      setError(err instanceof Error ? err.message : t('Failed to load planning session.'));
    } finally {
      setLoadingDetail(false);
    }
  }, [t]);

  useEffect(() => {
    loadCatalog();
  }, [loadCatalog]);

  useEffect(() => {
    setMessageTools({});
    setMessageThinking({});
    if (!id) {
      setDetail(null);
      setSelectedMessageId('');
      setDispatchTitle('');
      setDispatchDescription('');
      dispatchSeedRef.current = null;
      return;
    }

    loadDetail(id);
  }, [id, loadDetail]);

  useEffect(() => {
    const unsubscribe = subscribe((msg: WebSocketMessage) => {
      if (msg.type === 'planning-session.changed') {
        const payload = msg.data as { session?: PlanningSession } | undefined;
        if (!payload?.session) return;

        setSessions((current) => upsertSession(current, payload.session!));
        setDetail((current) => current && current.session.id === payload.session!.id
          ? { ...current, session: payload.session! }
          : current);
        return;
      }

      if (msg.type === 'captain.changed') {
        const payload = msg.data as { id?: string; name?: string; state?: string } | undefined;
        if (!payload?.id || !payload.state) return;
        const captainUpdate = {
          id: payload.id,
          name: payload.name,
          state: payload.state,
        };

        setCaptains((current) => mergeCaptainState(current, captainUpdate));
        setDetail((current) => {
          if (!current?.captain || current.captain.id !== captainUpdate.id) return current;
          return {
            ...current,
            captain: {
              ...current.captain,
              state: captainUpdate.state,
              name: captainUpdate.name ?? current.captain.name,
            },
          };
        });
        return;
      }

      if (msg.type === 'planning-session.message.created' || msg.type === 'planning-session.message.updated') {
        const payload = msg.data as { sessionId?: string; message?: PlanningSessionMessage } | undefined;
        if (!payload?.sessionId || !payload.message) return;

        setDetail((current) => {
          if (!current || current.session.id !== payload.sessionId) return current;
          return {
            ...current,
            messages: upsertMessage(current.messages, payload.message!),
          };
        });
        return;
      }

      if (msg.type === 'planning-session.tool') {
        const d = msg.data as ({ sessionId?: string; messageId?: string } & Parameters<typeof applyToolEvent>[1]) | undefined;
        if (!d || d.sessionId !== id || !d.messageId || !d.id) return;
        setMessageTools((current) => ({ ...current, [d.messageId!]: applyToolEvent(current[d.messageId!], d) }));
        return;
      }

      if (msg.type === 'planning-session.thinking') {
        const d = msg.data as { sessionId?: string; messageId?: string; delta?: string } | undefined;
        if (!d || d.sessionId !== id || !d.messageId || !d.delta) return;
        setMessageThinking((current) => ({ ...current, [d.messageId!]: (current[d.messageId!] ?? '') + d.delta }));
        return;
      }

      if (msg.type === 'planning-session.summary.created') {
        const payload = msg.data as PlanningSummaryEventPayload | undefined;
        if (!payload?.sessionId || payload.sessionId !== id || !payload.draft) return;

        if (payload.messageId) {
          setSelectedMessageId(payload.messageId);
        }
        setDispatchTitle(payload.draft.title || '');
        setDispatchDescription(payload.draft.description || '');
        dispatchSeedRef.current = {
          key: `${payload.sessionId}:${payload.messageId || 'latest'}`,
          title: payload.draft.title || '',
          description: payload.draft.description || '',
          source: 'summary',
        };
        return;
      }

      if (msg.type === 'planning-session.dispatch.created') {
        const payload = msg.data as { sessionId?: string; voyageId?: string } | undefined;
        if (!payload?.sessionId || payload.sessionId !== id || !payload.voyageId) return;
        pushToast('success', t('Dispatch created from this planning session.'));
        return;
      }

      if (msg.type === 'planning-session.deleted') {
        const payload = msg.data as { sessionId?: string } | undefined;
        if (!payload?.sessionId) return;

        setSessions((current) => removeSession(current, payload.sessionId!));
        if (payload.sessionId === id) {
          setDetail(null);
          setSelectedMessageId('');
          setDispatchTitle('');
          setDispatchDescription('');
          dispatchSeedRef.current = null;
          navigate('/planning');
          if (!deleting) {
            pushToast('warning', t('Planning session deleted.'));
          }
        }
      }
    });

    return unsubscribe;
  }, [deleting, id, navigate, pushToast, subscribe, t]);

  useEffect(() => {
    if (!detail) return;

    const latestAssistant = getLatestAssistantMessage(detail.messages);
    if (!latestAssistant) return;

    const selectedStillExists = detail.messages.some((message) => message.id === selectedMessageId);
    if (!selectedMessageId || !selectedStillExists) {
      setSelectedMessageId(latestAssistant.id);
    }
  }, [detail, selectedMessageId]);

  useEffect(() => {
    if (!detail) return;

    const selectedMessage = detail.messages.find((message) => message.id === selectedMessageId) || null;
    const nextSeed = resolveDispatchSeedUpdate({
      sessionId: detail.session.id,
      sessionTitle: detail.session.title,
      message: selectedMessage,
      currentTitle: dispatchTitle,
      currentDescription: dispatchDescription,
      previousSeed: dispatchSeedRef.current,
    });
    if (!nextSeed) return;

    dispatchSeedRef.current = nextSeed;
    if (nextSeed.title !== dispatchTitle) {
      setDispatchTitle(nextSeed.title);
    }
    if (nextSeed.description !== dispatchDescription) {
      setDispatchDescription(nextSeed.description);
    }
  }, [detail, dispatchDescription, dispatchTitle, selectedMessageId]);

  useEffect(() => {
    if (planningPrefillAppliedRef.current || id || loadingCatalog) return;

    const prefill = location.state as PlanningPrefillState | null;
    if (!prefill?.fromWorkspace && !prefill?.fromIncident && !prefill?.fromObjective && !prefill?.fromSetupWizard && !prefill?.captainId) return;

    if (prefill.title) setTitle(prefill.title);
    if (prefill.captainId) setCaptainId(prefill.captainId);
    if (prefill.fleetId) setFleetId(prefill.fleetId);
    if (prefill.vesselId) setVesselId(prefill.vesselId);
    if (prefill.pipelineId) setPipelineId(prefill.pipelineId);
    if (prefill.initialPrompt) setComposer(prefill.initialPrompt);
    if (prefill.objectiveId) setObjectiveId(prefill.objectiveId);

    planningPrefillAppliedRef.current = true;
  }, [id, loadingCatalog, location.state]);

  useEffect(() => {
    if (!transcriptRef.current) return;
    transcriptRef.current.scrollTop = transcriptRef.current.scrollHeight;
  }, [detail?.messages]);

  useEffect(() => {
    if (!fleetId || !vesselId || loadingCatalog) return;

    const selectedVessel = vessels.find((vessel) => vessel.id === vesselId);
    if (!selectedVessel) return;

    if (selectedVessel.fleetId !== fleetId) {
      setVesselId('');
    }
  }, [fleetId, loadingCatalog, vesselId, vessels]);

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

  const availableVessels = useMemo(() => {
    return fleetId ? vessels.filter((vessel) => vessel.fleetId === fleetId) : vessels;
  }, [fleetId, vessels]);

  const selectedCaptain = useMemo(
    () => captains.find((captain) => captain.id === captainId) || null,
    [captainId, captains],
  );
  const captainNameById = useMemo(
    () => new Map(captains.map((captain) => [captain.id, captain.name])),
    [captains],
  );
  const vesselNameById = useMemo(
    () => new Map(vessels.map((vessel) => [vessel.id, vessel.name])),
    [vessels],
  );
  const pipelineNameById = useMemo(
    () => new Map(pipelines.map((pipeline) => [pipeline.id, pipeline.name])),
    [pipelines],
  );

  function resolveCaptainName(currentCaptainId: string) {
    return captainNameById.get(currentCaptainId) || currentCaptainId;
  }

  function resolveVesselName(currentVesselId: string) {
    return vesselNameById.get(currentVesselId) || currentVesselId;
  }

  function resolvePipelineName(currentPipelineId: string | null) {
    return (currentPipelineId && pipelineNameById.get(currentPipelineId)) || currentPipelineId || '-';
  }

  const currentSession = detail?.session || null;
  const currentMessages = detail?.messages || [];
  const selectedMessage = currentMessages.find((message) => message.id === selectedMessageId) || null;
  const sessionStopping = currentSession?.status === 'Stopping';
  const canStartSession = canCaptainStartPlanning(selectedCaptain) && !!vesselId && !creating;
  const canSend = currentSession?.status === 'Active' && composer.trim().length > 0 && !sending;
  const canEndSession = !!currentSession && ['Active', 'Responding'].includes(currentSession.status) && !endingSessionId;
  const canSummarize = !!currentSession && !!selectedMessage?.content.trim() && !summarizing;
  const canOpenInDispatch = !!currentSession && dispatchDescription.trim().length > 0;
  const canDispatch = !!currentSession && !!selectedMessage?.content.trim() && dispatchDescription.trim().length > 0 && !dispatching;
  const planningPrefill = location.state as PlanningPrefillState | null;

  // After starting a session, jump down to the Current Session chat window once it loads.
  useEffect(() => {
    if (detail && scrollToSessionRef.current) {
      scrollToSessionRef.current = false;
      requestAnimationFrame(() => requestAnimationFrame(() => {
        transcriptRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' });
      }));
    }
  }, [detail]);

  // Rotate the "waiting" message every 4 seconds while the captain is responding.
  useEffect(() => {
    if (currentSession?.status !== 'Responding') return;
    setThinking((prev) => randomThinkingMessage(prev));
    const timer = window.setInterval(() => setThinking((prev) => randomThinkingMessage(prev)), 4000);
    return () => window.clearInterval(timer);
  }, [currentSession?.status]);

  async function handleCreateSession() {
    if (!captainId || !vesselId) return;

    try {
      setCreating(true);
      setError('');
      const result = await createPlanningSession({
        title: title.trim() || undefined,
        captainId,
        vesselId,
        fleetId: fleetId || undefined,
        pipelineId: pipelineId || undefined,
        selectedPlaybooks,
        objectiveId: objectiveId || undefined,
      });

      setSessions((current) => upsertSession(current, result.session));
      pushToast('success', t('Planning session started.'));
      scrollToSessionRef.current = true;
      navigate(`/planning/${result.session.id}`, {
        state: composer.trim() ? { fromWorkspace: true, initialPrompt: composer.trim() } : undefined,
      });
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : t('Failed to start planning session.');
      if (err instanceof TimeoutError) {
        await loadCatalog();
        setError(t('Starting the planning session is taking longer than expected. Armada is still provisioning the dock and worktree. If setup completes, the session will appear in the list on the left.'));
      } else {
        setError(message);
      }
    } finally {
      setCreating(false);
    }
  }

  async function handleSendMessage() {
    if (!currentSession || !composer.trim()) return;

    try {
      setSending(true);
      setError('');
      const result = await sendPlanningSessionMessage(currentSession.id, { content: composer.trim(), showThinking, stream: streamingEnabled });
      setComposer('');
      setDetail(result);
      setSessions((current) => upsertSession(current, result.session));
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to send message.'));
    } finally {
      setSending(false);
    }
  }

  async function handleStopTurn() {
    if (!currentSession) return;

    try {
      setError('');
      const result = await stopPlanningTurn(currentSession.id);
      setDetail(result);
      setSessions((current) => upsertSession(current, result.session));
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to stop the current turn.'));
    }
  }

  async function handleSummarize() {
    if (!currentSession || !selectedMessageId) return;

    try {
      setSummarizing(true);
      setError('');
      const result = await summarizePlanningSession(currentSession.id, {
        messageId: selectedMessageId,
        title: dispatchTitle.trim() || undefined,
      });
      setDispatchTitle(result.title);
      setDispatchDescription(result.description);
      dispatchSeedRef.current = {
        key: `${result.sessionId}:${result.messageId}`,
        title: result.title,
        description: result.description,
        source: 'summary',
      };
      pushToast('success', t('Dispatch draft summarized from planning output.'));
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to summarize planning output.'));
    } finally {
      setSummarizing(false);
    }
  }

  // Release the reserved captain and dock once the operator commits planning output toward dispatch. The
  // planning transcript is preserved (only the captain/dock are freed), and a release failure must not block
  // the dispatch handoff, so this is best-effort.
  async function releaseSessionOnDispatch() {
    if (!currentSession) return;
    try {
      const result = await stopPlanningSession(currentSession.id);
      setSessions((current) => upsertSession(current, result.session));
    } catch {
      // Best-effort: the operator can still end the session by hand if the automatic release fails.
    }
  }

  async function handleOpenInDispatch() {
    if (!currentSession || !dispatchDescription.trim()) return;

    const state = {
      fromPlanning: true,
      vesselId: currentSession.vesselId,
      pipelineName: pipelines.find((pipeline) => pipeline.id === currentSession.pipelineId)?.name,
      selectedPlaybooks: currentSession.selectedPlaybooks || [],
      prompt: dispatchDescription.trim(),
      voyageTitle: dispatchTitle.trim() || undefined,
    };
    await releaseSessionOnDispatch();
    navigate('/dispatch', { state });
  }

  // Send a specific assistant reply straight to the main Dispatch page, carrying its full text as the prompt.
  async function handleOpenMessageInDispatch(messageId: string) {
    if (!currentSession) return;

    const message = currentMessages.find((item) => item.id === messageId);
    const prompt = message?.content.trim();
    if (!prompt) return;

    const state = {
      fromPlanning: true,
      vesselId: currentSession.vesselId,
      pipelineName: pipelines.find((pipeline) => pipeline.id === currentSession.pipelineId)?.name,
      selectedPlaybooks: currentSession.selectedPlaybooks || [],
      prompt,
      voyageTitle: dispatchTitle.trim() || currentSession.title || undefined,
    };
    await releaseSessionOnDispatch();
    navigate('/dispatch', { state });
  }

  async function handleDispatch() {
    if (!currentSession || !dispatchDescription.trim()) return;

    try {
      setDispatching(true);
      setError('');
      const voyage = await dispatchPlanningSession(currentSession.id, {
        messageId: selectedMessageId || undefined,
        title: dispatchTitle.trim() || undefined,
        description: dispatchDescription.trim(),
      });

      pushToast('success', t('Dispatch created from planning session.'));
      navigate(`/voyages/${voyage.id}`);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to dispatch from planning session.'));
    } finally {
      setDispatching(false);
    }
  }

  function requestEndSession(session: PlanningSession) {
    setPendingEndSession(session);
    setConfirmEndOpen(true);
  }

  async function handleEndSession() {
    if (!pendingEndSession) return;

    const targetSessionId = pendingEndSession.id;
    try {
      setEndingSessionId(targetSessionId);
      setError('');
      const result = await stopPlanningSession(targetSessionId);
      if (currentSession?.id === targetSessionId) {
        setDetail(result);
      }
      setSessions((current) => upsertSession(current, result.session));
      pushToast('warning', t('Planning session is ending.'));
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to end planning session.'));
    } finally {
      setEndingSessionId(null);
      setPendingEndSession(null);
    }
  }

  async function handleDeleteSession() {
    if (!currentSession) return;

    try {
      setDeleting(true);
      setError('');
      await deletePlanningSession(currentSession.id);
      setSessions((current) => removeSession(current, currentSession.id));
      setDetail(null);
      setSelectedMessageId('');
      setDispatchTitle('');
      setDispatchDescription('');
      dispatchSeedRef.current = null;
      navigate('/planning');
      pushToast('warning', t('Planning session deleted.'));
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to delete planning session.'));
    } finally {
      setDeleting(false);
    }
  }

  async function handleDeleteSessionById(session: PlanningSession) {
    try {
      setError('');
      await deletePlanningSession(session.id);
      setSessions((current) => removeSession(current, session.id));
      if (currentSession?.id === session.id) {
        setDetail(null);
        setSelectedMessageId('');
        navigate('/planning');
      }
      pushToast('warning', t('Planning session deleted.'));
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to delete planning session.'));
    }
  }

  async function handleDeleteAllSessions() {
    const targets = sessions.slice();
    if (targets.length === 0) return;

    try {
      setDeletingAll(true);
      setError('');

      // Delete each session independently so one failure (e.g. a session that cannot be deleted while
      // running) does not abort the rest; count failures and report them.
      let failures = 0;
      for (const session of targets) {
        try {
          await deletePlanningSession(session.id);
          setSessions((current) => removeSession(current, session.id));
        } catch {
          failures += 1;
        }
      }

      // Delete-all removes the open session too; reset the workspace back to the empty state.
      setDetail(null);
      setSelectedMessageId('');
      setDispatchTitle('');
      setDispatchDescription('');
      dispatchSeedRef.current = null;
      navigate('/planning');

      if (failures > 0) {
        pushToast('warning', t('{{count}} planning session(s) could not be deleted (they may still be running).', { count: failures }));
      } else {
        pushToast('warning', t('All planning sessions deleted.'));
      }
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to delete planning sessions.'));
    } finally {
      setDeletingAll(false);
    }
  }

  return (
    <div>
      <PageHeader
        title={t('Planning')}
        subtitle={t('Chat with a captain against a specific vessel, preserve the transcript, and dispatch directly from the planning output.')}
      />

      {error && (
        <div className="alert alert-error" style={{ marginBottom: '1rem' }}>
          {error}
        </div>
      )}

      {!id && (planningPrefill?.fromWorkspace || planningPrefill?.fromIncident || planningPrefill?.fromObjective || planningPrefill?.fromSetupWizard) && (
        <div className="alert" style={{ marginBottom: '1rem' }}>
          {planningPrefill?.fromObjective
            ? t('Prefilled from a backlog item. Review the vessel and prompt, then start a planning session to turn that scoped work into an execution plan.')
            : planningPrefill?.fromIncident
            ? t('Prefilled from an incident. Review the vessel and prompt, then start a planning session for the hotfix or recovery path.')
            : planningPrefill?.fromSetupWizard
            ? t('Prefilled from the setup wizard. Review the vessel and prompt, then start a planning session to turn onboarding follow-up into an execution plan.')
            : t('Prefilled from Workspace. Review the vessel and prompt, then start a planning session.')}
          {objectiveId && (
            <>
              {' '}
              <button type="button" className="btn btn-sm" style={{ marginLeft: '0.75rem' }} onClick={() => navigate(`/backlog/${objectiveId}`)}>
                {t('Open Backlog Item')}
              </button>
            </>
          )}
        </div>
      )}

      <PlanningStartCard
        t={t}
        loading={loadingCatalog}
        captains={captains}
        fleets={fleets}
        vessels={vessels}
        pipelines={pipelines}
        title={title}
        captainId={captainId}
        fleetId={fleetId}
        vesselId={vesselId}
        pipelineId={pipelineId}
        availableVessels={availableVessels}
        selectedCaptain={selectedCaptain}
        selectedPlaybooks={selectedPlaybooks}
        pendingInitialPrompt={composer}
        creating={creating}
        canStartSession={canStartSession}
        onTitleChange={setTitle}
        onCaptainChange={setCaptainId}
        onFleetChange={setFleetId}
        onVesselChange={setVesselId}
        onPipelineChange={setPipelineId}
        onSelectedPlaybooksChange={setSelectedPlaybooks}
        onStart={handleCreateSession}
      />

      {vesselId && (
        <ReadinessPanel
          title={t('Vessel Readiness')}
          readiness={readiness}
          loading={loadingReadiness}
          emptyMessage={t('Select a vessel to inspect readiness.')}
          compact
        />
      )}

      <div className="planning-section-stack">
        <PlanningSessionListCard
          t={t}
          sessions={sessions}
          activeSessionId={id}
          endingSessionId={endingSessionId}
          formatRelativeTime={formatRelativeTime}
          resolveCaptainName={resolveCaptainName}
          resolveVesselName={resolveVesselName}
          resolvePipelineName={resolvePipelineName}
          onSelect={(sessionId) => navigate(`/planning/${sessionId}`)}
          onEndSession={requestEndSession}
          onDeleteSession={handleDeleteSessionById}
          onDeleteAll={() => setConfirmDeleteAllOpen(true)}
          deletingAll={deletingAll}
        />

        {!id ? (
          <div className="card" style={{ padding: '1.25rem' }}>
            <h3>{t('Current Session')}</h3>
            <p className="text-muted">
              {t('Choose an existing planning session from the table above, or start a new one to begin chatting with a captain.')}
            </p>
          </div>
        ) : loadingDetail ? (
          <div className="card" style={{ padding: '1.25rem' }}>
            <p className="text-muted">{t('Loading planning session...')}</p>
          </div>
        ) : !detail ? (
          <div className="card" style={{ padding: '1.25rem' }}>
            <p className="text-muted">{t('Planning session not found.')}</p>
          </div>
        ) : (
          <>
            <PlanningTranscriptCard
              t={t}
              transcriptRef={transcriptRef}
              title={detail.session.title}
              captainName={detail.captain?.name || detail.session.captainId}
              captainRuntime={detail.captain?.runtime || '-'}
              vesselName={detail.vessel?.name || detail.session.vesselId}
              branchName={detail.session.branchName}
              pipelineName={resolvePipelineName(detail.session.pipelineId)}
              playbookCount={detail.session.selectedPlaybooks?.length || 0}
              currentStatus={currentSession?.status}
              failureReason={detail.session.failureReason}
              updatedUtc={detail.session.lastUpdateUtc}
              messages={currentMessages}
              messageTools={messageTools}
              messageThinking={messageThinking}
              thinkingMessage={thinking}
              streamingEnabled={streamingEnabled}
              onStreamingChange={setStreamingEnabled}
              showThinking={showThinking}
              onShowThinkingChange={setShowThinking}
              composer={composer}
              sending={sending}
              canSend={canSend}
              canEndSession={canEndSession}
              endingSession={(endingSessionId === currentSession?.id) || sessionStopping}
              deleting={deleting}
              formatDateTime={formatDateTime}
              formatRelativeTime={formatRelativeTime}
              onOpenMessageInDispatch={handleOpenMessageInDispatch}
              onComposerChange={setComposer}
              onSend={handleSendMessage}
              onStopTurn={handleStopTurn}
              onEndSession={() => currentSession && requestEndSession(currentSession)}
              onDelete={() => setConfirmDeleteOpen(true)}
            />

            <PlanningDispatchCard
              t={t}
              selectedMessage={selectedMessage}
              dispatchTitle={dispatchTitle}
              dispatchDescription={dispatchDescription}
              canSummarize={canSummarize}
              canOpenInDispatch={canOpenInDispatch}
              canDispatch={canDispatch}
              summarizing={summarizing}
              dispatching={dispatching}
              onDispatchTitleChange={setDispatchTitle}
              onDispatchDescriptionChange={setDispatchDescription}
              onSummarize={handleSummarize}
              onOpenInDispatch={handleOpenInDispatch}
              onDispatch={handleDispatch}
            />
          </>
        )}
      </div>

      <ConfirmDialog
        open={confirmEndOpen}
        title={t('End Planning Session')}
        message={t('End this planning session and release the reserved captain and dock? The transcript will be kept until you delete it or it expires under server retention settings.')}
        confirmLabel={t('End Session')}
        cancelLabel={t('Cancel')}
        onConfirm={() => {
          setConfirmEndOpen(false);
          void handleEndSession();
        }}
        onCancel={() => {
          setConfirmEndOpen(false);
          setPendingEndSession(null);
        }}
      />

      <ConfirmDialog
        open={confirmDeleteOpen}
        title={t('Delete Planning Session')}
        message={t('Delete this planning session and its transcript?')}
        confirmLabel={t('Delete Session')}
        cancelLabel={t('Cancel')}
        danger
        onConfirm={() => {
          setConfirmDeleteOpen(false);
          void handleDeleteSession();
        }}
        onCancel={() => setConfirmDeleteOpen(false)}
      />

      <ConfirmDialog
        open={confirmDeleteAllOpen}
        title={t('Delete All Planning Sessions')}
        message={t('Delete all {{count}} planning session(s) and their transcripts? This cannot be undone.', { count: sessions.length })}
        confirmLabel={t('Delete All')}
        cancelLabel={t('Cancel')}
        danger
        requireDeleteConfirm
        onConfirm={() => {
          setConfirmDeleteAllOpen(false);
          void handleDeleteAllSessions();
        }}
        onCancel={() => setConfirmDeleteAllOpen(false)}
      />
    </div>
  );
}
