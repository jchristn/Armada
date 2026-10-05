import { useState } from 'react';
import type { CliPermissionPolicy } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import ConfirmDialog from '../shared/ConfirmDialog';
import { bypassWarning, policyLabel } from '../../lib/cliPermissions';

interface CliPermissionPolicySelectProps {
  id?: string;
  /** Current value; null means Inherit. */
  value: CliPermissionPolicy | null;
  onChange: (value: CliPermissionPolicy | null) => void;
  /** Offer Inherit (captains and threads); server defaults have no Inherit. */
  allowInherit?: boolean;
  /** Offer Bypass (admins only). A current Bypass value is always shown so the select reflects the stored state. */
  allowBypass: boolean;
  disabled?: boolean;
  /** Label for Inherit, e.g. "Inherit (Approve in Armada)". */
  inheritLabel?: string;
  ariaLabel?: string;
  ariaDescribedBy?: string;
  className?: string;
}

/**
 * Picker for a CLI tool permission policy (Inherit / Refuse / Approve in Armada / Bypass). Choosing Bypass first shows
 * a strong warning that must be confirmed; cancelling keeps the previous value.
 */
export default function CliPermissionPolicySelect({
  id, value, onChange, allowInherit = true, allowBypass, disabled, inheritLabel, ariaLabel, ariaDescribedBy, className,
}: CliPermissionPolicySelectProps) {
  const { t } = useLocale();
  const [confirmBypass, setConfirmBypass] = useState(false);
  const showBypass = allowBypass || value === 'Bypass';

  function select(raw: string) {
    const next = raw ? (raw as CliPermissionPolicy) : null;
    if (next === value) return;
    if (next === 'Bypass') {
      if (!allowBypass) return;
      setConfirmBypass(true);
      return;
    }
    onChange(next);
  }

  return (
    <>
      <select
        id={id}
        className={className}
        value={value ?? ''}
        disabled={disabled}
        onChange={(e) => select(e.target.value)}
        aria-label={ariaLabel}
        aria-describedby={ariaDescribedBy}
      >
        {allowInherit && <option value="">{inheritLabel ?? t('Inherit')}</option>}
        <option value="Refuse">{policyLabel(t, 'Refuse')}</option>
        <option value="ApproveInArmada">{policyLabel(t, 'ApproveInArmada')}</option>
        {showBypass && <option value="Bypass" disabled={!allowBypass}>{policyLabel(t, 'Bypass')}</option>}
      </select>
      <ConfirmDialog
        open={confirmBypass}
        title={t('Allow every CLI tool call without asking?')}
        message={bypassWarning(t)}
        confirmLabel={t('Use Bypass')}
        cancelLabel={t('Cancel')}
        danger
        onConfirm={() => { setConfirmBypass(false); onChange('Bypass'); }}
        onCancel={() => setConfirmBypass(false)}
      />
    </>
  );
}
