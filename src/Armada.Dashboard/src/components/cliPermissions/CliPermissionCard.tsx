import { useId } from 'react';
import { Link } from 'react-router-dom';
import type { CliPermissionRequest } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import CopyButton from '../shared/CopyButton';
import { decisionSourceText, isPendingRequest, requestStatusLabel } from '../../lib/cliPermissions';
import CliPermissionCountdown from './CliPermissionCountdown';
import CliPermissionDecisionControls from './CliPermissionDecisionControls';

/** Pretty-print JSON text; fall back to the raw text. */
function prettyJson(raw: string | null | undefined): string {
  if (!raw) return '';
  try { return JSON.stringify(JSON.parse(raw), null, 2); } catch { return raw; }
}

interface CliPermissionCardProps {
  request: CliPermissionRequest;
  onDecided?: (updated: CliPermissionRequest) => void;
  /** Highlight (deep link target on the CLI Tool Permissions page). */
  highlighted?: boolean;
  /** Show the conversation title (on the permissions page, where the request is out of context). */
  showThread?: boolean;
}

/**
 * Approval card for a CLI tool call a captain wants to make (shell command, file edit, web fetch): the tool, the
 * command or input, who asked, a live expiry countdown while pending, and the decision afterwards. Approvers get
 * Allow once / Allow and remember / Deny; everyone else sees that it waits on an admin.
 */
export default function CliPermissionCard({ request, onDecided, highlighted, showThread }: CliPermissionCardProps) {
  const { t, formatRelativeTime, formatDateTime } = useLocale();
  const status = String(request.status || '');
  const normalized = status.toLowerCase();
  const pending = isPendingRequest(request);
  const input = prettyJson(request.inputText);
  const summary = (request.summaryText ?? '').trim();
  const summaryId = useId();
  const decidedHow = pending ? null : decisionSourceText(t, request);

  return (
    <div
      id={`cli-permission-${request.id}`}
      className={`ask-confirm-card cli-perm-card is-${normalized || 'unknown'}${highlighted ? ' is-highlighted' : ''}`}
      role="group"
      aria-label={t('CLI permission: {{tool}}', { tool: request.toolName })}
      data-request-id={request.id}
    >
      <div className="ask-confirm-header">
        <span className="ask-confirm-label">{pending ? t('Permission needed') : t('CLI permission')}</span>
        <code className="ask-confirm-tool">{request.toolName || t('tool')}</code>
        <span className="ask-confirm-status">
          <span className={`tag cli-perm-status is-${normalized}`}>{requestStatusLabel(t, status)}</span>
        </span>
        <CliPermissionCountdown expiresUtc={request.expiresUtc} active={pending} />
      </div>

      {summary && (
        <div className="ask-confirm-pre-wrap cli-perm-summary">
          <pre id={summaryId} className="ask-confirm-pre">{summary}</pre>
          <CopyButton text={summary} title={t('Copy command')} />
        </div>
      )}

      <dl className="cli-perm-meta">
        {request.captainName || request.captainId ? (
          <div>
            <dt>{t('Captain')}</dt>
            <dd>{request.captainId ? <Link to={`/captains/${encodeURIComponent(request.captainId)}`}>{request.captainName || request.captainId}</Link> : request.captainName}</dd>
          </div>
        ) : null}
        {request.vesselId && (
          <div>
            <dt>{t('Vessel')}</dt>
            <dd><Link to={`/vessels/${encodeURIComponent(request.vesselId)}`}>{request.vesselName || request.vesselId}</Link></dd>
          </div>
        )}
        {request.missionId && (
          <div>
            <dt>{t('Mission')}</dt>
            <dd><Link to={`/missions/${encodeURIComponent(request.missionId)}`}>{request.missionTitle || request.missionId}</Link></dd>
          </div>
        )}
        {showThread && request.threadId && (
          <div>
            <dt>{t('Conversation')}</dt>
            <dd><Link to={`/ask/${encodeURIComponent(request.threadId)}`}>{request.threadTitle || request.threadId}</Link></dd>
          </div>
        )}
        {request.createdUtc && (
          <div>
            <dt>{t('Asked')}</dt>
            <dd><time dateTime={request.createdUtc} title={formatDateTime(request.createdUtc)}>{formatRelativeTime(request.createdUtc)}</time></dd>
          </div>
        )}
      </dl>

      {input && input !== '{}' && (
        <details className="ask-confirm-details">
          <summary>{t('Input')}</summary>
          <div className="ask-confirm-pre-wrap">
            <pre className="ask-confirm-pre">{input}</pre>
            <CopyButton text={request.inputText ?? ''} title={t('Copy input')} />
          </div>
        </details>
      )}

      {pending && request.canDecide && (
        <CliPermissionDecisionControls request={request} onDecided={onDecided} describedBy={summary ? summaryId : undefined} />
      )}
      {pending && !request.canDecide && (
        <p className="ask-confirm-outcome text-dim cli-perm-waiting">{t('Waiting for an admin to decide.')}</p>
      )}

      {!pending && (
        <div className={`ask-confirm-outcome cli-perm-outcome${normalized === 'allowed' ? ' is-success' : normalized === 'denied' ? ' is-failed' : ' text-dim'}`}>
          <span>
            {normalized === 'allowed' && t('Allowed. The captain ran the tool.')}
            {normalized === 'denied' && t('Denied. The tool did not run.')}
            {normalized === 'expired' && t('Expired without a decision. The tool did not run.')}
            {normalized === 'cancelled' && t('Cancelled. The turn ended before a decision.')}
          </span>
          {decidedHow && <span className="text-dim"> {decidedHow}</span>}
          {request.decidedUtc && (
            <span className="text-dim"> <time dateTime={request.decidedUtc} title={formatDateTime(request.decidedUtc)}>{formatRelativeTime(request.decidedUtc)}</time></span>
          )}
          {request.decisionMessage && <p className="cli-perm-decision-message">{t('Message: {{text}}', { text: request.decisionMessage })}</p>}
        </div>
      )}
    </div>
  );
}
