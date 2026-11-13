import { useState } from 'react';
import { Link } from 'react-router-dom';
import type { AskBlackBoxCard as AskBlackBoxCardModel, AskBlackBoxFollowUp } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import { getBlackBoxBrief } from '../../api/client';
import { copyToClipboard } from '../shared/CopyButton';
import { blackBoxChips, cardFollowUps, followUpArguments, shortCommit } from '../../lib/askBlackBox';
import { useAskBlackBoxActions } from './AskBlackBoxActions';

interface AskBlackBoxCardProps {
  card: AskBlackBoxCardModel;
}

/**
 * The body of a black box card in an Ask Armada conversation: status chips, where and how it failed, why (the entry's
 * findings), conflicting missions and files, recovery paths and configuration recommendations as buttons that create
 * proposals, Copy Markdown, and the follow-ups (Fix it, Apply the configuration change, Draft an Armada fix mission,
 * Compare with similar failures). Every value comes from the entry.
 */
export default function AskBlackBoxCard({ card }: AskBlackBoxCardProps) {
  const { t } = useLocale();
  const actions = useAskBlackBoxActions();
  const [copyState, setCopyState] = useState<'idle' | 'copied' | 'failed'>('idle');
  const digest = card.digest;
  const entryId = digest.entryId;
  const chips = blackBoxChips(digest);
  const conflicting = card.conflictingMissions ?? [];
  const files = card.conflictedFiles ?? [];
  const paths = [...(digest.recoveryPaths ?? [])].sort((a, b) => a.rank - b.rank);
  const recommendations = card.recommendations ?? [];
  const followUps = cardFollowUps(card).filter((f) => (f.toolName ? true : !!actions?.canAsk));
  const where = [digest.sourceFile ? `${digest.sourceFile}${digest.sourceLine ? `:${digest.sourceLine}` : ''}` : null, digest.method, digest.host].filter(Boolean).join(' · ');

  async function copyBrief() {
    try {
      const brief = await getBlackBoxBrief(entryId);
      await copyToClipboard(typeof brief === 'string' ? brief : String(brief ?? ''));
      setCopyState('copied');
    } catch {
      setCopyState('failed');
    }
  }

  function run(followUp: AskBlackBoxFollowUp) {
    if (!actions) return;
    if (followUp.toolName) void actions.propose(followUp.toolName, followUpArguments(followUp));
    else if (followUp.prompt) actions.ask(followUp.prompt);
  }

  return (
    <div className="ask-blackbox">
      {chips.length > 0 && (
        <ul className="ask-blackbox-chips" aria-label={t('Black box summary')}>
          {chips.map((chip) => (
            <li key={chip.key} className={`ask-blackbox-chip is-${chip.tone}`} title={t(chip.title)}>
              <span className="text-dim">{t(chip.label)}</span> <strong>{chip.value}</strong>
            </li>
          ))}
        </ul>
      )}

      <dl className="ask-work-row-facts ask-blackbox-facts">
        {where && <div><dt title={t('The Armada source file, method and host where the failure was recorded.')}>{t('Where')}</dt><dd className="mono">{where}</dd></div>}
        {(digest.statusBefore || digest.statusAfter) && (
          <div><dt title={t('The mission status before and after the failure.')}>{t('Status')}</dt><dd>{digest.statusBefore ?? '?'} &rarr; {digest.statusAfter ?? '?'}</dd></div>
        )}
        {digest.mechanism && <div><dt title={t('How it failed: the exception, command or git operation.')}>{t('How')}</dt><dd>{digest.mechanism}</dd></div>}
      </dl>
      {digest.reason && <p className="ask-work-row-reason">{digest.reason}</p>}

      {(digest.why ?? []).length > 0 && (
        <section aria-label={t('Why')}>
          <h4 className="ask-blackbox-heading">{t('Why')}</h4>
          <ul className="ask-blackbox-list">
            {(digest.why ?? []).map((finding) => (
              <li key={finding.ruleId + finding.statement}>
                <code>{finding.ruleId}</code> <span className="text-dim">({t(finding.confidence)})</span> {finding.statement}
              </li>
            ))}
          </ul>
        </section>
      )}

      {conflicting.length > 0 && (
        <section aria-label={t('Conflicting missions')}>
          <h4 className="ask-blackbox-heading">{t('Conflicting missions')}</h4>
          <ul className="ask-blackbox-list">
            {conflicting.map((m, index) => (
              <li key={(m.missionId ?? m.commitHash ?? '') + index}>
                {m.missionId
                  ? <Link to={`/missions/${encodeURIComponent(m.missionId)}`}>{m.title || m.missionId}</Link>
                  : <span>{m.title || (m.isOutsideArmada ? t('A change made outside Armada') : t('Unknown change'))}</span>}
                {m.commitHash && <> <code title={m.commitHash}>{shortCommit(m.commitHash)}</code></>}
                {(m.overlappingFileTotal ?? m.overlappingFiles?.length ?? 0) > 0 && (
                  <span className="text-dim"> {t('{{count}} overlapping files', { count: m.overlappingFileTotal ?? m.overlappingFiles?.length ?? 0 })}</span>
                )}
              </li>
            ))}
          </ul>
        </section>
      )}

      {files.length > 0 && (
        <section aria-label={t('Conflicted files')}>
          <h4 className="ask-blackbox-heading">{t('Conflicted files')}</h4>
          <ul className="ask-blackbox-list mono">
            {files.map((file) => <li key={file}>{file}</li>)}
          </ul>
          {(digest.conflictedFileTotal ?? 0) > files.length && (
            <p className="text-dim ask-work-more">{t('Showing {{shown}} of {{total}} rows.', { shown: files.length, total: digest.conflictedFileTotal ?? files.length })}</p>
          )}
        </section>
      )}

      {paths.length > 0 && (
        <section aria-label={t('Recovery paths')}>
          <h4 className="ask-blackbox-heading">{t('Recovery paths')}</h4>
          <ul className="ask-blackbox-list">
            {paths.map((path) => (
              <li key={path.id} className="ask-blackbox-action-row">
                <span>
                  <strong>{path.rank}.</strong> {path.title} <code className="text-dim">{path.id}</code>
                  {path.dataLossRisk && <span className="ask-blackbox-chip is-bad"> {t('Risks data loss')}</span>}
                </span>
                {actions && (
                  <button
                    type="button"
                    className="btn btn-sm"
                    disabled={actions.busy}
                    onClick={() => void actions.propose('run_blackbox_recovery', { entryId, pathId: path.id })}
                    title={t('Propose this recovery path. Nothing runs until you approve it; approving runs a dry run, then confirms it.')}
                  >
                    {t('Propose')}
                  </button>
                )}
              </li>
            ))}
          </ul>
        </section>
      )}

      {recommendations.length > 0 && (
        <section aria-label={t('Configuration recommendations')}>
          <h4 className="ask-blackbox-heading">{t('Configuration recommendations')}</h4>
          <ul className="ask-blackbox-list">
            {recommendations.map((rec) => (
              <li key={rec.id} className="ask-blackbox-action-row">
                <span>
                  <code>{rec.setting}</code>
                  {(rec.currentValue || rec.suggestedValue) && <> {rec.currentValue ?? '?'} &rarr; {rec.suggestedValue ?? '?'}</>}
                  {rec.rationale && <span className="text-dim"> {rec.rationale}</span>}
                </span>
                {actions && rec.canApply && (
                  <button
                    type="button"
                    className="btn btn-sm"
                    disabled={actions.busy}
                    onClick={() => void actions.propose('apply_blackbox_recommendation', { entryId, recommendationId: rec.id })}
                    title={t('Propose this configuration change. Nothing changes until you approve it.')}
                  >
                    {t('Propose')}
                  </button>
                )}
              </li>
            ))}
          </ul>
        </section>
      )}

      <div className="ask-blackbox-followups" role="group" aria-label={t('Follow-ups')}>
        {actions && followUps.map((followUp) => (
          <button
            key={followUp.name}
            type="button"
            className={`btn btn-sm${followUp.name === 'fix' ? ' btn-primary' : ''}`}
            disabled={actions.busy}
            onClick={() => run(followUp)}
            title={followUp.description ?? t(followUp.label)}
          >
            {t(followUp.label)}
          </button>
        ))}
        <button type="button" className="btn btn-sm" onClick={() => void copyBrief()} title={t('Copy the FIX THIS brief for an agent to the clipboard.')}>
          {copyState === 'copied' ? t('Copied') : t('Copy Markdown')}
        </button>
        {copyState === 'failed' && <span className="ask-work-failed-text" role="status">{t('Could not copy the brief.')}</span>}
      </div>
    </div>
  );
}
