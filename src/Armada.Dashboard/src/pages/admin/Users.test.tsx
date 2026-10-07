import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import Users from './Users';
import { ApiError, listUsers, updateUser } from '../../api/client';
import { onlyCallArgs } from '../../test/mockCalls';
import type { UserMaster, UserUpsertRequest } from '../../types/models';

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
    listUsers: vi.fn(),
    listTenants: vi.fn(),
    createUser: vi.fn(),
    updateUser: vi.fn(),
    deleteUser: vi.fn(),
  };
});

const tenant = { id: 'ten_1', name: 'Default', active: true, isProtected: false, createdUtc: '2026-01-01T00:00:00Z', lastUpdateUtc: '2026-01-01T00:00:00Z' };

function makeUser(id: string, email: string): UserMaster {
  return {
    id, tenantId: 'ten_1', email, firstName: null, lastName: null, isAdmin: false, isTenantAdmin: true,
    isProtected: false, active: true, createdUtc: '2026-01-01T00:00:00Z', lastUpdateUtc: '2026-01-01T00:00:00Z',
  };
}

const self = makeUser('usr_self', 'me@example.com');
const other = makeUser('usr_other', 'other@example.com');

vi.mock('../../context/AuthContext', () => ({
  useAuth: () => ({
    user: { tenant, user: self },
    isAdmin: false,
    isTenantAdmin: true,
  }),
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

async function openEditor(email: string) {
  render(<Users />);
  fireEvent.click(await screen.findByText(email));
  await screen.findByText('Edit User');
}

function setNewPassword(value: string) {
  fireEvent.change(screen.getByPlaceholderText('Leave blank to keep current password'), { target: { value } });
  fireEvent.change(screen.getByPlaceholderText('Repeat new password'), { target: { value } });
}

function save() {
  fireEvent.click(screen.getByRole('button', { name: 'Save' }));
}

describe('Users edit form password change', () => {
  beforeEach(() => {
    vi.mocked(listUsers).mockResolvedValue({
      success: true, pageNumber: 1, pageSize: 9999, totalPages: 1, totalRecords: 2, totalMs: 1, objects: [self, other],
    } as Awaited<ReturnType<typeof listUsers>>);
    vi.mocked(updateUser).mockReset();
    vi.mocked(updateUser).mockResolvedValue(self);
  });

  it('requires the current password before changing your own password', async () => {
    await openEditor('me@example.com');
    const current = screen.getByLabelText('Current Password');
    expect(current).toHaveAttribute('type', 'password');
    expect(current).toHaveAttribute('autocomplete', 'current-password');
    setNewPassword('new-password-1');
    expect(current).toBeRequired();
    // Bypass native constraint validation to reach the submit handler's own check.
    fireEvent.submit(screen.getByRole('button', { name: 'Save' }).closest('form')!);
    expect(await screen.findByText('Enter your current password to change your own password.')).toBeInTheDocument();
    expect(updateUser).not.toHaveBeenCalled();
  });

  it('sends CurrentPassword with your own password change', async () => {
    await openEditor('me@example.com');
    setNewPassword('new-password-1');
    fireEvent.change(screen.getByLabelText('Current Password'), { target: { value: 'old-password-1' } });
    save();
    await waitFor(() => expect(updateUser).toHaveBeenCalled());
    const [id, body] = onlyCallArgs(vi.mocked(updateUser)) as [string, UserUpsertRequest];
    expect(id).toBe('usr_self');
    expect(body.password).toBe('new-password-1');
    expect(body.currentPassword).toBe('old-password-1');
  });

  it('does not send CurrentPassword when your own password is unchanged', async () => {
    await openEditor('me@example.com');
    expect(screen.getByLabelText('Current Password')).not.toBeRequired();
    save();
    await waitFor(() => expect(updateUser).toHaveBeenCalled());
    const [, body] = onlyCallArgs(vi.mocked(updateUser)) as [string, UserUpsertRequest];
    expect(body.password).toBeUndefined();
    expect('currentPassword' in body).toBe(false);
  });

  it('shows a clear message when the server rejects the current password (403)', async () => {
    vi.mocked(updateUser).mockRejectedValue(new ApiError('CurrentPassword is incorrect', 403, null));
    await openEditor('me@example.com');
    setNewPassword('new-password-1');
    fireEvent.change(screen.getByLabelText('Current Password'), { target: { value: 'wrong-password' } });
    save();
    expect(await screen.findByText('Current password is incorrect.')).toBeInTheDocument();
  });

  it('does not ask for or send a current password when editing another user', async () => {
    await openEditor('other@example.com');
    expect(screen.queryByLabelText('Current Password')).not.toBeInTheDocument();
    setNewPassword('new-password-1');
    save();
    await waitFor(() => expect(updateUser).toHaveBeenCalled());
    const [id, body] = onlyCallArgs(vi.mocked(updateUser)) as [string, UserUpsertRequest];
    expect(id).toBe('usr_other');
    expect(body.password).toBe('new-password-1');
    expect('currentPassword' in body).toBe(false);
  });
});

async function openColumnChooser() {
  await act(async () => { fireEvent.click(screen.getByRole('button', { name: /^Columns/ })); });
  return screen.getByRole('menu', { name: 'Choose visible columns' });
}

describe('Users table', () => {
  beforeEach(() => {
    window.localStorage.clear();
    vi.mocked(listUsers).mockResolvedValue({
      success: true, pageNumber: 1, pageSize: 9999, totalPages: 1, totalRecords: 2, totalMs: 1, objects: [self, other],
    } as Awaited<ReturnType<typeof listUsers>>);
  });

  it('locks Email and ID in the column chooser and hides an optional column', async () => {
    render(<Users />);
    await screen.findByText('me@example.com');
    const menu = await openColumnChooser();
    expect(within(menu).getByRole('menuitemcheckbox', { name: /^Email/ })).toHaveAttribute('aria-disabled', 'true');
    expect(within(menu).getByRole('menuitemcheckbox', { name: /^ID/ })).toHaveAttribute('aria-disabled', 'true');
    expect(screen.getByRole('columnheader', { name: /Tenant Admin/ })).toBeInTheDocument();
    fireEvent.click(within(menu).getByRole('menuitemcheckbox', { name: /^Tenant Admin/ }));
    expect(screen.queryByRole('columnheader', { name: /Tenant Admin/ })).not.toBeInTheDocument();
    expect(screen.getByText('me@example.com')).toBeInTheDocument();
  });
});
