import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import type { BlackBoxResolution, BlackBoxResolveRequest } from '@dashboard/types/blackbox';
import { BottomSheet, Button, SelectField, TextField } from '../../components/ui';
import { useLocale } from '../../i18n/LocaleContext';
import { spacing } from '../../theme/typography';
import { BLACKBOX_NOTE_MAX, BLACKBOX_RESOLUTIONS, spaced } from './blackBoxModel';

/** Resolve an entry: the resolution, an optional note and an optional link (a commit or pull request). */
export function ResolveSheet({ open, busy, onClose, onSubmit }: {
  open: boolean;
  busy: boolean;
  onClose: () => void;
  onSubmit: (request: BlackBoxResolveRequest) => void;
}) {
  const { t } = useLocale();
  return (
    <BottomSheet open={open} title={t('Resolve Black Box Entry')} onClose={onClose} closeLabel={t('Close')} testID="bbx-resolve-sheet">
      {open ? <ResolveForm busy={busy} onClose={onClose} onSubmit={onSubmit} /> : null}
    </BottomSheet>
  );
}

function ResolveForm({ busy, onClose, onSubmit }: { busy: boolean; onClose: () => void; onSubmit: (request: BlackBoxResolveRequest) => void }) {
  const { t } = useLocale();
  const [resolution, setResolution] = useState<BlackBoxResolution>('Resolved');
  const [note, setNote] = useState('');
  const [link, setLink] = useState('');
  return (
    <View>
      <SelectField
        label={t('Resolution')}
        value={resolution}
        onChange={(value) => setResolution(value as BlackBoxResolution)}
        placeholder={t('Select resolution...')}
        closeLabel={t('Close')}
        options={BLACKBOX_RESOLUTIONS.map((r) => ({ value: r, label: t(spaced(r)) }))}
        testID="bbx-resolve-resolution"
      />
      <TextField label={t('Note')} value={note} onChangeText={setNote} multiline maxLength={BLACKBOX_NOTE_MAX} testID="bbx-resolve-note" />
      <TextField label={t('Link')} value={link} onChangeText={setLink} autoCapitalize="none" autoCorrect={false} hint={t('A commit, pull request or issue (optional)')} testID="bbx-resolve-link" />
      <View style={styles.actions}>
        <Button label={t('Cancel')} variant="ghost" onPress={onClose} />
        <Button
          label={t('Resolve')}
          busy={busy}
          disabled={busy}
          onPress={() => onSubmit({ resolution, note: note.trim() || null, link: link.trim() || null })}
          testID="bbx-resolve-submit"
        />
      </View>
    </View>
  );
}

/** Add a note to an entry (1 to 8000 characters; the server records the author as Operator). */
export function NoteSheet({ open, busy, onClose, onSubmit }: { open: boolean; busy: boolean; onClose: () => void; onSubmit: (body: string) => void }) {
  const { t } = useLocale();
  return (
    <BottomSheet open={open} title={t('Add Note')} onClose={onClose} closeLabel={t('Close')} testID="bbx-note-sheet">
      {open ? <NoteForm busy={busy} onClose={onClose} onSubmit={onSubmit} /> : null}
    </BottomSheet>
  );
}

function NoteForm({ busy, onClose, onSubmit }: { busy: boolean; onClose: () => void; onSubmit: (body: string) => void }) {
  const { t } = useLocale();
  const [body, setBody] = useState('');
  const trimmed = body.trim();
  const tooLong = trimmed.length > BLACKBOX_NOTE_MAX;
  return (
    <View>
      <TextField
        label={t('Note')}
        value={body}
        onChangeText={setBody}
        multiline
        error={tooLong ? t('Notes are at most {{max}} characters.', { max: BLACKBOX_NOTE_MAX }) : null}
        testID="bbx-note-body"
      />
      <View style={styles.actions}>
        <Button label={t('Cancel')} variant="ghost" onPress={onClose} />
        <Button label={t('Add Note')} busy={busy} disabled={busy || !trimmed || tooLong} onPress={() => onSubmit(trimmed)} testID="bbx-note-submit" />
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  actions: { flexDirection: 'row', justifyContent: 'flex-end', gap: spacing.sm },
});
