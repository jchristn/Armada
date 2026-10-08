import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { updateVessel } from '@dashboard/api/client';
import type { Vessel } from '@dashboard/types/models';
import { ActionRow } from '../../build/fields';
import { errorMessage } from '../../build/useLiveResource';
import { AppText, Banner, BottomSheet, Button, TextField } from '../../components/ui';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { spacing, typography } from '../../theme/typography';

export type ContextField = 'projectContext' | 'styleGuide' | 'modelContext';

export interface WorkspaceContextSheetProps {
  vessel: Vessel;
  /** True when files are selected (or a file is open) so "Append Selection" can work. */
  canAppend: boolean;
  /** Builds the "## Workspace Selection" snippet of the selected files (null when no text file was usable). */
  buildSnippet: () => Promise<string | null>;
  onSaved: (vessel: Vessel) => void;
  onClose: () => void;
}

/** Vessel context editor (the dashboard's Workspace context modal): edit the three fields or append the selection. */
export function WorkspaceContextSheet({ vessel, canAppend, buildSnippet, onSaved, onClose }: WorkspaceContextSheetProps) {
  const { t } = useLocale();
  const { pushToast } = useNotifications();
  const [drafts, setDrafts] = useState<Record<ContextField, string>>({
    projectContext: vessel.projectContext || '',
    styleGuide: vessel.styleGuide || '',
    modelContext: vessel.modelContext || '',
  });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  async function append(field: ContextField) {
    try {
      const snippet = await buildSnippet();
      if (!snippet) {
        pushToast('warning', t('No text files were available for context curation.'));
        return;
      }
      setDrafts((cur) => ({ ...cur, [field]: cur[field].trim() ? `${cur[field].trim()}\n\n${snippet}` : snippet }));
    } catch (e) {
      setError(errorMessage(e) || t('Failed to build context from selection.'));
    }
  }

  async function save() {
    setSaving(true);
    try {
      const updated = await updateVessel(vessel.id, { projectContext: drafts.projectContext, styleGuide: drafts.styleGuide, modelContext: drafts.modelContext });
      pushToast('success', t('Saved vessel context.'));
      onSaved(updated);
    } catch (e) {
      setError(errorMessage(e) || t('Failed to save vessel context.'));
    } finally {
      setSaving(false);
    }
  }

  const fields: [ContextField, string][] = [['projectContext', t('Project Context')], ['styleGuide', t('Style Guide')], ['modelContext', t('Model Context')]];
  return (
    <BottomSheet open title={t('Vessel Context')} onClose={() => { if (!saving) onClose(); }} closeLabel={t('Close')} testID="workspace-context">
      <AppText muted style={styles.gap}>{t('Edit the vessel context fields directly, or append the current Workspace selection into any field.')}</AppText>
      {error ? <Banner tone="danger" title={error} /> : null}
      {fields.map(([field, label]) => (
        <View key={field}>
          <TextField
            label={label}
            value={drafts[field]}
            onChangeText={(v) => setDrafts((cur) => ({ ...cur, [field]: v }))}
            multiline
            autoCapitalize="none"
            autoCorrect={false}
            testID={`workspace-context-${field}`}
          />
          <Button label={t('Append Selection')} variant="ghost" disabled={!canAppend || saving} onPress={() => void append(field)} style={styles.append} />
        </View>
      ))}
      <ActionRow>
        <Button label={t('Cancel')} variant="ghost" disabled={saving} onPress={onClose} />
        <Button label={saving ? t('Saving...') : t('Save Context')} busy={saving} onPress={() => void save()} testID="workspace-context-save" />
      </ActionRow>
    </BottomSheet>
  );
}

const styles = StyleSheet.create({
  gap: { marginBottom: spacing.md },
  append: { alignSelf: 'flex-start', marginTop: -spacing.md },
  mono: typography.mono,
});
