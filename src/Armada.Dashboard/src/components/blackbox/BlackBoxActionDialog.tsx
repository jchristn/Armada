import { useCallback, useEffect, useState } from 'react';
import { applyBlackBoxRecommendation, runBlackBoxRecovery } from '../../api/client';
import type { BlackBoxAction, BlackBoxRecoveryStep, BlackBoxRecoveryStepResult } from '../../types/blackbox';
import { useLocale } from '../../context/LocaleContext';
import { blackBoxErrorCode, blackBoxErrorStatus } from '../../lib/blackbox';
import DialogShell from '../shared/DialogShell';
import LoadingIndicator from '../shared/LoadingIndicator';

/** What the dialog runs: a ranked recovery path or a configuration recommendation recorded in the entry. */
export interface BlackBoxActionTarget {
  kind: 'recovery' | 'recommendation';
  id: string;
  title: string;
  /** The path is marked dataLossRisk (recovery only); a step marked so in the plan also asks for the acknowledgement. */
  dataLossRisk?: boolean;
}

interface BlackBoxActionDialogProps {
  open: boolean;
  entryId: string;
  target: BlackBoxActionTarget | null;
  onClose: () => void;
  /** Called after a confirmed run finished (succeeded or not), so the host reloads the entry. */
  onRan: (succeeded: boolean) => void;
}

type Phase = 'dryRun' | 'review' | 'running' | 'done' | 'error';

interface Plan {
  steps: BlackBoxRecoveryStep[];
  action: BlackBoxAction | null;
  message: string | null;
  confirmToken: string | null;
  expiresUtc: string | null;
}

/**
 * Dry run, then confirm: runs the recorded recovery path or recommendation with dryRun first and shows the plan
 * (every step and command), asks for the data loss acknowledgement when a step can lose work, and runs it only after
 * the user confirms, with the confirm token from the dry run and AuthorKind Operator. Refusals (an expired token, a
 * mission that changed, missing permission, a server without the runner) are explained and offer a new dry run.
 */
export default function BlackBoxActionDialog({ open, entryId, target, onClose, onRan }: BlackBoxActionDialogProps) {
  const { t, formatDateTime } = useLocale();
  const [phase, setPhase] = useState<Phase>('dryRun');
  const [plan, setPlan] = useState<Plan | null>(null);
  const [results, setResults] = useState<BlackBoxRecoveryStepResult[]>([]);
  const [resultMessage, setResultMessage] = useState<string | null>(null);
  const [succeeded, setSucceeded] = useState(false);
  const [error, setError] = useState('');
  const [acknowledged, setAcknowledged] = useState(false);

  const explain = useCallback((err: unknown): string => {
    const code = blackBoxErrorCode(err);
    const message = err instanceof Error ? err.message : String(err);
    if (code === 'BlackBoxConfirmationRequired' || code === 'BlackBoxConfirmationInvalid') {
      return t('The confirmation expired, was already used, or the mission changed since the dry run. Run the dry run again to see the current plan.');
    }
    if (code === 'BlackBoxActionsUnavailable') return t('This server cannot run recovery actions yet. Follow the steps by hand.');
    if ((blackBoxErrorStatus(err) === 401 || blackBoxErrorStatus(err) === 403)) return t('You are not allowed to run this action: {{message}}', { message });
    if (blackBoxErrorStatus(err) === 404) return t('The entry or this action no longer exists. Reload the black box entry.');
    return t('The action failed: {{message}}', { message });
  }, [t]);

  const dryRun = useCallback(async () => {
    if (!target) return;
    setPhase('dryRun');
    setError('');
    setAcknowledged(false);
    setResults([]);
    try {
      if (target.kind === 'recovery') {
        const r = await runBlackBoxRecovery(entryId, target.id, { dryRun: true, authorKind: 'Operator' });
        setPlan({ steps: r.plan ?? [], action: null, message: r.message ?? null, confirmToken: r.confirmToken ?? null, expiresUtc: r.confirmTokenExpiresUtc ?? null });
      } else {
        const r = await applyBlackBoxRecommendation(entryId, target.id, { dryRun: true, authorKind: 'Operator' });
        setPlan({ steps: [], action: r.action ?? null, message: r.message ?? null, confirmToken: r.confirmToken ?? null, expiresUtc: r.confirmTokenExpiresUtc ?? null });
      }
      setPhase('review');
    } catch (err: unknown) {
      setError(explain(err));
      setPhase('error');
    }
  }, [entryId, target, explain]);

  useEffect(() => {
    if (open && target) void dryRun();
    if (!open) { setPlan(null); setPhase('dryRun'); setError(''); }
  }, [open, target, dryRun]);

  const needsAcknowledgement = !!target && target.kind === 'recovery'
    && (!!target.dataLossRisk || (plan?.steps ?? []).some((s) => s.dataLossRisk));

  async function confirm() {
    if (!target || !plan?.confirmToken) return;
    if (needsAcknowledgement && !acknowledged) return;
    setPhase('running');
    setError('');
    try {
      if (target.kind === 'recovery') {
        const r = await runBlackBoxRecovery(entryId, target.id, {
          dryRun: false, confirmToken: plan.confirmToken, acknowledgeDataLoss: needsAcknowledgement && acknowledged, authorKind: 'Operator',
        });
        setResults(r.stepResults ?? []);
        setResultMessage(r.message ?? null);
        setSucceeded(!!r.succeeded);
        onRan(!!r.succeeded);
      } else {
        const r = await applyBlackBoxRecommendation(entryId, target.id, { dryRun: false, confirmToken: plan.confirmToken, authorKind: 'Operator' });
        setResults([]);
        setResultMessage(r.message ?? null);
        setSucceeded(!!r.succeeded);
        onRan(!!r.succeeded);
      }
      setPhase('done');
    } catch (err: unknown) {
      setError(explain(err));
      setPhase('error');
    }
  }

  if (!open || !target) return null;

  const title = target.kind === 'recovery'
    ? t('Recovery: {{title}}', { title: target.title })
    : t('Apply recommendation: {{title}}', { title: target.title });

  const footer = (
    <>
      <button type="button" className="btn" onClick={onClose} title={t('Close this dialog. Nothing runs unless you confirmed.')}>{phase === 'done' ? t('Close') : t('Cancel')}</button>
      {(phase === 'error' || phase === 'done') && (
        <button type="button" className="btn" onClick={() => void dryRun()} title={t('Ask the server for the current plan again without running anything.')}>{t('Dry run again')}</button>
      )}
      {phase === 'review' && (
        <button
          type="button"
          className={`btn ${needsAcknowledgement ? 'btn-danger' : 'btn-primary'}`}
          onClick={() => void confirm()}
          disabled={!plan?.confirmToken || (needsAcknowledgement && !acknowledged)}
          title={needsAcknowledgement && !acknowledged ? t('Tick the acknowledgement above first.') : t('Run exactly the steps listed above.')}
        >
          {t('Confirm and run')}
        </button>
      )}
    </>
  );

  return (
    <DialogShell open={open} title={title} subtitle={t('Dry run first: nothing has run yet. Review the plan, then confirm.')} onClose={onClose} footer={footer} size="xl" className="blackbox-dialog" dismissible={phase !== 'running'}>
      {phase === 'dryRun' && <LoadingIndicator label={t('Running the dry run...')} />}
      {phase === 'running' && <LoadingIndicator label={t('Running the confirmed steps...')} />}
      {phase === 'error' && <div className="blackbox-notice blackbox-notice-error" role="alert">{error}</div>}
      {(phase === 'review' || phase === 'running') && plan && (
        <div className="blackbox-action-plan">
          {plan.message && <p>{plan.message}</p>}
          {plan.steps.length > 0 && (
            <ol className="blackbox-list" aria-label={t('Steps')}>
              {plan.steps.map((step) => (
                <li key={step.order} data-data-loss={step.dataLossRisk ? 'true' : undefined}>
                  <div>{step.description}{step.dataLossRisk && <span className="blackbox-chip blackbox-chip-danger" style={{ marginLeft: '0.4rem' }}>{t('Data loss risk')}</span>}</div>
                  {step.action && <code className="mono">{step.action.tool} {JSON.stringify(step.action.arguments ?? {})}</code>}
                  {!step.action && (step.mcpCommand || step.cliCommand || step.restCommand) && <code className="mono">{step.mcpCommand || step.cliCommand || step.restCommand}</code>}
                  {!step.action && !step.mcpCommand && !step.cliCommand && !step.restCommand && <span className="text-dim"> ({t('manual step, not run')})</span>}
                </li>
              ))}
            </ol>
          )}
          {plan.action && (
            <p><span className="text-dim">{t('Will call')}: </span><code className="mono">{plan.action.tool} {JSON.stringify(plan.action.arguments ?? {})}</code></p>
          )}
          {plan.expiresUtc && <p className="text-dim">{t('This plan can be confirmed until {{time}}.', { time: formatDateTime(plan.expiresUtc) })}</p>}
          {!plan.confirmToken && <div className="blackbox-notice" role="status">{t('The server returned no confirmation token, so this plan cannot be run from here.')}</div>}
          {needsAcknowledgement && (
            <label className="blackbox-notice blackbox-notice-error" style={{ display: 'flex', gap: '0.5rem', alignItems: 'flex-start' }}>
              <input type="checkbox" checked={acknowledged} onChange={(e) => setAcknowledged(e.target.checked)} aria-label={t('I understand that this can delete work that cannot be recovered.')} />
              <span>{t('I understand that this can delete work that cannot be recovered.')}</span>
            </label>
          )}
        </div>
      )}
      {phase === 'done' && (
        <div>
          <div className={`blackbox-notice ${succeeded ? 'blackbox-notice-info' : 'blackbox-notice-error'}`} role="status">
            {succeeded ? t('The action ran. The entry was annotated with what ran.') : t('The action did not complete. The entry was annotated with what ran.')}
          </div>
          {resultMessage && <p>{resultMessage}</p>}
          {results.length > 0 && (
            <ol className="blackbox-list" aria-label={t('Step results')}>
              {results.map((r) => (
                <li key={r.order}>
                  <strong style={{ color: r.succeeded ? 'var(--green)' : 'var(--red)' }}>{r.succeeded ? t('Succeeded') : t('Failed')}</strong>
                  {r.tool && <> <code className="mono">{r.tool}</code></>}
                  {r.message && <>: {r.message}</>}
                </li>
              ))}
            </ol>
          )}
        </div>
      )}
    </DialogShell>
  );
}
