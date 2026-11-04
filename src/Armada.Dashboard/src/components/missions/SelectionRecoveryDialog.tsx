import { useId, useState } from 'react';
import { isApiStatus, apiErrorMessage, authorizeCaptainSelectionRecovery, getCaptainSelection } from '../../api/client';
import { useLocale } from '../../context/LocaleContext';
import type { UsageSelectionRecovery } from '../../types/models';
import DialogShell from '../shared/DialogShell';
import RefreshButton from '../shared/RefreshButton';

interface Props { missionId: string; current?: UsageSelectionRecovery | null; onClose: () => void; onSaved: () => void }

/** Explicit, audited permissions for one pending mission, using the shared accessible dialog frame. */
export default function SelectionRecoveryDialog({ missionId, current, onClose, onSaved }: Props) {
  const { t } = useLocale();
  const formId = useId();
  const [baseline, setBaseline] = useState(current);
  const [conflict, setConflict] = useState(false);
  const [fallback, setFallback] = useState(current?.allowFallback ?? false);
  const [reserve, setReserve] = useState(current?.reserveException ?? false);
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  return <DialogShell open title={t('usageSelection.recovery.title')} subtitle={t('usageSelection.recovery.scope')} onClose={onClose} dismissible={!busy}
    className="selection-recovery-dialog" footer={<>
      <button type="button" className="btn" disabled={busy} onClick={onClose}>{t('Cancel')}</button>
      <button type="submit" form={formId} className="btn btn-primary" disabled={busy || conflict || !reason.trim()}>{t(busy ? 'Saving...' : 'usageSelection.recovery.save')}</button>
    </>}>
    <form id={formId} onSubmit={async e => {
      e.preventDefault(); if (busy || conflict || !reason.trim()) return;
      setBusy(true); setError('');
      try {
        await authorizeCaptainSelectionRecovery(missionId, { expectedRevision: baseline?.revision ?? 0, allowFallback: fallback, reserveException: reserve, reason: reason.trim() });
        onSaved(); onClose();
      } catch (failure) {
        if ([401, 403, 404].some(code => isApiStatus(failure, code))) { onSaved(); onClose(); }
        else { setConflict(isApiStatus(failure, 409)); setError(isApiStatus(failure, 409) ? t('usageSelection.recovery.conflict') : apiErrorMessage(failure, t('usageSelection.saveFailed'))); }
      } finally { setBusy(false); }
    }}>
      {error && <div className="selection-recovery-conflict"><p className="usage-form-error" role="alert">{error}</p>
        {conflict && <RefreshButton title="usageSelection.refresh" onRefresh={async () => {
          try {
            const summary = await getCaptainSelection(missionId);
            if (!summary.canManageRecovery) { onSaved(); onClose(); return; }
            setBaseline(summary.recovery); setFallback(summary.recovery?.allowFallback ?? false); setReserve(summary.recovery?.reserveException ?? false);
            setConflict(false); setError('');
          } catch (failure) {
            if ([401, 403, 404].some(code => isApiStatus(failure, code))) { onSaved(); onClose(); }
            else setError(apiErrorMessage(failure, t('usageSelection.loadFailed')));
            throw failure;
          }
        }} />}
      </div>}
      <fieldset disabled={busy} className="selection-recovery-options">
        <legend className="sr-only">{t('usageSelection.recovery.title')}</legend>
        <label className="selection-recovery-option"><input type="checkbox" checked={fallback} onChange={e => setFallback(e.target.checked)} />
          <span><strong>{t('usageSelection.recovery.fallback')}</strong><span className="text-dim">{t('usageSelection.recovery.fallbackHelp')}</span></span></label>
        <label className="selection-recovery-option"><input type="checkbox" checked={reserve} onChange={e => setReserve(e.target.checked)} />
          <span><strong>{t('usageSelection.recovery.reserve')}</strong><span className="text-dim">{t('usageSelection.recovery.reserveHelp')}</span></span></label>
        <div className="form-group"><label htmlFor={`${formId}-reason`}>{t('usageSelection.recovery.reason')}</label>
          <textarea id={`${formId}-reason`} value={reason} onChange={e => setReason(e.target.value)} maxLength={512} required rows={3} aria-describedby={`${formId}-audit`} />
          <span id={`${formId}-audit`} className="text-dim form-help">{t('usageSelection.recovery.audit')}</span>
        </div>
      </fieldset>
    </form>
  </DialogShell>;
}
