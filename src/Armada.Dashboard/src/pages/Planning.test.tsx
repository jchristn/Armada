import { act, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import Planning from './Planning';
import { getPlanningSession, listCaptains, listFleets, listPipelines, listPlanningSessionMessages, listPlanningSessions, listVessels, sendPlanningSessionMessage } from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import type { PlanningSession, PlanningSessionMessage, PlanningSessionView, WebSocketMessage } from '../types/models';

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
  listPlanningSessionMessages: vi.fn().mockResolvedValue({ success: true, pageNumber: 1, pageSize: 100, totalPages: 0, totalRecords: 0, objects: [], totalMs: 0 }),
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

function detail(s: PlanningSession, messages: PlanningSessionMessage[]): PlanningSessionView {
  return { session: s, messages, messageCount: messages.length, captain: null, vessel: null };
}

function emit(msg: WebSocketMessage) {
  act(() => { handlers.forEach((h) => h(msg)); });
}

describe('Planning send ordering (F16)', () => {
  beforeEach(() => {
    handlers.clear();
    vi.mocked(listPlanningSessions).mockResolvedValue(page([session('Active', '2026-10-05T00:00:00Z')]) as never);
    vi.mocked(listCaptains).mockResolvedValue(page([]) as never);
    vi.mocked(listFleets).mockResolvedValue(page([]) as never);
    vi.mocked(listVessels).mockResolvedValue(page([]) as never);
    vi.mocked(listPipelines).mockResolvedValue(page([]) as never);
    vi.mocked(getPlanningSession).mockResolvedValue(detail(session('Active', '2026-10-05T00:00:00Z'), []));
  });

  it('keeps a fast reply that arrived over the WebSocket before the send response', async () => {
    let resolveSend: (value: PlanningSessionView) => void = () => {};
    vi.mocked(sendPlanningSessionMessage).mockImplementation(() => new Promise((resolve) => { resolveSend = resolve; }));

    render(
      <MemoryRouter initialEntries={['/dispatch?tab=planning&session=psn_1']}>
        <Routes>
          <Route path="/dispatch" element={<Planning />} />
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

  it('keeps the planning transcript position when streamed assistant text changes', async () => {
    vi.mocked(getPlanningSession).mockResolvedValue({ session: session('Responding', '2026-10-05T00:00:00Z'), messageCount: 1, captain: null, vessel: null });
    vi.mocked(listPlanningSessionMessages).mockResolvedValue({
      success: true, pageNumber: 1, pageSize: 100, totalPages: 1, totalRecords: 1, totalMs: 1,
      objects: [message('psm_a', 'Assistant', 1, 'First part', '2026-10-05T00:00:00Z')],
    });

    const { container } = render(
      <MemoryRouter initialEntries={['/dispatch?tab=planning&session=psn_1']}>
        <Routes>
          <Route path="/dispatch" element={<Planning />} />
        </Routes>
      </MemoryRouter>,
    );

    const transcript = await screen.findByText('First part').then((node) => node.closest('.ask-chat-window') as HTMLDivElement);
    Object.defineProperty(transcript, 'scrollHeight', { configurable: true, value: 900 });
    Object.defineProperty(transcript, 'clientHeight', { configurable: true, value: 300 });
    transcript.scrollTop = 120;

    emit({ type: 'planning-session.message.updated', data: { sessionId: 'psn_1', message: message('psm_a', 'Assistant', 1, 'First part with more text', '2026-10-05T00:00:01Z') } });

    expect(transcript.scrollTop).toBe(120);
    expect(container.querySelector('.ask-chat-window')).toBe(transcript);
  });

  it('loads the transcript one page at a time: the newest page first, older pages on request, with the total', async () => {
    const all = Array.from({ length: 150 }, (_, i) => message(`psm_${i + 1}`, i % 2 === 0 ? 'User' : 'Assistant', i + 1, `line ${i + 1}`, '2026-10-05T00:00:01Z'));
    vi.mocked(getPlanningSession).mockResolvedValue({ session: session('Active', '2026-10-05T00:00:00Z'), messageCount: 150, captain: null, vessel: null });
    vi.mocked(listPlanningSessionMessages).mockImplementation(async (_id, query) => {
      const newestFirst = [...all].reverse();
      const size = query?.pageSize ?? 10;
      const number = query?.pageNumber ?? 1;
      return { success: true, pageNumber: number, pageSize: size, totalPages: Math.ceil(150 / size), totalRecords: 150, totalMs: 1, objects: newestFirst.slice((number - 1) * size, number * size) };
    });

    render(
      <MemoryRouter initialEntries={['/dispatch?tab=planning&session=psn_1']}>
        <Routes>
          <Route path="/dispatch" element={<Planning />} />
        </Routes>
      </MemoryRouter>,
    );

    expect((await screen.findAllByText('line 150')).length).toBeGreaterThan(0);
    expect(screen.queryByText('line 50')).not.toBeInTheDocument();
    const older = screen.getByRole('button', { name: 'Load earlier messages (100 of 150)' });
    expect(vi.mocked(listPlanningSessionMessages)).toHaveBeenCalledWith('psn_1', expect.objectContaining({ pageNumber: 1, pageSize: 100, order: 'CreatedDescending' }));
    await act(async () => { fireEvent.click(older); });
    expect(vi.mocked(listPlanningSessionMessages)).toHaveBeenCalledWith('psn_1', expect.objectContaining({ pageNumber: 2, pageSize: 100 }));
    expect((await screen.findAllByText('line 1')).length).toBeGreaterThan(0);
    expect(screen.queryByRole('button', { name: /Load earlier messages/ })).not.toBeInTheDocument();
  });

  it('counts every planning session, not just the first page, and loads the next page on request', async () => {
    const sessions = Array.from({ length: 100 }, (_, i) => ({ ...session('Stopped', '2026-10-05T00:00:00Z'), id: `psn_${i}`, title: `Plan ${i}` }));
    vi.mocked(listPlanningSessions).mockImplementation(async (query) => ({
      success: true, pageNumber: query?.pageNumber ?? 1, pageSize: 100, totalPages: 2, totalRecords: 101, totalMs: 1,
      objects: (query?.pageNumber ?? 1) === 1 ? sessions : [{ ...session('Stopped', '2026-10-05T00:00:00Z'), id: 'psn_last', title: 'Plan last' }],
    }));

    render(
      <MemoryRouter initialEntries={['/dispatch?tab=planning']}>
        <Routes>
          <Route path="/dispatch" element={<Planning />} />
        </Routes>
      </MemoryRouter>,
    );

    expect(await screen.findByText('101 total')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Recent Sessions/ }));
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Load more (100 of 101)' })); });
    expect(vi.mocked(listPlanningSessions)).toHaveBeenCalledWith(expect.objectContaining({ pageNumber: 2, pageSize: 100 }));
    expect(await screen.findByText('Plan last')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Load more/ })).not.toBeInTheDocument();
  });
});
