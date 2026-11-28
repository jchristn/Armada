import type { ModelEndpoint, Persona, PersonaCaptainPriority, PersonaPriorityAvailability, PersonaPriorityExhaustion } from '../types/models';
import { CAPTAIN_RUNTIMES, CAPTAIN_TIERS } from './captainForm';

export const PERSONA_PRIORITY_RUNTIMES = [...CAPTAIN_RUNTIMES, { value: 'Custom', label: 'Custom' }];

export interface PersonaRoutingForm {
  captainPriorities: PersonaCaptainPriority[];
  /** Escalation ladder, lowest rung first (RS-5.6). */
  escalationLadder: PersonaCaptainPriority[];
  priorityAvailability: PersonaPriorityAvailability;
  priorityExhaustion: PersonaPriorityExhaustion;
}

export function personaRoutingForm(persona?: Partial<Persona>): PersonaRoutingForm {
  return {
    captainPriorities: (persona?.captainPriorities ?? []).map(p => ({ ...p })),
    escalationLadder: (persona?.escalationLadder ?? []).map(p => ({ ...p })),
    priorityAvailability: persona?.priorityAvailability ?? 'FallThrough',
    priorityExhaustion: persona?.priorityExhaustion ?? 'Wait',
  };
}

/** Validate before submission; returns a localization key, or null. */
export function validatePersonaPriorities(priorities: PersonaCaptainPriority[]): string | null {
  for (const p of priorities) {
    if (!p || (!p.runtime && !p.model?.trim() && !p.endpoint?.trim() && !p.minimumTier)) return 'Each priority needs at least one matching field.';
    if (p.runtime && !PERSONA_PRIORITY_RUNTIMES.some(r => r.value === p.runtime)) return 'Invalid captain runtime.';
    if (p.minimumTier && !CAPTAIN_TIERS.includes(p.minimumTier)) return 'Invalid captain tier.';
    if (p.endpoint && p.runtime !== 'Mux' && p.runtime !== 'ApiEndpoint') return 'An endpoint requires the Mux or API Endpoint runtime.';
    if ((p.model != null && !p.model.trim()) || (p.endpoint != null && !p.endpoint.trim())) return 'Matching fields must not be blank.';
  }
  return null;
}

export function prioritySummary(priority: PersonaCaptainPriority): string {
  return [priority.runtime, priority.model, priority.endpoint, priority.minimumTier].filter(Boolean).join(' / ');
}

/** Stable UI selection tokens; endpoint IDs, never names or providers, identify API priorities. */
export function priorityTarget(priority: PersonaCaptainPriority): string {
  return priority.runtime === 'ApiEndpoint' && priority.endpoint ? `endpoint:${priority.endpoint}` : priority.runtime ?? '';
}

export function priorityTargetPatch(target: string, previous?: PersonaCaptainPriority): Partial<PersonaCaptainPriority> {
  return target.startsWith('endpoint:')
    ? { runtime: 'ApiEndpoint', endpoint: target.slice('endpoint:'.length) }
    : { runtime: target || null, ...(previous ? { endpoint: target === 'Mux' && previous.runtime === 'Mux' ? previous.endpoint : null } : {}) };
}

export function personaPriorityTargets(endpoints: ModelEndpoint[]): { value: string; label: string }[] {
  return [
    ...PERSONA_PRIORITY_RUNTIMES.filter(r => r.value !== 'ApiEndpoint'),
    ...endpoints.filter(e => e.kind === 'Inference').map(e => ({ value: `endpoint:${e.id}`, label: `API Endpoint: ${e.name} (${e.provider}${e.model ? ` / ${e.model}` : ''})` })),
  ];
}
