import { useCallback, useEffect, useRef, useState } from 'react';
import { getBlackBoxStorage, getBlackBoxStorageMigration, migrateBlackBoxStorage, updateSettings } from '../../api/client';
import type {
  BlackBoxRetentionPolicy,
  BlackBoxSettingsData,
  BlackBoxStorageMigrationJob,
  BlackBoxStorageStatus,
  BlackBoxStorageTarget,
  BlackBoxTargetSettings,
} from '../../types/blackbox';
import { useLocale } from '../../context/LocaleContext';
import { BLACKBOX_STORAGE_TARGETS, blackBoxErrorStatus, formatBlackBoxBytes } from '../../lib/blackbox';
import { REDACTED_SECRET } from '../../lib/serverSettings';
import ConfirmDialog from '../shared/ConfirmDialog';
import '../blackbox/BlackBox.css';

interface BlackBoxSettingsProps {
  blackBox: BlackBoxSettingsData | null | undefined;
  /** Disable editing (remote proxy mode). */
  locked: boolean;
  onSaved: (updated: Record<string, unknown>) => void;
  notify: (severity: 'success' | 'error', message: string) => void;
}

/** Settings object of each external target in blackBox.storage. */
const TARGET_SECTION: Partial<Record<BlackBoxStorageTarget, string>> = {
  Disk: 'disk', S3: 's3', AzureBlob: 'azureBlob', GoogleCloud: 'googleCloud', Cifs: 'cifs', Nfs: 'nfs',
};

/** Fields of each target, in display order; fields the server adds later are shown after these. */
const TARGET_FIELDS: Record<string, string[]> = {
  disk: ['directory'],
  s3: ['endpoint', 'region', 'bucket', 'accessKey', 'secretKey', 'usePathStyle', 'useSsl'],
  azureBlob: ['accountName', 'container', 'endpoint', 'accessKey', 'connectionString'],
  googleCloud: ['projectId', 'bucket', 'endpoint', 'credentialsPath'],
  cifs: ['hostname', 'port', 'share', 'domain', 'username', 'password', 'preferEncryption'],
  nfs: ['hostname', 'exportPath', 'version', 'port', 'mountPort', 'userId', 'groupId'],
};

/** Secret fields: write-only (read back masked), never shown. */
const SECRET_FIELDS = new Set(['accessKey', 'secretKey', 'connectionString', 'credentialsPath', 'password']);

const FIELD_LABELS: Record<string, string> = {
  directory: 'Directory', endpoint: 'Endpoint', region: 'Region', bucket: 'Bucket', accessKey: 'Access key', secretKey: 'Secret key',
  usePathStyle: 'Use path-style URLs', useSsl: 'Use TLS', accountName: 'Account name', container: 'Container',
  connectionString: 'Connection string', projectId: 'Project ID', credentialsPath: 'Credentials file path', hostname: 'Hostname',
  port: 'Port', share: 'Share', domain: 'Domain', username: 'Username', password: 'Password', preferEncryption: 'Prefer encryption',
  exportPath: 'Export path', version: 'NFS version', mountPort: 'Mount port', userId: 'User ID', groupId: 'Group ID',
};

interface RetentionFieldDef { key: keyof BlackBoxRetentionPolicy; label: string; help: string; min?: number; max?: number; bool?: boolean }

/** blackBox.retention fields (MISSION_BLACKBOX_PLAN.md 7.1). */
export const BLACKBOX_RETENTION_FIELDS: RetentionFieldDef[] = [
  { key: 'retentionDays', label: 'Retention (days)', help: 'Age after which an unpinned entry expires (0 keeps entries forever; default 180).', min: 0, max: 3650 },
  { key: 'resolvedRetentionDays', label: 'Resolved retention (days)', help: 'Age after resolution after which a resolved entry expires, if sooner (default 30).', min: 0, max: 3650 },
  { key: 'maxEntriesPerVessel', label: 'Max entries per vessel', help: 'Oldest unpinned, non-current entries are evicted above this (10 to 100000).', min: 10, max: 100000 },
  { key: 'maxTotalBytes', label: 'Max total bytes', help: 'Global cap; oldest unpinned resolved entries go first (16 MB to 1 TB; default 1 GB).', min: 16777216, max: 1099511627776 },
  { key: 'gcIntervalMinutes', label: 'GC interval (minutes)', help: 'How often garbage collection runs (5 to 1440).', min: 5, max: 1440 },
  { key: 'gcBatchSize', label: 'GC batch size', help: 'Entries per garbage collection batch (10 to 1000).', min: 10, max: 1000 },
  { key: 'orphanGraceHours', label: 'Orphan grace (hours)', help: 'Minimum age before an unreferenced external object is deleted (1 to 720).', min: 1, max: 720 },
  { key: 'keepLatestPerMission', label: 'Keep the latest entry of each mission', help: 'Keep a mission\'s current entry until the mission is deleted, even past expiry.', bool: true },
  { key: 'deleteWithMission', label: 'Delete with the mission', help: 'Delete a mission\'s entries when the mission is purged.', bool: true },
  { key: 'deleteWithVessel', label: 'Delete with the vessel', help: 'Delete a vessel\'s entries when the vessel is deleted.', bool: true },
];

function clone<T>(value: T): T {
  return JSON.parse(JSON.stringify(value)) as T;
}

function humanize(key: string): string {
  return key.replace(/([A-Z])/g, ' $1').replace(/^./, (c) => c.toUpperCase());
}

const DONE_STATES = new Set(['Completed', 'Failed', 'Cancelled']);
const MIGRATION_POLL_MS = 2000;

/**
 * Settings page section for the black box: the storage target and its fields (secrets are write-only: the server
 * returns them masked, and a field left untouched keeps the stored value), the store's health, a migration between
 * targets with live progress, and the retention policy. Saves the whole blackBox group.
 */
export default function BlackBoxSettings({ blackBox, locked, onSaved, notify }: BlackBoxSettingsProps) {
  const { t, formatDateTime } = useLocale();
  const [draft, setDraft] = useState<BlackBoxSettingsData | null>(() => (blackBox ? clone(blackBox) : null));
  const [dirty, setDirty] = useState(false);
  const [saving, setSaving] = useState(false);
  const [status, setStatus] = useState<BlackBoxStorageStatus | null>(null);
  const [statusError, setStatusError] = useState('');
  const [migrateTo, setMigrateTo] = useState<BlackBoxStorageTarget | ''>('');
  const [deleteSource, setDeleteSource] = useState(false);
  const [confirmMigrate, setConfirmMigrate] = useState(false);
  const [job, setJob] = useState<BlackBoxStorageMigrationJob | null>(null);
  const [migrateError, setMigrateError] = useState('');
  const pollRef = useRef<ReturnType<typeof setInterval> | null>(null);

  useEffect(() => { if (!dirty) setDraft(blackBox ? clone(blackBox) : null); }, [blackBox, dirty]);

  const loadStatus = useCallback(async () => {
    try {
      const s = await getBlackBoxStorage();
      setStatus(s);
      setStatusError('');
      return s;
    } catch (err: unknown) {
      setStatusError(t('Storage status could not be read: {{message}}', { message: err instanceof Error ? err.message : String(err) }));
      return null;
    }
  }, [t]);

  const stopPolling = useCallback(() => {
    if (pollRef.current) clearInterval(pollRef.current);
    pollRef.current = null;
  }, []);

  const follow = useCallback((id: string) => {
    stopPolling();
    const tick = async () => {
      try {
        const next = await getBlackBoxStorageMigration(id);
        setJob(next);
        if (DONE_STATES.has(next.state)) { stopPolling(); void loadStatus(); }
      } catch {
        stopPolling();
      }
    };
    void tick();
    pollRef.current = setInterval(() => { void tick(); }, MIGRATION_POLL_MS);
  }, [loadStatus, stopPolling]);

  useEffect(() => {
    void loadStatus().then((s) => { if (s?.activeMigrationId) follow(s.activeMigrationId); });
    return stopPolling;
  }, [loadStatus, follow, stopPolling]);

  if (!draft) return null;
  const storage = draft.storage;
  const section = TARGET_SECTION[storage.target];
  const targetSettings: BlackBoxTargetSettings = section ? ((storage[section] as BlackBoxTargetSettings | undefined) ?? {}) : {};
  // The server writes every field back (nulls included), so the fields present are the fields this server knows.
  const known = section ? TARGET_FIELDS[section] ?? [] : [];
  const fieldKeys = section ? [...known.filter((k) => k in targetSettings), ...Object.keys(targetSettings).filter((k) => !known.includes(k))] : [];

  function update(mutate: (d: BlackBoxSettingsData) => void) {
    setDraft((d) => {
      if (!d) return d;
      const next = clone(d);
      mutate(next);
      return next;
    });
    setDirty(true);
  }

  function setTargetField(key: string, value: string | number | boolean | null) {
    if (!section) return;
    update((d) => {
      const current = (d.storage[section] as BlackBoxTargetSettings | undefined) ?? {};
      d.storage[section] = { ...current, [key]: value };
    });
  }

  async function save() {
    if (!draft) return;
    setSaving(true);
    try {
      const updated = await updateSettings({ blackBox: draft });
      setDirty(false);
      onSaved(updated as Record<string, unknown>);
      notify('success', t('Black box settings saved.'));
      void loadStatus();
    } catch (err: unknown) {
      notify('error', t('Failed: {{message}}', { message: err instanceof Error ? err.message : t('Unknown error') }));
    } finally {
      setSaving(false);
    }
  }

  async function migrate() {
    setConfirmMigrate(false);
    if (!migrateTo) return;
    setMigrateError('');
    try {
      const started = await migrateBlackBoxStorage({ to: migrateTo, from: status?.target ?? null, deleteSource });
      setJob(started);
      follow(started.id);
    } catch (err: unknown) {
      setMigrateError(blackBoxErrorStatus(err) === 501
        ? t('This server cannot migrate black box storage yet.')
        : t('The migration could not start: {{message}}', { message: err instanceof Error ? err.message : String(err) }));
    }
  }

  function targetField(key: string) {
    const value = targetSettings[key];
    const label = FIELD_LABELS[key] ? t(FIELD_LABELS[key]) : humanize(key);
    const id = `blackbox-${section}-${key}`;
    if (SECRET_FIELDS.has(key)) {
      const stored = value === REDACTED_SECRET;
      return (
        <div className="form-group" key={key}>
          <label htmlFor={id}>{label}</label>
          <input
            id={id}
            type="password"
            autoComplete="new-password"
            data-secret="true"
            value={stored ? '' : String(value ?? '')}
            placeholder={stored ? t('Stored (leave blank to keep)') : t('Not set')}
            onChange={(e) => setTargetField(key, e.target.value === '' && stored ? REDACTED_SECRET : e.target.value || null)}
          />
          <span className="text-dim form-help">{t('Write-only: the stored value is never shown.')}</span>
        </div>
      );
    }
    if (typeof value === 'boolean') {
      return (
        <div className="form-group" key={key}>
          <label className="settings-checkbox-label"><input id={id} type="checkbox" checked={value} onChange={(e) => setTargetField(key, e.target.checked)} /><span>{label}</span></label>
        </div>
      );
    }
    if (typeof value === 'number') {
      return (
        <div className="form-group" key={key}>
          <label htmlFor={id}>{label}</label>
          <input id={id} type="number" value={value} onChange={(e) => setTargetField(key, e.target.value === '' ? null : Number(e.target.value))} />
        </div>
      );
    }
    return (
      <div className="form-group" key={key}>
        <label htmlFor={id}>{label}</label>
        <input id={id} type="text" value={String(value ?? '')} onChange={(e) => setTargetField(key, e.target.value || null)} />
      </div>
    );
  }

  const health = status?.health;
  const progress = job && job.totalEntries > 0 ? Math.min(100, Math.round(((job.migratedEntries + job.failedEntries) / job.totalEntries) * 100)) : 0;
  const migrating = !!job && !DONE_STATES.has(job.state);

  return (
    <div className="settings-section" style={{ marginTop: '1.5rem' }} data-testid="blackbox-settings">
      <h3>{t('Black Box')}</h3>
      <p className="text-muted" style={{ marginBottom: '0.75rem' }}>
        {t('Where mission and voyage failure records are stored and how long they are kept. Storage credentials are write-only.')}
      </p>

      <div className="blackbox-notice blackbox-notice-info" role="status" style={{ marginBottom: '0.75rem' }} data-testid="blackbox-storage-health">
        {statusError || (health
          ? (health.healthy
            ? t('Store {{target}} is healthy ({{latency}} ms, checked {{time}}).', { target: t(health.target), latency: health.latencyMs ?? '-', time: formatDateTime(health.checkedUtc) })
            : t('Store {{target}} is unhealthy: {{message}}', { target: t(health.target), message: health.message ?? '' }))
          : t('Checking the store...'))}
        {status && <> {t('Fallback to the database: {{value}}.', { value: status.fallbackToDatabase ? t('On') : t('Off') })}</>}
        {!!status?.usage?.length && <> {status.usage.map((u) => `${t(u.target)}: ${u.entries} / ${formatBlackBoxBytes(u.bytes)}`).join('; ')}</>}
        {' '}<button type="button" className="btn btn-sm" onClick={() => void loadStatus()} title={t('Check the store health again.')}>{t('Check again')}</button>
      </div>

      <fieldset disabled={locked} className="settings-fieldset">
        <div className="settings-grid">
          <div className="form-group">
            <label htmlFor="blackbox-target">{t('Storage target')}</label>
            <select id="blackbox-target" value={storage.target} onChange={(e) => update((d) => { d.storage.target = e.target.value as BlackBoxStorageTarget; })}>
              {BLACKBOX_STORAGE_TARGETS.map((target) => <option key={target} value={target}>{t(target)}</option>)}
            </select>
            <span className="text-dim form-help">{t('New entries go to this target. Existing entries stay where they are until you migrate them.')}</span>
          </div>
          <div className="form-group">
            <label htmlFor="blackbox-prefix">{t('Key prefix')}</label>
            <input id="blackbox-prefix" value={storage.keyPrefix ?? ''} onChange={(e) => update((d) => { d.storage.keyPrefix = e.target.value; })} />
          </div>
          <div className="form-group">
            <label className="settings-checkbox-label"><input type="checkbox" checked={storage.compress ?? true} onChange={(e) => update((d) => { d.storage.compress = e.target.checked; })} /><span>{t('Compress objects')}</span></label>
          </div>
          <div className="form-group">
            <label className="settings-checkbox-label"><input type="checkbox" checked={storage.fallbackToDatabase ?? true} onChange={(e) => update((d) => { d.storage.fallbackToDatabase = e.target.checked; })} /><span>{t('Fall back to the database when the store is down')}</span></label>
          </div>
          {fieldKeys.map(targetField)}
        </div>

        <h4 style={{ margin: '1rem 0 0.5rem' }}>{t('Retention')}</h4>
        <div className="settings-grid">
          {BLACKBOX_RETENTION_FIELDS.map((f) => {
            const value = draft.retention?.[f.key];
            const id = `blackbox-retention-${f.key}`;
            if (f.bool) {
              return (
                <div className="form-group" key={f.key}>
                  <label className="settings-checkbox-label" title={t(f.help)}><input id={id} type="checkbox" checked={!!value} onChange={(e) => update((d) => { (d.retention as unknown as Record<string, unknown>)[f.key] = e.target.checked; })} /><span>{t(f.label)}</span></label>
                </div>
              );
            }
            const n = Number(value);
            const invalid = !Number.isFinite(n) || (f.min !== undefined && n < f.min) || (f.max !== undefined && n > f.max);
            return (
              <div className="form-group" key={f.key}>
                <label htmlFor={id}>{t(f.label)}</label>
                <input id={id} type="number" min={f.min} max={f.max} value={value === undefined ? '' : String(value)} aria-invalid={invalid} onChange={(e) => update((d) => { (d.retention as unknown as Record<string, unknown>)[f.key] = e.target.value === '' ? '' : Number(e.target.value); })} />
                <span className={invalid ? 'field-error' : 'text-dim form-help'}>{t(f.help)}</span>
              </div>
            );
          })}
        </div>

        <div className="settings-actions">
          <button type="button" className="btn-primary btn-sm" onClick={() => void save()} disabled={saving || !dirty || BLACKBOX_RETENTION_FIELDS.some((f) => !f.bool && (() => { const n = Number(draft.retention?.[f.key]); return !Number.isFinite(n) || (f.min !== undefined && n < f.min) || (f.max !== undefined && n > f.max); })())} title={t('Save this section to the Admiral. It applies immediately; other sections are saved separately.')}>
            {saving ? t('Saving...') : t('Save Black Box Settings')}
          </button>
          {dirty && <button type="button" className="btn btn-sm" onClick={() => { setDirty(false); setDraft(blackBox ? clone(blackBox) : null); }}>{t('Discard changes')}</button>}
          {dirty && <span className="text-dim">{t('Unsaved changes')}</span>}
        </div>
      </fieldset>

      <h4 style={{ margin: '1rem 0 0.5rem' }}>{t('Migrate stored entries')}</h4>
      <div className="settings-grid">
        <div className="form-group">
          <label htmlFor="blackbox-migrate-to">{t('Move entries to')}</label>
          <select id="blackbox-migrate-to" value={migrateTo} disabled={locked || migrating} onChange={(e) => setMigrateTo(e.target.value as BlackBoxStorageTarget | '')}>
            <option value="">{t('Choose a target')}</option>
            {BLACKBOX_STORAGE_TARGETS.filter((target) => target !== status?.target).map((target) => <option key={target} value={target}>{t(target)}</option>)}
          </select>
        </div>
        <div className="form-group">
          <label className="settings-checkbox-label"><input type="checkbox" checked={deleteSource} disabled={locked || migrating} onChange={(e) => setDeleteSource(e.target.checked)} /><span>{t('Delete the source copy after each entry moves')}</span></label>
        </div>
      </div>
      <button type="button" className="btn btn-sm" disabled={locked || migrating || !migrateTo || dirty} onClick={() => setConfirmMigrate(true)} title={dirty ? t('Save or discard the changes above first.') : t('Copy every stored entry to the chosen target in the background.')}>{t('Migrate')}</button>
      {migrateError && <div className="blackbox-notice blackbox-notice-error" role="alert" style={{ marginTop: '0.5rem' }}>{migrateError}</div>}
      {job && (
        <div style={{ marginTop: '0.6rem' }} data-testid="blackbox-migration">
          <div className="text-dim" style={{ fontSize: '0.85rem', marginBottom: '0.25rem' }}>
            {t('Migration {{id}} ({{state}}): {{migrated}} of {{total}} moved, {{failed}} failed, {{bytes}} copied.', {
              id: job.id, state: t(job.state), migrated: job.migratedEntries, total: job.totalEntries, failed: job.failedEntries, bytes: formatBlackBoxBytes(job.bytesCopied),
            })}
          </div>
          <div className="blackbox-progress" role="progressbar" aria-valuemin={0} aria-valuemax={100} aria-valuenow={progress} aria-label={t('Migration progress')}><div style={{ width: `${progress}%` }} /></div>
          {job.lastError && <div className="text-dim" style={{ fontSize: '0.8rem' }}>{job.lastError}</div>}
        </div>
      )}

      <ConfirmDialog
        open={confirmMigrate}
        title="Migrate black box storage"
        message={t('Copy every stored black box entry from {{from}} to {{to}}? It runs in the background; entries stay readable while they move.', { from: t(status?.target ?? storage.target), to: t(migrateTo || '-') })}
        confirmLabel="Migrate"
        onConfirm={() => void migrate()}
        onCancel={() => setConfirmMigrate(false)}
      />
    </div>
  );
}
