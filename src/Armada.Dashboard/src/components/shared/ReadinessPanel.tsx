import { Link } from 'react-router-dom';
import type { VesselReadinessResult } from '../../types/models';
import { formatInputProvider, getReadinessLabel, getReadinessTone, readinessBranchSummary, readinessDriftSummary } from '../../lib/readiness';

interface ReadinessPanelProps {
  title: string;
  readiness: VesselReadinessResult | null;
  loading?: boolean;
  emptyMessage?: string;
  compact?: boolean;
}

export default function ReadinessPanel(props: ReadinessPanelProps) {
  const { title, readiness, loading = false, emptyMessage = 'No readiness data.', compact = false } = props;
  const tone = getReadinessTone(readiness);
  const label = getReadinessLabel(readiness);
  const branchSummary = readinessBranchSummary(readiness);
  const aheadBehindSummary = readinessDriftSummary(readiness);

  return (
    <div className={`card readiness-panel${compact ? ' compact' : ''}`}>
      <div className="readiness-panel-header">
        <div>
          <h3>{title}</h3>
          {readiness?.workflowProfileName && (
            <div className="readiness-panel-meta">
              Resolved profile: <strong>{readiness.workflowProfileName}</strong>
              {readiness.workflowProfileScope ? ` (${readiness.workflowProfileScope})` : ''}
            </div>
          )}
          {!compact && readiness && readiness.setupChecklistTotalCount > 0 && (
            <div className="readiness-panel-meta">
              Onboarding: <strong>{readiness.setupChecklistSatisfiedCount}/{readiness.setupChecklistTotalCount}</strong> steps complete
            </div>
          )}
        </div>
        <span className={`readiness-pill ${tone}`}>{label}</span>
      </div>

      {loading ? (
        <div className="text-dim">Checking readiness...</div>
      ) : !readiness ? (
        <div className="text-dim">{emptyMessage}</div>
      ) : (
        <>
          <div className="readiness-summary-row">
            <span>{readiness.hasWorkingDirectory ? 'Working directory available' : 'Working directory unavailable'}</span>
            <span>{readiness.hasRepositoryContext ? 'Repository context available' : 'Repository context unavailable'}</span>
            {readiness.availableCheckTypes.length > 0 && (
              <span>{readiness.availableCheckTypes.length} check type(s) available</span>
            )}
          </div>

          {readiness.availableCheckTypes.length > 0 && !compact && (
            <div className="readiness-available-types">
              {readiness.availableCheckTypes.map((item) => (
                <span key={item} className="readiness-type-pill">{item}</span>
              ))}
            </div>
          )}

          {!compact && (branchSummary || aheadBehindSummary || readiness.detectedToolchains.length > 0 || readiness.deploymentEnvironments.length > 0 || readiness.deploymentMetadata) && (
            <div className="readiness-info-grid">
              {branchSummary && (
                <div className="readiness-info-card">
                  <div className="readiness-info-label">Branch</div>
                  <div className="mono">{branchSummary}</div>
                </div>
              )}
              {aheadBehindSummary && (
                <div className="readiness-info-card">
                  <div className="readiness-info-label">Remote drift</div>
                  <div>{aheadBehindSummary}</div>
                </div>
              )}
              {readiness.hasUncommittedChanges != null && (
                <div className="readiness-info-card">
                  <div className="readiness-info-label">Working tree</div>
                  <div>{readiness.hasUncommittedChanges ? 'Uncommitted changes present' : 'Clean working tree'}</div>
                </div>
              )}
              {readiness.detectedToolchains.length > 0 && (
                <div className="readiness-info-card">
                  <div className="readiness-info-label">Detected toolchains</div>
                  <div className="readiness-inline-list">
                    {readiness.detectedToolchains.map((item) => (
                      <span key={item} className="readiness-type-pill">{item}</span>
                    ))}
                  </div>
                </div>
              )}
              {readiness.toolchainProbes.length > 0 && (
                <div className="readiness-info-card">
                  <div className="readiness-info-label">Toolchain probes</div>
                  <div className="readiness-probe-list">
                    {readiness.toolchainProbes.map((probe) => (
                      <div key={`${probe.name}-${probe.command}`} className="readiness-probe-row">
                        <span className={`readiness-type-pill${probe.available ? '' : ' warning'}`}>{probe.name}</span>
                        <span className="mono">{probe.version || (probe.available ? 'available' : 'missing')}</span>
                        {probe.expected && <span className="text-dim">expected</span>}
                      </div>
                    ))}
                  </div>
                </div>
              )}
              {readiness.deploymentEnvironments.length > 0 && (
                <div className="readiness-info-card">
                  <div className="readiness-info-label">Environments</div>
                  <div className="readiness-inline-list">
                    {readiness.deploymentEnvironments.map((item) => (
                      <span key={item} className="readiness-type-pill">{item}</span>
                    ))}
                  </div>
                </div>
              )}
              {readiness.deploymentMetadata && (
                <div className="readiness-info-card">
                  <div className="readiness-info-label">Delivery coverage</div>
                  <div className="readiness-inline-list">
                    <span className="readiness-type-pill">{`${readiness.deploymentMetadata.environmentCount} env(s)`}</span>
                    {readiness.deploymentMetadata.hasDeployCommand && <span className="readiness-type-pill">Deploy</span>}
                    {readiness.deploymentMetadata.hasRollbackCommand && <span className="readiness-type-pill">Rollback</span>}
                    {readiness.deploymentMetadata.hasSmokeTestCommand && <span className="readiness-type-pill">Smoke</span>}
                    {readiness.deploymentMetadata.hasHealthCheckCommand && <span className="readiness-type-pill">Health</span>}
                    {readiness.deploymentMetadata.hasDeploymentVerificationCommand && <span className="readiness-type-pill">Deploy Verify</span>}
                    {readiness.deploymentMetadata.hasRollbackVerificationCommand && <span className="readiness-type-pill">Rollback Verify</span>}
                  </div>
                </div>
              )}
            </div>
          )}

          {!compact && readiness.setupChecklist.length > 0 && (
            <div className="readiness-checklist">
              <div className="readiness-section-title">
                Setup checklist
                <span className="text-dim" style={{ marginLeft: '0.5rem', fontWeight: 400 }}>
                  {readiness.setupChecklistSatisfiedCount}/{readiness.setupChecklistTotalCount} complete
                </span>
                {readiness.setupChecklistSatisfiedCount < readiness.setupChecklistTotalCount && (
                  <Link to={`/vessels/${readiness.vesselId}/onboarding`} className="btn btn-sm" style={{ marginLeft: '0.75rem' }}>
                    Open Onboarding
                  </Link>
                )}
              </div>
              <div className="readiness-checklist-items">
                {readiness.setupChecklist.map((item) => (
                  <div key={item.code} className={`readiness-checklist-item${item.isSatisfied ? ' satisfied' : ''}`}>
                    <div className="readiness-checklist-title-row">
                      <strong>{item.title}</strong>
                      <span className={`readiness-issue-severity ${item.severity.toLowerCase()}`}>
                        {item.isSatisfied ? 'Done' : item.severity}
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
                ))}
              </div>
            </div>
          )}

          {readiness.issues.length > 0 ? (
            <div className="readiness-issues">
              {readiness.issues.map((issue, index) => {
                const relatedValue = issue.relatedValue || '';
                const providerLabel = issue.inputProvider ? formatInputProvider(issue.inputProvider) : null;
                const providerSuffix = providerLabel ? ` (${providerLabel})` : '';

                return (
                  <div key={`${issue.code}-${index}`} className={`readiness-issue ${issue.severity.toLowerCase()}`}>
                    <div className="readiness-issue-title-row">
                      <strong>{issue.title}</strong>
                      <span className={`readiness-issue-severity ${issue.severity.toLowerCase()}`}>{issue.severity}</span>
                    </div>
                    <div className="text-dim">{issue.message}</div>
                    {relatedValue && (
                      <div className="readiness-related-value mono">
                        {relatedValue}
                        {providerSuffix}
                      </div>
                    )}
                  </div>
                );
              })}
            </div>
          ) : (
            <div className="readiness-success-copy">
              This vessel looks ready for the currently selected workflow surface.
            </div>
          )}
        </>
      )}
    </div>
  );
}
