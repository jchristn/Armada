import { useMemo, useState } from 'react';
import { createTenant, deleteTenant, listTenants, updateTenant } from '@dashboard/api/client';
import type { TenantCreateRequest, TenantMetadata } from '@dashboard/types/models';
import { useAuth } from '../../auth/AuthContext';
import { JsonSheet } from '../../components/resource/DetailParts';
import { FormSheet, bool, str } from '../../components/resource/FormSheet';
import { ResourceList } from '../../components/resource/ResourceList';
import { useConfirm } from '../../components/resource/ResourceRow';
import { resourceStyles } from '../../components/resource/styles';
import { AppText } from '../../components/ui/AppText';
import { Button } from '../../components/ui/Button';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { useLoad } from '../../resource/useLoad';
import { AdminRow, RemoteProxyBanner, SecretOnceDialog, typedDeleteMessage, useRemoteProxyMode } from './TenantAdminCommon';

/** Minimum length the server accepts for a creator-supplied tenant admin password. */
export const MIN_ADMIN_PASSWORD_LENGTH = 8;

/**
 * Settings > Tenants (admins; others see their own tenant): list with name search, create (optional admin password,
 * else the server generates one and it is shown once), edit name and active, View JSON, delete and bulk delete with
 * the typed `delete` confirmation. Remote proxy mode is read-only, as on the dashboard.
 */
export function TenantsTab() {
  const { t, formatRelativeTime } = useLocale();
  const { user, isAdmin } = useAuth();
  const { pushToast } = useNotifications();
  const { remote, instanceId } = useRemoteProxyMode();
  const { confirm, dialog } = useConfirm('tenant-confirm');
  const ownTenant = user?.tenant ?? null;

  const { data, loading, refreshing, error, reload, refresh } = useLoad<TenantMetadata[]>(
    async () => (isAdmin ? (await listTenants()).objects ?? [] : ownTenant ? [ownTenant] : []),
    [isAdmin, ownTenant?.id],
    { fallbackError: t('Failed to load tenants.') },
  );
  const items = useMemo(() => data ?? [], [data]);
  const [search, setSearch] = useState('');
  const [selected, setSelected] = useState<string[]>([]);
  const [editing, setEditing] = useState<TenantMetadata | 'new' | null>(null);
  const [json, setJson] = useState<TenantMetadata | null>(null);
  const [generated, setGenerated] = useState<{ email: string; password: string } | null>(null);
  const canManage = isAdmin && !remote;

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    return [...items]
      .filter((tenant) => !term || tenant.name.toLowerCase().includes(term))
      .sort((a, b) => a.name.toLowerCase().localeCompare(b.name.toLowerCase()));
  }, [items, search]);

  function toggle(id: string) {
    setSelected((s) => (s.includes(id) ? s.filter((x) => x !== id) : [...s, id]));
  }

  function remove(tenant: TenantMetadata) {
    confirm({
      title: t('Delete Tenant'),
      message: typedDeleteMessage(t, t('Delete tenant "{{name}}"? This is destructive and cannot be undone.', { name: tenant.name }), tenant.name),
      confirmLabel: t('Yes'),
      danger: true,
      typed: 'delete',
      onConfirm: async () => {
        try {
          await deleteTenant(tenant.id);
          pushToast('warning', t('Tenant "{{name}}" deleted.', { name: tenant.name }));
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
      title: t('Delete Selected Tenants'),
      message: typedDeleteMessage(t, t('Delete {{count}} tenant(s)? This cannot be undone.', { count: ids.length }), `${ids.length} tenant(s)`),
      confirmLabel: t('Yes'),
      danger: true,
      typed: 'delete',
      onConfirm: async () => {
        setSelected([]);
        let failed = 0;
        for (const id of ids) { try { await deleteTenant(id); } catch { failed++; } }
        const success = ids.length - failed;
        if (success > 0) {
          pushToast(failed > 0 ? 'warning' : 'success', failed > 0
            ? t('Deleted {{success}} tenants. {{failed}} failed.', { success, failed })
            : t('Deleted {{success}} tenants.', { success }));
        }
        if (failed > 0) pushToast('error', t('Deleted {{success}}, {{failed}} failed.', { success, failed }));
        await reload();
      },
    });
  }

  return (
    <>
      <ResourceList
        testID="tenants"
        items={filtered}
        keyOf={(tenant) => tenant.id}
        loading={loading}
        error={error}
        onRetry={() => void reload()}
        refreshing={refreshing}
        onRefresh={() => void refresh()}
        search={{ value: search, onChange: setSearch, placeholder: t('Filter by name') }}
        header={(
          <>
            <AppText muted style={resourceStyles.pad}>
              {isAdmin ? t('Manage tenants in the system. Each tenant is an isolated organizational unit.') : t('View your tenant information.')}
            </AppText>
            {remote ? <RemoteProxyBanner entity="Tenant" instanceId={instanceId} /> : null}
            {canManage ? <Button label={`+ ${t('Tenant')}`} onPress={() => setEditing('new')} style={resourceStyles.create} testID="tenants-create" /> : null}
            {canManage && selected.length > 0 ? (
              <Button label={`${t('Delete Selected')} (${selected.length})`} variant="danger" onPress={removeSelected} style={resourceStyles.create} testID="tenants-delete-selected" />
            ) : null}
          </>
        )}
        emptyTitle={items.length > 0 ? t('No tenants match filters.') : t('No tenants found.')}
        renderItem={(tenant) => (
          <AdminRow
            testID={`tenant-row-${tenant.id}`}
            title={tenant.name}
            subtitle={tenant.id}
            badge={{ label: tenant.active ? t('Active') : t('Inactive'), tone: tenant.active ? 'success' : 'cancelled' }}
            meta={formatRelativeTime(tenant.createdUtc)}
            selected={selected.includes(tenant.id)}
            onPress={() => (canManage ? setEditing(tenant) : setJson(tenant))}
            onToggleSelect={canManage ? () => toggle(tenant.id) : undefined}
            actions={[
              ...(canManage ? [{ key: 'edit', label: t('Edit'), icon: 'create-outline' as const, onPress: () => setEditing(tenant) }] : []),
              { key: 'json', label: t('View JSON'), icon: 'code-slash-outline' as const, onPress: () => setJson(tenant) },
              ...(canManage ? [{ key: 'delete', label: t('Delete'), icon: 'trash-outline' as const, tone: 'danger' as const, onPress: () => remove(tenant) }] : []),
            ]}
          />
        )}
      />
      <FormSheet
        testID="tenant-form"
        open={editing !== null}
        title={editing === 'new' ? t('Create Tenant') : t('Edit Tenant')}
        initial={editing && editing !== 'new' ? { name: editing.name, active: editing.active, adminPassword: '' } : { name: '', active: true, adminPassword: '' }}
        fields={() => [
          { kind: 'text', key: 'name', label: t('Name'), required: true },
          ...(editing === 'new'
            ? [{ kind: 'secret' as const, key: 'adminPassword', label: t('Admin Password (optional)'), placeholder: t('Leave blank to generate one') }]
            : [{ kind: 'switch' as const, key: 'active', label: t('Active') }]),
        ]}
        validate={(v) => (editing === 'new' && str(v, 'adminPassword') && str(v, 'adminPassword').length < MIN_ADMIN_PASSWORD_LENGTH
          ? t('Admin password must be at least 8 characters.')
          : null)}
        submitLabel={t('Save')}
        onClose={() => setEditing(null)}
        onSubmit={async (v) => {
          const name = str(v, 'name');
          try {
            if (editing && editing !== 'new') {
              await updateTenant(editing.id, { name, active: bool(v, 'active') });
            } else {
              const request: TenantCreateRequest = { name, active: bool(v, 'active') };
              if (str(v, 'adminPassword')) request.adminPassword = str(v, 'adminPassword');
              const created = await createTenant(request);
              if (created?.adminPassword) setGenerated({ email: created.adminEmail || 'admin@armada', password: created.adminPassword });
            }
          } catch {
            throw new Error(t('Save failed.'));
          }
          pushToast('success', editing && editing !== 'new' ? t('Tenant "{{name}}" saved.', { name }) : t('Tenant "{{name}}" created.', { name }));
          setEditing(null);
          await reload();
        }}
      />
      <SecretOnceDialog
        testID="tenant-password-once"
        open={generated !== null}
        title={t('Tenant admin password (shown once)')}
        message={t('Copy this password now. It is shown only once and cannot be retrieved later.')}
        email={generated?.email}
        emailLabel={t('Admin email')}
        secretLabel={t('Password')}
        secret={generated?.password ?? ''}
        doneLabel={t('I have saved it')}
        onClose={() => setGenerated(null)}
      />
      <JsonSheet open={json !== null} title={json ? `${t('Tenant')}: ${json.name}` : ''} data={json} onClose={() => setJson(null)} />
      {dialog}
    </>
  );
}
