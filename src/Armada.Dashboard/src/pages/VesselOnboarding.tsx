import { useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { getVessel, getVesselReadiness } from '../api/client';
import type { Vessel, VesselReadinessResult, VesselSetupChecklistItem } from '../types/models';
import { useLocale } from '../context/LocaleContext';
import ErrorModal from '../components/shared/ErrorModal';
import ReadinessPanel from '../components/shared/ReadinessPanel';
import { groupSetupChecklist, nextChecklistItem as findNextChecklistItem } from '../lib/readiness';

export default function VesselOnboarding() {
  const { id } = useParams<{ id: string }>();
  const vesselId = id ?? '';
  const navigate = useNavigate();
  const { t } = useLocale();
  const [vessel, setVessel] = useState<Vessel | null>(null);
  const [readiness, setReadiness] = useState<VesselReadinessResult | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  useEffect(() => {
    if (!vesselId) return;

    let mounted = true;
    async function load() {
      try {
        setLoading(true);
        const [loadedVessel, loadedReadiness] = await Promise.all([
          getVessel(vesselId),
          getVesselReadiness(vesselId),
        ]);
        if (!mounted) return;
        setVessel(loadedVessel);
        setReadiness(loadedReadiness);
        setError('');
      } catch (err: unknown) {
        if (!mounted) return;
        setError(err instanceof Error ? err.message : t('Failed to load vessel onboarding.'));
      } finally {
        if (mounted) setLoading(false);
      }
    }

    load();
    return () => { mounted = false; };
  }, [vesselId, t]);

  const groupedChecklist = useMemo(() => groupSetupChecklist(readiness), [readiness]);

  const nextChecklistItem = useMemo(() => findNextChecklistItem(readiness), [readiness]);

  function renderChecklistItem(item: VesselSetupChecklistItem) {
    return (
      <div key={item.code} className={`readiness-checklist-item${item.isSatisfied ? ' satisfied' : ''}`}>
        <div className="readiness-checklist-title-row">
          <strong>{item.title}</strong>
          <span className={`readiness-issue-severity ${item.severity.toLowerCase()}`}>
            {item.isSatisfied ? t('Done') : item.severity}
          </span>
        </div>
        <div className="text-dim">{item.message}</div>
        {!item.isSatisfied && item.actionLabel && item.actionRoute && (
          <div className="readiness-checklist-action">
            <Link to={item.actionRoute} className="btn btn-sm">
              {item.actionLabel}
            </Link>
          </div>
        )}
      </div>
    );
  }

  if (!vesselId) return <p className="text-dim">{t('Vessel not found.')}</p>;
  if (loading) return <p className="text-dim">{t('Loading...')}</p>;
  if (!vessel) return <p className="text-dim">{t('Vessel not found.')}</p>;

  return (
    <div>
      <div className="breadcrumb">
        <Link to="/vessels">{t('Vessels')}</Link> <span className="breadcrumb-sep">&gt;</span>{' '}
        <Link to={`/vessels/${vessel.id}`}>{vessel.name}</Link> <span className="breadcrumb-sep">&gt;</span>{' '}
        <span>{t('Onboarding')}</span>
      </div>

      <div className="detail-header">
        <div>
          <h2>{t('Vessel Onboarding')}</h2>
          <div className="text-dim">
            {t('Use this checklist to take the vessel from registration through workflow-ready onboarding.')}
          </div>
        </div>
        <div className="inline-actions">
          <button type="button" className="btn btn-sm" onClick={() => navigate(`/workspace/${vessel.id}`)}>
            {t('Open Workspace')}
          </button>
          <button type="button" className="btn btn-sm" onClick={() => navigate('/checks', { state: { prefill: { vesselId: vessel.id, branchName: vessel.defaultBranch || '' } } })}>
            {t('Run Check')}
          </button>
          <button type="button" className="btn btn-sm" onClick={() => navigate(`/vessels/${vessel.id}`)}>
            {t('Back To Vessel')}
          </button>
        </div>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />

      <div className="playbook-overview-grid">
        <div className="card playbook-overview-card">
          <span>{t('Completed')}</span>
          <strong>{`${readiness?.setupChecklistSatisfiedCount || 0}/${readiness?.setupChecklistTotalCount || 0}`}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Blocking Issues')}</span>
          <strong>{readiness?.errorCount || 0}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Warnings')}</span>
          <strong>{readiness?.warningCount || 0}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Environments')}</span>
          <strong>{readiness?.deploymentEnvironments.length || 0}</strong>
        </div>
      </div>

      {nextChecklistItem && (
        <div className="card" style={{ marginBottom: '1rem', padding: '1rem' }}>
          <div className="detail-header" style={{ marginBottom: '0.5rem' }}>
            <h3>{t('Next Recommended Step')}</h3>
          </div>
          <strong>{nextChecklistItem.title}</strong>
          <div className="text-dim" style={{ marginTop: '0.35rem' }}>{nextChecklistItem.message}</div>
          {!nextChecklistItem.isSatisfied && nextChecklistItem.actionLabel && nextChecklistItem.actionRoute && (
            <div style={{ marginTop: '0.75rem' }}>
              <Link to={nextChecklistItem.actionRoute} className="btn btn-sm btn-primary">
                {nextChecklistItem.actionLabel}
              </Link>
            </div>
          )}
        </div>
      )}

      <ReadinessPanel
        title={t('Current Readiness')}
        readiness={readiness}
        emptyMessage={t('Readiness data is not available for this vessel yet.')}
      />

      {groupedChecklist.map((group) => (
        <div key={group.key} className="card" style={{ marginBottom: '1rem', padding: '1rem' }}>
          <div className="detail-header" style={{ marginBottom: '0.75rem' }}>
            <h3>{t(group.title)}</h3>
          </div>
          <div className="readiness-checklist-items">
            {group.items.map((item) => renderChecklistItem(item))}
          </div>
        </div>
      ))}

      {readiness && readiness.issues.length > 0 && (
        <div className="card" style={{ padding: '1rem' }}>
          <div className="detail-header" style={{ marginBottom: '0.75rem' }}>
            <h3>{t('Open Issues')}</h3>
          </div>
          <div className="readiness-issues">
            {readiness.issues.map((issue, index) => (
              <div key={`${issue.code}-${index}`} className={`readiness-issue ${issue.severity.toLowerCase()}`}>
                <div className="readiness-issue-title-row">
                  <strong>{issue.title}</strong>
                  <span className={`readiness-issue-severity ${issue.severity.toLowerCase()}`}>{issue.severity}</span>
                </div>
                <div className="text-dim">{issue.message}</div>
                {issue.relatedValue && <div className="readiness-related-value mono">{issue.relatedValue}</div>}
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
