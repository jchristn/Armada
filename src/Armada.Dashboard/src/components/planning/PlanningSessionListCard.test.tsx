import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import PlanningSessionListCard from './PlanningSessionListCard';
import { onlyCallArgs } from '../../test/mockCalls';

vi.mock('../shared/StatusBadge', () => ({
  default: ({ status }: { status: string }) => <span>{status}</span>,
}));

vi.mock('../../context/LocaleContext', () => ({
  useLocale: () => ({ t: (value: string) => value }),
}));

const t = (value: string) => value;

function session(overrides: Record<string, unknown> = {}) {
  return {
    id: 'psn_active',
    tenantId: null,
    userId: null,
    captainId: 'cpt_1',
    vesselId: 'ves_1',
    fleetId: 'flt_1',
    dockId: 'dok_1',
    branchName: 'armada/planning/psn_active',
    title: 'Active planning session',
    status: 'Active',
    pipelineId: null,
    processId: null,
    failureReason: null,
    selectedPlaybooks: [],
    createdUtc: '2026-05-17T00:00:00Z',
    startedUtc: '2026-05-17T00:00:00Z',
    completedUtc: null,
    lastUpdateUtc: '2026-05-17T00:05:00Z',
    ...overrides,
  };
}

function renderCard(props: Partial<Record<string, unknown>> = {}, sessions = [session()]) {
  return render(
    <PlanningSessionListCard
      t={t}
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      sessions={sessions as any}
      activeSessionId={undefined}
      endingSessionId={null}
      formatRelativeTime={() => 'just now'}
      resolveCaptainName={() => 'Planner'}
      resolveVesselName={() => 'Repository'}
      resolvePipelineName={() => '-'}
      onSelect={props.onSelect as never ?? (() => undefined)}
      onEndSession={props.onEndSession as never ?? (() => undefined)}
      onDeleteSession={props.onDeleteSession as never ?? (() => undefined)}
      onDeleteAll={props.onDeleteAll as never ?? (() => undefined)}
    />,
  );
}

describe('PlanningSessionListCard', () => {
  it('starts collapsed and expands to show the table', async () => {
    const user = userEvent.setup();
    renderCard();
    expect(screen.queryByText('Active planning session')).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /Recent Sessions/ }));
    expect(screen.getByText('Active planning session')).toBeInTheDocument();
  });

  it('ends an active session from the action menu without triggering row selection', async () => {
    const user = userEvent.setup();
    const onSelect = vi.fn();
    const onEndSession = vi.fn();
    renderCard({ onSelect, onEndSession });

    await user.click(screen.getByRole('button', { name: /Recent Sessions/ }));
    await user.click(screen.getByTitle('Actions'));
    await user.click(screen.getByRole('menuitem', { name: 'End Session' }));

    expect(onEndSession).toHaveBeenCalledTimes(1);
    expect(onlyCallArgs(onEndSession)[0].id).toBe('psn_active');
    expect(onSelect).not.toHaveBeenCalled();
  });

  it('deletes a session from the action menu', async () => {
    const user = userEvent.setup();
    const onDeleteSession = vi.fn();
    renderCard({ onDeleteSession });

    await user.click(screen.getByRole('button', { name: /Recent Sessions/ }));
    await user.click(screen.getByTitle('Actions'));
    await user.click(screen.getByRole('menuitem', { name: 'Delete' }));

    expect(onDeleteSession).toHaveBeenCalledTimes(1);
    expect(onlyCallArgs(onDeleteSession)[0].id).toBe('psn_active');
  });
});
