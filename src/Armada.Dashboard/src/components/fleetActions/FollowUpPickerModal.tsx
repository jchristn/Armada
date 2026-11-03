import { useEffect, useState } from 'react';
import type { FleetAction } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import DialogShell from '../shared/DialogShell';
import { KIND_LABELS, isReportOnly } from '../../lib/fleetActionLabels';

interface FollowUpPickerModalProps {
  open: boolean;
  /** Run being followed up, shown in the subtitle. */
  runId: string;
  /** Actions paired with the run's action (fixes for a scan), listed first. */
  suggested: FleetAction[];
  /** Every other action. */
  others: FleetAction[];
  loading?: boolean;
  onClose: () => void;
  onPick: (actionId: string) => void;
}

/** Choose the action for a follow-up run: suggested fixes first, then all other actions. */
export default function FollowUpPickerModal({ open, runId, suggested, others, loading = false, onClose, onPick }: FollowUpPickerModalProps) {
  const { t } = useLocale();
  const [selected, setSelected] = useState('');

  useEffect(() => {
    if (!open) return;
    setSelected((suggested[0] ?? others[0])?.id ?? '');
  }, [open, suggested, others]);

  const option = (a: FleetAction) => (
    <label key={a.id} className={`follow-up-option${selected === a.id ? ' active' : ''}`} title={a.description ?? undefined}>
      <input type="radio" name="follow-up-action" checked={selected === a.id} onChange={() => setSelected(a.id)} />
      <span className="follow-up-option-name">{a.name}</span>
      <span className="text-dim">{t(KIND_LABELS[a.kind])}{isReportOnly(a) ? `, ${t('Report-only')}` : ''}</span>
    </label>
  );

  return (
    <DialogShell
      open={open}
      onClose={onClose}
      title={t('Follow up')}
      subtitle={t('Pick an action to run on vessels from run {{id}}.', { id: runId })}
      footer={(
        <>
          <button type="button" className="btn" onClick={onClose} title={t('Close without running anything.')}>{t('Cancel')}</button>
          <button type="button" className="btn btn-primary" onClick={() => onPick(selected)} disabled={!selected} title={t('Choose the target statuses and review the run. Nothing runs yet.')}>{t('Continue')}</button>
        </>
      )}
    >
      {loading && <p className="text-dim">{t('Loading...')}</p>}
      {!loading && suggested.length === 0 && others.length === 0 && <p className="text-dim">{t('No saved actions')}</p>}
      <div className="follow-up-options" role="radiogroup" aria-label={t('Action')}>
        {suggested.length > 0 && (
          <>
            <div className="form-label" title={t('Actions paired with this run\'s action, such as a fix for a scan.')}>{t('Suggested')}</div>
            {suggested.map(option)}
          </>
        )}
        {others.length > 0 && (
          <>
            <div className="form-label">{suggested.length > 0 ? t('Other actions') : t('All actions')}</div>
            {others.map(option)}
          </>
        )}
      </div>
    </DialogShell>
  );
}
