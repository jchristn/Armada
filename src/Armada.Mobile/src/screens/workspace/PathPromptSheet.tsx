import { useState } from 'react';
import { ActionRow } from '../../build/fields';
import { BottomSheet, Button, TextField } from '../../components/ui';
import { useLocale } from '../../i18n/LocaleContext';

export interface PathPromptSheetProps {
  title: string;
  label: string;
  initialValue: string;
  confirmLabel: string;
  onSubmit: (value: string) => void;
  onClose: () => void;
  testID?: string;
}

/** Asks for a workspace path (new file, new folder, rename or move): the dashboard's window.prompt as a sheet. */
export function PathPromptSheet({ title, label, initialValue, confirmLabel, onSubmit, onClose, testID }: PathPromptSheetProps) {
  const { t } = useLocale();
  const [value, setValue] = useState(initialValue);
  const submit = () => { if (value.trim()) onSubmit(value.trim()); };
  return (
    <BottomSheet open title={title} onClose={onClose} closeLabel={t('Close')} testID={testID}>
      <TextField
        label={label}
        value={value}
        onChangeText={setValue}
        autoCapitalize="none"
        autoCorrect={false}
        autoFocus
        onSubmitEditing={submit}
        testID={testID ? `${testID}-input` : undefined}
      />
      <ActionRow>
        <Button label={t('Cancel')} variant="ghost" onPress={onClose} />
        <Button label={confirmLabel} disabled={!value.trim()} onPress={submit} testID={testID ? `${testID}-submit` : undefined} />
      </ActionRow>
    </BottomSheet>
  );
}
