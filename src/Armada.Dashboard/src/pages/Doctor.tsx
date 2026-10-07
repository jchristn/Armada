import { useState, useCallback, useEffect } from 'react';
import { getDoctor } from '../api/client';
import StatusBadge from '../components/shared/StatusBadge';
import ErrorModal from '../components/shared/ErrorModal';
import PageHeader from '../components/shared/PageHeader';
import DataTable from '../components/shared/DataTable';
import { useLocale } from '../context/LocaleContext';

interface DiagnosticCheck {
  name: string;
  status: string;
  message: string;
}

export default function Doctor() {
  const { t } = useLocale();
  const [results, setResults] = useState<DiagnosticCheck[]>([]);
  const [running, setRunning] = useState(false);
  const [error, setError] = useState('');

  const runChecks = useCallback(async () => {
    setRunning(true);
    setResults([]);
    setError('');
    try {
      const checks = await getDoctor();
      setResults(checks);
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : t('Unknown error');
      setResults([{ name: t('Error'), status: 'Fail', message: t('Failed to run health checks: {{message}}', { message: msg }) }]);
    } finally {
      setRunning(false);
    }
  }, [t]);

  useEffect(() => {
    runChecks();
  }, [runChecks]);

  const passCount = results.filter((c) => c.status === 'Pass').length;
  const warnCount = results.filter((c) => c.status === 'Warn').length;
  const failCount = results.filter((c) => c.status === 'Fail').length;

  const hasResults = results.length > 0;
  const allPassing = hasResults && failCount === 0 && warnCount === 0;
  const hasFailures = failCount > 0;

  return (
    <div>
      <PageHeader
        title={t('Diagnostics')}
        subtitle={t('System health diagnostics and checks.')}
        actions={(
          <>
            {hasResults && (
              <span
                className={`doctor-badge ${hasFailures ? 'doctor-fail' : allPassing ? 'doctor-pass' : 'doctor-warn'}`}
                style={{ marginRight: '0.5rem' }}
              >
                {hasFailures ? t('Unhealthy') : allPassing ? t('Healthy') : t('Warnings')}
              </span>
            )}
            <button
              className="btn-primary btn-sm"
              onClick={runChecks}
              disabled={running}
              title={t('Run all health checks')}
            >
              {running ? t('Running...') : t('Run Checks')}
            </button>
          </>
        )}
      />

      <ErrorModal error={error} onClose={() => setError('')} />

      {running && (
        <div style={{ textAlign: 'center', padding: '2rem' }}>
          <span className="text-muted">{t('Running health checks...')}</span>
        </div>
      )}

      {!running && hasResults && (
        <>
          {/* Summary */}
          <div className="card-grid" style={{ marginBottom: '1.5rem' }}>
            <div className="card">
              <div className="card-label">{t('Passed')}</div>
              <div className="card-value" style={{ color: 'var(--color-success, #22c55e)' }}>
                {passCount}
              </div>
            </div>
            <div className="card">
              <div className="card-label">{t('Warnings')}</div>
              <div className="card-value" style={{ color: 'var(--color-warning, #f59e0b)' }}>
                {warnCount}
              </div>
            </div>
            <div className="card">
              <div className="card-label">{t('Failed')}</div>
              <div className="card-value" style={{ color: 'var(--color-error, #ef4444)' }}>
                {failCount}
              </div>
            </div>
          </div>

          {/* Results Table */}
          <DataTable
            tableKey="doctor-checks"
            className="table"
            rows={results.map((check, index) => ({ check, index }))}
            rowKey={(row) => String(row.index)}
            recordCount={null}
            columns={[
              { key: 'check', label: t('Check'), required: true, render: ({ check }) => <span style={{ fontWeight: 500 }}>{check.name}</span> },
              { key: 'status', label: t('Status'), cellClassName: 'cell-nowrap', render: ({ check }) => <StatusBadge status={check.status} /> },
              { key: 'message', label: t('Message'), cellClassName: 'text-muted', render: ({ check }) => check.message },
            ]}
          />
        </>
      )}

      {!running && !hasResults && (
        <div className="text-muted" style={{ padding: '2rem', textAlign: 'center' }}>
          {t('No results yet. Click "Run Checks" to start diagnostics.')}
        </div>
      )}
    </div>
  );
}
