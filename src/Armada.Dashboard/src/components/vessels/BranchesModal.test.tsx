import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen } from '@testing-library/react';
import BranchesModal from './BranchesModal';
import { getVesselBranches } from '../../api/client';
import { translateTemplate } from '../../i18n/runtime';

vi.mock('../../api/client', () => ({
  getVesselBranches: vi.fn(),
  pushVesselBranch: vi.fn(),
  mergeVesselBranch: vi.fn(),
}));
vi.mock('../../context/LocaleContext', () => ({
  useLocale: () => ({ t: (text: string, params?: Record<string, string | number>) => translateTemplate('en', text, null, params) }),
}));
vi.mock('../../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast: vi.fn() }) }));

const branch = {
  name: 'feature/a-very-long-branch-name-that-should-not-wrap-in-the-table',
  isDefault: false,
  isCurrent: true,
  ahead: 2,
  behind: 1,
  commitSubject: 'Fix the login redirect loop',
  commitHash: 'abc1234',
  commitDate: '2026-10-01T10:00:00Z',
};

describe('BranchesModal table', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.mocked(getVesselBranches).mockReset();
    vi.mocked(getVesselBranches).mockResolvedValue({ vesselId: 'vsl_1', defaultBranch: 'main', branches: [branch], branchCount: 1 } as never);
  });

  it('shows the last commit on one line with hash and date in the tooltip, hash column hidden by default', async () => {
    render(<BranchesModal vesselId="vsl_1" vesselName="gateway" open onClose={() => undefined} />);
    const subject = await screen.findByText('Fix the login redirect loop');
    expect(subject).toHaveClass('truncate-text');
    const cell = subject.closest('td') as HTMLElement;
    expect(cell.querySelectorAll('div')).toHaveLength(0);
    expect(cell.getAttribute('title')).toContain('abc1234');
    expect(screen.queryByRole('columnheader', { name: 'Commit' })).not.toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Committed' })).toBeInTheDocument();
  });

  it('truncates the branch name on one line and keeps it in the title', async () => {
    render(<BranchesModal vesselId="vsl_1" vesselName="gateway" open onClose={() => undefined} />);
    const name = await screen.findByTitle(branch.name);
    expect(name).toHaveClass('url-value');
    expect(name.closest('td')).toHaveClass('cell-nowrap');
  });

  it('locks the Branch column and refreshes from the toolbar', async () => {
    render(<BranchesModal vesselId="vsl_1" vesselName="gateway" open onClose={() => undefined} />);
    await screen.findByText('Fix the login redirect loop');
    expect(getVesselBranches).toHaveBeenCalledTimes(1);
    await act(async () => { fireEvent.click(screen.getByTitle('Refresh')); });
    expect(getVesselBranches).toHaveBeenCalledTimes(2);
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: /^Columns/ })); });
    expect(screen.getByRole('menuitemcheckbox', { name: /Branch/ })).toHaveAttribute('aria-disabled', 'true');
  });
});
