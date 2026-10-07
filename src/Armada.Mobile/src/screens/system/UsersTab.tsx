import { useMemo, useState } from 'react';
import { createUser, deleteUser, isApiStatus, listTenants, listUsers, updateUser } from '@dashboard/api/client';
import type { TenantMetadata, UserMaster, UserUpsertRequest } from '@dashboard/types/models';
import { useAuth } from '../../auth/AuthContext';
import { JsonSheet } from '../../components/resource/DetailParts';
import { FormSheet, bool, str, type FormField, type FormValues } from '../../components/resource/FormSheet';
import { ResourceList } from '../../components/resource/ResourceList';
import { useConfirm } from '../../components/resource/ResourceRow';
import { resourceStyles } from '../../components/resource/styles';
import { AppText } from '../../components/ui/AppText';
import { Button } from '../../components/ui/Button';
import { useLocale, type Translate } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { useLoad } from '../../resource/useLoad';
import { AdminRow, RemoteProxyBanner, typedDeleteMessage, useRemoteProxyMode } from './TenantAdminCommon';

/** Form values for a user (create when `user` is null), as the dashboard's form state. */
export function userFormValues(user: UserMaster | null, defaultTenantId: string): FormValues {
  return {
    email: user?.email ?? '', firstName: user?.firstName ?? '', lastName: user?.lastName ?? '',
    password: '', confirmPassword: '', currentPassword: '',
    isAdmin: user?.isAdmin ?? false, isTenantAdmin: user?.isTenantAdmin ?? false,
    tenantId: user?.tenantId ?? defaultTenantId, active: user?.active ?? true,
  };
}

/**
 * Validates the user form like the dashboard (password required on create, passwords match, current password when
 * changing your own) and returns the error, or null.
 */
export function userFormError(t: Translate, v: FormValues, creating: boolean, editingSelf: boolean): string | null {
  const password = str(v, 'password');
  if (creating && !password.trim()) return t('Password is required when creating a user.');
  if (password !== str(v, 'confirmPassword')) return t('Passwords do not match.');
  if (editingSelf && password.trim() && !str(v, 'currentPassword')) return t('Enter your current password to change your own password.');
  return null;
}

/** The upsert payload (password and current password only when given), as the dashboard builds it. */
export function userPayload(v: FormValues, editingSelf: boolean): UserUpsertRequest {
  const password = str(v, 'password');
  return {
    email: str(v, 'email'),
    firstName: str(v, 'firstName') || null,
    lastName: str(v, 'lastName') || null,
    tenantId: str(v, 'tenantId'),
    isAdmin: bool(v, 'isAdmin'),
    isTenantAdmin: bool(v, 'isTenantAdmin'),
    active: bool(v, 'active'),
    ...(password.trim() ? { password } : {}),
    ...(editingSelf && password.trim() ? { currentPassword: str(v, 'currentPassword') } : {}),
  };
}

/**
 * Settings > Users (admins and tenant admins; others see and edit themselves): email / name / tenant filters, create,
 * edit (new password with confirmation; your own password needs the current one), View JSON, delete and bulk delete
 * with the typed `delete` confirmation. Remote proxy mode is read-only.
 */
export function UsersTab() {
  const { t, formatRelativeTime } = useLocale();
  const { user, isAdmin, isTenantAdmin } = useAuth();
  const { pushToast } = useNotifications();
  const { remote, instanceId } = useRemoteProxyMode();
  const { confirm, dialog } = useConfirm('user-confirm');
  const ownTenant = user?.tenant ?? null;
  const canDelete = (isAdmin || isTenantAdmin) && !remote;

  const { data, loading, refreshing, error, reload, refresh } = useLoad(async () => {
    const [users, tenants] = await Promise.all([
      listUsers(),
      isAdmin ? listTenants() : Promise.resolve({ objects: ownTenant ? [ownTenant] : [] } as { objects: TenantMetadata[] }),
    ]);
    return { users: users.objects ?? [], tenants: tenants.objects ?? [] };
  }, [isAdmin, ownTenant?.id], { fallbackError: t('Failed to load users.') });
  const items = useMemo(() => data?.users ?? [], [data]);
  const tenants = useMemo(() => data?.tenants ?? [], [data]);
  const tenantName = (id: string) => tenants.find((tn) => tn.id === id)?.name ?? id;

  const [search, setSearch] = useState('');
  const [tenantFilter, setTenantFilter] = useState('all');
  const [selected, setSelected] = useState<string[]>([]);
  const [editing, setEditing] = useState<UserMaster | 'new' | null>(null);
  const [json, setJson] = useState<UserMaster | null>(null);

  const filtered = useMemo(() => items
    // One search box covers the dashboard's email and name column filters.
    .filter((u) => (!search || u.email.toLowerCase().includes(search.toLowerCase())
        || `${u.firstName ?? ''} ${u.lastName ?? ''}`.toLowerCase().includes(search.toLowerCase()))
      && (tenantFilter === 'all' || u.tenantId === tenantFilter))
    .sort((a, b) => a.email.toLowerCase().localeCompare(b.email.toLowerCase())), [items, search, tenantFilter]);

  const editingUser = editing && editing !== 'new' ? editing : null;
  const editingSelf = !!editingUser && !!user?.user?.id && editingUser.id === user.user.id;

  function remove(u: UserMaster) {
    confirm({
      title: t('Delete User'),
      message: typedDeleteMessage(t, t('Delete user "{{email}}"? This cannot be undone.', { email: u.email }), u.email),
      confirmLabel: t('Yes'),
      danger: true,
      typed: 'delete',
      onConfirm: async () => {
        try {
          await deleteUser(u.id);
          pushToast('warning', t('User "{{email}}" deleted.', { email: u.email }));
        } catch {
          pushToast('error', t('Delete failed.'));
        }
        await reload();
      },
    });
  }

  function removeSelected() {
    const ids = [...selected];
    confirm({
      title: t('Delete Selected Users'),
      message: typedDeleteMessage(t, t('Delete {{count}} user(s)?', { count: ids.length }), `${ids.length} user(s)`),
      confirmLabel: t('Yes'),
      danger: true,
      typed: 'delete',
      onConfirm: async () => {
        setSelected([]);
        let failed = 0;
        for (const id of ids) { try { await deleteUser(id); } catch { failed++; } }
        const success = ids.length - failed;
        if (success > 0) {
          pushToast(failed > 0 ? 'warning' : 'success', failed > 0
            ? t('Deleted {{success}} users. {{failed}} failed.', { success, failed })
            : t('Deleted {{success}} users.', { success }));
        }
        if (failed > 0) pushToast('error', t('Deleted {{success}}, {{failed}} failed.', { success, failed }));
        await reload();
      },
    });
  }

  function fields(v: FormValues): FormField[] {
    const creating = editing === 'new';
    const tenantChoices = isAdmin ? tenants : tenants.filter((tn) => tn.id === str(v, 'tenantId') || tn.id === ownTenant?.id);
    return [
      { kind: 'text', key: 'email', label: t('Email'), required: true },
      { kind: 'text', key: 'firstName', label: t('First Name') },
      { kind: 'text', key: 'lastName', label: t('Last Name') },
      { kind: 'secret', key: 'password', label: creating ? t('Password') : t('New Password'), placeholder: creating ? t('Enter password') : t('Leave blank to keep current password') },
      { kind: 'secret', key: 'confirmPassword', label: creating ? t('Confirm Password') : t('Confirm New Password'), placeholder: creating ? t('Repeat password') : t('Repeat new password') },
      ...(editingSelf ? [{ kind: 'secret' as const, key: 'currentPassword', label: t('Current Password'), placeholder: t('Required to change your own password') }] : []),
      {
        kind: 'select', key: 'tenantId', label: t('Tenant'), disabled: !isAdmin, placeholder: t('Select tenant...'),
        options: [...(isAdmin ? [{ value: '', label: t('Select tenant...') }] : []), ...tenantChoices.map((tn) => ({ value: tn.id, label: tn.name }))],
      },
      ...(isAdmin ? [{ kind: 'switch' as const, key: 'isAdmin', label: t('Global Admin') }] : []),
      ...(isTenantAdmin ? [{ kind: 'switch' as const, key: 'isTenantAdmin', label: t('Tenant Admin') }] : []),
      ...(!creating ? [{ kind: 'switch' as const, key: 'active', label: t('Active') }] : []),
    ];
  }

  const subtitle = isAdmin
    ? t('Manage user accounts across all tenants.')
    : isTenantAdmin ? t('Manage user accounts within your tenant.') : t('View and update your own user account.');

  return (
    <>
      <ResourceList
        testID="users"
        items={filtered}
        keyOf={(u) => u.id}
        loading={loading}
        error={error}
        onRetry={() => void reload()}
        refreshing={refreshing}
        onRefresh={() => void refresh()}
        search={{ value: search, onChange: setSearch, placeholder: t('Search...') }}
        filters={[
          { key: 'tenant', label: t('Tenant'), value: tenantFilter, onChange: setTenantFilter, options: [{ value: 'all', label: t('All tenants') }, ...tenants.map((tn) => ({ value: tn.id, label: tn.name }))] },
        ]}
        header={(
          <>
            <AppText muted style={resourceStyles.pad}>{subtitle}</AppText>
            {remote ? <RemoteProxyBanner entity="User" instanceId={instanceId} /> : null}
            {canDelete ? <Button label={`+ ${t('User')}`} onPress={() => setEditing('new')} style={resourceStyles.create} testID="users-create" /> : null}
            {canDelete && selected.length > 0 ? (
              <Button label={`${t('Delete Selected')} (${selected.length})`} variant="danger" onPress={removeSelected} style={resourceStyles.create} testID="users-delete-selected" />
            ) : null}
          </>
        )}
        emptyTitle={items.length > 0 ? t('No users match filters.') : t('No users found.')}
        renderItem={(u) => (
          <AdminRow
            testID={`user-row-${u.id}`}
            title={u.email}
            subtitle={[[u.firstName, u.lastName].filter(Boolean).join(' ') || null, tenantName(u.tenantId), u.isAdmin ? t('Global Admin') : u.isTenantAdmin ? t('Tenant Admin') : null].filter(Boolean).join(' \u2022 ')}
            badge={{ label: u.active ? t('Active') : t('Inactive'), tone: u.active ? 'success' : 'cancelled' }}
            meta={formatRelativeTime(u.createdUtc)}
            selected={selected.includes(u.id)}
            onPress={() => (remote ? setJson(u) : setEditing(u))}
            onToggleSelect={canDelete ? () => setSelected((s) => (s.includes(u.id) ? s.filter((x) => x !== u.id) : [...s, u.id])) : undefined}
            actions={[
              ...(remote ? [] : [{ key: 'edit', label: t('Edit'), icon: 'create-outline' as const, onPress: () => setEditing(u) }]),
              { key: 'json', label: t('View JSON'), icon: 'code-slash-outline' as const, onPress: () => setJson(u) },
              ...(canDelete ? [{ key: 'delete', label: t('Delete'), icon: 'trash-outline' as const, tone: 'danger' as const, onPress: () => remove(u) }] : []),
            ]}
          />
        )}
      />
      <FormSheet
        testID="user-form"
        open={editing !== null}
        title={editing === 'new' ? t('Create User') : t('Edit User')}
        initial={userFormValues(editingUser, tenants[0]?.id ?? ownTenant?.id ?? '')}
        fields={fields}
        validate={(v) => userFormError(t, v, editing === 'new', editingSelf)}
        submitLabel={t('Save')}
        onClose={() => setEditing(null)}
        onSubmit={async (v) => {
          try {
            if (editingUser) await updateUser(editingUser.id, userPayload(v, editingSelf));
            else await createUser(userPayload(v, false));
          } catch (err: unknown) {
            if (editingSelf && str(v, 'password').trim() && isApiStatus(err, 403)) throw new Error(t('Current password is incorrect.'));
            throw err instanceof Error ? err : new Error(t('Save failed.'));
          }
          const email = str(v, 'email');
          pushToast('success', editingUser ? t('User "{{email}}" saved.', { email }) : t('User "{{email}}" created.', { email }));
          setEditing(null);
          await reload();
        }}
      />
      <JsonSheet open={json !== null} title={json ? `${t('User')}: ${json.email}` : ''} data={json} onClose={() => setJson(null)} />
      {dialog}
    </>
  );
}
