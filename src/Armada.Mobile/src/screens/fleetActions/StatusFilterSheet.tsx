import { BottomSheet, ListRow } from '../../components/ui';
import { useLocale } from '../../i18n/LocaleContext';

export interface StatusFilterOption<T extends string> {
  value: T;
  /** English source label (translated here). */
  label: string;
}

/** A status filter in a sheet ("All statuses" plus each status), the mobile form of the dashboard's status select. */
export function StatusFilterSheet<T extends string>({ open, title, value, options, onChange, onClose, testID }: {
  open: boolean;
  title: string;
  value: T | '';
  options: StatusFilterOption<T>[];
  onChange: (value: T | '') => void;
  onClose: () => void;
  testID?: string;
}) {
  const { t } = useLocale();
  const all: StatusFilterOption<T | ''>[] = [{ value: '', label: 'All statuses' }, ...options];
  return (
    <BottomSheet open={open} title={title} onClose={onClose} closeLabel={t('Close')} testID={testID}>
      {all.map((o) => (
        <ListRow
          key={o.value || 'all'}
          testID={testID ? `${testID}-${o.value || 'all'}` : undefined}
          title={t(o.label)}
          icon={o.value === value ? 'radio-button-on' : 'radio-button-off'}
          selected={o.value === value}
          onPress={() => { onChange(o.value); onClose(); }}
        />
      ))}
    </BottomSheet>
  );
}
