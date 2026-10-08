import { useEffect, useMemo, useState } from 'react';
import { StyleSheet } from 'react-native';
import { approvalTargetFromInboxItem, approvalTargetFromPush, approvalTargetKey } from '../approvals/actions';
import { ApprovalCard } from '../components/approvals/ApprovalCard';
import { AppText } from '../components/ui/AppText';
import { BottomSheet } from '../components/ui/BottomSheet';
import { Button } from '../components/ui/Button';
import { useLocale } from '../i18n/LocaleContext';
import { useApprovals } from '../notifications/ApprovalsContext';
import { spacing } from '../theme/typography';
import type { PushPayload } from './payload';

export interface PushApprovalReviewSheetProps {
  /** The push whose Approve was tapped, or null when the sheet is closed. */
  payload: PushPayload | null;
  onClose: () => void;
}

/**
 * What Approve on a notification opens: the full request (the CLI command or tool input, the Ask proposal's
 * arguments, who asked) with the same decision controls as the Approvals center. The notification text is cut to a
 * short summary, so Approve never decides from the lock screen; the user approves here, after seeing everything.
 */
export function PushApprovalReviewSheet({ payload, onClose }: PushApprovalReviewSheetProps) {
  const { t } = useLocale();
  const { items, refresh } = useApprovals();
  // The payload whose inbox reload finished (so a new push never shows the previous one's "gone" state).
  const [loadedFor, setLoadedFor] = useState<PushPayload | null>(null);
  const loaded = payload !== null && loadedFor === payload;

  const targetKey = useMemo(() => {
    if (!payload) return null;
    const target = approvalTargetFromPush({ kind: payload.kind, entityId: payload.entityId, threadId: payload.threadId, url: payload.path });
    return target ? approvalTargetKey(target) : null;
  }, [payload]);

  // Reload the inbox when the sheet opens, so the request is current (it may have been decided elsewhere).
  useEffect(() => {
    if (!payload) return undefined;
    let active = true;
    void refresh().finally(() => { if (active) setLoadedFor(payload); });
    return () => { active = false; };
  }, [payload, refresh]);

  const item = useMemo(() => {
    if (!targetKey) return null;
    return items.find((candidate) => {
      const target = approvalTargetFromInboxItem(candidate);
      return !!target && approvalTargetKey(target) === targetKey;
    }) ?? null;
  }, [items, targetKey]);

  return (
    <BottomSheet open={payload !== null} title={t('Review before approving')} onClose={onClose} closeLabel={t('Close')} testID="push-review-sheet">
      {item ? (
        <>
          <AppText muted style={styles.gap}>{t('Check the full request before you approve it.')}</AppText>
          <ApprovalCard item={item} onChanged={() => { void refresh(); onClose(); }} />
        </>
      ) : loaded ? (
        <>
          <AppText style={styles.gap} testID="push-review-gone">{t('This request was already decided or is no longer available.')}</AppText>
          <Button label={t('Close')} variant="secondary" onPress={onClose} />
        </>
      ) : (
        <AppText muted>{t('Loading...')}</AppText>
      )}
    </BottomSheet>
  );
}

const styles = StyleSheet.create({
  gap: { marginBottom: spacing.md },
});
