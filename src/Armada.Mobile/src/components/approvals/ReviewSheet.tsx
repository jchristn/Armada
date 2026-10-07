import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { useLocale } from '../../i18n/LocaleContext';
import { spacing } from '../../theme/typography';
import { AppText } from '../ui/AppText';
import { BottomSheet } from '../ui/BottomSheet';
import { Button } from '../ui/Button';
import { TextField } from '../ui/TextField';
import type { ApprovalDecision, ApprovalOptions } from '../../approvals/actions';

export interface ReviewVerdict {
  decision: ApprovalDecision;
  options: ApprovalOptions;
}

interface ReviewSheetProps {
  open: boolean;
  title: string;
  busy: boolean;
  error: string | null;
  onSubmit: (verdict: ReviewVerdict) => void;
  onClose: () => void;
}

/**
 * The mission review decision (the dashboard's review modal on the mission page): feedback plus Approve,
 * Conditionally Approve, More Work Required, and Deny. Feedback is required for the two middle verdicts.
 */
export function ReviewSheet({ open, title, busy, error, onSubmit, onClose }: ReviewSheetProps) {
  const { t } = useLocale();
  const [comment, setComment] = useState('');
  const hasComment = !!comment.trim();
  const close = () => { if (!busy) { setComment(''); onClose(); } };
  return (
    <BottomSheet open={open} title={title} onClose={close} closeLabel={t('Cancel')} testID="review-sheet">
      {error ? <AppText color="danger" accessibilityRole="alert" style={styles.gap}>{error}</AppText> : null}
      <TextField
        label={t('Feedback')}
        value={comment}
        onChangeText={setComment}
        multiline
        placeholder={t('Required for Conditionally Approve and More Work Required; optional for Approve/Deny.')}
        testID="review-comment"
      />
      <View style={styles.help}>
        <AppText variant="caption" muted><AppText variant="caption" style={styles.bold}>{t('Approve')}</AppText> {t('- accept this stage and continue.')}</AppText>
        <AppText variant="caption" muted><AppText variant="caption" style={styles.bold}>{t('Conditionally Approve')}</AppText> {t('- continue, but the next step must consider your feedback.')}</AppText>
        <AppText variant="caption" muted><AppText variant="caption" style={styles.bold}>{t('More Work Required')}</AppText> {t('- redo this same step with your feedback.')}</AppText>
        <AppText variant="caption" muted><AppText variant="caption" style={styles.bold}>{t('Deny')}</AppText> {t('- reject this stage and fail the pipeline.')}</AppText>
      </View>
      <Button label={t('Approve')} onPress={() => onSubmit({ decision: 'approve', options: { comment } })} disabled={busy} busy={busy} testID="review-approve" />
      <Button
        label={t('Conditionally Approve')}
        variant="secondary"
        onPress={() => onSubmit({ decision: 'approve', options: { comment, reviewVariant: 'conditional' } })}
        disabled={busy || !hasComment}
        accessibilityHint={hasComment ? undefined : t('Add feedback first')}
        testID="review-conditional"
      />
      <Button
        label={t('More Work Required')}
        variant="secondary"
        onPress={() => onSubmit({ decision: 'deny', options: { comment, reviewVariant: 'RetryStage' } })}
        disabled={busy || !hasComment}
        accessibilityHint={hasComment ? undefined : t('Add feedback first')}
        testID="review-more-work"
      />
      <Button label={t('Deny')} variant="danger" onPress={() => onSubmit({ decision: 'deny', options: { comment, reviewVariant: 'FailPipeline' } })} disabled={busy} testID="review-deny" />
      <Button label={t('Cancel')} variant="ghost" onPress={close} disabled={busy} />
    </BottomSheet>
  );
}

const styles = StyleSheet.create({
  gap: { marginBottom: spacing.sm },
  help: { gap: spacing.xs, marginBottom: spacing.lg },
  bold: { fontWeight: '700' },
});
