import type { JudgeStrengthPolicy } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';

export const JUDGE_STRENGTH_TIP = 'A Judge must be at least as strong as the stages it reviews. Wait holds the Judge stage with a blocker until a strong enough captain is free; Proceed and flag runs it on a weaker captain and flags the review; Off routes the Judge like any other stage.';

export const JUDGE_STRENGTH_OPTIONS: ReadonlyArray<{ value: JudgeStrengthPolicy; label: string }> = [
  { value: 'Wait', label: 'Wait for a strong enough Judge' },
  { value: 'ProceedAndFlag', label: 'Proceed and flag' },
  { value: 'Off', label: 'Off' },
];

/** The label of a pipeline's Judge strength rule; null (pipelines stored before 1.5.0) reads as Off. */
export function judgeStrengthLabel(value: JudgeStrengthPolicy | null | undefined): string {
  return JUDGE_STRENGTH_OPTIONS.find(o => o.value === (value ?? 'Off'))?.label ?? 'Off';
}

interface JudgeStrengthFieldProps {
  value: JudgeStrengthPolicy;
  onChange: (value: JudgeStrengthPolicy) => void;
}

/** Select for a pipeline's Judge strength rule (RS-8.7), shared by the create and edit forms. */
export default function JudgeStrengthField({ value, onChange }: JudgeStrengthFieldProps) {
  const { t } = useLocale();
  return (
    <label title={t(JUDGE_STRENGTH_TIP)}>{t('Judge strength rule')}
      <select aria-label={t('Judge strength rule')} title={t(JUDGE_STRENGTH_TIP)} value={value} onChange={e => onChange(e.target.value as JudgeStrengthPolicy)}>
        {JUDGE_STRENGTH_OPTIONS.map(o => <option key={o.value} value={o.value}>{t(o.label)}</option>)}
      </select>
    </label>
  );
}
