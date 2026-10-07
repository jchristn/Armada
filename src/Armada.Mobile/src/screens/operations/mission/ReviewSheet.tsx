import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { reviewVerdictNeedsComment, type ReviewVerdict } from '@dashboard/lib/missionActions';
import { AppText, BottomSheet, Button, TextField } from '../../../components/ui';
import { useLocale } from '../../../i18n/LocaleContext';
import { spacing } from '../../../theme/typography';

/**
 * The dashboard's Resolve Review modal: feedback plus Approve, Conditionally Approve, More Work Required, and Deny
 * (the middle two need feedback).
 */
export function ReviewSheet({ open, initialComment, onClose, onSubmit }: {
  open: boolean;
  initialComment: string;
  onClose: () => void;
  onSubmit: (verdict: ReviewVerdict, comment: string) => void;
}) {
  const { t } = useLocale();
  return (
    <BottomSheet open={open} title={t('Resolve Review')} onClose={onClose} closeLabel={t('Close')} testID="mission-review-sheet">
      {open ? <ReviewForm initialComment={initialComment} onSubmit={onSubmit} /> : null}
    </BottomSheet>
  );
}

function ReviewForm({ initialComment, onSubmit }: { initialComment: string; onSubmit: (verdict: ReviewVerdict, comment: string) => void }) {
  const { t } = useLocale();
  const [comment, setComment] = useState(initialComment);
  const hasComment = !!comment.trim();
  const verdicts: { verdict: ReviewVerdict; label: string; help: string; variant: 'primary' | 'secondary' | 'danger' }[] = [
    { verdict: 'approve', label: t('Approve'), help: t('- accept this stage and continue.'), variant: 'primary' },
    { verdict: 'conditional', label: t('Conditionally Approve'), help: t('- continue, but the next step must consider your feedback.'), variant: 'secondary' },
    { verdict: 'morework', label: t('More Work Required'), help: t('- redo this same step with your feedback.'), variant: 'secondary' },
    { verdict: 'deny', label: t('Deny'), help: t('- reject this stage and fail the pipeline.'), variant: 'danger' },
  ];
  return (
    <View>
      <AppText muted style={styles.intro}>{t('Choose how to resolve this review gate. Your feedback is carried into the next step or the re-run.')}</AppText>
      <TextField
        label={t('Feedback')}
        value={comment}
        onChangeText={setComment}
        multiline
        numberOfLines={6}
        textAlignVertical="top"
        placeholder={t('Required for Conditionally Approve and More Work Required; optional for Approve/Deny.')}
        testID="mission-review-comment"
      />
      {verdicts.map((v) => {
        const blocked = reviewVerdictNeedsComment(v.verdict) && !hasComment;
        return (
          <View key={v.verdict}>
            <Button
              label={v.label}
              variant={v.variant}
              disabled={blocked}
              accessibilityHint={blocked ? t('Add feedback first') : v.help}
              onPress={() => onSubmit(v.verdict, comment.trim())}
              testID={`mission-review-${v.verdict}`}
            />
            <AppText variant="caption" muted style={styles.help}>{v.label} {v.help}</AppText>
          </View>
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  intro: { marginBottom: spacing.md },
  help: { marginBottom: spacing.md },
});
