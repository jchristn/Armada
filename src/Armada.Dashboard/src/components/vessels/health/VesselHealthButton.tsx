import { useState } from 'react';
import { useAuth } from '../../../context/AuthContext';
import { useLocale } from '../../../context/LocaleContext';
import { useNotifications } from '../../../context/NotificationContext';
import { describeEvaluationStart, useHealthEvaluation } from '../../../lib/health/useHealthEvaluation';
import { formatCount } from '../../../lib/health/healthText';
import VesselHealthDetailModal from './VesselHealthDetailModal';

interface VesselHealthButtonProps {
  vesselId: string;
  vesselName: string;
  defaultBranch?: string | null;
}

/**
 * "Health" header button for the vessel detail page. Opens the health detail modal and owns a small
 * evaluation tracker so Re-evaluate works without the Health tab.
 */
export default function VesselHealthButton({ vesselId, vesselName, defaultBranch }: VesselHealthButtonProps) {
  const { t, locale } = useLocale();
  const { isTenantAdmin } = useAuth();
  const { pushToast } = useNotifications();
  const [open, setOpen] = useState(false);
  const [refreshToken, setRefreshToken] = useState(0);
  const evaluation = useHealthEvaluation({
    onFinished: (job) => {
      pushToast(job.status === 'Succeeded' ? 'success' : 'warning', job.status === 'Succeeded' ? t('Evaluation finished.') : t('Evaluation ended with status {{status}}.', { status: t(job.status) }));
      setRefreshToken((n) => n + 1);
    },
  });

  async function reevaluate(id: string) {
    try {
      const start = await evaluation.start({ VesselIds: [id], Force: true });
      const message = describeEvaluationStart(start);
      const params: Record<string, string> = {};
      for (const [k, v] of Object.entries(message.params)) params[k] = formatCount(locale, v);
      pushToast(message.severity, t(message.key, params));
    } catch (err) {
      pushToast('error', t('Could not start the evaluation: {{message}}', { message: err instanceof Error ? err.message : String(err) }));
    }
  }

  return (
    <>
      <button type="button" className="btn btn-sm" onClick={() => setOpen(true)} title={t('Divergence, dirty checkout, branches, dependencies, tests, and CI for this vessel')}>
        {t('Health')}
      </button>
      {open && (
        <VesselHealthDetailModal
          vesselId={vesselId}
          vesselName={vesselName}
          defaultBranch={defaultBranch}
          canAdmin={isTenantAdmin}
          evaluationRunning={evaluation.running}
          refreshToken={refreshToken}
          onReevaluate={(id) => { void reevaluate(id); }}
          onClose={() => setOpen(false)}
        />
      )}
    </>
  );
}
