import type { Captain } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';

interface CaptainPickerProps {
  captains: Captain[];
  value: string | null | undefined;
  onChange: (captainId: string | null) => void;
  /** Label for the empty option that leaves the choice on normal routing. */
  autoLabel?: string;
  disabled?: boolean;
  id?: string;
  ariaLabel?: string;
  className?: string;
  /** When true, append each captain's state (Idle, Working, ...) to its option. */
  showState?: boolean;
  /** Options for which this returns true are rendered disabled (for example captains that are not idle). */
  isOptionDisabled?: (captain: Captain) => boolean;
  /** Extra attributes for validation wiring. */
  invalid?: boolean;
  describedBy?: string;
}

/**
 * Dropdown for choosing a preferred captain. Shows each captain's name, tier, and runtime so the operator can
 * pick by capability, and offers an explicit "auto" option that leaves the step on normal persona/tier
 * routing. All labels are localized; the picker is a plain select so it stays keyboard-accessible.
 */
export default function CaptainPicker({ captains, value, onChange, autoLabel, disabled, id, ariaLabel, className, showState, isOptionDisabled, invalid, describedBy }: CaptainPickerProps) {
  const { t } = useLocale();

  return (
    <select
      id={id}
      className={`captain-picker${className ? ` ${className}` : ''}`}
      value={value ?? ''}
      disabled={disabled}
      aria-label={ariaLabel ?? t('Preferred captain')}
      aria-invalid={invalid || undefined}
      aria-describedby={describedBy}
      onChange={(event) => onChange(event.target.value ? event.target.value : null)}
    >
      <option value="">{autoLabel ?? t('Auto (default routing)')}</option>
      {captains.map((captain) => (
        <option key={captain.id} value={captain.id} disabled={isOptionDisabled ? isOptionDisabled(captain) : undefined}>
          {captain.name}
          {captain.tier ? ` - ${t(captain.tier)}` : ''}
          {captain.runtime ? ` (${captain.runtime})` : ''}
          {showState && captain.state ? ` - ${t(captain.state)}` : ''}
        </option>
      ))}
    </select>
  );
}
