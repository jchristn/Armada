import type { AskActionProposal } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import StatusBadge from '../shared/StatusBadge';
import CopyButton from '../shared/CopyButton';

/** Pretty-print JSON text; fall back to the raw text. */
export function prettyJson(raw: string | null | undefined): string {
  if (!raw) return '';
  try { return JSON.stringify(JSON.parse(raw), null, 2); } catch { return raw; }
}

interface AskConfirmCardProps {
  proposal: AskActionProposal;
  onApprove?: (proposal: AskActionProposal) => void;
  onReject?: (proposal: AskActionProposal) => void;
  /** True while an approve or reject call for this proposal is in flight. */
  busy?: boolean;
  /** Compact mode for ActionResult messages: no heading text, outcome only. */
  compact?: boolean;
}

/**
 * Inline confirmation for a state-changing tool call. Shows the tool, a one-line summary, the exact arguments,
 * Approve / Reject while pending, and the outcome afterwards. Nothing runs until the user approves.
 */
export default function AskConfirmCard({ proposal, onApprove, onReject, busy, compact }: AskConfirmCardProps) {
  const { t, formatRelativeTime } = useLocale();
  const status = String(proposal.status || '');
  const normalized = status.toLowerCase();
  const pending = normalized === 'pending';
  const args = prettyJson(proposal.argumentsText);
  const result = prettyJson(proposal.resultText);
  const fromQuickAction = String(proposal.source).toLowerCase() === 'quickaction';

  return (
    <div className={`ask-confirm-card is-${normalized || 'unknown'}${compact ? ' is-compact' : ''}`} role="group" aria-label={t('Action: {{tool}}', { tool: proposal.toolName })}>
      <div className="ask-confirm-header">
        <span className="ask-confirm-label">
          {pending ? t('Approval needed') : t('Action')}
        </span>
        <code className="ask-confirm-tool">{proposal.toolName}</code>
        <span className="ask-confirm-source text-dim">{fromQuickAction ? t('Quick action') : t('Proposed by the captain')}</span>
        <span className="ask-confirm-status"><StatusBadge status={status} /></span>
      </div>

      {proposal.summaryText && <p className="ask-confirm-summary">{proposal.summaryText}</p>}

      {args && (
        <details className="ask-confirm-details">
          <summary>{t('Exact arguments')}</summary>
          <div className="ask-confirm-pre-wrap">
            <pre className="ask-confirm-pre">{args}</pre>
            <CopyButton text={proposal.argumentsText ?? ''} title={t('Copy arguments')} />
          </div>
        </details>
      )}

      {pending && (
        <div className="ask-confirm-actions">
          <span className="text-dim ask-confirm-note">
            {proposal.expiresUtc
              ? t('Nothing runs until you approve. Expires {{time}}.', { time: formatRelativeTime(proposal.expiresUtc) })
              : t('Nothing runs until you approve.')}
          </span>
          <button type="button" className="btn btn-sm" onClick={() => onReject?.(proposal)} disabled={busy || !onReject}>
            {t('Reject')}
          </button>
          <button type="button" className="btn btn-sm btn-primary" onClick={() => onApprove?.(proposal)} disabled={busy || !onApprove}>
            {busy ? t('Working...') : t('Approve')}
          </button>
        </div>
      )}

      {normalized === 'approved' && <p className="ask-confirm-outcome text-dim">{t('Approved. Running now...')}</p>}
      {normalized === 'rejected' && <p className="ask-confirm-outcome text-dim">{t('Rejected. Nothing was run.')}</p>}
      {normalized === 'expired' && <p className="ask-confirm-outcome text-dim">{t('Expired without running.')}</p>}
      {normalized === 'executed' && (
        <div className="ask-confirm-outcome is-success">
          <span>
            {proposal.executedUtc ? t('Ran {{time}}.', { time: formatRelativeTime(proposal.executedUtc) }) : t('Ran successfully.')}
          </span>
          {result && (
            <details className="ask-confirm-details">
              <summary>{t('Result')}</summary>
              <pre className="ask-confirm-pre">{result}</pre>
            </details>
          )}
        </div>
      )}
      {normalized === 'failed' && (
        <div className="ask-confirm-outcome is-failed" role="alert">
          {proposal.errorText || t('The action failed.')}
        </div>
      )}
    </div>
  );
}
