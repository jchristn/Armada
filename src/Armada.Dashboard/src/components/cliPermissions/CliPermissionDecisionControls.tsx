import { useId, useState } from 'react';
import { decideCliPermissionRequest } from '../../api/client';
import type { CliPermissionDecision, CliPermissionRequest, CliPermissionRuleScope } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import DialogShell from '../shared/DialogShell';

interface CliPermissionDecisionControlsProps {
  request: CliPermissionRequest;
  /** Called with the updated request the server returned. */
  onDecided?: (updated: CliPermissionRequest) => void;
  /** Id of the element that describes what the buttons act on (screen readers). */
  describedBy?: string;
}

/**
 * Allow once / Allow and remember / Deny for a pending CLI permission request. Allow and remember (offered only when
 * the caller may create rules) opens a dialog to edit the allow-rule pattern, prefilled with the suggested rule, and
 * pick its scope; Deny takes an optional message the captain sees. Decides through the REST API.
 */
export default function CliPermissionDecisionControls({ request, onDecided, describedBy }: CliPermissionDecisionControlsProps) {
  const { t } = useLocale();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [rememberOpen, setRememberOpen] = useState(false);
  const [denyOpen, setDenyOpen] = useState(false);
  const [pattern, setPattern] = useState('');
  const [scope, setScope] = useState<CliPermissionRuleScope>('Captain');
  const [message, setMessage] = useState('');
  const patternId = useId();
  const scopeId = useId();
  const messageId = useId();

  async function decide(decision: CliPermissionDecision, extra?: { message?: string; rulePattern?: string; ruleScope?: CliPermissionRuleScope }): Promise<boolean> {
    setBusy(true);
    setError('');
    try {
      const updated = await decideCliPermissionRequest(request.id, { decision, ...extra });
      if (updated) onDecided?.(updated);
      return true;
    } catch (err: unknown) {
      setError(err instanceof Error && err.message ? err.message : t('Permission decision failed.'));
      return false;
    } finally {
      setBusy(false);
    }
  }

  function openRemember() {
    setPattern(request.suggestedRule || request.toolName || '');
    setScope(request.captainId ? 'Captain' : request.vesselId ? 'Vessel' : 'Global');
    setRememberOpen(true);
  }

  function openDeny() {
    setMessage('');
    setDenyOpen(true);
  }

  async function confirmRemember() {
    const trimmed = pattern.trim();
    if (!trimmed) return;
    if (await decide('AllowAndRemember', { rulePattern: trimmed, ruleScope: scope })) setRememberOpen(false);
  }

  async function confirmDeny() {
    const trimmed = message.trim();
    if (await decide('Deny', trimmed ? { message: trimmed } : undefined)) setDenyOpen(false);
  }

  return (
    <div className="cli-perm-actions" onClick={(e) => e.stopPropagation()}>
      <button type="button" className="btn btn-sm btn-primary" aria-describedby={describedBy} onClick={() => void decide('AllowOnce')} disabled={busy}>
        {busy && !rememberOpen && !denyOpen ? t('Working...') : t('Allow once')}
      </button>
      {request.canRemember && (
        <button type="button" className="btn btn-sm" aria-describedby={describedBy} onClick={openRemember} disabled={busy}>
          {t('Allow and remember')}
        </button>
      )}
      <button type="button" className="btn btn-sm btn-danger" aria-describedby={describedBy} onClick={openDeny} disabled={busy}>
        {t('Deny')}
      </button>
      {error && !rememberOpen && !denyOpen && <span className="cli-perm-error" role="alert">{error}</span>}

      <DialogShell
        open={rememberOpen}
        title={t('Allow and remember')}
        subtitle={t('Allows this call now and creates an allow rule so matching calls run without asking.')}
        onClose={() => { if (!busy) setRememberOpen(false); }}
        dismissible={!busy}
        footer={(
          <>
            <button type="button" className="btn" onClick={() => setRememberOpen(false)} disabled={busy}>{t('Cancel')}</button>
            <button type="button" className="btn btn-primary" onClick={() => void confirmRemember()} disabled={busy || !pattern.trim()}>
              {busy ? t('Working...') : t('Allow and create rule')}
            </button>
          </>
        )}
      >
        {error && <div className="alert alert-error" role="alert">{error}</div>}
        <div className="form-group">
          <label htmlFor={patternId}>{t('Rule pattern')}</label>
          <input id={patternId} className="mono" value={pattern} onChange={(e) => setPattern(e.target.value)} aria-describedby={`${patternId}-help`} />
          <span id={`${patternId}-help`} className="text-dim form-help">
            {t('Claude Code permission rule syntax, for example Bash(git status:*) or WebFetch(domain:example.com).')}
          </span>
        </div>
        <div className="form-group">
          <label htmlFor={scopeId}>{t('Remember for')}</label>
          <select id={scopeId} value={scope} onChange={(e) => setScope(e.target.value as CliPermissionRuleScope)}>
            {request.captainId && <option value="Captain">{request.captainName ? t('Captain {{name}}', { name: request.captainName }) : t('This captain')}</option>}
            {request.vesselId && <option value="Vessel">{request.vesselName ? t('Vessel {{name}}', { name: request.vesselName }) : t('This vessel')}</option>}
            <option value="Global">{t('Everywhere (global)')}</option>
          </select>
        </div>
      </DialogShell>

      <DialogShell
        open={denyOpen}
        title={t('Deny')}
        subtitle={t('The captain is told the tool call was denied.')}
        onClose={() => { if (!busy) setDenyOpen(false); }}
        dismissible={!busy}
        footer={(
          <>
            <button type="button" className="btn" onClick={() => setDenyOpen(false)} disabled={busy}>{t('Cancel')}</button>
            <button type="button" className="btn btn-danger" onClick={() => void confirmDeny()} disabled={busy}>
              {busy ? t('Working...') : t('Deny')}
            </button>
          </>
        )}
      >
        {error && <div className="alert alert-error" role="alert">{error}</div>}
        <div className="form-group">
          <label htmlFor={messageId}>{t('Optional message for the captain')}</label>
          <textarea id={messageId} rows={3} value={message} onChange={(e) => setMessage(e.target.value)} placeholder={t('e.g., Use the test script instead of deleting files.')} />
        </div>
      </DialogShell>
    </div>
  );
}
