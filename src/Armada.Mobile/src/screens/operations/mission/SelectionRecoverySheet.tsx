import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { apiErrorMessage, authorizeCaptainSelectionRecovery, getCaptainSelection, isApiStatus } from '@dashboard/api/client';
import type { UsageSelectionRecovery } from '@dashboard/types/models';
import { SwitchField } from '../../../build/fields';
import { AppText, BottomSheet, Button, TextField } from '../../../components/ui';
import { useLocale } from '../../../i18n/LocaleContext';
import { spacing } from '../../../theme/typography';

interface SheetProps { open: boolean; missionId: string; current?: UsageSelectionRecovery | null; onClose: () => void; onSaved: () => void }

/**
 * The Dashboard's recovery permissions dialog: explicit, audited fallback and soft-reserve permissions for one Pending
 * mission. A conflict keeps the draft reason; Refresh reloads the current revision before another attempt.
 */
export function SelectionRecoverySheet({ open, missionId, current, onClose, onSaved }: SheetProps) {
  const { t } = useLocale();
  return (
    <BottomSheet open={open} title={t('usageSelection.recovery.title')} onClose={onClose} closeLabel={t('Close')} testID="selection-recovery-sheet">
      {open ? <RecoveryForm missionId={missionId} current={current} onClose={onClose} onSaved={onSaved} /> : null}
    </BottomSheet>
  );
}

function RecoveryForm({ missionId, current, onClose, onSaved }: Omit<SheetProps, 'open'>) {
  const { t } = useLocale();
  const [baseline, setBaseline] = useState(current);
  const [fallback, setFallback] = useState(current?.allowFallback ?? false);
  const [reserve, setReserve] = useState(current?.reserveException ?? false);
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [conflict, setConflict] = useState(false);
  const [error, setError] = useState('');
  const lost = (failure: unknown) => [401, 403, 404].some(code => isApiStatus(failure, code));
  const save = async () => {
    if (busy || conflict || !reason.trim()) return;
    setBusy(true); setError('');
    try {
      await authorizeCaptainSelectionRecovery(missionId, { expectedRevision: baseline?.revision ?? 0, allowFallback: fallback, reserveException: reserve, reason: reason.trim() });
      onSaved(); onClose();
    } catch (failure) {
      if (lost(failure)) { onSaved(); onClose(); return; }
      setConflict(isApiStatus(failure, 409));
      setError(isApiStatus(failure, 409) ? t('usageSelection.recovery.conflict') : apiErrorMessage(failure, t('usageSelection.recovery.saveFailed')));
    } finally { setBusy(false); }
  };
  const refresh = async () => {
    try {
      const summary = await getCaptainSelection(missionId);
      if (!summary.canManageRecovery) { onSaved(); onClose(); return; }
      setBaseline(summary.recovery); setFallback(summary.recovery?.allowFallback ?? false); setReserve(summary.recovery?.reserveException ?? false);
      setConflict(false); setError('');
    } catch (failure) {
      if (lost(failure)) { onSaved(); onClose(); } else setError(apiErrorMessage(failure, t('usageSelection.loadFailed')));
    }
  };
  return (
    <View style={styles.form}>
      <AppText muted>{t('usageSelection.recovery.scope')}</AppText>
      {error ? <View style={styles.row}>
        <AppText color="danger" accessibilityRole="alert">{error}</AppText>
        {conflict ? <Button label={t('usageSelection.refresh')} variant="secondary" onPress={() => void refresh()} testID="selection-recovery-refresh" /> : null}
      </View> : null}
      <SwitchField label={t('usageSelection.recovery.fallback')} hint={t('usageSelection.recovery.fallbackHelp')} value={fallback} onChange={setFallback} disabled={busy} testID="selection-recovery-fallback" />
      <SwitchField label={t('usageSelection.recovery.reserve')} hint={t('usageSelection.recovery.reserveHelp')} value={reserve} onChange={setReserve} disabled={busy} testID="selection-recovery-reserve" />
      <TextField label={t('usageSelection.recovery.reason')} value={reason} onChangeText={setReason}
        multiline maxLength={512} editable={!busy} testID="selection-recovery-reason" />
      <AppText variant="caption" muted>{t('usageSelection.recovery.audit')}</AppText>
      <View style={styles.actions}>
        <Button label={t('Cancel')} variant="ghost" onPress={onClose} disabled={busy} />
        <Button label={t(busy ? 'Saving...' : 'usageSelection.recovery.save')} onPress={() => void save()} disabled={busy || conflict || !reason.trim()} testID="selection-recovery-save" />
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  form: { gap: spacing.md },
  row: { gap: spacing.sm },
  actions: { flexDirection: 'row', justifyContent: 'flex-end', gap: spacing.sm },
});
