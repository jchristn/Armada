import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { listVessels } from '../../api/client';
import type { BlackBoxEntry, BlackBoxHandoffTrail, BlackBoxResilience } from '../../types/blackbox';
import { useLocale } from '../../context/LocaleContext';
import { blackBoxAskHref, blackBoxAskState } from '../../lib/blackbox';
import {
  armadaFixAskPrompt,
  armadaFixDispatchState,
  findArmadaVessel,
  proposedChangeBrief,
  severityChipClass,
  topProposedChange,
} from '../../lib/blackboxResilience';
import CopyButton from '../shared/CopyButton';

const VESSEL_PAGE_SIZE = 200;

export interface BlackBoxResilienceSectionProps {
  entry: BlackBoxEntry;
  resilience: BlackBoxResilience | null | undefined;
  handoff?: BlackBoxHandoffTrail | null;
}

/**
 * The Armada resilience section of the black box panel, shown right after the Summary: whether Armada's handoff and
 * landing could have healed the failure by itself, the detection gap and invariant, severity with recurrence, the
 * recoveries Armada already attempted, code pointers at the build commit, the proposed Armada changes as mission briefs
 * and the regression test, with Draft Armada fix mission (Dispatch on the Armada vessel, or Ask Armada without one).
 * Outside the handoff and landing scope it is a short line.
 */
export default function BlackBoxResilienceSection({ entry, resilience, handoff }: BlackBoxResilienceSectionProps) {
  const { t, formatDateTime } = useLocale();
  const navigate = useNavigate();
  const [drafting, setDrafting] = useState(false);
  const title = t('Armada resilience');
  const tooltip = t('Could Armada\'s own handoff and landing have handled this failure, and what Armada should change so it cannot happen again.');

  if (!resilience) {
    return (
      <section className="blackbox-section blackbox-resilience" aria-label={title} data-testid="blackbox-resilience">
        <h4 title={tooltip}>{title}</h4>
        <p className="text-dim" style={{ margin: 0 }}>{t('No resilience analysis yet.')}</p>
      </section>
    );
  }

  const changes = [...(resilience.proposedChanges ?? [])].sort((a, b) => a.rank - b.rank);
  const top = topProposedChange(resilience);
  const build = [resilience.armadaVersion || entry.armadaVersion, (resilience.armadaCommit || entry.armadaCommit) ? `${t('commit')} ${resilience.armadaCommit || entry.armadaCommit}` : null].filter(Boolean).join(' ');

  async function draftFixMission() {
    setDrafting(true);
    try {
      let vesselId: string | null = null;
      if (top) {
        try {
          const result = await listVessels({ pageNumber: 1, pageSize: VESSEL_PAGE_SIZE });
          vesselId = findArmadaVessel(result.objects ?? [])?.id ?? null;
        } catch {
          vesselId = null;
        }
      }
      if (top && vesselId) {
        navigate('/dispatch', { state: armadaFixDispatchState(top, vesselId, entry) });
      } else {
        const prompt = armadaFixAskPrompt(entry.id, top);
        navigate(blackBoxAskHref(prompt), { state: blackBoxAskState(prompt) });
      }
    } finally {
      setDrafting(false);
    }
  }

  if (!resilience.inScope && changes.length === 0) {
    return (
      <section className="blackbox-section blackbox-resilience" aria-label={title} data-testid="blackbox-resilience" data-in-scope="false">
        <h4 title={tooltip}>{title}</h4>
        <p className="text-dim" style={{ margin: 0 }} data-testid="blackbox-resilience-none">{resilience.noChangeReason || t('No Armada change suggested: the failure is outside Armada\'s handoff and landing.')}</p>
      </section>
    );
  }

  const recoveries = handoff?.autoRecoveries ?? [];
  const steps = handoff?.steps ?? [];

  return (
    <section className="blackbox-section blackbox-resilience" aria-label={title} data-testid="blackbox-resilience" data-in-scope={resilience.inScope ? 'true' : 'false'}>
      <h4 title={tooltip}>{title}</h4>
      {resilience.inScope
        ? <p style={{ margin: '0 0 0.35rem' }}>{t('This failure is in Armada\'s own handoff and landing. Look for the Armada fix first, with a test that would have caught it.')}{resilience.scopeReason ? <span className="text-dim"> ({resilience.scopeReason})</span> : null}</p>
        : <p className="text-dim" style={{ margin: '0 0 0.35rem' }}>{t('Outside Armada\'s handoff and landing; the rules still suggest an Armada change.')}</p>}
      <div className="blackbox-chips" style={{ marginBottom: '0.35rem' }}>
        <span className={resilience.selfHealable === 'Yes' ? 'blackbox-chip blackbox-chip-warning' : 'blackbox-chip'} data-testid="blackbox-self-healable" title={t('Whether Armada could have recovered from this by itself.')}>{t('Self-healable: {{value}}', { value: t(resilience.selfHealable) })}</span>
        <span className={severityChipClass(resilience.severity)} title={t('Severity of the Armada change, escalated when the failure recurs or survives an upgrade.')}>{t('Severity: {{severity}}', { severity: t(resilience.severity) })}</span>
        {resilience.occurrences30Days != null && <span className="blackbox-chip">{t('{{count}} in the last 30 days', { count: resilience.occurrences30Days })}</span>}
        {(resilience.armadaVersionCount ?? 0) > 1 && <span className="blackbox-chip">{t('Seen in {{count}} Armada versions', { count: resilience.armadaVersionCount ?? 0 })}</span>}
      </div>
      <dl className="blackbox-kv">
        {resilience.selfHealingProposal && <><dt>{t('Armada could have')}</dt><dd>{resilience.selfHealingProposal}</dd></>}
        {resilience.detectionGap && <><dt>{t('Detection gap')}</dt><dd>{resilience.detectionGapKind && resilience.detectionGapKind !== 'Unknown' ? <span className="blackbox-chip" style={{ marginRight: '0.3rem' }}>{t(resilience.detectionGapKind)}</span> : null}{resilience.detectionGap}</dd></>}
        {resilience.invariant && <><dt>{t('Invariant that should hold')}</dt><dd>{resilience.invariant}</dd></>}
        {!!resilience.attemptedRecoveries?.length && <><dt>{t('Armada already attempted')}</dt><dd>{resilience.attemptedRecoveries.join('; ')}</dd></>}
        {!!resilience.codePointers?.length && <><dt>{t('Code pointers')}</dt><dd><span className="mono" style={{ fontSize: '0.8rem' }}>{resilience.codePointers.join(', ')}</span>{build ? <span className="text-dim"> ({t('Armada {{build}}', { build })})</span> : null}</dd></>}
        {resilience.regressionTest && <><dt>{t('Regression test')}</dt><dd><span className="mono">{resilience.regressionTest.suite}</span>: {resilience.regressionTest.description}</dd></>}
        {!!resilience.ruleIds?.length && <><dt>{t('Rules')}</dt><dd>{resilience.ruleIds.map((r) => <span key={r} className="blackbox-rule" style={{ marginRight: '0.3rem' }}>{r}</span>)}</dd></>}
      </dl>
      {changes.length > 0 && (
        <>
          <div className="text-dim" style={{ fontSize: '0.82rem', marginTop: '0.4rem' }}>{t('Proposed Armada changes (mission briefs, most impactful first)')}</div>
          <div className="blackbox-paths" data-testid="blackbox-proposed-changes">
            {changes.map((c) => (
              <div key={`${c.rank}-${c.title}`} className="blackbox-path">
                <div className="blackbox-path-body">
                  <div><strong>{c.rank}. {c.title}</strong> <span className={severityChipClass(c.severity)}>{t(c.severity)}</span>{c.ruleId ? <span className="blackbox-rule" style={{ marginLeft: '0.3rem' }}>{c.ruleId}</span> : null}</div>
                  <div>{c.change}</div>
                  {c.acceptanceTest && <div className="text-dim" style={{ fontSize: '0.82rem' }}>{t('Acceptance test')}: {c.acceptanceTest}</div>}
                </div>
                <CopyButton text={proposedChangeBrief(c, entry)} title="Copy this change as a mission brief." />
              </div>
            ))}
          </div>
        </>
      )}
      <div className="blackbox-toolbar" style={{ marginTop: '0.4rem' }}>
        <button type="button" className="btn btn-sm btn-primary" disabled={drafting} onClick={() => void draftFixMission()} data-testid="blackbox-draft-armada-fix" title={t('Open Dispatch with the top proposed Armada change as a mission on the Armada vessel (Ask Armada drafts it when there is no Armada vessel); nothing is dispatched until you press Dispatch.')}>{t('Draft Armada fix mission')}</button>
      </div>
      {(steps.length > 0 || recoveries.length > 0) && (
        <details style={{ marginTop: '0.4rem' }} data-testid="blackbox-handoff-trail">
          <summary>{t('Handoff and landing trail ({{count}} steps)', { count: handoff?.stepTotal ?? steps.length })}</summary>
          {recoveries.length > 0 && (
            <ul className="blackbox-list">
              {recoveries.map((r) => <li key={r.kind}>{t(r.kind)} x{r.attempts}: {t(r.outcome)}{r.detail ? <span className="text-dim"> ({r.detail})</span> : null}</li>)}
            </ul>
          )}
          {handoff?.targetMoved && <p className="text-dim" style={{ margin: '0.2rem 0' }}>{t('The target moved during the run{{detail}}.', { detail: handoff.targetTipAtStart && handoff.targetTipAtFailure ? ` (${handoff.targetTipAtStart.slice(0, 10)} -> ${handoff.targetTipAtFailure.slice(0, 10)})` : '' })}</p>}
          <ul className="blackbox-list" style={{ fontSize: '0.8rem' }}>
            {steps.map((s, i) => <li key={`${s.entityId ?? i}-${s.kind}`}><span className="text-dim">{formatDateTime(s.timestampUtc)}</span> <span className="mono">{s.kind}</span>{s.message ? ` · ${s.message}` : ''}</li>)}
          </ul>
        </details>
      )}
    </section>
  );
}
