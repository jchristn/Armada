import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import Inbox from './Inbox';
import { decideCliPermissionRequest, getInbox } from '../api/client';
import type { CliPermissionRequest, InboxItem, WebSocketMessage } from '../types/models';

vi.mock('../api/client', () => ({ getInbox: vi.fn(), decideCliPermissionRequest: vi.fn() }));
const handlers = new Set<(msg: WebSocketMessage) => void>();
vi.mock('../context/WebSocketContext', () => ({
  useWebSocket: () => ({
    connected: true,
    reconnectCount: 0,
    subscribe: (handler: (msg: WebSocketMessage) => void) => { handlers.add(handler); return () => handlers.delete(handler); },
    send: vi.fn(),
  }),
}));
vi.mock('../context/LocaleContext', () => ({
  useLocale: () => ({
    t: (text: string, params?: Record<string, string>) =>
      Object.entries(params ?? {}).reduce((acc, [k, v]) => acc.split(`{{${k}}}`).join(String(v)), text),
    formatRelativeTime: (v: string) => v, formatDateTime: (v: string) => v,
  }),
}));
vi.mock('../context/NotificationContext', () => ({
  useNotifications: () => ({ notifications: [], unreadCount: 0, markRead: vi.fn(), markAllRead: vi.fn() }),
}));

function item(kind: string, title: string, href: string, severity: 'Critical' | 'Warning' = 'Warning'): InboxItem {
  return { kind, severity, title, detail: 'detail', entityType: null, entityId: `${kind}_1`, href };
}

function Probe() {
  const location = useLocation();
  return <div data-testid="location">{location.pathname}</div>;
}

describe('Needs You approvals (F15)', () => {
  it('lists pending Ask proposals and deployment approvals under Waiting for your approval, with links', async () => {
    vi.mocked(getInbox).mockResolvedValue([
      item('failed', 'Failed: Append line', '/missions/msn_1', 'Critical'),
      item('ask_proposal', 'Ask approval: Dispatch voyage Fix login to vessel gateway', '/ask/ath_1'),
      { ...item('deployment_approval', 'Deployment awaiting approval: Production', '/deployments/dpl_1'), entityName: 'Production', environmentName: 'Production', deploymentTitle: 'Release 2.3 hotfix' },
    ]);

    render(
      <MemoryRouter initialEntries={['/inbox']}>
        <Routes>
          <Route path="*" element={<><Inbox /><Probe /></>} />
        </Routes>
      </MemoryRouter>,
    );

    const approvals = await screen.findByRole('region', { name: 'Waiting for your approval' });
    expect(within(approvals).getByText('Ask approval: Dispatch voyage Fix login to vessel gateway')).toBeInTheDocument();
    // Environment first, deployment title second: the same label as the TUI and the approval confirm dialog.
    expect(within(approvals).getByText('Deploy to Production: Release 2.3 hotfix')).toBeInTheDocument();
    expect(within(approvals).queryByText('Deployment awaiting approval: Production')).not.toBeInTheDocument();
    expect(within(approvals).queryByText('Failed: Append line')).not.toBeInTheDocument();

    const interventions = screen.getByRole('region', { name: 'Needs intervention' });
    expect(within(interventions).getByText('Failed: Append line')).toBeInTheDocument();

    fireEvent.click(within(approvals).getByRole('button', { name: 'Open conversation' }));
    expect(screen.getByTestId('location').textContent).toBe('/ask/ath_1');
  });
});

describe('Needs You CLI tool permission requests', () => {
  const request: CliPermissionRequest = {
    id: 'cpr_1', toolName: 'Bash', summaryText: 'rm -rf build', inputText: '{"command":"rm -rf build"}', suggestedRule: 'Bash(rm -rf build)',
    status: 'Pending', captainId: 'cpt_1', captainName: 'Ada', threadId: 'ath_9', expiresUtc: new Date(Date.now() + 120000).toISOString(),
    canDecide: true, canRemember: false,
  };
  const cliItem = (overrides: Partial<CliPermissionRequest> = {}): InboxItem => ({
    ...item('cli_permission', 'CLI permission: Bash rm -rf build', '/cli-permissions?request=cpr_1'),
    entityId: 'cpr_1', cliPermission: { ...request, ...overrides }, expiresUtc: request.expiresUtc,
  });

  beforeEach(() => {
    handlers.clear();
    vi.mocked(getInbox).mockReset();
    vi.mocked(decideCliPermissionRequest).mockReset();
  });

  function renderInbox() {
    render(
      <MemoryRouter initialEntries={['/inbox']}>
        <Routes>
          <Route path="*" element={<><Inbox /><Probe /></>} />
        </Routes>
      </MemoryRouter>,
    );
  }

  it('lists a decidable request under approvals with the command, a countdown, and inline decisions', async () => {
    vi.mocked(getInbox).mockResolvedValueOnce([cliItem()]).mockResolvedValue([]);
    vi.mocked(decideCliPermissionRequest).mockResolvedValue({ ...request, status: 'Allowed', canDecide: false });
    renderInbox();

    const approvals = await screen.findByRole('region', { name: 'Waiting for your approval' });
    expect(within(approvals).getByText('CLI permission: Bash rm -rf build')).toBeInTheDocument();
    expect(within(approvals).getByText('rm -rf build')).toBeInTheDocument();
    expect(within(approvals).getByText(/^Expires in \d+:\d\d$/)).toBeInTheDocument();
    // CanRemember is false: no Allow and remember; no plain link button either, the decisions are inline.
    expect(within(approvals).queryByRole('button', { name: 'Allow and remember' })).not.toBeInTheDocument();
    expect(within(approvals).queryByRole('button', { name: 'Open request' })).not.toBeInTheDocument();

    fireEvent.click(within(approvals).getByRole('button', { name: 'Allow once' }));
    await waitFor(() => expect(decideCliPermissionRequest).toHaveBeenCalledWith('cpr_1', { decision: 'AllowOnce' }));
    // Deciding inline does not navigate away.
    expect(screen.getByTestId('location').textContent).toBe('/inbox');
    await waitFor(() => expect(getInbox).toHaveBeenCalledTimes(2));
  });

  it('links to the request when the user cannot decide it', async () => {
    vi.mocked(getInbox).mockResolvedValue([cliItem({ canDecide: false })]);
    renderInbox();
    const approvals = await screen.findByRole('region', { name: 'Waiting for your approval' });
    expect(within(approvals).queryByRole('button', { name: 'Allow once' })).not.toBeInTheDocument();
    fireEvent.click(within(approvals).getByRole('button', { name: 'Open request' }));
    expect(screen.getByTestId('location').textContent).toBe('/cli-permissions');
  });

  it('reloads when a cli_permission event arrives', async () => {
    vi.mocked(getInbox).mockResolvedValueOnce([]).mockResolvedValue([cliItem()]);
    renderInbox();
    await screen.findByText('You are all caught up.');
    await waitFor(() => expect(handlers.size).toBeGreaterThan(0));
    act(() => { handlers.forEach((h) => h({ type: 'cli_permission.requested', data: { requestId: 'cpr_1', status: 'Pending', request: { id: 'cpr_1' } } })); });
    expect(await screen.findByText('CLI permission: Bash rm -rf build')).toBeInTheDocument();
  });
});
