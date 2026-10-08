import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import * as client from '@dashboard/api/client';
import type { Credential, TenantMetadata, UserMaster } from '@dashboard/types/models';
import MoreLayout from '../app/(app)/(more)/_layout';
import ServerRoute from '../app/(app)/(more)/server';
import { userFormError, userFormValues, userPayload } from '../screens/system/UsersTab';
import { page, renderW4Routes, resetW4 } from '../test/w4';
import { rowActionTarget } from '../test/a11y';

jest.mock('@dashboard/api/client', () => require('../test/w4Client').autoMockClient());

const api = client as jest.Mocked<typeof client>;
const t = (text: string, params?: Record<string, string | number | null | undefined>) =>
  text.replace(/\{\{(\w+)\}\}/g, (_m, k: string) => String(params?.[k] ?? ''));

const TENANT: TenantMetadata = { id: 'ten_1', name: 'Default', active: true, isProtected: true, createdUtc: '2026-10-01T00:00:00Z', lastUpdateUtc: '2026-10-01T00:00:00Z' };
const TENANT2: TenantMetadata = { ...TENANT, id: 'ten_2', name: 'Acme', isProtected: false };
function user(over: Partial<UserMaster> = {}): UserMaster {
  return { id: 'usr_2', tenantId: 'ten_1', email: 'dev@armada', firstName: 'Dev', lastName: 'One', isAdmin: false, isTenantAdmin: false, isProtected: false, active: true, createdUtc: '2026-10-01T00:00:00Z', lastUpdateUtc: '2026-10-01T00:00:00Z', ...over };
}
const CRED: Credential = { id: 'crd_1', tenantId: 'ten_1', userId: 'usr_2', name: 'CI', bearerToken: 'abcd****', isProtected: false, active: true, createdUtc: '2026-10-01T00:00:00Z', lastUpdateUtc: '2026-10-01T00:00:00Z' };

const ROUTES = {
  '(more)/_layout': MoreLayout,
  '(more)/more': () => null,
  '(more)/server': ServerRoute,
};

beforeEach(async () => {
  await resetW4();
  jest.clearAllMocks();
  api.listTenants.mockResolvedValue(page([TENANT, TENANT2]) as never);
  api.listUsers.mockResolvedValue(page([user(), user({ id: 'usr_1', email: 'someone@armada', isAdmin: true })]) as never);
  api.listCredentials.mockResolvedValue(page([CRED]) as never);
  api.getProxySessionContext.mockResolvedValue(null);
});

describe('user form rules (as the dashboard)', () => {
  it('requires a password on create, matching confirmation, and the current password for your own', () => {
    const v = userFormValues(null, 'ten_1');
    expect(userFormError(t, v, true, false)).toBe('Password is required when creating a user.');
    expect(userFormError(t, { ...v, password: 'a', confirmPassword: 'b' }, true, false)).toBe('Passwords do not match.');
    expect(userFormError(t, { ...v, password: 'a', confirmPassword: 'a' }, false, true)).toBe('Enter your current password to change your own password.');
    expect(userFormError(t, { ...v }, false, true)).toBeNull();
  });

  it('sends the password only when given and the current password only for your own', () => {
    const v = { ...userFormValues(user(), 'ten_1'), password: 'pw', confirmPassword: 'pw', currentPassword: 'old', firstName: '' };
    expect(userPayload(v, true)).toMatchObject({ email: 'dev@armada', firstName: null, password: 'pw', currentPassword: 'old' });
    expect(userPayload(v, false)).not.toHaveProperty('currentPassword');
    expect(userPayload({ ...v, password: '' }, true)).not.toHaveProperty('password');
  });
});

describe('Settings hub admin tabs', () => {
  it('regular users see no Tenants, Users, or Credentials tabs', async () => {
    await renderW4Routes(ROUTES, '/server?tab=users', 'user');
    await waitFor(() => expect(screen.getByTestId('server-tab-server')).toBeTruthy());
    expect(screen.queryByTestId('server-tab-tenants')).toBeNull();
    expect(screen.queryByTestId('server-tab-users')).toBeNull();
    expect(screen.queryByTestId('server-tab-credentials')).toBeNull();
  });

  it('tenant admins get Users and Credentials but not Tenants', async () => {
    await renderW4Routes(ROUTES, '/server?tab=users', 'tenantAdmin');
    await waitFor(() => expect(screen.getByTestId('user-row-usr_2')).toBeTruthy());
    expect(screen.getByTestId('server-tab-credentials')).toBeTruthy();
    expect(screen.queryByTestId('server-tab-tenants')).toBeNull();
  });

  it('creates a tenant and shows the generated admin password once', async () => {
    api.createTenant.mockResolvedValue({ ...TENANT2, id: 'ten_3', name: 'New', adminEmail: 'admin@armada', adminPassword: 'Gen3rated!' });
    await renderW4Routes(ROUTES, '/server?tab=tenants');
    await waitFor(() => expect(screen.getByTestId('tenant-row-ten_2')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('tenants-create'));
    await waitFor(() => expect(screen.getByTestId('tenant-form-name')).toBeTruthy());
    await fireEvent.changeText(screen.getByTestId('tenant-form-name'), 'New');
    await fireEvent.changeText(screen.getByTestId('tenant-form-adminPassword'), 'short');
    await act(async () => { await fireEvent.press(screen.getByTestId('tenant-form-submit')); });
    expect(screen.getByText('Admin password must be at least 8 characters.')).toBeTruthy();
    expect(api.createTenant).not.toHaveBeenCalled();
    await fireEvent.changeText(screen.getByTestId('tenant-form-adminPassword'), '');
    await act(async () => { await fireEvent.press(screen.getByTestId('tenant-form-submit')); });
    expect(api.createTenant).toHaveBeenCalledWith({ name: 'New', active: true });
    await waitFor(() => expect(screen.getByTestId('tenant-password-once-secret')).toHaveTextContent('Gen3rated!'));
    await fireEvent.press(screen.getByTestId('tenant-password-once-done'));
    await waitFor(() => expect(screen.queryByTestId('tenant-password-once-secret')).toBeNull());
  });

  it('deletes a tenant only after typing delete', async () => {
    api.deleteTenant.mockResolvedValue(undefined as never);
    await renderW4Routes(ROUTES, '/server?tab=tenants');
    await waitFor(() => expect(screen.getByTestId('tenant-row-ten_2')).toBeTruthy());
    await fireEvent(rowActionTarget(screen.getByTestId('tenant-row-ten_2-swipe'), 'delete'), 'accessibilityAction', { nativeEvent: { actionName: 'delete' } });
    await waitFor(() => expect(screen.getByTestId('tenant-confirm-typed')).toBeTruthy());
    expect(screen.getByText(/Are you sure you wish to delete: Acme/)).toBeTruthy();
    await act(async () => { await fireEvent.press(screen.getByTestId('tenant-confirm-confirm')); });
    expect(api.deleteTenant).not.toHaveBeenCalled();
    await fireEvent.changeText(screen.getByTestId('tenant-confirm-typed'), 'delete');
    await act(async () => { await fireEvent.press(screen.getByTestId('tenant-confirm-confirm')); });
    await waitFor(() => expect(api.deleteTenant).toHaveBeenCalledWith('ten_2'));
  });

  it('creates a user with the dashboard payload', async () => {
    api.createUser.mockResolvedValue(user({ id: 'usr_9', email: 'new@armada' }));
    await renderW4Routes(ROUTES, '/server?tab=users');
    await waitFor(() => expect(screen.getByTestId('user-row-usr_2')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('users-create'));
    await waitFor(() => expect(screen.getByTestId('user-form-email')).toBeTruthy());
    await fireEvent.changeText(screen.getByTestId('user-form-email'), 'new@armada');
    await fireEvent.changeText(screen.getByTestId('user-form-password'), 'pw1');
    await fireEvent.changeText(screen.getByTestId('user-form-confirmPassword'), 'pw2');
    await act(async () => { await fireEvent.press(screen.getByTestId('user-form-submit')); });
    expect(screen.getByText('Passwords do not match.')).toBeTruthy();
    await fireEvent.changeText(screen.getByTestId('user-form-confirmPassword'), 'pw1');
    await act(async () => { await fireEvent.press(screen.getByTestId('user-form-submit')); });
    expect(api.createUser).toHaveBeenCalledWith(expect.objectContaining({ email: 'new@armada', password: 'pw1', tenantId: 'ten_1', isAdmin: false, active: true }));
  });

  it('creates a credential and shows its bearer token once', async () => {
    api.createCredential.mockResolvedValue({ ...CRED, id: 'crd_2', bearerToken: 'full-secret-token' });
    await renderW4Routes(ROUTES, '/server?tab=credentials');
    await waitFor(() => expect(screen.getByTestId('credential-row-crd_1')).toBeTruthy());
    expect(screen.getByText(/abcd\*\*\*\*/)).toBeTruthy();
    expect(screen.queryByText('full-secret-token')).toBeNull();
    await fireEvent.press(screen.getByTestId('credentials-create'));
    await waitFor(() => expect(screen.getByTestId('credential-form-name')).toBeTruthy());
    await fireEvent.changeText(screen.getByTestId('credential-form-name'), 'Phone');
    await act(async () => { await fireEvent.press(screen.getByTestId('credential-form-submit')); });
    expect(api.createCredential).toHaveBeenCalledWith({ userId: 'usr_2', tenantId: 'ten_1', name: 'Phone' });
    await waitFor(() => expect(screen.getByTestId('credential-token-once-secret')).toHaveTextContent('full-secret-token'));
  });

  it('remote proxy mode blocks create and shows the warning', async () => {
    api.getProxySessionContext.mockResolvedValue({ selectedInstanceId: 'armada-e2e' } as never);
    await renderW4Routes(ROUTES, '/server?tab=credentials');
    await waitFor(() => expect(screen.getByTestId('admin-remote-banner')).toBeTruthy());
    expect(screen.queryByTestId('credentials-create')).toBeNull();
  });
});
