import { useLocalSearchParams } from 'expo-router';
import { useMemo, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import {
  createCliPermissionRule,
  deleteCliPermissionRule,
  getCliPermissionRequest,
  getCliPermissionRule,
  listCaptains,
  listCliPermissionRequests,
  listCliPermissionRules,
  updateCliPermissionRule,
} from '@dashboard/api/client';
import type { Captain, CliPermissionRequest, CliPermissionRequestStatus, CliPermissionRule, CliPermissionRuleAction, CliPermissionRuleScope } from '@dashboard/types/models';
import { CLI_PERMISSION_EVENT_PREFIX, mergeCliRequest, requestStatusLabel } from '@dashboard/lib/cliPermissions';
import { useAuth } from '../../auth/AuthContext';
import { CliPermissionCard } from '../../components/cliPermissions/CliPermissionCard';
import { Field, FieldCard } from '../../components/resource/DetailParts';
import { FormSheet, str, type FormField, type FormValues } from '../../components/resource/FormSheet';
import { HubScreen, type HubTab } from '../../components/resource/Hub';
import { ResourceList } from '../../components/resource/ResourceList';
import { ResourceRow, useConfirm } from '../../components/resource/ResourceRow';
import { resourceStyles } from '../../components/resource/styles';
import { AppText } from '../../components/ui/AppText';
import { BottomSheet } from '../../components/ui/BottomSheet';
import { Button } from '../../components/ui/Button';
import type { Translate } from '../../i18n/LocaleContext';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { param } from '../../resource/links';
import { useReference, useVessels } from '../../resource/lookups';
import { errorText, useLoad } from '../../resource/useLoad';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing } from '../../theme/typography';

export const REQUEST_LIMIT = 200;
const STATUSES: CliPermissionRequestStatus[] = ['Pending', 'Allowed', 'Denied', 'Expired', 'Cancelled'];

/** The deep-linked request first: added from getCliPermissionRequest when the status filter hides it, as the dashboard does. */
export async function loadRequests(status: CliPermissionRequestStatus | '', highlightId: string): Promise<CliPermissionRequest[]> {
  const list = (await listCliPermissionRequests({ status: status || null, limit: REQUEST_LIMIT })) ?? [];
  if (highlightId && !list.some((r) => r.id === highlightId)) {
    try {
      const linked = await getCliPermissionRequest(highlightId);
      if (linked?.id) return [linked, ...list];
    } catch { /* not visible to this user or gone; the list still renders */ }
  }
  return list;
}

/** Requests tab: status filter (Pending by default), decide in place, and the ?request= deep link highlighted first. */
function RequestsPanel() {
  const { t } = useLocale();
  const { colors } = useTheme();
  const params = useLocalSearchParams<{ request?: string }>();
  const highlightId = param(params.request);
  const [status, setStatus] = useState<CliPermissionRequestStatus | ''>('Pending');
  const { data, loading, refreshing, error, reload, refresh, setData } = useLoad(
    () => loadRequests(status, highlightId),
    [status, highlightId],
    { live: [CLI_PERMISSION_EVENT_PREFIX], fallbackError: t('Failed to load CLI tool requests.') },
  );
  const requests = useMemo(() => {
    const list = data ?? [];
    const linked = list.find((r) => r.id === highlightId);
    return linked ? [linked, ...list.filter((r) => r.id !== highlightId)] : list;
  }, [data, highlightId]);

  function decided(updated: CliPermissionRequest) {
    setData((data ?? []).map((r) => (r.id === updated.id ? mergeCliRequest(r, updated) : r)));
  }

  return (
    <ResourceList
      testID="cli-requests"
      items={requests}
      keyOf={(r) => r.id}
      loading={loading}
      error={error}
      onRetry={() => void reload()}
      refreshing={refreshing}
      onRefresh={() => void refresh()}
      filters={[{
        key: 'status', label: t('Status'), value: status || 'all', allValue: 'Pending',
        onChange: (v) => setStatus(v === 'all' ? '' : (v as CliPermissionRequestStatus)),
        options: [...STATUSES.map((s) => ({ value: s, label: requestStatusLabel(t, s) })), { value: 'all', label: t('All') }],
      }]}
      emptyTitle={status === 'Pending' ? t('No CLI tool requests are waiting.') : t('No CLI tool requests match this filter.')}
      emptyMessage={t('Captains with the Approve in Armada policy ask here before running a shell command, edit, or fetch that no rule allows.')}
      renderItem={(request) => (
        <View
          style={[styles.request, request.id === highlightId ? { borderColor: colors.focus, borderWidth: 2, borderRadius: radius.md } : null]}
          testID={request.id === highlightId ? 'cli-request-highlighted' : undefined}
        >
          <CliPermissionCard request={request} onDecided={decided} showThread />
        </View>
      )}
    />
  );
}

/** Rule target text: the vessel or captain by name, or Global. */
export function ruleTarget(t: Translate, rule: CliPermissionRule, vesselNames: Map<string, string>, captainNames: Map<string, string>): string {
  if (rule.scope === 'Vessel') return t('Vessel {{name}}', { name: (rule.vesselId && vesselNames.get(rule.vesselId)) || rule.vesselId || '-' });
  if (rule.scope === 'Captain') return t('Captain {{name}}', { name: (rule.captainId && captainNames.get(rule.captainId)) || rule.captainId || '-' });
  return t('Global');
}

/** The new-rule form's error, or null (the dashboard disables Create rule until it is null). */
export function ruleDraftError(t: Translate, v: FormValues): string | null {
  if (!str(v, 'pattern').trim()) return t('Enter a rule pattern.');
  if (str(v, 'scope') === 'Vessel' && !str(v, 'vesselId')) return t('Choose a vessel.');
  if (str(v, 'scope') === 'Captain' && !str(v, 'captainId')) return t('Choose a captain.');
  return null;
}

const EMPTY_RULE: FormValues = { pattern: '', action: 'Allow', scope: 'Global', vesselId: '', captainId: '', description: '' };

/** Rules tab: list (filter by scope), create, open a rule to edit its pattern, action, and description, delete. */
function RulesPanel() {
  const { t, formatRelativeTime, formatDateTime } = useLocale();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { pushToast } = useNotifications();
  const canEdit = !!isAdmin || !!isTenantAdmin;
  const { confirm, dialog } = useConfirm('cli-rule-confirm');
  const [scopeFilter, setScopeFilter] = useState<CliPermissionRuleScope | ''>('');
  const [creating, setCreating] = useState(false);
  const [openRule, setOpenRule] = useState<CliPermissionRule | null>(null);
  const [editingRule, setEditingRule] = useState<CliPermissionRule | null>(null);
  const vessels = useVessels();
  const captains = useReference<Captain>(() => listCaptains({ pageSize: 1000 }));
  const vesselNames = useMemo(() => new Map(vessels.map((v) => [v.id, v.name])), [vessels]);
  const captainNames = useMemo(() => new Map(captains.map((c) => [c.id, c.name])), [captains]);

  const { data, loading, refreshing, error, reload, refresh, setData } = useLoad(
    async () => (await listCliPermissionRules({ scope: scopeFilter || null })) ?? [],
    [scopeFilter],
    { fallbackError: t('Failed to load CLI permission rules.') },
  );
  const rules = data ?? [];

  async function openDetail(rule: CliPermissionRule) {
    setOpenRule(rule);
    try {
      const fresh = await getCliPermissionRule(rule.id);
      if (fresh?.id) setOpenRule(fresh);
    } catch { /* keep the listed copy */ }
  }

  function remove(rule: CliPermissionRule) {
    confirm({
      title: t('Delete rule'),
      message: t('Delete the {{action}} rule {{pattern}}? Matching CLI tool calls go back to the policy (asking in Armada, refused, or bypassed).', {
        action: rule.action === 'Deny' ? t('Deny') : t('Allow'),
        pattern: rule.pattern,
      }),
      confirmLabel: t('Delete'),
      danger: true,
      onConfirm: async () => {
        try {
          await deleteCliPermissionRule(rule.id);
          setData((data ?? []).filter((r) => r.id !== rule.id));
          setOpenRule(null);
          pushToast('success', t('Rule deleted.'));
        } catch (err: unknown) {
          pushToast('error', errorText(err, t('The rule could not be deleted.')));
        }
      },
    });
  }

  function createFields(v: FormValues): FormField[] {
    return [
      { kind: 'text', key: 'pattern', label: t('Pattern'), placeholder: 'Bash(git status:*)' },
      { kind: 'select', key: 'action', label: t('Action'), options: [{ value: 'Allow', label: t('Allow') }, { value: 'Deny', label: t('Deny') }] },
      { kind: 'select', key: 'scope', label: t('Scope'), options: [{ value: 'Global', label: t('Global') }, { value: 'Vessel', label: t('Vessel') }, { value: 'Captain', label: t('Captain') }] },
      ...(str(v, 'scope') === 'Vessel' ? [{ kind: 'select' as const, key: 'vesselId', label: t('Vessel'), placeholder: t('Select a vessel...'), options: [{ value: '', label: t('Select a vessel...') }, ...vessels.map((x) => ({ value: x.id, label: x.name }))] }] : []),
      ...(str(v, 'scope') === 'Captain' ? [{ kind: 'select' as const, key: 'captainId', label: t('Captain'), placeholder: t('Select a captain...'), options: [{ value: '', label: t('Select a captain...') }, ...captains.map((x) => ({ value: x.id, label: x.name }))] }] : []),
      { kind: 'text', key: 'description', label: t('Description'), placeholder: t('Optional') },
    ];
  }

  return (
    <>
      <ResourceList
        testID="cli-rules"
        items={rules}
        keyOf={(r) => r.id}
        loading={loading}
        error={error}
        onRetry={() => void reload()}
        refreshing={refreshing}
        onRefresh={() => void refresh()}
        filters={[{
          key: 'scope', label: t('Scope'), value: scopeFilter || 'all',
          onChange: (v) => setScopeFilter(v === 'all' ? '' : (v as CliPermissionRuleScope)),
          options: [{ value: 'all', label: t('All') }, { value: 'Global', label: t('Global') }, { value: 'Vessel', label: t('Vessel') }, { value: 'Captain', label: t('Captain') }],
        }]}
        header={(
          <>
            <AppText muted style={resourceStyles.pad}>
              {t('Rules use Claude Code permission rule syntax, for example Bash(git status:*), Bash(npm run *), WebFetch(domain:example.com), Edit(src/**), or mcp__server__tool. Deny rules win over allow rules; every part of a compound shell command must be allowed.')}
            </AppText>
            {canEdit ? <Button label={t('New rule')} icon="add" onPress={() => setCreating(true)} style={resourceStyles.create} testID="cli-rules-create" /> : null}
          </>
        )}
        emptyTitle={t('No rules yet.')}
        renderItem={(rule) => (
          <ResourceRow
            testID={`cli-rule-row-${rule.id}`}
            title={rule.pattern}
            subtitle={`${ruleTarget(t, rule, vesselNames, captainNames)}${!rule.tenantId ? ` ${t('(all tenants)')}` : ''}${rule.description ? ` \u2022 ${rule.description}` : ''}`}
            badge={{ label: rule.action === 'Deny' ? t('Deny') : t('Allow'), tone: rule.action === 'Deny' ? 'failed' : 'success' }}
            meta={rule.createdUtc ? formatRelativeTime(rule.createdUtc) : null}
            onPress={() => void openDetail(rule)}
            actions={canEdit ? [
              { key: 'edit', label: t('Edit'), icon: 'create-outline', onPress: () => setEditingRule(rule) },
              { key: 'delete', label: t('Delete'), icon: 'trash-outline', tone: 'danger', onPress: () => remove(rule) },
            ] : []}
          />
        )}
      />
      <BottomSheet open={openRule !== null} title={openRule?.pattern ?? ''} onClose={() => setOpenRule(null)} closeLabel={t('Close')} testID="cli-rule-detail">
        {openRule ? (
          <>
            <FieldCard>
              <Field label={t('Pattern')} value={openRule.pattern} mono />
              <Field label={t('Action')} value={openRule.action === 'Deny' ? t('Deny') : t('Allow')} />
              <Field label={t('Applies to')} value={`${ruleTarget(t, openRule, vesselNames, captainNames)}${!openRule.tenantId ? ` ${t('(all tenants)')}` : ''}`} />
              <Field label={t('Description')} value={openRule.description} />
              <Field label={t('Created')} value={openRule.createdUtc ? `${formatRelativeTime(openRule.createdUtc)} (${formatDateTime(openRule.createdUtc)})` : null} />
              <Field label={t('ID')} value={openRule.id} mono />
            </FieldCard>
            {canEdit ? (
              <View style={styles.actions}>
                <Button label={t('Edit')} variant="secondary" onPress={() => { const r = openRule; setOpenRule(null); setEditingRule(r); }} style={resourceStyles.action} testID="cli-rule-edit" />
                <Button label={t('Delete')} variant="danger" onPress={() => remove(openRule)} style={resourceStyles.action} testID="cli-rule-delete" />
              </View>
            ) : null}
          </>
        ) : null}
      </BottomSheet>
      <FormSheet
        testID="cli-rule-form"
        open={creating}
        title={t('New rule')}
        initial={EMPTY_RULE}
        fields={createFields}
        validate={(v) => ruleDraftError(t, v)}
        submitLabel={t('Create rule')}
        onClose={() => setCreating(false)}
        onSubmit={async (v) => {
          const scope = str(v, 'scope') as CliPermissionRuleScope;
          try {
            await createCliPermissionRule({
              pattern: str(v, 'pattern').trim(),
              action: str(v, 'action') as CliPermissionRuleAction,
              scope,
              vesselId: scope === 'Vessel' ? str(v, 'vesselId') : null,
              captainId: scope === 'Captain' ? str(v, 'captainId') : null,
              description: str(v, 'description').trim() || null,
            });
          } catch (err: unknown) {
            throw new Error(errorText(err, t('The rule could not be created.')));
          }
          pushToast('success', t('Rule created.'));
          setCreating(false);
          await reload();
        }}
      />
      <FormSheet
        testID="cli-rule-edit-form"
        open={editingRule !== null}
        title={t('Edit')}
        initial={editingRule ? { pattern: editingRule.pattern, action: editingRule.action, description: editingRule.description ?? '' } : {}}
        fields={() => [
          { kind: 'text', key: 'pattern', label: t('Pattern'), required: true },
          { kind: 'select', key: 'action', label: t('Action'), options: [{ value: 'Allow', label: t('Allow') }, { value: 'Deny', label: t('Deny') }] },
          { kind: 'text', key: 'description', label: t('Description'), placeholder: t('Optional') },
        ]}
        submitLabel={t('Save')}
        onClose={() => setEditingRule(null)}
        onSubmit={async (v) => {
          if (!editingRule) return;
          await updateCliPermissionRule(editingRule.id, {
            pattern: str(v, 'pattern').trim(),
            action: str(v, 'action') as CliPermissionRuleAction,
            description: str(v, 'description').trim() || null,
          });
          pushToast('success', t('Saved.'));
          setEditingRule(null);
          await reload();
        }}
      />
      {dialog}
    </>
  );
}

/**
 * CLI Tool Permissions (/cli-permissions): the CLI tool calls captains asked Armada to approve (Requests, decided in
 * place with the same controls as Ask and the approvals center) and the allow / deny rules (Rules). Push and inbox
 * links open ?request={id}.
 */
export function CliPermissionsHub() {
  const { t } = useLocale();
  const tabs: HubTab[] = [
    { key: 'requests', label: 'Requests', render: () => <RequestsPanel /> },
    { key: 'rules', label: 'Rules', render: () => <RulesPanel /> },
  ];
  return <HubScreen title={t('CLI Tool Permissions')} tabs={tabs} defaultKey="requests" label={t('CLI Tool Permissions')} testID="cli-permissions" />;
}

const styles = StyleSheet.create({
  request: { marginHorizontal: spacing.md, marginBottom: spacing.sm },
  actions: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm, marginTop: spacing.md },
});
