import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { useState } from 'react';
import { MemoryRouter, Route, Routes, useNavigate } from 'react-router-dom';
import { deleteCheckRun, getCheckRun, getVessel, getWorkflowProfile, listCheckRuns, retryCheckRun } from '../api/client';
import { translateTemplate } from '../i18n/catalog';
import { LIVE_REFRESH_DEBOUNCE_MS } from '../lib/useLiveRefresh';
import type { CheckRun, EnumerationResult, WebSocketMessage } from '../types/models';
import CheckRunDetail from './CheckRunDetail';

vi.mock('../api/client', () => ({
  deleteCheckRun: vi.fn(),
  getCheckRun: vi.fn(),
  getVessel: vi.fn(),
  getWorkflowProfile: vi.fn(),
  listCheckRuns: vi.fn(),
  retryCheckRun: vi.fn(),
}));

type Translate = (text: string, params?: Record<string, string | number | null | undefined>) => string;

function translate(text: string, params?: Record<string, string | number | null | undefined>): string {
  return translateTemplate('en', text, null, params);
}

let translator: Translate = translate;

vi.mock('../context/LocaleContext', () => ({
  useLocale: () => ({
    t: translator,
    locale: 'en',
    formatDateTime: (value: string | null | undefined) => value ?? '',
    formatRelativeTime: (value: string | null | undefined) => value ?? '',
    formatDate: (value: string | null | undefined) => value ?? '',
  }),
}));

vi.mock('../context/NotificationContext', () => ({
  useNotifications: () => ({ pushToast: vi.fn() }),
}));

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

const OPEN_ID = 'chk_open';
const SHORT_PAGE_NOTE = 'Searched 500 of 501 runs of this type. Runs outside this page are not part of the comparison.';

interface Deferred<T> {
  promise: Promise<T>;
  resolve: (value: T) => void;
  reject: (reason: unknown) => void;
}

function deferred<T>(): Deferred<T> {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

const reads = new Map<string, Deferred<CheckRun>[]>();

function enqueue(id: string): Promise<CheckRun> {
  const slot = deferred<CheckRun>();
  const list = reads.get(id) ?? [];
  list.push(slot);
  reads.set(id, list);
  return slot.promise;
}

function latest(id: string): Deferred<CheckRun> {
  const list = reads.get(id);
  if (!list || list.length === 0) throw new Error(`No check run read is waiting for ${id}.`);
  return list[list.length - 1];
}

function checkRun(id: string, label: string, output: string): CheckRun {
  return {
    id,
    tenantId: null,
    userId: null,
    workflowProfileId: null,
    vesselId: null,
    missionId: null,
    voyageId: null,
    deploymentId: null,
    label,
    type: 'Build',
    source: 'Armada',
    status: 'Passed',
    providerName: null,
    externalId: null,
    externalUrl: null,
    environmentName: null,
    command: 'dotnet build',
    workingDirectory: null,
    branchName: null,
    commitHash: null,
    exitCode: 0,
    output,
    summary: null,
    testSummary: null,
    coverageSummary: null,
    artifacts: [],
    durationMs: null,
    startedUtc: null,
    completedUtc: null,
    createdUtc: '2026-05-03T00:00:00Z',
    lastUpdateUtc: '2026-05-03T00:00:00Z',
  };
}

function CheckRunHarness() {
  const navigate = useNavigate();
  const [, setTick] = useState(0);
  return (
    <>
      <button type="button" onClick={() => navigate('/checks/chk_b')}>Open chk_b</button>
      <button
        type="button"
        onClick={() => {
          translator = (text, params) => translate(text, params);
          setTick((value) => value + 1);
        }}
      >
        Reload translator
      </button>
      <Routes>
        <Route path="/checks/:id" element={<CheckRunDetail />} />
      </Routes>
    </>
  );
}

beforeEach(() => {
  reads.clear();
  translator = translate;
  vi.mocked(getCheckRun).mockImplementation((id: string) => enqueue(id));
  vi.mocked(getVessel).mockResolvedValue(null as never);
  vi.mocked(getWorkflowProfile).mockResolvedValue(null as never);
  vi.mocked(listCheckRuns).mockResolvedValue({ objects: [] } as never);
});

afterEach(() => {
  translator = translate;
  vi.clearAllMocks();
});

describe('CheckRunDetail route change', () => {
  it('shows the new load error and hides the previous run', async () => {
    render(
      <MemoryRouter initialEntries={['/checks/chk_a']}>
        <CheckRunHarness />
      </MemoryRouter>,
    );

    await act(async () => {
      latest('chk_a').resolve(checkRun('chk_a', 'Alpha run', 'alpha-output-line'));
    });
    expect(await screen.findByText('alpha-output-line')).toBeInTheDocument();
    expect(screen.getAllByText('Alpha run').length).toBeGreaterThan(0);

    fireEvent.click(screen.getByRole('button', { name: 'Reload translator' }));
    await waitFor(() => expect(reads.get('chk_a')).toHaveLength(2));

    fireEvent.click(screen.getByRole('button', { name: 'Open chk_b' }));
    expect(await screen.findByText('Loading...')).toBeInTheDocument();
    expect(screen.queryByText('alpha-output-line')).toBeNull();
    expect(screen.queryByText('Alpha run')).toBeNull();

    await act(async () => {
      latest('chk_b').reject(new Error('Cannot read check run B.'));
    });

    expect(await screen.findByRole('alertdialog')).toHaveTextContent('Cannot read check run B.');
    expect(screen.queryByText('alpha-output-line')).toBeNull();
    expect(screen.queryByText('Alpha run')).toBeNull();

    await act(async () => {
      reads.get('chk_a')![1].resolve(checkRun('chk_a', 'Alpha run', 'alpha-output-line'));
    });

    expect(screen.getByRole('alertdialog')).toHaveTextContent('Cannot read check run B.');
    expect(screen.queryByText('alpha-output-line')).toBeNull();
    expect(screen.queryByText('Alpha run')).toBeNull();
  });
});

function liveRun(overrides: Partial<CheckRun> = {}): CheckRun {
  return {
    id: OPEN_ID,
    tenantId: 'ten_1',
    userId: null,
    workflowProfileId: null,
    vesselId: 'vsl_1',
    missionId: null,
    voyageId: null,
    deploymentId: null,
    label: 'Open check',
    type: 'Build',
    source: 'Armada',
    status: 'Running',
    providerName: null,
    externalId: null,
    externalUrl: null,
    environmentName: null,
    command: 'echo check',
    workingDirectory: null,
    branchName: null,
    commitHash: null,
    exitCode: null,
    output: 'captured line one',
    summary: null,
    testSummary: null,
    coverageSummary: null,
    artifacts: [],
    durationMs: null,
    startedUtc: null,
    completedUtc: null,
    createdUtc: '2026-05-03T00:00:00Z',
    lastUpdateUtc: '2026-05-03T00:00:00Z',
    ...overrides,
  };
}

function page(objects: CheckRun[], totalRecords: number): EnumerationResult<CheckRun> {
  return {
    success: true,
    pageNumber: 1,
    pageSize: objects.length,
    totalPages: 1,
    totalRecords,
    totalMs: 1,
    objects,
  };
}

function emit(message: WebSocketMessage) {
  act(() => { handlers.forEach((handler) => handler(message)); });
}

function renderDetail() {
  render(
    <MemoryRouter initialEntries={[`/checks/${OPEN_ID}`]}>
      <Routes>
        <Route path="/checks/:id" element={<CheckRunDetail />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('CheckRunDetail live refresh', () => {
  beforeEach(() => {
    handlers.clear();
    vi.mocked(getCheckRun).mockReset();
    vi.mocked(listCheckRuns).mockReset();
    vi.mocked(getVessel).mockResolvedValue({ id: 'vsl_1', name: 'Example vessel' } as never);
    vi.mocked(getWorkflowProfile).mockResolvedValue(null as never);
    vi.mocked(listCheckRuns).mockResolvedValue(page([], 0));
    vi.mocked(deleteCheckRun).mockResolvedValue(undefined as never);
    vi.mocked(retryCheckRun).mockResolvedValue(liveRun());
  });

  afterEach(() => {
    cleanup();
    vi.clearAllMocks();
  });

  it('reloads the open run when check-run.changed names it, and ignores a different run id', async () => {
    let releaseSecond: (value: CheckRun) => void = () => {};
    const secondLoad = new Promise<CheckRun>((resolve) => { releaseSecond = resolve; });
    vi.mocked(getCheckRun)
      .mockResolvedValueOnce(liveRun({ output: 'captured line one', status: 'Running' }))
      .mockImplementationOnce(() => secondLoad);

    renderDetail();
    expect(await screen.findByText('captured line one')).toBeInTheDocument();
    expect(vi.mocked(getCheckRun)).toHaveBeenCalledTimes(1);

    emit({ type: 'check-run.changed', data: { id: 'chk_other' } });
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, LIVE_REFRESH_DEBOUNCE_MS + 50));
    });
    expect(vi.mocked(getCheckRun)).toHaveBeenCalledTimes(1);
    expect(screen.getByText('captured line one')).toBeInTheDocument();

    emit({ type: 'check-run.changed', data: { id: OPEN_ID, output: 'captured line two', status: 'Passed' } });
    await waitFor(() => expect(vi.mocked(getCheckRun)).toHaveBeenCalledTimes(2));
    expect(screen.queryByText('Loading...')).toBeNull();
    expect(screen.getByText('captured line one')).toBeInTheDocument();

    await act(async () => {
      releaseSecond(liveRun({ output: 'captured line two', status: 'Passed' }));
    });
    expect(await screen.findByText('captured line two')).toBeInTheDocument();
    expect(screen.queryByText('captured line one')).toBeNull();
    expect(screen.queryByText('Loading...')).toBeNull();
  });
});

describe('CheckRunDetail comparison page', () => {
  beforeEach(() => {
    handlers.clear();
    vi.mocked(getCheckRun).mockReset();
    vi.mocked(listCheckRuns).mockReset();
    vi.mocked(getCheckRun).mockResolvedValue(liveRun());
    vi.mocked(getVessel).mockResolvedValue({ id: 'vsl_1', name: 'Example vessel' } as never);
    vi.mocked(getWorkflowProfile).mockResolvedValue(null as never);
  });

  afterEach(() => {
    cleanup();
    vi.clearAllMocks();
  });

  it('says how many runs it searched when the page is shorter than totalRecords, and stays quiet when the counts match', async () => {
    const equalRuns = [
      liveRun({ id: 'chk_old_a', status: 'Passed', createdUtc: '2026-05-01T00:00:00Z', output: 'older a' }),
      liveRun({ id: 'chk_old_b', status: 'Passed', createdUtc: '2026-05-02T00:00:00Z', output: 'older b' }),
    ];
    vi.mocked(listCheckRuns).mockResolvedValue(page(equalRuns, equalRuns.length));
    renderDetail();
    expect(await screen.findByText('captured line one')).toBeInTheDocument();
    expect(screen.queryByText(/Searched \d+ of \d+/)).toBeNull();
    cleanup();

    const shortRuns = Array.from({ length: 500 }, (_, index) => liveRun({
      id: `chk_page_${index}`,
      status: 'Passed',
      createdUtc: '2026-05-01T00:00:00Z',
      output: 'older',
    }));
    vi.mocked(listCheckRuns).mockResolvedValue(page(shortRuns, 501));
    renderDetail();
    expect(await screen.findByText(SHORT_PAGE_NOTE)).toBeInTheDocument();
  });
});
