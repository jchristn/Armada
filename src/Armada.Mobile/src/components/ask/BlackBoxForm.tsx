import { useEffect, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { listBlackBox } from '@dashboard/api/client';
import type { BlackBoxEntrySummary } from '@dashboard/types/blackbox';
import { useLocale } from '../../i18n/LocaleContext';
import { spacing } from '../../theme/typography';
import { AppText } from '../ui/AppText';
import { Button } from '../ui/Button';
import { SelectField } from '../ui/SelectSheet';
import { TextField } from '../ui/TextField';
import type { QuickActionFormProps } from './DispatchForm';

/** How many recent failures the picker lists. */
export const RECENT_BLACKBOX_ENTRIES = 25;

/** Pure: the `/blackbox` arguments for a mission id and optional question, or the English error key. */
export function blackBoxFormArguments(missionId: string, question: string): { args: Record<string, unknown> } | { error: string } {
  const id = missionId.trim();
  if (!/^msn_\S+$/.test(id)) return { error: 'Choose a failed mission or type its id (msn_...).' };
  const args: Record<string, unknown> = { missionId: id };
  if (question.trim()) args.question = question.trim();
  return { args };
}

/**
 * The `/blackbox` form (the dashboard's AskBlackBoxForm): pick a recent failed mission with a black box entry, or
 * type a mission id, and optionally a question. Submitting runs `get_blackbox`, shows the card, and sends the question
 * to the captain. Reading the black box changes nothing.
 */
export function BlackBoxForm({ busy, onSubmit, onCancel }: QuickActionFormProps) {
  const { t } = useLocale();
  const [entries, setEntries] = useState<BlackBoxEntrySummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [missionId, setMissionId] = useState('');
  const [question, setQuestion] = useState('');
  const [error, setError] = useState('');

  useEffect(() => {
    let active = true;
    listBlackBox({ pageSize: RECENT_BLACKBOX_ENTRIES, isCurrent: true, subjectType: 'Mission', order: 'CreatedDescending' })
      .then((page) => { if (active) setEntries((page?.objects ?? []).filter((e) => !!e.missionId)); })
      .catch(() => { /* the mission id can still be typed */ })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, []);

  function submit() {
    const result = blackBoxFormArguments(missionId, question);
    if ('error' in result) { setError(result.error); return; }
    setError('');
    onSubmit(result.args);
  }

  const options = entries.map((e) => ({
    value: e.missionId ?? '',
    label: e.missionTitle || e.missionId || e.id,
    description: [e.missionId, e.vesselName, e.step].filter(Boolean).join(' - '),
  }));

  return (
    <View testID="ask-blackbox-form">
      <SelectField
        label={t('Recent failures')}
        value={entries.some((e) => e.missionId === missionId) ? missionId : ''}
        options={[{ value: '', label: loading ? t('Loading...') : entries.length ? t('Choose a failed mission') : t('No recent failures') }, ...options]}
        onChange={(value) => { setMissionId(value); setError(''); }}
        closeLabel={t('Close')}
        disabled={loading || busy || entries.length === 0}
        testID="ask-blackbox-recent"
      />
      <TextField
        label={t('Mission')}
        value={missionId}
        onChangeText={(value) => { setMissionId(value); setError(''); }}
        placeholder="msn_..."
        autoCapitalize="none"
        autoCorrect={false}
        hint={t('A failed mission with a black box entry. Recent failures are listed; you can also type a mission id.')}
        error={error ? t(error) : null}
        editable={!busy}
        testID="ask-blackbox-mission"
      />
      <TextField
        label={t('Question')}
        value={question}
        onChangeText={setQuestion}
        multiline
        numberOfLines={3}
        textAlignVertical="top"
        placeholder={t('Why did it fail, and what could have been done differently?')}
        hint={t('Optional. Sent to the captain once the black box card is shown.')}
        editable={!busy}
        testID="ask-blackbox-question"
      />
      <AppText variant="caption" muted style={styles.gap}>{t('Reading the black box changes nothing.')}</AppText>
      <Button label={busy ? t('Starting...') : t('Show black box')} onPress={submit} disabled={busy} busy={busy} testID="ask-blackbox-submit" />
      <Button label={t('Cancel')} variant="ghost" onPress={onCancel} disabled={busy} />
    </View>
  );
}

const styles = StyleSheet.create({
  gap: { marginBottom: spacing.sm },
});
