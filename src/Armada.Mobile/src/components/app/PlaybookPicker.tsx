import { listPlaybooks } from '@dashboard/api/client';
import {
  DEFAULT_PLAYBOOK_DELIVERY_MODE,
  PLAYBOOK_DELIVERY_MODES,
  activePlaybooksOf,
  availablePlaybooks,
  playbookOptionsForRow,
} from '@dashboard/lib/playbookSelection';
import type { Playbook, PlaybookDeliveryMode, SelectedPlaybook } from '@dashboard/types/models';
import { useEffect, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { AppText, Button, IconButton, Section } from '../ui';
import { SelectField } from '../../components/ui/SelectField';
import { useLocale } from '../../i18n/LocaleContext';
import { spacing } from '../../theme/typography';

const MODES = Object.keys(PLAYBOOK_DELIVERY_MODES) as PlaybookDeliveryMode[];

/** The dashboard's PlaybookSelector: attach active playbooks with a delivery mode each. */
export function PlaybookPicker({ value, onChange, disabled, testID = 'playbooks' }: {
  value: SelectedPlaybook[];
  onChange: (next: SelectedPlaybook[]) => void;
  disabled?: boolean;
  testID?: string;
}) {
  const { t } = useLocale();
  const [playbooks, setPlaybooks] = useState<Playbook[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [draftId, setDraftId] = useState('');
  const [draftMode, setDraftMode] = useState<PlaybookDeliveryMode>(DEFAULT_PLAYBOOK_DELIVERY_MODE);

  useEffect(() => {
    let mounted = true;
    listPlaybooks({ pageSize: 9999 })
      .then((result) => { if (mounted) { setPlaybooks(result?.objects ?? []); setError(''); } })
      .catch((e: unknown) => { if (mounted) setError(e instanceof Error ? e.message : t('Failed to load playbooks.')); })
      .finally(() => { if (mounted) setLoading(false); });
    return () => { mounted = false; };
  }, [t]);

  const active = activePlaybooksOf(playbooks);
  const available = availablePlaybooks(playbooks, value);
  const draft = available.some((p) => p.id === draftId) ? draftId : '';
  const modeOptions = MODES.map((mode) => ({ value: mode, label: t(PLAYBOOK_DELIVERY_MODES[mode].label), description: t(PLAYBOOK_DELIVERY_MODES[mode].description) }));

  const update = (index: number, patch: Partial<SelectedPlaybook>) => {
    if (disabled) return;
    onChange(value.map((item, i) => (i === index ? { ...item, ...patch } : item)));
  };

  return (
    <Section title={t('Playbooks')}>
      <View style={styles.body}>
        {error ? <AppText color="danger">{error}</AppText> : null}
        {loading ? <AppText muted>{t('Loading playbooks...')}</AppText> : null}
        {!loading && active.length === 0 && value.length === 0 ? (
          <View>
            <AppText variant="label">{t('No active playbooks found.')}</AppText>
            <AppText variant="caption" muted>{t('Create one from the Playbooks page, then return here to attach it.')}</AppText>
          </View>
        ) : null}
        {value.map((item, index) => {
          const playbook = playbooks.find((p) => p.id === item.playbookId);
          const options = playbookOptionsForRow(playbooks, value, item.playbookId).map((p) => ({ value: p.id, label: p.fileName, description: p.description }));
          if (!playbook) options.push({ value: item.playbookId, label: t('{{id}} (Unavailable)', { id: item.playbookId }), description: null });
          return (
            <View key={`${item.playbookId}-${index}`} style={styles.row}>
              <View style={styles.flex}>
                <SelectField
                  label={t('Playbook {{index}}', { index: index + 1 })}
                  value={item.playbookId}
                  options={options}
                  onChange={(id) => { if (id) update(index, { playbookId: id }); }}
                  placeholder={t('Select a playbook...')}
                  closeLabel={t('Close')}
                  disabled={disabled}
                  testID={`${testID}-${index}`}
                />
                <SelectField
                  label={t('Delivery mode for playbook {{index}}', { index: index + 1 })}
                  value={item.deliveryMode}
                  options={modeOptions}
                  onChange={(mode) => update(index, { deliveryMode: mode as PlaybookDeliveryMode })}
                  placeholder={t('Delivery Mode')}
                  closeLabel={t('Close')}
                  disabled={disabled}
                  testID={`${testID}-${index}-mode`}
                />
              </View>
              <IconButton
                icon="trash-outline"
                color="danger"
                label={t('Remove playbook {{index}}', { index: index + 1 })}
                onPress={() => { if (!disabled) onChange(value.filter((_, i) => i !== index)); }}
                testID={`${testID}-${index}-remove`}
              />
            </View>
          );
        })}
        {available.length > 0 ? (
          <View>
            <SelectField
              label={t('Add Playbook')}
              value={draft}
              allowEmpty
              placeholder={t('Select a playbook...')}
              options={available.map((p) => ({ value: p.id, label: p.fileName, description: p.description }))}
              onChange={setDraftId}
              closeLabel={t('Close')}
              disabled={disabled}
              testID={`${testID}-add`}
            />
            <SelectField
              label={t('Delivery mode for new playbook')}
              value={draftMode}
              options={modeOptions}
              onChange={(mode) => setDraftMode(mode as PlaybookDeliveryMode)}
              placeholder={t('Delivery Mode')}
              closeLabel={t('Close')}
              disabled={disabled}
              testID={`${testID}-add-mode`}
            />
            <Button
              label={t('Add playbook')}
              icon="add"
              variant="secondary"
              disabled={disabled || !draft}
              onPress={() => {
                if (disabled || !draft) return;
                onChange([...value, { playbookId: draft, deliveryMode: draftMode }]);
                setDraftId('');
                setDraftMode(DEFAULT_PLAYBOOK_DELIVERY_MODE);
              }}
              testID={`${testID}-add-submit`}
            />
          </View>
        ) : null}
      </View>
    </Section>
  );
}

const styles = StyleSheet.create({
  body: { padding: spacing.md, gap: spacing.sm },
  row: { flexDirection: 'row', alignItems: 'flex-start', gap: spacing.xs },
  flex: { flex: 1 },
});
