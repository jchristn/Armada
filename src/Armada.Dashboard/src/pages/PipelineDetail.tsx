import { useState, useEffect, useCallback } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import { dispatchWarningsFrom } from '../lib/dispatchPreview';
import DispatchPreview from '../components/blackbox/DispatchWarnings';
import { createPipeline, getPipeline, updatePipeline, deletePipeline, listPersonas, listVessels, createVoyage, apiErrorMessage } from '../api/client';
import { listAll } from '../api/paging';
import type { Pipeline, PipelineStage, Vessel } from '../types/models';
import { useAuth } from '../context/AuthContext';
import { canEdit as canEditScoped, type ScopeViewer } from '../lib/scoping';
import ActionMenu from '../components/shared/ActionMenu';
import PageHeader from '../components/shared/PageHeader';
import JsonViewer from '../components/shared/JsonViewer';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import CopyButton from '../components/shared/CopyButton';
import DataTable from '../components/shared/DataTable';
import ErrorModal from '../components/shared/ErrorModal';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import { buildPipelineDuplicatePayload } from '../lib/duplicates';
import { useDeleteProgress } from '../lib/useDeleteProgress';

interface StageForm {
  personaName: string;
  isOptional: boolean;
  description: string;
  requiresReview: boolean;
  reviewDenyAction: 'RetryStage' | 'FailPipeline';
}

function PipelineFlow({ stages, label }: { stages: PipelineStage[]; label: (key: string) => string }) {
  const ordered = stages.slice().sort((a, b) => a.order - b.order);
  if (ordered.length === 0) return <p className="text-dim">{label('No stages defined.')}</p>;
  return (
    <div className="pipeline-flow" style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'stretch', gap: '0.25rem' }}>
      {ordered.map((stage, i) => (
        <div key={stage.id || i} style={{ display: 'flex', alignItems: 'center' }}>
          <div
            className="card"
            style={{ padding: '0.6rem 0.8rem', minWidth: '140px', borderLeft: stage.requiresReview ? '3px solid var(--warning, #d98a00)' : undefined }}
            title={stage.description || ''}
          >
            <div className="text-dim" style={{ fontSize: '0.7rem' }}>{label('Stage')} {i + 1}</div>
            <strong>{stage.personaName || label('(unset)')}</strong>
            <div style={{ marginTop: '0.3rem', display: 'flex', gap: '0.3rem', flexWrap: 'wrap' }}>
              {stage.isOptional && <span className="badge badge-info" title={label('Marked optional. This is reserved and has no effect today: the stage still runs.')}>{label('Optional')}</span>}
              {stage.requiresReview && <span className="badge badge-warning" title={label('Review gate: the pipeline pauses after this stage until a person approves its work.')}>{label('Review')}</span>}
            </div>
          </div>
          {i < ordered.length - 1 && <span style={{ padding: '0 0.4rem', color: 'var(--text-dim)' }}>{'→'}</span>}
        </div>
      ))}
    </div>
  );
}

const PIPELINE_DESCRIPTION_TIP = 'Optional note on what this pipeline is for, shown in lists and pickers.';
const STAGE_PERSONA_TIP = 'The persona (role) for this stage, such as Architect, Worker or Judge. The stage becomes a mission routed to a captain for that persona. Required.';
const STAGE_OPTIONAL_TIP = 'Reserved: the setting is saved but has no effect today. Every stage runs whether or not it is marked optional.';
const STAGE_REVIEW_TIP = 'Review gate: when this stage finishes, the pipeline pauses until a person approves its work in Review. Off lets the next stage start right away.';
const STAGE_DENY_TIP = 'What happens when a reviewer denies this stage: Retry stage sends it back to rework the same mission; Fail pipeline fails it and cancels the stages after it. Needs Review gate on.';
const RUN_VESSEL_TIP = 'The repository the pipeline works on. Every stage mission runs against this vessel. Required.';
const RUN_TITLE_TIP = 'Name for the voyage and its missions, shown in lists, for example Add CSV export. Required.';
const RUN_DESCRIPTION_TIP = 'What you want done, in plain words. It becomes the mission instructions each stage works from. Optional, but captains do much better with a clear goal.';

export default function PipelineDetail() {
  const { t, formatDateTime } = useLocale();
  const deletion = useDeleteProgress();
  const { pushToast } = useNotifications();
  const { isAdmin, isTenantAdmin, user } = useAuth();
  const viewer: ScopeViewer = { isAdmin, isTenantAdmin, tenantId: user?.user?.tenantId, userId: user?.user?.id };
  const { name } = useParams<{ name: string }>();
  const navigate = useNavigate();
  const [pipeline, setPipeline] = useState<Pipeline | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Live run-mode
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [showRun, setShowRun] = useState(false);
  const [runVesselId, setRunVesselId] = useState('');
  const [runTitle, setRunTitle] = useState('');
  const [runDescription, setRunDescription] = useState('');
  const [running, setRunning] = useState(false);
  const canManage = pipeline ? canEditScoped(viewer, pipeline) : (isAdmin || isTenantAdmin);

  // Edit modal
  const [showForm, setShowForm] = useState(false);
  const [form, setForm] = useState<{ description: string; stages: StageForm[] }>({ description: '', stages: [] });
  const [personaNames, setPersonaNames] = useState<string[]>([]);

  // JSON viewer
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });

  // Confirm
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

  const load = useCallback(async () => {
    if (!name) return;
    try {
      setLoading(true);
      const isInitialLoad = !pipeline;
      const found = await getPipeline(name);
      setPipeline(found);
      const personaResult = await listAll(listPersonas);
      setPersonaNames(personaResult.objects.map(p => p.name));
      try {
        const vesselResult = await listAll(listVessels);
        setVessels(vesselResult.objects || []);
      } catch { /* vessels are optional for run-mode */ }
      if (isInitialLoad) setError('');
    } catch {
      setError(t('Failed to load pipeline.'));
    } finally {
      setLoading(false);
    }
  }, [name, t]);

  useEffect(() => { load(); }, [load]);

  function openEdit() {
    if (!pipeline) return;
    setForm({
      description: pipeline.description ?? '',
      stages: pipeline.stages.map(s => ({
        personaName: s.personaName,
        isOptional: s.isOptional,
        description: s.description ?? '',
        requiresReview: s.requiresReview,
        reviewDenyAction: s.reviewDenyAction,
      })),
    });
    setShowForm(true);
  }

  function duplicateStage(index: number) {
    setForm(current => ({ ...current, stages: current.stages.flatMap((stage, i) => i === index ? [stage, { ...stage }] : [stage]) }));
  }

  function addStage() {
    setForm(f => ({ ...f, stages: [...f.stages, { personaName: '', isOptional: false, description: '', requiresReview: false, reviewDenyAction: 'RetryStage' }] }));
  }

  function moveStage(index: number, direction: number) {
    setForm(f => {
      const stages = [...f.stages];
      const target = index + direction;
      if (target < 0 || target >= stages.length) return f;
      const temp = stages[index];
      stages[index] = stages[target];
      stages[target] = temp;
      return { ...f, stages };
    });
  }

  function removeStage(index: number) {
    setForm(f => ({ ...f, stages: f.stages.filter((_, i) => i !== index) }));
  }

  function updateStage(index: number, field: keyof StageForm, value: string | boolean) {
    setForm(f => ({
      ...f,
      stages: f.stages.map((s, i) => i === index ? { ...s, [field]: value } : s),
    }));
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!pipeline) return;
    try {
      const stagesPayload = form.stages
        .filter(s => s.personaName.trim() !== '')
        .map((s, i) => ({ personaName: s.personaName.trim(), isOptional: s.isOptional, description: s.description || null, requiresReview: s.requiresReview, reviewDenyAction: s.reviewDenyAction, order: i + 1 }));
      const payload = { description: form.description || null, stages: stagesPayload } as Partial<Pipeline>;
      await updatePipeline(pipeline.name, payload);
      setShowForm(false);
      pushToast('success', t('Pipeline "{{name}}" saved.', { name: pipeline.name }));
      load();
    } catch (err: unknown) { setError(apiErrorMessage(err, t('Save failed.'))); }
  }

  function handleDelete() {
    if (!pipeline) return;
    if (pipeline.isBuiltIn) {
      setError(t('Built-in pipelines cannot be deleted.'));
      return;
    }
    setConfirm({
      open: true,
      title: t('Delete Pipeline'),
      message: t('Delete pipeline "{{name}}"? This cannot be undone.', { name: pipeline.name }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        if (!(await deletion.run({ kind: 'pipeline', action: () => deletePipeline(pipeline.name) }))) return;
        pushToast('warning', t('Pipeline "{{name}}" deleted.', { name: pipeline.name }));
        navigate('/pipelines');
      },
    });
  }

  async function handleDuplicate() {
    if (!pipeline) return;
    try {
      const created = await createPipeline(buildPipelineDuplicatePayload(pipeline));
      pushToast('success', t('Pipeline "{{name}}" duplicated.', { name: created.name }));
      navigate(`/pipelines/${encodeURIComponent(created.name)}`);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Duplicate failed.'));
    }
  }

  function openRun() {
    if (!pipeline) return;
    setRunTitle(t('Run: {{name}}', { name: pipeline.name }));
    setRunDescription('');
    setRunVesselId(vessels[0]?.id ?? '');
    setShowRun(true);
  }

  async function handleRun(e: React.FormEvent) {
    e.preventDefault();
    if (!pipeline || !runVesselId) return;
    setRunning(true);
    try {
      const voyage = await createVoyage({
        title: runTitle || t('Run: {{name}}', { name: pipeline.name }),
        vesselId: runVesselId,
        pipeline: pipeline.name,
        missions: [{ vesselId: runVesselId, title: runTitle || pipeline.name, description: runDescription || undefined }],
      });
      setShowRun(false);
      pushToast('success', t('Voyage launched.'));
      navigate(`/voyages/${voyage.id}`, { state: { dispatchWarnings: dispatchWarningsFrom(voyage) } });
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Run failed.'));
    } finally {
      setRunning(false);
    }
  }

  if (loading) return <p className="text-dim">{t('Loading...')}</p>;
  if (error && !pipeline) return <ErrorModal error={error} onClose={() => setError('')} />;
  if (!pipeline) return <p className="text-dim">{t('Pipeline not found.')}</p>;

  return (
    <div>
      <PageHeader
        breadcrumb={
          <>
            <Link to="/pipelines">{t('Pipelines')}</Link> <span className="breadcrumb-sep">&gt;</span> <span>{pipeline.name}</span>
          </>
        }
        title={pipeline.name}
        actions={
          <>
            {canManage && (
              <button className="btn btn-primary" onClick={openRun} disabled={pipeline.stages.length === 0} title={t('Start a voyage on a vessel that runs this pipeline\'s stages in order. You pick the vessel and describe the work first. Needs at least one stage.')}>
                {'▶'} {t('Run')}
              </button>
            )}
            <ActionMenu id={`pipeline-${pipeline.name}`} items={[
              { label: 'View JSON', title: 'Show this pipeline\'s full record as JSON, as the API returns it.', onClick: () => setJsonData({ open: true, title: t('Pipeline: {{name}}', { name: pipeline.name }), data: pipeline }) },
              { label: 'Edit', title: 'Change this pipeline\'s description and stages.', onClick: openEdit },
              { label: 'Duplicate', title: 'Create a copy of this pipeline under a new name, with the same stages, then open it.', onClick: () => void handleDuplicate() },
              { label: 'Delete', danger: true, title: pipeline.isBuiltIn ? 'Built-in pipelines ship with Armada and cannot be deleted.' : 'Permanently delete this pipeline. Vessels, fleets and dispatches that name it can no longer use it. You confirm first.', onClick: handleDelete, disabled: pipeline.isBuiltIn },
            ]} />
          </>
        }
      />

      <ErrorModal error={error} onClose={() => setError('')} />

      {/* Edit Modal */}
      {showForm && (
        <div className="modal-overlay" onClick={() => setShowForm(false)}>
          <form className="modal" onClick={e => e.stopPropagation()} onSubmit={handleSubmit} style={{ maxWidth: '720px', width: '90%' }}>
            <h3>{t('Edit Pipeline')}</h3>
            <label title={t(PIPELINE_DESCRIPTION_TIP)}>{t('Description')}<textarea title={t(PIPELINE_DESCRIPTION_TIP)} value={form.description} onChange={e => setForm({ ...form, description: e.target.value })} rows={3} /></label>
            <div style={{ marginTop: '1rem' }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '0.5rem' }}>
                <strong title={t('Stages run top to bottom. Each stage creates a mission for its persona that starts when the stage before it finishes.')}>{t('Stages')}</strong>
                <button type="button" className="btn" onClick={addStage} title={t('Add a stage at the end of the pipeline, so it runs last.')}>+ {t('Add Stage')}</button>
              </div>
              {form.stages.map((stage, i) => (
                <div key={i} style={{ display: 'flex', flexWrap: 'wrap', gap: '0.5rem', alignItems: 'center', marginBottom: '0.5rem' }}>
                  <span className="text-dim" style={{ minWidth: '1.5rem' }}>{i + 1}.</span>
                  <select aria-label={t('Select persona...')}
                    title={t(STAGE_PERSONA_TIP)}
                    value={stage.personaName}
                    onChange={e => updateStage(i, 'personaName', e.target.value)}
                    required
                    style={{ flex: '0 1 200px', minWidth: '120px' }}
                  >
                    <option value="">{t('Select persona...')}</option>
                    {personaNames.map((personaName) => (
                      <option key={personaName} value={personaName}>{personaName}</option>
                    ))}
                  </select>
                  <label title={t(STAGE_OPTIONAL_TIP)} style={{ display: 'inline-flex', alignItems: 'center', gap: '0.35rem', margin: 0, whiteSpace: 'nowrap', lineHeight: 1, cursor: 'pointer' }}>
                    <input type="checkbox" title={t(STAGE_OPTIONAL_TIP)} checked={stage.isOptional} onChange={e => updateStage(i, 'isOptional', e.target.checked)} style={{ width: 'auto', margin: 0, verticalAlign: 'middle' }} />
                    <span style={{ verticalAlign: 'middle' }}>{t('Optional')}</span>
                  </label>
                  <label title={t(STAGE_REVIEW_TIP)} style={{ display: 'inline-flex', alignItems: 'center', gap: '0.35rem', margin: 0, whiteSpace: 'nowrap', lineHeight: 1, cursor: 'pointer' }}>
                    <input type="checkbox" title={t(STAGE_REVIEW_TIP)} checked={stage.requiresReview} onChange={e => updateStage(i, 'requiresReview', e.target.checked)} style={{ width: 'auto', margin: 0, verticalAlign: 'middle' }} />
                    <span style={{ verticalAlign: 'middle' }}>{t('Review gate')}</span>
                  </label>
                  <select aria-label={t('Retry stage')}
                    title={t(STAGE_DENY_TIP)}
                    value={stage.reviewDenyAction}
                    disabled={!stage.requiresReview}
                    onChange={e => updateStage(i, 'reviewDenyAction', e.target.value as 'RetryStage' | 'FailPipeline')}
                    style={{ flex: '0 1 150px', minWidth: '120px' }}
                  >
                    <option value="RetryStage">{t('Retry stage')}</option>
                    <option value="FailPipeline">{t('Fail pipeline')}</option>
                  </select>
                  <span style={{ width: '0.75rem', flexShrink: 0 }} />
                  <button type="button" className="btn btn-sm" onClick={() => moveStage(i, -1)} disabled={i === 0} title={t('Move this stage one place earlier in the run order.')} style={{ padding: '0.15rem 0.4rem', fontSize: '0.75rem' }}>{'\u25B2'}</button>
                  <button type="button" className="btn btn-sm" onClick={() => duplicateStage(i)} title={t('Copy this stage immediately after it. Save the pipeline to apply the change.')}>{t('Duplicate')}</button>
                  <button type="button" className="btn btn-sm" onClick={() => moveStage(i, 1)} disabled={i === form.stages.length - 1} title={t('Move this stage one place later in the run order.')} style={{ padding: '0.15rem 0.4rem', fontSize: '0.75rem' }}>{'\u25BC'}</button>
                  <span style={{ width: '0.5rem', flexShrink: 0 }} />
                  <button type="button" className="btn btn-sm btn-danger" onClick={() => removeStage(i)} title={t('Remove this stage from the pipeline. Nothing changes until you save.')} style={{ flexShrink: 0 }}>X</button>
                </div>
              ))}
              {form.stages.length === 0 && <p className="text-dim">{t('No stages defined.')}</p>}
            </div>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary" title={t('Save changes to this pipeline. Dispatches started after saving use the new stages; running ones keep theirs.')}>{t('Save')}</button>
              <button type="button" className="btn" onClick={() => setShowForm(false)}>{t('Cancel')}</button>
            </div>
          </form>
        </div>
      )}

      {/* Run modal (live run-mode) */}
      {showRun && (
        <div className="modal-overlay" onClick={() => setShowRun(false)}>
          <form className="modal" onClick={e => e.stopPropagation()} onSubmit={handleRun} style={{ maxWidth: '520px', width: '90%' }}>
            <h3>{t('Run Pipeline')}</h3>
            <p className="text-dim" style={{ marginTop: 0 }}>{t('Dispatch a voyage that runs this pipeline\'s stages against a vessel.')}</p>
            <label title={t(RUN_VESSEL_TIP)}>{t('Vessel')}
              <select title={t(RUN_VESSEL_TIP)} value={runVesselId} onChange={e => setRunVesselId(e.target.value)} required>
                <option value="">{t('Select a vessel...')}</option>
                {vessels.map(v => <option key={v.id} value={v.id}>{v.name}</option>)}
              </select>
            </label>
            <label title={t(RUN_TITLE_TIP)}>{t('Title')}<input type="text" title={t(RUN_TITLE_TIP)} value={runTitle} onChange={e => setRunTitle(e.target.value)} required /></label>
            <label title={t(RUN_DESCRIPTION_TIP)}>{t('Objective / Description')}<textarea rows={4} title={t(RUN_DESCRIPTION_TIP)} value={runDescription} onChange={e => setRunDescription(e.target.value)} /></label>
            <DispatchPreview compact vesselId={runVesselId} title={runTitle} description={runDescription} enabled={!running} />
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary" disabled={running || !runVesselId} title={t('Create the voyage now. Its first stage mission is queued for a captain right away; this starts real agent work.')}>{running ? t('Launching...') : t('Launch Voyage')}</button>
              <button type="button" className="btn" onClick={() => setShowRun(false)}>{t('Cancel')}</button>
            </div>
          </form>
        </div>
      )}

      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      {deletion.dialog}
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message}
        onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />

      {/* Pipeline Info */}
      <div className="detail-grid">
        <div className="detail-field">
          <span className="detail-label" title={t('Unique pipeline ID (ppl_ prefix). Pipelines are usually referred to by name.')}>{t('ID')}</span>
          <span className="id-display">
            <span className="mono">{pipeline.id}</span>
            <CopyButton text={pipeline.id} />
          </span>
        </div>
        <div className="detail-field"><span className="detail-label">{t('Name')}</span><span>{pipeline.name}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Description')}</span><span>{pipeline.description || '-'}</span></div>
        <div className="detail-field"><span className="detail-label" title={t('Built-in pipelines ship with Armada and cannot be deleted.')}>{t('Built-in')}</span><span>{pipeline.isBuiltIn ? <span className="badge badge-info" title={t('Built-in: ships with Armada and cannot be deleted.')}>{t('Yes')}</span> : <span className="badge" title={t('Custom: created by a user and can be deleted.')}>{t('No')}</span>}</span></div>
        <div className="detail-field"><span className="detail-label" title={t('Marks the pipeline as in use or retired. It is a label only: dispatch does not skip inactive pipelines.')}>{t('Active')}</span><span>{pipeline.active !== false ? <span className="badge badge-success" title={t('Active (a label only; it does not change dispatch).')}>{t('Yes')}</span> : <span className="badge badge-dim" title={t('Inactive (a label only; dispatch can still use it).')}>{t('No')}</span>}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Created')}</span><span title={pipeline.createdUtc}>{formatDateTime(pipeline.createdUtc)}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Last Updated')}</span><span>{formatDateTime(pipeline.lastUpdateUtc)}</span></div>
      </div>

      {/* Visual flow */}
      <div style={{ marginBottom: '1.5rem' }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <h3 title={t('The stages as a diagram, in the order missions run. Review gates are shown on the stages that have them.')}>{t('Flow')}</h3>
          {canManage && <button className="btn" onClick={openEdit} title={t('Add, remove, reorder or change this pipeline\'s stages.')}>{t('Edit stages')}</button>}
        </div>
        <div className="table-wrap" style={{ overflowX: 'auto' }}>
          <PipelineFlow stages={pipeline.stages} label={t} />
        </div>
      </div>

      {/* Stages */}
      <div>
        <h3>{t('Stages')}</h3>
        {pipeline.stages.length > 0 ? (
          <DataTable
            tableKey="pipeline-detail-stages"
            rows={pipeline.stages.slice().sort((a, b) => a.order - b.order)}
            rowKey={(stage) => stage.id}
            recordCount={null}
            columns={[
              { key: 'actions', label: t('Actions'), fixed: true, interactive: true, render: stage => canManage ? <ActionMenu id={`pipeline-stage-${stage.id}`} items={[
                { label: 'Duplicate', title: 'Copy this stage into the pipeline editor. Review and save to apply it.', onClick: () => { openEdit(); duplicateStage(pipeline.stages.findIndex(item => item.id === stage.id)); } },
              ]} /> : null },
              { key: 'order', label: t('Order'), required: true, headerTitle: t('Run order. Stage 1 runs first; each later stage waits for the one before it.'), cellClassName: 'cell-nowrap', render: (stage) => stage.order },
              { key: 'persona', label: t('Persona Name'), required: true, headerTitle: t('The persona (role) this stage\'s mission is given, which decides its instructions and which captains can take it.'), cellClassName: 'cell-ident', render: (stage) => <strong>{stage.personaName}</strong> },
              {
                key: 'optional', label: t('Optional'), headerTitle: t('Reserved: saved but has no effect today. Every stage runs whether or not it is marked optional.'), cellClassName: 'cell-nowrap',
                render: (stage) => stage.isOptional ? <span className="badge badge-info" title={t('Marked optional. This is reserved and has no effect today: the stage still runs.')}>{t('Yes')}</span> : <span className="badge" title={t('Not marked optional.')}>{t('No')}</span>,
              },
              {
                key: 'review', label: t('Review'), headerTitle: t('Required means the pipeline pauses after this stage until a person approves its work in Review.'), cellClassName: 'cell-nowrap',
                render: (stage) => stage.requiresReview ? <span className="badge badge-warning" title={t('Review gate: the pipeline pauses after this stage until a person approves its work.')}>{t('Required')}</span> : <span className="text-dim">{t('None')}</span>,
              },
              {
                key: 'onDeny', label: t('On Deny'), headerTitle: t('What a denied review does: Retry stage reworks the same mission; Fail pipeline fails it and cancels the stages after it.'), cellClassName: 'cell-nowrap',
                render: (stage) => stage.requiresReview ? <span>{stage.reviewDenyAction === 'FailPipeline' ? t('Fail pipeline') : t('Retry stage')}</span> : <span className="text-dim">-</span>,
              },
              {
                key: 'description', label: t('Description'), headerTitle: t('What this stage is for. Hover a value for the full text.'), cellClassName: 'text-dim',
                cellTitle: (stage) => stage.description || undefined,
                render: (stage) => <span className="line-clamp-2">{stage.description || '-'}</span>,
              },
            ]}
          />
        ) : (
          <p className="text-dim" style={{ marginTop: '1rem' }}>{t('No stages defined.')}</p>
        )}
      </div>
    </div>
  );
}
