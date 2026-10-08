import { useCallback, useEffect, useState } from 'react';
import { listJobs, cancelJob } from '../api/client';
import type { Job } from '../types/models';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import StatusBadge from '../components/shared/StatusBadge';
import DataTable, { type DataTableColumn } from '../components/shared/DataTable';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import { isJobTerminal } from '../lib/jobs';

export default function Jobs() {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const [jobs, setJobs] = useState<Job[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const result = await listJobs();
      setJobs(result.objects || []);
      setError('');
    } catch {
      setError(t('Failed to load jobs.'));
    } finally {
      setLoading(false);
    }
  }, [t]);

  useEffect(() => { load(); }, [load]);

  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('jobs', load);

  async function handleCancel(job: Job) {
    try {
      await cancelJob(job.id);
      pushToast('warning', t('Job "{{name}}" cancelled.', { name: job.name }));
      load();
    } catch {
      setError(t('Failed to cancel job.'));
    }
  }

  const columns: DataTableColumn<Job>[] = [
    {
      key: 'name', label: t('Name'), required: true,
      cellTitle: (job) => [job.name, job.errorReason].filter(Boolean).join('\n'),
      render: (job) => <span className="cell-one-line">{job.name}</span>,
    },
    { key: 'kind', label: t('Kind'), render: (job) => job.kind },
    { key: 'status', label: t('Status'), cellClassName: 'cell-nowrap', render: (job) => <StatusBadge status={job.status} /> },
    { key: 'progress', label: t('Progress'), cellClassName: 'mono cell-nowrap', render: (job) => `${job.progress}%` },
    {
      // Was a small second line under the name; now its own one-line column (full text in the tooltip).
      key: 'error', label: t('Error'), cellClassName: 'text-dim',
      render: (job) => (job.errorReason ? <span className="cell-one-line" title={job.errorReason}>{job.errorReason}</span> : '-'),
    },
    {
      key: 'created', label: t('Created'), cellClassName: 'text-dim cell-nowrap',
      cellTitle: (job) => formatDateTime(job.createdUtc),
      render: (job) => formatRelativeTime(job.createdUtc),
    },
    {
      key: 'updated', label: t('Updated'), cellClassName: 'text-dim cell-nowrap',
      cellTitle: (job) => formatDateTime(job.lastUpdateUtc),
      render: (job) => formatRelativeTime(job.lastUpdateUtc),
    },
    {
      key: 'actions', label: t('Actions'), header: '', fixed: true, interactive: true, className: 'text-right',
      render: (job) => (!isJobTerminal(job.status) ? (
        <button type="button" className="btn btn-sm" onClick={() => handleCancel(job)}>{t('Cancel')}</button>
      ) : null),
    },
  ];

  return (
    <div className="jobs-page">
      <div className="view-header">
        <div>
          <h2>{t('Jobs')}</h2>
          <p className="text-dim view-subtitle">{t('Background jobs and their status.')}</p>
        </div>
      </div>

      {error && <div className="alert alert-error">{error}</div>}

      <DataTable
        tableKey="jobs"
        columns={columns}
        rows={jobs}
        rowKey={(job) => job.id}
        autoRefresh={{ seconds: refreshSeconds, onChange: setRefreshSeconds }}
        onRefresh={load}
        refreshTitle={t('Refresh jobs')}
        placeholder={loading && jobs.length === 0 ? <p className="text-dim">{t('Loading...')}</p> : jobs.length === 0 ? (
          <div className="card" style={{ padding: '1.25rem' }}>
            <p className="text-muted">{t('No background jobs.')}</p>
          </div>
        ) : undefined}
      />
    </div>
  );
}
