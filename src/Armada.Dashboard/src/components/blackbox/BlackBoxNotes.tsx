import { useCallback, useEffect, useState } from 'react';
import { addBlackBoxNote, listBlackBoxNotes } from '../../api/client';
import type { BlackBoxNote } from '../../types/blackbox';
import { useLocale } from '../../context/LocaleContext';

const NOTE_PAGE_SIZE = 25;
const NOTE_MAX_LENGTH = 8000;

/** Notes on an entry (operators, agents, Ask Armada and the recovery runner), newest first, with an Add note form. */
export default function BlackBoxNotes({ entryId, refreshKey }: { entryId: string; refreshKey?: number }) {
  const { t, formatDateTime } = useLocale();
  const [notes, setNotes] = useState<BlackBoxNote[]>([]);
  const [total, setTotal] = useState(0);
  const [pageSize, setPageSize] = useState(NOTE_PAGE_SIZE);
  const [draft, setDraft] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    try {
      const result = await listBlackBoxNotes(entryId, { pageNumber: 1, pageSize });
      setNotes(result.objects ?? []);
      setTotal(result.totalRecords ?? (result.objects ?? []).length);
      setError('');
    } catch (err: unknown) {
      setError(t('Notes could not be loaded: {{message}}', { message: err instanceof Error ? err.message : String(err) }));
    }
  }, [entryId, pageSize, t]);

  useEffect(() => { void load(); }, [load, refreshKey]);

  async function add() {
    const body = draft.trim();
    if (!body) return;
    setSaving(true);
    try {
      await addBlackBoxNote(entryId, body);
      setDraft('');
      await load();
    } catch (err: unknown) {
      setError(t('The note could not be saved: {{message}}', { message: err instanceof Error ? err.message : String(err) }));
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="blackbox-notes">
      {error && <div className="blackbox-notice blackbox-notice-error" role="alert">{error}</div>}
      {notes.length === 0 && !error && <p className="text-dim" style={{ margin: 0 }}>{t('No notes yet.')}</p>}
      {notes.map((note) => (
        <div key={note.id} className="blackbox-note">
          <div className="blackbox-note-meta">{t(note.authorKind)} · {formatDateTime(note.createdUtc)}{note.userId ? ` · ${note.userId}` : ''}</div>
          <div style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{note.body}</div>
        </div>
      ))}
      {total > notes.length && (
        <button type="button" className="btn btn-sm" onClick={() => setPageSize((s) => s + NOTE_PAGE_SIZE)}>{t('Show more notes ({{shown}} of {{total}})', { shown: notes.length, total })}</button>
      )}
      <div className="blackbox-note-form">
        <textarea
          value={draft}
          maxLength={NOTE_MAX_LENGTH}
          onChange={(e) => setDraft(e.target.value)}
          placeholder={t('Add a note: what you tried, what you found, a link.')}
          aria-label={t('New note')}
        />
        <div>
          <button type="button" className="btn btn-sm btn-primary" disabled={saving || !draft.trim()} onClick={() => void add()} title={t('Save this note on the entry. Everyone who can see the entry sees it.')}>
            {saving ? t('Saving...') : t('Add note')}
          </button>
        </div>
      </div>
    </div>
  );
}
