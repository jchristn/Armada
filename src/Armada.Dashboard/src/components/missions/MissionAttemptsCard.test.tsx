import { act, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import MissionAttemptsCard from './MissionAttemptsCard';
import MissionSubmissionsCard from './MissionSubmissionsCard';
import { getMissionAttempts, listMissionSubmissions } from '../../api/client';
import { translateTemplate } from '../../i18n/runtime';
import type { MissionAttemptHistory, StageSubmission } from '../../types/models';

vi.mock('../../api/client', () => ({
  getMissionAttempts: vi.fn(),
  listMissionSubmissions: vi.fn(),
}));

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
};
vi.mock('../../context/LocaleContext', () => ({ useLocale: () => localeValue }));

type Handler = (msg: { type: string; data?: unknown }) => void;
const handlers: Handler[] = [];
vi.mock('../../context/WebSocketContext', async () => {
  const React = await import('react');
  return {
    useMissionAttemptRevision: (missionId: string) => {
      const [revision, setRevision] = React.useState(0);
      React.useEffect(() => {
        const h: Handler = (msg) => {
          const data = msg.data as { id?: string; missionId?: string } | undefined;
          if (data?.missionId === missionId || data?.id === missionId) setRevision((r) => r + 1);
        };
        handlers.push(h);
        return () => { handlers.splice(handlers.indexOf(h), 1); };
      }, [missionId]);
      return revision;
    },
  };
});

function history(attempts = 2): MissionAttemptHistory {
  const all = [
    {
      outcomeId: 'out_1', attempt: 1, reworkRound: 0, captainId: 'cpt_open', captainName: 'qwen-worker', runtime: 'ApiEndpoint',
      model: 'qwen3.8', modelProvider: 'Ollama', disposition: 'Escalated', failureKind: 'ToolCallMalformed', failureReason: 'bad JSON',
      contractRepairKind: 'None', isRepair: false, escalated: true, nextCaptainId: 'cpt_frontier', isEvaluation: false, durationMs: 190000,
    },
    {
      outcomeId: 'out_2', attempt: 2, reworkRound: 1, captainId: 'cpt_frontier', captainName: 'opus', runtime: 'ClaudeCode',
      model: 'opus-5.5', disposition: 'Completed', contractRepairKind: 'CompletionLineMissing', isRepair: false, escalated: false,
      judgeVerdict: 'PASS', isEvaluation: false, durationMs: 42000,
    },
  ];
  return {
    missionId: 'msn_1', status: 'Complete', tier: 'Premium', escalationCount: 1, escalatedFromModel: 'qwen3.8', reworkEscalated: false,
    voyageId: null, persona: 'Worker', mode: 'Implementation', captainId: 'cpt_frontier', escalatedFromCaptainId: 'cpt_open',
    redispatchAttempts: 0, outputRepairAttempts: 0, noOpRedispatchAttempts: 0, stallRecoveryAttempts: 0, attempts: all.slice(0, attempts) as unknown as MissionAttemptHistory['attempts'],
  };
}

describe('MissionAttemptsCard', () => {
  beforeEach(() => { vi.mocked(getMissionAttempts).mockReset(); handlers.length = 0; });

  it('renders each attempt with captain, model, outcome, failure kind, next step and duration', async () => {
    vi.mocked(getMissionAttempts).mockResolvedValue(history());
    render(<MemoryRouter><MissionAttemptsCard missionId="msn_1" /></MemoryRouter>);
    const rows = await screen.findAllByTestId('mission-attempt-row');
    expect(rows).toHaveLength(2);
    expect(within(rows[0]).getByText('qwen-worker')).toHaveAttribute('href', '/captains/cpt_open');
    expect(within(rows[0]).getByText('ToolCallMalformed')).toBeInTheDocument();
    expect(within(rows[0]).getByText('Escalated to cpt_frontier')).toBeInTheDocument();
    expect(within(rows[0]).getByText('3m 10s')).toBeInTheDocument();
    expect(within(rows[1]).getByText('Repaired: CompletionLineMissing')).toBeInTheDocument();
    expect(within(rows[1]).getByText('2 (rework 1)')).toBeInTheDocument();
    expect(screen.getByText(/Escalated from qwen3.8/)).toBeInTheDocument();
    expect(screen.getByText('Attempts (2)')).toBeInTheDocument();
  });

  it('is hidden when the mission has no attempts or the read fails', async () => {
    vi.mocked(getMissionAttempts).mockResolvedValue(history(0));
    const { container, rerender } = render(<MemoryRouter><MissionAttemptsCard missionId="msn_1" /></MemoryRouter>);
    await waitFor(() => expect(getMissionAttempts).toHaveBeenCalled());
    expect(container.querySelector('[data-testid="mission-attempts"]')).toBeNull();
    vi.mocked(getMissionAttempts).mockRejectedValue(new Error('404'));
    rerender(<MemoryRouter><MissionAttemptsCard missionId="msn_1" revision="r2" /></MemoryRouter>);
    await waitFor(() => expect(getMissionAttempts).toHaveBeenCalledTimes(2));
    expect(container.querySelector('[data-testid="mission-attempts"]')).toBeNull();
  });

  it('reloads on an attempt event for this mission only', async () => {
    vi.mocked(getMissionAttempts).mockResolvedValueOnce(history(1)).mockResolvedValue(history(2));
    render(<MemoryRouter><MissionAttemptsCard missionId="msn_1" /></MemoryRouter>);
    expect(await screen.findAllByTestId('mission-attempt-row')).toHaveLength(1);
    act(() => { handlers.forEach(h => h({ type: 'mission.escalated', data: { missionId: 'msn_other' } })); });
    expect(getMissionAttempts).toHaveBeenCalledTimes(1);
    act(() => { handlers.forEach(h => h({ type: 'mission.escalated', data: { missionId: 'msn_1' } })); });
    await waitFor(() => expect(screen.getAllByTestId('mission-attempt-row')).toHaveLength(2));
  });
});

describe('MissionSubmissionsCard', () => {
  beforeEach(() => { vi.mocked(listMissionSubmissions).mockReset(); handlers.length = 0; });

  const verdict: StageSubmission = {
    id: 'sbm_1', missionId: 'msn_1', attemptId: 'out_2', persona: 'Judge', kind: 'Verdict', source: 'Tool', createdUtc: '2026-11-26T10:00:00Z',
    submission: {
      kind: 'Verdict',
      verdict: {
        verdict: 'FAIL', correctness: 'wrong', summary: 'Two problems',
        findings: [
          { severity: 'non_blocking', scenario: 'Naming nit', file: 'a.cs', line: 3 },
          { severity: 'blocking', scenario: 'Null dereference', file: 'b.cs', line: 10, requiredFix: 'Guard the input' },
        ],
      },
    },
  } as unknown as StageSubmission;

  it('shows the verdict, summary and findings with blocking first', async () => {
    vi.mocked(listMissionSubmissions).mockResolvedValue([verdict]);
    render(<MissionSubmissionsCard missionId="msn_1" />);
    expect(await screen.findByText('Submitted Results (1)')).toBeInTheDocument();
    expect(screen.getByText('FAIL')).toBeInTheDocument();
    expect(screen.getByText(/2 findings, 1 blocking/)).toBeInTheDocument();
    const rows = screen.getAllByTestId('mission-finding');
    expect(within(rows[0]).getByText('Blocking')).toBeInTheDocument();
    expect(within(rows[0]).getByText('b.cs:10')).toBeInTheDocument();
    expect(within(rows[0]).getByText('Guard the input')).toBeInTheDocument();
    expect(within(rows[1]).getByText('Non-blocking')).toBeInTheDocument();
  });

  it('is hidden with no submissions', async () => {
    vi.mocked(listMissionSubmissions).mockResolvedValue([]);
    const { container } = render(<MissionSubmissionsCard missionId="msn_1" />);
    await waitFor(() => expect(listMissionSubmissions).toHaveBeenCalled());
    expect(container.querySelector('[data-testid="mission-submissions"]')).toBeNull();
  });
});
