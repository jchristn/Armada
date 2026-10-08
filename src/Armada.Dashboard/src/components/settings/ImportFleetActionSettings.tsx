import { useEffect, useState } from 'react';
import { updateSettings } from '../../api/client';
import type { FleetActionSettingsData, VesselImportSettingsData } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import ListEditor from './ListEditor';
import { FLEET_ACTION_RANGES, FLEET_DEFAULTS, IMPORT_DEFAULTS, IMPORT_RANGES, rangeError, type NumberField } from '../../lib/settingsRanges';

interface ImportFleetActionSettingsProps {
  importSettings: VesselImportSettingsData | null | undefined;
  fleetActionSettings: FleetActionSettingsData | null | undefined;
  /** Disable editing (remote proxy mode or insufficient permission). */
  locked: boolean;
  /** Called with the full settings payload the server returned after a save. */
  onSaved: (updated: Record<string, unknown>) => void;
  /** Toast helper from the host page. */
  notify: (severity: 'success' | 'error', message: string) => void;
}

export { FLEET_ACTION_RANGES, IMPORT_RANGES, rangeError } from '../../lib/settingsRanges';

interface ImportDraft { allowedRoots: string[]; excludedDirectoryNames: string[]; maxDepth: string; inlineBatchLimit: string; categorizationTimeoutMinutes: string }
interface FleetDraft { maxConcurrency: string; defaultTimeoutSeconds: string; maxOutputBytes: string; runRetentionDays: string }

function toImportDraft(s: VesselImportSettingsData | null | undefined): ImportDraft {
  const v = s ?? IMPORT_DEFAULTS;
  return { allowedRoots: [...(v.allowedRoots ?? [])], excludedDirectoryNames: [...(v.excludedDirectoryNames ?? [])], maxDepth: String(v.maxDepth), inlineBatchLimit: String(v.inlineBatchLimit), categorizationTimeoutMinutes: String(v.categorizationTimeoutMinutes ?? 20) };
}

function toFleetDraft(s: FleetActionSettingsData | null | undefined): FleetDraft {
  const v = s ?? FLEET_DEFAULTS;
  return { maxConcurrency: String(v.maxConcurrency), defaultTimeoutSeconds: String(v.defaultTimeoutSeconds), maxOutputBytes: String(v.maxOutputBytes), runRetentionDays: String(v.runRetentionDays) };
}

/**
 * Settings page sections for Vessel Import (`Import`) and Fleet Actions (`FleetActions`). Each section keeps a
 * local draft (so the page's auto-refresh never overwrites unsaved edits), validates ranges per field, and
 * saves its whole group, which the server replaces and applies live.
 */
export default function ImportFleetActionSettings({ importSettings, fleetActionSettings, locked, onSaved, notify }: ImportFleetActionSettingsProps) {
  const { t } = useLocale();
  const [importDraft, setImportDraft] = useState<ImportDraft>(() => toImportDraft(importSettings));
  const [importDirty, setImportDirty] = useState(false);
  const [fleetDraft, setFleetDraft] = useState<FleetDraft>(() => toFleetDraft(fleetActionSettings));
  const [fleetDirty, setFleetDirty] = useState(false);
  const [savingImport, setSavingImport] = useState(false);
  const [savingFleet, setSavingFleet] = useState(false);

  useEffect(() => { if (!importDirty) setImportDraft(toImportDraft(importSettings)); }, [importSettings, importDirty]);
  useEffect(() => { if (!fleetDirty) setFleetDraft(toFleetDraft(fleetActionSettings)); }, [fleetActionSettings, fleetDirty]);

  const importErrors = {
    maxDepth: rangeError(importDraft.maxDepth, IMPORT_RANGES.maxDepth),
    inlineBatchLimit: rangeError(importDraft.inlineBatchLimit, IMPORT_RANGES.inlineBatchLimit),
    categorizationTimeoutMinutes: rangeError(importDraft.categorizationTimeoutMinutes, IMPORT_RANGES.categorizationTimeoutMinutes),
  };
  const fleetErrors = {
    maxConcurrency: rangeError(fleetDraft.maxConcurrency, FLEET_ACTION_RANGES.maxConcurrency),
    defaultTimeoutSeconds: rangeError(fleetDraft.defaultTimeoutSeconds, FLEET_ACTION_RANGES.defaultTimeoutSeconds),
    maxOutputBytes: rangeError(fleetDraft.maxOutputBytes, FLEET_ACTION_RANGES.maxOutputBytes),
    runRetentionDays: rangeError(fleetDraft.runRetentionDays, FLEET_ACTION_RANGES.runRetentionDays),
  };
  const importValid = !importErrors.maxDepth && !importErrors.inlineBatchLimit && !importErrors.categorizationTimeoutMinutes;
  const fleetValid = Object.values(fleetErrors).every((e) => !e);

  const rangeText = (r: NumberField) => t('Must be a whole number from {{min}} to {{max}}.', { min: r.min.toLocaleString(), max: r.max.toLocaleString() });

  async function saveImport() {
    if (!importValid) return;
    setSavingImport(true);
    try {
      const updated = await updateSettings({
        import: {
          allowedRoots: importDraft.allowedRoots,
          maxDepth: Number(importDraft.maxDepth),
          excludedDirectoryNames: importDraft.excludedDirectoryNames,
          inlineBatchLimit: Number(importDraft.inlineBatchLimit),
          categorizationTimeoutMinutes: Number(importDraft.categorizationTimeoutMinutes),
        },
      });
      setImportDirty(false);
      onSaved(updated as Record<string, unknown>);
      notify('success', t('Import settings saved and applied.'));
    } catch (e: unknown) {
      notify('error', t('Failed: {{message}}', { message: e instanceof Error ? e.message : t('Unknown error') }));
    } finally {
      setSavingImport(false);
    }
  }

  async function saveFleet() {
    if (!fleetValid) return;
    setSavingFleet(true);
    try {
      const updated = await updateSettings({
        fleetActions: {
          maxConcurrency: Number(fleetDraft.maxConcurrency),
          defaultTimeoutSeconds: Number(fleetDraft.defaultTimeoutSeconds),
          maxOutputBytes: Number(fleetDraft.maxOutputBytes),
          runRetentionDays: Number(fleetDraft.runRetentionDays),
        },
      });
      setFleetDirty(false);
      onSaved(updated as Record<string, unknown>);
      notify('success', t('Fleet action settings saved and applied.'));
    } catch (e: unknown) {
      notify('error', t('Failed: {{message}}', { message: e instanceof Error ? e.message : t('Unknown error') }));
    } finally {
      setSavingFleet(false);
    }
  }

  function setImport(patch: Partial<ImportDraft>) { setImportDraft((d) => ({ ...d, ...patch })); setImportDirty(true); }
  function setFleet(patch: Partial<FleetDraft>) { setFleetDraft((d) => ({ ...d, ...patch })); setFleetDirty(true); }

  function numberInput(id: string, label: string, help: string, value: string, error: string, range: NumberField, onChange: (v: string) => void) {
    return (
      <div className="form-group">
        <label htmlFor={id}>{label}</label>
        <input id={id} type="number" min={range.min} max={range.max} value={value} onChange={(e) => onChange(e.target.value)} aria-invalid={Boolean(error)} aria-describedby={`${id}-help`} />
        <span id={`${id}-help`} className={error ? 'field-error' : 'text-dim form-help'}>{error ? rangeText(range) : help}</span>
      </div>
    );
  }

  return (
    <>
      <div className="settings-section" style={{ marginTop: '1.5rem' }}>
        <h3>{t('Vessel Import')}</h3>
        <p className="text-muted" style={{ marginBottom: '0.75rem' }}>
          {t('Controls where the import wizard may browse and discover repositories on the Admiral host. Changes apply immediately.')}
        </p>
        <fieldset disabled={locked} className="settings-fieldset">
          <div className="settings-grid settings-grid-wide">
            <ListEditor
              id="import-allowed-roots"
              label={t('Allowed roots')}
              help={t('Absolute folders that browse and discover are limited to. Empty means the user profile folder of the account running the Admiral.')}
              placeholder={t('/Users/alex/Code')}
              values={importDraft.allowedRoots}
              onChange={(v) => setImport({ allowedRoots: v })}
              mono
            />
            <ListEditor
              id="import-excluded-names"
              label={t('Excluded folder names')}
              help={t('Folder names discovery never descends into. Names starting with a dot are always skipped.')}
              placeholder="node_modules"
              values={importDraft.excludedDirectoryNames}
              onChange={(v) => setImport({ excludedDirectoryNames: v })}
              mono
            />
          </div>
          <div className="settings-grid">
            {numberInput('import-max-depth', t('Max depth'), t('Folder levels searched below each scan root (1-16, default 6).'), importDraft.maxDepth, importErrors.maxDepth, IMPORT_RANGES.maxDepth, (v) => setImport({ maxDepth: v }))}
            {numberInput('import-inline-limit', t('Inline batch limit'), t('Largest selection imported inside the request; larger imports run as a background job (1-500, default 25).'), importDraft.inlineBatchLimit, importErrors.inlineBatchLimit, IMPORT_RANGES.inlineBatchLimit, (v) => setImport({ inlineBatchLimit: v }))}
            {numberInput('import-categorization-timeout', t('Fleet categorization time limit (minutes)'), t('Longest a captain may spend recommending fleets for an import before it is stopped (1-240, default 20).'), importDraft.categorizationTimeoutMinutes, importErrors.categorizationTimeoutMinutes, IMPORT_RANGES.categorizationTimeoutMinutes, (v) => setImport({ categorizationTimeoutMinutes: v }))}
          </div>
          <div className="settings-actions">
            <button type="button" className="btn-primary btn-sm" onClick={() => void saveImport()} disabled={!importValid || savingImport || !importDirty}>
              {savingImport ? t('Saving...') : t('Save Import Settings')}
            </button>
            {importDirty && <button type="button" className="btn btn-sm" onClick={() => { setImportDirty(false); setImportDraft(toImportDraft(importSettings)); }}>{t('Discard changes')}</button>}
            {importDirty && <span className="text-dim">{t('Unsaved changes')}</span>}
          </div>
        </fieldset>
      </div>

      <div className="settings-section" style={{ marginTop: '1.5rem' }}>
        <h3>{t('Fleet Actions')}</h3>
        <p className="text-muted" style={{ marginBottom: '0.75rem' }}>
          {t('Limits for commands and missions run across many vessels. Changes apply immediately.')}
        </p>
        <fieldset disabled={locked} className="settings-fieldset">
          <div className="settings-grid">
            {numberInput('fa-max-concurrency', t('Max concurrency'), t('Command targets executing at once across every run on the Admiral (1-32, default 8).'), fleetDraft.maxConcurrency, fleetErrors.maxConcurrency, FLEET_ACTION_RANGES.maxConcurrency, (v) => setFleet({ maxConcurrency: v }))}
            {numberInput('fa-default-timeout', t('Default timeout (seconds)'), t('Timeout for new Command actions that do not set one (5-7200, default 300).'), fleetDraft.defaultTimeoutSeconds, fleetErrors.defaultTimeoutSeconds, FLEET_ACTION_RANGES.defaultTimeoutSeconds, (v) => setFleet({ defaultTimeoutSeconds: v }))}
            {numberInput('fa-max-output', t('Max output bytes'), t('Bytes kept per output stream per target; the end of the stream is kept (1024-1048576, default 65536).'), fleetDraft.maxOutputBytes, fleetErrors.maxOutputBytes, FLEET_ACTION_RANGES.maxOutputBytes, (v) => setFleet({ maxOutputBytes: v }))}
            {numberInput('fa-retention', t('Run retention (days)'), t('Finished runs older than this are pruned (1-3650, default 30).'), fleetDraft.runRetentionDays, fleetErrors.runRetentionDays, FLEET_ACTION_RANGES.runRetentionDays, (v) => setFleet({ runRetentionDays: v }))}
          </div>
          <div className="settings-actions">
            <button type="button" className="btn-primary btn-sm" onClick={() => void saveFleet()} disabled={!fleetValid || savingFleet || !fleetDirty}>
              {savingFleet ? t('Saving...') : t('Save Fleet Action Settings')}
            </button>
            {fleetDirty && <button type="button" className="btn btn-sm" onClick={() => { setFleetDirty(false); setFleetDraft(toFleetDraft(fleetActionSettings)); }}>{t('Discard changes')}</button>}
            {fleetDirty && <span className="text-dim">{t('Unsaved changes')}</span>}
          </div>
        </fieldset>
      </div>
    </>
  );
}
