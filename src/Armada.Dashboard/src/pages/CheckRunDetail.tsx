import { useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { deleteCheckRun, getCheckRun, getVessel, getWorkflowProfile, listCheckRuns, retryCheckRun } from '../api/client';
import type { CheckRun, Vessel, WorkflowProfile } from '../types/models';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import CopyButton from '../components/shared/CopyButton';
import DataTable from '../components/shared/DataTable';
import ErrorModal from '../components/shared/ErrorModal';
import JsonViewer from '../components/shared/JsonViewer';
import PageHeader from '../components/shared/PageHeader';
import StatusBadge from '../components/shared/StatusBadge';
import {
  buildCheckRunComparison,
  formatCheckRunComparisonScope,
  formatCheckRunComparisonSummary,
  formatSignedCountDelta,
  formatSignedDurationDelta,
  formatSignedPercentageDelta,
  type CheckRunComparison,
} from '../lib/checkRunComparison';
import { formatCheckDuration, formatCoverageMetric } from '../lib/deliveryForms';

export default function CheckRunDetail() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { t, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();

  const [run, setRun] = useState<CheckRun | null>(null);
  const [vessel, setVessel] = useState<Vessel | null>(null);
  const [profile, setProfile] = useState<WorkflowProfile | null>(null);
  const [comparison, setComparison] = useState<CheckRunComparison | null>(null);
  const [loading, setLoading] = useState(true);
  const [retrying, setRetrying] = useState(false);
  const [error, setError] = useState('');
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({
    open: false,
    title: '',
    message: '',
    onConfirm: () => {},
  });

  useEffect(() => {
    if (!id) return;
    let mounted = true;
    const runId = id;

    async function load() {
      try {
        setLoading(true);
        const nextRun = await getCheckRun(runId);
        if (!mounted) return;
        setRun(nextRun);

        const [nextVessel, nextProfile, relatedRunsResult] = await Promise.all([
          nextRun.vesselId ? getVessel(nextRun.vesselId).catch(() => null) : Promise.resolve(null),
          nextRun.workflowProfileId ? getWorkflowProfile(nextRun.workflowProfileId).catch(() => null) : Promise.resolve(null),
          nextRun.vesselId
            ? listCheckRuns({
              pageSize: 1000,
              filters: {
                vesselId: nextRun.vesselId,
                type: nextRun.type,
              },
            }).catch(() => null)
            : Promise.resolve(null),
        ]);

        if (!mounted) return;
        setVessel(nextVessel);
        setProfile(nextProfile);
        setComparison(relatedRunsResult?.objects ? buildCheckRunComparison(nextRun, relatedRunsResult.objects) : null);
        setError('');
      } catch (err: unknown) {
        if (mounted) setError(err instanceof Error ? err.message : t('Failed to load check run.'));
      } finally {
        if (mounted) setLoading(false);
      }
    }

    load();
    return () => { mounted = false; };
  }, [id, t]);

  const outputLineCount = useMemo(() => Math.max(1, (run?.output || '').split(/\r?\n/).length), [run?.output]);

  async function handleRetry() {
    if (!run) return;
    try {
      setRetrying(true);
      const retried = await retryCheckRun(run.id);
      pushToast(retried.status === 'Passed' ? 'success' : 'warning', t('Retry completed with status {{status}}.', { status: retried.status }));
      navigate(`/checks/${retried.id}`);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Retry failed.'));
    } finally {
      setRetrying(false);
    }
  }

  function handleDelete() {
    if (!run) return;
    setConfirm({
      open: true,
      title: t('Delete Check Run'),
      message: t('Delete check run "{{id}}"?', { id: run.id }),
      onConfirm: async () => {
        setConfirm((current) => ({ ...current, open: false }));
        try {
          await deleteCheckRun(run.id);
          pushToast('warning', t('Check run deleted.'));
          navigate('/checks');
        } catch (err: unknown) {
          setError(err instanceof Error ? err.message : t('Delete failed.'));
        }
      },
    });
  }

  if (loading) return <p className="text-dim">{t('Loading...')}</p>;
  if (!run) return <p className="text-dim">{t('Check run not found.')}</p>;

  return (
    <div>
      <PageHeader
        breadcrumb={
          <>
            <Link to="/checks">{t('Checks')}</Link> <span className="breadcrumb-sep">&gt;</span> <span>{run.label || run.id}</span>
          </>
        }
        title={run.label || run.type}
        actions={
          <>
            <StatusBadge status={run.status} />
            <button className="btn btn-sm" onClick={() => setJsonData({ open: true, title: run.label || run.id, data: run })}>
              {t('View JSON')}
            </button>
            <button className="btn btn-sm" disabled={retrying} onClick={handleRetry}>
              {retrying ? t('Retrying...') : t('Retry')}
            </button>
            <button
              className="btn btn-sm"
              onClick={() => navigate('/releases/new', {
                state: {
                  prefill: {
                    vesselId: run.vesselId || null,
                    voyageIds: run.voyageId ? [run.voyageId] : [],
                    missionIds: run.missionId ? [run.missionId] : [],
                    checkRunIds: [run.id],
                    title: run.label ? `${run.label} Release` : `${run.type} Release`,
                  },
                },
              })}
            >
              {t('Draft Release')}
            </button>
            <button className="btn btn-sm btn-danger" onClick={handleDelete}>
              {t('Delete')}
            </button>
          </>
        }
      />

      <ErrorModal error={error} onClose={() => setError('')} />
      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <ConfirmDialog
        open={confirm.open}
        title={confirm.title}
        message={confirm.message}
        onConfirm={confirm.onConfirm}
        onCancel={() => setConfirm((current) => ({ ...current, open: false }))}
      />

      <div className="detail-grid" style={{ marginBottom: '1rem' }}>
        <div className="detail-field">
          <span className="detail-label">{t('ID')}</span>
          <span className="id-display">
            <span className="mono">{run.id}</span>
            <CopyButton text={run.id} />
          </span>
        </div>
        <div className="detail-field"><span className="detail-label">{t('Type')}</span><span>{run.type}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Status')}</span><StatusBadge status={run.status} /></div>
        <div className="detail-field"><span className="detail-label">{t('Exit Code')}</span><span>{run.exitCode ?? '-'}</span></div>
        <div className="detail-field">
          <span className="detail-label">{t('Vessel')}</span>
          <span>{run.vesselId ? <Link to={`/vessels/${run.vesselId}`}>{vessel?.name || run.vesselId}</Link> : '-'}</span>
        </div>
        <div className="detail-field">
          <span className="detail-label">{t('Workflow Profile')}</span>
          <span>{run.workflowProfileId ? <Link to={`/workflow-profiles/${run.workflowProfileId}`}>{profile?.name || run.workflowProfileId}</Link> : t('Resolved override only')}</span>
        </div>
        <div className="detail-field"><span className="detail-label">{t('Source')}</span><span>{run.source}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Provider')}</span><span>{run.providerName || '-'}</span></div>
        <div className="detail-field"><span className="detail-label">{t('External ID')}</span><span className="mono">{run.externalId || '-'}</span></div>
        <div className="detail-field">
          <span className="detail-label">{t('External URL')}</span>
          <span>{run.externalUrl ? <a href={run.externalUrl} target="_blank" rel="noopener noreferrer">{run.externalUrl}</a> : '-'}</span>
        </div>
        <div className="detail-field"><span className="detail-label">{t('Environment')}</span><span>{run.environmentName || '-'}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Duration')}</span><span>{formatCheckDuration(run.durationMs)}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Mission ID')}</span><span>{run.missionId ? <Link to={`/missions/${run.missionId}`}>{run.missionId}</Link> : '-'}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Voyage ID')}</span><span>{run.voyageId ? <Link to={`/voyages/${run.voyageId}`}>{run.voyageId}</Link> : '-'}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Deployment ID')}</span><span>{run.deploymentId ? <Link to={`/deployments/${run.deploymentId}`}>{run.deploymentId}</Link> : '-'}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Branch')}</span><span className="mono">{run.branchName || '-'}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Commit')}</span><span className="mono">{run.commitHash || '-'}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Created')}</span><span>{formatDateTime(run.createdUtc)}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Started')}</span><span>{run.startedUtc ? formatDateTime(run.startedUtc) : '-'}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Completed')}</span><span>{run.completedUtc ? formatDateTime(run.completedUtc) : '-'}</span></div>
      </div>

      <div className="card" style={{ marginBottom: '1rem' }}>
        <h3>{t('Command')}</h3>
        <pre className="detail-context-block" style={{ whiteSpace: 'pre-wrap' }}>{run.command}</pre>
      </div>

      {run.summary && (
        <div className="card" style={{ marginBottom: '1rem' }}>
          <h3>{t('Summary')}</h3>
          <p style={{ margin: 0 }}>{run.summary}</p>
        </div>
      )}

      {comparison && (
        <div className="card check-run-comparison-card" style={{ marginBottom: '1rem' }}>
          <div className="check-run-comparison-header">
            <div className="check-run-comparison-meta">
              <h3>{t('Compare to Previous Run')}</h3>
              <p className="check-run-comparison-summary">
                {t('Compared to')} <Link to={`/checks/${comparison.baseline.id}`}>{comparison.baseline.label || comparison.baseline.id}</Link>{' '}
                ({formatCheckRunComparisonScope(comparison.scope)}) • {formatDateTime(comparison.baseline.createdUtc)}
              </p>
              <p className="check-run-comparison-summary">{formatCheckRunComparisonSummary(comparison)}</p>
            </div>
            <div className="check-run-comparison-badges">
              <span className="tag">{t(formatCheckRunComparisonScope(comparison.scope))}</span>
              <span className={`tag ${comparison.hasRegression ? 'failed' : comparison.hasImprovement ? 'passed' : ''}`.trim()}>
                {comparison.hasRegression ? t('Regression detected') : comparison.hasImprovement ? t('Improvement detected') : t('No significant change')}
              </span>
            </div>
          </div>

          <div className="detail-grid">
            <div className="detail-field"><span className="detail-label">{t('Status')}</span><span>{comparison.baseline.status} {'->'} {run.status}</span></div>
            <div className="detail-field"><span className="detail-label">{t('Duration Delta')}</span><span>{formatSignedDurationDelta(comparison.durationDeltaMs) || '-'}</span></div>
            <div className="detail-field"><span className="detail-label">{t('Artifact Delta')}</span><span>{formatSignedCountDelta(comparison.artifactCountDelta, 'artifacts') || '-'}</span></div>
            <div className="detail-field"><span className="detail-label">{t('Passed Delta')}</span><span>{formatSignedCountDelta(comparison.testDelta.passed, 'passed') || '-'}</span></div>
            <div className="detail-field"><span className="detail-label">{t('Failed Delta')}</span><span>{formatSignedCountDelta(comparison.testDelta.failed, 'failed') || '-'}</span></div>
            <div className="detail-field"><span className="detail-label">{t('Skipped Delta')}</span><span>{formatSignedCountDelta(comparison.testDelta.skipped, 'skipped') || '-'}</span></div>
            <div className="detail-field"><span className="detail-label">{t('Total Delta')}</span><span>{formatSignedCountDelta(comparison.testDelta.total, 'tests') || '-'}</span></div>
            <div className="detail-field"><span className="detail-label">{t('Line Coverage Delta')}</span><span>{formatSignedPercentageDelta(comparison.coverageDelta.linesPct) || '-'}</span></div>
            <div className="detail-field"><span className="detail-label">{t('Branch Coverage Delta')}</span><span>{formatSignedPercentageDelta(comparison.coverageDelta.branchesPct) || '-'}</span></div>
            <div className="detail-field"><span className="detail-label">{t('Function Coverage Delta')}</span><span>{formatSignedPercentageDelta(comparison.coverageDelta.functionsPct) || '-'}</span></div>
            <div className="detail-field"><span className="detail-label">{t('Statement Coverage Delta')}</span><span>{formatSignedPercentageDelta(comparison.coverageDelta.statementsPct) || '-'}</span></div>
          </div>
        </div>
      )}

      {(run.testSummary || run.coverageSummary) && (
        <div className="detail-grid" style={{ marginBottom: '1rem' }}>
          {run.testSummary && (
            <div className="card">
              <h3>{t('Test Results')}</h3>
              <div className="detail-grid">
                <div className="detail-field"><span className="detail-label">{t('Format')}</span><span>{run.testSummary.format || '-'}</span></div>
                <div className="detail-field"><span className="detail-label">{t('Passed')}</span><span>{run.testSummary.passed ?? '-'}</span></div>
                <div className="detail-field"><span className="detail-label">{t('Failed')}</span><span>{run.testSummary.failed ?? '-'}</span></div>
                <div className="detail-field"><span className="detail-label">{t('Skipped')}</span><span>{run.testSummary.skipped ?? '-'}</span></div>
                <div className="detail-field"><span className="detail-label">{t('Total')}</span><span>{run.testSummary.total ?? '-'}</span></div>
                <div className="detail-field"><span className="detail-label">{t('Test Duration')}</span><span>{formatCheckDuration(run.testSummary.durationMs)}</span></div>
              </div>
            </div>
          )}

          {run.coverageSummary && (
            <div className="card">
              <h3>{t('Coverage')}</h3>
              <div className="detail-grid">
                <div className="detail-field"><span className="detail-label">{t('Format')}</span><span>{run.coverageSummary.format || '-'}</span></div>
                <div className="detail-field"><span className="detail-label">{t('Source')}</span><span className="mono">{run.coverageSummary.sourcePath || '-'}</span></div>
                <div className="detail-field"><span className="detail-label">{t('Lines')}</span><span>{formatCoverageMetric(run.coverageSummary.lines) || '-'}</span></div>
                <div className="detail-field"><span className="detail-label">{t('Branches')}</span><span>{formatCoverageMetric(run.coverageSummary.branches) || '-'}</span></div>
                <div className="detail-field"><span className="detail-label">{t('Functions')}</span><span>{formatCoverageMetric(run.coverageSummary.functions) || '-'}</span></div>
                <div className="detail-field"><span className="detail-label">{t('Statements')}</span><span>{formatCoverageMetric(run.coverageSummary.statements) || '-'}</span></div>
              </div>
            </div>
          )}
        </div>
      )}

      <div className="card" style={{ marginBottom: '1rem' }}>
        <h3>{t('Artifacts')}</h3>
        {run.artifacts.length === 0 ? (
          <p className="text-dim">{t('No artifacts were collected for this run.')}</p>
        ) : (
          <DataTable
            tableKey="check-run-artifacts"
            rows={run.artifacts}
            rowKey={(artifact) => artifact.path}
            recordCount={null}
            columns={[
              {
                key: 'path', label: t('Path'), required: true, cellClassName: 'mono',
                render: (artifact) => <span className="cell-one-line" style={{ ['--cell-max' as string]: '40rem' }} title={artifact.path}>{artifact.path}</span>,
              },
              { key: 'size', label: t('Size'), cellClassName: 'cell-nowrap', render: (artifact) => `${artifact.sizeBytes.toLocaleString()} ${t('bytes')}` },
              { key: 'lastWrite', label: t('Last Write'), cellClassName: 'cell-nowrap', render: (artifact) => formatDateTime(artifact.lastWriteUtc) },
            ]}
          />
        )}
      </div>

      <div className="card">
        <h3>{t('Output')}</h3>
        <div className="workspace-code-view" style={{ maxHeight: '32rem' }}>
          <div className="workspace-code-line-numbers" aria-hidden="true">
            {Array.from({ length: outputLineCount }, (_, index) => (
              <span key={index + 1}>{index + 1}</span>
            ))}
          </div>
          <pre className="workspace-code-block" style={{ margin: 0, whiteSpace: 'pre-wrap' }}>{run.output || t('No output captured.')}</pre>
        </div>
      </div>
    </div>
  );
}
