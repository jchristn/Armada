import type { Captain, CaptainTier } from '@dashboard/types/models';
import { SelectField } from '../../../components/ui';
import type { Translate } from '../../../i18n/LocaleContext';
import { useLocale } from '../../../i18n/LocaleContext';

/** Fallback tiers in the dashboard's order. */
export const CAPTAIN_TIERS: CaptainTier[] = ['Economy', 'Standard', 'Premium'];

/** Pure: the dashboard CaptainPicker's option text ("name - Tier (runtime)"). */
export function captainOptionLabel(captain: Captain, t: Translate): string {
  return `${captain.name}${captain.tier ? ` - ${t(captain.tier)}` : ''}${captain.runtime ? ` (${captain.runtime})` : ''}`;
}

/** The dashboard's CaptainPicker: a preferred captain, or Auto (default routing). */
export function CaptainPickerField({ label, captains, value, onChange, disabled, testID }: {
  label: string;
  captains: Captain[];
  value: string | null;
  onChange: (captainId: string | null) => void;
  disabled?: boolean;
  testID?: string;
}) {
  const { t } = useLocale();
  return (
    <SelectField
      label={label}
      value={value ?? ''}
      onChange={(v) => onChange(v || null)}
      allowEmpty
      placeholder={t('Auto (default routing)')}
      closeLabel={t('Close')}
      searchLabel={t('Search captains')}
      options={captains.map((c) => ({ value: c.id, label: captainOptionLabel(c, t) }))}
      disabled={disabled}
      testID={testID}
    />
  );
}

/** The dashboard's FallbackTierSelect: the tier to fall back to when the preferred captain is busy, or Auto. */
export function FallbackTierField({ label, value, onChange, disabled, testID }: {
  label: string;
  value: CaptainTier | null;
  onChange: (tier: CaptainTier | null) => void;
  disabled?: boolean;
  testID?: string;
}) {
  const { t } = useLocale();
  return (
    <SelectField
      label={label}
      value={value ?? ''}
      onChange={(v) => onChange(v ? (v as CaptainTier) : null)}
      allowEmpty
      placeholder={t('Auto')}
      closeLabel={t('Close')}
      options={CAPTAIN_TIERS.map((tier) => ({ value: tier, label: t(tier) }))}
      disabled={disabled}
      testID={testID}
    />
  );
}
