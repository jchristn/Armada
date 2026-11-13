import { buildDispatchSeed, getLatestAssistantMessage, mergeCaptainState, removeSession, resolveDispatchSeedUpdate, upsertMessage, upsertSession } from './planningUtils';
import type { Captain, PlanningSession, PlanningSessionMessage } from '../../types/models';

function session(id: string, lastUpdateUtc: string): PlanningSession {
  return {
    id,
    tenantId: null,
    userId: null,
    captainId: 'cpt_123',
    vesselId: 'vsl_123',
    fleetId: null,
    dockId: null,
    branchName: null,
    title: `Session ${id}`,
    status: 'Active',
    pipelineId: null,
    processId: null,
    failureReason: null,
    createdUtc: lastUpdateUtc,
    startedUtc: lastUpdateUtc,
    completedUtc: null,
    lastUpdateUtc,
    selectedPlaybooks: [],
  };
}

function message(id: string, role: string, sequence: number, content: string): PlanningSessionMessage {
  return {
    id,
    planningSessionId: 'psn_123',
    tenantId: null,
    userId: null,
    role,
    sequence,
    content,
    isSelectedForDispatch: false,
    createdUtc: '2026-04-29T00:00:00Z',
    lastUpdateUtc: '2026-04-29T00:00:00Z',
  };
}

function captain(id: string, state: string, name = `Captain ${id}`): Captain {
  return {
    id,
    tenantId: null,
    name,
    runtime: 'Codex',
    supportsPlanningSessions: true,
    planningSessionSupportReason: null,
    systemInstructions: null,
    model: null,
    allowedPersonas: null,
    preferredPersona: null,
    state,
    processId: null,
    recoveryAttempts: 0,
    lastHeartbeatUtc: null,
    createdUtc: '2026-04-29T00:00:00Z',
    lastUpdateUtc: '2026-04-29T00:00:00Z',
  };
}

describe('planningUtils', () => {
  it('keeps sessions sorted newest-first when upserting and removing', () => {
    const sessions = upsertSession(
      [session('psn_old', '2026-04-29T00:00:00Z')],
      session('psn_new', '2026-04-29T01:00:00Z'),
    );

    expect(sessions.map((item) => item.id)).toEqual(['psn_new', 'psn_old']);
    expect(removeSession(sessions, 'psn_new').map((item) => item.id)).toEqual(['psn_old']);
  });

  it('tracks the latest assistant output and seeds a dispatch draft from it', () => {
    const messages = [
      message('psm_1', 'Assistant', 1, 'Older output'),
      message('psm_2', 'Assistant', 2, ''),
      message('psm_3', 'Assistant', 3, 'Latest usable output'),
    ];

    const latest = getLatestAssistantMessage(messages);
    expect(latest?.id).toBe('psm_3');

    const seededMessages = upsertMessage(messages, message('psm_2', 'Assistant', 2, 'Updated middle output'));
    expect(seededMessages[1].content).toBe('Updated middle output');

    expect(buildDispatchSeed('Planning Title', latest)).toEqual({
      title: 'Planning Title',
      description: 'Latest usable output',
    });
  });

  it('refreshes the auto-seeded draft while a selected assistant message streams, but preserves manual edits and summaries', () => {
    const assistant = message('psm_stream', 'Assistant', 3, 'Reading additional input from stdin...');

    expect(resolveDispatchSeedUpdate({
      sessionId: 'psn_123',
      sessionTitle: 'Planning Title',
      message: assistant,
      currentTitle: '',
      currentDescription: '',
      previousSeed: null,
    })).toEqual({
      key: 'psn_123:psm_stream',
      title: 'Planning Title',
      description: 'Reading additional input from stdin...',
      source: 'auto',
    });

    expect(resolveDispatchSeedUpdate({
      sessionId: 'psn_123',
      sessionTitle: 'Planning Title',
      message: message('psm_stream', 'Assistant', 3, 'Final streamed reply'),
      currentTitle: 'Planning Title',
      currentDescription: 'Reading additional input from stdin...',
      previousSeed: {
        key: 'psn_123:psm_stream',
        title: 'Planning Title',
        description: 'Reading additional input from stdin...',
        source: 'auto',
      },
    })).toEqual({
      key: 'psn_123:psm_stream',
      title: 'Planning Title',
      description: 'Final streamed reply',
      source: 'auto',
    });

    expect(resolveDispatchSeedUpdate({
      sessionId: 'psn_123',
      sessionTitle: 'Planning Title',
      message: message('psm_stream', 'Assistant', 3, 'Final streamed reply'),
      currentTitle: 'Custom Title',
      currentDescription: 'Hand-edited draft',
      previousSeed: {
        key: 'psn_123:psm_stream',
        title: 'Planning Title',
        description: 'Reading additional input from stdin...',
        source: 'auto',
      },
    })).toEqual({
      key: 'psn_123:psm_stream',
      title: 'Custom Title',
      description: 'Hand-edited draft',
      source: 'auto',
    });

    expect(resolveDispatchSeedUpdate({
      sessionId: 'psn_123',
      sessionTitle: 'Planning Title',
      message: message('psm_stream', 'Assistant', 3, 'Final streamed reply'),
      currentTitle: 'Summary Title',
      currentDescription: 'Summarized draft',
      previousSeed: {
        key: 'psn_123:psm_stream',
        title: 'Summary Title',
        description: 'Summarized draft',
        source: 'summary',
      },
    })).toBeNull();
  });

  it('merges captain state updates from websocket events without reloading the list', () => {
    expect(mergeCaptainState(
      [captain('cpt_idle', 'Idle'), captain('cpt_busy', 'Working', 'Worker')],
      { id: 'cpt_idle', state: 'Planning', name: 'Planner' },
    )).toEqual([
      captain('cpt_idle', 'Planning', 'Planner'),
      captain('cpt_busy', 'Working', 'Worker'),
    ]);
  });
});
