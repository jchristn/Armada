import { useState } from 'react';
import { importObjectiveFromGitHub } from '@dashboard/api/client';
import type { GitHubObjectiveSourceType, Objective, Vessel } from '@dashboard/types/models';
import { AppText, BottomSheet, Button, TextField } from '../../components/ui';
import { SelectField } from '../../components/ui/SelectSheet';
import { errorMessage } from '../../build/useLiveResource';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';

/** The GitHub number field's value as a positive whole number, or null (the dashboard's validation). */
export function parseGitHubNumber(value: string): number | null {
  const parsed = Number(value.trim());
  return value.trim() && Number.isFinite(parsed) && parsed > 0 ? parsed : null;
}

/** Import GitHub Backlog Item: create a backlog item from an issue or pull request of a vessel's repository. */
export function GitHubImportSheet({ open, onClose, vessels, onImported }: { open: boolean; onClose: () => void; vessels: Vessel[]; onImported: (objective: Objective) => void }) {
  const { t } = useLocale();
  const { pushToast } = useNotifications();
  const [vesselId, setVesselId] = useState('');
  const [sourceType, setSourceType] = useState<GitHubObjectiveSourceType>('Issue');
  const [number, setNumber] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit() {
    const parsed = parseGitHubNumber(number);
    if (!vesselId || parsed === null) {
      setError(t('Select a vessel and enter a valid GitHub issue or pull-request number.'));
      return;
    }
    setSaving(true);
    setError(null);
    try {
      const imported = await importObjectiveFromGitHub({ vesselId, sourceType, number: parsed });
      pushToast('success', t('Imported GitHub {{sourceType}} #{{number}} into backlog item "{{title}}".', { sourceType, number: parsed, title: imported.title }));
      setVesselId('');
      setSourceType('Issue');
      setNumber('');
      onImported(imported);
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setSaving(false);
    }
  }

  return (
    <BottomSheet open={open} title={t('Import GitHub Backlog Item')} onClose={() => { if (!saving) onClose(); }} closeLabel={t('Cancel')} testID="objective-import">
      <AppText muted style={{ marginBottom: 12 }}>
        {t('Create a backlog item from a GitHub issue or pull request using the selected vessel repository and configured GitHub token.')}
      </AppText>
      <SelectField
        label={t('Vessel')}
        value={vesselId}
        options={[{ value: '', label: t('Select a vessel') }, ...vessels.map((v) => ({ value: v.id, label: v.name }))]}
        onChange={setVesselId}
        closeLabel={t('Close')}
        testID="objective-import-vessel"
      />
      <SelectField
        label={t('Source Type')}
        value={sourceType}
        options={[{ value: 'Issue', label: t('Issue') }, { value: 'PullRequest', label: t('Pull Request') }]}
        onChange={setSourceType}
        closeLabel={t('Close')}
        testID="objective-import-type"
      />
      <TextField label={t('Number')} value={number} onChangeText={setNumber} keyboardType="number-pad" placeholder="123" error={error} testID="objective-import-number" />
      <Button label={saving ? t('Importing...') : t('Import')} busy={saving} onPress={() => void submit()} testID="objective-import-submit" />
    </BottomSheet>
  );
}
