import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import {
  downloadBlackBox,
  getBlackBox,
  getBlackBoxBrief,
  getMissionBlackBox,
  getVoyageBlackBox,
  listMissionBlackBoxEntries,
} from '../../api/client';
import type { BlackBoxEntry, BlackBoxEntrySummary, BlackBoxRecord } from '../../types/blackbox';
import { useAuth } from '../../context/AuthContext';
import { useLocale } from '../../context/LocaleContext';
import { useNotifications } from '../../context/NotificationContext';
import {
  blackBoxAskHref,
  blackBoxAskState,
  blackBoxBriefFileName,
  blackBoxErrorCode,
  blackBoxEntryAskPrompt,
  blackBoxErrorStatus,
  isBlackBoxPending,
} from '../../lib/blackbox';
import { useBlackBoxLive } from '../../lib/useBlackBoxLive';
import { blackBoxHref } from '../../lib/navTargets';
import { copyToClipboard } from '../shared/CopyButton';
import CopyButton from '../shared/CopyButton';
import JsonViewer from '../shared/JsonViewer';
import LoadingIndicator from '../shared/LoadingIndicator';
import Markdown from '../shared/Markdown';
import BlackBoxActionDialog, { type BlackBoxActionTarget } from './BlackBoxActionDialog';
import BlackBoxAttemptDiff from './BlackBoxAttemptDiff';
import BlackBoxEvidence from './BlackBoxEvidence';
import BlackBoxNearbySuccesses from './BlackBoxNearbySuccesses';
import BlackBoxNotes from './BlackBoxNotes';
import BlackBoxResolveDialog from './BlackBoxResolveDialog';
import BlackBoxTimeline from './BlackBoxTimeline';
import { downloadBlackBoxMarkdown, reanalyzeBlackBoxEntry, toggleBlackBoxPin } from './blackBoxEntryActions';
import './BlackBox.css';

const ATTEMPT_PAGE_SIZE = 20;
const FILE_DISPLAY_LIMIT = 50;

export interface BlackBoxPanelProps {
  /** The mission whose current entry to show. */
  missionId?: string;
  /** The voyage whose current entry to show. */
  voyageId?: string;
  /** A specific entry (the full-page view). */
  entryId?: string;
  /** Failure reason recorded on the mission or voyage, shown when it has no black box entry. */
  fallbackReason?: string | null;
  /** Failure kind of the mission (data-failure-kind on the fallback reason). */
  fallbackKind?: string | null;
  /** Extra actions shown in the toolbar (for example Retry Landing on the mission page). */
  extraActions?: ReactNode;
  /** `page` (the /blackbox/:id view) opens Evidence and Notes and hides the Open full page link. */
  variant?: 'embedded' | 'page';
  /** Called after a recovery run or resolution, so the host reloads its own record. */
  onChanged?: () => void;
}

type LoadState = 'loading' | 'ready' | 'notFound' | 'deleting' | 'error';

function Section({ title, tooltip, children, testId }: { title: string; tooltip?: string; children: ReactNode; testId?: string }) {
  return (
    <section className="blackbox-section" aria-label={title} data-testid={testId}>
      <h4 title={tooltip}>{title}</h4>
      {children}
    </section>
  );
}

function Lazy({ title, children, defaultOpen }: { title: string; children: () => ReactNode; defaultOpen?: boolean }) {
  const [open, setOpen] = useState(!!defaultOpen);
  return (
    <details className="blackbox-section" open={open} onToggle={(e) => setOpen((e.target as HTMLDetailsElement).open)} aria-label={title}>
      <summary>{title}</summary>
      {open && children()}
    </details>
  );
}

/**
 * The black box panel (MISSION_BLACKBOX_PLAN.md 11.1): one failure's record rendered as the brief's sections (Summary,
 * Where, How, Why, Conflict, Configuration, What would have prevented it, What Armada could change, Recovery,
 * Attempts, Evidence, Notes), with Copy FIX THIS, the .md and .zip downloads, the JSON record,
 * Ask Armada, Resolve, Pin and Reanalyze. Recovery paths dry run first and confirm in BlackBoxActionDialog. Follows
 * the blackbox.* WebSocket events for its subject, so a pending entry fills in when its analysis lands.
 */
export default function BlackBoxPanel({ missionId, voyageId, entryId, fallbackReason, fallbackKind, extraActions, variant = 'embedded', onChanged }: BlackBoxPanelProps) {
  const { t, formatDateTime } = useLocale();
  const { isTenantAdmin } = useAuth();
  const { pushToast } = useNotifications();
  const navigate = useNavigate();

  const [state, setState] = useState<LoadState>('loading');
  const [error, setError] = useState('');
  const [entry, setEntry] = useState<BlackBoxEntry | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(entryId ?? null);
  const [attempts, setAttempts] = useState<BlackBoxEntrySummary[]>([]);
  const [brief, setBrief] = useState<{ key: string; text: string } | null>(null);
  const [briefError, setBriefError] = useState('');
  const [showJson, setShowJson] = useState(false);
  const [showResolve, setShowResolve] = useState(false);
  const [action, setAction] = useState<BlackBoxActionTarget | null>(null);
  const [busy, setBusy] = useState('');
  const [notesKey, setNotesKey] = useState(0);
  const loadedOnce = useRef(false);

  const load = useCallback(async (explicitId?: string | null) => {
    if (!loadedOnce.current) setState('loading');
    try {
      const id = explicitId ?? selectedId;
      let next: BlackBoxEntry;
      if (id) next = await getBlackBox(id);
      else if (missionId) next = await getMissionBlackBox(missionId);
      else if (voyageId) next = await getVoyageBlackBox(voyageId);
      else { setState('notFound'); return; }
      setEntry(next);
      setState('ready');
      setError('');
      loadedOnce.current = true;
    } catch (err: unknown) {
      const code = blackBoxErrorCode(err);
      if (code === 'BlackBoxNotFound' || (blackBoxErrorStatus(err) === 404)) {
        setEntry(null);
        setState('notFound');
      } else if (code === 'BlackBoxDeleting' || (blackBoxErrorStatus(err) === 409)) {
        setEntry(null);
        setState('deleting');
      } else {
        setError(err instanceof Error ? err.message : String(err));
        setState('error');
      }
    }
  }, [missionId, voyageId, selectedId]);

  useEffect(() => { void load(); }, [load]);

  // Attempts of the mission (every failed attempt has its own entry).
  const attemptMissionId = entry?.missionId ?? missionId ?? null;
  const loadAttempts = useCallback(async () => {
    if (!attemptMissionId) { setAttempts([]); return; }
    try {
      const result = await listMissionBlackBoxEntries(attemptMissionId, { pageNumber: 1, pageSize: ATTEMPT_PAGE_SIZE });
      const rows = [...(result.objects ?? [])].sort((a, b) => a.attempt - b.attempt);
      setAttempts(rows);
    } catch {
      setAttempts([]);
    }
  }, [attemptMissionId]);
  useEffect(() => { void loadAttempts(); }, [loadAttempts]);

  useBlackBoxLive([entry?.id, missionId, voyageId], () => {
    void load();
    void loadAttempts();
    setBrief(null);
  });

  const briefKey = entry ? `${entry.id}|${entry.lastUpdateUtc ?? ''}` : '';
  const fetchBrief = useCallback(async (): Promise<string> => {
    if (!entry) return '';
    if (brief && brief.key === briefKey) return brief.text;
    const text = await getBlackBoxBrief(entry.id);
    setBrief({ key: briefKey, text });
    setBriefError('');
    return text;
  }, [entry, brief, briefKey]);

  useEffect(() => {
    if (!entry || state !== 'ready') return;
    let cancelled = false;
    getBlackBoxBrief(entry.id)
      .then((text) => { if (!cancelled) { setBrief({ key: briefKey, text }); setBriefError(''); } })
      .catch((err: unknown) => { if (!cancelled) setBriefError(err instanceof Error ? err.message : String(err)); });
    return () => { cancelled = true; };
    // briefKey covers the entry id and its last update.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [briefKey, state]);

  async function copyFixThis() {
    try {
      const text = await fetchBrief();
      await copyToClipboard(text);
      pushToast('success', t('FIX THIS brief copied. Paste it into your coding agent.'));
    } catch (err: unknown) {
      pushToast('error', t('The brief could not be copied: {{message}}', { message: err instanceof Error ? err.message : String(err) }));
    }
  }

  async function downloadMarkdown() {
    if (!entry) return;
    await downloadBlackBoxMarkdown(blackBoxBriefFileName(entry.id), fetchBrief, t, pushToast);
  }

  async function downloadZip() {
    if (!entry) return;
    try {
      setBusy('zip');
      await downloadBlackBox(entry.id);
    } catch (err: unknown) {
      pushToast('error', t('The download failed: {{message}}', { message: err instanceof Error ? err.message : String(err) }));
    } finally {
      setBusy('');
    }
  }

  async function togglePin() {
    if (!entry) return;
    setBusy('pin');
    const updated = await toggleBlackBoxPin(entry, t, pushToast);
    if (updated) setEntry({ ...entry, ...updated, record: updated.record ?? entry.record });
    setBusy('');
  }

  async function reanalyze() {
    if (!entry) return;
    setBusy('reanalyze');
    const updated = await reanalyzeBlackBoxEntry(entry.id, t('Reanalysis started. The panel updates when it finishes.'), t, pushToast);
    if (updated) {
      setEntry({ ...entry, ...updated, record: updated.record ?? entry.record });
      setBrief(null);
    }
    setBusy('');
  }

  function askArmada() {
    const prompt = blackBoxEntryAskPrompt({ id: entry?.id ?? '', missionId: entry?.missionId ?? missionId, voyageId: entry?.voyageId ?? voyageId });
    navigate(blackBoxAskHref(prompt), { state: blackBoxAskState(prompt) });
  }

  const record: BlackBoxRecord | null = entry?.record ?? null;
  const analysis = record?.analysis ?? null;
  const recoveryPaths = useMemo(() => [...(analysis?.recoveryPaths ?? [])].sort((a, b) => a.rank - b.rank), [analysis]);

  if (state === 'loading') return <div className="blackbox-panel" aria-busy="true"><LoadingIndicator label={t('Loading the black box...')} /></div>;

  if (state === 'error') {
    return (
      <div className="blackbox-panel" role="alert">
        <div className="blackbox-notice blackbox-notice-error">{t('The black box could not be loaded: {{message}}', { message: error })}</div>
        <div><button type="button" className="btn btn-sm" onClick={() => void load()}>{t('Retry')}</button></div>
        {fallbackReason && <pre className="blackbox-pre mission-failure-reason" data-failure-kind={fallbackKind ?? undefined}>{fallbackReason}</pre>}
        {extraActions && <div className="blackbox-toolbar">{extraActions}</div>}
      </div>
    );
  }

  if (state === 'notFound' || state === 'deleting') {
    if (!fallbackReason && variant === 'embedded' && state === 'notFound' && !extraActions) return null;
    return (
      <div className="blackbox-panel" data-state={state}>
        <h3 style={{ margin: 0, fontSize: '1rem' }}>{t('Black box')}</h3>
        <div className="blackbox-notice" role="status">
          {state === 'deleting'
            ? t('This black box entry is being deleted.')
            : variant === 'page'
              ? t('Black box entry not found. It may have expired, been deleted, or belong to another tenant.')
              : t('No black box entry was recorded for this failure. The recorded reason is below.')}
        </div>
        {fallbackReason && <pre className="blackbox-pre mission-failure-reason" data-failure-kind={fallbackKind ?? undefined}>{fallbackReason}</pre>}
        {extraActions && <div className="blackbox-toolbar">{extraActions}</div>}
      </div>
    );
  }

  if (!entry) return null;

  const pending = isBlackBoxPending(entry);
  const resolved = entry.resolution !== 'Open' && entry.resolution !== 'Unknown';
  const site = record?.site;
  const mechanism = record?.mechanism;
  const classification = record?.classification;
  const hasSuspectedCause = !!classification?.suspectedCause && classification.suspectedCause !== 'Unknown' && classification.suspectedCause !== 'None';
  const hasMechanismFields = !!(mechanism?.exceptionType || mechanism?.failingCommand || mechanism?.gitOperation || mechanism?.runtimeFailureKind)
    || mechanism?.exitCode != null || hasSuspectedCause;
  const hasMechanism = hasMechanismFields || !!classification?.rawReason || !!mechanism?.gitStderrTail || !!mechanism?.commandOutputTail
    || !!mechanism?.stackFrames?.length;
  const workspace = record?.workspace;
  const conflict = record?.conflict;
  const configValues = record?.configuration?.values ?? [];
  const findings = analysis?.why ?? [];
  const recommendations = analysis?.configRecommendations ?? [];
  const improvements = analysis?.armadaImprovements ?? [];
  const related = record?.related;
  const summaryText = analysis?.summary || entry.summary || entry.reasonExcerpt || classification?.rawReason || '';
  const triage = analysis?.triage;
  const conflictedFiles = conflict?.conflictedFiles ?? [];
  const conflictedTotal = conflict?.conflictedFileTotal ?? conflictedFiles.length;

  return (
    <div className={`blackbox-panel${resolved ? ' blackbox-panel-resolved' : ''}${pending ? ' blackbox-panel-pending' : ''}`} data-entry-id={entry.id} data-state={entry.state}>
      <div className="blackbox-panel-head">
        <div style={{ minWidth: 0 }}>
          <h3 title={t('Everything Armada recorded about this failure: where it happened, how, why, and how to recover.')}>
            {t('Black box')} <span className="mono text-dim" style={{ fontSize: '0.8rem' }}>{entry.id}</span> <CopyButton text={entry.id} />
          </h3>
          <div className="blackbox-chips">
            {triage && triage.verdict !== 'Unknown' && <span className="blackbox-chip blackbox-chip-warning" title={t('Triage verdict: whose fix this most likely is, and how confident the rules are.')}>{t(triage.verdict)} ({t(triage.confidence)})</span>}
            <span className={`blackbox-chip${pending ? ' blackbox-chip-warning' : entry.state === 'Analyzed' ? ' blackbox-chip-ok' : entry.state === 'EnrichmentFailed' || entry.state === 'ObjectMissing' ? ' blackbox-chip-danger' : ''}`} title={t('Capture state: Captured, then Enriched with git and log forensics, then Analyzed.')}>{t(entry.state)}</span>
            <span className={`blackbox-chip${resolved ? ' blackbox-chip-ok' : ''}`} title={t('Resolution of this entry.')}>{t(entry.resolution)}</span>
            {entry.failureKind && <span className="blackbox-chip">{t(entry.failureKind)}</span>}
            <span className="blackbox-chip">{t('Attempt {{attempt}}', { attempt: entry.attempt })}</span>
            {entry.pinned && <span className="blackbox-chip" title={t('Pinned entries are never garbage collected.')}>{t('Pinned')}</span>}
            {!entry.isCurrent && <span className="blackbox-chip" title={t('A later attempt has its own entry.')}>{t('Superseded')}</span>}
          </div>
        </div>
      </div>

      <div className="blackbox-toolbar">
        <button type="button" className="btn btn-sm btn-primary" onClick={() => void copyFixThis()} title={t('Copy the FIX THIS brief, ready to paste into a coding agent.')}>{t('Copy FIX THIS')}</button>
        <button type="button" className="btn btn-sm" onClick={() => void downloadMarkdown()} title={t('Save the FIX THIS brief as a Markdown file.')}>{t('Download .md')}</button>
        <button type="button" className="btn btn-sm" disabled={busy === 'zip'} onClick={() => void downloadZip()} title={t('Download every object of this entry and its manifest as a .zip.')}>{t('Download .zip')}</button>
        <button type="button" className="btn btn-sm" onClick={() => setShowJson(true)} title={t('Show the entry and its versioned record as JSON.')}>{t('View JSON')}</button>
        <button type="button" className="btn btn-sm" onClick={askArmada} title={t('Open Ask Armada with a prompt about this failure; nothing is sent until you press Send.')}>{t('Ask Armada about this failure')}</button>
        <button type="button" className="btn btn-sm" onClick={() => setShowResolve(true)} title={t('Set the resolution of this entry, with a note and a link.')}>{t('Resolve')}</button>
        <button type="button" className="btn btn-sm" disabled={busy === 'pin'} onClick={() => void togglePin()} title={entry.pinned ? t('Let garbage collection expire this entry again.') : t('Keep this entry past its retention.')}>{entry.pinned ? t('Unpin') : t('Pin')}</button>
        {isTenantAdmin && <button type="button" className="btn btn-sm" disabled={busy === 'reanalyze'} onClick={() => void reanalyze()} title={t('Run enrichment and analysis again with the current rules.')}>{t('Reanalyze')}</button>}
        {variant === 'embedded' && <Link className="btn btn-sm" to={`/blackbox/${encodeURIComponent(entry.id)}`} title={t('Open this entry on its own page.')}>{t('Open full page')}</Link>}
        {extraActions}
      </div>

      {pending && (
        <div className="blackbox-notice" role="status" data-testid="blackbox-pending">
          {t('Analysis is still running: the summary, findings and recovery paths appear when it finishes. This panel updates by itself.')}
        </div>
      )}
      {entry.state === 'EnrichmentFailed' && (
        <div className="blackbox-notice blackbox-notice-error" role="status">{t('Enrichment failed and will be retried: {{message}}', { message: entry.lastError || t('no message') })}</div>
      )}
      {entry.state === 'ObjectMissing' && (
        <div className="blackbox-notice blackbox-notice-error" role="status">{t('Some objects of this entry are missing from the store (ObjectMissing). The record below is still complete.')}</div>
      )}
      {entry.captureMode === 'Sweeper' && (
        <div className="blackbox-notice blackbox-notice-info" role="status" data-testid="blackbox-sweeper">
          {t('Captured by the sweeper after the failure, not at the failure site: volatile facts such as the exact exception, command output and checkout state may be missing.')}
        </div>
      )}
      {entry.captureMode === 'Backfill' && (
        <div className="blackbox-notice blackbox-notice-info" role="status">{t('Backfilled from stored records: volatile facts from the failure site are missing.')}</div>
      )}
      {briefError && <div className="blackbox-notice blackbox-notice-error" role="status">{t('The brief could not be loaded: {{message}}', { message: briefError })}</div>}

      <Section title={t('Summary')} testId="blackbox-summary">
        {summaryText ? <p style={{ margin: 0 }}>{summaryText}</p> : <p className="text-dim" style={{ margin: 0 }}>{t('No summary yet.')}</p>}
        <dl className="blackbox-kv" style={{ marginTop: '0.4rem' }}>
          <dt>{t('Status')}</dt><dd>{t(record?.status?.before ?? entry.statusBefore ?? '-')} &rarr; {t(record?.status?.after ?? entry.statusAfter ?? '-')}</dd>
          <dt>{t('Captured')}</dt><dd>{formatDateTime(entry.createdUtc)} ({t(entry.captureMode)})</dd>
          {entry.missionId && <><dt>{t('Mission')}</dt><dd><Link to={`/missions/${entry.missionId}`}>{entry.missionTitle || entry.missionId}</Link></dd></>}
          {entry.voyageId && <><dt>{t('Voyage')}</dt><dd><Link to={`/voyages/${entry.voyageId}`} className="mono">{entry.voyageId}</Link></dd></>}
          {resolved && entry.resolutionNote && <><dt>{t('Resolution note')}</dt><dd>{entry.resolutionNote}</dd></>}
          {resolved && entry.resolutionLink && <><dt>{t('Resolution link')}</dt><dd className="mono">{entry.resolutionLink}</dd></>}
        </dl>
      </Section>

      <Section title={t('Where')} tooltip={t('The phase and step that failed, the host and the code site.')} testId="blackbox-where">
        <dl className="blackbox-kv">
          <dt>{t('Phase / step')}</dt><dd>{t(site?.phase ?? entry.phase)} / {t(site?.step ?? entry.step)}</dd>
          {site?.host && <><dt>{t('Host')}</dt><dd>{site.host}</dd></>}
          {(site?.component || site?.method) && <><dt>{t('Component')}</dt><dd className="mono">{[site?.component, site?.method].filter(Boolean).join('.')}</dd></>}
          {site?.sourceFile && <><dt>{t('Source')}</dt><dd className="mono">{site.sourceFile}{site.sourceLine ? `:${site.sourceLine}` : ''}</dd></>}
          {(site?.armadaVersion || entry.armadaVersion) && <><dt>{t('Armada version')}</dt><dd className="mono">{site?.armadaVersion || entry.armadaVersion}{(site?.armadaCommit || entry.armadaCommit) ? ` (${site?.armadaCommit || entry.armadaCommit})` : ''}</dd></>}
          {entry.vesselId && <><dt>{t('Vessel')}</dt><dd><Link to={`/vessels/${entry.vesselId}`}>{entry.vesselName || entry.vesselId}</Link></dd></>}
          {record?.subject?.captainId && <><dt>{t('Captain')}</dt><dd><Link to={`/captains/${record.subject.captainId}`}>{record.subject.captainName || record.subject.captainId}</Link>{record.subject.captainModel ? <span className="text-dim"> ({[record.subject.captainRuntime, record.subject.captainModel].filter(Boolean).join(', ')})</span> : null}</dd></>}
          {entry.dockId && <><dt>{t('Dock')}</dt><dd><Link to={`/docks/${entry.dockId}`} className="mono">{entry.dockId}</Link>{workspace?.dockState ? <span className="text-dim"> ({t(workspace.dockState)})</span> : null}</dd></>}
          {entry.mergeEntryId && <><dt>{t('Merge entry')}</dt><dd><Link to={`/merge-queue/${entry.mergeEntryId}`} className="mono">{entry.mergeEntryId}</Link></dd></>}
          {entry.incidentId && <><dt>{t('Incident')}</dt><dd><Link to={`/incidents/${entry.incidentId}`} className="mono">{entry.incidentId}</Link></dd></>}
          {workspace?.branchName && <><dt>{t('Branch')}</dt><dd className="mono">{workspace.branchName}{workspace.targetBranch ? ` → ${workspace.targetBranch}` : ''}</dd></>}
          {workspace?.commitsBehindTarget != null && <><dt>{t('Commits behind target')}</dt><dd>{workspace.commitsBehindTarget}</dd></>}
          {workspace?.checkoutDirty && <><dt>{t('Dirty checkout')}</dt><dd>{t('{{count}} changed paths', { count: workspace.dirtyPathTotal ?? workspace.dirtyPaths?.length ?? 0 })}{workspace.dirtyPaths?.length ? `: ${workspace.dirtyPaths.slice(0, 10).join(', ')}` : ''}</dd></>}
        </dl>
      </Section>

      <Section title={t('How')} tooltip={t('The mechanism: the exception, the failing command or git operation, and the recorded reason.')} testId="blackbox-how">
        {!hasMechanism && <p className="text-dim" style={{ margin: 0 }}>{t('No mechanism was recorded for this failure.')}</p>}
        {hasMechanismFields && <dl className="blackbox-kv">
          {mechanism?.exceptionType && <><dt>{t('Exception')}</dt><dd className="mono">{mechanism.exceptionType}{mechanism.exceptionMessage ? `: ${mechanism.exceptionMessage}` : ''}</dd></>}
          {mechanism?.failingCommand && <><dt>{t('Failing command')}</dt><dd className="mono">{mechanism.failingCommand}{mechanism.commandExitCode != null ? ` (${t('exit {{code}}', { code: mechanism.commandExitCode })})` : ''}</dd></>}
          {mechanism?.gitOperation && <><dt>{t('Git operation')}</dt><dd className="mono">{mechanism.gitOperation}</dd></>}
          {mechanism?.runtimeFailureKind && <><dt>{t('Runtime failure')}</dt><dd>{mechanism.runtimeFailureKind}</dd></>}
          {mechanism?.exitCode != null && <><dt>{t('Exit code')}</dt><dd>{mechanism.exitCode}</dd></>}
          {hasSuspectedCause && <><dt>{t('Suspected cause')}</dt><dd>{t(classification!.suspectedCause!)}{classification!.causeRuleId ? <span className="blackbox-rule"> {classification!.causeRuleId}</span> : null}</dd></>}
        </dl>}
        {classification?.rawReason && <pre className="blackbox-pre mission-failure-reason" data-failure-kind={entry.failureKind ?? fallbackKind ?? undefined} data-i18n-skip="true">{classification.rawReason}</pre>}
        {mechanism?.gitStderrTail && <pre className="blackbox-pre" data-i18n-skip="true" aria-label={t('Git output')}>{mechanism.gitStderrTail}</pre>}
        {mechanism?.commandOutputTail && <pre className="blackbox-pre" data-i18n-skip="true" aria-label={t('Command output')}>{mechanism.commandOutputTail}</pre>}
        {!!mechanism?.stackFrames?.length && <pre className="blackbox-pre" data-i18n-skip="true" aria-label={t('Stack')}>{mechanism.stackFrames.join('\n')}</pre>}
      </Section>

      <Section title={t('Why')} tooltip={t('Findings of the rule-based analysis, with rule ids and confidence.')} testId="blackbox-why">
        {findings.length === 0 ? <p className="text-dim" style={{ margin: 0 }}>{pending ? t('Waiting for the analysis.') : t('No findings.')}</p> : (
          <ul className="blackbox-list">
            {findings.map((f) => (
              <li key={`${f.ruleId}-${f.statement}`}>{f.statement} <span className="blackbox-chip">{t(f.confidence)}</span> <span className="blackbox-rule">{f.ruleId}</span></li>
            ))}
          </ul>
        )}
      </Section>

      {(conflict?.isConflict || conflictedFiles.length > 0 || (conflict?.conflictingMissions?.length ?? 0) > 0) && (
        <Section title={t('Conflict')} tooltip={t('The files that conflicted, the directories they roll up to, and the missions that changed them.')} testId="blackbox-conflict">
          {(conflict?.conflictingMissions?.length ?? 0) > 0 && (
            <>
              <div className="text-dim" style={{ fontSize: '0.82rem' }}>{t('Conflicting missions')}</div>
              <ul className="blackbox-list">
                {conflict!.conflictingMissions!.map((m, i) => (
                  <li key={`${m.missionId ?? m.commitHash ?? i}`}>
                    {m.missionId ? <Link to={`/missions/${m.missionId}`}>{m.title || m.missionId}</Link> : <span>{m.isOutsideArmada ? t('Outside Armada') : '-'}{m.author ? ` (${m.author})` : ''}</span>}
                    {m.commitHash && <span className="mono text-dim"> {m.commitHash.slice(0, 12)}</span>}
                    {m.landedUtc && <span className="text-dim"> · {formatDateTime(m.landedUtc)}</span>}
                    {!!m.overlappingFiles?.length && <div className="mono text-dim" style={{ fontSize: '0.78rem' }}>{m.overlappingFiles.join(', ')}{(m.overlappingFileTotal ?? 0) > m.overlappingFiles.length ? ` ${t('and {{count}} more', { count: (m.overlappingFileTotal ?? 0) - m.overlappingFiles.length })}` : ''}</div>}
                  </li>
                ))}
              </ul>
            </>
          )}
          {conflictedFiles.length > 0 && (
            <>
              <div className="text-dim" style={{ fontSize: '0.82rem', marginTop: '0.4rem' }}>{t('Conflicted files ({{count}})', { count: conflictedTotal })}</div>
              <ul className="blackbox-list mono" style={{ fontSize: '0.8rem' }}>
                {conflictedFiles.slice(0, FILE_DISPLAY_LIMIT).map((f) => <li key={f}>{f}</li>)}
              </ul>
              {conflictedTotal > Math.min(conflictedFiles.length, FILE_DISPLAY_LIMIT) && <p className="text-dim" style={{ margin: '0.2rem 0 0' }}>{t('and {{count}} more', { count: conflictedTotal - Math.min(conflictedFiles.length, FILE_DISPLAY_LIMIT) })}</p>}
            </>
          )}
          {!!conflict?.conflictedDirectories?.length && (
            <div className="table-wrap" style={{ marginTop: '0.4rem' }}>
              <table className="table-dense" aria-label={t('Directories')}>
                <thead><tr><th>{t('Directory')}</th><th>{t('Files')}</th></tr></thead>
                <tbody>{conflict.conflictedDirectories.map((d) => <tr key={d.directory}><td className="mono">{d.directory}</td><td>{d.fileCount}</td></tr>)}</tbody>
              </table>
            </div>
          )}
        </Section>
      )}

      {configValues.length > 0 && (
        <Section title={t('Configuration')} tooltip={t('Effective settings that shaped this failure, and where each value came from.')} testId="blackbox-configuration">
          <div className="table-wrap">
            <table className="table-dense" aria-label={t('Configuration')}>
              <thead><tr><th>{t('Setting')}</th><th>{t('Value')}</th><th>{t('Source')}</th></tr></thead>
              <tbody>
                {configValues.map((c) => (
                  <tr key={c.setting}><td className="mono">{c.setting}</td><td className="mono">{c.value ?? '-'}</td><td>{t(c.source ?? 'Default')}</td></tr>
                ))}
              </tbody>
            </table>
          </div>
        </Section>
      )}

      <Section title={t('What would have prevented it')} tooltip={t('Configuration changes the analysis recommends. Apply runs a dry run first.')} testId="blackbox-prevent">
        {recommendations.length === 0 ? <p className="text-dim" style={{ margin: 0 }}>{t('No configuration recommendations.')}</p> : (
          <div className="blackbox-paths">
            {recommendations.map((r) => (
              <div key={r.id} className="blackbox-path">
                <div className="blackbox-path-body">
                  <div><strong className="mono">{r.setting}</strong>{r.scope ? <span className="text-dim"> ({t(r.scope)})</span> : null}: <span className="mono">{r.currentValue ?? '-'}</span> &rarr; <span className="mono">{r.suggestedValue ?? '-'}</span></div>
                  {r.rationale && <div className="text-dim">{r.rationale}</div>}
                  {r.howToApply && <div style={{ fontSize: '0.82rem' }}>{r.howToApply}</div>}
                  {r.ruleId && <span className="blackbox-rule">{r.ruleId}</span>}
                </div>
                {r.applyAction && <button type="button" className="btn btn-sm" onClick={() => setAction({ kind: 'recommendation', id: r.id, title: r.setting })} title={t('Dry run this change, then confirm to apply it.')}>{t('Apply...')}</button>}
              </div>
            ))}
          </div>
        )}
      </Section>

      <Section title={t('What Armada could change')} tooltip={t('Hypotheses about Armada behavior that made this failure likely, for maintainers.')} testId="blackbox-improvements">
        {improvements.length === 0 ? <p className="text-dim" style={{ margin: 0 }}>{t('No Armada improvements suggested.')}</p> : (
          <ul className="blackbox-list">
            {improvements.map((imp, i) => (
              <li key={`${imp.ruleId ?? ''}-${i}`}>
                {imp.area && <strong>{imp.area}: </strong>}{imp.hypothesis}
                {imp.severity && imp.severity !== 'Unknown' && <span className="blackbox-chip" style={{ marginLeft: '0.3rem' }}>{t(imp.severity)}</span>}
                {imp.knownIssue && <span className="text-dim"> ({imp.knownIssue})</span>}
                {imp.evidence && <div className="text-dim" style={{ fontSize: '0.82rem' }}>{imp.evidence}</div>}
                {!!imp.sourcePointers?.length && <div className="mono text-dim" style={{ fontSize: '0.78rem' }}>{imp.sourcePointers.join(', ')}</div>}
              </li>
            ))}
          </ul>
        )}
      </Section>

      <Section title={t('Recovery')} tooltip={t('Ranked recovery paths recorded by the analysis. Each one dry runs first and runs only after you confirm.')} testId="blackbox-recovery">
        {recoveryPaths.length === 0 ? <p className="text-dim" style={{ margin: 0 }}>{pending ? t('Waiting for the analysis.') : t('No recovery paths were recorded.')}</p> : (
          <div className="blackbox-paths">
            {recoveryPaths.map((p) => (
              <div key={p.id} className="blackbox-path" data-path-id={p.id}>
                <div className="blackbox-path-body">
                  <div><strong>{p.rank}. {p.title}</strong>
                    {p.risk && p.risk !== 'Unknown' && <span className="blackbox-chip" style={{ marginLeft: '0.35rem' }}>{t('Risk: {{risk}}', { risk: t(p.risk) })}</span>}
                    {p.dataLossRisk && <span className="blackbox-chip blackbox-chip-danger" style={{ marginLeft: '0.35rem' }}>{t('Data loss risk')}</span>}
                  </div>
                  {p.expectedOutcome && <div className="text-dim">{p.expectedOutcome}</div>}
                  {!!p.preconditions?.length && <div style={{ fontSize: '0.82rem' }}>{t('Preconditions')}: {p.preconditions.join('; ')}</div>}
                  {!!p.steps?.length && <div className="text-dim" style={{ fontSize: '0.82rem' }}>{t('{{count}} steps', { count: p.steps.length })}</div>}
                  {p.runbookId && <div style={{ fontSize: '0.82rem' }}>{t('Runs runbook')}: <Link to={`/runbooks/${encodeURIComponent(p.runbookId)}`} title={t('This path starts the linked runbook instead of its own steps. Open the runbook.')}>{p.runbookTitle || p.runbookId}</Link></div>}
                  {p.successRate != null && <div className="text-dim" style={{ fontSize: '0.82rem' }} title={t('Share of settled confirmed runs of this path that recovered the mission.')}>{t('Success rate {{rate}}% over {{runs}} tracked runs', { rate: Math.round(p.successRate * 100), runs: p.trackedRuns ?? 0 })}</div>}
                </div>
                <button type="button" className="btn btn-sm btn-primary" onClick={() => setAction({ kind: 'recovery', id: p.id, title: p.title, dataLossRisk: !!p.dataLossRisk })} title={t('Show what this path would do (dry run); nothing runs until you confirm.')}>{t('Dry run...')}</button>
              </div>
            ))}
          </div>
        )}
      </Section>

      <Section title={t('Attempts')} tooltip={t('Each failed attempt of the mission has its own entry. Pick one to show it.')} testId="blackbox-attempts">
        {attempts.length > 1 ? (
          <div className="blackbox-attempts" role="group" aria-label={t('Attempts')}>
            {attempts.map((a) => (
              <button key={a.id} type="button" className="btn btn-sm" aria-pressed={a.id === entry.id} onClick={() => { setSelectedId(a.id); setBrief(null); }} title={a.reasonExcerpt || a.step}>
                {t('Attempt {{attempt}}', { attempt: a.attempt })}
              </button>
            ))}
          </div>
        ) : <p className="text-dim" style={{ margin: 0 }}>{t('This is the only recorded attempt.')}</p>}
        {!!record?.attempts?.length && (
          <ul className="blackbox-list" style={{ marginTop: '0.4rem' }}>
            {record.attempts.map((a, i) => (
              <li key={`${a.entryId ?? i}`}>{t('Attempt {{attempt}}', { attempt: a.attempt ?? i + 1 })}: {a.kind ? t(a.kind) : '-'}{a.reasonExcerpt ? ` · ${a.reasonExcerpt}` : ''}{a.model ? <span className="text-dim"> ({[a.runtime, a.model].filter(Boolean).join(', ')})</span> : null}</li>
            ))}
          </ul>
        )}
      </Section>

      {(!!related?.similarEntries?.length || !!entry.fingerprint) && (
        <Section title={t('Similar failures')} testId="blackbox-related">
          {entry.fingerprint && (
            <p style={{ margin: '0 0 0.3rem' }}>
              <Link to={blackBoxHref('entries', { fingerprint: entry.fingerprint })} title={t('List every entry with this failure fingerprint.')}>{t('All entries with this fingerprint')}</Link>
              {related?.occurrencesLast30Days != null && <span className="text-dim"> · {t('{{count}} in the last 30 days', { count: related.occurrencesLast30Days })}</span>}
            </p>
          )}
          {!!related?.similarEntries?.length && (
            <ul className="blackbox-list">
              {related.similarEntries.map((s) => (
                <li key={s.entryId}><Link to={`/blackbox/${encodeURIComponent(s.entryId)}`} className="mono">{s.entryId}</Link> {s.missionTitle ? `· ${s.missionTitle}` : ''} {s.resolution && <span className="blackbox-chip">{t(s.resolution)}</span>}</li>
              ))}
            </ul>
          )}
        </Section>
      )}

      <Lazy title={t('Timeline')} defaultOpen={variant === 'page'}>
        {() => <BlackBoxTimeline entryId={entry.id} />}
      </Lazy>
      <Lazy title={t('Attempt diff')}>
        {() => <BlackBoxAttemptDiff entryId={entry.id} attempts={attempts} />}
      </Lazy>
      <Lazy title={t('Nearby successes')}>
        {() => <BlackBoxNearbySuccesses entryId={entry.id} />}
      </Lazy>
      <Lazy title={t('Brief preview')}>
        {() => brief ? <Markdown>{brief.text}</Markdown> : <LoadingIndicator label={t('Loading...')} />}
      </Lazy>
      <Lazy title={t('Evidence')} defaultOpen={variant === 'page'}>
        {() => <BlackBoxEvidence entryId={entry.id} />}
      </Lazy>
      <Lazy title={t('Notes')} defaultOpen={variant === 'page'}>
        {() => <BlackBoxNotes entryId={entry.id} refreshKey={notesKey} />}
      </Lazy>

      <JsonViewer open={showJson} title={t('Black box entry')} id={entry.id} data={entry} onClose={() => setShowJson(false)} />
      <BlackBoxResolveDialog
        open={showResolve}
        entry={entry}
        onClose={() => setShowResolve(false)}
        onResolved={(updated) => {
          setShowResolve(false);
          setEntry({ ...entry, ...updated, record: updated.record ?? entry.record });
          setNotesKey((k) => k + 1);
          pushToast('success', t('Resolution saved.'));
          onChanged?.();
        }}
      />
      <BlackBoxActionDialog
        open={!!action}
        entryId={entry.id}
        target={action}
        onClose={() => setAction(null)}
        onRan={() => { setNotesKey((k) => k + 1); void load(); onChanged?.(); }}
      />
    </div>
  );
}
