import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import {
  createCliPermissionRule,
  deleteCliPermissionRule,
  getCliPermissionRequest,
  listCaptains,
  listCliPermissionRequests,
  enumerateCliPermissionRules,
  listVessels,
  updateCliPermissionRule,
} from '../api/client';
import { listAll } from '../api/paging';
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
import RefreshButton from '../components/shared/RefreshButton';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import DataTable, { type DataTableColumn } from '../components/shared/DataTable';
import ErrorModal from '../components/shared/ErrorModal';
import CliPermissionCard from '../components/cliPermissions/CliPermissionCard';
import { useLiveRefresh } from '../lib/useLiveRefresh';
import { CLI_PERMISSION_EVENT_PREFIX, mergeCliRequest, requestStatusLabel } from '../lib/cliPermissions';
import { useDeleteProgress } from '../lib/useDeleteProgress';
import TruncationNote from '../components/shared/TruncationNote';

/** The route's maximum; the list has no total, so a full page means there may be more (said visibly below). */
const REQUEST_LIMIT = 1000;
const STATUSES: CliPermissionRequestStatus[] = ['Pending', 'Allowed', 'Denied', 'Expired', 'Cancelled'];

function errorText(err: unknown, fallback: string): string {
  return err instanceof Error && err.message ? err.message : fallback;
}

/** Requests section: filter by status (Pending by default), decide inline, and highlight the ?request= deep link. */
function RequestsPanel() {
  const { t } = useLocale();
  const [searchParams] = useSearchParams();
  const highlightId = searchParams.get('request');
  const [status, setStatus] = useState<CliPermissionRequestStatus | ''>('Pending');
  const [requests, setRequests] = useState<CliPermissionRequest[]>([]);
  const [atLimit, setAtLimit] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const list = (await listCliPermissionRequests({ status: status || null, limit: REQUEST_LIMIT })) ?? [];
      setAtLimit(list.length >= REQUEST_LIMIT);
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
          <select value={status} onChange={(e) => setStatus(e.target.value as CliPermissionRequestStatus | '')} aria-label={t('Status')} title={t('Show requests in one state. Pending ones still wait for a decision; All shows every request you can see, newest first.')}>
            {STATUSES.map((s) => <option key={s} value={s}>{requestStatusLabel(t, s)}</option>)}
            <option value="">{t('All')}</option>
          </select>
        </label>
        <RefreshButton onRefresh={load} title={t('Reload the request list. It also updates on its own when a request is made or decided.')} />
      </div>
      <ErrorModal error={error} onClose={() => setError('')} />
      {atLimit && (
        <div className="alert alert-warning" role="status">
          {t('Showing the first {{count}} requests. Narrow the filter to see the rest.', { count: REQUEST_LIMIT.toLocaleString() })}
        </div>
      )}
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

/** Rules section: list (filter by scope), create, edit, and delete allow / deny rules. */
function RulesPanel() {
  const { t, formatRelativeTime, formatDateTime } = useLocale();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { pushToast } = useNotifications();
  const deletion = useDeleteProgress();
  const canEdit = !!isAdmin || !!isTenantAdmin;
  const [scopeFilter, setScopeFilter] = useState<CliPermissionRuleScope | ''>('');
  const [rules, setRules] = useState<CliPermissionRule[]>([]);
  const [ruleTotal, setRuleTotal] = useState(0);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [captains, setCaptains] = useState<Captain[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [draft, setDraft] = useState<RuleDraft>(EMPTY_RULE);
  /** Rule being edited in the form; null while the form creates a new rule. Scope and target cannot change on edit. */
  const [editingId, setEditingId] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<CliPermissionRule | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      // One bounded page of the largest size; the note above the table says when more rules exist.
      const page = await enumerateCliPermissionRules({ scope: scopeFilter || null });
      setRules(page?.objects ?? []);
      setRuleTotal(page?.totalRecords ?? (page?.objects?.length ?? 0));
      setError('');
    } catch (err: unknown) {
      setError(errorText(err, t('Failed to load CLI permission rules.')));
    } finally {
      setLoading(false);
    }
  }, [scopeFilter, t]);

  useEffect(() => { void load(); }, [load]);

  useEffect(() => {
    listAll(listVessels).then((r) => setVessels(r?.objects ?? [])).catch(() => setVessels([]));
    listAll(listCaptains).then((r) => setCaptains(r?.objects ?? [])).catch(() => setCaptains([]));
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

  function startEdit(rule: CliPermissionRule) {
    setEditingId(rule.id);
    setDraft({
      pattern: rule.pattern,
      action: rule.action,
      scope: rule.scope,
      vesselId: rule.vesselId ?? '',
      captainId: rule.captainId ?? '',
      description: rule.description ?? '',
    });
    document.getElementById('cli-rule-pattern')?.focus();
  }

  function cancelEdit() {
    setEditingId(null);
    setDraft(EMPTY_RULE);
  }

  async function save(e: React.FormEvent) {
    e.preventDefault();
    if (draftError || saving) return;
    setSaving(true);
    if (editingId) {
      try {
        await updateCliPermissionRule(editingId, {
          pattern: draft.pattern.trim(),
          action: draft.action,
          description: draft.description.trim() || null,
        });
        pushToast('success', t('Rule updated.'));
        setEditingId(null);
        setDraft(EMPTY_RULE);
        void load();
      } catch (err: unknown) {
        setError(errorText(err, t('The rule could not be updated.')));
      } finally {
        setSaving(false);
      }
      return;
    }
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
    if (!(await deletion.run({ kind: 'cliPermissionRule', action: () => deleteCliPermissionRule(rule.id), errorMessage: (err) => errorText(err, t('The rule could not be deleted.')) }))) return;
    setRules((prev) => prev.filter((r) => r.id !== rule.id));
    if (editingId === rule.id) cancelEdit();
    pushToast('success', t('Rule deleted.'));
  }

  const ruleColumns: DataTableColumn<CliPermissionRule>[] = [
    {
      key: 'pattern', label: t('Pattern'), headerTitle: t('Which calls the rule matches, in Claude Code rule syntax, for example Bash(npm run *), WebFetch(domain:example.com), or Edit(src/**).'), required: true,
      render: (rule) => <code className="cell-one-line" title={rule.pattern}>{rule.pattern}</code>,
    },
    {
      key: 'action', label: t('Action'), headerTitle: t('Allow runs matching calls without asking; Deny refuses them. Deny rules win over allow rules.'), cellClassName: 'cell-nowrap',
      render: (rule) => <span className={`tag cli-rule-${String(rule.action).toLowerCase()}`} title={rule.action === 'Deny' ? t('Matching calls are refused without asking.') : t('Matching calls run without asking.')}>{rule.action === 'Deny' ? t('Deny') : t('Allow')}</span>,
    },
    {
      key: 'target', label: t('Applies to'), headerTitle: t('The captain, vessel, or whole tenant the rule covers. (all tenants) means a global admin made it apply everywhere.'), cellClassName: 'cell-nowrap',
      render: (rule) => <>{target(rule)}{!rule.tenantId && <span className="text-dim"> {t('(all tenants)')}</span>}</>,
    },
    {
      key: 'description', label: t('Description'), headerTitle: t('Note about why the rule exists. Does not affect matching.'), cellClassName: 'truncate-cell',
      render: (rule) => (rule.description ? <span className="truncate-text" title={rule.description}>{rule.description}</span> : <span className="text-dim">-</span>),
    },
    {
      key: 'created', label: t('Created'), headerTitle: t('When the rule was made, either here or with Allow and remember on a request.'), cellClassName: 'cell-nowrap',
      render: (rule) => (rule.createdUtc ? <span title={formatDateTime(rule.createdUtc)}>{formatRelativeTime(rule.createdUtc)}</span> : '-'),
    },
    ...(canEdit ? [{
      key: 'actions', label: t('Actions'), fixed: true, interactive: true,
      header: <span className="sr-only">{t('Actions')}</span>,
      render: (rule: CliPermissionRule) => (
        <span className="cli-rule-row-actions">
          <button type="button" className="btn btn-sm" onClick={() => startEdit(rule)} aria-label={t('Edit rule {{pattern}}', { pattern: rule.pattern })} title={t('Change the pattern, action, or description of this rule in the form below.')}>
            {t('Edit')}
          </button>
          <button type="button" className="btn btn-sm btn-danger" onClick={() => setDeleteTarget(rule)} aria-label={t('Delete rule {{pattern}}', { pattern: rule.pattern })} title={t('Delete this rule. Matching calls go back to following the policy. Asks first.')}>
            {t('Delete')}
          </button>
        </span>
      ),
    }] : []),
  ];

  return (
    <section aria-label={t('Rules')}>
      <p className="text-dim cli-perm-hint">
        {t('Rules use Claude Code permission rule syntax, for example Bash(git status:*), Bash(npm run *), WebFetch(domain:example.com), Edit(src/**), or mcp__server__tool. Deny rules win over allow rules; every part of a compound shell command must be allowed.')}
      </p>
      <div className="cli-perm-toolbar">
        <label>
          <span className="text-dim">{t('Scope')}</span>{' '}
          <select value={scopeFilter} onChange={(e) => setScopeFilter(e.target.value as CliPermissionRuleScope | '')} aria-label={t('Scope')} title={t('Show only rules of one scope: Global (the whole tenant), Vessel, or Captain.')}>
            <option value="">{t('All')}</option>
            <option value="Global">{t('Global')}</option>
            <option value="Vessel">{t('Vessel')}</option>
            <option value="Captain">{t('Captain')}</option>
          </select>
        </label>
      </div>
      <ErrorModal error={error} onClose={() => setError('')} />

      <TruncationNote shown={rules.length} total={ruleTotal} />
      <DataTable
        tableKey="cli-permission-rules"
        columns={ruleColumns}
        rows={rules}
        rowKey={(rule) => rule.id}
        onRefresh={load}
        refreshTitle="Reload the rule list."
        placeholder={rules.length > 0 ? undefined : loading ? <p className="text-dim">{t('Loading...')}</p> : <p className="text-dim">{t('No rules yet.')}</p>}
      />

      {canEdit && (
        <form className="card cli-rule-form" onSubmit={(e) => void save(e)} aria-label={editingId ? t('Edit rule') : t('New rule')}>
          <h3 style={{ marginTop: 0 }}>{editingId ? t('Edit rule') : t('New rule')}</h3>
          <div className="settings-grid">
            <div className="form-group">
              <label htmlFor="cli-rule-pattern" title={t('Which calls the rule matches, in Claude Code rule syntax, for example Bash(npm run *), WebFetch(domain:example.com), or Edit(src/**).')}>{t('Pattern')}</label>
              <input id="cli-rule-pattern" title={t('Which calls the rule matches, in Claude Code rule syntax, for example Bash(npm run *), WebFetch(domain:example.com), or Edit(src/**).')} className="mono" value={draft.pattern} onChange={(e) => setDraft({ ...draft, pattern: e.target.value })} placeholder="Bash(git status:*)" />
            </div>
            <div className="form-group">
              <label htmlFor="cli-rule-action" title={t('Allow runs matching calls without asking; Deny refuses them. Deny rules win over allow rules.')}>{t('Action')}</label>
              <select id="cli-rule-action" title={t('Allow runs matching calls without asking; Deny refuses them. Deny rules win over allow rules.')} value={draft.action} onChange={(e) => setDraft({ ...draft, action: e.target.value as CliPermissionRuleAction })}>
                <option value="Allow">{t('Allow')}</option>
                <option value="Deny">{t('Deny')}</option>
              </select>
            </div>
            <div className="form-group">
              <label htmlFor="cli-rule-scope" title={t('Where the rule applies: Global covers every captain in the tenant, Vessel covers missions on one vessel, Captain covers one captain (missions and Ask turns).')}>{t('Scope')}</label>
              <select id="cli-rule-scope" disabled={!!editingId} title={t('Where the rule applies: Global covers every captain in the tenant, Vessel covers missions on one vessel, Captain covers one captain (missions and Ask turns).')} value={draft.scope} onChange={(e) => setDraft({ ...draft, scope: e.target.value as CliPermissionRuleScope })}>
                <option value="Global">{t('Global')}</option>
                <option value="Vessel">{t('Vessel')}</option>
                <option value="Captain">{t('Captain')}</option>
              </select>
            </div>
            {draft.scope === 'Vessel' && (
              <div className="form-group">
                <label htmlFor="cli-rule-vessel" title={t('The vessel whose missions this rule applies to.')}>{t('Vessel')}</label>
                <select id="cli-rule-vessel" disabled={!!editingId} title={t('The vessel whose missions this rule applies to.')} value={draft.vesselId} onChange={(e) => setDraft({ ...draft, vesselId: e.target.value })}>
                  <option value="">{t('Select a vessel...')}</option>
                  {vessels.map((v) => <option key={v.id} value={v.id}>{v.name}</option>)}
                </select>
              </div>
            )}
            {draft.scope === 'Captain' && (
              <div className="form-group">
                <label htmlFor="cli-rule-captain" title={t('The captain this rule applies to, in both its missions and its Ask turns.')}>{t('Captain')}</label>
                <select id="cli-rule-captain" disabled={!!editingId} title={t('The captain this rule applies to, in both its missions and its Ask turns.')} value={draft.captainId} onChange={(e) => setDraft({ ...draft, captainId: e.target.value })}>
                  <option value="">{t('Select a captain...')}</option>
                  {captains.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
                </select>
              </div>
            )}
            <div className="form-group">
              <label htmlFor="cli-rule-description" title={t('Optional note for other admins, for example why the rule exists. Does not affect matching.')}>{t('Description')}</label>
              <input id="cli-rule-description" title={t('Optional note for other admins, for example why the rule exists. Does not affect matching.')} value={draft.description} onChange={(e) => setDraft({ ...draft, description: e.target.value })} placeholder={t('Optional')} />
            </div>
          </div>
          <div className="settings-actions">
            <button type="submit" className="btn btn-primary btn-sm" disabled={!!draftError || saving} title={draftError ?? t('Save the rule. It applies to the next matching call right away.')}>{saving ? t('Saving...') : editingId ? t('Save rule') : t('Create rule')}</button>
            {editingId && <button type="button" className="btn btn-sm" onClick={cancelEdit} title={t('Stop editing and clear the form. The rule keeps its saved values.')}>{t('Cancel')}</button>}
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
      {deletion.dialog}
    </section>
  );
}

type Section = 'requests' | 'rules';

const SECTIONS: Array<{ key: Section; label: string; tooltip: string }> = [
  { key: 'requests', label: 'Requests', tooltip: 'Tool permission prompts from CLI captains (shell commands, web fetches, file access) waiting for your answer, and past decisions.' },
  { key: 'rules', label: 'Rules', tooltip: 'Saved allow and deny rules that answer matching CLI permission prompts without asking.' },
];

/**
 * CLI Tool Permissions, a tab of Settings (`/server?tab=cli-permissions`): the CLI tool calls captains asked Armada to
 * approve (Requests) and the allow / deny rules that decide matching calls without asking (Rules), as two compact
 * sub-sections under the Settings tab strip (no page header of its own). The sub-section lives in `?section=`
 * (`requests` by default) and the inbox and push notifications link here with `?request={id}`; the old
 * `/cli-permissions` route redirects here. Default policies and timeouts stay in the Server tab's settings.
 */
export default function CliPermissions() {
  const { t } = useLocale();
  const { isAdmin } = useAuth();
  const [searchParams, setSearchParams] = useSearchParams();
  const requested = searchParams.get('section');
  const section: Section = requested === 'rules' ? 'rules' : 'requests';

  function select(next: Section) {
    const params = new URLSearchParams(searchParams);
    params.set('section', next);
    if (next === 'rules') params.delete('request');
    setSearchParams(params);
  }

  return (
    <div className="cli-perm-tab">
      <div className="cli-perm-tab-header">
        <div className="segmented cli-perm-sections" role="radiogroup" aria-label={t('CLI Tool Permissions')}>
          {SECTIONS.map((entry) => (
            <label key={entry.key} className={`segmented-option${section === entry.key ? ' active' : ''}`} title={t(entry.tooltip)}>
              <input type="radio" name="cli-perm-section" checked={section === entry.key} onChange={() => select(entry.key)} />
              <span>{t(entry.label)}</span>
            </label>
          ))}
        </div>
        <p className="text-dim cli-perm-hint">
          {t('Shell commands, file edits, and fetches that CLI captains asked to run, and the rules that allow or deny them.')}
          {isAdmin && (
            <>
              {' '}
              <Link to="/server?tab=server" title={t('Default policies for Ask conversations and missions, owner approval, and the request timeout are server settings.')}>{t('Default policies')}</Link>
            </>
          )}
        </p>
      </div>
      {section === 'rules' ? <RulesPanel /> : <RequestsPanel />}
    </div>
  );
}
