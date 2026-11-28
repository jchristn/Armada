import { useEffect, useRef, useState } from 'react';
import { useLocale } from '../../context/LocaleContext';
import { CAPTAIN_TIERS } from '../../lib/captainForm';
import { personaPriorityTargets, priorityTarget, priorityTargetPatch } from '../../lib/personaPriorities';
import type { PersonaRoutingForm } from '../../lib/personaPriorities';
import { enumerateModelEndpoints } from '../../api/client';
import { listAll } from '../../api/paging';
import type { ModelEndpoint, PersonaCaptainPriority } from '../../types/models';

const RUNTIME_TIP = 'Match only captains with this runtime, for example Codex. Any matches every runtime.';
const TIER_TIP = 'Match only captains at or above this tier (Economy, Standard, Premium). A captain\'s set tier wins over the tier guessed from its model. Any matches every tier.';
const MODEL_TIP = 'Match only captains whose Model is exactly this, ignoring case, for example gpt-5.4-mini. No wildcards. Blank matches any model.';
const ENDPOINT_TIP = 'For Mux, match an endpoint name ignoring case. For API Endpoint, choose a specific configured inference endpoint.';
const AVAILABILITY_TIP = 'Try lower priorities: if no captain in a priority is idle, try the next one. Wait for the first priority: only ever use priority 1 and keep missions Pending until one is free.';
const EXHAUSTION_TIP = 'When no priority has a free captain: Wait keeps the mission Pending and retries later; Use any eligible captain falls back to normal persona and tier routing. Not used when waiting for the first priority.';

interface Props {
  value: PersonaRoutingForm;
  onChange: (value: PersonaRoutingForm) => void;
  /** Which list this editor edits: the captain priorities (default) or the escalation ladder (RS-5.6). */
  list?: 'captainPriorities' | 'escalationLadder';
}

const LADDER_TIP = 'Captain classes, lowest rung first, that a work stage moves up when its captain cannot finish: after its resumes, repeated no-op runs, repeated malformed tool calls or Judge rejections past the rework budget. The work continues on the same branch with the first free captain on a higher rung, up to MaxEscalations hops. Empty means escalation goes only to a captain with a higher tier.';

export default function PersonaPriorityEditor({ value, onChange, list = 'captainPriorities' }: Props) {
  const { t } = useLocale();
  const ladder = list === 'escalationLadder';
  const items = value[list];
  const set = (next: PersonaCaptainPriority[]) => onChange({ ...value, [list]: next });
  const itemLabel = (number: number) => ladder ? t('Rung {{number}}', { number }) : t('Priority {{number}}', { number });
  const [endpoints, setEndpoints] = useState<ModelEndpoint[]>([]);
  const [endpointError, setEndpointError] = useState(false);
  useEffect(() => {
    let active = true;
    Promise.resolve().then(() => listAll(enumerateModelEndpoints)).then(page => { if (active) setEndpoints(page?.objects ?? []); }).catch(() => { if (active) setEndpointError(true); });
    return () => { active = false; };
  }, []);
  const targets = personaPriorityTargets(endpoints);
  const rowTargets = (priority: PersonaCaptainPriority) => {
    const selected = priorityTarget(priority);
    return selected && !targets.some(target => target.value === selected)
      ? [...targets, { value: selected, label: priority.runtime === 'ApiEndpoint' ? `API Endpoint: ${priority.endpoint ?? t('Any')}` : selected }]
      : targets;
  };
  const [dropTarget, setDropTarget] = useState<number | null>(null);
  const [announcement, setAnnouncement] = useState('');
  const dragged = useRef<number | null>(null);
  function update(index: number, patch: Partial<PersonaCaptainPriority>) {
    set(items.map((p, i) => i === index ? { ...p, ...patch } : p));
  }
  function move(index: number, destination: number) {
    if (destination < 0 || destination >= items.length || index === destination) return;
    const priorities = [...items];
    const [priority] = priorities.splice(index, 1);
    priorities.splice(destination, 0, priority);
    set(priorities);
    setAnnouncement(itemLabel(destination + 1));
  }
  return <fieldset className="persona-priority-editor form-fieldset">
    {ladder
      ? <><legend title={t(LADDER_TIP)}>{t('Escalation ladder')}</legend>
        <p className="text-dim">{t('Rungs run from the weakest captain at the top to the strongest at the bottom. An empty ladder escalates only to a higher tier.')}</p></>
      : <><legend title={t('Ordered list of captain classes this persona\'s missions go to. Each entry matches captains on all the fields it sets. Leave it empty to use the default captain and normal routing.')}>{t('Captain priorities')}</legend>
        <p className="text-dim">{t('Priorities run from top to bottom. Empty lists use the default captain routing.')}</p></>}
    <p className="text-dim persona-priority-help">{t('Drag to reorder, or use the arrows or position menu.')}</p>
    {endpointError && <p role="alert" className="text-danger">{t('Failed to load endpoints.')}</p>}
    <div className="persona-priority-list">
      {items.map((priority, index) => <fieldset
        className={`persona-priority-class${dropTarget === index ? ' persona-priority-drop-target' : ''}`} key={index}
        onDragOver={e => { if (dragged.current !== null) { e.preventDefault(); e.dataTransfer.dropEffect = 'move'; setDropTarget(index); } }}
        onDrop={e => { e.preventDefault(); if (dragged.current !== null) move(dragged.current, index); dragged.current = null; setDropTarget(null); }}>
        <legend title={t(ladder ? 'Captains matching every field set here form this rung. A failed captain moves to the next rung below it.' : 'Captains matching every field set here are tried in this order. Lower numbers are tried first.')}>{itemLabel(index + 1)}</legend>
        <div className="persona-priority-toolbar">
          <button type="button" className="btn btn-sm persona-priority-drag" draggable
            aria-label={t('Drag to reorder')} title={t('Drag onto another priority to move this one there. With the handle focused, Alt+Up and Alt+Down move it too.')}
            onDragStart={e => { dragged.current = index; e.dataTransfer.effectAllowed = 'move'; e.dataTransfer.setData('text/plain', String(index)); }}
            onDragEnd={() => { dragged.current = null; setDropTarget(null); }}
            onKeyDown={e => { if (e.altKey && (e.key === 'ArrowUp' || e.key === 'ArrowDown')) { e.preventDefault(); move(index, index + (e.key === 'ArrowUp' ? -1 : 1)); } }}>
            <span aria-hidden="true">⠿</span>
          </button>
          <label className="persona-priority-position" title={t('Move this priority to the chosen position. 1 is tried first.')}>{t('Position')}<select aria-label={t('Position')} title={t('Move this priority to the chosen position. 1 is tried first.')} value={index} onChange={e => move(index, Number(e.target.value))}>
            {items.map((_, position) => <option key={position} value={position}>{position + 1}</option>)}
          </select></label>
          <div className="persona-priority-actions">
            <button type="button" className="btn btn-sm" disabled={index === 0} aria-label={t('Move up')} title={t('Move this priority up one place so it is tried earlier.')} onClick={() => move(index, index - 1)}><span aria-hidden="true">↑</span></button>
            <button type="button" className="btn btn-sm" disabled={index === items.length - 1} aria-label={t('Move down')} title={t('Move this priority down one place so it is tried later.')} onClick={() => move(index, index + 1)}><span aria-hidden="true">↓</span></button>
            <button type="button" className="btn btn-sm" aria-label={t(ladder ? 'Remove rung' : 'Remove priority')} title={t('Remove this priority from the list. Nothing changes until you save the persona.')} onClick={() => set(items.filter((_, i) => i !== index))}><span aria-hidden="true">×</span></button>
          </div>
        </div>
        <div className="persona-priority-fields">
          <label title={t(RUNTIME_TIP)}>{t('Runtime')}<select aria-label={t('Runtime')} title={t(RUNTIME_TIP)} value={priorityTarget(priority)} onChange={e => update(index, priorityTargetPatch(e.target.value, priority))}>
            <option value="">{t('Any')}</option>{rowTargets(priority).map(r => <option key={r.value} value={r.value}>{r.label}</option>)}
          </select></label>
          <label title={t(TIER_TIP)}>{t('Minimum tier')}<select aria-label={t('Minimum tier')} title={t(TIER_TIP)} value={priority.minimumTier ?? ''} onChange={e => update(index, { minimumTier: e.target.value || null })}>
            <option value="">{t('Any')}</option>{CAPTAIN_TIERS.map(tier => <option key={tier} value={tier}>{t(tier)}</option>)}
          </select></label>
          <label title={t(MODEL_TIP)}>{t('Model')}<input maxLength={1024} title={t(MODEL_TIP)} placeholder={t('Any')} value={priority.model ?? ''} onChange={e => update(index, { model: e.target.value || null })} /></label>
          <label title={t(ENDPOINT_TIP)}>{t('Endpoint')}{priority.runtime === 'ApiEndpoint'
            ? <select aria-label={t('Endpoint')} value={priority.endpoint ?? ''} onChange={e => update(index, { endpoint: e.target.value || null })}>
              <option value="">{t('Any')}</option>
              {endpoints.filter(e => e.kind === 'Inference').map(e => <option key={e.id} value={e.id}>{e.name} ({e.provider}{e.model ? ` / ${e.model}` : ''})</option>)}
              {priority.endpoint && !endpoints.some(e => e.kind === 'Inference' && e.id === priority.endpoint) && <option value={priority.endpoint}>{priority.endpoint}</option>}
            </select>
            : <input maxLength={1024} title={t(ENDPOINT_TIP)} disabled={priority.runtime !== 'Mux'} placeholder={priority.runtime === 'Mux' ? t('Any') : t('Mux only')} value={priority.endpoint ?? ''} onChange={e => update(index, { endpoint: e.target.value || null })} />}</label>
        </div>
      </fieldset>)}
    </div>
    <div className="persona-priority-add">
      {/* Choosing a runtime adds the priority at once: a separate pick-then-click step let a choice that was never added be
          saved as an empty list. */}
      <label title={t('Choose a runtime to add it as a priority at the bottom of the list, so it is tried last. You can change it and add more fields after adding.')}>{t(ladder ? 'Add rung' : 'Add priority')}<select aria-label={t(ladder ? 'Add rung' : 'Add priority')} title={t('Choose a runtime to add it as a priority at the bottom of the list, so it is tried last. You can change it and add more fields after adding.')} value="" onChange={e => {
        if (!e.target.value) return;
        set([...items, priorityTargetPatch(e.target.value)]);
        setAnnouncement(itemLabel(items.length + 1));
      }}>
        <option value="">{t('Choose a runtime to add')}</option>
        {targets.map(r => <option key={r.value} value={r.value}>{r.label}</option>)}
      </select></label>
    </div>
    {!ladder && <div className="persona-priority-policies">
      <label title={t(AVAILABILITY_TIP)}>{t('When a priority is unavailable')}<select title={t(AVAILABILITY_TIP)} value={value.priorityAvailability} onChange={e => onChange({ ...value, priorityAvailability: e.target.value as PersonaRoutingForm['priorityAvailability'] })}>
        <option value="FallThrough">{t('Try lower priorities')}</option><option value="WaitForFirstPriority">{t('Wait for the first priority')}</option>
      </select></label>
      <label title={t(EXHAUSTION_TIP)}>{t('When all priorities are unavailable')}<select title={t(EXHAUSTION_TIP)} disabled={value.priorityAvailability === 'WaitForFirstPriority'} value={value.priorityExhaustion} onChange={e => onChange({ ...value, priorityExhaustion: e.target.value as PersonaRoutingForm['priorityExhaustion'] })}>
        <option value="Wait">{t('Wait')}</option><option value="UseEligibleCaptain">{t('Use any eligible captain')}</option>
      </select></label>
    </div>}
    <span className="sr-only" aria-live="polite">{announcement}</span>
  </fieldset>;
}
