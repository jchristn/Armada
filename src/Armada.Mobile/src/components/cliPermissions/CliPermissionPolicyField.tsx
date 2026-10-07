import { useState } from 'react';
import type { CliPermissionPolicy } from '@dashboard/types/models';
import { bypassWarning, policyLabel } from '@dashboard/lib/cliPermissions';
import { useLocale } from '../../i18n/LocaleContext';
import { ConfirmDialog } from '../ui/ConfirmDialog';
import { SelectField, type SelectOption } from '../ui/SelectSheet';

type PolicyValue = CliPermissionPolicy | '';

interface CliPermissionPolicyFieldProps {
  label: string;
  /** Current value; null means Inherit. */
  value: CliPermissionPolicy | null;
  onChange: (value: CliPermissionPolicy | null) => void;
  /** Offer Bypass (admins). A current Bypass value is always shown so the field reflects the stored state. */
  allowBypass: boolean;
  disabled?: boolean;
  hint?: string | null;
  testID?: string;
}

/**
 * Picker for a CLI tool permission policy (Inherit / Refuse / Approve in Armada / Bypass), the dashboard's
 * CliPermissionPolicySelect. Choosing Bypass first shows the strong warning, which must be confirmed.
 */
export function CliPermissionPolicyField({ label, value, onChange, allowBypass, disabled, hint, testID }: CliPermissionPolicyFieldProps) {
  const { t } = useLocale();
  const [confirmBypass, setConfirmBypass] = useState(false);
  const options: SelectOption<PolicyValue>[] = [
    { value: '', label: t('Inherit') },
    { value: 'Refuse', label: policyLabel(t, 'Refuse') },
    { value: 'ApproveInArmada', label: policyLabel(t, 'ApproveInArmada') },
  ];
  if (allowBypass || value === 'Bypass') options.push({ value: 'Bypass', label: policyLabel(t, 'Bypass'), disabled: !allowBypass });

  function select(raw: PolicyValue) {
    const next = raw ? raw : null;
    if (next === value) return;
    if (next === 'Bypass') {
      if (allowBypass) setConfirmBypass(true);
      return;
    }
    onChange(next);
  }

  return (
    <>
      <SelectField label={label} value={value ?? ''} options={options} onChange={select} closeLabel={t('Close')} disabled={disabled} hint={hint} testID={testID} />
      <ConfirmDialog
        open={confirmBypass}
        title={t('Allow every CLI tool call without asking?')}
        message={bypassWarning(t)}
        confirmLabel={t('Use Bypass')}
        cancelLabel={t('Cancel')}
        danger
        onConfirm={() => { setConfirmBypass(false); onChange('Bypass'); }}
        onCancel={() => setConfirmBypass(false)}
        testID="cli-bypass-confirm"
      />
    </>
  );
}
