import { useRouter, type Href } from 'expo-router';
import { useEffect, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { getAskThread } from '@dashboard/api/client';
import type { AskActionProposal, CliPermissionRequest, InboxItem, InboxSeverity } from '@dashboard/types/models';
import { inboxItemTitle } from '@dashboard/lib/deploymentApprovalLabel';
import { CLI_PERMISSION_KIND, inboxActionLabel } from '@dashboard/lib/inboxKinds';
import { isPendingRequest } from '@dashboard/lib/cliPermissions';
import { approvalTargetFromInboxItem, decideApproval, interventionFor, runIntervention, type ApprovalTarget, type Intervention } from '../../approvals/actions';
import { useLocale } from '../../i18n/LocaleContext';
import { appPathFromLink } from '../../navigation/deepLinks';
import { useNotifications } from '../../notifications/NotificationContext';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing } from '../../theme/typography';
import type { Palette } from '../../theme/palette';
import { ConfirmCard } from '../ask/ConfirmCard';
import { CliPermissionCard } from '../cliPermissions/CliPermissionCard';
import { CliPermissionCountdown } from '../cliPermissions/CliPermissionCountdown';
import { AppText } from '../ui/AppText';
import { Button } from '../ui/Button';
import { ConfirmDialog } from '../ui/ConfirmDialog';
import { StatusBadge } from '../ui/StatusBadge';
import { ReviewSheet, type ReviewVerdict } from './ReviewSheet';

function severityColor(severity: InboxSeverity): keyof Palette {
  if (severity === 'Critical') return 'danger';
  if (severity === 'Warning') return 'warning';
  return 'border';
}

function errorText(err: unknown, fallback: string): string {
  return err instanceof Error && err.message ? err.message : fallback;
}

interface ApprovalCardProps {
  item: InboxItem;
  /** Something changed on the server (a decision was made): reload the inbox. */
  onChanged: () => void;
}

/** The item's page in the app (the inbox href is a dashboard path). */
function itemPath(item: InboxItem): string | null {
  return appPathFromLink(item.href);
}

/** Header shared by every inbox card: title, detail, severity, and the link to the item's page. */
function ItemHeader({ item, openLabel }: { item: InboxItem; openLabel?: string }) {
  const { t } = useLocale();
  const router = useRouter();
  const path = itemPath(item);
  return (
    <View style={styles.head}>
      <View style={styles.headText}>
        <AppText variant="label">{inboxItemTitle(t, item)}</AppText>
        {item.detail ? <AppText variant="caption" muted>{item.detail}</AppText> : null}
      </View>
      <StatusBadge label={t(item.severity)} tone={item.severity === 'Critical' ? 'error' : item.severity === 'Warning' ? 'warning' : 'info'} />
      {path && openLabel ? (
        <AppText variant="caption" color="primary" accessibilityRole="link" onPress={() => router.push(path as Href)} style={styles.open}>{openLabel}</AppText>
      ) : null}
    </View>
  );
}

/** An Ask proposal waiting on the user: the confirm card with the exact arguments, read from its conversation. */
function AskProposalApproval({ item, target, onChanged }: { item: InboxItem; target: Extract<ApprovalTarget, { kind: 'ask_proposal' }>; onChanged: () => void }) {
  const { t } = useLocale();
  const [proposal, setProposal] = useState<AskActionProposal | null>(null);
  const [loaded, setLoaded] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    let active = true;
    getAskThread(target.threadId)
      .then((detail) => {
        if (!active) return;
        setProposal((detail?.pendingProposals ?? []).find((p) => p.id === target.proposalId) ?? null);
      })
      .catch(() => { /* the card falls back to the inbox text and the Open link */ })
      .finally(() => { if (active) setLoaded(true); });
    return () => { active = false; };
  }, [target.threadId, target.proposalId]);

  async function decide(approve: boolean) {
    setBusy(true);
    setError('');
    try {
      const result = await decideApproval(target, approve ? 'approve' : 'deny');
      if (result.proposal?.id) setProposal(result.proposal);
      onChanged();
    } catch (err: unknown) {
      setError(errorText(err, approve ? t('The action could not be approved.') : t('The action could not be rejected.')));
    } finally {
      setBusy(false);
    }
  }

  return (
    <View style={styles.inner}>
      <ItemHeader item={item} openLabel={inboxActionLabel(t, item)} />
      {proposal ? (
        <ConfirmCard proposal={proposal} onApprove={() => void decide(true)} onReject={() => void decide(false)} busy={busy} argumentsOpen />
      ) : loaded ? null : (
        <AppText variant="caption" muted>{t('Loading...')}</AppText>
      )}
      {error ? <AppText variant="caption" color="danger" accessibilityRole="alert">{error}</AppText> : null}
    </View>
  );
}

/** A mission review: Review opens the verdict sheet (feedback plus four verdicts). */
function ReviewApproval({ item, target, onChanged }: { item: InboxItem; target: Extract<ApprovalTarget, { kind: 'review' }>; onChanged: () => void }) {
  const { t } = useLocale();
  const { pushToast } = useNotifications();
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(verdict: ReviewVerdict) {
    setBusy(true);
    setError(null);
    try {
      await decideApproval(target, verdict.decision, verdict.options);
      setOpen(false);
      pushToast(verdict.decision === 'approve' ? 'success' : 'warning', verdict.decision === 'approve' ? t('Review approved for "{{title}}".', { title: item.entityName || item.title }) : t('Review denied for "{{title}}".', { title: item.entityName || item.title }));
      onChanged();
    } catch (err: unknown) {
      setError(t('Review decision failed: {{message}}', { message: errorText(err, '') }));
    } finally {
      setBusy(false);
    }
  }

  return (
    <View style={styles.inner}>
      <ItemHeader item={item} openLabel={t('Open')} />
      <Button label={t('Review mission')} onPress={() => { setError(null); setOpen(true); }} testID={`review-open-${target.missionId}`} style={styles.noMargin} />
      <ReviewSheet open={open} title={t('Review: {{title}}', { title: item.entityName || item.title })} busy={busy} error={error} onSubmit={(v) => void submit(v)} onClose={() => setOpen(false)} />
    </View>
  );
}

/** A deployment approval: Approve / Deny, each confirmed with the dashboard's wording. */
function DeploymentApproval({ item, target, onChanged }: { item: InboxItem; target: Extract<ApprovalTarget, { kind: 'deployment_approval' }>; onChanged: () => void }) {
  const { t } = useLocale();
  const { pushToast } = useNotifications();
  const [confirm, setConfirm] = useState<'approve' | 'deny' | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const label = inboxItemTitle(t, item);

  async function run(decision: 'approve' | 'deny') {
    setConfirm(null);
    setBusy(true);
    setError('');
    try {
      await decideApproval(target, decision);
      pushToast(decision === 'approve' ? 'success' : 'warning', decision === 'approve' ? t('Approved "{{title}}".', { title: label }) : t('Denied "{{title}}".', { title: label }));
      onChanged();
    } catch (err: unknown) {
      setError(errorText(err, t('Action failed.')));
    } finally {
      setBusy(false);
    }
  }

  return (
    <View style={styles.inner}>
      <ItemHeader item={item} openLabel={inboxActionLabel(t, item)} />
      <View style={styles.actions}>
        <Button label={t('Deny')} variant="secondary" onPress={() => setConfirm('deny')} disabled={busy} testID={`deployment-deny-${target.deploymentId}`} style={styles.noMargin} />
        <Button label={t('Approve')} onPress={() => setConfirm('approve')} disabled={busy} busy={busy} testID={`deployment-approve-${target.deploymentId}`} style={styles.noMargin} />
      </View>
      {error ? <AppText variant="caption" color="danger" accessibilityRole="alert">{error}</AppText> : null}
      <ConfirmDialog
        open={confirm !== null}
        title={confirm === 'deny' ? t('Deny Deployment') : t('Approve Deployment')}
        message={confirm === 'deny' ? t('Deny "{{title}}" without executing it?', { title: label }) : t('Approve and execute "{{title}}"?', { title: label })}
        confirmLabel={confirm === 'deny' ? t('Deny') : t('Approve')}
        cancelLabel={t('Cancel')}
        danger={confirm === 'deny'}
        onConfirm={() => { if (confirm) void run(confirm); }}
        onCancel={() => setConfirm(null)}
        testID="deployment-confirm"
      />
    </View>
  );
}

/** A pending CLI permission request: the full card with Allow once / Allow and remember / Deny when the user may decide. */
function CliApproval({ item, onChanged }: { item: InboxItem; onChanged: () => void }) {
  const { t } = useLocale();
  const [request, setRequest] = useState<CliPermissionRequest | null>(item.cliPermission ?? null);
  if (!request) {
    return (
      <View style={styles.inner}>
        <ItemHeader item={item} openLabel={inboxActionLabel(t, item)} />
        <CliPermissionCountdown expiresUtc={item.expiresUtc} active />
      </View>
    );
  }
  return (
    <View style={styles.inner}>
      <ItemHeader item={item} openLabel={inboxActionLabel(t, item)} />
      <CliPermissionCard request={request} showThread onDecided={(updated) => { setRequest(updated); if (!isPendingRequest(updated)) onChanged(); }} />
    </View>
  );
}

/** One "Waiting for your approval" item, with its decision controls inline. */
export function ApprovalCard({ item, onChanged }: ApprovalCardProps) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const target = approvalTargetFromInboxItem(item);
  let body;
  if (item.kind === CLI_PERMISSION_KIND) body = <CliApproval item={item} onChanged={onChanged} />;
  else if (target?.kind === 'ask_proposal') body = <AskProposalApproval item={item} target={target} onChanged={onChanged} />;
  else if (target?.kind === 'review') body = <ReviewApproval item={item} target={target} onChanged={onChanged} />;
  else if (target?.kind === 'deployment_approval') body = <DeploymentApproval item={item} target={target} onChanged={onChanged} />;
  else body = <View style={styles.inner}><ItemHeader item={item} openLabel={inboxActionLabel(t, item)} /></View>;
  return (
    <View testID={`approval-${item.kind}-${item.entityId ?? ''}`} style={[styles.card, { backgroundColor: colors.surface, borderColor: colors.border, borderLeftColor: colors[severityColor(item.severity)] }]}>
      {body}
    </View>
  );
}

/** One "Needs intervention" item: the failure, a link to its page, and the one-tap fix when there is one. */
export function InterventionCard({ item, onChanged }: ApprovalCardProps) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const { pushToast } = useNotifications();
  const intervention = interventionFor(item);
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const title = inboxItemTitle(t, item);

  const labels: Record<Intervention['kind'], { action: string; confirm: string; done: string }> = {
    retry_landing: { action: t('Retry landing'), confirm: t('Retry landing "{{title}}"?', { title }), done: t('Landing retried for "{{title}}".', { title }) },
    restart_mission: { action: t('Restart mission'), confirm: t('Restart "{{title}}"?', { title }), done: t('Restarted "{{title}}".', { title }) },
    stop_captain: { action: t('Stop captain'), confirm: t('Stop "{{title}}"?', { title }), done: t('Stopped "{{title}}".', { title }) },
  };
  const label = intervention ? labels[intervention.kind] : null;

  async function run() {
    if (!intervention || !label) return;
    setConfirming(false);
    setBusy(true);
    setError('');
    try {
      await runIntervention(intervention);
      pushToast('success', label.done);
      onChanged();
    } catch (err: unknown) {
      setError(errorText(err, t('Action failed.')));
    } finally {
      setBusy(false);
    }
  }

  return (
    <View testID={`intervention-${item.kind}-${item.entityId ?? ''}`} style={[styles.card, { backgroundColor: colors.surface, borderColor: colors.border, borderLeftColor: colors[severityColor(item.severity)] }]}>
      <View style={styles.inner}>
        <ItemHeader item={item} openLabel={t('Open')} />
        {intervention && label ? (
          <Button label={label.action} variant="secondary" onPress={() => setConfirming(true)} busy={busy} disabled={busy} testID={`intervention-run-${item.entityId ?? ''}`} style={styles.noMargin} />
        ) : null}
        {error ? <AppText variant="caption" color="danger" accessibilityRole="alert">{error}</AppText> : null}
      </View>
      {label ? (
        <ConfirmDialog
          open={confirming}
          title={label.action}
          message={label.confirm}
          confirmLabel={label.action}
          cancelLabel={t('Cancel')}
          danger={intervention?.kind !== 'retry_landing'}
          onConfirm={() => void run()}
          onCancel={() => setConfirming(false)}
          testID="intervention-confirm"
        />
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  card: { borderWidth: StyleSheet.hairlineWidth, borderLeftWidth: 4, borderRadius: radius.md, marginHorizontal: spacing.md, marginBottom: spacing.sm },
  inner: { padding: spacing.md, gap: spacing.sm },
  head: { flexDirection: 'row', alignItems: 'flex-start', flexWrap: 'wrap', gap: spacing.sm },
  headText: { flex: 1, minWidth: 180, gap: 2 },
  open: { paddingVertical: spacing.xs },
  actions: { flexDirection: 'row', justifyContent: 'flex-end', gap: spacing.sm },
  noMargin: { marginBottom: 0 },
});
