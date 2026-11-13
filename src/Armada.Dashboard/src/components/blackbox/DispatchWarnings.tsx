import { Link } from 'react-router-dom';
import type { BriefLintFinding, MissionOverlapKind, MissionOverlapWarning } from '../../types/blackbox';
import { useLocale } from '../../context/LocaleContext';
import { useDispatchPreview, type DispatchPreviewInput } from '../../lib/dispatchPreview';
import LoadingIndicator from '../shared/LoadingIndicator';
import './BlackBox.css';

const FILE_PREVIEW = 5;

const KIND_LABEL: Record<MissionOverlapKind, string> = {
  ActiveMission: 'Active mission',
  Reservation: 'Reserved directory',
  Hotspot: 'Conflict hotspot',
  Unknown: 'Overlap',
};

const KIND_TOOLTIP: Record<MissionOverlapKind, string> = {
  ActiveMission: 'An active mission on this vessel is likely to touch the same files.',
  Reservation: 'An active mission reserves this directory because failures keep conflicting there.',
  Hotspot: 'The black box recorded repeated conflicts in this directory.',
  Unknown: 'Dispatch found an overlap with work on this vessel.',
};

export interface DispatchWarningListProps {
  overlapWarnings?: MissionOverlapWarning[] | null;
  briefLintWarnings?: BriefLintFinding[] | null;
  /** Total overlaps when the list was cut (the preview keeps the strongest 50). */
  overlapTotal?: number;
  /** The mission dispatch would chain after (preview with serializeOverlappingMissions). */
  suggestedDependsOnMissionId?: string | null;
  serializeOverlappingMissions?: boolean;
}

/** Overlap warnings and brief lint findings, as returned by dispatch or the preview. Renders nothing when both are empty. */
export function DispatchWarningList({ overlapWarnings, briefLintWarnings, overlapTotal, suggestedDependsOnMissionId, serializeOverlappingMissions }: DispatchWarningListProps) {
  const { t } = useLocale();
  const overlaps = overlapWarnings ?? [];
  const lint = briefLintWarnings ?? [];
  if (overlaps.length === 0 && lint.length === 0) return null;
  return (
    <div className="dispatch-warnings" data-testid="dispatch-warnings">
      {overlaps.length > 0 && (
        <div role="group" aria-label={t('Overlap warnings')}>
          <h5 title={t('Work on this vessel that this mission is likely to collide with. Dispatch still goes ahead.')}>
            {t('Overlap warnings')} <span className="text-dim">({overlapTotal && overlapTotal > overlaps.length ? t('{{shown}} of {{total}}', { shown: overlaps.length, total: overlapTotal }) : overlaps.length})</span>
          </h5>
          <ul className="blackbox-list">
            {overlaps.map((w, i) => (
              <li key={`${w.kind}|${w.overlappingMissionId ?? ''}|${w.directory ?? ''}|${i}`} data-overlap-kind={w.kind}>
                <span className="blackbox-chip blackbox-chip-warning" title={t(KIND_TOOLTIP[w.kind] ?? KIND_TOOLTIP.Unknown)}>{t(KIND_LABEL[w.kind] ?? KIND_LABEL.Unknown)}</span>{' '}
                {w.serialized && <><span className="blackbox-chip" title={t('Dispatch chained this mission after the overlapping one, so it starts when that one lands.')}>{t('Chained')}</span>{' '}</>}
                <span>{w.message}</span>
                {w.overlappingMissionId && (
                  <div style={{ fontSize: '0.82rem' }}>
                    <Link to={`/missions/${encodeURIComponent(w.overlappingMissionId)}`} title={t('Open the overlapping mission.')}>{w.overlappingMissionTitle || w.overlappingMissionId}</Link>
                    {w.overlappingMissionStatus && <span className="text-dim"> · {t(w.overlappingMissionStatus)}</span>}
                  </div>
                )}
                {w.directory && <div className="mono text-dim" style={{ fontSize: '0.8rem' }}>{w.directory}{w.hotspotEntryCount ? ` · ${t('{{count}} black box entries', { count: w.hotspotEntryCount })}` : ''}</div>}
                {!!w.files?.length && (
                  <div className="mono text-dim" style={{ fontSize: '0.8rem' }}>
                    {w.files.slice(0, FILE_PREVIEW).join(', ')}
                    {(w.fileCount ?? w.files.length) > FILE_PREVIEW && ` ${t('and {{count}} more', { count: (w.fileCount ?? w.files.length) - FILE_PREVIEW })}`}
                  </div>
                )}
              </li>
            ))}
          </ul>
          {suggestedDependsOnMissionId && (
            <p style={{ margin: '0.3rem 0 0', fontSize: '0.85rem' }}>
              {serializeOverlappingMissions
                ? t('This vessel serializes overlapping missions: dispatch will chain this mission after {{missionId}}.', { missionId: suggestedDependsOnMissionId })
                : t('Consider making this mission depend on {{missionId}}, or turn on Serialize Overlapping Missions for the vessel.', { missionId: suggestedDependsOnMissionId })}
            </p>
          )}
        </div>
      )}
      {lint.length > 0 && (
        <div role="group" aria-label={t('Brief warnings')}>
          <h5 title={t('Problems in the mission brief that tend to make missions fail. Dispatch never refuses a brief because of them.')}>{t('Brief warnings')} <span className="text-dim">({lint.length})</span></h5>
          <ul className="blackbox-list">
            {lint.map((f, i) => (
              <li key={`${f.ruleId}|${i}`} data-rule-id={f.ruleId}>
                <span className="blackbox-chip blackbox-chip-warning mono">{f.ruleId}</span> <span>{f.message}</span>
                {f.excerpt && <div className="mono text-dim" style={{ fontSize: '0.8rem' }}>{f.excerpt}</div>}
                {f.suggestion && <div style={{ fontSize: '0.82rem' }}>{t('Suggestion')}: {f.suggestion}</div>}
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}

export interface DispatchPreviewProps extends DispatchPreviewInput {
  /** Smaller heading for a per-mission preview. */
  compact?: boolean;
}

/**
 * Live preview of what dispatch would warn about, refreshed as the brief changes (debounced): the overlaps with the
 * vessel's active work and the brief lint findings. Nothing is dispatched.
 */
export default function DispatchPreview({ compact, ...input }: DispatchPreviewProps) {
  const { t } = useLocale();
  const state = useDispatchPreview(input);
  if (state.status === 'idle') return null;
  const preview = state.preview;
  const lint = state.lint?.findings ?? [];
  const overlaps = preview?.warnings ?? [];
  return (
    <section className={`dispatch-preview${compact ? ' dispatch-preview-compact' : ''}`} aria-label={t('Dispatch preview')} aria-live="polite" data-testid="dispatch-preview">
      <div className="dispatch-preview-head">
        <strong title={t('What dispatch would warn about for this brief, refreshed as you type. Nothing is dispatched.')}>{t('Dispatch preview')}</strong>
        {state.status === 'loading' && <LoadingIndicator label={t('Checking for overlaps...')} />}
      </div>
      {state.status === 'error' && <div className="text-dim" role="alert">{t('The preview is not available: {{message}}', { message: state.error })}</div>}
      {state.status === 'ready' && overlaps.length === 0 && lint.length === 0 && (
        <div className="text-dim">{input.vesselId ? t('No overlaps or brief warnings found.') : t('No brief warnings found. Choose a vessel to check for overlaps too.')}</div>
      )}
      {state.status === 'ready' && preview && preview.likelyFileCount > 0 && (
        <div className="text-dim" style={{ fontSize: '0.8rem' }}>{t('{{count}} likely files compared with {{missions}} active missions.', { count: preview.likelyFileCount, missions: preview.activeMissionsCompared })}</div>
      )}
      {state.status === 'ready' && (
        <DispatchWarningList
          overlapWarnings={overlaps}
          overlapTotal={preview?.warningCount}
          briefLintWarnings={lint}
          suggestedDependsOnMissionId={preview?.suggestedDependsOnMissionId}
          serializeOverlappingMissions={preview?.serializeOverlappingMissions}
        />
      )}
    </section>
  );
}

/** The warnings a create response carried, shown after dispatch (on the voyage page or the missions list). */
export function DispatchWarningsBanner({ overlapWarnings, briefLintWarnings, onDismiss }: { overlapWarnings?: MissionOverlapWarning[] | null; briefLintWarnings?: BriefLintFinding[] | null; onDismiss?: () => void }) {
  const { t } = useLocale();
  if (!overlapWarnings?.length && !briefLintWarnings?.length) return null;
  return (
    <section className="dispatch-preview dispatch-warnings-banner" role="status" aria-label={t('Dispatch warnings')} data-testid="dispatch-warnings-banner">
      <div className="dispatch-preview-head">
        <strong title={t('Dispatch went ahead; these are the overlaps and brief problems it found.')}>{t('Dispatch warnings')}</strong>
        {onDismiss && <button type="button" className="btn btn-sm" onClick={onDismiss} title={t('Hide these warnings.')}>{t('Dismiss')}</button>}
      </div>
      <DispatchWarningList overlapWarnings={overlapWarnings} briefLintWarnings={briefLintWarnings} />
    </section>
  );
}
