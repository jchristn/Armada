import { act, fireEvent, render, screen, within } from '@testing-library/react';
import Credentials from './Credentials';
import { listCredentials, listTenants, listUsers } from '../../api/client';
import type { Credential } from '../../types/models';

const translate = (text: string, params?: Record<string, string | number | null | undefined>) => {
  if (!params) return text;
  return Object.entries(params).reduce(
    (current, [key, value]) => current.split(`{{${key}}}`).join(value == null ? '' : String(value)),
    text,
  );
};
const localeValue = { t: translate, formatDateTime: (v: string | null | undefined) => v ?? '', formatRelativeTime: (v: string | null | undefined) => v ?? '' };
const notifications = { pushToast: vi.fn() };

vi.mock('../../api/client', () => ({
  listCredentials: vi.fn(),
  listUsers: vi.fn(),
  listTenants: vi.fn(),
  createCredential: vi.fn(),
  updateCredential: vi.fn(),
  deleteCredential: vi.fn(),
}));
vi.mock('../../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../../context/NotificationContext', () => ({ useNotifications: () => notifications }));
vi.mock('../../lib/useAutoRefresh', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../lib/useAutoRefresh')>()),
  useAutoRefresh: () => ({ seconds: 30, setSeconds: vi.fn() }),
}));
const auth = { user: null, isAdmin: true, isTenantAdmin: true };
vi.mock('../../context/AuthContext', () => ({ useAuth: () => auth }));
vi.mock('../../lib/useProxySessionContext', () => ({ useProxySessionContext: () => null }));

const credential: Credential = {
  id: 'crd_1', tenantId: 'ten_1', userId: 'usr_1', name: 'CI token', bearerToken: '****abcd', isProtected: false, active: true,
  createdUtc: '2026-10-01T00:00:00Z', lastUpdateUtc: '2026-10-01T00:00:00Z',
};

function page<T>(objects: T[]) {
  return { success: true, pageNumber: 1, pageSize: 9999, totalPages: 1, totalRecords: objects.length, totalMs: 1, objects } as never;
}

describe('Credentials table', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.mocked(listCredentials).mockResolvedValue(page([credential]));
    vi.mocked(listUsers).mockResolvedValue(page([]));
    vi.mocked(listTenants).mockResolvedValue(page([]));
  });

  it('puts refresh controls in the toolbar and locks Name and ID in the column chooser', async () => {
    const { container } = render(<Credentials />);
    await screen.findByText('CI token');
    const bar = container.querySelector('.data-table .pagination-bar') as HTMLElement;
    expect(within(bar).getByTitle('Refresh credentials')).toBeInTheDocument();
    expect(within(bar).getByLabelText('Auto-refresh interval')).toBeInTheDocument();
    expect(container.querySelector('.view-actions .auto-refresh-select')).toBeNull();
    await act(async () => { fireEvent.click(within(bar).getByRole('button', { name: /^Columns/ })); });
    const menu = screen.getByRole('menu', { name: 'Choose visible columns' });
    expect(within(menu).getByRole('menuitemcheckbox', { name: /^Name/ })).toHaveAttribute('aria-disabled', 'true');
    expect(within(menu).getByRole('menuitemcheckbox', { name: /^ID/ })).toHaveAttribute('aria-disabled', 'true');
    fireEvent.click(within(menu).getByRole('menuitemcheckbox', { name: /^Bearer Token/ }));
    expect(screen.queryByText('****abcd')).not.toBeInTheDocument();
  });
});
