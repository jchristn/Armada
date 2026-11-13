import { useRouter, type Href } from 'expo-router';
import { useCallback, useState, type ReactNode } from 'react';
import { StyleSheet, View } from 'react-native';
import {
  addBlackBoxNote,
  getBlackBox,
  getBlackBoxBrief,
  getMissionBlackBox,
  getVoyageBlackBox,
  resolveBlackBox,
  runBlackBoxRecovery,
} from '@dashboard/api/client';
import type { BlackBoxEntry, BlackBoxRecoveryPath, BlackBoxResolveRequest } from '@dashboard/types/blackbox';
import { Markdown } from '../../components/ask/Markdown';
import { AppText, BottomSheet, Button, CodeBlock, ErrorState, KeyValueRow, ListRow, LoadingState, Section, StatusBadge } from '../../components/ui';
import { errorMessage } from '../../data/errors';
import { useLiveRefresh } from '../../data/useLiveRefresh';
import { useQuery } from '../../data/useQuery';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { copyText, shareText } from '../../platform/shareText';
import { spacing } from '../../theme/typography';
import {
  askPrefillRoute,
  blackBoxAskPrompt,
  bounded,
  entrySubject,
  isBlackBoxNotFound,
  pathIsRunnable,
  pathLosesData,
  rankedRecoveryPaths,
  resolutionTone,
  spaced,
  triageTone,
  whatHappened,
} from './blackBoxModel';
import { NoteSheet, ResolveSheet } from './EntryFormSheets';
import { RecoverySheet, type RecoveryStage } from './RecoverySheet';
import { ResilienceSection } from './ResilienceSection';
import { armadaFixPrompt, changeBrief, isArmadaDefect, isPriorOnly, resilienceOf, type ResilienceChange } from './resilienceModel';

/** Which entry to show: a mission's or voyage's current entry, or one entry by id. */
export interface BlackBoxSource {
  kind: 'mission' | 'voyage' | 'entry';
  id: string;
}

function loadEntry(source: BlackBoxSource): Promise<BlackBoxEntry> {
  if (source.kind === 'mission') return getMissionBlackBox(source.id);
  if (source.kind === 'voyage') return getVoyageBlackBox(source.id);
  return getBlackBox(source.id);
}

export interface BlackBoxPanelProps {
  source: BlackBoxSource;
  /**
   * Embedded on a mission or voyage: shown while the entry loads and when the subject has none (the plain failure
   * reason). The entry screen passes nothing and shows its own loading and not-found states.
   */
  fallback?: ReactNode;
  /** The entry screen: full-page loading, not-found and error states, and no "Open entry" action. */
  standalone?: boolean;
  /** Extra sections shown under the panel once the entry has loaded (the entry screen's timeline and nearby successes). */
  after?: (entry: BlackBoxEntry) => ReactNode;
}

/**
 * The black box of a failed mission or voyage (MISSION_BLACKBOX_PLAN.md 11.5): the summary (what
 * happened, where, how, status before and after, why, conflicting missions, files and directories), the recorded
 * recovery paths (dry run, then a confirmation with a data-loss acknowledgement; runs are recorded as Operator),
 * Resolve, Add note, Share, Copy FIX THIS and Full report (all the entry's single FIX THIS brief), and Ask Armada
 * about this failure. The Armada resilience section follows the summary (a "Suspected Armada defect" badge marks an
 * ArmadaDefect triage), with Draft Armada fix mission (Ask Armada prefilled) and Copy fix brief. Follows the entry
 * live over the socket (blackbox.* events).
 */
export function BlackBoxPanel({ source, fallback = null, standalone = false, after }: BlackBoxPanelProps) {
  const { t, formatDateTime } = useLocale();
  const router = useRouter();
  const { pushToast } = useNotifications();
  const query = useQuery<BlackBoxEntry>(() => loadEntry(source), [source.kind, source.id], t('Failed to load the black box.'));
  const reload = query.reload;
  useLiveRefresh(['blackbox.'], () => { void reload(); });
  const [busy, setBusy] = useState<string | null>(null);
  const [recovery, setRecovery] = useState<RecoveryStage | null>(null);
  const [resolveOpen, setResolveOpen] = useState(false);
  const [noteOpen, setNoteOpen] = useState(false);
  const [report, setReport] = useState<string | null>(null);

  const entry = query.data;
  const open = useCallback((path: string) => router.push(path as Href), [router]);

  const withBrief = useCallback(async (key: string, apply: (brief: string) => Promise<void>, failure: string) => {
    if (!entry) return;
    setBusy(key);
    try {
      const brief = await getBlackBoxBrief(entry.id);
      await apply(brief);
    } catch (e) {
      pushToast('error', t(failure, { message: errorMessage(e, '') }));
    } finally {
      setBusy(null);
    }
  }, [entry, pushToast, t]);

  if (!entry) {
    const missing = query.failure != null && isBlackBoxNotFound(query.failure);
    if (standalone) {
      if (query.loading) return <LoadingState label={t('Loading...')} />;
      return (
        <ErrorState
          title={missing ? t('Black box entry not found.') : t('Failed to load the black box.')}
          message={missing ? null : query.error}
          retryLabel={t('Retry')}
          onRetry={() => void query.refresh()}
        />
      );
    }
    if (query.loading || missing || query.failure == null) return <>{fallback}</>;
    return (
      <>
        {fallback}
        <View style={styles.inlineError} testID="bbx-panel-error">
          <AppText color="danger" accessibilityRole="alert">{t('Black box unavailable: {{message}}', { message: query.error ?? '' })}</AppText>
          <Button label={t('Retry')} variant="secondary" onPress={() => void query.refresh()} testID="bbx-panel-retry" />
        </View>
      </>
    );
  }

  const record = entry.record ?? null;
  const subject = entrySubject(entry);
  const site = record?.site ?? null;
  const mechanism = record?.mechanism ?? null;
  const conflict = record?.conflict ?? null;
  const findings = record?.analysis?.why ?? [];
  const paths = rankedRecoveryPaths(entry);
  const conflictingMissions = conflict?.conflictingMissions ?? [];
  const files = bounded(conflict?.conflictedFiles, conflict?.conflictedFileTotal);
  const directories = bounded(conflict?.conflictedDirectories);
  const statusBefore = record?.status?.before ?? entry.statusBefore ?? null;
  const statusAfter = record?.status?.after ?? entry.statusAfter ?? null;
  const summary = whatHappened(entry);
  const where = [site?.component, site?.method].filter(Boolean).join('.');
  const host = site?.host ? `${site.hostKind && site.hostKind !== 'Unknown' ? `${site.hostKind} ` : ''}${site.host}` : null;
  const source_ = site?.sourceFile ? `${site.sourceFile}${site.sourceLine ? `:${site.sourceLine}` : ''}` : null;
  const how = [
    mechanism?.exceptionType ? `${mechanism.exceptionType}${mechanism.exceptionMessage ? `: ${mechanism.exceptionMessage}` : ''}` : null,
    mechanism?.failingCommand ? `${t('Command')}: ${mechanism.failingCommand}${mechanism.commandExitCode != null ? ` (${t('exit {{code}}', { code: mechanism.commandExitCode })})` : ''}` : null,
    mechanism?.gitOperation ? `${t('Git')}: ${mechanism.gitOperation}` : null,
    mechanism?.runtimeFailureKind ? `${t('Runtime')}: ${spaced(mechanism.runtimeFailureKind)}${mechanism.exitCode != null ? ` (${t('exit {{code}}', { code: mechanism.exitCode })})` : ''}` : null,
  ].filter(Boolean) as string[];
  const tail = mechanism?.gitStderrTail || mechanism?.commandOutputTail || null;

  async function dryRun(path: BlackBoxRecoveryPath) {
    if (!entry) return;
    setBusy(`recover-${path.id}`);
    try {
      const result = await runBlackBoxRecovery(entry.id, path.id, { dryRun: true, authorKind: 'Operator' });
      if (!result.confirmToken) {
        pushToast('error', t('The dry run did not return a confirmation. {{message}}', { message: result.message ?? '' }).trim());
        return;
      }
      setRecovery({ stage: 'confirm', path, dryRun: result, acknowledged: false, running: false });
    } catch (e) {
      pushToast('error', t('Dry run failed: {{message}}', { message: errorMessage(e, '') }));
    } finally {
      setBusy(null);
    }
  }

  async function confirmRecovery() {
    if (!entry || recovery?.stage !== 'confirm') return;
    const { path, dryRun: plan, acknowledged } = recovery;
    const losesData = pathLosesData(path);
    if (losesData && !acknowledged) return;
    setRecovery({ ...recovery, running: true });
    try {
      const result = await runBlackBoxRecovery(entry.id, path.id, {
        dryRun: false,
        confirmToken: plan.confirmToken,
        acknowledgeDataLoss: losesData ? true : undefined,
        authorKind: 'Operator',
      });
      setRecovery({ stage: 'done', path, result });
      if (result.succeeded) pushToast('success', t('Recovery "{{title}}" ran.', { title: path.title }));
      else pushToast('error', t('Recovery "{{title}}" failed: {{message}}', { title: path.title, message: result.message ?? '' }));
    } catch (e) {
      // Refused (expired, used or stale confirmation, data loss not acknowledged, not permitted): start over.
      setRecovery(null);
      pushToast('error', t('Recovery refused: {{message}} Run a new dry run.', { message: errorMessage(e, '') }));
    }
    void reload();
  }

  async function resolve(request: BlackBoxResolveRequest) {
    if (!entry) return;
    setBusy('resolve');
    try {
      await resolveBlackBox(entry.id, request);
      setResolveOpen(false);
      pushToast('success', t('Black box entry marked {{resolution}}.', { resolution: t(spaced(request.resolution)) }));
      void reload();
    } catch (e) {
      pushToast('error', t('Resolve failed: {{message}}', { message: errorMessage(e, '') }));
    } finally {
      setBusy(null);
    }
  }

  async function addNote(body: string) {
    if (!entry) return;
    setBusy('note');
    try {
      await addBlackBoxNote(entry.id, body);
      setNoteOpen(false);
      pushToast('success', t('Note added.'));
      void reload();
    } catch (e) {
      pushToast('error', t('Add note failed: {{message}}', { message: errorMessage(e, '') }));
    } finally {
      setBusy(null);
    }
  }

  const copy = () => withBrief('copy', async (brief) => {
    await copyText(brief);
    pushToast('success', t('FIX THIS brief copied.'));
  }, 'Copy failed: {{message}}');
  const share = () => withBrief('share', async (brief) => {
    await shareText(brief, t('Black box: {{id}}', { id: subject.id }));
  }, 'Share failed: {{message}}');
  const viewReport = () => withBrief('report', async (brief) => { setReport(brief); }, 'Failed to load the report: {{message}}');
  const ask = () => open(askPrefillRoute(blackBoxAskPrompt(entry)));
  const draftFix = () => open(askPrefillRoute(armadaFixPrompt(entry, resilienceOf(entry))));
  const copyFixBrief = async (change: ResilienceChange) => {
    try {
      await copyText(changeBrief(change));
      pushToast('success', t('Armada fix brief copied.'));
    } catch (e) {
      pushToast('error', t('Copy failed: {{message}}', { message: errorMessage(e, '') }));
    }
  };

  return (
    <View testID="bbx-panel">
      <Section title={t('Black Box')}>
        <View style={styles.pad}>
          <View style={styles.badges}>
            <StatusBadge label={t(spaced(entry.failureKind || entry.step))} tone="failed" />
            <StatusBadge label={t(spaced(entry.triage))} tone={triageTone(entry.triage)} />
            <StatusBadge label={t(spaced(entry.resolution))} tone={resolutionTone(entry.resolution)} />
            {entry.attempt > 1 ? <StatusBadge label={t('Attempt {{n}}', { n: entry.attempt })} tone="info" /> : null}
            {isArmadaDefect(entry) ? (
              <View testID="bbx-suspected-defect">
                <StatusBadge label={isPriorOnly(entry) ? t('Suspected Armada defect (prior only)') : t('Suspected Armada defect')} tone="failed" />
              </View>
            ) : null}
          </View>
          {summary ? <AppText selectable testID="bbx-what-happened">{summary}</AppText> : null}
        </View>
        <KeyValueRow label={t('Where')} value={`${spaced(entry.phase)} / ${spaced(entry.step)}`} testID="bbx-where" />
        {where ? <KeyValueRow label={t('Component')} value={where} mono /> : null}
        {host ? <KeyValueRow label={t('Host')} value={host} /> : null}
        {source_ ? <KeyValueRow label={t('Source')} value={source_} mono /> : null}
        {entry.vesselId ? <KeyValueRow label={t('Vessel')} value={entry.vesselName || entry.vesselId} onPress={() => open(`/vessels/${entry.vesselId}`)} /> : null}
        {record?.subject?.branchName ? <KeyValueRow label={t('Branch')} value={record.subject.branchName} mono /> : null}
        <KeyValueRow label={t('Status')} value={`${statusBefore ? t(statusBefore) : '-'} -> ${statusAfter ? t(statusAfter) : '-'}`} testID="bbx-status-change" />
        <KeyValueRow label={t('Captured')} value={formatDateTime(entry.createdUtc)} />
      </Section>

      <ResilienceSection entry={entry} busy={busy !== null} onDraft={draftFix} onCopyBrief={(change) => void copyFixBrief(change)} />

      {how.length > 0 || tail ? (
        <Section title={t('How')}>
          <View style={[styles.pad, styles.gap]} testID="bbx-how">
            {how.map((line) => <AppText key={line} selectable>{line}</AppText>)}
            {tail ? <CodeBlock wrap text={tail} /> : null}
          </View>
        </Section>
      ) : null}

      {findings.length > 0 ? (
        <Section title={t('Why')}>
          {findings.map((finding, index) => (
            <ListRow
              key={`${finding.ruleId}-${index}`}
              testID={`bbx-why-${index}`}
              title={finding.statement}
              subtitle={`${finding.ruleId} - ${t('{{confidence}} confidence', { confidence: t(finding.confidence) })}`}
            />
          ))}
        </Section>
      ) : null}

      {conflictingMissions.length > 0 || files.shown.length > 0 || directories.shown.length > 0 ? (
        <Section title={t('Conflicts')}>
          {conflictingMissions.map((m, index) => (
            <ListRow
              key={`${m.missionId ?? m.commitHash ?? 'conflict'}-${index}`}
              testID={`bbx-conflict-mission-${index}`}
              icon="git-branch-outline"
              title={m.title || m.missionId || m.commitHash || t('Change outside Armada')}
              subtitle={[
                m.missionId,
                m.isOutsideArmada ? t('outside Armada') : null,
                m.overlappingFileTotal ? t('{{count}} overlapping file(s)', { count: m.overlappingFileTotal }) : null,
              ].filter(Boolean).join(' - ') || null}
              onPress={m.missionId ? () => open(`/missions/${m.missionId}`) : undefined}
            />
          ))}
          {files.shown.length > 0 ? (
            <View style={[styles.pad, styles.gap]} testID="bbx-conflict-files">
              <AppText variant="label">{t('Conflicted files')}</AppText>
              {files.shown.map((file) => <AppText key={file} variant="caption" selectable>{file}</AppText>)}
              {files.more > 0 ? <AppText variant="caption" muted>{t('and {{count}} more', { count: files.more })}</AppText> : null}
            </View>
          ) : null}
          {directories.shown.length > 0 ? (
            <View style={[styles.pad, styles.gap]} testID="bbx-conflict-directories">
              <AppText variant="label">{t('Directories')}</AppText>
              {directories.shown.map((d) => <AppText key={d.directory} variant="caption" selectable>{`${d.directory} (${d.fileCount})`}</AppText>)}
              {directories.more > 0 ? <AppText variant="caption" muted>{t('and {{count}} more', { count: directories.more })}</AppText> : null}
            </View>
          ) : null}
        </Section>
      ) : null}

      {paths.length > 0 ? (
        <Section title={t('Recovery')} footer={t('Each recovery runs as a dry run first; nothing runs until you confirm.')}>
          {paths.map((path) => (
            <View key={path.id} style={[styles.pad, styles.gap]} testID={`bbx-path-${path.id}`}>
              <AppText variant="label">{path.title}</AppText>
              <View style={styles.badges}>
                {path.risk && path.risk !== 'Unknown' ? <StatusBadge label={t('{{risk}} risk', { risk: t(path.risk) })} tone={path.risk === 'Low' ? 'info' : 'warning'} /> : null}
                {pathLosesData(path) ? <StatusBadge label={t('Can lose work')} tone="failed" /> : null}
                {path.successRate != null ? <StatusBadge label={t('{{percent}}% recovered ({{runs}} runs)', { percent: Math.round(path.successRate * 100), runs: path.trackedRuns ?? 0 })} tone="info" /> : null}
              </View>
              {path.runbookId ? (
                <Button
                  label={t('Runbook: {{title}}', { title: path.runbookTitle || path.runbookId })}
                  variant="ghost"
                  icon="book-outline"
                  accessibilityHint={t('This recovery starts the linked runbook')}
                  onPress={() => open(`/runbooks/${path.runbookId}`)}
                  testID={`bbx-runbook-${path.id}`}
                />
              ) : null}
              {(path.steps ?? []).map((step) => <AppText key={step.order} variant="caption" muted>{`${step.order}. ${step.description}`}</AppText>)}
              {pathIsRunnable(path) ? (
                <Button
                  label={t('Dry run')}
                  variant="secondary"
                  icon="play-outline"
                  busy={busy === `recover-${path.id}`}
                  disabled={busy !== null}
                  accessibilityHint={t('Shows what this recovery will do; nothing runs until you confirm')}
                  onPress={() => void dryRun(path)}
                  testID={`bbx-recover-${path.id}`}
                />
              ) : <AppText variant="caption" muted>{t('Manual steps only.')}</AppText>}
            </View>
          ))}
        </Section>
      ) : null}

      <View style={styles.actions}>
        <Button label={t('Copy FIX THIS')} icon="copy-outline" variant="secondary" busy={busy === 'copy'} disabled={busy !== null} onPress={() => void copy()} testID="bbx-copy" />
        <Button label={t('Share')} icon="share-outline" variant="secondary" busy={busy === 'share'} disabled={busy !== null} onPress={() => void share()} testID="bbx-share" />
        <Button label={t('Ask Armada about this failure')} icon="chatbubbles-outline" variant="secondary" onPress={ask} testID="bbx-ask" />
        <Button label={t('Full report')} icon="document-text-outline" variant="ghost" busy={busy === 'report'} disabled={busy !== null} onPress={() => void viewReport()} testID="bbx-report" />
        {entry.resolution === 'Open' ? (
          <Button label={t('Resolve')} icon="checkmark-done-outline" variant="secondary" onPress={() => setResolveOpen(true)} testID="bbx-resolve" />
        ) : (
          <Button label={t('Change resolution')} icon="checkmark-done-outline" variant="ghost" onPress={() => setResolveOpen(true)} testID="bbx-resolve" />
        )}
        <Button label={t('Add Note')} icon="create-outline" variant="ghost" onPress={() => setNoteOpen(true)} testID="bbx-add-note" />
        {!standalone ? <Button label={t('Open entry')} icon="open-outline" variant="ghost" onPress={() => open(`/blackbox/${entry.id}`)} testID="bbx-open-entry" /> : null}
      </View>
      {entry.resolutionNote || entry.resolutionLink ? (
        <AppText variant="caption" muted style={styles.resolution} selectable testID="bbx-resolution-note">
          {[entry.resolutionNote, entry.resolutionLink].filter(Boolean).join(' - ')}
        </AppText>
      ) : null}
      {after ? after(entry) : null}

      <RecoverySheet
        state={recovery}
        onAcknowledge={(value) => setRecovery((current) => (current?.stage === 'confirm' ? { ...current, acknowledged: value } : current))}
        onConfirm={() => void confirmRecovery()}
        onClose={() => setRecovery(null)}
      />
      <ResolveSheet open={resolveOpen} busy={busy === 'resolve'} onClose={() => setResolveOpen(false)} onSubmit={(request) => void resolve(request)} />
      <NoteSheet open={noteOpen} busy={busy === 'note'} onClose={() => setNoteOpen(false)} onSubmit={(body) => void addNote(body)} />
      <BottomSheet open={report !== null} title={t('Black box report')} onClose={() => setReport(null)} closeLabel={t('Close')} testID="bbx-report-sheet">
        {report !== null ? <Markdown testID="bbx-report-text">{report}</Markdown> : null}
      </BottomSheet>
    </View>
  );
}

const styles = StyleSheet.create({
  pad: { paddingHorizontal: spacing.lg, paddingVertical: spacing.sm },
  gap: { gap: spacing.xs },
  badges: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs, marginBottom: spacing.xs },
  actions: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm, paddingHorizontal: spacing.md, marginBottom: spacing.lg },
  resolution: { paddingHorizontal: spacing.lg, marginBottom: spacing.lg },
  inlineError: { paddingHorizontal: spacing.lg, gap: spacing.sm, marginBottom: spacing.lg },
});
