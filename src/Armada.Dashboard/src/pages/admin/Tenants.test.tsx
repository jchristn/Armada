import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import Tenants from './Tenants';
import { createTenant, listTenants } from '../../api/client';
import { onlyCallArgs } from '../../test/mockCalls';
import type { TenantCreateRequest, TenantCreateResult } from '../../types/models';

const translate = (text: string, params?: Record<string, string | number | null | undefined>) => {
  if (!params) return text;
  return Object.entries(params).reduce(
    (current, [key, value]) => current.split(`{{${key}}}`).join(value == null ? '' : String(value)),
    text,
  );
};

vi.mock('../../api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/client')>();
  return {
    ...actual,
    listTenants: vi.fn(),
    createTenant: vi.fn(),
    updateTenant: vi.fn(),
    deleteTenant: vi.fn(),
  };
});

vi.mock('../../context/AuthContext', () => ({
  useAuth: () => ({ user: null, isAdmin: true, isTenantAdmin: true }),
}));

vi.mock('../../context/LocaleContext', () => ({
  useLocale: () => ({
    t: translate,
    formatDateTime: (value: string | null | undefined) => value ?? '',
    formatRelativeTime: (value: string | null | undefined) => value ?? '',
  }),
}));

vi.mock('../../context/NotificationContext', () => ({
  useNotifications: () => ({ pushToast: vi.fn() }),
}));

vi.mock('../../lib/useProxySessionContext', () => ({
  useProxySessionContext: () => null,
}));

vi.mock('../../lib/useAutoRefresh', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../lib/useAutoRefresh')>()),
  useAutoRefresh: () => ({ seconds: 0, setSeconds: vi.fn() }),
}));

const GENERATED = 'Gen3rated-Secret-Value';

function result(adminPassword: string | null): TenantCreateResult {
  return {
    id: 'ten_new', name: 'Acme', active: true, isProtected: false, createdUtc: '2026-01-01T00:00:00Z',
    lastUpdateUtc: '2026-01-01T00:00:00Z', adminEmail: 'admin@armada', adminPassword,
  };
}

async function openCreate() {
  render(<Tenants />);
  await waitFor(() => expect(listTenants).toHaveBeenCalled());
  fireEvent.click(screen.getByRole('button', { name: '+ Tenant' }));
  await screen.findByText('Create Tenant');
  fireEvent.change(screen.getByRole('textbox', { name: 'Name' }), { target: { value: 'Acme' } });
}

describe('Tenants create admin password', () => {
  beforeEach(() => {
    vi.mocked(listTenants).mockResolvedValue({
      success: true, pageNumber: 1, pageSize: 9999, totalPages: 1, totalRecords: 0, totalMs: 1, objects: [],
    } as Awaited<ReturnType<typeof listTenants>>);
    vi.mocked(createTenant).mockReset();
    window.localStorage.clear();
    window.sessionStorage.clear();
  });

  it('sends a supplied admin password and shows no one-time dialog', async () => {
    vi.mocked(createTenant).mockResolvedValue(result(null));
    await openCreate();
    const field = screen.getByLabelText(/Admin Password \(optional\)/);
    expect(field).toHaveAttribute('type', 'password');
    expect(field).toHaveAttribute('autocomplete', 'new-password');
    fireEvent.change(field, { target: { value: 'a-strong-password' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(createTenant).toHaveBeenCalled());
    const [body] = onlyCallArgs(vi.mocked(createTenant)) as [TenantCreateRequest];
    expect(body.name).toBe('Acme');
    expect(body.adminPassword).toBe('a-strong-password');
    expect(screen.queryByText('Tenant admin password (shown once)')).not.toBeInTheDocument();
  });

  it('rejects a supplied admin password shorter than 8 characters without calling the server', async () => {
    await openCreate();
    fireEvent.change(screen.getByLabelText(/Admin Password \(optional\)/), { target: { value: 'short' } });
    fireEvent.submit(screen.getByRole('button', { name: 'Save' }).closest('form')!);
    expect(await screen.findByText('Admin password must be at least 8 characters.')).toBeInTheDocument();
    expect(createTenant).not.toHaveBeenCalled();
  });

  it('omits AdminPassword when empty and shows the generated password once, then forgets it', async () => {
    vi.mocked(createTenant).mockResolvedValue(result(GENERATED));
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    await openCreate();
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(createTenant).toHaveBeenCalled());
    const [body] = onlyCallArgs(vi.mocked(createTenant)) as [TenantCreateRequest];
    expect('adminPassword' in body).toBe(false);

    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText('Tenant admin password (shown once)')).toBeInTheDocument();
    expect(within(dialog).getByText('Copy this password now. It is shown only once and cannot be retrieved later.')).toBeInTheDocument();
    expect(within(dialog).getByDisplayValue('admin@armada')).toBeInTheDocument();
    expect(within(dialog).getByLabelText('Generated password')).toHaveValue(GENERATED);

    fireEvent.click(within(dialog).getByRole('button', { name: 'Copy password' }));
    await waitFor(() => expect(writeText).toHaveBeenCalledWith(GENERATED));
    expect(await within(dialog).findByRole('button', { name: 'Copied!' })).toBeInTheDocument();

    fireEvent.click(within(dialog).getByRole('button', { name: 'I have saved it' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(screen.queryByDisplayValue(GENERATED)).not.toBeInTheDocument();
    expect(document.body.innerHTML).not.toContain(GENERATED);

    const stored = [
      ...Object.keys(window.localStorage).map(k => window.localStorage.getItem(k) ?? ''),
      ...Object.keys(window.sessionStorage).map(k => window.sessionStorage.getItem(k) ?? ''),
    ];
    expect(stored.some(v => v.includes(GENERATED))).toBe(false);
  });
});

async function openColumnChooser() {
  await act(async () => { fireEvent.click(screen.getByRole('button', { name: /^Columns/ })); });
  return screen.getByRole('menu', { name: 'Choose visible columns' });
}

describe('Tenants table', () => {
  beforeEach(() => window.localStorage.clear());

  it('keeps the toolbar (refresh, auto-refresh, columns) when there are no tenants, and locks Name and ID', async () => {
    vi.mocked(listTenants).mockResolvedValue({
      success: true, pageNumber: 1, pageSize: 9999, totalPages: 1, totalRecords: 0, totalMs: 1, objects: [],
    } as Awaited<ReturnType<typeof listTenants>>);
    const { container } = render(<Tenants />);
    expect(await screen.findByText('No tenants found.')).toBeInTheDocument();
    const bar = container.querySelector('.data-table .pagination-bar') as HTMLElement;
    expect(within(bar).getByLabelText('Auto-refresh interval')).toBeInTheDocument();
    expect(within(bar).getByTitle('Refresh tenants')).toBeInTheDocument();
    expect(container.querySelector('.view-actions .auto-refresh-select')).toBeNull();
    const menu = await openColumnChooser();
    expect(within(menu).getByRole('menuitemcheckbox', { name: /^Name/ })).toHaveAttribute('aria-disabled', 'true');
    expect(within(menu).getByRole('menuitemcheckbox', { name: /^ID/ })).toHaveAttribute('aria-disabled', 'true');
    expect(within(menu).getByRole('menuitemcheckbox', { name: /^Last Updated/ })).not.toHaveAttribute('aria-disabled');
  });
});
