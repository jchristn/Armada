import { fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import Inbox from './Inbox';
import { getInbox } from '../api/client';
import type { InboxItem } from '../types/models';

vi.mock('../api/client', () => ({ getInbox: vi.fn() }));
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
