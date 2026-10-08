import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { listPlaybooks } from '@dashboard/api/client';
import type { PlaybookDeliveryMode, SelectedPlaybook } from '@dashboard/types/models';
import { useLiveResource } from '../../build/useLiveResource';
import { AppText, Button, IconButton } from '../../components/ui';
import { SelectField, type SelectOption } from '../../components/ui/SelectSheet';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing } from '../../theme/typography';

/** The dashboard's delivery mode copy (PlaybookSelector), translated at render time. */
export const DELIVERY_MODES: { value: PlaybookDeliveryMode; label: string; description: string }[] = [
  { value: 'InlineFullContent', label: 'Inline Full Content', description: 'Inject the complete markdown into the mission instructions.' },
  { value: 'InstructionWithReference', label: 'Instruction With Reference', description: 'Tell the model to read the materialized playbook path outside the worktree.' },
  { value: 'AttachIntoWorktree', label: 'Attach Into Worktree', description: 'Materialize the playbook in `.armada/playbooks/` and instruct the model to read it there.' },
];

export interface PlaybookPickerProps {
  value: SelectedPlaybook[];
  onChange: (next: SelectedPlaybook[]) => void;
  disabled?: boolean;
  testID?: string;
}

/**
 * The dashboard's PlaybookSelector: attach active playbooks, each with a delivery mode; a playbook can be attached
 * once. Selected rows change their mode in place or are removed.
 */
export function PlaybookPicker({ value, onChange, disabled, testID = 'playbooks' }: PlaybookPickerProps) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const playbooks = useLiveResource(async () => (await listPlaybooks({ pageSize: 9999 })).objects ?? [], []);
  const [draftId, setDraftId] = useState('');
  const [draftMode, setDraftMode] = useState<PlaybookDeliveryMode>('InlineFullContent');

  const all = playbooks.data ?? [];
  const active = all.filter((p) => p.active !== false);
  const selectedIds = new Set(value.map((v) => v.playbookId));
  const available = active.filter((p) => !selectedIds.has(p.id));
  const modeOptions: SelectOption<PlaybookDeliveryMode>[] = DELIVERY_MODES.map((m) => ({ value: m.value, label: t(m.label), description: t(m.description) }));
  const nameOf = (id: string) => all.find((p) => p.id === id)?.fileName ?? t('{{id}} (Unavailable)', { id });

  const add = () => {
    if (disabled || !draftId) return;
    onChange([...value, { playbookId: draftId, deliveryMode: draftMode }]);
    setDraftId('');
    setDraftMode('InlineFullContent');
  };

  return (
    <View testID={testID}>
      <AppText variant="label" style={styles.heading}>{t('Playbooks')}</AppText>
      {playbooks.loading ? <AppText muted>{t('Loading playbooks...')}</AppText> : null}
      {playbooks.error ? <AppText color="danger">{playbooks.error}</AppText> : null}
      {!playbooks.loading && !playbooks.error && active.length === 0 ? (
        <AppText variant="caption" muted style={styles.gap}>
          {`${t('No active playbooks found.')} ${t('Create one from the Playbooks page, then return here to attach it.')}`}
        </AppText>
      ) : null}
      {value.map((item, index) => (
        <View key={item.playbookId} style={[styles.row, { borderColor: colors.border }]} testID={`${testID}-item-${index}`}>
          <View style={styles.rowHead}>
            <AppText variant="label" style={styles.flex} numberOfLines={1}>{nameOf(item.playbookId)}</AppText>
            <IconButton
              icon="trash-outline"
              color="danger"
              label={t('Remove playbook {{index}}', { index: index + 1 })}
              onPress={() => { if (!disabled) onChange(value.filter((_, i) => i !== index)); }}
              testID={`${testID}-remove-${index}`}
            />
          </View>
          <SelectField
            label={t('Delivery mode for playbook {{index}}', { index: index + 1 })}
            value={item.deliveryMode}
            options={modeOptions}
            onChange={(mode) => onChange(value.map((v, i) => (i === index ? { ...v, deliveryMode: mode } : v)))}
            closeLabel={t('Close')}
            disabled={disabled}
          />
        </View>
      ))}
      {available.length > 0 ? (
        <View style={[styles.row, { borderColor: colors.border }]}>
          <SelectField
            label={t('Add Playbook')}
            value={draftId}
            options={[{ value: '', label: t('Select a playbook...') }, ...available.map((p) => ({ value: p.id, label: p.fileName, description: p.description ?? undefined }))]}
            onChange={setDraftId}
            closeLabel={t('Close')}
            disabled={disabled}
            testID={`${testID}-add-select`}
          />
          <SelectField
            label={t('Delivery mode for new playbook')}
            value={draftMode}
            options={modeOptions}
            onChange={setDraftMode}
            closeLabel={t('Close')}
            disabled={disabled}
          />
          <Button label={t('Add Playbook')} variant="secondary" icon="add" onPress={add} disabled={disabled || !draftId} testID={`${testID}-add`} />
        </View>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  heading: { marginBottom: spacing.sm },
  gap: { marginBottom: spacing.md },
  row: { borderWidth: StyleSheet.hairlineWidth, borderRadius: radius.md, padding: spacing.md, marginBottom: spacing.md },
  rowHead: { flexDirection: 'row', alignItems: 'center' },
});
