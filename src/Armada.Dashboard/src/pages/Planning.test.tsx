import { act, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import Planning from './Planning';
import { getPlanningSession, listCaptains, listFleets, listPipelines, listPlanningSessions, listVessels, sendPlanningSessionMessage } from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import type { PlanningSession, PlanningSessionDetail, PlanningSessionMessage, WebSocketMessage } from '../types/models';

vi.mock('../api/client', () => ({
  createPlanningSession: vi.fn(),
  deletePlanningSession: vi.fn(),
  dispatchPlanningSession: vi.fn(),
  getVesselReadiness: vi.fn().mockResolvedValue(null),
  getPlanningSession: vi.fn(),
  listCaptains: vi.fn(),
  listFleets: vi.fn(),
  listPipelines: vi.fn(),
  listPlanningSessions: vi.fn(),
  listVessels: vi.fn(),
  sendPlanningSessionMessage: vi.fn(),
  stopPlanningSession: vi.fn(),
  stopPlanningTurn: vi.fn(),
  summarizePlanningSession: vi.fn(),
  TimeoutError: class TimeoutError extends Error {},
}));

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast: vi.fn() }) }));

const handlers = new Set<(message: WebSocketMessage) => void>();
vi.mock('../context/WebSocketContext', () => ({
  useWebSocket: () => ({
    connected: true,
    reconnectCount: 0,
    send: vi.fn(),
    subscribe: (handler: (message: WebSocketMessage) => void) => {
      handlers.add(handler);
      return () => { handlers.delete(handler); };
    },
  }),
}));

function page<T>(objects: T[]) {
  return { success: true, pageNumber: 1, pageSize: 1000, totalPages: 1, totalRecords: objects.length, totalMs: 1, objects };
}

function session(status: string, lastUpdateUtc: string): PlanningSession {
  return {
    id: 'psn_1', tenantId: null, userId: null, captainId: 'cpt_1', vesselId: 'vsl_1', fleetId: null, dockId: null,
    branchName: null, title: 'Plan the gateway', status, pipelineId: null, processId: null, failureReason: null,
    createdUtc: '2026-10-05T00:00:00Z', startedUtc: '2026-10-05T00:00:00Z', completedUtc: null, lastUpdateUtc,
  };
}

function message(id: string, role: string, sequence: number, content: string, lastUpdateUtc: string): PlanningSessionMessage {
  return {
    id, planningSessionId: 'psn_1', tenantId: null, userId: null, role, sequence, content,
    isSelectedForDispatch: false, createdUtc: '2026-10-05T00:00:01Z', lastUpdateUtc,
  };
}

function detail(s: PlanningSession, messages: PlanningSessionMessage[]): PlanningSessionDetail {
  return { session: s, messages, captain: null, vessel: null };
}

function emit(msg: WebSocketMessage) {
  act(() => { handlers.forEach((h) => h(msg)); });
}

describe('Planning send ordering (F16)', () => {
  beforeEach(() => {
    handlers.clear();
    vi.mocked(listPlanningSessions).mockResolvedValue([session('Active', '2026-10-05T00:00:00Z')] as never);
    vi.mocked(listCaptains).mockResolvedValue(page([]) as never);
    vi.mocked(listFleets).mockResolvedValue(page([]) as never);
    vi.mocked(listVessels).mockResolvedValue(page([]) as never);
    vi.mocked(listPipelines).mockResolvedValue(page([]) as never);
    vi.mocked(getPlanningSession).mockResolvedValue(detail(session('Active', '2026-10-05T00:00:00Z'), []));
  });

  it('keeps a fast reply that arrived over the WebSocket before the send response', async () => {
    let resolveSend: (value: PlanningSessionDetail) => void = () => {};
    vi.mocked(sendPlanningSessionMessage).mockImplementation(() => new Promise((resolve) => { resolveSend = resolve; }));

    render(
      <MemoryRouter initialEntries={['/planning/psn_1']}>
        <Routes>
          <Route path="/planning/:id" element={<Planning />} />
        </Routes>
      </MemoryRouter>,
    );

    const input = await screen.findByPlaceholderText(/Describe the problem/);
    fireEvent.change(input, { target: { value: 'Plan it' } });
    fireEvent.click(screen.getByRole('button', { name: 'Send' }));

    // The captain answers and the turn completes before the POST response is delivered.
    const user = message('psm_u', 'User', 1, 'Plan it', '2026-10-05T00:00:01.1Z');
    emit({ type: 'planning-session.message.created', data: { sessionId: 'psn_1', message: user } });
    emit({ type: 'planning-session.message.updated', data: { sessionId: 'psn_1', message: message('psm_a', 'Assistant', 2, 'Fast reply', '2026-10-05T00:00:01.1234567Z') } });
    emit({ type: 'planning-session.changed', data: { session: session('Active', '2026-10-05T00:00:01.1234568Z') } });

    // The POST response describes the turn as it started (Responding, empty reply), with slightly older timestamps.
    await act(async () => {
      resolveSend(detail(session('Responding', '2026-10-05T00:00:01.12Z'), [user, message('psm_a', 'Assistant', 2, '', '2026-10-05T00:00:01.12Z')]));
    });

    expect((await screen.findAllByText('Fast reply')).length).toBeGreaterThan(0);
    expect(screen.queryByRole('button', { name: 'Stop' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Send' })).toBeInTheDocument();
  });
});
