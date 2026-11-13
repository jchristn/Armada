import { useLocale } from '../../context/LocaleContext';

/** Verification owns its evidence and dock, while execution capacity is available to other work. */
export default function VerificationStatusCard() {
  const { t } = useLocale();
  return (
    <div className="card" role="status" style={{ marginBottom: '1rem' }}>
      <div className="readiness-panel-header"><h3>{t('Verifying')}</h3></div>
      <p style={{ margin: '0.5rem 0 0' }}>{t('Checks may be queued or running. Verification does not reserve a captain execution slot; any repair waits for available capacity.')}</p>
    </div>
  );
}
