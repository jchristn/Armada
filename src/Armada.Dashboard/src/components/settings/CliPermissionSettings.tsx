import { useEffect, useState } from 'react';
import { updateSettings } from '../../api/client';
import type { CliPermissionPolicy, CliPermissionSettingsData } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import CliPermissionPolicySelect from '../cliPermissions/CliPermissionPolicySelect';
import { PROMPT_TIMEOUT_RANGE } from '../../lib/cliPermissions';

interface CliPermissionSettingsProps {
  permissions: CliPermissionSettingsData | null | undefined;
  /** Disable editing (remote proxy mode). */
  locked: boolean;
  /** Called with the full settings payload the server returned after a save. */
  onSaved: (updated: Record<string, unknown>) => void;
  /** Toast helper from the host page. */
  notify: (severity: 'success' | 'error', message: string) => void;
}

/** Server defaults of the Permissions group (CliPermissionSettings). */
export const CLI_PERMISSION_DEFAULTS: CliPermissionSettingsData = {
  askDefaultPolicy: 'ApproveInArmada',
  missionDefaultPolicy: 'Bypass',
  allowOwnerApproval: false,
  promptTimeoutSeconds: 600,
};

interface Draft {
  askDefaultPolicy: CliPermissionPolicy;
  missionDefaultPolicy: CliPermissionPolicy;
  allowOwnerApproval: boolean;
  promptTimeoutSeconds: string;
}

function toDraft(s: CliPermissionSettingsData | null | undefined): Draft {
  const v = { ...CLI_PERMISSION_DEFAULTS, ...(s ?? {}) };
  return {
    askDefaultPolicy: v.askDefaultPolicy,
    missionDefaultPolicy: v.missionDefaultPolicy,
    allowOwnerApproval: !!v.allowOwnerApproval,
    promptTimeoutSeconds: String(v.promptTimeoutSeconds),
  };
}

/** True when the timeout is a whole number of seconds from 10 to 3600. */
export function validPromptTimeout(value: string): boolean {
  if (!/^\d+$/.test(value.trim())) return false;
  const n = Number(value);
  return n >= PROMPT_TIMEOUT_RANGE.min && n <= PROMPT_TIMEOUT_RANGE.max;
}

/**
 * Settings page section for CLI tool permissions (`Permissions`, global admin only): the default policy of Ask
 * conversations and of missions, whether conversation and mission owners may approve their own requests, and how long
 * a request waits for a decision. Choosing Bypass needs the strong warning confirmation.
 */
export default function CliPermissionSettings({ permissions, locked, onSaved, notify }: CliPermissionSettingsProps) {
  const { t } = useLocale();
  const [draft, setDraft] = useState<Draft>(() => toDraft(permissions));
  const [dirty, setDirty] = useState(false);
  const [saving, setSaving] = useState(false);

  useEffect(() => { if (!dirty) setDraft(toDraft(permissions)); }, [permissions, dirty]);

  const timeoutValid = validPromptTimeout(draft.promptTimeoutSeconds);

  function set<K extends keyof Draft>(field: K, value: Draft[K]) {
    setDraft((d) => ({ ...d, [field]: value }));
    setDirty(true);
  }

  async function save() {
    if (!timeoutValid) return;
    setSaving(true);
    try {
      const updated = await updateSettings({
        permissions: {
          askDefaultPolicy: draft.askDefaultPolicy,
          missionDefaultPolicy: draft.missionDefaultPolicy,
          allowOwnerApproval: draft.allowOwnerApproval,
          promptTimeoutSeconds: Number(draft.promptTimeoutSeconds),
        },
      });
      setDirty(false);
      onSaved(updated as Record<string, unknown>);
      notify('success', t('CLI tool permission settings saved.'));
    } catch (e: unknown) {
      notify('error', t('Failed: {{message}}', { message: e instanceof Error ? e.message : t('Unknown error') }));
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="settings-section" style={{ marginTop: '1.5rem' }}>
      <h3>{t('CLI Tool Permissions')}</h3>
      <p className="text-muted" style={{ marginBottom: '0.75rem' }}>
        {t('How CLI captains handle shell commands, file edits, and fetches that need permission when neither the conversation nor the captain sets a policy. Refuse lets the CLI refuse them; Approve in Armada asks an approver here (Claude Code only; other runtimes fall back to Refuse); Bypass runs them without asking.')}
      </p>
      <fieldset disabled={locked} className="settings-fieldset">
        <div className="settings-grid">
          <div className="form-group">
            <label htmlFor="cli-perm-ask-default">{t('Ask conversation default')}</label>
            <CliPermissionPolicySelect
              id="cli-perm-ask-default"
              value={draft.askDefaultPolicy}
              onChange={(v) => set('askDefaultPolicy', v ?? CLI_PERMISSION_DEFAULTS.askDefaultPolicy)}
              allowInherit={false}
              allowBypass
              disabled={locked}
              ariaDescribedBy="cli-perm-ask-default-help"
            />
            <span id="cli-perm-ask-default-help" className="text-dim form-help">{t('Used by Ask conversation turns (default Approve in Armada). Narrations and summaries never ask; they refuse instead.')}</span>
          </div>
          <div className="form-group">
            <label htmlFor="cli-perm-mission-default">{t('Mission default')}</label>
            <CliPermissionPolicySelect
              id="cli-perm-mission-default"
              value={draft.missionDefaultPolicy}
              onChange={(v) => set('missionDefaultPolicy', v ?? CLI_PERMISSION_DEFAULTS.missionDefaultPolicy)}
              allowInherit={false}
              allowBypass
              disabled={locked}
              ariaDescribedBy="cli-perm-mission-default-help"
            />
            <span id="cli-perm-mission-default-help" className="text-dim form-help">{t('Used by missions (default Bypass, the previous behavior). A vessel with auto-approve off caps missions at Refuse.')}</span>
          </div>
          <div className="form-group">
            <label htmlFor="cli-perm-timeout">{t('Prompt timeout (seconds)')}</label>
            <input
              id="cli-perm-timeout"
              type="number"
              min={PROMPT_TIMEOUT_RANGE.min}
              max={PROMPT_TIMEOUT_RANGE.max}
              value={draft.promptTimeoutSeconds}
              onChange={(e) => set('promptTimeoutSeconds', e.target.value)}
              aria-invalid={!timeoutValid}
              aria-describedby="cli-perm-timeout-help"
            />
            <span id="cli-perm-timeout-help" className={timeoutValid ? 'text-dim form-help' : 'field-error'}>
              {timeoutValid
                ? t('How long a request waits for a decision before it expires and is denied (10-3600, default 600).')
                : t('Must be a whole number from {{min}} to {{max}}.', { min: PROMPT_TIMEOUT_RANGE.min, max: PROMPT_TIMEOUT_RANGE.max.toLocaleString() })}
            </span>
          </div>
          <div className="form-group">
            <label className="settings-checkbox">
              <input type="checkbox" checked={draft.allowOwnerApproval} onChange={(e) => set('allowOwnerApproval', e.target.checked)} />
              {' '}{t('Let owners approve their own requests')}
            </label>
            <span className="text-dim form-help">{t('When on, the owner of a conversation or mission may allow or deny its requests. Admins and tenant admins can always decide; only they can create rules.')}</span>
          </div>
        </div>
        <div className="settings-actions">
          <button type="button" className="btn-primary btn-sm" onClick={() => void save()} disabled={!timeoutValid || saving || !dirty}>
            {saving ? t('Saving...') : t('Save CLI Tool Permissions')}
          </button>
          {dirty && <button type="button" className="btn btn-sm" onClick={() => { setDirty(false); setDraft(toDraft(permissions)); }}>{t('Discard changes')}</button>}
          {dirty && <span className="text-dim">{t('Unsaved changes')}</span>}
        </div>
      </fieldset>
    </div>
  );
}
