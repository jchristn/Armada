import { useEffect, useState } from 'react';
import { listBlackBox } from '../../api/client';
import type { BlackBoxEntrySummary } from '../../types/blackbox';
import { useLocale } from '../../context/LocaleContext';

interface AskBlackBoxFormProps {
  busy: boolean;
  onSubmit: (args: Record<string, unknown>) => void;
  onCancel: () => void;
}

const RECENT_ENTRIES = 25;

/**
 * Inline `/blackbox` form: pick a failed mission that has a black box entry (recent entries are listed; any mission id
 * can be typed) and optionally a question. Submitting runs `get_blackbox`, shows the card, and sends the question to
 * the captain.
 */
export default function AskBlackBoxForm({ busy, onSubmit, onCancel }: AskBlackBoxFormProps) {
  const { t } = useLocale();
  const [entries, setEntries] = useState<BlackBoxEntrySummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [missionId, setMissionId] = useState('');
  const [question, setQuestion] = useState('');
  const [error, setError] = useState('');

  useEffect(() => {
    let active = true;
    listBlackBox({ pageSize: RECENT_ENTRIES, isCurrent: true, subjectType: 'Mission', order: 'CreatedDescending' })
      .then((page) => { if (active) setEntries((page?.objects ?? []).filter((e) => !!e.missionId)); })
      .catch(() => { /* the mission id can still be typed */ })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, []);

  function submit() {
    const id = missionId.trim();
    if (!/^msn_\S+$/.test(id)) { setError('Choose a failed mission or type its id (msn_...).'); return; }
    setError('');
    const args: Record<string, unknown> = { missionId: id };
    if (question.trim()) args.question = question.trim();
    onSubmit(args);
  }

  return (
    <form
      className="ask-inline-form"
      aria-label={t('Black box of a failure')}
      onSubmit={(e) => { e.preventDefault(); submit(); }}
      onKeyDown={(e) => { if (e.key === 'Escape') { e.preventDefault(); onCancel(); } }}
    >
      <div className="ask-inline-form-head">
        <code>/blackbox</code>
        <span>{t('Black box of a failure')}</span>
      </div>

      <label className="ask-field" title={t('A failed mission with a black box entry. Recent failures are listed; you can also type a mission id.')}>
        <span>{t('Mission')}</span>
        <input
          list="ask-blackbox-missions"
          value={missionId}
          onChange={(e) => setMissionId(e.target.value)}
          placeholder={loading ? t('Loading...') : 'msn_...'}
          disabled={busy}
          aria-invalid={!!error || undefined}
          autoFocus
        />
        <datalist id="ask-blackbox-missions">
          {entries.map((e) => (
            <option key={e.id} value={e.missionId ?? ''}>{[e.missionTitle, e.vesselName, e.step].filter(Boolean).join(' · ')}</option>
          ))}
        </datalist>
        {error && <span className="ask-field-error">{t(error)}</span>}
      </label>

      <label className="ask-field" title={t('Optional. Sent to the captain once the black box card is shown.')}>
        <span>{t('Question')}</span>
        <textarea rows={2} value={question} onChange={(e) => setQuestion(e.target.value)} disabled={busy} placeholder={t('Why did it fail, and what could have been done differently?')} />
      </label>

      <div className="ask-inline-form-actions">
        <span className="text-dim">{t('Reading the black box changes nothing.')}</span>
        <button type="button" className="btn btn-sm" onClick={onCancel} disabled={busy} title={t('Close the form without running anything (Escape).')}>{t('Cancel')}</button>
        <button type="submit" className="btn btn-sm btn-primary" disabled={busy} title={t('Show the black box card for this mission in the conversation.')}>{busy ? t('Starting...') : t('Show black box')}</button>
      </div>
    </form>
  );
}
