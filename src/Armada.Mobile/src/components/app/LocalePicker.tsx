import { useState } from 'react';
import { localeOptionLabel, getLocaleMeta } from '@dashboard/i18n/catalog';
import { useLocale } from '../../i18n/LocaleContext';
import { BottomSheet, Icon, ListRow } from '../ui';

/** The language row plus its picker sheet; lists every locale the catalog supports (beta locales are labelled). */
export function LocalePicker({ testID = 'locale-picker' }: { testID?: string }) {
  const { locale, setLocale, supportedLocales, catalog, t } = useLocale();
  const [open, setOpen] = useState(false);
  const current = getLocaleMeta(locale, catalog);
  return (
    <>
      <ListRow
        testID={testID}
        icon="language-outline"
        title={t('Language')}
        subtitle={localeOptionLabel(current)}
        onPress={() => setOpen(true)}
      />
      <BottomSheet open={open} title={t('Language')} onClose={() => setOpen(false)} closeLabel={t('Close')} testID={`${testID}-sheet`}>
        {supportedLocales.map((meta) => (
          <ListRow
            key={meta.code}
            testID={`${testID}-${meta.code}`}
            title={localeOptionLabel(meta)}
            subtitle={meta.label}
            selected={meta.code === locale}
            accessory={meta.code === locale ? <Icon name="checkmark" color="primary" /> : null}
            onPress={() => { setLocale(meta.code); setOpen(false); }}
          />
        ))}
      </BottomSheet>
    </>
  );
}
