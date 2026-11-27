import { describe, expect, it } from 'vitest';
import catalogSource from '../../../Armada.Server/wwwroot/i18n/armada.json?raw';
import listSource from '../pages/ModelProfiles.tsx?raw';
import detailSource from '../pages/ModelProfileDetail.tsx?raw';
import judgeSource from '../components/pipelines/JudgeStrengthField.tsx?raw';
import libSource from './modelProfiles.ts?raw';
import type { I18nCatalog } from '../i18n/runtime';
import type { ModelProfile } from '../types/models';
import { capabilityLabel, formToUpdate, profileToForm, servedContextLabel, validateProfileForm } from './modelProfiles';

const t = (key: string, vars?: Record<string, string | number>) => key.replace(/\{\{(\w+)\}\}/g, (_, name: string) => String(vars?.[name] ?? name));

function profile(overrides: Partial<ModelProfile> = {}): ModelProfile {
  return {
    id: 'mdl_1', tenantId: null,
    identity: { runtime: 'ApiEndpoint', provider: 'Ollama', model: 'qwen3.8', servedContextTokens: 32768, servedContextSource: 'Discovered' },
    capabilities: { toolsAvailable: null, mcpAvailable: false, probedToolsAvailable: true, probedMcpAvailable: null, probedUtc: null },
    rendering: { template: 'Compact', repeatContractAtEnd: true, maxFilesPerMission: null },
    recovery: { maxResumes: 4, unclassifiedResumes: null, transientGraceMinutes: null, judgeReworkRounds: null, outputRepairAttempts: 2, escalateAfterResumes: 1 },
    tiering: { tier: 'Standard', suggestedTier: 'Premium', suggestedReason: 'score', suggestedSamples: 24, suggestedUtc: null },
    timeouts: { stallThresholdMinutes: 20, toolWaitHardLimitMinutes: null, maxRuntimeMinutes: null, firstOutputMinutes: null },
    speed: { tokensPerSecond: 12.5, firstOutputMs: 900, measuredUtc: null },
    notes: null, createdUtc: '2026-11-01T00:00:00Z', lastUpdateUtc: '2026-11-02T00:00:00Z',
    ...overrides,
  };
}

describe('model profile form', () => {
  it('round-trips a profile into the PUT body, sending every section whole', () => {
    const body = formToUpdate(profileToForm(profile()));
    expect(body.tiering).toEqual({ tier: 'Standard' });
    expect(body.capabilities).toEqual({ toolsAvailable: null, mcpAvailable: false });
    expect(body.rendering).toEqual({ template: 'Compact', repeatContractAtEnd: true, maxFilesPerMission: null });
    expect(body.recovery).toEqual({ maxResumes: 4, unclassifiedResumes: null, transientGraceMinutes: null, judgeReworkRounds: null, outputRepairAttempts: 2, escalateAfterResumes: 1 });
    expect(body.timeouts).toEqual({ stallThresholdMinutes: 20, toolWaitHardLimitMinutes: null, maxRuntimeMinutes: null, firstOutputMinutes: null });
    expect(body.notes).toBe('');
  });

  it('clears an override and the tier when the field is blank', () => {
    const form = profileToForm(profile());
    form.tier = '';
    form.recovery.maxResumes = '';
    const body = formToUpdate(form);
    expect(body.tiering).toEqual({ tier: null });
    expect(body.recovery?.maxResumes).toBeNull();
  });

  it('rejects values outside the server bounds and resumes rules', () => {
    const form = profileToForm(profile());
    expect(validateProfileForm(form, t)).toBeNull();
    form.recovery.judgeReworkRounds = '0';
    expect(validateProfileForm(form, t)).toBe('Judge rework rounds must be a whole number from 1 to 5.');
    form.recovery.judgeReworkRounds = '';
    form.recovery.escalateAfterResumes = '6';
    expect(validateProfileForm(form, t)).toBe('Unclassified resumes and Escalate after resumes cannot be more than Resumes.');
    form.recovery.escalateAfterResumes = '';
    form.timeouts.maxRuntimeMinutes = '3000';
    expect(validateProfileForm(form, t)).toBe('Maximum runtime (minutes) must be a whole number from 1 to 2880.');
  });

  it('labels served context and capabilities with their source', () => {
    expect(servedContextLabel(profile().identity, t)).toBe(`${(32768).toLocaleString()} tokens (Discovered)`);
    expect(servedContextLabel({ runtime: 'ClaudeCode', provider: null, model: null, servedContextTokens: null }, t)).toBe('-');
    expect(capabilityLabel(null, true, t)).toBe('Yes (Probed)');
    expect(capabilityLabel(false, true, t)).toBe('No (Set)');
    expect(capabilityLabel(null, null, t)).toBe('Unknown');
  });
});

const S = "'((?:[^'\\\\]|\\\\.)*)'";
const PATTERNS = [new RegExp(`\\bt\\(\\s*${S}`, 'g'), new RegExp(`(?:label|tip): ${S}`, 'g'), new RegExp(`const [A-Z_]+_TIP = ${S}`, 'g')];
// Enum values shown as-is in every locale (API values), like the tier names elsewhere in the dashboard.
const UNTRANSLATED = new Set(['Economy', 'Standard', 'Premium', 'Current', 'Lean', 'Compact', 'Off']);

function placeholders(text: string): string {
  return (text.match(/\{\{[\w.]+\}\}/g) ?? []).sort().join(',');
}

describe('Model profile and Judge strength i18n catalog', () => {
  const catalog = JSON.parse(catalogSource) as I18nCatalog;
  const keys = new Set<string>();
  for (const source of [listSource, detailSource, judgeSource, libSource]) {
    for (const pattern of PATTERNS) {
      for (const m of source.matchAll(pattern)) {
        const key = m[1].replace(/\\'/g, "'");
        if (key.trim() && !UNTRANSLATED.has(key)) keys.add(key);
      }
    }
  }

  it('finds the strings', () => {
    expect(keys.has('Recovery policy')).toBe(true);
    expect(keys.has('Judge strength rule')).toBe(true);
  });

  it('has a translation for every string in every non-English locale, with placeholders intact', () => {
    const problems: string[] = [];
    for (const meta of catalog.supportedLocales) {
      if (meta.code === catalog.defaultLocale) continue;
      const pack = catalog.locales[meta.code] ?? {};
      for (const key of keys) {
        const value = pack.phrases?.[key] ?? pack.terms?.[key];
        if (!value) problems.push(`${meta.code}: missing ${key}`);
        else if (placeholders(key) !== placeholders(value)) problems.push(`${meta.code}: placeholder ${key}`);
      }
    }
    expect(problems).toEqual([]);
  });
});
