import { useEffect, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { buildVesselContext, listCaptains } from '@dashboard/api/client';
import type { Captain, Vessel } from '@dashboard/types/models';
import { errorMessage } from '../../build/useLiveResource';
import { AppText, Button, TextField } from '../../components/ui';
import { SelectField, type SelectOption } from '../../components/ui/SelectSheet';
import { useLocale } from '../../i18n/LocaleContext';
import { spacing } from '../../theme/typography';

/**
 * Build or refine a vessel's Model Context (the dashboard's BuildContextModal): pick a captain and optional focus
 * notes, then launch it synchronously (it can take minutes; the sheet stays open and busy meanwhile).
 */
export function BuildContext({ vessel, refine, onClose, onBuilt }: { vessel: Vessel; refine: boolean; onClose: () => void; onBuilt: (v: Vessel) => void }) {
  const { t } = useLocale();
  const [captains, setCaptains] = useState<Captain[]>([]);
  const [captainId, setCaptainId] = useState('');
  const [notes, setNotes] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    let live = true;
    listCaptains({ pageSize: 200 })
      .then((result) => {
        if (!live) return;
        setCaptains(result.objects);
        if (result.objects.length > 0) setCaptainId((current) => current || result.objects[0].id);
      })
      .catch((e: unknown) => { if (live) setError(e instanceof Error ? e.message : t('Failed to load captains.')); });
    return () => { live = false; };
  }, [t]);

  async function submit() {
    if (!captainId || busy) return;
    setBusy(true);
    setError('');
    try {
      const updated = await buildVesselContext(vessel.id, { captainId, notes: notes.trim() || undefined });
      onBuilt(updated);
      onClose();
    } catch (e) {
      setError(errorMessage(e) || t('Failed to build Model Context.'));
    } finally {
      setBusy(false);
    }
  }

  const options: SelectOption<string>[] = captains.length === 0
    ? [{ value: '', label: t('No captains available') }]
    : captains.map((c) => ({ value: c.id, label: `${c.name} (${c.model || c.runtime})` }));

  return (
    <View>
      <AppText muted style={styles.intro}>
        {refine
          ? t('Launch a captain to inspect {{name}} and refine its existing Model Context.', { name: vessel.name })
          : t('Launch a captain to inspect {{name}} and write its Model Context.', { name: vessel.name })}
      </AppText>
      <SelectField label={t('Captain')} value={captainId} options={options} onChange={setCaptainId} closeLabel={t('Close')} disabled={busy} testID="vessel-build-context-captain" />
      <TextField
        label={t('Focus / guidance (optional)')}
        value={notes}
        onChangeText={setNotes}
        editable={!busy}
        multiline
        textAlignVertical="top"
        placeholder={t('e.g. Emphasize the build and test commands, and how the plugin system works.')}
        testID="vessel-build-context-notes"
      />
      <AppText variant="caption" muted style={styles.intro}>
        {t('The captain follows the editable "vessel.build_context" prompt (Configuration > Prompts). This runs synchronously and can take a few minutes.')}
      </AppText>
      {error ? <AppText color="danger" accessibilityRole="alert" style={styles.intro}>{error}</AppText> : null}
      {busy ? (
        <AppText muted accessibilityLiveRegion="polite" style={styles.intro}>
          {refine ? t('Refining Model Context... this can take a few minutes.') : t('Building Model Context... this can take a few minutes.')}
        </AppText>
      ) : null}
      <View style={styles.actions}>
        <Button label={t('Cancel')} variant="ghost" onPress={onClose} disabled={busy} />
        <Button
          label={busy ? t('Working...') : refine ? t('Refine Context') : t('Build Context')}
          onPress={() => void submit()}
          busy={busy}
          disabled={!captainId}
          testID="vessel-build-context-submit"
        />
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  intro: { marginBottom: spacing.md },
  actions: { flexDirection: 'row', justifyContent: 'flex-end', gap: spacing.sm },
});
