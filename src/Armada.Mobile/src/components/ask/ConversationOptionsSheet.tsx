import { StyleSheet, Switch, View } from 'react-native';
import type { AskThread, Captain, CliPermissionPolicy } from '@dashboard/types/models';
import { resolutionSummary } from '@dashboard/lib/cliPermissions';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { spacing } from '../../theme/typography';
import { CliPermissionPolicyField } from '../cliPermissions/CliPermissionPolicyField';
import { AppText } from '../ui/AppText';
import { BottomSheet } from '../ui/BottomSheet';
import { ListRow } from '../ui/ListRow';
import { SelectField } from '../ui/SelectSheet';
import type { ThreadActions } from './ThreadActionsSheet';

export interface ConversationOptionsProps extends ThreadActions {
  open: boolean;
  onClose: () => void;
  thread: AskThread | null;
  captains: Captain[];
  /** Captain for a conversation that does not exist yet. */
  draftCaptainId: string;
  onDraftCaptainChange: (id: string) => void;
  onCaptainChange: (captainId: string | null) => void;
  onAutoApproveChange: (value: boolean) => void;
  onCliPolicyChange: (policy: CliPermissionPolicy | null) => void;
  /** The current user may choose Bypass (global or tenant admin). */
  canBypassCli: boolean;
  /** A captain turn is running (the captain cannot change mid-turn). */
  busy: boolean;
  onStartRename: () => void;
}

/** Captain label as the dashboard's picker shows it: name, then model or runtime. */
export function captainLabel(c: Captain): string {
  const detail = c.model || c.runtime;
  return detail ? `${c.name} (${detail})` : c.name;
}

/**
 * The conversation header controls in a sheet (the dashboard's AskConversationHeader): captain picker, the
 * Auto-approve switch with its warning, the CLI tools policy with the effective resolution, Summarize, and the
 * conversation actions.
 */
export function ConversationOptionsSheet(props: ConversationOptionsProps) {
  const { open, onClose, thread, captains, draftCaptainId, onDraftCaptainChange, onCaptainChange, onAutoApproveChange, onCliPolicyChange, canBypassCli, busy, onStartRename } = props;
  const { t } = useLocale();
  const { colors } = useTheme();
  const captainValue = thread ? (thread.captainId ?? '') : draftCaptainId;
  const autoWarning = t('Auto-approve runs state-changing actions the captain proposes immediately, without a confirm card. Only turn it on for conversations you trust; every action is still recorded here.');
  const effective = thread?.cliPermission ? resolutionSummary(t, { ...thread.cliPermission, fallbackReason: null }) : null;

  return (
    <BottomSheet open={open} title={thread?.title || t('New conversation')} onClose={onClose} closeLabel={t('Close')} testID="ask-options">
      <SelectField
        label={t('Captain')}
        value={captainValue}
        options={[{ value: '', label: t('No captain (quick actions only)') }, ...captains.map((c) => ({ value: c.id, label: captainLabel(c) }))]}
        onChange={(id) => (thread ? onCaptainChange(id || null) : onDraftCaptainChange(id))}
        closeLabel={t('Close')}
        disabled={busy}
        testID="ask-captain"
      />

      {thread ? (
        <View style={styles.switchRow}>
          <View style={styles.flex}>
            <AppText variant="label">{t('Auto-approve')}</AppText>
            <AppText variant="caption" muted>{autoWarning}</AppText>
          </View>
          <Switch
            testID="ask-auto-approve"
            value={!!thread.autoApprove}
            onValueChange={onAutoApproveChange}
            accessibilityLabel={t('Auto-approve')}
            accessibilityHint={autoWarning}
            trackColor={{ true: colors.warning, false: colors.border }}
          />
        </View>
      ) : null}

      {thread ? (
        <CliPermissionPolicyField
          label={t('CLI tools')}
          value={thread.cliPermissionPolicy ?? null}
          onChange={onCliPolicyChange}
          allowBypass={canBypassCli}
          hint={effective ?? t('How the captain CLI handles shell commands, file edits, and fetches that need permission. Inherit uses the captain policy, then the server default.')}
          testID="ask-cli-policy"
        />
      ) : null}

      {thread ? (
        <View style={[styles.actions, { borderColor: colors.border }]}>
          <ListRow title={t('Summarize')} subtitle={t('Post a short summary of this conversation')} icon="document-text-outline" onPress={() => { onClose(); props.onSummarize(thread); }} testID="ask-options-summarize" />
          <ListRow title={t('Rename')} icon="create-outline" onPress={() => { onClose(); onStartRename(); }} testID="ask-options-rename" />
          <ListRow title={thread.pinned ? t('Unpin') : t('Pin')} icon="pin-outline" onPress={() => { onClose(); props.onTogglePin(thread); }} />
          <ListRow title={thread.archived ? t('Unarchive') : t('Archive')} icon="archive-outline" onPress={() => { onClose(); props.onToggleArchive(thread); }} />
          <ListRow title={t('Delete')} icon="trash-outline" destructive onPress={() => { onClose(); props.onDelete(thread); }} testID="ask-options-delete" />
        </View>
      ) : null}
    </BottomSheet>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  switchRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.md, marginBottom: spacing.lg },
  actions: { borderTopWidth: StyleSheet.hairlineWidth },
});
