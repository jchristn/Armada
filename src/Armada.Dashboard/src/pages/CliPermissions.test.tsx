import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import CliPermissions from './CliPermissions';
import {
  createCliPermissionRule,
  deleteCliPermissionRule,
  getCliPermissionRequest,
  listCaptains,
  listCliPermissionRequests,
  listCliPermissionRules,
  enumerateCliPermissionRules,
  listVessels,
  updateCliPermissionRule,
} from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import type { CliPermissionRequest, CliPermissionRule, EnumerationResult, WebSocketMessage } from '../types/models';
import { asPage } from '../test/asPage';

vi.mock('../api/client', () => ({
  createCliPermissionRule: vi.fn(),
  deleteCliPermissionRule: vi.fn(),
  decideCliPermissionRequest: vi.fn(),
  getCliPermissionRequest: vi.fn(),
  listCaptains: vi.fn(),
  listCliPermissionRequests: vi.fn(),
  listCliPermissionRules: vi.fn(),
  enumerateCliPermissionRules: vi.fn(),
  listVessels: vi.fn(),
  updateCliPermissionRule: vi.fn(),
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

function LocationProbe() {
  const location = useLocation();
  return <div data-testid="location">{location.pathname + location.search}</div>;
}

/** The page is the CLI Tool Permissions tab of Settings, so it renders at /server?tab=cli-permissions. */
function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/server" element={<><CliPermissions /><LocationProbe /></>} />
      </Routes>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  authValue.isAdmin = true;
  vi.mocked(listCliPermissionRequests).mockResolvedValue([pending]);
  vi.mocked(listCliPermissionRules).mockResolvedValue(rules);
  vi.mocked(enumerateCliPermissionRules).mockImplementation((async (query: unknown) => asPage(await vi.mocked(listCliPermissionRules)(query as never))) as never);
  vi.mocked(listVessels).mockResolvedValue(page([{ id: 'vsl_1', name: 'payments-api' } as never]));
  vi.mocked(listCaptains).mockResolvedValue(page([{ id: 'cpt_1', name: 'Ada' } as never]));
});

describe('CLI Tool Permissions page', () => {
  it('says when the rules list holds one page of a larger set', async () => {
    vi.mocked(enumerateCliPermissionRules).mockResolvedValue({ ...asPage(rules), totalRecords: 1500, totalPages: 2 });
    renderAt('/server?tab=cli-permissions&section=rules');
    expect(await screen.findByText('Bash(git status:*)')).toBeInTheDocument();
    expect(screen.getByTestId('truncation-note')).toHaveTextContent(`Showing ${rules.length} of 1500`);
  });

  it('fits in the Settings tab: no page header, Requests and Rules as sub-sections synced to ?section=', async () => {
    renderAt('/server?tab=cli-permissions&request=cpr_1');
    await screen.findByRole('group', { name: 'CLI permission: Bash' });
    expect(screen.queryByRole('heading', { level: 1 })).not.toBeInTheDocument();
    expect(screen.queryByRole('tablist')).not.toBeInTheDocument();
    const sections = screen.getByRole('radiogroup', { name: 'CLI Tool Permissions' });
    expect(within(sections).getByRole('radio', { name: 'Requests' })).toBeChecked();
    expect(screen.queryByRole('region', { name: 'Rules' })).not.toBeInTheDocument();

    fireEvent.click(within(sections).getByRole('radio', { name: 'Rules' }));
    expect(await screen.findByText('Bash(git status:*)')).toBeInTheDocument();
    // The Settings tab stays selected and the request highlight is dropped when leaving Requests.
    expect(screen.getByTestId('location').textContent).toBe('/server?tab=cli-permissions&section=rules');
    expect(listCliPermissionRules).toHaveBeenCalledWith({ scope: null });
  });

  it('edits a rule in place: pattern, action, and description change, scope and target stay locked', async () => {
    vi.mocked(updateCliPermissionRule).mockResolvedValue({ ...rules[1], pattern: 'Bash(rm -rf build:*)', action: 'Allow' });
    renderAt('/server?tab=cli-permissions&section=rules');
    fireEvent.click(await screen.findByRole('button', { name: 'Edit rule Bash(rm -rf:*)' }));
    const form = screen.getByRole('form', { name: 'Edit rule' });
    expect(within(form).getByLabelText('Pattern')).toHaveValue('Bash(rm -rf:*)');
    expect(within(form).getByLabelText('Scope')).toBeDisabled();
    expect(within(form).getByLabelText('Vessel')).toBeDisabled();
    expect(within(form).getByLabelText('Description')).toHaveValue('Never wipe');
    fireEvent.change(within(form).getByLabelText('Pattern'), { target: { value: 'Bash(rm -rf build:*)' } });
    fireEvent.change(within(form).getByLabelText('Action'), { target: { value: 'Allow' } });
    fireEvent.click(within(form).getByRole('button', { name: 'Save rule' }));
    await waitFor(() => expect(updateCliPermissionRule).toHaveBeenCalledWith('cpl_2', { pattern: 'Bash(rm -rf build:*)', action: 'Allow', description: 'Never wipe' }));
    expect(createCliPermissionRule).not.toHaveBeenCalled();
    expect(await screen.findByRole('form', { name: 'New rule' })).toBeInTheDocument();
  });

  it('cancels an edit without saving', async () => {
    renderAt('/server?tab=cli-permissions&section=rules');
    fireEvent.click(await screen.findByRole('button', { name: 'Edit rule Bash(git status:*)' }));
    const form = screen.getByRole('form', { name: 'Edit rule' });
    fireEvent.click(within(form).getByRole('button', { name: 'Cancel' }));
    const fresh = screen.getByRole('form', { name: 'New rule' });
    expect(within(fresh).getByLabelText('Pattern')).toHaveValue('');
    expect(updateCliPermissionRule).not.toHaveBeenCalled();
  });

  it('lists pending requests by default and highlights the deep-linked request even when it is decided', async () => {
    vi.mocked(getCliPermissionRequest).mockResolvedValue(decided);
    renderAt('/server?tab=cli-permissions&request=cpr_2');
    await waitFor(() => expect(listCliPermissionRequests).toHaveBeenCalledWith({ status: 'Pending', limit: 1000 }));
    const linked = await screen.findByRole('group', { name: 'CLI permission: WebFetch' });
    expect(linked.className).toContain('is-highlighted');
    expect(within(linked).getByText('Denied by a saved rule.')).toBeInTheDocument();
    const other = screen.getByRole('group', { name: 'CLI permission: Bash' });
    expect(other.className).not.toContain('is-highlighted');
    expect(within(other).getByRole('button', { name: 'Allow once' })).toBeInTheDocument();

    fireEvent.change(screen.getByRole('combobox', { name: 'Status' }), { target: { value: '' } });
    await waitFor(() => expect(listCliPermissionRequests).toHaveBeenLastCalledWith({ status: null, limit: 1000 }));
  });

  it('says visibly when the request list stops at the route limit', async () => {
    const many = Array.from({ length: 1000 }, (_, i) => ({ ...pending, id: `cpr_many_${i}` }));
    vi.mocked(listCliPermissionRequests).mockResolvedValue(many);
    renderAt('/server?tab=cli-permissions');
    expect(await screen.findByText('Showing the first 1,000 requests. Narrow the filter to see the rest.')).toBeInTheDocument();
  });

  it('shows no limit notice when fewer requests than the limit come back', async () => {
    renderAt('/server?tab=cli-permissions');
    await screen.findByRole('group', { name: 'CLI permission: Bash' });
    expect(screen.queryByText(/Narrow the filter to see the rest/)).not.toBeInTheDocument();
  });

  it('lists rules, creates a vessel rule, and deletes a rule after confirming', async () => {
    vi.mocked(createCliPermissionRule).mockResolvedValue({ id: 'cpl_3', scope: 'Vessel', vesselId: 'vsl_1', pattern: 'Bash(npm test:*)', action: 'Allow' });
    vi.mocked(deleteCliPermissionRule).mockResolvedValue(undefined);
    renderAt('/server?tab=cli-permissions&section=rules');
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
    renderAt('/server?tab=cli-permissions&section=rules');
    expect(await screen.findByText('Bash(git status:*)')).toBeInTheDocument();
    expect(screen.queryByRole('form', { name: 'New rule' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Delete rule/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Edit rule/ })).not.toBeInTheDocument();
  });
  it('rules table: shared column chooser with Pattern locked, refresh in the toolbar, patterns on one line', async () => {
    renderAt('/server?tab=cli-permissions&section=rules');
    const pattern = await screen.findByText('Bash(rm -rf:*)');
    expect(pattern).toHaveClass('cell-one-line');
    expect(pattern).toHaveAttribute('title', 'Bash(rm -rf:*)');
    const bar = document.querySelector('.data-table .pagination-bar') as HTMLElement;
    expect(within(bar).getByTitle('Reload the rule list.')).toBeInTheDocument();
    fireEvent.click(within(bar).getByRole('button', { name: /^Columns/ }));
    const menu = await screen.findByRole('menu', { name: 'Choose visible columns' });
    expect(within(menu).getByRole('menuitemcheckbox', { name: /^Pattern/ })).toHaveAttribute('aria-disabled', 'true');
    fireEvent.click(within(menu).getByRole('menuitemcheckbox', { name: /^Description/ }));
    expect(screen.queryByText('Never wipe')).not.toBeInTheDocument();
  });
});
