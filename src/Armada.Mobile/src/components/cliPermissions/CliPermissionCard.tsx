import { useRouter, type Href } from 'expo-router';
import { StyleSheet, View } from 'react-native';
import type { CliPermissionRequest } from '@dashboard/types/models';
import { prettyJson } from '@dashboard/lib/askFormat';
import { decisionSourceText, isPendingRequest, requestStatusLabel } from '@dashboard/lib/cliPermissions';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing } from '../../theme/typography';
import { CodeBlock } from '../ask/CodeBlock';
import { statusTone } from '../ask/statusTone';
import { AppText } from '../ui/AppText';
import { Disclosure } from '../ui/Disclosure';
import { StatusBadge } from '../ui/StatusBadge';
import { CliPermissionCountdown } from './CliPermissionCountdown';
import { CliPermissionDecisionControls } from './CliPermissionDecisionControls';

interface CliPermissionCardProps {
  request: CliPermissionRequest;
  onDecided?: (updated: CliPermissionRequest) => void;
  /** Show the conversation title (the approvals center, where the request is out of context). */
  showThread?: boolean;
}

function Fact({ label, value, onPress }: { label: string; value: string; onPress?: () => void }) {
  return (
    <View style={styles.fact}>
      <AppText variant="caption" muted>{label}</AppText>
      <AppText variant="caption" color={onPress ? 'primary' : 'text'} accessibilityRole={onPress ? 'link' : 'text'} onPress={onPress}>{value}</AppText>
    </View>
  );
}

/**
 * Approval card for a CLI tool call a captain wants to make (the dashboard's CliPermissionCard): the tool, the
 * command or input, who asked, a live expiry countdown while pending, and the decision afterwards. Approvers get
 * Allow once / Allow and remember / Deny; everyone else sees that it waits on an admin.
 */
export function CliPermissionCard({ request, onDecided, showThread }: CliPermissionCardProps) {
  const { t, formatRelativeTime } = useLocale();
  const { colors } = useTheme();
  const router = useRouter();
  const status = String(request.status || '');
  const normalized = status.toLowerCase();
  const pending = isPendingRequest(request);
  const input = prettyJson(request.inputText);
  const summary = (request.summaryText ?? '').trim();
  const decidedHow = pending ? null : decisionSourceText(t, request);
  const accent = pending ? colors.warning : normalized === 'allowed' ? colors.success : normalized === 'denied' ? colors.danger : colors.border;
  const go = (path: string) => () => router.push(path as Href);

  return (
    <View
      testID={`cli-permission-${request.id}`}
      accessibilityLabel={t('CLI permission: {{tool}}', { tool: request.toolName })}
      style={[styles.card, { borderColor: accent, backgroundColor: colors.surface }]}
    >
      <View style={styles.header}>
        <AppText variant="label">{pending ? t('Permission needed') : t('CLI permission')}</AppText>
        <AppText variant="mono">{request.toolName || t('tool')}</AppText>
        <StatusBadge label={requestStatusLabel(t, status)} tone={pending ? 'warning' : statusTone(status)} />
        <CliPermissionCountdown expiresUtc={request.expiresUtc} active={pending} />
      </View>

      {summary ? <CodeBlock text={summary} testID={`cli-permission-summary-${request.id}`} /> : null}

      <View style={styles.facts}>
        {request.captainName || request.captainId ? (
          <Fact label={t('Captain')} value={request.captainName || request.captainId || ''} onPress={request.captainId ? go(`/captains/${encodeURIComponent(request.captainId)}`) : undefined} />
        ) : null}
        {request.vesselId ? <Fact label={t('Vessel')} value={request.vesselName || request.vesselId} onPress={go(`/vessels/${encodeURIComponent(request.vesselId)}`)} /> : null}
        {request.missionId ? <Fact label={t('Mission')} value={request.missionTitle || request.missionId} onPress={go(`/missions/${encodeURIComponent(request.missionId)}`)} /> : null}
        {showThread && request.threadId ? (
          <Fact label={t('Conversation')} value={request.threadTitle || request.threadId} onPress={go(`/ask/${encodeURIComponent(request.threadId)}`)} />
        ) : null}
        {request.createdUtc ? <Fact label={t('Asked')} value={formatRelativeTime(request.createdUtc)} /> : null}
      </View>

      {input && input !== '{}' ? (
        <Disclosure title={t('Input')}>
          <CodeBlock text={input} />
        </Disclosure>
      ) : null}

      {pending && request.canDecide ? (
        <CliPermissionDecisionControls request={request} onDecided={onDecided} describedBy={summary || request.toolName} />
      ) : null}
      {pending && !request.canDecide ? <AppText variant="caption" muted>{t('Waiting for an admin to decide.')}</AppText> : null}

      {!pending ? (
        <View style={styles.outcome}>
          <AppText variant="caption" color={normalized === 'allowed' ? 'success' : normalized === 'denied' ? 'danger' : 'textMuted'}>
            {normalized === 'allowed' ? t('Allowed. The captain ran the tool.') : null}
            {normalized === 'denied' ? t('Denied. The tool did not run.') : null}
            {normalized === 'expired' ? t('Expired without a decision. The tool did not run.') : null}
            {normalized === 'cancelled' ? t('Cancelled. The turn ended before a decision.') : null}
            {decidedHow ? ` ${decidedHow}` : ''}
            {request.decidedUtc ? ` ${formatRelativeTime(request.decidedUtc)}` : ''}
          </AppText>
          {request.decisionMessage ? <AppText variant="caption">{t('Message: {{text}}', { text: request.decisionMessage })}</AppText> : null}
        </View>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  card: { borderWidth: 1, borderLeftWidth: 4, borderRadius: radius.md, padding: spacing.md, gap: spacing.sm },
  header: { flexDirection: 'row', alignItems: 'center', flexWrap: 'wrap', gap: spacing.sm },
  facts: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.md },
  fact: { gap: 2, maxWidth: '100%' },
  outcome: { gap: spacing.xs },
});
