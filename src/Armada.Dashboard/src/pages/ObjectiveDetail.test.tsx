import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import ObjectiveDetail from './ObjectiveDetail';
import {
  getBacklogItem,
  listBacklog,
  listBacklogRefinementSessions,
  listCaptains,
  listFleets,
  listPipelines,
  listVessels,
  updateBacklogItem,
} from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import type { Objective, WebSocketMessage } from '../types/models';

const socket = vi.hoisted(() => {
  const handlers = new Set<(message: { type: string; data?: unknown }) => void>();
  const subscribe = (handler: (message: { type: string; data?: unknown }) => void) => {
    handlers.add(handler);
    return () => { handlers.delete(handler); };
  };
  return { handlers, subscribe };
});

vi.mock('../api/client', () => ({
  applyObjectiveRefinementSummary: vi.fn(),
  createBacklogItem: vi.fn(),
  createBacklogRefinementSession: vi.fn(),
  deleteBacklogItem: vi.fn(),
  deleteObjectiveRefinementSession: vi.fn(),
  getBacklogItem: vi.fn(),
  getObjectiveRefinementSession: vi.fn(),
  importObjectiveFromGitHub: vi.fn(),
  listBacklog: vi.fn(),
  listBacklogRefinementSessions: vi.fn(),
  listCaptains: vi.fn(),
  listFleets: vi.fn(),
  listPipelines: vi.fn(),
  listVessels: vi.fn(),
  sendObjectiveRefinementMessage: vi.fn(),
  stopObjectiveRefinementSession: vi.fn(),
  summarizeObjectiveRefinementSession: vi.fn(),
  updateBacklogItem: vi.fn(),
}));

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast: vi.fn() }) }));
vi.mock('../context/AuthContext', () => ({ useAuth: () => ({ isAdmin: true, isTenantAdmin: true }) }));
vi.mock('../context/WebSocketContext', () => ({
  useWebSocket: () => ({
    connected: true,
    reconnectCount: 0,
    send: vi.fn(),
    subscribe: socket.subscribe,
  }),
}));

const TITLE_LABEL = 'Short headline for the work, shown in backlog lists and reused as the default planning, voyage and release title. Required. Example: Add SSO login.';

function page<T>(objects: T[]) {
  return { success: true, pageNumber: 1, pageSize: 1000, totalPages: 1, totalRecords: objects.length, totalMs: 1, objects };
}

function objective(overrides: Partial<Objective> = {}): Objective {
  return {
    id: 'obj_1',
    tenantId: null,
    userId: null,
    title: 'Original title',
    description: 'Original description',
    status: 'Draft',
    kind: 'Feature',
    category: null,
    priority: 'P2',
    rank: 1,
    backlogState: 'Inbox',
    effort: 'M',
    owner: null,
    targetVersion: null,
    dueUtc: null,
    parentObjectiveId: null,
    blockedByObjectiveIds: [],
    refinementSummary: null,
    suggestedPipelineId: null,
    suggestedPlaybooks: [],
    refinementSessionIds: [],
    sourceProvider: null,
    sourceType: null,
    sourceId: null,
    sourceUrl: null,
    sourceUpdatedUtc: null,
    tags: [],
    acceptanceCriteria: [],
    nonGoals: [],
    rolloutConstraints: [],
    evidenceLinks: [],
    fleetIds: [],
    vesselIds: [],
    planningSessionIds: [],
    voyageIds: [],
    missionIds: [],
    checkRunIds: [],
    releaseIds: [],
    deploymentIds: [],
    incidentIds: [],
    createdUtc: '2026-10-27T00:00:00Z',
    lastUpdateUtc: '2026-10-27T00:00:00Z',
    completedUtc: null,
    ...overrides,
  };
}

function emit(message: WebSocketMessage) {
  act(() => { socket.handlers.forEach((handler) => handler(message)); });
}

async function renderLoaded(): Promise<HTMLElement> {
  render(
    <MemoryRouter initialEntries={['/backlog/obj_1']}>
      <Routes>
        <Route path="/backlog/:id" element={<ObjectiveDetail />} />
      </Routes>
    </MemoryRouter>,
  );
  const title = await screen.findByRole('textbox', { name: TITLE_LABEL });
  expect(title).toHaveValue('Original title');
  expect(socket.handlers.size).toBeGreaterThan(0);
  return title;
}

describe('ObjectiveDetail live updates', () => {
  beforeEach(() => {
    socket.handlers.clear();
    vi.mocked(getBacklogItem).mockResolvedValue(objective());
    vi.mocked(listBacklogRefinementSessions).mockResolvedValue(page([]) as never);
    vi.mocked(listFleets).mockResolvedValue(page([]) as never);
    vi.mocked(listVessels).mockResolvedValue(page([]) as never);
    vi.mocked(listCaptains).mockResolvedValue(page([]) as never);
    vi.mocked(listPipelines).mockResolvedValue(page([]) as never);
    vi.mocked(listBacklog).mockResolvedValue(page([]) as never);
  });

  it('keeps an edited title when objective.changed arrives and reloads the server copy on request', async () => {
    const title = await renderLoaded();
    fireEvent.change(title, { target: { value: 'Edited title' } });

    emit({ type: 'objective.changed', data: objective({ title: 'Server title' }) });

    expect(title).toHaveValue('Edited title');
    expect(screen.getByRole('status')).toHaveTextContent('A newer copy of this backlog item is on the server.');

    fireEvent.click(screen.getByRole('button', { name: 'Reload server copy' }));
    expect(title).toHaveValue('Server title');
    expect(screen.queryByRole('status')).toBeNull();
  });

  it('updates a clean title when objective.changed arrives', async () => {
    const title = await renderLoaded();

    emit({ type: 'objective.changed', data: objective({ title: 'Server title' }) });

    expect(title).toHaveValue('Server title');
    expect(screen.queryByRole('button', { name: 'Reload server copy' })).toBeNull();
  });

  it('keeps an edited title when an applied refinement event arrives and reloads that copy on request', async () => {
    const title = await renderLoaded();
    fireEvent.change(title, { target: { value: 'Edited title' } });

    emit({
      type: 'objective-refinement-session.applied',
      data: { objective: objective({ title: 'Applied title' }) },
    });

    expect(title).toHaveValue('Edited title');
    expect(screen.getByRole('status')).toHaveTextContent('A newer copy of this backlog item is on the server.');

    fireEvent.click(screen.getByRole('button', { name: 'Reload server copy' }));
    expect(title).toHaveValue('Applied title');
  });

  it('updates a clean title when an applied refinement event arrives', async () => {
    const title = await renderLoaded();

    emit({
      type: 'objective-refinement-session.applied',
      data: { objective: objective({ title: 'Applied title' }) },
    });

    expect(title).toHaveValue('Applied title');
  });

  it('applies a later live update after a successful save', async () => {
    const title = await renderLoaded();
    fireEvent.change(title, { target: { value: 'Saved title' } });
    vi.mocked(updateBacklogItem).mockResolvedValue(objective({ title: 'Saved title' }));

    fireEvent.click(screen.getByRole('button', { name: 'Save Changes' }));
    await waitFor(() => expect(title).toHaveValue('Saved title'));

    emit({ type: 'objective.changed', data: objective({ title: 'Server title' }) });

    expect(title).toHaveValue('Server title');
  });
});
