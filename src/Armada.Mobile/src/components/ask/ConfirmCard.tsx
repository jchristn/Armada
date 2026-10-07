import { StyleSheet, View } from 'react-native';
import type { AskActionProposal } from '@dashboard/types/models';
import { prettyJson } from '@dashboard/lib/askFormat';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing } from '../../theme/typography';
import { AppText } from '../ui/AppText';
import { Button } from '../ui/Button';
import { Disclosure } from '../ui/Disclosure';
import { StatusBadge } from '../ui/StatusBadge';
import { CodeBlock } from './CodeBlock';
import { statusTone } from './statusTone';

export interface ConfirmCardProps {
  proposal: AskActionProposal;
  onApprove?: (proposal: AskActionProposal) => void;
  onReject?: (proposal: AskActionProposal) => void;
  /** An approve or reject call for this proposal is in flight. */
  busy?: boolean;
  /** Compact mode for ActionResult messages: outcome only. */
  compact?: boolean;
  /** Show the exact arguments expanded (the approvals center, where the card is out of its conversation). */
  argumentsOpen?: boolean;
}

/**
 * Inline confirmation for a state-changing tool call (the dashboard's AskConfirmCard): the tool, a one-line
 * summary, the exact arguments, Approve / Reject while pending, and the outcome afterwards. Nothing runs until the
 * user approves.
 */
export function ConfirmCard({ proposal, onApprove, onReject, busy, compact, argumentsOpen }: ConfirmCardProps) {
  const { t, formatRelativeTime } = useLocale();
  const { colors } = useTheme();
  const status = String(proposal.status || '');
  const normalized = status.toLowerCase();
  const pending = normalized === 'pending';
  const args = prettyJson(proposal.argumentsText);
  const result = prettyJson(proposal.resultText);
  const fromQuickAction = String(proposal.source).toLowerCase() === 'quickaction';
  const accent = pending ? colors.warning : normalized === 'failed' ? colors.danger : normalized === 'executed' ? colors.success : colors.border;
  const summary = proposal.summaryText ? ` ${proposal.summaryText}` : '';

  return (
    <View
      testID={`confirm-card-${proposal.id}`}
      accessibilityLabel={t('Action: {{tool}}', { tool: proposal.toolName })}
      style={[styles.card, { borderColor: accent, backgroundColor: colors.surface }, compact ? styles.compact : null]}
    >
      <View style={styles.header}>
        <AppText variant="label">{pending ? t('Approval needed') : t('Action')}</AppText>
        <AppText variant="mono" style={styles.tool}>{proposal.toolName}</AppText>
        <StatusBadge label={t(status || 'Unknown')} tone={statusTone(status)} />
      </View>
      <AppText variant="caption" muted>{fromQuickAction ? t('Quick action') : t('Proposed by the captain')}</AppText>

      {proposal.summaryText ? <AppText>{proposal.summaryText}</AppText> : null}

      {args ? (
        <Disclosure title={t('Exact arguments')} initiallyOpen={argumentsOpen} testID={`confirm-args-${proposal.id}`}>
          <CodeBlock text={args} />
        </Disclosure>
      ) : null}

      {pending ? (
        <View style={styles.pending}>
          <AppText variant="caption" muted>
            {proposal.expiresUtc
              ? t('Nothing runs until you approve. Expires {{time}}.', { time: formatRelativeTime(proposal.expiresUtc) })
              : t('Nothing runs until you approve.')}
          </AppText>
          <View style={styles.actions}>
            <Button
              label={t('Reject')}
              variant="secondary"
              onPress={() => onReject?.(proposal)}
              disabled={busy || !onReject}
              accessibilityHint={summary.trim() || undefined}
              testID={`proposal-reject-${proposal.id}`}
              style={styles.action}
            />
            <Button
              label={busy ? t('Working...') : t('Approve')}
              onPress={() => onApprove?.(proposal)}
              disabled={busy || !onApprove}
              busy={busy}
              accessibilityHint={summary.trim() || undefined}
              testID={`proposal-approve-${proposal.id}`}
              style={styles.action}
            />
          </View>
        </View>
      ) : null}

      {normalized === 'approved' ? <AppText variant="caption" muted>{t('Approved. Running now...')}</AppText> : null}
      {normalized === 'rejected' ? <AppText variant="caption" muted>{t('Rejected. Nothing was run.')}</AppText> : null}
      {normalized === 'expired' ? <AppText variant="caption" muted>{t('Expired without running.')}</AppText> : null}
      {normalized === 'executed' ? (
        <View style={styles.outcome}>
          <AppText variant="caption" color="success">
            {proposal.executedUtc ? t('Ran {{time}}.', { time: formatRelativeTime(proposal.executedUtc) }) : t('Ran successfully.')}
          </AppText>
          {result ? (
            <Disclosure title={t('Result')}>
              <CodeBlock text={result} />
            </Disclosure>
          ) : null}
        </View>
      ) : null}
      {normalized === 'failed' ? (
        <AppText variant="caption" color="danger" accessibilityRole="alert">{proposal.errorText || t('The action failed.')}</AppText>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  card: { borderWidth: 1, borderLeftWidth: 4, borderRadius: radius.md, padding: spacing.md, gap: spacing.sm },
  compact: { padding: spacing.sm },
  header: { flexDirection: 'row', alignItems: 'center', flexWrap: 'wrap', gap: spacing.sm },
  tool: { flexShrink: 1 },
  pending: { gap: spacing.sm },
  actions: { flexDirection: 'row', gap: spacing.sm, justifyContent: 'flex-end', flexWrap: 'wrap' },
  action: { minWidth: 110, marginBottom: 0 },
  outcome: { gap: spacing.xs },
});
