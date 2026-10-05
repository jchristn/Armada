import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import CliPermissions from './CliPermissions';
import {
  createCliPermissionRule,
  deleteCliPermissionRule,
  getCliPermissionRequest,
  listCaptains,
  listCliPermissionRequests,
  listCliPermissionRules,
  listVessels,
} from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import type { CliPermissionRequest, CliPermissionRule, EnumerationResult, WebSocketMessage } from '../types/models';

vi.mock('../api/client', () => ({
  createCliPermissionRule: vi.fn(),
  deleteCliPermissionRule: vi.fn(),
  decideCliPermissionRequest: vi.fn(),
  getCliPermissionRequest: vi.fn(),
  listCaptains: vi.fn(),
  listCliPermissionRequests: vi.fn(),
  listCliPermissionRules: vi.fn(),
  listVessels: vi.fn(),
}));

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => (v ? 'recently' : ''),
};
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../context/WebSocketContext', () => ({
  useWebSocket: () => ({ connected: true, reconnectCount: 0, subscribe: (_h: (m: WebSocketMessage) => void) => () => undefined, send: vi.fn() }),
}));
const authValue = { isAdmin: true, isTenantAdmin: false };
vi.mock('../context/AuthContext', () => ({ useAuth: () => authValue }));
vi.mock('../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast: vi.fn() }) }));

const page = <T,>(objects: T[]): EnumerationResult<T> => ({ success: true, pageNumber: 1, pageSize: 50, totalPages: 1, totalRecords: objects.length, objects, totalMs: 1 });

const pending: CliPermissionRequest = {
  id: 'cpr_1', toolName: 'Bash', summaryText: 'make deploy', status: 'Pending', captainId: 'cpt_1', captainName: 'Ada', canDecide: true, canRemember: true,
  expiresUtc: new Date(Date.now() + 60000).toISOString(),
};
const decided: CliPermissionRequest = { id: 'cpr_2', toolName: 'WebFetch', summaryText: 'https://example.com', status: 'Denied', decisionSource: 'DenyRule', canDecide: false };
const rules: CliPermissionRule[] = [
  { id: 'cpl_1', scope: 'Global', pattern: 'Bash(git status:*)', action: 'Allow', tenantId: 'ten_1' },
  { id: 'cpl_2', scope: 'Vessel', vesselId: 'vsl_1', pattern: 'Bash(rm -rf:*)', action: 'Deny', description: 'Never wipe', tenantId: 'ten_1' },
];

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/cli-permissions" element={<CliPermissions />} />
      </Routes>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  authValue.isAdmin = true;
  vi.mocked(listCliPermissionRequests).mockResolvedValue([pending]);
  vi.mocked(listCliPermissionRules).mockResolvedValue(rules);
  vi.mocked(listVessels).mockResolvedValue(page([{ id: 'vsl_1', name: 'payments-api' } as never]));
  vi.mocked(listCaptains).mockResolvedValue(page([{ id: 'cpt_1', name: 'Ada' } as never]));
});

describe('CLI Tool Permissions page', () => {
  it('lists pending requests by default and highlights the deep-linked request even when it is decided', async () => {
    vi.mocked(getCliPermissionRequest).mockResolvedValue(decided);
    renderAt('/cli-permissions?request=cpr_2');
    await waitFor(() => expect(listCliPermissionRequests).toHaveBeenCalledWith({ status: 'Pending', limit: 200 }));
    const linked = await screen.findByRole('group', { name: 'CLI permission: WebFetch' });
    expect(linked.className).toContain('is-highlighted');
    expect(within(linked).getByText('Denied by a saved rule.')).toBeInTheDocument();
    const other = screen.getByRole('group', { name: 'CLI permission: Bash' });
    expect(other.className).not.toContain('is-highlighted');
    expect(within(other).getByRole('button', { name: 'Allow once' })).toBeInTheDocument();

    fireEvent.change(screen.getByRole('combobox', { name: 'Status' }), { target: { value: '' } });
    await waitFor(() => expect(listCliPermissionRequests).toHaveBeenLastCalledWith({ status: null, limit: 200 }));
  });

  it('lists rules, creates a vessel rule, and deletes a rule after confirming', async () => {
    vi.mocked(createCliPermissionRule).mockResolvedValue({ id: 'cpl_3', scope: 'Vessel', vesselId: 'vsl_1', pattern: 'Bash(npm test:*)', action: 'Allow' });
    vi.mocked(deleteCliPermissionRule).mockResolvedValue(undefined);
    renderAt('/cli-permissions?tab=rules');
    expect(await screen.findByText('Bash(git status:*)')).toBeInTheDocument();
    expect(await screen.findByText('Vessel payments-api')).toBeInTheDocument();

    const form = screen.getByRole('form', { name: 'New rule' });
    fireEvent.change(within(form).getByLabelText('Pattern'), { target: { value: 'Bash(npm test:*)' } });
    fireEvent.change(within(form).getByLabelText('Scope'), { target: { value: 'Vessel' } });
    const create = within(form).getByRole('button', { name: 'Create rule' });
    expect(create).toBeDisabled();
    fireEvent.change(await within(form).findByLabelText('Vessel'), { target: { value: 'vsl_1' } });
    fireEvent.change(within(form).getByLabelText('Description'), { target: { value: 'Tests are safe' } });
    fireEvent.click(create);
    await waitFor(() => expect(createCliPermissionRule).toHaveBeenCalledWith({
      pattern: 'Bash(npm test:*)', action: 'Allow', scope: 'Vessel', vesselId: 'vsl_1', captainId: null, description: 'Tests are safe',
    }));

    fireEvent.click(screen.getByRole('button', { name: 'Delete rule Bash(rm -rf:*)' }));
    const dialog = await screen.findByRole('alertdialog');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }));
    await waitFor(() => expect(deleteCliPermissionRule).toHaveBeenCalledWith('cpl_2'));
    await waitFor(() => expect(screen.queryByText('Bash(rm -rf:*)')).not.toBeInTheDocument());
  });

  it('hides rule editing from users who are not admins', async () => {
    authValue.isAdmin = false;
    renderAt('/cli-permissions?tab=rules');
    expect(await screen.findByText('Bash(git status:*)')).toBeInTheDocument();
    expect(screen.queryByRole('form', { name: 'New rule' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Delete rule/ })).not.toBeInTheDocument();
  });
});
