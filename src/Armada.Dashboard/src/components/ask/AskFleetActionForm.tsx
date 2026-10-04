import { useEffect, useMemo, useState } from 'react';
import { enumerateFleetActions, listVessels } from '../../api/client';
import type { FleetAction, Vessel } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import { buildFleetActionArguments, validateFleetAction, type FleetActionDraft } from '../../lib/askQuickActions';

interface AskFleetActionFormProps {
  busy: boolean;
  onSubmit: (args: Record<string, unknown>) => void;
  onCancel: () => void;
}

/**
 * Inline `/fleet-action` form: pick a saved fleet action and the vessels to run it on. Submitting runs the MCP
 * `run_fleet_action` tool through the thread's actions endpoint.
 */
export default function AskFleetActionForm({ busy, onSubmit, onCancel }: AskFleetActionFormProps) {
  const { t } = useLocale();
  const [actions, setActions] = useState<FleetAction[]>([]);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState('');
  const [filter, setFilter] = useState('');
  const [draft, setDraft] = useState<FleetActionDraft>({ actionId: '', vesselIds: [] });
  const [errors, setErrors] = useState<Record<string, string>>({});

  useEffect(() => {
    let active = true;
    Promise.all([enumerateFleetActions({ pageSize: 500 }), listVessels({ pageSize: 9999 })])
      .then(([a, v]) => {
        if (!active) return;
        setActions(a.objects || []);
        setVessels((v.objects || []).slice().sort((x, y) => x.name.localeCompare(y.name)));
      })
      .catch((err: unknown) => { if (active) setLoadError(err instanceof Error ? err.message : t('Failed to load fleet actions.')); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [t]);

  const visible = useMemo(() => {
    const term = filter.trim().toLowerCase();
    return term ? vessels.filter((v) => v.name.toLowerCase().includes(term)) : vessels;
  }, [vessels, filter]);
  const allVisibleSelected = visible.length > 0 && visible.every((v) => draft.vesselIds.includes(v.id));

  function toggle(id: string) {
    setDraft((d) => ({ ...d, vesselIds: d.vesselIds.includes(id) ? d.vesselIds.filter((x) => x !== id) : [...d.vesselIds, id] }));
  }

  function toggleAllVisible() {
    setDraft((d) => {
      const ids = new Set(d.vesselIds);
      if (allVisibleSelected) visible.forEach((v) => ids.delete(v.id));
      else visible.forEach((v) => ids.add(v.id));
      return { ...d, vesselIds: [...ids] };
    });
  }

  function submit() {
    const found = validateFleetAction(draft);
    setErrors(found);
    if (Object.keys(found).length > 0) return;
    onSubmit(buildFleetActionArguments(draft));
  }

  return (
    <form
      className="ask-inline-form"
      aria-label={t('Run a fleet action')}
      onSubmit={(e) => { e.preventDefault(); submit(); }}
      onKeyDown={(e) => { if (e.key === 'Escape') { e.preventDefault(); onCancel(); } }}
    >
      <div className="ask-inline-form-head">
        <code>/fleet-action</code>
        <span>{t('Run a fleet action')}</span>
      </div>
      {loadError && <div className="alert alert-error" role="alert">{loadError}</div>}

      <label className="ask-field">
        <span>{t('Action')}</span>
        <select
          value={draft.actionId}
          onChange={(e) => setDraft((d) => ({ ...d, actionId: e.target.value }))}
          disabled={loading || busy}
          aria-invalid={!!errors.action || undefined}
          autoFocus
        >
          <option value="">{loading ? t('Loading...') : t('Choose an action')}</option>
          {actions.map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}
        </select>
        {errors.action && <span className="ask-field-error">{t(errors.action)}</span>}
      </label>

      <fieldset className="ask-vessel-pick">
        <legend>{t('Vessels')} <span className="text-dim">({t('{{count}} selected', { count: draft.vesselIds.length })})</span></legend>
        <div className="ask-vessel-pick-tools">
          <input
            type="search"
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
            placeholder={t('Filter vessels')}
            aria-label={t('Filter vessels')}
          />
          <button type="button" className="btn btn-sm" onClick={toggleAllVisible} disabled={visible.length === 0 || busy}>
            {allVisibleSelected ? t('Clear visible') : t('Select visible')}
          </button>
        </div>
        <ul className="ask-vessel-pick-list">
          {visible.map((v) => (
            <li key={v.id}>
              <label>
                <input type="checkbox" checked={draft.vesselIds.includes(v.id)} onChange={() => toggle(v.id)} disabled={busy} />
                <span>{v.name}</span>
              </label>
            </li>
          ))}
          {!loading && visible.length === 0 && <li className="text-dim">{t('No vessels match.')}</li>}
        </ul>
        {errors.vessels && <span className="ask-field-error">{t(errors.vessels)}</span>}
      </fieldset>

      <div className="ask-inline-form-actions">
        <span className="text-dim">{t('Submitting this form is the confirmation; the run starts right away.')}</span>
        <button type="button" className="btn btn-sm" onClick={onCancel} disabled={busy}>{t('Cancel')}</button>
        <button type="submit" className="btn btn-sm btn-primary" disabled={busy || loading}>{busy ? t('Starting...') : t('Run action')}</button>
      </div>
    </form>
  );
}
