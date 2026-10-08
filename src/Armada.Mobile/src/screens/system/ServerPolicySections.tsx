import { useState } from 'react';
import type { CliPermissionPolicy, VesselHealthCriterion } from '@dashboard/types/models';
import { bypassWarning, policyLabel, PROMPT_TIMEOUT_RANGE } from '@dashboard/lib/cliPermissions';
import { criterionLabel, formatCount, HEALTH_CRITERIA } from '@dashboard/lib/health/healthText';
import {
  CLI_PERMISSION_DEFAULTS,
  FLEET_ACTION_RANGES,
  FLEET_DEFAULTS,
  IMPORT_DEFAULTS,
  IMPORT_RANGES,
  mergeRepositoryHealth,
  rangeError,
  REPOSITORY_HEALTH_FIELDS,
  REPOSITORY_HEALTH_THRESHOLD_FIELDS,
  repositoryHealthCrossErrors,
  RETENTION_DEFAULTS,
  RETENTION_RANGE,
  validateRange,
  validateRetentionDraft,
  validPromptTimeout,
  type NumberField as RangeDef,
  type RetentionDraft,
} from '@dashboard/lib/settingsRanges';
import { AppText } from '../../components/ui/AppText';
import { Banner } from '../../components/ui/Banner';
import { ConfirmDialog } from '../../components/ui/ConfirmDialog';
import { SelectField } from '../../components/ui/SelectSheet';
import { SwitchField } from '../../components/ui/SwitchField';
import { useLocale } from '../../i18n/LocaleContext';
import { useSettingsSave, type SectionProps } from './ServerConfigSections';
import { useDraft } from './settingsModel';
import { ListEditor, NumberField, SaveRow, SettingsSection } from './settingsParts';

function useRangeText() {
  const { t } = useLocale();
  return (r: { min: number; max: number }) => t('Must be a whole number from {{min}} to {{max}}.', { min: r.min.toLocaleString(), max: r.max.toLocaleString() });
}

/** Vessel Import (`Import`): where the import wizard may browse and discover repositories. */
export function ImportSettingsSection({ settings, locked, onSaved }: SectionProps) {
  const { t } = useLocale();
  const rangeText = useRangeText();
  const s = settings.import ?? IMPORT_DEFAULTS;
  const draft = useDraft({
    allowedRoots: [...(s.allowedRoots ?? [])],
    excludedDirectoryNames: [...(s.excludedDirectoryNames ?? [])],
    maxDepth: String(s.maxDepth),
    inlineBatchLimit: String(s.inlineBatchLimit),
    categorizationTimeoutMinutes: String(s.categorizationTimeoutMinutes ?? 20),
  });
  const { save, saving } = useSettingsSave(onSaved);
  const v = draft.value;
  const err = (value: string, range: RangeDef) => (rangeError(value, range) ? rangeText(range) : null);
  const errors = {
    maxDepth: err(v.maxDepth, IMPORT_RANGES.maxDepth),
    inlineBatchLimit: err(v.inlineBatchLimit, IMPORT_RANGES.inlineBatchLimit),
    categorizationTimeoutMinutes: err(v.categorizationTimeoutMinutes, IMPORT_RANGES.categorizationTimeoutMinutes),
  };
  const valid = Object.values(errors).every((e) => !e);
  return (
    <SettingsSection title={t('Vessel Import')} description={t('Controls where the import wizard may browse and discover repositories on the Admiral host. Changes apply immediately.')}>
      <ListEditor label={t('Allowed roots')} help={t('Absolute folders that browse and discover are limited to. Empty means the user profile folder of the account running the Admiral.')} placeholder={t('/Users/alex/Code')} values={v.allowedRoots} onChange={(x) => draft.set({ allowedRoots: x })} disabled={locked} testID="settings-import-roots" />
      <ListEditor label={t('Excluded folder names')} help={t('Folder names discovery never descends into. Names starting with a dot are always skipped.')} placeholder="node_modules" values={v.excludedDirectoryNames} onChange={(x) => draft.set({ excludedDirectoryNames: x })} disabled={locked} />
      <NumberField label={t('Max depth')} hint={t('Folder levels searched below each scan root (1-16, default 6).')} error={errors.maxDepth} value={v.maxDepth} onChange={(x) => draft.set({ maxDepth: x })} disabled={locked} />
      <NumberField label={t('Inline batch limit')} hint={t('Largest selection imported inside the request; larger imports run as a background job (1-500, default 25).')} error={errors.inlineBatchLimit} value={v.inlineBatchLimit} onChange={(x) => draft.set({ inlineBatchLimit: x })} disabled={locked} />
      <NumberField label={t('Fleet categorization time limit (minutes)')} hint={t('Longest a captain may spend recommending fleets for an import before it is stopped (1-240, default 20).')} error={errors.categorizationTimeoutMinutes} value={v.categorizationTimeoutMinutes} onChange={(x) => draft.set({ categorizationTimeoutMinutes: x })} disabled={locked} />
      <SaveRow
        label={t('Save Import Settings')}
        saving={saving}
        disabled={locked || !valid || !draft.dirty}
        dirty={draft.dirty}
        onDiscard={draft.reset}
        onSave={async () => {
          const ok = await save({
            import: {
              allowedRoots: v.allowedRoots,
              maxDepth: Number(v.maxDepth),
              excludedDirectoryNames: v.excludedDirectoryNames,
              inlineBatchLimit: Number(v.inlineBatchLimit),
              categorizationTimeoutMinutes: Number(v.categorizationTimeoutMinutes),
            },
          }, t('Import settings saved and applied.'));
          if (ok) draft.reset();
        }}
      />
    </SettingsSection>
  );
}

/** Fleet Actions (`FleetActions`): limits for commands and missions run across many vessels. */
export function FleetActionSettingsSection({ settings, locked, onSaved }: SectionProps) {
  const { t } = useLocale();
  const rangeText = useRangeText();
  const s = settings.fleetActions ?? FLEET_DEFAULTS;
  const draft = useDraft({ maxConcurrency: String(s.maxConcurrency), defaultTimeoutSeconds: String(s.defaultTimeoutSeconds), maxOutputBytes: String(s.maxOutputBytes), runRetentionDays: String(s.runRetentionDays) });
  const { save, saving } = useSettingsSave(onSaved);
  const v = draft.value;
  const err = (key: keyof typeof FLEET_ACTION_RANGES) => (rangeError(v[key], FLEET_ACTION_RANGES[key]) ? rangeText(FLEET_ACTION_RANGES[key]) : null);
  const valid = (Object.keys(FLEET_ACTION_RANGES) as (keyof typeof FLEET_ACTION_RANGES)[]).every((k) => !err(k));
  return (
    <SettingsSection title={t('Fleet Actions')} description={t('Limits for commands and missions run across many vessels. Changes apply immediately.')}>
      <NumberField label={t('Max concurrency')} hint={t('Command targets executing at once across every run on the Admiral (1-32, default 8).')} error={err('maxConcurrency')} value={v.maxConcurrency} onChange={(x) => draft.set({ maxConcurrency: x })} disabled={locked} />
      <NumberField label={t('Default timeout (seconds)')} hint={t('Timeout for new Command actions that do not set one (5-7200, default 300).')} error={err('defaultTimeoutSeconds')} value={v.defaultTimeoutSeconds} onChange={(x) => draft.set({ defaultTimeoutSeconds: x })} disabled={locked} />
      <NumberField label={t('Max output bytes')} hint={t('Bytes kept per output stream per target; the end of the stream is kept (1024-1048576, default 65536).')} error={err('maxOutputBytes')} value={v.maxOutputBytes} onChange={(x) => draft.set({ maxOutputBytes: x })} disabled={locked} />
      <NumberField label={t('Run retention (days)')} hint={t('Finished runs older than this are pruned (1-3650, default 30).')} error={err('runRetentionDays')} value={v.runRetentionDays} onChange={(x) => draft.set({ runRetentionDays: x })} disabled={locked} />
      <SaveRow
        label={t('Save Fleet Action Settings')}
        saving={saving}
        disabled={locked || !valid || !draft.dirty}
        dirty={draft.dirty}
        onDiscard={draft.reset}
        onSave={async () => {
          const ok = await save({
            fleetActions: { maxConcurrency: Number(v.maxConcurrency), defaultTimeoutSeconds: Number(v.defaultTimeoutSeconds), maxOutputBytes: Number(v.maxOutputBytes), runRetentionDays: Number(v.runRetentionDays) },
          }, t('Fleet action settings saved and applied.'));
          if (ok) draft.reset();
        }}
      />
    </SettingsSection>
  );
}

/** Data Retention (`Retention`): days to keep Ask threads, jobs, import batches, CLI permission requests. */
export function RetentionSettingsSection({ settings, locked, onSaved }: SectionProps) {
  const { t } = useLocale();
  const rangeText = useRangeText();
  const s = { ...RETENTION_DEFAULTS, ...(settings.retention ?? {}) };
  const draft = useDraft<RetentionDraft>({
    askThreadArchiveAfterDays: String(s.askThreadArchiveAfterDays),
    askThreadDeleteAfterDays: String(s.askThreadDeleteAfterDays),
    jobRetentionDays: String(s.jobRetentionDays),
    importBatchRetentionDays: String(s.importBatchRetentionDays),
    cliPermissionRequestRetentionDays: String(s.cliPermissionRequestRetentionDays),
  });
  const { save, saving } = useSettingsSave(onSaved);
  const v = draft.value;
  const errors = validateRetentionDraft(v);
  const valid = Object.keys(errors).length === 0;
  const field = (key: keyof RetentionDraft, label: string, help: string) => (
    <NumberField label={label} hint={help} error={errors[key] ? rangeText(RETENTION_RANGE) : null} value={v[key]} onChange={(x) => draft.set({ [key]: x } as Partial<RetentionDraft>)} disabled={locked} />
  );
  return (
    <SettingsSection title={t('Data Retention')} description={t('How long Armada keeps Ask threads, finished background jobs, and import history. 0 means never. Changes apply immediately; pruning runs in the background about once an hour.')}>
      {field('askThreadArchiveAfterDays', t('Archive Ask threads after (days)'), t('Threads with no activity for this long are archived; pinned threads never are (0-3650, default 90).'))}
      {field('askThreadDeleteAfterDays', t('Delete Ask threads after (days)'), t('Threads with no activity for this long are deleted with their messages; pinned threads never are (0-3650, default 0).'))}
      {field('jobRetentionDays', t('Job retention (days)'), t('Finished background jobs older than this are deleted; the latest of each kind is kept (0-3650, default 30).'))}
      {field('importBatchRetentionDays', t('Import history retention (days)'), t('Finished vessel import batches older than this are deleted; imported vessels are not affected (0-3650, default 90).'))}
      {field('cliPermissionRequestRetentionDays', t('CLI permission request retention (days)'), t('Decided, expired, and cancelled CLI tool permission requests older than this are deleted; pending ones are kept (0-3650, default 90).'))}
      <SaveRow
        label={t('Save Retention Settings')}
        saving={saving}
        disabled={locked || !valid || !draft.dirty}
        dirty={draft.dirty}
        onDiscard={draft.reset}
        onSave={async () => {
          const ok = await save({
            retention: {
              askThreadArchiveAfterDays: Number(v.askThreadArchiveAfterDays),
              askThreadDeleteAfterDays: Number(v.askThreadDeleteAfterDays),
              jobRetentionDays: Number(v.jobRetentionDays),
              importBatchRetentionDays: Number(v.importBatchRetentionDays),
              cliPermissionRequestRetentionDays: Number(v.cliPermissionRequestRetentionDays),
            },
          }, t('Retention settings saved and applied.'));
          if (ok) draft.reset();
        }}
      />
    </SettingsSection>
  );
}

/** A CLI permission policy picker without Inherit (server defaults); choosing Bypass needs the strong warning. */
function PolicyField({ label, hint, value, onChange, disabled, testID }: { label: string; hint: string; value: CliPermissionPolicy; onChange: (v: CliPermissionPolicy) => void; disabled?: boolean; testID?: string }) {
  const { t } = useLocale();
  const [confirm, setConfirm] = useState(false);
  return (
    <>
      <SelectField
        label={label}
        hint={hint}
        value={value}
        options={(['Refuse', 'ApproveInArmada', 'Bypass'] as CliPermissionPolicy[]).map((p) => ({ value: p, label: policyLabel(t, p) }))}
        onChange={(next) => { if (next === 'Bypass') setConfirm(true); else onChange(next); }}
        closeLabel={t('Close')}
        disabled={disabled}
        testID={testID}
      />
      <ConfirmDialog
        open={confirm}
        title={t('Allow every CLI tool call without asking?')}
        message={bypassWarning(t)}
        confirmLabel={t('Use Bypass')}
        cancelLabel={t('Cancel')}
        danger
        onConfirm={() => { setConfirm(false); onChange('Bypass'); }}
        onCancel={() => setConfirm(false)}
        testID="settings-bypass-confirm"
      />
    </>
  );
}

/** CLI Tool Permissions (`Permissions`, global admins only): default policies, owner approval, prompt timeout. */
export function CliPermissionSettingsSection({ settings, locked, onSaved }: SectionProps) {
  const { t } = useLocale();
  const s = { ...CLI_PERMISSION_DEFAULTS, ...(settings.permissions ?? {}) };
  const draft = useDraft({ askDefaultPolicy: s.askDefaultPolicy, missionDefaultPolicy: s.missionDefaultPolicy, allowOwnerApproval: !!s.allowOwnerApproval, promptTimeoutSeconds: String(s.promptTimeoutSeconds) });
  const { save, saving } = useSettingsSave(onSaved);
  const v = draft.value;
  const timeoutValid = validPromptTimeout(v.promptTimeoutSeconds);
  return (
    <SettingsSection title={t('CLI Tool Permissions')} description={t('How CLI captains handle shell commands, file edits, and fetches that need permission when neither the conversation nor the captain sets a policy. Refuse lets the CLI refuse them; Approve in Armada asks an approver here (Claude Code only; other runtimes fall back to Refuse); Bypass runs them without asking.')}>
      <PolicyField label={t('Ask conversation default')} hint={t('Used by Ask conversation turns (default Approve in Armada). Narrations and summaries never ask; they refuse instead.')} value={v.askDefaultPolicy} onChange={(x) => draft.set({ askDefaultPolicy: x })} disabled={locked} testID="settings-cli-ask-default" />
      <PolicyField label={t('Mission default')} hint={t('Used by missions (default Bypass, the previous behavior). A vessel with auto-approve off caps missions at Refuse.')} value={v.missionDefaultPolicy} onChange={(x) => draft.set({ missionDefaultPolicy: x })} disabled={locked} />
      <NumberField
        label={t('Prompt timeout (seconds)')}
        hint={t('How long a request waits for a decision before it expires and is denied (10-3600, default 600).')}
        error={timeoutValid ? null : t('Must be a whole number from {{min}} to {{max}}.', { min: PROMPT_TIMEOUT_RANGE.min, max: PROMPT_TIMEOUT_RANGE.max.toLocaleString() })}
        value={v.promptTimeoutSeconds}
        onChange={(x) => draft.set({ promptTimeoutSeconds: x })}
        disabled={locked}
      />
      <SwitchField label={t('Let owners approve their own requests')} hint={t('When on, the owner of a conversation or mission may allow or deny its requests. Admins and tenant admins can always decide; only they can create rules.')} value={v.allowOwnerApproval} onChange={(x) => draft.set({ allowOwnerApproval: x })} disabled={locked} />
      <SaveRow
        label={t('Save CLI Tool Permissions')}
        saving={saving}
        disabled={locked || !timeoutValid || !draft.dirty}
        dirty={draft.dirty}
        onDiscard={draft.reset}
        onSave={async () => {
          const ok = await save({
            permissions: { askDefaultPolicy: v.askDefaultPolicy, missionDefaultPolicy: v.missionDefaultPolicy, allowOwnerApproval: v.allowOwnerApproval, promptTimeoutSeconds: Number(v.promptTimeoutSeconds) },
          }, t('CLI tool permission settings saved.'));
          if (ok) draft.reset();
        }}
      />
    </SettingsSection>
  );
}

/** Repository Health (`RepositoryHealth`): schedule, dependency freshness, scored criteria, thresholds; admins edit. */
export function RepositoryHealthSettingsSection({ settings, locked, onSaved, isAdmin }: SectionProps & { isAdmin: boolean }) {
  const { t, locale } = useLocale();
  const merged = mergeRepositoryHealth(settings.repositoryHealth);
  const source: Record<string, string> = { fetchBeforeEvaluate: merged.fetchBeforeEvaluate ? '1' : '', scoredCriteria: merged.scoredCriteria.join(',') };
  for (const f of REPOSITORY_HEALTH_FIELDS) source[f.key] = String(merged[f.key]);
  for (const f of REPOSITORY_HEALTH_THRESHOLD_FIELDS) source[`thresholds.${f.key}`] = String(merged.thresholds[f.key]);
  const draft = useDraft<Record<string, string>>(source);
  const { save, saving } = useSettingsSave(onSaved);
  const v = draft.value;
  const editable = isAdmin && !locked;
  const criteria = (v.scoredCriteria ? v.scoredCriteria.split(',') : []) as VesselHealthCriterion[];

  const errors: Record<string, string | null> = {};
  for (const f of REPOSITORY_HEALTH_FIELDS) errors[f.key] = validateRange(v[f.key] ?? '', f.min, f.max);
  for (const f of REPOSITORY_HEALTH_THRESHOLD_FIELDS) errors[`thresholds.${f.key}`] = validateRange(v[`thresholds.${f.key}`] ?? '', f.min, f.max);
  const crossErrors = repositoryHealthCrossErrors(v, errors);
  const hasErrors = Object.values(errors).some((e) => e !== null) || crossErrors.length > 0;
  const num = (key: string) => parseInt(v[key] ?? '', 10);

  const field = (key: string, f: { label: string; help: string; min: number; max: number }) => {
    const e = errors[key];
    return (
      <NumberField
        key={key}
        label={t(f.label)}
        hint={`${t(f.help)} ${t('Range {{min}} to {{max}}.', { min: formatCount(locale, f.min), max: formatCount(locale, f.max) })}`}
        error={e ? t(e, { min: formatCount(locale, f.min), max: formatCount(locale, f.max) }) : null}
        value={v[key] ?? ''}
        onChange={(x) => draft.set({ [key]: x })}
        disabled={!editable}
      />
    );
  };

  return (
    <SettingsSection title={t('Repository Health')} description={t('Controls scheduled vessel health evaluation, dependency freshness, and the thresholds behind Warn and Fail. Changes apply immediately.')}>
      {!isAdmin ? <AppText variant="caption" muted>{t('Only administrators can change these settings.')}</AppText> : null}
      {REPOSITORY_HEALTH_FIELDS.map((f) => field(f.key, f))}
      <SwitchField label={t('Fetch before evaluating')} hint={t('Run git fetch before measuring divergence. If the fetch fails, divergence is Unknown.')} value={!!v.fetchBeforeEvaluate} onChange={(x) => draft.set({ fetchBeforeEvaluate: x ? '1' : '' })} disabled={!editable} />
      <AppText variant="label">{t('Scored criteria')}</AppText>
      <AppText variant="caption" muted>{t('The overall status is the worst status among these criteria.')}</AppText>
      {HEALTH_CRITERIA.map((c) => (
        <SwitchField
          key={c}
          label={criterionLabel(t, c)}
          value={criteria.includes(c)}
          disabled={!editable}
          onChange={(on) => {
            const next = on ? [...criteria, c] : criteria.filter((x) => x !== c);
            draft.set({ scoredCriteria: HEALTH_CRITERIA.filter((x) => next.includes(x)).join(',') });
          }}
        />
      ))}
      {criteria.length === 0 ? <Banner tone="warning" title={t('With no scored criteria every vessel is Unknown overall.')} /> : null}
      <AppText variant="label">{t('Thresholds')}</AppText>
      {REPOSITORY_HEALTH_THRESHOLD_FIELDS.map((f) => field(`thresholds.${f.key}`, f))}
      {crossErrors.map((e) => <AppText key={e} color="danger" accessibilityRole="alert">{t(e)}</AppText>)}
      {isAdmin ? (
        <SaveRow
          label={t('Save Repository Health Settings')}
          saving={saving}
          disabled={!editable || hasErrors}
          dirty={draft.dirty}
          onDiscard={draft.reset}
          onSave={async () => {
            const ok = await save({
              repositoryHealth: {
                ...merged,
                fetchBeforeEvaluate: !!v.fetchBeforeEvaluate,
                scoredCriteria: criteria,
                intervalMinutes: num('intervalMinutes'),
                maxConcurrency: num('maxConcurrency'),
                dependencyMaxAgeHours: num('dependencyMaxAgeHours'),
                dependencyCommandTimeoutSeconds: num('dependencyCommandTimeoutSeconds'),
                staleBranchDays: num('staleBranchDays'),
                missionWindowDays: num('missionWindowDays'),
                thresholds: {
                  behindWarn: num('thresholds.behindWarn'),
                  behindFail: num('thresholds.behindFail'),
                  staleBranchWarn: num('thresholds.staleBranchWarn'),
                  staleBranchFail: num('thresholds.staleBranchFail'),
                  missionFailureWarn: num('thresholds.missionFailureWarn'),
                  missionFailureFail: num('thresholds.missionFailureFail'),
                },
              },
            }, t('Repository health settings saved. They apply immediately.'));
            if (ok) draft.reset();
          }}
        />
      ) : null}
    </SettingsSection>
  );
}
