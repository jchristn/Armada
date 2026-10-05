import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import MissionFailureDetails from './MissionFailureDetails';
import { getMission, getMissionLog, listIncidents } from '../../api/client';
import { translateTemplate } from '../../i18n/runtime';
import type { Mission } from '../../types/models';

vi.mock('../../api/client', () => ({
  getMission: vi.fn(),
  getMissionLog: vi.fn(),
  listIncidents: vi.fn(),
}));
vi.mock('../../context/LocaleContext', () => ({
  useLocale: () => ({ t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params) }),
}));

function mission(id: string, title: string, status: string, failureReason: string | null = null): Mission {
  return { id, title, status, failureReason, vesselId: 'vsl_1', captainId: 'cpt_1' } as unknown as Mission;
}

describe('MissionFailureDetails (F37 wizard handoff)', () => {
  beforeEach(() => {
    vi.mocked(listIncidents).mockResolvedValue({ success: true, pageNumber: 1, pageSize: 50, totalPages: 1, totalRecords: 1, totalMs: 1, objects: [
      { id: 'inc_1', missionId: 'msn_1', rescueMissionIds: ['msn_r1'] },
    ] } as never);
    vi.mocked(getMission).mockResolvedValue(mission('msn_r1', '[Rescue] Append Armada line', 'InProgress'));
    vi.mocked(getMissionLog).mockResolvedValue({ log: 'line one\nnothing to commit', lines: 2, totalLines: 2 });
  });

  it('shows the failure reason, the rescue missions, and the mission log', async () => {
    const onOpen = vi.fn();
    render(<MissionFailureDetails mission={mission('msn_1', 'Append Armada line', 'Failed', 'Agent exited with code 1: nothing to commit')} onOpenMission={onOpen} />);

    expect(screen.getByText('The mission failed.')).toBeInTheDocument();
    expect(screen.getByText('Agent exited with code 1: nothing to commit')).toBeInTheDocument();

    const rescue = await screen.findByRole('button', { name: '[Rescue] Append Armada line' });
    expect(screen.getByText('Armada started a rescue mission to retry this work:')).toBeInTheDocument();
    expect(listIncidents).toHaveBeenCalledWith(expect.objectContaining({ missionId: 'msn_1' }));
    fireEvent.click(rescue);
    expect(onOpen).toHaveBeenCalledWith('msn_r1');

    fireEvent.click(screen.getByRole('button', { name: 'View Mission Log' }));
    await waitFor(() => expect(getMissionLog).toHaveBeenCalledWith('msn_1', 200));
    expect(await screen.findByText(/nothing to commit/, { selector: '#log-viewer-content *' })).toBeInTheDocument();
  });

  it('says so when no reason was recorded', () => {
    vi.mocked(listIncidents).mockResolvedValue({ success: true, pageNumber: 1, pageSize: 50, totalPages: 1, totalRecords: 0, totalMs: 1, objects: [] } as never);
    render(<MissionFailureDetails mission={mission('msn_2', 'Broke', 'Failed', null)} onOpenMission={vi.fn()} />);
    expect(screen.getByText(/No reason was recorded/)).toBeInTheDocument();
  });
});
