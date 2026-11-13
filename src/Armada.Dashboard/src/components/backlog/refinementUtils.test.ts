import { describe, expect, it } from 'vitest';
import {
  getLatestAssistantRefinementMessage,
  mergeCaptainState,
  removeRefinementSession,
  upsertRefinementMessage,
  upsertRefinementSession,
} from './refinementUtils';
import type { Captain, ObjectiveRefinementMessage, ObjectiveRefinementSession } from '../../types/models';

function createSession(overrides: Partial<ObjectiveRefinementSession> = {}): ObjectiveRefinementSession {
  return {
    id: 'ref_123',
    objectiveId: 'obj_123',
    tenantId: 'ten_123',
    userId: 'usr_123',
    captainId: 'cpt_123',
    fleetId: null,
    vesselId: null,
    title: 'Refinement Session',
    status: 'Active',
    processId: null,
    failureReason: null,
    createdUtc: '2026-05-01T00:00:00Z',
    startedUtc: '2026-05-01T00:01:00Z',
    completedUtc: null,
    lastUpdateUtc: '2026-05-01T00:02:00Z',
    ...overrides,
  };
}

function createMessage(overrides: Partial<ObjectiveRefinementMessage> = {}): ObjectiveRefinementMessage {
  return {
    id: 'msg_123',
    objectiveRefinementSessionId: 'ref_123',
    objectiveId: 'obj_123',
    tenantId: 'ten_123',
    userId: 'usr_123',
    role: 'User',
    sequence: 1,
    content: 'Refine the backlog item.',
    isSelected: false,
    createdUtc: '2026-05-01T00:00:00Z',
    lastUpdateUtc: '2026-05-01T00:00:00Z',
    ...overrides,
  };
}

describe('refinementUtils', () => {
  it('upserts and removes refinement sessions in newest-first order', () => {
    const older = createSession({ id: 'ref_old', lastUpdateUtc: '2026-05-01T00:00:00Z' });
    const newer = createSession({ id: 'ref_new', lastUpdateUtc: '2026-05-02T00:00:00Z' });
    const updated = createSession({ id: 'ref_old', title: 'Updated session', lastUpdateUtc: '2026-05-03T00:00:00Z' });

    const inserted = upsertRefinementSession([older], newer);
    expect(inserted.map((session) => session.id)).toEqual(['ref_new', 'ref_old']);

    const replaced = upsertRefinementSession(inserted, updated);
    expect(replaced.map((session) => session.id)).toEqual(['ref_old', 'ref_new']);
    expect(replaced[0].title).toBe('Updated session');

    expect(removeRefinementSession(replaced, 'ref_new').map((session) => session.id)).toEqual(['ref_old']);
  });

  it('upserts messages by sequence and finds the latest non-empty assistant message', () => {
    const userMessage = createMessage({ id: 'msg_user', role: 'User', sequence: 1 });
    const emptyAssistant = createMessage({ id: 'msg_empty', role: 'Assistant', sequence: 2, content: '   ' });
    const assistant = createMessage({ id: 'msg_assistant', role: 'Assistant', sequence: 3, content: 'Tighten rollout constraints.' });

    const messages = upsertRefinementMessage([assistant], userMessage);
    const sorted = upsertRefinementMessage(messages, emptyAssistant);
    expect(sorted.map((message) => message.id)).toEqual(['msg_user', 'msg_empty', 'msg_assistant']);
    expect(getLatestAssistantRefinementMessage(sorted)?.id).toBe('msg_assistant');
  });

  it('merges captain state updates without disturbing unrelated captains', () => {
    const captains: Captain[] = [
      {
        id: 'cpt_123',
        tenantId: 'ten_123',
        name: 'Captain A',
        runtime: 'Codex',
        supportsPlanningSessions: true,
        planningSessionSupportReason: null,
        systemInstructions: null,
        model: null,
        allowedPersonas: null,
        preferredPersona: null,
        state: 'Idle',
        processId: null,
        recoveryAttempts: 0,
        lastHeartbeatUtc: null,
        createdUtc: '2026-05-01T00:00:00Z',
        lastUpdateUtc: '2026-05-01T00:00:00Z',
      },
      {
        id: 'cpt_456',
        tenantId: 'ten_123',
        name: 'Captain B',
        runtime: 'Codex',
        supportsPlanningSessions: true,
        planningSessionSupportReason: null,
        systemInstructions: null,
        model: null,
        allowedPersonas: null,
        preferredPersona: null,
        state: 'Idle',
        processId: null,
        recoveryAttempts: 0,
        lastHeartbeatUtc: null,
        createdUtc: '2026-05-01T00:00:00Z',
        lastUpdateUtc: '2026-05-01T00:00:00Z',
      },
    ];

    const updated = mergeCaptainState(captains, { id: 'cpt_123', state: 'Refining', name: 'Captain Alpha' });
    expect(updated[0].state).toBe('Refining');
    expect(updated[0].name).toBe('Captain Alpha');
    expect(updated[1].state).toBe('Idle');
  });
});
