import { useEffect, useState } from 'react';
import { getCaptain, listBatteryPacks, listCaptains } from '../api/client';
import type { BatteryPackInfo, Captain, ModelEvaluationOptions, ModelProbeDepth } from '../types/models';
import { useLocale } from '../context/LocaleContext';
import { captainModelFamily } from '../lib/modelFamily';
import DialogShell from './shared/DialogShell';

/** Run counts the server accepts for a Full Evaluate (default 3). */
export const EVALUATE_RUN_COUNT_MIN = 1;
export const EVALUATE_RUN_COUNT_MAX = 10;

interface ModelEvaluateDialogProps {
  open: boolean;
  /** The captain to evaluate. */
  captainId: string;
  /** True while the start request is in flight. */
  submitting?: boolean;
  onClose: () => void;
  /** Start the Evaluate with the chosen depth and options (options are empty for Quick). */
  onSubmit: (depth: ModelProbeDepth, options: ModelEvaluationOptions) => void;
}

function parseAllowedPersonas(value: string | null | undefined): string[] {
  if (!value) return [];
  try {
    const parsed: unknown = JSON.parse(value);
    return Array.isArray(parsed) ? parsed.filter((p): p is string => typeof p === 'string' && p.trim() !== '') : [];
  } catch {
    return [];
  }
}

function toggle(list: string[], value: string): string[] {
  return list.includes(value) ? list.filter((v) => v !== value) : [...list, value];
}

/**
 * The captain Evaluate dialog: depth (Quick or Full, Full by default), and for Full the evaluator captain (optional,
 * idle captains only, with a note when one shares the evaluated captain's model family), the battery packs (empty means
 * the server default), the personas (empty means all the captain is allowed) and the run count.
 */
export default function ModelEvaluateDialog({ open, captainId, submitting = false, onClose, onSubmit }: ModelEvaluateDialogProps) {
  const { t } = useLocale();
  const [depth, setDepth] = useState<ModelProbeDepth>('Full');
  const [evaluatorId, setEvaluatorId] = useState('');
  const [packs, setPacks] = useState<string[]>([]);
  const [personas, setPersonas] = useState<string[]>([]);
  const [runCount, setRunCount] = useState('');
  const [candidate, setCandidate] = useState<Captain | null>(null);
  const [idleCaptains, setIdleCaptains] = useState<Captain[]>([]);
  const [installedPacks, setInstalledPacks] = useState<BatteryPackInfo[]>([]);
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState('');

  useEffect(() => {
    if (!open) return undefined;
    let cancelled = false;
    setDepth('Full');
    setEvaluatorId('');
    setPacks([]);
    setPersonas([]);
    setRunCount('');
    setLoadError('');
    setLoading(true);
    Promise.all([getCaptain(captainId), listCaptains({ pageSize: 1000 }), listBatteryPacks()])
      .then(([captain, captains, packPage]) => {
        if (cancelled) return;
        setCandidate(captain);
        setIdleCaptains((captains.objects ?? []).filter((c) => c.id !== captainId && c.state === 'Idle'));
        setInstalledPacks(packPage.objects ?? []);
      })
      .catch((err: unknown) => {
        if (!cancelled) setLoadError(err instanceof Error ? err.message : t('Failed to load evaluation options.'));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => { cancelled = true; };
    // t is stable enough; reloading on its identity would refetch while the dialog is open.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, captainId]);

  const candidateFamily = candidate ? captainModelFamily(candidate) : null;
  const allowed = parseAllowedPersonas(candidate?.allowedPersonas);
  const validPacks = installedPacks.filter((p) => p.valid);
  const packsInScope = packs.length > 0 ? validPacks.filter((p) => packs.includes(p.id)) : validPacks;
  const personaOptions = allowed.length > 0
    ? allowed
    : Array.from(new Set(packsInScope.flatMap((p) => (p.tasks ?? []).map((task) => task.persona ?? '').filter((x) => x !== '')))).sort();
  const evaluator = idleCaptains.find((c) => c.id === evaluatorId) ?? null;
  const sameFamily = !!evaluator && !!candidateFamily && captainModelFamily(evaluator) === candidateFamily;
  const runCountValue = runCount.trim() === '' ? null : Number(runCount);
  const runCountInvalid = runCountValue !== null
    && (!Number.isInteger(runCountValue) || runCountValue < EVALUATE_RUN_COUNT_MIN || runCountValue > EVALUATE_RUN_COUNT_MAX);
  const full = depth === 'Full';

  function handleSubmit() {
    if (full && runCountInvalid) return;
    const options: ModelEvaluationOptions = full
      ? {
        evaluatorCaptainId: evaluatorId || null,
        packs,
        personas: personas.filter((p) => personaOptions.includes(p)),
        runCount: runCountValue,
      }
      : {};
    onSubmit(depth, options);
  }

  const footer = (
    <>
      <button type="button" className="btn" onClick={onClose} disabled={submitting}>{t('Cancel')}</button>
      <button
        type="button"
        className="btn btn-primary"
        onClick={handleSubmit}
        disabled={submitting || loading || (full && runCountInvalid)}
        title={t('Queue the evaluation. Advisory only: nothing changes routing, persona captain lists or prompts.')}
      >
        {submitting ? t('Starting...') : t('Start evaluation')}
      </button>
    </>
  );

  return (
    <DialogShell
      open={open}
      title={t('Evaluate {{name}}', { name: candidate?.name ?? captainId })}
      subtitle={t('Quick runs the mechanical probes. Full adds the fixture battery and an evaluator captain\'s grading.')}
      onClose={onClose}
      dismissible={!submitting}
      footer={footer}
    >
      {loadError && <p role="alert" className="text-danger">{loadError}</p>}
      <fieldset className="model-evaluate-depth" style={{ border: 0, padding: 0, margin: '0 0 0.75rem' }}>
        <legend><strong>{t('Depth')}</strong></legend>
        <label style={{ marginRight: '1rem' }}>
          <input type="radio" name="model-evaluate-depth" value="Quick" checked={depth === 'Quick'} onChange={() => setDepth('Quick')} />{' '}
          {t('Quick')}
        </label>
        <label>
          <input type="radio" name="model-evaluate-depth" value="Full" checked={depth === 'Full'} onChange={() => setDepth('Full')} />{' '}
          {t('Full')}
        </label>
      </fieldset>

      <fieldset disabled={!full || loading} style={{ border: 0, padding: 0, margin: 0 }}>
        <label htmlFor="model-evaluate-evaluator"><strong>{t('Evaluator captain')}</strong></label>
        <select id="model-evaluate-evaluator" value={evaluatorId} onChange={(e) => setEvaluatorId(e.target.value)} style={{ display: 'block', width: '100%' }}>
          <option value="">{t('Automatic (highest-tier idle captain of another model family)')}</option>
          {idleCaptains.map((c) => (
            <option key={c.id} value={c.id}>
              {candidateFamily && captainModelFamily(c) === candidateFamily
                ? t('{{name}} (same model family)', { name: c.name })
                : c.name}
            </option>
          ))}
        </select>
        {sameFamily && (
          <p className="text-warning" data-testid="model-evaluate-family-note">
            {t('{{name}} is of the same model family ({{family}}) as the evaluated captain; the report will carry a warning.', { name: evaluator!.name, family: candidateFamily ?? '' })}
          </p>
        )}
        {!loading && idleCaptains.length === 0 && <p className="text-dim">{t('No other captain is idle.')}</p>}

        <div style={{ marginTop: '0.75rem' }}>
          <strong>{t('Battery packs')}</strong>{' '}
          <span className="text-dim">{t('None selected: the packs matching your repositories\' languages.')}</span>
          {!loading && installedPacks.length === 0 && <p className="text-dim">{t('No battery packs are installed.')}</p>}
          <ul style={{ listStyle: 'none', margin: '0.25rem 0 0', padding: 0 }}>
            {installedPacks.map((pack) => (
              <li key={pack.id}>
                <label title={pack.valid ? (pack.description ?? '') : pack.errors.join(' ')}>
                  <input type="checkbox" checked={packs.includes(pack.id)} disabled={!pack.valid} onChange={() => setPacks(toggle(packs, pack.id))} />{' '}
                  {pack.name ? `${pack.name} (${pack.id})` : pack.id}
                  {!pack.valid && <span className="text-danger">{' '}{t('invalid')}</span>}
                </label>
              </li>
            ))}
          </ul>
        </div>

        <div style={{ marginTop: '0.75rem' }}>
          <strong>{t('Personas')}</strong>{' '}
          <span className="text-dim">{t('None selected: every persona the captain is allowed.')}</span>
          <ul style={{ listStyle: 'none', margin: '0.25rem 0 0', padding: 0 }}>
            {personaOptions.map((persona) => (
              <li key={persona}>
                <label>
                  <input type="checkbox" checked={personas.includes(persona)} onChange={() => setPersonas(toggle(personas, persona))} />{' '}
                  {persona}
                </label>
              </li>
            ))}
          </ul>
        </div>

        <div style={{ marginTop: '0.75rem' }}>
          <label htmlFor="model-evaluate-run-count"><strong>{t('Run count')}</strong></label>{' '}
          <input
            id="model-evaluate-run-count"
            type="number"
            min={EVALUATE_RUN_COUNT_MIN}
            max={EVALUATE_RUN_COUNT_MAX}
            placeholder="3"
            value={runCount}
            onChange={(e) => setRunCount(e.target.value)}
            style={{ width: '6rem' }}
          />{' '}
          <span className="text-dim">{t('Runs of each battery task, 1 to 10 (empty: 3).')}</span>
          {full && runCountInvalid && <p className="text-danger">{t('The run count must be a whole number from 1 to 10.')}</p>}
        </div>
      </fieldset>
    </DialogShell>
  );
}
