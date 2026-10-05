import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import CliPermissionCard from './CliPermissionCard';
import { decideCliPermissionRequest } from '../../api/client';
import { translateTemplate } from '../../i18n/runtime';
import type { CliPermissionRequest } from '../../types/models';

vi.mock('../../api/client', () => ({ decideCliPermissionRequest: vi.fn() }));

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => (v ? 'recently' : ''),
};
vi.mock('../../context/LocaleContext', () => ({ useLocale: () => localeValue }));

const NOW = Date.parse('2026-10-05T12:00:00Z');

function request(overrides: Partial<CliPermissionRequest> = {}): CliPermissionRequest {
  return {
    id: 'cpr_1',
    toolName: 'Bash',
    summaryText: 'git push origin main',
    inputText: '{"command":"git push origin main","description":"Push"}',
    suggestedRule: 'Bash(git push:*)',
    status: 'Pending',
    captainId: 'cpt_1',
    captainName: 'Ada',
    vesselId: 'vsl_1',
    vesselName: 'payments-api',
    missionId: 'msn_1',
    missionTitle: 'Add backoff',
    threadId: 'ath_1',
    expiresUtc: new Date(NOW + 125000).toISOString(),
    createdUtc: new Date(NOW - 5000).toISOString(),
    canDecide: true,
    canRemember: true,
    ...overrides,
  };
}

function renderCard(req: CliPermissionRequest, onDecided = vi.fn()) {
  render(<MemoryRouter><CliPermissionCard request={req} onDecided={onDecided} /></MemoryRouter>);
  return onDecided;
}

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: false });
  vi.setSystemTime(NOW);
  vi.mocked(decideCliPermissionRequest).mockReset();
});

afterEach(() => {
  vi.useRealTimers();
});

describe('CliPermissionCard', () => {
  it('shows the tool, command, captain, vessel, mission, expandable input, and a live countdown', () => {
    renderCard(request());
    const card = screen.getByRole('group', { name: 'CLI permission: Bash' });
    expect(within(card).getByText('Permission needed')).toBeInTheDocument();
    expect(within(card).getByText('Bash')).toBeInTheDocument();
    expect(within(card).getByText('git push origin main')).toBeInTheDocument();
    expect(within(card).getByRole('link', { name: 'Ada' })).toHaveAttribute('href', '/captains/cpt_1');
    expect(within(card).getByRole('link', { name: 'payments-api' })).toHaveAttribute('href', '/vessels/vsl_1');
    expect(within(card).getByRole('link', { name: 'Add backoff' })).toHaveAttribute('href', '/missions/msn_1');
    expect(within(card).getByText('Input')).toBeInTheDocument();
    expect(within(card).getByText(/"description": "Push"/)).toBeInTheDocument();

    expect(within(card).getByText('Expires in 2:05')).toBeInTheDocument();
    act(() => { vi.advanceTimersByTime(3000); });
    expect(within(card).getByText('Expires in 2:02')).toBeInTheDocument();
  });

  it('offers Allow once, Allow and remember, and Deny when the user can decide and remember', () => {
    renderCard(request());
    expect(screen.getByRole('button', { name: 'Allow once' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Allow and remember' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Deny' })).toBeInTheDocument();
    expect(screen.queryByText('Waiting for an admin to decide.')).not.toBeInTheDocument();
  });

  it('hides Allow and remember when the user cannot create rules', () => {
    renderCard(request({ canRemember: false }));
    expect(screen.getByRole('button', { name: 'Allow once' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Allow and remember' })).not.toBeInTheDocument();
  });

  it('says it waits for an admin when the user cannot decide', () => {
    renderCard(request({ canDecide: false, canRemember: false }));
    expect(screen.getByText('Waiting for an admin to decide.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Allow once' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Deny' })).not.toBeInTheDocument();
  });

  it('allows once through the API and reports the updated request', async () => {
    const updated = request({ status: 'Allowed', canDecide: false, decisionSource: 'Approver' });
    vi.mocked(decideCliPermissionRequest).mockResolvedValue(updated);
    const onDecided = renderCard(request());
    fireEvent.click(screen.getByRole('button', { name: 'Allow once' }));
    await act(async () => { await Promise.resolve(); });
    expect(decideCliPermissionRequest).toHaveBeenCalledWith('cpr_1', { decision: 'AllowOnce' });
    expect(onDecided).toHaveBeenCalledWith(updated);
  });

  it('allow and remember edits the suggested rule and picks a scope', async () => {
    vi.mocked(decideCliPermissionRequest).mockResolvedValue(request({ status: 'Allowed', canDecide: false }));
    renderCard(request());
    fireEvent.click(screen.getByRole('button', { name: 'Allow and remember' }));
    const dialog = screen.getByRole('dialog', { name: 'Allow and remember' });
    const pattern = within(dialog).getByLabelText('Rule pattern') as HTMLInputElement;
    expect(pattern.value).toBe('Bash(git push:*)');
    const scope = within(dialog).getByLabelText('Remember for') as HTMLSelectElement;
    expect(scope.value).toBe('Captain');
    expect(Array.from(scope.options).map((o) => o.value)).toEqual(['Captain', 'Vessel', 'Global']);

    fireEvent.change(pattern, { target: { value: 'Bash(git push origin:*)' } });
    fireEvent.change(scope, { target: { value: 'Vessel' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Allow and create rule' }));
    await act(async () => { await Promise.resolve(); });
    expect(decideCliPermissionRequest).toHaveBeenCalledWith('cpr_1', { decision: 'AllowAndRemember', rulePattern: 'Bash(git push origin:*)', ruleScope: 'Vessel' });
  });

  it('offers the Vessel scope only when the request has a vessel', () => {
    renderCard(request({ vesselId: null, vesselName: null, missionId: null }));
    fireEvent.click(screen.getByRole('button', { name: 'Allow and remember' }));
    const scope = within(screen.getByRole('dialog', { name: 'Allow and remember' })).getByLabelText('Remember for') as HTMLSelectElement;
    expect(Array.from(scope.options).map((o) => o.value)).toEqual(['Captain', 'Global']);
  });

  it('denies with an optional message', async () => {
    vi.mocked(decideCliPermissionRequest).mockResolvedValue(request({ status: 'Denied', canDecide: false }));
    renderCard(request());
    fireEvent.click(screen.getByRole('button', { name: 'Deny' }));
    const dialog = screen.getByRole('dialog', { name: 'Deny' });
    fireEvent.change(within(dialog).getByLabelText('Optional message for the captain'), { target: { value: 'Open a PR instead.' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Deny' }));
    await act(async () => { await Promise.resolve(); });
    expect(decideCliPermissionRequest).toHaveBeenCalledWith('cpr_1', { decision: 'Deny', message: 'Open a PR instead.' });
  });

  it('shows the server error when a decision fails', async () => {
    vi.mocked(decideCliPermissionRequest).mockRejectedValue(new Error('The request is no longer pending.'));
    renderCard(request());
    fireEvent.click(screen.getByRole('button', { name: 'Allow once' }));
    await act(async () => { await Promise.resolve(); await Promise.resolve(); });
    expect(screen.getByRole('alert')).toHaveTextContent('The request is no longer pending.');
  });

  it('shows the outcome, how it was decided, and the message after a decision', () => {
    renderCard(request({ status: 'Denied', decisionSource: 'Approver', decisionMessage: 'Not on main.', decidedUtc: new Date(NOW).toISOString(), canDecide: false }));
    expect(screen.getByText('Denied. The tool did not run.')).toBeInTheDocument();
    expect(screen.getByText('Decided by an approver.')).toBeInTheDocument();
    expect(screen.getByText('Message: Not on main.')).toBeInTheDocument();
    expect(screen.queryByText(/Expires in/)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Allow once' })).not.toBeInTheDocument();
  });
});
