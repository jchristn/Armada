import { useEffect, useState } from 'react';
import type { Vessel, Captain } from '../../types/models';
import { buildVesselContext, listCaptains } from '../../api/client';
import { listAll } from '../../api/paging';
import { useLocale } from '../../context/LocaleContext';

interface BuildContextModalProps {
  vessel: Vessel;
  onClose: () => void;
  /** Called with the updated vessel after a successful build/refine. */
  onBuilt: (vessel: Vessel) => void;
}

/**
 * Prompts the operator to pick a captain (and optional focus notes) and launches it to build -- or, when the
 * vessel already has one, refine -- the vessel's Model Context. The default prompt driving the captain lives
 * in Configuration > Prompts (template "vessel.build_context") and is editable there.
 */
export default function BuildContextModal({ vessel, onClose, onBuilt }: BuildContextModalProps) {
  const { t } = useLocale();
  const refine = !!(vessel.modelContext && vessel.modelContext.trim().length > 0);

  const [captains, setCaptains] = useState<Captain[]>([]);
  const [captainsLoading, setCaptainsLoading] = useState(true);
  const [captainId, setCaptainId] = useState('');
  const [notes, setNotes] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    listAll(listCaptains)
      .then((result) => {
        setCaptains(result.objects);
        if (result.objects.length > 0) setCaptainId((current) => current || result.objects[0].id);
      })
      .catch((err: unknown) => setError(err instanceof Error ? err.message : t('Failed to load captains.')))
      .finally(() => setCaptainsLoading(false));
  }, [t]);

  async function submit() {
    if (!captainId || busy) return;
    try {
      setBusy(true);
      setError('');
      const updated = await buildVesselContext(vessel.id, { captainId, notes: notes.trim() || undefined });
      onBuilt(updated);
      onClose();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to build Model Context.'));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="modal-overlay" style={{ zIndex: 1400 }} onClick={busy ? undefined : onClose}>
      <div className="modal-box" style={{ maxWidth: 620, width: '92vw' }} onClick={(e) => e.stopPropagation()}>
        <h3 style={{ marginTop: 0, marginBottom: '0.35rem' }}>
          {refine ? t('Refine Model Context') : t('Build Model Context')}
        </h3>
        <p className="text-dim" style={{ fontSize: '0.85rem', marginTop: 0, marginBottom: '1rem' }}>
          {refine
            ? t('Launch a captain to inspect {{name}} and refine its existing Model Context.', { name: vessel.name })
            : t('Launch a captain to inspect {{name}} and write its Model Context.', { name: vessel.name })}
        </p>

        <label className="text-dim" style={{ display: 'flex', flexDirection: 'column', gap: '0.3rem', fontSize: '0.8rem', marginBottom: '0.9rem' }} title={t('Captain (AI agent) that reads the repository and writes the Model Context. It is busy for the duration and uses that model\'s tokens; idle captains finish fastest.')}>
          {t('Captain')}
          <select title={t('Captain (AI agent) that reads the repository and writes the Model Context. It is busy for the duration and uses that model\'s tokens; idle captains finish fastest.')} value={captainId} onChange={(e) => setCaptainId(e.target.value)} disabled={busy}>
            {captains.length === 0 && <option value="">{captainsLoading ? t('Loading...') : t('No captains available')}</option>}
            {captains.map((c) => (
              <option key={c.id} value={c.id}>{c.name} ({c.model || c.runtime})</option>
            ))}
          </select>
        </label>

        <label className="text-dim" style={{ display: 'flex', flexDirection: 'column', gap: '0.3rem', fontSize: '0.8rem', marginBottom: '0.9rem' }} title={t('Optional extra instructions for this run, such as areas to focus on or skip. Blank uses only the vessel.build_context prompt.')}>
          {t('Focus / guidance (optional)')}
          <textarea
            title={t('Optional extra instructions for this run, such as areas to focus on or skip. Blank uses only the vessel.build_context prompt.')}
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            disabled={busy}
            rows={4}
            placeholder={t('e.g. Emphasize the build and test commands, and how the plugin system works.')}
            style={{ resize: 'vertical', minHeight: '5rem' }}
          />
        </label>

        <p className="text-dim" style={{ fontSize: '0.78rem', marginTop: 0 }}>
          {t('The captain follows the editable "vessel.build_context" prompt (Configuration > Prompts). This runs synchronously and can take a few minutes.')}
        </p>

        {error && (
          <div className="alert alert-error" style={{ marginTop: '0.5rem' }}>{error}</div>
        )}

        {busy && (
          <div className="text-dim" style={{ fontSize: '0.85rem', marginTop: '0.5rem' }}>
            {refine ? t('Refining Model Context... this can take a few minutes.') : t('Building Model Context... this can take a few minutes.')}
          </div>
        )}

        <div className="modal-actions" style={{ marginTop: '1rem' }}>
          <button type="button" className="btn" onClick={onClose} disabled={busy} title={t('Close without launching a captain. The Model Context is left unchanged.')}>{t('Cancel')}</button>
          <button type="button" className="btn btn-primary" onClick={() => void submit()} disabled={busy || !captainId} title={refine ? t('Launch the selected captain to review the repository and update the existing Model Context. Takes a few minutes and uses model tokens.') : t('Launch the selected captain to study the repository and write a new Model Context. Takes a few minutes and uses model tokens.')}>
            {busy ? t('Working...') : refine ? t('Refine Context') : t('Build Context')}
          </button>
        </div>
      </div>
    </div>
  );
}
