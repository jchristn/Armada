import { useEffect, useState } from 'react';
import { resolveBlackBox } from '../../api/client';
import type { BlackBoxEntry, BlackBoxResolution } from '../../types/blackbox';
import { useLocale } from '../../context/LocaleContext';
import { BLACKBOX_RESOLUTIONS } from '../../lib/blackbox';
import DialogShell from '../shared/DialogShell';

interface BlackBoxResolveDialogProps {
  open: boolean;
  entry: BlackBoxEntry;
  onClose: () => void;
  onResolved: (entry: BlackBoxEntry) => void;
}

/** Set an entry's resolution (Resolved, Duplicate, WontFix, ArmadaFixed, or back to Open) with a note and a link. */
export default function BlackBoxResolveDialog({ open, entry, onClose, onResolved }: BlackBoxResolveDialogProps) {
  const { t } = useLocale();
  const [resolution, setResolution] = useState<BlackBoxResolution>('Resolved');
  const [note, setNote] = useState('');
  const [link, setLink] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    if (!open) return;
    setResolution(entry.resolution && entry.resolution !== 'Open' && entry.resolution !== 'Unknown' ? entry.resolution : 'Resolved');
    setNote(entry.resolutionNote ?? '');
    setLink(entry.resolutionLink ?? '');
    setError('');
  }, [open, entry]);

  async function save() {
    setSaving(true);
    try {
      const updated = await resolveBlackBox(entry.id, { resolution, note: note.trim() || null, link: link.trim() || null });
      onResolved(updated);
    } catch (err: unknown) {
      setError(t('The resolution could not be saved: {{message}}', { message: err instanceof Error ? err.message : String(err) }));
    } finally {
      setSaving(false);
    }
  }

  return (
    <DialogShell
      open={open}
      title={t('Resolve black box entry')}
      subtitle={<span className="mono">{entry.id}</span>}
      onClose={onClose}
      size="xl"
      className="blackbox-dialog"
      dismissible={!saving}
      footer={(
        <>
          <button type="button" className="btn" onClick={onClose}>{t('Cancel')}</button>
          <button type="button" className="btn btn-primary" disabled={saving} onClick={() => void save()} title={t('Save the resolution, note and link on the entry.')}>{saving ? t('Saving...') : t('Save resolution')}</button>
        </>
      )}
    >
      {error && <div className="blackbox-notice blackbox-notice-error" role="alert">{error}</div>}
      <div className="form-group">
        <label htmlFor="blackbox-resolution">{t('Resolution')}</label>
        <select id="blackbox-resolution" value={resolution} onChange={(e) => setResolution(e.target.value as BlackBoxResolution)}>
          {BLACKBOX_RESOLUTIONS.map((r) => <option key={r} value={r}>{t(r)}</option>)}
        </select>
        <span className="text-dim form-help">{t('Resolved entries expire sooner (resolved retention). ArmadaFixed means a later Armada release fixed the cause.')}</span>
      </div>
      <div className="form-group">
        <label htmlFor="blackbox-resolution-note">{t('Note')}</label>
        <textarea id="blackbox-resolution-note" value={note} maxLength={8000} onChange={(e) => setNote(e.target.value)} />
      </div>
      <div className="form-group">
        <label htmlFor="blackbox-resolution-link">{t('Link')}</label>
        <input id="blackbox-resolution-link" value={link} maxLength={2000} onChange={(e) => setLink(e.target.value)} placeholder={t('A commit, pull request, mission id or issue URL')} />
      </div>
    </DialogShell>
  );
}
