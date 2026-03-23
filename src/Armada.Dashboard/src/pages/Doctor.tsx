import { useState, useCallback, useEffect } from 'react';
import { getDoctor } from '../api/client';
import StatusBadge from '../components/shared/StatusBadge';
import ErrorModal from '../components/shared/ErrorModal';

interface DiagnosticCheck {
  name: string;
  status: string;
  message: string;
}

export default function Doctor() {
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
      const msg = e instanceof Error ? e.message : 'Unknown error';
      setResults([{ name: 'Error', status: 'Fail', message: `Failed to run health checks: ${msg}` }]);
    } finally {
      setRunning(false);
    }
  }, []);

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
      <div className="page-header">
        <div>
          <h2>Doctor</h2>
          <p className="text-muted">System health diagnostics and checks.</p>
        </div>
        <div className="page-actions">
          {hasResults && (
            <span
              className={`doctor-badge ${hasFailures ? 'doctor-fail' : allPassing ? 'doctor-pass' : 'doctor-warn'}`}
              style={{ marginRight: '0.5rem' }}
            >
              {hasFailures ? 'Unhealthy' : allPassing ? 'Healthy' : 'Warnings'}
            </span>
          )}
          <button
            className="btn-primary btn-sm"
            onClick={runChecks}
            disabled={running}
            title="Run all health checks"
          >
            {running ? 'Running...' : 'Run Checks'}
          </button>
        </div>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />

      {running && (
        <div style={{ textAlign: 'center', padding: '2rem' }}>
          <span className="text-muted">Running health checks...</span>
        </div>
      )}

      {!running && hasResults && (
        <>
          {/* Summary */}
          <div className="card-grid" style={{ marginBottom: '1.5rem' }}>
            <div className="card">
              <div className="card-label">Passed</div>
              <div className="card-value" style={{ color: 'var(--color-success, #22c55e)' }}>
                {passCount}
              </div>
            </div>
            <div className="card">
              <div className="card-label">Warnings</div>
              <div className="card-value" style={{ color: 'var(--color-warning, #f59e0b)' }}>
                {warnCount}
              </div>
            </div>
            <div className="card">
              <div className="card-label">Failed</div>
              <div className="card-value" style={{ color: 'var(--color-error, #ef4444)' }}>
                {failCount}
              </div>
            </div>
          </div>

          {/* Results Table */}
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Check</th>
                  <th>Status</th>
                  <th>Message</th>
                </tr>
              </thead>
              <tbody>
                {results.map((check, i) => (
                  <tr key={i}>
                    <td style={{ fontWeight: 500 }}>{check.name}</td>
                    <td>
                      <StatusBadge status={check.status} />
                    </td>
                    <td className="text-muted">{check.message}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}

      {!running && !hasResults && (
        <div className="text-muted" style={{ padding: '2rem', textAlign: 'center' }}>
          No results yet. Click "Run Checks" to start diagnostics.
        </div>
      )}
    </div>
  );
}
