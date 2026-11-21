import { useEffect, useMemo, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import {
  getVesselReadiness,
  listCheckRuns,
  listVessels,
  listWorkflowProfiles,
  previewWorkflowProfileForVessel,
  runCheck,
} from '../api/client';
import { listAll } from '../api/paging';
import type { CheckRun, CheckRunRequest, CheckRunType, Vessel, VesselReadinessResult, WorkflowProfile, WorkflowProfileResolutionPreviewResult } from '../types/models';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import ActionMenu from '../components/shared/ActionMenu';
import PageHeader from '../components/shared/PageHeader';
import ErrorModal from '../components/shared/ErrorModal';
import JsonViewer from '../components/shared/JsonViewer';
import RecordDetailModal from '../components/shared/RecordDetailModal';
import ReadinessPanel from '../components/shared/ReadinessPanel';
import CopyButton from '../components/shared/CopyButton';
import DataTable, { type DataTableColumn } from '../components/shared/DataTable';
import LoadingValue from '../components/shared/LoadingValue';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import StatusBadge from '../components/shared/StatusBadge';
import WorkflowCommandPreview from '../components/shared/WorkflowCommandPreview';
import {
  buildCheckRunComparisonMap,
  formatCheckRunComparisonScope,
  formatCheckRunComparisonSummary,
} from '../lib/checkRunComparison';
import { ALL_CHECK_TYPES, getAvailableCheckTypes, requiresEnvironment, summarizeRunParsing } from '../lib/deliveryForms';

interface CheckRunPrefillState {
  prefill?: Partial<CheckRunRequest>;
}

export default function CheckRuns() {
  const navigate = useNavigate();
  const location = useLocation();
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();

  const [runs, setRuns] = useState<CheckRun[]>([]);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [profiles, setProfiles] = useState<WorkflowProfile[]>([]);
  const [loaded, setLoaded] = useState(false);
  const [error, setError] = useState('');
  const [vesselFilter, setVesselFilter] = useState('all');
  const [statusFilter, setStatusFilter] = useState<'all' | 'Passed' | 'Failed' | 'Running' | 'Pending' | 'Canceled'>('all');
  const [sourceFilter, setSourceFilter] = useState<'all' | 'Armada' | 'External'>('all');
  const [typeFilter, setTypeFilter] = useState<'all' | CheckRunType>('all');
  const [colFilters, setColFilters] = useState({ label: '', environmentName: '' });
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });
  const [viewRecord, setViewRecord] = useState<Record<string, unknown> | null>(null);

  const [showRunModal, setShowRunModal] = useState(false);
  const [running, setRunning] = useState(false);
  const [selectedVesselId, setSelectedVesselId] = useState('');
  const [selectedWorkflowProfileId, setSelectedWorkflowProfileId] = useState('');
  const [selectedType, setSelectedType] = useState<CheckRunType>('Build');
  const [environmentName, setEnvironmentName] = useState('');
  const [label, setLabel] = useState('');
  const [missionId, setMissionId] = useState('');
  const [voyageId, setVoyageId] = useState('');
  const [deploymentId, setDeploymentId] = useState('');
  const [branchName, setBranchName] = useState('');
  const [commitHash, setCommitHash] = useState('');
  const [commandOverride, setCommandOverride] = useState('');
  const [resolvedPreview, setResolvedPreview] = useState<WorkflowProfileResolutionPreviewResult | null>(null);
  const [resolvingProfile, setResolvingProfile] = useState(false);
  const [readiness, setReadiness] = useState<VesselReadinessResult | null>(null);
  const [loadingReadiness, setLoadingReadiness] = useState(false);

  async function load() {
    try {
      const [runResult, vesselResult, profileResult] = await Promise.all([
        listAll(listCheckRuns),
        listAll(listVessels),
        listAll(listWorkflowProfiles),
      ]);
      setRuns(runResult.objects || []);
      setVessels(vesselResult.objects || []);
      setProfiles(profileResult.objects || []);
      setError('');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to load check runs.'));
    } finally {
      setLoaded(true);
    }
  }

  useEffect(() => {
    load();
  }, []);
  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('checkruns', load);

  useEffect(() => {
    const state = (location.state || {}) as CheckRunPrefillState;
    if (!state.prefill) return;

    openRunModal(state.prefill);
    navigate(location.pathname, { replace: true, state: null });
  }, [location.pathname, location.state, navigate]);

  useEffect(() => {
    if (!selectedVesselId) {
      setResolvedPreview(null);
      return;
    }

    let cancelled = false;
    setResolvingProfile(true);
    previewWorkflowProfileForVessel(selectedVesselId, selectedWorkflowProfileId || undefined)
      .then((preview) => {
        if (cancelled) return;
        setResolvedPreview(preview);
      })
      .catch(() => {
        if (!cancelled) setResolvedPreview(null);
      })
      .finally(() => {
        if (!cancelled) setResolvingProfile(false);
      });

    return () => { cancelled = true; };
  }, [selectedVesselId, selectedWorkflowProfileId]);

  useEffect(() => {
    if (!selectedVesselId) {
      setReadiness(null);
      setLoadingReadiness(false);
      return;
    }

    let cancelled = false;
    setLoadingReadiness(true);
    getVesselReadiness(selectedVesselId, {
      workflowProfileId: selectedWorkflowProfileId || null,
      checkType: selectedType,
      environmentName: environmentName || null,
      includeWorkflowRequirements: commandOverride.trim().length === 0,
    })
      .then((value) => {
        if (!cancelled) setReadiness(value);
      })
      .catch(() => {
        if (!cancelled) setReadiness(null);
      })
      .finally(() => {
        if (!cancelled) setLoadingReadiness(false);
      });

    return () => {
      cancelled = true;
    };
  }, [commandOverride, environmentName, selectedType, selectedVesselId, selectedWorkflowProfileId]);

  const resolvedProfile = useMemo(() => resolvedPreview?.resolvedProfile || null, [resolvedPreview]);
  const availableTypes = useMemo(
    () => (resolvedPreview?.availableCheckTypes?.length ? resolvedPreview.availableCheckTypes as CheckRunType[] : getAvailableCheckTypes(resolvedProfile)),
    [resolvedPreview, resolvedProfile],
  );
  const environmentOptions = useMemo(() => resolvedProfile?.environments || [], [resolvedProfile]);
  const runBlocked = (readiness?.errorCount || 0) > 0;

  useEffect(() => {
    if (commandOverride.trim()) return;
    if (availableTypes.includes(selectedType)) return;
    setSelectedType(availableTypes[0] || 'Build');
  }, [availableTypes, commandOverride, selectedType]);

  useEffect(() => {
    if (!requiresEnvironment(selectedType)) {
      if (environmentName) setEnvironmentName('');
      return;
    }

    if (environmentOptions.length === 1 && !environmentName) {
      setEnvironmentName(environmentOptions[0].environmentName);
    }
  }, [environmentName, environmentOptions, selectedType]);

  const filtered = useMemo(() => runs.filter((run) => {
    const matchesVessel = vesselFilter === 'all' || run.vesselId === vesselFilter;
    const matchesStatus = statusFilter === 'all' || run.status === statusFilter;
    const matchesSource = sourceFilter === 'all' || run.source === sourceFilter;
    const matchesType = typeFilter === 'all' || run.type === typeFilter;
    const matchesColFilters = (!colFilters.label || (run.label || run.type || '').toLowerCase().includes(colFilters.label.toLowerCase()))
      && (!colFilters.environmentName || (run.environmentName ?? '').toLowerCase().includes(colFilters.environmentName.toLowerCase()));
    return matchesVessel && matchesStatus && matchesSource && matchesType && matchesColFilters;
  }), [colFilters, runs, sourceFilter, statusFilter, typeFilter, vesselFilter]);
  const comparisonMap = useMemo(() => buildCheckRunComparisonMap(runs), [runs]);

  const vesselMap = useMemo(() => new Map(vessels.map((vessel) => [vessel.id, vessel.name])), [vessels]);

  const tips = {
    vessel: t('Repository to run the check in. The command runs in its working directory, on the Admiral or on the Harbor that holds its checkout.'),
    workflowProfile: t('Profile whose command for the chosen check type runs. Blank uses the vessel\'s profile, then its fleet\'s, then the global default.'),
    checkType: t('Which profile command to run, such as Build, UnitTest, or Deploy. Only types the resolved profile has a command for are listed.'),
    environment: t('Environment whose command to run, from the profile\'s environment entries. Required for deploy, rollback, smoke, health, and verification checks.'),
    label: t('Display name for this run, such as Nightly build. Blank shows the check type.'),
    missionId: t('Mission to link this run to as evidence, such as msn_abc123. Optional.'),
    voyageId: t('Voyage to link this run to as evidence, such as vyg_abc123. Optional.'),
    deploymentId: t('Deployment to link this run to, such as dpl_abc123. The run then appears in that deployment\'s checks.'),
    branch: t('Branch to record on the run, such as main. Used for comparisons; Armada does not check it out, the command runs on the working directory as it is.'),
    commit: t('Commit hash to record on the run. Used for evidence only; Armada does not check it out.'),
    commandOverride: t('Shell command to run instead of the profile command, such as npm test. Runs in the vessel\'s working directory with the same permissions as Armada.'),
  };
  const passedCount = runs.filter((run) => run.status === 'Passed').length;
  const failedCount = runs.filter((run) => run.status === 'Failed').length;
  const runningCount = runs.filter((run) => run.status === 'Running').length;

  function resetRunModal(prefill?: Partial<CheckRunRequest>) {
    setSelectedVesselId(prefill?.vesselId || '');
    setSelectedWorkflowProfileId(prefill?.workflowProfileId || '');
    setSelectedType(prefill?.type || 'Build');
    setEnvironmentName(prefill?.environmentName || '');
    setLabel(prefill?.label || '');
    setMissionId(prefill?.missionId || '');
    setVoyageId(prefill?.voyageId || '');
    setDeploymentId(prefill?.deploymentId || '');
    setBranchName(prefill?.branchName || '');
    setCommitHash(prefill?.commitHash || '');
    setCommandOverride(prefill?.commandOverride || '');
  }

  function openRunModal(prefill?: Partial<CheckRunRequest>) {
    resetRunModal(prefill);
    setShowRunModal(true);
  }

  async function handleRunCheck() {
    if (!selectedVesselId) {
      setError(t('Select a vessel before running a check.'));
      return;
    }

    if (requiresEnvironment(selectedType) && !environmentName && !commandOverride.trim()) {
      setError(t('Select an environment for this check type.'));
      return;
    }

    try {
      setRunning(true);
      const run = await runCheck({
        vesselId: selectedVesselId,
        workflowProfileId: selectedWorkflowProfileId || null,
        type: selectedType,
        environmentName: environmentName || null,
        label: label || null,
        missionId: missionId || null,
        voyageId: voyageId || null,
        deploymentId: deploymentId || null,
        branchName: branchName || null,
        commitHash: commitHash || null,
        commandOverride: commandOverride || null,
      });
      pushToast(run.status === 'Passed' ? 'success' : run.status === 'Failed' ? 'warning' : 'info', t('Check run "{{id}}" completed with status {{status}}.', { id: run.id, status: run.status }));
      setShowRunModal(false);
      await load();
      navigate(`/checks/${run.id}`);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Check run failed.'));
    } finally {
      setRunning(false);
    }
  }

  const columns: DataTableColumn<CheckRun>[] = [
    {
      key: 'id', label: t('ID'), required: true, headerTitle: t('Check run ID (chk_ prefix), used by the REST API, MCP tools, and links.'), cellClassName: 'mono text-dim table-id-cell',
      render: (run) => (
        <span className="id-display">
          <span className="id-value" title={run.id}>{run.id}</span>
          <CopyButton text={run.id} onClick={e => e.stopPropagation()} />
        </span>
      ),
    },
    {
      key: 'check', label: t('Check'), required: true, headerTitle: t('Run label, or the check type when no label was given.'),
      filter: <input type="text" className="col-filter" aria-label={t('Filter by check')} title={t('Show only runs whose label or type contains this text.')} value={colFilters.label} onChange={e => setColFilters(f => ({ ...f, label: e.target.value }))} placeholder={t('Filter...')} />,
      // One line: type, parsed results, comparison and ID were stacked under the label; each is its own column now.
      cellTitle: (run) => `${run.label || run.type} (${run.type})`,
      render: (run) => <strong className="cell-one-line">{run.label || run.type}</strong>,
    },
    { key: 'type', label: t('Type'), headerTitle: t('Which workflow profile command ran, such as Build, UnitTest, Deploy, or HealthCheck.'), cellClassName: 'text-dim cell-nowrap', render: (run) => run.type },
    {
      key: 'results', label: t('Results'), headerTitle: t('Test and coverage counts parsed from the run\'s output or result files, when it produced any.'), cellClassName: 'text-dim',
      render: (run) => {
        const summary = summarizeRunParsing(run);
        return summary ? <span className="cell-one-line" title={summary}>{summary}</span> : '-';
      },
    },
    {
      key: 'comparison', label: t('Comparison'), headerTitle: t('Change in test results versus the previous run of the same type, preferring the same branch, profile, or environment.'),
      render: (run) => {
        const comparison = comparisonMap.get(run.id);
        if (!comparison) return <span className="text-dim">-</span>;
        const scope = `vs ${formatCheckRunComparisonScope(comparison.scope)}`;
        const summary = formatCheckRunComparisonSummary(comparison);
        return (
          <span
            className={`check-run-comparison-line check-run-comparison-inline${comparison.hasRegression ? ' regression' : comparison.hasImprovement ? ' improvement' : ''}`}
            title={`${scope}: ${summary}`}
          >
            <span className="check-run-comparison-context">{scope}</span>
            <span className="cell-one-line">{summary}</span>
          </span>
        );
      },
    },
    { key: 'vessel', label: t('Vessel'), headerTitle: t('Repository the check ran against.'), render: (run) => (run.vesselId ? (vesselMap.get(run.vesselId) || run.vesselId) : '-') },
    { key: 'status', label: t('Status'), headerTitle: t('Outcome: Passed or Failed from the command\'s exit code, or Pending, Running, or Canceled.'), cellClassName: 'cell-nowrap', render: (run) => <StatusBadge status={run.status} /> },
    { key: 'source', label: t('Source'), headerTitle: t('Armada means Armada ran the command; External means it was imported, for example from GitHub Actions.'), cellClassName: 'text-dim', render: (run) => (run.source === 'External' ? `${run.source}${run.providerName ? ` / ${run.providerName}` : ''}` : run.source) },
    {
      key: 'environment', label: t('Environment'), headerTitle: t('Environment the check targeted, for deploy and verification checks.'), cellClassName: 'text-dim',
      clearFilter: () => setColFilters(f => ({ ...f, environmentName: '' })),
      filter: <input type="text" className="col-filter" aria-label={t('Filter by environment')} title={t('Show only runs whose environment name contains this text.')} value={colFilters.environmentName} onChange={e => setColFilters(f => ({ ...f, environmentName: e.target.value }))} placeholder={t('Filter...')} />,
      render: (run) => run.environmentName || '-',
    },
    { key: 'duration', label: t('Duration'), headerTitle: t('How long the command ran, in milliseconds.'), cellClassName: 'text-dim cell-nowrap', render: (run) => (run.durationMs != null ? `${Math.round(run.durationMs)} ms` : '-') },
    {
      key: 'created', label: t('Created'), headerTitle: t('When the run started. Hover a cell for the exact time.'), cellClassName: 'text-dim cell-nowrap',
      cellTitle: (run) => formatDateTime(run.createdUtc),
      render: (run) => formatRelativeTime(run.createdUtc),
    },
    {
      key: 'actions', label: t('Actions'), fixed: true, interactive: true, className: 'text-right',
      render: (run) => (
        <ActionMenu
          id={`check-run-${run.id}`}
          items={[
            { label: 'Open', title: 'Open the check run with its full output, parsed results, and artifacts.', onClick: () => navigate(`/checks/${run.id}`) },
            {
              label: 'Draft Release',
              title: 'Start a new release prefilled with this check run and its mission and voyage as evidence.',
              onClick: () => navigate('/releases/new', {
                state: {
                  prefill: {
                    vesselId: run.vesselId || null,
                    voyageIds: run.voyageId ? [run.voyageId] : [],
                    missionIds: run.missionId ? [run.missionId] : [],
                    checkRunIds: [run.id],
                    title: run.label ? `${run.label} Release` : `${run.type} Release`,
                  },
                },
              }),
            },
            { label: 'View JSON', title: 'Show the raw check run record as returned by the API.', onClick: () => setJsonData({ open: true, title: run.label || run.id, data: run }) },
          ]}
        />
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title={t('Checks')}
        subtitle={t('Structured build, test, deploy, and verification runs with durable output, artifacts, and retry support.')}
        actions={(
          <>
            <button className="btn btn-primary" onClick={() => openRunModal()} title={t('Run one of a vessel\'s workflow profile commands, such as a build or test, and record its output as evidence.')}>
              + {t('Run Check')}
            </button>
          </>
        )}
      />

      <ErrorModal error={error} onClose={() => setError('')} />
      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <RecordDetailModal
        open={!!viewRecord}
        title={viewRecord ? String(viewRecord.label || viewRecord.type || viewRecord.id || '') : ''}
        subtitle={viewRecord ? String(viewRecord.type || '') : undefined}
        record={viewRecord}
        onClose={() => setViewRecord(null)}
        onEdit={() => {
          const id = viewRecord?.id;
          setViewRecord(null);
          if (id) navigate(`/checks/${String(id)}`);
        }}
        editLabel={t('Open Details')}
      />

      {showRunModal && (
        <div className="modal-overlay" onClick={() => !running && setShowRunModal(false)}>
          <div className="modal modal-lg" onClick={(event) => event.stopPropagation()}>
            <h3>{t('Run Check')}</h3>
            <div className="detail-grid" style={{ marginBottom: '1rem' }}>
              <label className="playbook-editor-field" title={tips.vessel}>
                <span>{t('Vessel')}</span>
                <select title={tips.vessel} value={selectedVesselId} onChange={(event) => setSelectedVesselId(event.target.value)}>
                  <option value="">{t('Select a vessel...')}</option>
                  {vessels.map((vessel) => (
                    <option key={vessel.id} value={vessel.id}>{vessel.name}</option>
                  ))}
                </select>
              </label>
              <label className="playbook-editor-field" title={tips.workflowProfile}>
                <span>{t('Workflow Profile')}</span>
                <select title={tips.workflowProfile} value={selectedWorkflowProfileId} onChange={(event) => setSelectedWorkflowProfileId(event.target.value)}>
                  <option value="">{t('Resolved default')}</option>
                  {profiles.map((profile) => (
                    <option key={profile.id} value={profile.id}>{profile.name}</option>
                  ))}
                </select>
              </label>
              <label className="playbook-editor-field" title={tips.checkType}>
                <span>{t('Check Type')}</span>
                <select title={tips.checkType} value={selectedType} onChange={(event) => setSelectedType(event.target.value as CheckRunType)}>
                  {availableTypes.map((type) => (
                    <option key={type} value={type}>{type}</option>
                  ))}
                </select>
              </label>
              {requiresEnvironment(selectedType) && (
                <label className="playbook-editor-field" title={tips.environment}>
                  <span>{t('Environment')}</span>
                  <select title={tips.environment} value={environmentName} onChange={(event) => setEnvironmentName(event.target.value)}>
                    <option value="">{t('Select an environment...')}</option>
                    {environmentOptions.map((environment) => (
                      <option key={environment.environmentName} value={environment.environmentName}>{environment.environmentName}</option>
                    ))}
                  </select>
                </label>
              )}
            </div>

            <div className="detail-grid" style={{ marginBottom: '1rem' }}>
              <label className="playbook-editor-field" title={tips.label}>
                <span>{t('Label')}</span>
                <input title={tips.label} type="text" value={label} onChange={(event) => setLabel(event.target.value)} placeholder={t('Optional display label')} />
              </label>
              <label className="playbook-editor-field" title={tips.missionId}>
                <span>{t('Mission ID')}</span>
                <input title={tips.missionId} type="text" value={missionId} onChange={(event) => setMissionId(event.target.value)} placeholder="msn_..." />
              </label>
              <label className="playbook-editor-field" title={tips.voyageId}>
                <span>{t('Voyage ID')}</span>
                <input title={tips.voyageId} type="text" value={voyageId} onChange={(event) => setVoyageId(event.target.value)} placeholder="vyg_..." />
              </label>
              <label className="playbook-editor-field" title={tips.deploymentId}>
                <span>{t('Deployment ID')}</span>
                <input title={tips.deploymentId} type="text" value={deploymentId} onChange={(event) => setDeploymentId(event.target.value)} placeholder="dpl_..." />
              </label>
              <label className="playbook-editor-field" title={tips.branch}>
                <span>{t('Branch')}</span>
                <input title={tips.branch} type="text" value={branchName} onChange={(event) => setBranchName(event.target.value)} />
              </label>
              <label className="playbook-editor-field" title={tips.commit}>
                <span>{t('Commit')}</span>
                <input title={tips.commit} type="text" value={commitHash} onChange={(event) => setCommitHash(event.target.value)} />
              </label>
            </div>

            <label className="playbook-editor-field" title={tips.commandOverride} style={{ marginBottom: '1rem' }}>
              <span>{t('Command Override')}</span>
              <textarea title={tips.commandOverride}
                value={commandOverride}
                onChange={(event) => setCommandOverride(event.target.value)}
                rows={4}
                placeholder={t('Optional ad-hoc command. Leave blank to use the selected or resolved workflow profile command.')}
              />
            </label>

            <div className="card" style={{ marginBottom: '1rem', padding: '1rem' }}>
              <strong title={t('The workflow profile this run will use, and the commands it defines for this vessel.')}>{t('Resolved Profile')}</strong>
              <div className="text-dim" style={{ marginTop: '0.4rem' }}>
                {resolvingProfile
                  ? t('Resolving...')
                  : resolvedProfile
                    ? `${resolvedProfile.name} (${resolvedProfile.scope})`
                    : t('No workflow profile resolved. Provide a command override or select a profile.')}
              </div>
              {!resolvingProfile && resolvedPreview?.resolvedProfile && (
                <>
                  <div className="text-dim" style={{ marginTop: '0.35rem', fontSize: '0.85rem' }}>
                    {t('Resolution mode')}: {resolvedPreview.resolutionMode}
                  </div>
                  <div className="text-dim" style={{ marginTop: '0.25rem', fontSize: '0.85rem' }}>
                    {t('Available check types')}: {resolvedPreview.availableCheckTypes.length > 0 ? resolvedPreview.availableCheckTypes.join(', ') : t('None')}
                  </div>
                  <div style={{ marginTop: '0.75rem' }}>
                    <WorkflowCommandPreview
                      commands={resolvedPreview.commandPreviews}
                      emptyMessage={t('No resolved commands are available for this vessel/profile combination.')}
                    />
                  </div>
                </>
              )}
            </div>

            <ReadinessPanel
              title={t('Preflight')}
              readiness={readiness}
              loading={loadingReadiness}
              emptyMessage={t('Select a vessel to evaluate readiness.')}
              compact
            />

            <div className="modal-actions">
              <button className="btn btn-primary" disabled={running || runBlocked} onClick={handleRunCheck} title={runBlocked ? t('Fix the errors listed under Preflight before running.') : t('Run the command now and wait for it to finish, then open the result.')}>
                {running ? t('Running...') : t('Run Check')}
              </button>
              <button className="btn" disabled={running} onClick={() => setShowRunModal(false)} title={t('Close without running.')}>
                {t('Cancel')}
              </button>
            </div>
          </div>
        </div>
      )}

      <div className="playbook-overview-grid">
        <div className="card playbook-overview-card" title={t('All check runs you can see, before filters.')}>
          <span>{t('Total Runs')}</span>
          <strong><LoadingValue loading={!loaded}>{runs.length}</LoadingValue></strong>
        </div>
        <div className="card playbook-overview-card" title={t('Runs whose command exited successfully.')}>
          <span>{t('Passed')}</span>
          <strong><LoadingValue loading={!loaded}>{passedCount}</LoadingValue></strong>
        </div>
        <div className="card playbook-overview-card" title={t('Runs whose command failed or timed out.')}>
          <span>{t('Failed')}</span>
          <strong><LoadingValue loading={!loaded}>{failedCount}</LoadingValue></strong>
        </div>
        <div className="card playbook-overview-card" title={t('Runs still in progress.')}>
          <span>{t('Running')}</span>
          <strong><LoadingValue loading={!loaded}>{runningCount}</LoadingValue></strong>
        </div>
      </div>

      <div className="card" style={{ padding: '1rem', marginBottom: '1rem' }}>
        <div className="playbook-filter-row">
          <select aria-label={t('All vessels')} title={t('Show only check runs for this vessel.')} value={vesselFilter} onChange={(event) => setVesselFilter(event.target.value)}>
            <option value="all">{t('All vessels')}</option>
            {vessels.map((vessel) => (
              <option key={vessel.id} value={vessel.id}>{vessel.name}</option>
            ))}
          </select>
          <select aria-label={t('All statuses')} title={t('Show only check runs with this outcome.')} value={statusFilter} onChange={(event) => setStatusFilter(event.target.value as typeof statusFilter)}>
            <option value="all">{t('All statuses')}</option>
            <option value="Passed">{t('Passed')}</option>
            <option value="Failed">{t('Failed')}</option>
            <option value="Running">{t('Running')}</option>
            <option value="Pending">{t('Pending')}</option>
            <option value="Canceled">{t('Canceled')}</option>
          </select>
          <select aria-label={t('All sources')} title={t('Show only runs Armada executed, or only runs imported from an external provider.')} value={sourceFilter} onChange={(event) => setSourceFilter(event.target.value as typeof sourceFilter)}>
            <option value="all">{t('All sources')}</option>
            <option value="Armada">{t('Armada')}</option>
            <option value="External">{t('External')}</option>
          </select>
          <select aria-label={t('All check types')} title={t('Show only check runs of this type.')} value={typeFilter} onChange={(event) => setTypeFilter(event.target.value as typeof typeFilter)}>
            <option value="all">{t('All check types')}</option>
            {ALL_CHECK_TYPES.map((type) => (
              <option key={type} value={type}>{type}</option>
            ))}
          </select>
        </div>
      </div>

      <DataTable
        tableKey="checkruns"
        columns={columns}
        rows={filtered}
        rowKey={(run) => run.id}
        onRowClick={(run) => setViewRecord(run as unknown as Record<string, unknown>)}
        autoRefresh={{ seconds: refreshSeconds, onChange: setRefreshSeconds }}
        onRefresh={load}
        refreshTitle={t('Refresh check runs')}
        loading={!loaded}
        placeholder={!loaded ? undefined : filtered.length === 0 ? (
          <div className="playbook-empty-state">
            <strong>{t('No check runs match the current filters.')}</strong>
            <span>{t('Run a structured check to capture build, test, or deploy evidence for a vessel.')}</span>
          </div>
        ) : undefined}
      />
    </div>
  );
}
