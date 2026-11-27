import { useCallback, useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { apiErrorMessage, getModelProfile, updateModelProfile } from '../api/client';
import type { ModelProfile, TimeoutSource } from '../types/models';
import { useAuth } from '../context/AuthContext';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import ActionMenu from '../components/shared/ActionMenu';
import CopyButton from '../components/shared/CopyButton';
import ErrorModal from '../components/shared/ErrorModal';
import JsonViewer from '../components/shared/JsonViewer';
import LoadingIndicator from '../components/shared/LoadingIndicator';
import PageHeader from '../components/shared/PageHeader';
import {
  PROMPT_VARIANTS, RECOVERY_FIELDS, TIERS, TIMEOUT_FIELDS, capabilityLabel, formToUpdate, profileToForm, servedContextLabel, validateProfileForm,
  type ModelProfileForm, type TriState,
} from '../lib/modelProfiles';

const TIER_TIP = 'Tier routing uses for captains on this profile when the captain has no explicit tier. Blank classifies them from the model name. A suggested tier never changes routing until applied here or from its Needs You alert.';
const TEMPLATE_TIP = 'Persona prompt variant the profile renders. Blank picks one from the served context: Compact below 32K tokens, Lean below 64K, else Current.';
const TOOLS_TIP = 'Whether the model can call tools as served. Blank uses the probe result.';
const MCP_TIP = 'Whether the model can use MCP tools as served. Blank uses the probe result.';

function sourceLabel(source: TimeoutSource, t: (key: string) => string): string {
  return source === 'Profile' ? t('Profile') : source === 'Measured' ? t('Measured speed') : t('Global setting');
}

function TriSelect({ value, onChange, label, tip }: { value: TriState; onChange: (v: TriState) => void; label: string; tip: string }) {
  const { t } = useLocale();
  return (
    <label title={t(tip)}>{t(label)}
      <select aria-label={t(label)} value={value} onChange={e => onChange(e.target.value as TriState)} title={t(tip)}>
        <option value="">{t('Not set')}</option>
        <option value="true">{t('Yes')}</option>
        <option value="false">{t('No')}</option>
      </select>
    </label>
  );
}

/**
 * One model profile: identity and served context, capabilities (set versus probed), the administrator tier and the
 * outcome-data suggestion, rendering, recovery and timeout overrides with the values in effect, speed, and revisions.
 * Admins edit the tier, capabilities, rendering, recovery, timeouts and notes; the server enforces AdminOnly.
 */
export default function ModelProfileDetail() {
  const { id } = useParams<{ id: string }>();
  const { t, formatDateTime } = useLocale();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { pushToast } = useNotifications();
  const canEdit = isAdmin || isTenantAdmin;
  const [profile, setProfile] = useState<ModelProfile | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [form, setForm] = useState<ModelProfileForm | null>(null);
  const [formError, setFormError] = useState('');
  const [saving, setSaving] = useState(false);
  const [showJson, setShowJson] = useState(false);

  const load = useCallback(async () => {
    if (!id) return;
    try {
      setLoading(true);
      setProfile(await getModelProfile(id));
      setError('');
    } catch (err: unknown) {
      setError(apiErrorMessage(err, t('Failed to load model profile.')));
    } finally {
      setLoading(false);
    }
  }, [id, t]);

  useEffect(() => { void load(); }, [load]);

  async function handleSave(e: React.FormEvent) {
    e.preventDefault();
    if (!profile || !form) return;
    const problem = validateProfileForm(form, t);
    if (problem) { setFormError(problem); return; }
    setSaving(true);
    try {
      const updated = await updateModelProfile(profile.id, formToUpdate(form));
      setProfile(updated);
      setForm(null);
      pushToast('success', t('Model profile saved.'));
    } catch (err: unknown) {
      setFormError(apiErrorMessage(err, t('Save failed.')));
    } finally {
      setSaving(false);
    }
  }

  if (loading && !profile) return <LoadingIndicator fullHeight label={t('Loading...')} />;
  if (!profile) return error ? <ErrorModal error={error} onClose={() => setError('')} /> : <p className="text-dim">{t('Model profile not found.')}</p>;

  const name = profile.identity.model || profile.identity.runtime;
  const recovery = profile.effectiveRecovery;
  const timeouts = profile.effectiveTimeouts;

  return (
    <div>
      <PageHeader
        breadcrumb={[{ label: t('Model Profiles'), to: '/captains?tab=profiles' }, { label: name }]}
        title={name}
        subtitle={t('The model as served, shared by every captain that runs it.')}
        actions={(
          <>
            {canEdit && <button className="btn btn-primary" onClick={() => { setFormError(''); setForm(profileToForm(profile)); }} title={t('Change the tier, capabilities, rendering, recovery policy, timeouts and notes of this profile.')}>{t('Edit')}</button>}
            <ActionMenu id={`model-profile-${profile.id}`} items={[
              { label: 'View JSON', title: 'Show this profile\'s full record as JSON, as the API returns it.', onClick: () => setShowJson(true) },
            ]} />
          </>
        )}
      />
      <ErrorModal error={error} onClose={() => setError('')} />
      <JsonViewer open={showJson} title={t('Model profile: {{name}}', { name })} data={profile} onClose={() => setShowJson(false)} />

      <div className="detail-grid">
        <div className="detail-field">
          <span className="detail-label" title={t('Unique model profile ID (mdl_ prefix).')}>{t('ID')}</span>
          <span className="id-display"><span className="mono">{profile.id}</span><CopyButton text={profile.id} /></span>
        </div>
        <div className="detail-field"><span className="detail-label">{t('Runtime')}</span><span>{profile.identity.runtime}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Provider')}</span><span>{profile.identity.provider || '-'}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Model')}</span><span>{profile.identity.model || t('(runtime default)')}</span></div>
        <div className="detail-field">
          <span className="detail-label" title={t('Context window the model is served with, and whether it was configured, discovered from the model server, or measured.')}>{t('Served context')}</span>
          <span>{servedContextLabel(profile.identity, t)}</span>
        </div>
        <div className="detail-field">
          <span className="detail-label" title={t(TIER_TIP)}>{t('Tier')}</span>
          <span>{profile.tiering.tier ? t(profile.tiering.tier) : <span className="text-dim">{t('From model name')}</span>}</span>
        </div>
        <div className="detail-field">
          <span className="detail-label" title={t('Tier the outcome data suggests. It changes nothing until an administrator applies it.')}>{t('Suggested tier')}</span>
          <span title={profile.tiering.suggestedReason || ''}>
            {profile.tiering.suggestedTier
              ? t('{{tier}} from {{count}} attempts', { tier: t(profile.tiering.suggestedTier), count: profile.tiering.suggestedSamples ?? 0 })
              : <span className="text-dim">{t('None')}</span>}
          </span>
        </div>
        <div className="detail-field"><span className="detail-label">{t('Created')}</span><span>{formatDateTime(profile.createdUtc)}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Last Updated')}</span><span>{formatDateTime(profile.lastUpdateUtc)}</span></div>
      </div>

      <h3 title={t('Set means an administrator chose the value; Probed means a probe run measured it.')}>{t('Capabilities')}</h3>
      <div className="detail-grid">
        <div className="detail-field"><span className="detail-label">{t('Tools')}</span><span>{capabilityLabel(profile.capabilities.toolsAvailable, profile.capabilities.probedToolsAvailable, t)}</span></div>
        <div className="detail-field"><span className="detail-label">{t('MCP tools')}</span><span>{capabilityLabel(profile.capabilities.mcpAvailable, profile.capabilities.probedMcpAvailable, t)}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Last probed')}</span><span>{profile.capabilities.probedUtc ? formatDateTime(profile.capabilities.probedUtc) : '-'}</span></div>
        <div className="detail-field">
          <span className="detail-label" title={t('Output speed from the newest probe run. Slow models get longer timeouts by default.')}>{t('Speed')}</span>
          <span>{profile.speed.tokensPerSecond != null ? t('{{rate}} tokens/s', { rate: profile.speed.tokensPerSecond.toFixed(1) }) : '-'}
            {profile.speed.firstOutputMs != null && <span className="text-dim"> {t('(first output {{ms}} ms)', { ms: profile.speed.firstOutputMs })}</span>}
          </span>
        </div>
      </div>

      <h3 title={t('Explicit overrides; blank means no override.')}>{t('Rendering')}</h3>
      <div className="detail-grid">
        <div className="detail-field"><span className="detail-label" title={t(TEMPLATE_TIP)}>{t('Prompt variant')}</span><span>{profile.rendering.template ? t(profile.rendering.template) : <span className="text-dim">{t('From served context')}</span>}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Repeat contract at end')}</span><span>{profile.rendering.repeatContractAtEnd == null ? '-' : profile.rendering.repeatContractAtEnd ? t('Yes') : t('No')}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Maximum files per mission')}</span><span>{profile.rendering.maxFilesPerMission ?? '-'}</span></div>
      </div>

      <h3 title={t('How missions on this model recover from failures. The override is blank when the global setting applies.')}>{t('Recovery policy')}</h3>
      <table className="table compact-table">
        <thead><tr><th>{t('Setting')}</th><th>{t('Override')}</th><th>{t('In effect')}</th></tr></thead>
        <tbody>
          {RECOVERY_FIELDS.map(field => (
            <tr key={field.key}>
              <td title={t(field.tip)}>{t(field.label)}</td>
              <td>{profile.recovery[field.key] ?? <span className="text-dim">-</span>}</td>
              <td>{recovery ? recovery[field.key] : '-'}</td>
            </tr>
          ))}
        </tbody>
      </table>

      <h3 title={t('Timeouts for missions on this model. A slow model gets longer timeouts from its measured speed unless an override is set.')}>{t('Timeouts')}</h3>
      {timeouts && timeouts.speedScale !== 1 && <p className="text-dim">{t('Measured speed scales the global timeouts by {{scale}}.', { scale: timeouts.speedScale.toFixed(2) })}</p>}
      <table className="table compact-table">
        <thead><tr><th>{t('Setting')}</th><th>{t('Override')}</th><th>{t('In effect')}</th><th>{t('Source')}</th></tr></thead>
        <tbody>
          {TIMEOUT_FIELDS.map(field => {
            const sourceKey = field.key === 'stallThresholdMinutes' ? 'stallThresholdSource' : field.key === 'toolWaitHardLimitMinutes' ? 'toolWaitHardLimitSource' : field.key === 'maxRuntimeMinutes' ? 'maxRuntimeSource' : 'firstOutputSource';
            return (
              <tr key={field.key}>
                <td title={t(field.tip)}>{t(field.label)}</td>
                <td>{profile.timeouts[field.key] ?? <span className="text-dim">-</span>}</td>
                <td>{timeouts ? timeouts[field.key] : '-'}</td>
                <td>{timeouts ? sourceLabel(timeouts[sourceKey], t) : '-'}</td>
              </tr>
            );
          })}
        </tbody>
      </table>

      {profile.notes && (<><h3>{t('Notes')}</h3><p style={{ whiteSpace: 'pre-wrap' }}>{profile.notes}</p></>)}

      <h3 title={t('Each runtime version or model digest seen for this profile, newest last.')}>{t('Revisions')}</h3>
      {(profile.revisions ?? []).length === 0 ? <p className="text-dim">{t('No revisions recorded.')}</p> : (
        <table className="table compact-table">
          <thead><tr><th>{t('First seen')}</th><th>{t('Runtime version')}</th><th>{t('Model digest')}</th></tr></thead>
          <tbody>
            {(profile.revisions ?? []).slice().reverse().slice(0, 25).map(rev => (
              <tr key={rev.id}>
                <td>{formatDateTime(rev.firstSeenUtc)}</td>
                <td className="mono">{rev.runtimeVersion || '-'}</td>
                <td className="mono" title={rev.modelDigest || ''}>{rev.modelDigest ? rev.modelDigest.substring(0, 19) : '-'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {form && (
        <div className="modal-overlay" onClick={() => setForm(null)}>
          <form className="modal modal-large" onClick={e => e.stopPropagation()} onSubmit={handleSave} aria-label={t('Edit model profile')}>
            <h3>{t('Edit model profile')}</h3>
            <div className="detail-form-grid">
              <label title={t(TIER_TIP)}>{t('Tier')}
                <select aria-label={t('Tier')} value={form.tier} onChange={e => setForm({ ...form, tier: e.target.value as ModelProfileForm['tier'] })} title={t(TIER_TIP)}>
                  <option value="">{t('From model name')}</option>
                  {TIERS.map(tier => <option key={tier} value={tier}>{t(tier)}{profile.tiering.suggestedTier === tier ? ` (${t('suggested')})` : ''}</option>)}
                </select>
              </label>
              <TriSelect label="Tools" tip={TOOLS_TIP} value={form.toolsAvailable} onChange={v => setForm({ ...form, toolsAvailable: v })} />
              <TriSelect label="MCP tools" tip={MCP_TIP} value={form.mcpAvailable} onChange={v => setForm({ ...form, mcpAvailable: v })} />
              <label title={t(TEMPLATE_TIP)}>{t('Prompt variant')}
                <select aria-label={t('Prompt variant')} value={form.template} onChange={e => setForm({ ...form, template: e.target.value as ModelProfileForm['template'] })} title={t(TEMPLATE_TIP)}>
                  <option value="">{t('From served context')}</option>
                  {PROMPT_VARIANTS.map(v => <option key={v} value={v}>{t(v)}</option>)}
                </select>
              </label>
              <TriSelect label="Repeat contract at end" tip="Repeat the output contract at the end of the prompt. Blank uses the default." value={form.repeatContractAtEnd} onChange={v => setForm({ ...form, repeatContractAtEnd: v })} />
              <label title={t('Most files a mission for this model should touch, 1 or more. Blank means no limit.')}>{t('Maximum files per mission')}
                <input aria-label={t('Maximum files per mission')} type="number" min={1} value={form.maxFilesPerMission} onChange={e => setForm({ ...form, maxFilesPerMission: e.target.value })} placeholder={t('No limit')} />
              </label>
            </div>
            <fieldset>
              <legend>{t('Recovery policy')}</legend>
              <div className="detail-form-grid">
                {RECOVERY_FIELDS.map(field => (
                  <label key={field.key} title={t(field.tip)}>{t(field.label)}
                    <input aria-label={t(field.label)} type="number" min={field.min} max={field.max} step={1} value={form.recovery[field.key]}
                      placeholder={recovery ? String(recovery[field.key]) : t('Global setting')}
                      onChange={e => setForm({ ...form, recovery: { ...form.recovery, [field.key]: e.target.value } })} />
                  </label>
                ))}
              </div>
            </fieldset>
            <fieldset>
              <legend>{t('Timeouts')}</legend>
              <div className="detail-form-grid">
                {TIMEOUT_FIELDS.map(field => (
                  <label key={field.key} title={t(field.tip)}>{t(field.label)}
                    <input aria-label={t(field.label)} type="number" min={field.min} max={field.max} step={1} value={form.timeouts[field.key]}
                      placeholder={timeouts ? String(timeouts[field.key]) : t('Global setting')}
                      onChange={e => setForm({ ...form, timeouts: { ...form.timeouts, [field.key]: e.target.value } })} />
                  </label>
                ))}
              </div>
            </fieldset>
            <label>{t('Notes')}<textarea aria-label={t('Notes')} rows={3} maxLength={4000} value={form.notes} onChange={e => setForm({ ...form, notes: e.target.value })} /></label>
            {formError && <div className="text-danger" role="alert">{formError}</div>}
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary" disabled={saving} title={t('Save the profile. Missions launched after saving use the new values.')}>{saving ? t('Saving...') : t('Save')}</button>
              <button type="button" className="btn" onClick={() => setForm(null)}>{t('Cancel')}</button>
            </div>
          </form>
        </div>
      )}
    </div>
  );
}
