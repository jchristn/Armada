import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { EntityStatusBadge } from '../../../components/app/EntityStatusBadge';
import { AppText, BottomSheet, Button, SelectField } from '../../../components/ui';
import { useLocale } from '../../../i18n/LocaleContext';
import { spacing } from '../../../theme/typography';

/**
 * The dashboard's Transition Mission Status modal. `statuses` is the list to offer (the list page omits the current
 * status; the detail page offers every status).
 */
export function TransitionSheet({ open, currentStatus, statuses, onClose, onSubmit }: {
  open: boolean;
  currentStatus: string;
  statuses: string[];
  onClose: () => void;
  onSubmit: (status: string) => void;
}) {
  const { t } = useLocale();
  return (
    <BottomSheet open={open} title={t('Transition Mission Status')} onClose={onClose} closeLabel={t('Close')} testID="mission-transition-sheet">
      {open ? <TransitionForm currentStatus={currentStatus} statuses={statuses} onClose={onClose} onSubmit={onSubmit} /> : null}
    </BottomSheet>
  );
}

function TransitionForm({ currentStatus, statuses, onClose, onSubmit }: { currentStatus: string; statuses: string[]; onClose: () => void; onSubmit: (status: string) => void }) {
  const { t } = useLocale();
  const [target, setTarget] = useState('');
  return (
    <View>
      <View style={styles.current}>
        <AppText muted>{t('Current status:')}</AppText>
        <EntityStatusBadge status={currentStatus} />
      </View>
      <SelectField
        label={t('New Status')}
        value={target}
        onChange={setTarget}
        placeholder={t('Select status...')}
        closeLabel={t('Close')}
        options={statuses.map((s) => ({ value: s, label: t(s) }))}
        testID="mission-transition-status"
      />
      <View style={styles.actions}>
        <Button label={t('Cancel')} variant="ghost" onPress={onClose} />
        <Button label={t('Transition')} onPress={() => onSubmit(target)} disabled={!target} testID="mission-transition-submit" />
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  current: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, marginBottom: spacing.lg },
  actions: { flexDirection: 'row', justifyContent: 'flex-end', gap: spacing.sm },
});
