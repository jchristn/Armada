import { Link } from 'react-router-dom';
import type { VesselReadinessResult } from '../../types/models';
import { formatInputProvider, readinessBranchSummary, readinessCheckout, readinessCheckoutText, readinessDriftSummary, readinessLabel, readinessTone } from '../../lib/readiness';
import { useLocale } from '../../context/LocaleContext';
import CopyButton from './CopyButton';

interface ReadinessPanelProps {
  title: string;
  readiness: VesselReadinessResult | null;
  loading?: boolean;
  emptyMessage?: string;
  compact?: boolean;
  /**
   * Shown inside another summary (the Workspace readiness strip) that already carries the title, status, profile,
   * check types and checkout: render only what is missing and the issues, with no card or header of its own.
   */
  embedded?: boolean;
}

export default function ReadinessPanel(props: ReadinessPanelProps) {
  const { title, readiness, loading = false, emptyMessage = 'No readiness data.', embedded = false } = props;
  const compact = props.compact || embedded;
  const tone = readinessTone(readiness);
  const label = readinessLabel(readiness);
  const branchSummary = readinessBranchSummary(readiness);
  const aheadBehindSummary = readinessDriftSummary(readiness);
  const { t } = useLocale();
  const checkout = readinessCheckout(readiness);
  const checkoutText = checkout ? readinessCheckoutText(checkout) : null;

  return (
    <div className={embedded ? 'readiness-panel embedded' : `card readiness-panel${compact ? ' compact' : ''}`}>
      {!embedded && (
      <div className="readiness-panel-header">
        <div>
          <h3>{title}</h3>
          {readiness?.workflowProfileName && (
            <div className="readiness-panel-meta" title={t('The workflow profile that applies to this vessel, chosen from its own, its fleet\'s, or the global profile. Its commands decide which checks can run.')}>
              Resolved profile: <strong>{readiness.workflowProfileName}</strong>
              {readiness.workflowProfileScope ? ` (${readiness.workflowProfileScope})` : ''}
            </div>
          )}
          {!compact && readiness && readiness.setupChecklistTotalCount > 0 && (
            <div className="readiness-panel-meta" title={t('Setup checklist items this vessel satisfies, out of the total.')}>
              Onboarding: <strong>{readiness.setupChecklistSatisfiedCount}/{readiness.setupChecklistTotalCount}</strong> steps complete
            </div>
          )}
        </div>
        <span className={`readiness-pill ${tone}`} title={t('Ready: no issues. Needs Attention: warnings only, work can still run. Blocked: at least one error stops missions or checks until fixed. Unknown: not checked yet.')}>{label}</span>
      </div>
      )}

      {loading ? (
        <div className="text-dim">Checking readiness...</div>
      ) : !readiness ? (
        <div className="text-dim">{emptyMessage}</div>
      ) : (
        <>
          {(!embedded || !readiness.hasWorkingDirectory || !readiness.hasRepositoryContext) && (
          <div className="readiness-summary-row">
            {(!embedded || !readiness.hasWorkingDirectory) && <span title={t('Whether a checkout of this vessel exists, on the Admiral host or a connected Harbor. Checks, workspace browsing and Local Merge need one.')}>{readiness.hasWorkingDirectory ? 'Working directory available' : 'Working directory unavailable'}</span>}
            {(!embedded || !readiness.hasRepositoryContext) && <span title={t('Whether the Admiral has its bare clone of the repository, or can recreate it from the repository URL. Missions need it to create worktrees.')}>{readiness.hasRepositoryContext ? 'Repository context available' : 'Repository context unavailable'}</span>}
            {!embedded && readiness.availableCheckTypes.length > 0 && (
              <span title={t('Kinds of structured checks (build, test, deploy and so on) the resolved workflow profile defines commands for.')}>{readiness.availableCheckTypes.length} check type(s) available</span>
            )}
          </div>
          )}

          {!embedded && checkout && checkoutText && (
            <div className={`readiness-checkout${checkout.kind === 'unavailable' ? ' unavailable' : ''}`} data-testid="readiness-checkout">
              <span className="readiness-checkout-label" title={t('Where checks and workspace actions for this vessel run: its working directory on the Admiral host, or a Harbor\'s copy.')}>{t('Checkout')}:</span>
              <span className="readiness-checkout-where">{t(checkoutText.template, checkoutText.params)}</span>
              {checkout.kind === 'harbor' && (
                <span className="readiness-checkout-id">
                  <span className="text-dim">{t('Harbor ID')}</span>
                  <code>{checkout.harborId}</code>
                  <CopyButton text={checkout.harborId} title="Copy Harbor ID" />
                </span>
              )}
            </div>
          )}

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
                  <div className="readiness-info-label" title={t('Branch checked out in the working directory, and the vessel\'s default branch.')}>Branch</div>
                  <div className="mono">{branchSummary}</div>
                </div>
              )}
              {aheadBehindSummary && (
                <div className="readiness-info-card">
                  <div className="readiness-info-label" title={t('How many commits the working directory is ahead of and behind the remote default branch, after a fetch.')}>Remote drift</div>
                  <div>{aheadBehindSummary}</div>
                </div>
              )}
              {readiness.hasUncommittedChanges != null && (
                <div className="readiness-info-card">
                  <div className="readiness-info-label" title={t('Whether the working directory has uncommitted changes. Local Merge and some fleet actions skip a dirty tree.')}>Working tree</div>
                  <div>{readiness.hasUncommittedChanges ? 'Uncommitted changes present' : 'Clean working tree'}</div>
                </div>
              )}
              {readiness.detectedToolchains.length > 0 && (
                <div className="readiness-info-card">
                  <div className="readiness-info-label" title={t('Build tools Armada recognized from files in the checkout, such as dotnet, node or python.')}>Detected toolchains</div>
                  <div className="readiness-inline-list">
                    {readiness.detectedToolchains.map((item) => (
                      <span key={item} className="readiness-type-pill">{item}</span>
                    ))}
                  </div>
                </div>
              )}
              {readiness.toolchainProbes.length > 0 && (
                <div className="readiness-info-card">
                  <div className="readiness-info-label" title={t('Each tool Armada tried to run on the checkout host, with the version found or missing. Expected means the profile\'s commands need it.')}>Toolchain probes</div>
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
                  <div className="readiness-info-label" title={t('Deployment environments defined for this vessel.')}>Environments</div>
                  <div className="readiness-inline-list">
                    {readiness.deploymentEnvironments.map((item) => (
                      <span key={item} className="readiness-type-pill">{item}</span>
                    ))}
                  </div>
                </div>
              )}
              {readiness.deploymentMetadata && (
                <div className="readiness-info-card">
                  <div className="readiness-info-label" title={t('Which delivery commands the workflow profile defines: deploy, rollback, smoke test, health check, and their verifications. Missing ones cannot run.')}>Delivery coverage</div>
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
                  <Link to={`/vessels/${readiness.vesselId}/onboarding`} className="btn btn-sm" style={{ marginLeft: '0.75rem' }} title={t('Open this vessel\'s onboarding page to work through the remaining checklist items.')}>
                    Open Onboarding
                  </Link>
                )}
              </div>
              <div className="readiness-checklist-items">
                {readiness.setupChecklist.map((item) => (
                  <div key={item.code} className={`readiness-checklist-item${item.isSatisfied ? ' satisfied' : ''}`}>
                    <div className="readiness-checklist-title-row">
                      <strong>{item.title}</strong>
                      <span className={`readiness-issue-severity ${item.isSatisfied ? 'done' : item.severity.toLowerCase()}`} title={t('Done: satisfied. Error: blocks work until fixed. Warning: recommended. Info: for your information.')}>
                        {item.isSatisfied ? t('Done') : t(item.severity)}
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

          {(readiness.issueCount ?? 0) > readiness.issues.length && (
            <p className="text-dim readiness-issues-capped">{t('Showing the {{shown}} most severe of {{total}} issues.', { shown: String(readiness.issues.length), total: String(readiness.issueCount) })}</p>
          )}
          {readiness.issues.length > 0 ? (
            <div className="readiness-issues">
              {readiness.issues.map((issue, index) => {
                const relatedValue = issue.relatedValue || '';
                const providerLabel = issue.inputProvider ? formatInputProvider(issue.inputProvider) : null;
                const providerSuffix = providerLabel ? ` (${providerLabel})` : '';

                const severityTag = <span className={`readiness-issue-severity ${issue.severity.toLowerCase()}`} title={t('Done: satisfied. Error: blocks work until fixed. Warning: recommended. Info: for your information.')}>{issue.severity}</span>;
                // Info items are for reference only: collapsed to their title row until opened.
                if (issue.severity === 'Info') {
                  return (
                    <details key={`${issue.code}-${index}`} className="readiness-issue info readiness-issue-collapsible" data-testid={`readiness-issue-${issue.code}`}>
                      <summary className="readiness-issue-title-row" title={t('Show or hide the details of this note.')}>
                        <strong>{issue.title}</strong>
                        {severityTag}
                      </summary>
                      <div className="text-dim">{issue.message}</div>
                      {relatedValue && (
                        <div className="readiness-related-value mono" title={t('The value this issue is about, such as a path, command or input name.')}>
                          {relatedValue}
                          {providerSuffix}
                        </div>
                      )}
                    </details>
                  );
                }

                return (
                  <div key={`${issue.code}-${index}`} className={`readiness-issue ${issue.severity.toLowerCase()}`}>
                    <div className="readiness-issue-title-row">
                      <strong>{issue.title}</strong>
                      {severityTag}
                    </div>
                    <div className="text-dim">{issue.message}</div>
                    {issue.actionHref && issue.actionLabel && (
                      <Link className="readiness-issue-action" to={issue.actionHref} title={t(issue.actionLabel)}>
                        {t(issue.actionLabel)}
                      </Link>
                    )}
                    {relatedValue && (
                      <div className="readiness-related-value mono" title={t('The value this issue is about, such as a path, command or input name.')}>
                        {relatedValue}
                        {providerSuffix}
                      </div>
                    )}
                  </div>
                );
              })}
            </div>
          ) : embedded ? (
            <div className="text-dim">{t('No readiness issues.')}</div>
          ) : (
            <div className="readiness-success-copy">
              {t('This vessel looks ready for the currently selected workflow surface.')}
            </div>
          )}
        </>
      )}
    </div>
  );
}
