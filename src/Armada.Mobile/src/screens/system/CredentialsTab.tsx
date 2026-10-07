import { useMemo, useState } from 'react';
import { createCredential, deleteCredential, listCredentials, listTenants, listUsers, updateCredential } from '@dashboard/api/client';
import type { Credential, TenantMetadata } from '@dashboard/types/models';
import { useAuth } from '../../auth/AuthContext';
import { JsonSheet } from '../../components/resource/DetailParts';
import { FormSheet, bool, str, type FormField, type FormValues } from '../../components/resource/FormSheet';
import { ResourceList } from '../../components/resource/ResourceList';
import { useConfirm } from '../../components/resource/ResourceRow';
import { resourceStyles } from '../../components/resource/styles';
import { AppText } from '../../components/ui/AppText';
import { Button } from '../../components/ui/Button';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { useLoad } from '../../resource/useLoad';
import { AdminRow, RemoteProxyBanner, SecretOnceDialog, typedDeleteMessage, useRemoteProxyMode } from './TenantAdminCommon';

/**
 * Settings > Credentials: API bearer tokens. Create shows the new token once (later reads are masked by the server
 * and shown as returned); edit changes the name and active flag only; View JSON; delete and bulk delete with the
 * typed `delete` confirmation. Remote proxy mode is read-only.
 */
export function CredentialsTab() {
  const { t, formatRelativeTime } = useLocale();
  const { user, isAdmin, isTenantAdmin } = useAuth();
  const { pushToast } = useNotifications();
  const { remote, instanceId } = useRemoteProxyMode();
  const { confirm, dialog } = useConfirm('credential-confirm');
  const ownTenant = user?.tenant ?? null;

  const { data, loading, refreshing, error, reload, refresh } = useLoad(async () => {
    const [credentials, users, tenants] = await Promise.all([
      listCredentials(),
      listUsers(),
      isAdmin ? listTenants() : Promise.resolve({ objects: ownTenant ? [ownTenant] : [] } as { objects: TenantMetadata[] }),
    ]);
    return { credentials: credentials.objects ?? [], users: users.objects ?? [], tenants: tenants.objects ?? [] };
  }, [isAdmin, ownTenant?.id], { fallbackError: t('Failed to load credentials.') });
  const items = useMemo(() => data?.credentials ?? [], [data]);
  const users = useMemo(() => data?.users ?? [], [data]);
  const tenants = useMemo(() => data?.tenants ?? [], [data]);
  const userName = (id: string) => users.find((u) => u.id === id)?.email ?? id;
  const tenantName = (id: string) => tenants.find((tn) => tn.id === id)?.name ?? id;

  const [search, setSearch] = useState('');
  const [userFilter, setUserFilter] = useState('all');
  const [tenantFilter, setTenantFilter] = useState('all');
  const [selected, setSelected] = useState<string[]>([]);
  const [editing, setEditing] = useState<Credential | 'new' | null>(null);
  const [json, setJson] = useState<Credential | null>(null);
  const [newToken, setNewToken] = useState<string | null>(null);

  const filtered = useMemo(() => items
    .filter((c) => (!search || (c.name ?? '').toLowerCase().includes(search.toLowerCase()))
      && (userFilter === 'all' || c.userId === userFilter)
      && (tenantFilter === 'all' || c.tenantId === tenantFilter))
    .sort((a, b) => (a.name ?? '').toLowerCase().localeCompare((b.name ?? '').toLowerCase())), [items, search, userFilter, tenantFilter]);

  const editingCredential = editing && editing !== 'new' ? editing : null;

  function remove(c: Credential) {
    const label = c.name || c.id;
    confirm({
      title: t('Delete Credential'),
      message: typedDeleteMessage(t, t('Delete credential "{{name}}"? This cannot be undone.', { name: label }), label),
      confirmLabel: t('Yes'),
      danger: true,
      typed: 'delete',
      onConfirm: async () => {
        try {
          await deleteCredential(c.id);
          pushToast('warning', t('Credential "{{name}}" deleted.', { name: label }));
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
      title: t('Delete Selected Credentials'),
      message: typedDeleteMessage(t, t('Delete {{count}} credential(s)?', { count: ids.length }), `${ids.length} credential(s)`),
      confirmLabel: t('Yes'),
      danger: true,
      typed: 'delete',
      onConfirm: async () => {
        setSelected([]);
        let failed = 0;
        for (const id of ids) { try { await deleteCredential(id); } catch { failed++; } }
        const success = ids.length - failed;
        if (success > 0) {
          pushToast(failed > 0 ? 'warning' : 'success', failed > 0
            ? t('Deleted {{success}} credentials. {{failed}} failed.', { success, failed })
            : t('Deleted {{success}} credentials.', { success }));
        }
        if (failed > 0) pushToast('error', t('Deleted {{success}}, {{failed}} failed.', { success, failed }));
        await reload();
      },
    });
  }

  function fields(v: FormValues): FormField[] {
    return [
      {
        kind: 'select', key: 'userId', label: t('User'), required: true, placeholder: t('Select user...'),
        disabled: !!editingCredential || (!isAdmin && !isTenantAdmin),
        options: [{ value: '', label: t('Select user...') }, ...users.map((u) => ({ value: u.id, label: u.email }))],
      },
      isAdmin
        ? { kind: 'select', key: 'tenantId', label: t('Tenant'), required: true, placeholder: t('Select tenant...'), disabled: !!editingCredential, options: [{ value: '', label: t('Select tenant...') }, ...tenants.map((tn) => ({ value: tn.id, label: tn.name }))] }
        : { kind: 'note', key: 'tenantId', label: `${t('Tenant')}: ${tenantName(str(v, 'tenantId'))}` },
      { kind: 'text', key: 'name', label: t('Name (optional)'), placeholder: t('e.g., CI/CD Token') },
      ...(editingCredential ? [{ kind: 'switch' as const, key: 'active', label: t('Active') }] : []),
    ];
  }

  const initial: FormValues = editingCredential
    ? { userId: editingCredential.userId, tenantId: editingCredential.tenantId, name: editingCredential.name ?? '', active: editingCredential.active }
    : { userId: users[0]?.id ?? user?.user?.id ?? '', tenantId: tenants[0]?.id ?? ownTenant?.id ?? '', name: '', active: true };

  const subtitle = isAdmin
    ? t('Manage API bearer tokens across all tenants.')
    : isTenantAdmin ? t('Manage API bearer tokens within your tenant.') : t('Manage your API bearer tokens.');

  return (
    <>
      <ResourceList
        testID="credentials"
        items={filtered}
        keyOf={(c) => c.id}
        loading={loading}
        error={error}
        onRetry={() => void reload()}
        refreshing={refreshing}
        onRefresh={() => void refresh()}
        search={{ value: search, onChange: setSearch, placeholder: t('Filter by name') }}
        filters={[
          { key: 'user', label: t('User'), value: userFilter, onChange: setUserFilter, options: [{ value: 'all', label: t('All users') }, ...users.map((u) => ({ value: u.id, label: u.email }))] },
          { key: 'tenant', label: t('Tenant'), value: tenantFilter, onChange: setTenantFilter, options: [{ value: 'all', label: t('All tenants') }, ...tenants.map((tn) => ({ value: tn.id, label: tn.name }))] },
        ]}
        header={(
          <>
            <AppText muted style={resourceStyles.pad}>{subtitle}</AppText>
            {remote ? <RemoteProxyBanner entity="Credential" instanceId={instanceId} /> : null}
            {!remote ? <Button label={`+ ${t('Credential')}`} onPress={() => setEditing('new')} style={resourceStyles.create} testID="credentials-create" /> : null}
            {!remote && selected.length > 0 ? (
              <Button label={`${t('Delete Selected')} (${selected.length})`} variant="danger" onPress={removeSelected} style={resourceStyles.create} testID="credentials-delete-selected" />
            ) : null}
          </>
        )}
        emptyTitle={items.length > 0 ? t('No credentials match filters.') : t('No credentials found.')}
        renderItem={(c) => (
          <AdminRow
            testID={`credential-row-${c.id}`}
            title={c.name || '-'}
            // The token as the server returns it on reads (masked); the full token is shown only once, at creation.
            subtitle={[userName(c.userId), tenantName(c.tenantId), c.bearerToken].filter(Boolean).join(' \u2022 ')}
            badge={{ label: c.active ? t('Active') : t('Inactive'), tone: c.active ? 'success' : 'cancelled' }}
            meta={formatRelativeTime(c.createdUtc)}
            selected={selected.includes(c.id)}
            onPress={() => (remote ? setJson(c) : setEditing(c))}
            onToggleSelect={!remote ? () => setSelected((s) => (s.includes(c.id) ? s.filter((x) => x !== c.id) : [...s, c.id])) : undefined}
            actions={[
              ...(remote ? [] : [{ key: 'edit', label: t('Edit'), icon: 'create-outline' as const, onPress: () => setEditing(c) }]),
              { key: 'json', label: t('View JSON'), icon: 'code-slash-outline' as const, onPress: () => setJson(c) },
              ...(remote ? [] : [{ key: 'delete', label: t('Delete'), icon: 'trash-outline' as const, tone: 'danger' as const, onPress: () => remove(c) }]),
            ]}
          />
        )}
      />
      <FormSheet
        testID="credential-form"
        open={editing !== null}
        title={editingCredential ? t('Edit Credential') : t('Create Credential')}
        initial={initial}
        fields={fields}
        submitLabel={editingCredential ? t('Save') : t('Create')}
        onClose={() => setEditing(null)}
        onSubmit={async (v) => {
          try {
            if (editingCredential) {
              await updateCredential(editingCredential.id, {
                id: editingCredential.id, userId: editingCredential.userId, tenantId: editingCredential.tenantId,
                name: str(v, 'name') || null, active: bool(v, 'active'),
              });
            } else {
              const created = await createCredential({ userId: str(v, 'userId'), tenantId: str(v, 'tenantId'), name: str(v, 'name') || null });
              setNewToken(created.bearerToken);
            }
          } catch {
            throw new Error(editingCredential ? t('Update failed.') : t('Create failed.'));
          }
          pushToast('success', editingCredential
            ? t('Credential "{{name}}" saved.', { name: str(v, 'name') || editingCredential.id })
            : t('Credential created.'));
          setEditing(null);
          await reload();
        }}
      />
      <SecretOnceDialog
        testID="credential-token-once"
        open={newToken !== null}
        title={t('Credential created')}
        message={t('Copy this bearer token now. It is shown only once; later reads show it masked.')}
        secretLabel={t('Bearer Token')}
        secret={newToken ?? ''}
        doneLabel={t('Done')}
        onClose={() => setNewToken(null)}
      />
      <JsonSheet open={json !== null} title={json ? `${t('Credential')}: ${json.name || json.id}` : ''} data={json} onClose={() => setJson(null)} />
      {dialog}
    </>
  );
}
