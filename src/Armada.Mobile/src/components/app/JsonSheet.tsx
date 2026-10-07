import { useLocale } from '../../i18n/LocaleContext';
import { BottomSheet, CodeBlock } from '../ui';

/** The dashboard's "View JSON" modal: the raw object, pretty-printed and selectable. */
export function JsonSheet({ open, title, data, onClose }: { open: boolean; title: string; data: unknown; onClose: () => void }) {
  const { t } = useLocale();
  let text = '';
  try {
    text = JSON.stringify(data, null, 2) ?? '';
  } catch {
    text = String(data);
  }
  return (
    <BottomSheet open={open} title={title} onClose={onClose} closeLabel={t('Close')} testID="json-sheet">
      <CodeBlock text={text} testID="json-sheet-text" />
    </BottomSheet>
  );
}
