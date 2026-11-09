import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { useState } from 'react';
import { MemoryRouter, Route, Routes, useNavigate } from 'react-router-dom';
import { getCheckRun, getVessel, getWorkflowProfile, listCheckRuns } from '../api/client';
import { translateTemplate } from '../i18n/catalog';
import type { CheckRun } from '../types/models';
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
  }),
}));

vi.mock('../context/NotificationContext', () => ({
  useNotifications: () => ({ pushToast: vi.fn() }),
}));

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
