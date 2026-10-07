import type { Captain } from '../../../types/models';
import { useLocale } from '../../../context/LocaleContext';
import CaptainPicker from '../../shared/CaptainPicker';
import { isCaptainAvailable, validateCategorization, type CategorizationOptions } from '../../../lib/vesselImport';

export { EMPTY_CATEGORIZATION, isCaptainAvailable, validateCategorization, type CategorizationOptions } from '../../../lib/vesselImport';

interface ImportCategorizationOptionsProps {
  value: CategorizationOptions;
  onChange: (value: CategorizationOptions) => void;
  captains: Captain[];
  captainsLoading?: boolean;
  defaultPrompt: string;
  defaultPromptError?: string;
  timeoutMinutes?: number;
  /** Show validation errors (after the operator tried to import). */
  showErrors?: boolean;
}

/**
 * Review-step section that asks a captain to recommend fleets after the import: an opt-in checkbox, a captain
 * picker (only idle captains can be chosen), editable instructions pre-filled from the import.fleet_categorization
 * prompt template with a reset link, and an "apply automatically" switch. Explains that the analysis runs in the
 * background after the vessels are created.
 */
export default function ImportCategorizationOptions({
  value,
  onChange,
  captains,
  captainsLoading = false,
  defaultPrompt,
  defaultPromptError,
  timeoutMinutes,
  showErrors = false,
}: ImportCategorizationOptionsProps) {
  const { t } = useLocale();
  const errors = validateCategorization(value);
  const availableCount = captains.filter(isCaptainAvailable).length;
  const selectedCaptain = captains.find((c) => c.id === value.captainId) ?? null;
  const promptIsDefault = value.prompt === defaultPrompt;

  return (
    <fieldset className="form-fieldset import-categorization">
      <legend className="form-label">{t('Fleet recommendations')}</legend>
      <label className="checkbox-row">
        <input
          type="checkbox"
          checked={value.enabled}
          onChange={(e) => onChange({ ...value, enabled: e.target.checked, prompt: value.prompt || defaultPrompt })}
        />
        <span>{t('Recommend fleets with a captain')}</span>
      </label>
      <p className="text-dim form-help">
        {t('After the vessels are created, a captain reads every imported repository, works out what each one does, and suggests a set of fleets. It runs in the background; you can review and edit the fleets before applying them.')}
      </p>

      {value.enabled && (
        <div className="import-categorization-body">
          <span className="form-label">{t('Captain')}</span>
          <CaptainPicker
            id="import-categorization-captain"
            captains={captains}
            value={value.captainId}
            onChange={(captainId) => onChange({ ...value, captainId })}
            autoLabel={captainsLoading ? t('Loading captains...') : t('Select a captain')}
            ariaLabel={t('Captain that recommends fleets')}
            showState
            isOptionDisabled={(c) => !isCaptainAvailable(c)}
            invalid={showErrors && !!errors.captain}
            describedBy="import-categorization-captain-help"
          />
          {showErrors && errors.captain
            ? <span id="import-categorization-captain-help" className="field-error" role="alert">{t(errors.captain)}</span>
            : (
              <span id="import-categorization-captain-help" className="text-dim form-help">
                {captains.length === 0 && !captainsLoading
                  ? t('No captains yet. Create one under Captains first.')
                  : availableCount === 0
                    ? t('Every captain is busy right now. Only idle captains can be chosen.')
                    : t('Only idle captains can be chosen; busy ones are listed but disabled.')}
                {selectedCaptain && selectedCaptain.model ? <> {' '}{t('Model: {{model}}', { model: selectedCaptain.model })}</> : null}
              </span>
            )}

          <div className="form-field">
            <span className="form-label-row">
              <label className="form-label" htmlFor="import-categorization-prompt">{t('Instructions for the captain')}</label>
              <button
                type="button"
                className="btn-link"
                onClick={() => onChange({ ...value, prompt: defaultPrompt })}
                disabled={promptIsDefault || !defaultPrompt}
              >
                {t('Reset to default')}
              </button>
            </span>
            <textarea
              id="import-categorization-prompt"
              rows={7}
              value={value.prompt}
              onChange={(e) => onChange({ ...value, prompt: e.target.value })}
              aria-invalid={showErrors && !!errors.prompt}
              aria-describedby="import-categorization-prompt-help"
            />
            {showErrors && errors.prompt && <span className="field-error" role="alert">{t(errors.prompt)}</span>}
            <span id="import-categorization-prompt-help" className="text-dim form-help">
              {defaultPromptError
                ? t('The default instructions could not be loaded: {{error}}', { error: defaultPromptError })
                : t('Pre-filled from the import.fleet_categorization prompt (Configuration > Prompts). Armada always adds the output format, so editing this text cannot break the result.')}
              {timeoutMinutes ? <> {' '}{t('{count, plural, one {The captain has up to # minute.} other {The captain has up to # minutes.}}', { count: timeoutMinutes })}</> : null}
            </span>
          </div>

          <label className="checkbox-row">
            <input type="checkbox" checked={value.applyAutomatically} onChange={(e) => onChange({ ...value, applyAutomatically: e.target.checked })} />
            <span>{t('Apply recommendations automatically')}</span>
          </label>
          <p className="text-dim form-help">
            {value.applyAutomatically
              ? t('Fleets are created (or reused by name) and vessels assigned as soon as the captain finishes.')
              : t('You review, edit, and apply the recommended fleets yourself when the captain finishes.')}
          </p>
        </div>
      )}
    </fieldset>
  );
}
