import { useLocale } from '../../context/LocaleContext';
import { DEFAULT_GLOBAL_LANDING_MODE, findLandingMode, getGlobalLandingModes } from '../../lib/vesselForm';

interface DefaultLandingModeFieldProps {
  value: string | null | undefined;
  onChange: (mode: string) => void;
}

/** The global default landing mode picker (every mode, no inherit entry) shared by the server settings pages. */
export default function DefaultLandingModeField({ value, onChange }: DefaultLandingModeFieldProps) {
  const { t } = useLocale();
  const modes = getGlobalLandingModes(t);
  const help = t('How finished missions land when neither the vessel nor the voyage sets a landing mode (default Merge and Push).');
  const current = value || DEFAULT_GLOBAL_LANDING_MODE;
  return (
    <div className="form-group">
      <label title={help}>{t('Default Landing Mode')}</label>
      <select aria-label={t('Default Landing Mode')} title={help} value={current} onChange={(e) => onChange(e.target.value)}>
        {modes.map((m) => <option key={m.value} value={m.value}>{m.label}</option>)}
      </select>
      <small className="text-dim">{findLandingMode(modes, current).description}</small>
      <small className="text-dim">{help}</small>
    </div>
  );
}
