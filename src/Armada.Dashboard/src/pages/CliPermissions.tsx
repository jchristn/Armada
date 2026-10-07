import { useCallback, useEffect, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import {
  createCliPermissionRule,
  deleteCliPermissionRule,
  getCliPermissionRequest,
  listCaptains,
  listCliPermissionRequests,
  listCliPermissionRules,
  listVessels,
} from '../api/client';
import type {
  Captain,
  CliPermissionRequest,
  CliPermissionRequestStatus,
  CliPermissionRule,
  CliPermissionRuleAction,
  CliPermissionRuleScope,
  Vessel,
} from '../types/models';
import { useLocale } from '../context/LocaleContext';
import { useAuth } from '../context/AuthContext';
import { useNotifications } from '../context/NotificationContext';
import PageHeader from '../components/shared/PageHeader';
import Tabs, { type TabDef } from '../components/shared/Tabs';
import RefreshButton from '../components/shared/RefreshButton';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import DataTable, { type DataTableColumn } from '../components/shared/DataTable';
import ErrorModal from '../components/shared/ErrorModal';
import CliPermissionCard from '../components/cliPermissions/CliPermissionCard';
import { useLiveRefresh } from '../lib/useLiveRefresh';
import { CLI_PERMISSION_EVENT_PREFIX, mergeCliRequest, requestStatusLabel } from '../lib/cliPermissions';

const REQUEST_LIMIT = 200;
const STATUSES: CliPermissionRequestStatus[] = ['Pending', 'Allowed', 'Denied', 'Expired', 'Cancelled'];

function errorText(err: unknown, fallback: string): string {
  return err instanceof Error && err.message ? err.message : fallback;
}

/** Requests tab: filter by status (Pending by default), decide inline, and highlight the ?request= deep link. */
function RequestsPanel() {
  const { t } = useLocale();
  const [searchParams] = useSearchParams();
  const highlightId = searchParams.get('request');
  const [status, setStatus] = useState<CliPermissionRequestStatus | ''>('Pending');
  const [requests, setRequests] = useState<CliPermissionRequest[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const list = (await listCliPermissionRequests({ status: status || null, limit: REQUEST_LIMIT })) ?? [];
      let merged = list;
      // The deep-linked request is shown even when the status filter would hide it (for example it was decided).
      if (highlightId && !list.some((r) => r.id === highlightId)) {
        try {
          const linked = await getCliPermissionRequest(highlightId);
          if (linked?.id) merged = [linked, ...list];
        } catch { /* not visible to this user or gone; the list still renders */ }
      }
      setRequests(merged);
      setError('');
    } catch (err: unknown) {
      setError(errorText(err, t('Failed to load CLI tool requests.')));
    } finally {
      setLoading(false);
    }
  }, [status, highlightId, t]);

  useEffect(() => { void load(); }, [load]);
  useLiveRefresh([CLI_PERMISSION_EVENT_PREFIX], () => { void load(); });

  useEffect(() => {
    if (!highlightId || loading) return;
    const el = document.getElementById(`cli-permission-${highlightId}`);
    el?.scrollIntoView?.({ block: 'center' });
  }, [highlightId, loading, requests.length]);

  function decided(updated: CliPermissionRequest) {
    setRequests((prev) => prev.map((r) => (r.id === updated.id ? mergeCliRequest(r, updated) : r)));
  }

  return (
    <section aria-label={t('Requests')}>
      <div className="cli-perm-toolbar">
        <label>
          <span className="text-dim">{t('Status')}</span>{' '}
          <select value={status} onChange={(e) => setStatus(e.target.value as CliPermissionRequestStatus | '')} aria-label={t('Status')}>
            {STATUSES.map((s) => <option key={s} value={s}>{requestStatusLabel(t, s)}</option>)}
            <option value="">{t('All')}</option>
          </select>
        </label>
        <RefreshButton onRefresh={load} title={t('Refresh requests')} />
      </div>
      <ErrorModal error={error} onClose={() => setError('')} />
      {loading && requests.length === 0 ? (
        <p className="text-dim">{t('Loading...')}</p>
      ) : requests.length === 0 ? (
        <div className="playbook-empty-state">
          <strong>{status === 'Pending' ? t('No CLI tool requests are waiting.') : t('No CLI tool requests match this filter.')}</strong>
          <span>{t('Captains with the Approve in Armada policy ask here before running a shell command, edit, or fetch that no rule allows.')}</span>
        </div>
      ) : (
        <div className="cli-perm-list">
          {requests.map((request) => (
            <CliPermissionCard key={request.id} request={request} onDecided={decided} highlighted={request.id === highlightId} showThread />
          ))}
        </div>
      )}
    </section>
  );
}

interface RuleDraft {
  pattern: string;
  action: CliPermissionRuleAction;
  scope: CliPermissionRuleScope;
  vesselId: string;
  captainId: string;
  description: string;
}

const EMPTY_RULE: RuleDraft = { pattern: '', action: 'Allow', scope: 'Global', vesselId: '', captainId: '', description: '' };

/** Rules tab: list (filter by scope), create, and delete allow / deny rules. */
function RulesPanel() {
  const { t, formatRelativeTime, formatDateTime } = useLocale();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { pushToast } = useNotifications();
  const canEdit = !!isAdmin || !!isTenantAdmin;
  const [scopeFilter, setScopeFilter] = useState<CliPermissionRuleScope | ''>('');
  const [rules, setRules] = useState<CliPermissionRule[]>([]);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [captains, setCaptains] = useState<Captain[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [draft, setDraft] = useState<RuleDraft>(EMPTY_RULE);
  const [saving, setSaving] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<CliPermissionRule | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setRules((await listCliPermissionRules({ scope: scopeFilter || null })) ?? []);
      setError('');
    } catch (err: unknown) {
      setError(errorText(err, t('Failed to load CLI permission rules.')));
    } finally {
      setLoading(false);
    }
  }, [scopeFilter, t]);

  useEffect(() => { void load(); }, [load]);

  useEffect(() => {
    listVessels({ pageSize: 1000 }).then((r) => setVessels(r?.objects ?? [])).catch(() => setVessels([]));
    listCaptains({ pageSize: 1000 }).then((r) => setCaptains(r?.objects ?? [])).catch(() => setCaptains([]));
  }, []);

  const vesselNames = useMemo(() => new Map(vessels.map((v) => [v.id, v.name])), [vessels]);
  const captainNames = useMemo(() => new Map(captains.map((c) => [c.id, c.name])), [captains]);

  function target(rule: CliPermissionRule): string {
    if (rule.scope === 'Vessel') return t('Vessel {{name}}', { name: (rule.vesselId && vesselNames.get(rule.vesselId)) || rule.vesselId || '-' });
    if (rule.scope === 'Captain') return t('Captain {{name}}', { name: (rule.captainId && captainNames.get(rule.captainId)) || rule.captainId || '-' });
    return t('Global');
  }

  const draftError = !draft.pattern.trim()
    ? t('Enter a rule pattern.')
    : draft.scope === 'Vessel' && !draft.vesselId
      ? t('Choose a vessel.')
      : draft.scope === 'Captain' && !draft.captainId
        ? t('Choose a captain.')
        : null;

  async function create(e: React.FormEvent) {
    e.preventDefault();
    if (draftError || saving) return;
    setSaving(true);
    try {
      await createCliPermissionRule({
        pattern: draft.pattern.trim(),
        action: draft.action,
        scope: draft.scope,
        vesselId: draft.scope === 'Vessel' ? draft.vesselId : null,
        captainId: draft.scope === 'Captain' ? draft.captainId : null,
        description: draft.description.trim() || null,
      });
      pushToast('success', t('Rule created.'));
      setDraft(EMPTY_RULE);
      void load();
    } catch (err: unknown) {
      setError(errorText(err, t('The rule could not be created.')));
    } finally {
      setSaving(false);
    }
  }

  async function confirmDelete() {
    const rule = deleteTarget;
    setDeleteTarget(null);
    if (!rule) return;
    try {
      await deleteCliPermissionRule(rule.id);
      setRules((prev) => prev.filter((r) => r.id !== rule.id));
      pushToast('success', t('Rule deleted.'));
    } catch (err: unknown) {
      setError(errorText(err, t('The rule could not be deleted.')));
    }
  }

  const ruleColumns: DataTableColumn<CliPermissionRule>[] = [
    {
      key: 'pattern', label: t('Pattern'), required: true,
      render: (rule) => <code className="cell-one-line" title={rule.pattern}>{rule.pattern}</code>,
    },
    {
      key: 'action', label: t('Action'), cellClassName: 'cell-nowrap',
      render: (rule) => <span className={`tag cli-rule-${String(rule.action).toLowerCase()}`}>{rule.action === 'Deny' ? t('Deny') : t('Allow')}</span>,
    },
    {
      key: 'target', label: t('Applies to'), cellClassName: 'cell-nowrap',
      render: (rule) => <>{target(rule)}{!rule.tenantId && <span className="text-dim"> {t('(all tenants)')}</span>}</>,
    },
    {
      key: 'description', label: t('Description'), cellClassName: 'truncate-cell',
      render: (rule) => (rule.description ? <span className="truncate-text" title={rule.description}>{rule.description}</span> : <span className="text-dim">-</span>),
    },
    {
      key: 'created', label: t('Created'), cellClassName: 'cell-nowrap',
      render: (rule) => (rule.createdUtc ? <span title={formatDateTime(rule.createdUtc)}>{formatRelativeTime(rule.createdUtc)}</span> : '-'),
    },
    ...(canEdit ? [{
      key: 'actions', label: t('Actions'), fixed: true, interactive: true,
      header: <span className="sr-only">{t('Actions')}</span>,
      render: (rule: CliPermissionRule) => (
        <button type="button" className="btn btn-sm btn-danger" onClick={() => setDeleteTarget(rule)} aria-label={t('Delete rule {{pattern}}', { pattern: rule.pattern })}>
          {t('Delete')}
        </button>
      ),
    }] : []),
  ];

  return (
    <section aria-label={t('Rules')}>
      <p className="text-dim" style={{ marginTop: 0 }}>
        {t('Rules use Claude Code permission rule syntax, for example Bash(git status:*), Bash(npm run *), WebFetch(domain:example.com), Edit(src/**), or mcp__server__tool. Deny rules win over allow rules; every part of a compound shell command must be allowed.')}
      </p>
      <div className="cli-perm-toolbar">
        <label>
          <span className="text-dim">{t('Scope')}</span>{' '}
          <select value={scopeFilter} onChange={(e) => setScopeFilter(e.target.value as CliPermissionRuleScope | '')} aria-label={t('Scope')}>
            <option value="">{t('All')}</option>
            <option value="Global">{t('Global')}</option>
            <option value="Vessel">{t('Vessel')}</option>
            <option value="Captain">{t('Captain')}</option>
          </select>
        </label>
      </div>
      <ErrorModal error={error} onClose={() => setError('')} />

      <DataTable
        tableKey="cli-permission-rules"
        columns={ruleColumns}
        rows={rules}
        rowKey={(rule) => rule.id}
        onRefresh={load}
        refreshTitle="Refresh rules"
        placeholder={rules.length > 0 ? undefined : loading ? <p className="text-dim">{t('Loading...')}</p> : <p className="text-dim">{t('No rules yet.')}</p>}
      />

      {canEdit && (
        <form className="card cli-rule-form" onSubmit={(e) => void create(e)} aria-label={t('New rule')}>
          <h3 style={{ marginTop: 0 }}>{t('New rule')}</h3>
          <div className="settings-grid">
            <div className="form-group">
              <label htmlFor="cli-rule-pattern">{t('Pattern')}</label>
              <input id="cli-rule-pattern" className="mono" value={draft.pattern} onChange={(e) => setDraft({ ...draft, pattern: e.target.value })} placeholder="Bash(git status:*)" />
            </div>
            <div className="form-group">
              <label htmlFor="cli-rule-action">{t('Action')}</label>
              <select id="cli-rule-action" value={draft.action} onChange={(e) => setDraft({ ...draft, action: e.target.value as CliPermissionRuleAction })}>
                <option value="Allow">{t('Allow')}</option>
                <option value="Deny">{t('Deny')}</option>
              </select>
            </div>
            <div className="form-group">
              <label htmlFor="cli-rule-scope">{t('Scope')}</label>
              <select id="cli-rule-scope" value={draft.scope} onChange={(e) => setDraft({ ...draft, scope: e.target.value as CliPermissionRuleScope })}>
                <option value="Global">{t('Global')}</option>
                <option value="Vessel">{t('Vessel')}</option>
                <option value="Captain">{t('Captain')}</option>
              </select>
            </div>
            {draft.scope === 'Vessel' && (
              <div className="form-group">
                <label htmlFor="cli-rule-vessel">{t('Vessel')}</label>
                <select id="cli-rule-vessel" value={draft.vesselId} onChange={(e) => setDraft({ ...draft, vesselId: e.target.value })}>
                  <option value="">{t('Select a vessel...')}</option>
                  {vessels.map((v) => <option key={v.id} value={v.id}>{v.name}</option>)}
                </select>
              </div>
            )}
            {draft.scope === 'Captain' && (
              <div className="form-group">
                <label htmlFor="cli-rule-captain">{t('Captain')}</label>
                <select id="cli-rule-captain" value={draft.captainId} onChange={(e) => setDraft({ ...draft, captainId: e.target.value })}>
                  <option value="">{t('Select a captain...')}</option>
                  {captains.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
                </select>
              </div>
            )}
            <div className="form-group">
              <label htmlFor="cli-rule-description">{t('Description')}</label>
              <input id="cli-rule-description" value={draft.description} onChange={(e) => setDraft({ ...draft, description: e.target.value })} placeholder={t('Optional')} />
            </div>
          </div>
          <div className="settings-actions">
            <button type="submit" className="btn btn-primary btn-sm" disabled={!!draftError || saving}>{saving ? t('Saving...') : t('Create rule')}</button>
            {draftError && draft.pattern && <span className="text-dim">{draftError}</span>}
          </div>
        </form>
      )}

      <ConfirmDialog
        open={!!deleteTarget}
        title={t('Delete rule')}
        message={t('Delete the {{action}} rule {{pattern}}? Matching CLI tool calls go back to the policy (asking in Armada, refused, or bypassed).', {
          action: deleteTarget?.action === 'Deny' ? t('Deny') : t('Allow'),
          pattern: deleteTarget?.pattern ?? '',
        })}
        confirmLabel={t('Delete')}
        cancelLabel={t('Cancel')}
        danger
        onConfirm={() => void confirmDelete()}
        onCancel={() => setDeleteTarget(null)}
      />
    </section>
  );
}

/**
 * CLI Tool Permissions: the CLI tool calls captains asked Armada to approve (Requests) and the allow / deny rules that
 * decide matching calls without asking (Rules). The inbox links here with ?request={id}.
 */
export default function CliPermissions() {
  const { t } = useLocale();
  const tabs: TabDef[] = [
    { key: 'requests', label: 'Requests', render: () => <RequestsPanel /> },
    { key: 'rules', label: 'Rules', render: () => <RulesPanel /> },
  ];
  return (
    <div>
      <PageHeader
        title={t('CLI Tool Permissions')}
        subtitle={t('Shell commands, file edits, and fetches that CLI captains asked to run, and the rules that allow or deny them.')}
      />
      <Tabs tabs={tabs} defaultTabKey="requests" ariaLabel={t('CLI Tool Permissions')} />
    </div>
  );
}
