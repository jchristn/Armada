/*
 * The resource layer: one description per kind of Armada record (persona, pipeline, release, deployment, harbor, ...)
 * drives a native tree, a detail page and the create and edit forms, so every configuration, delivery, activity and
 * system area works in VS Code the same way. Detail pages and edit forms are built from the record's own fields, so a
 * field the server adds shows up without a change here. Pure, for tests.
 */

import type { FormField } from '../ops/formPanel';

/** A record as the API returns it (camelCase keys). */
export type ResourceRecord = Record<string, unknown>;

/** An action on one record, beyond view, edit and delete. */
export interface ResourceAction {
  label: string;
  /** Shown only when this returns true. */
  when?(record: ResourceRecord): boolean;
  /** A confirmation question; the action runs only after the user agrees. */
  confirm?(record: ResourceRecord): string;
  run(record: ResourceRecord): Promise<unknown>;
}

/** A field of the create form, with how its text is converted. */
export interface CreateField extends FormField {
  kind?: 'string' | 'number' | 'boolean' | 'json' | 'lines';
}

/** One kind of record. */
export interface ResourceSpec {
  key: string;
  /** Singular, for titles ("Persona"). */
  label: string;
  /** Plural, for the tree ("Personas"). */
  plural: string;
  /** Codicon for the kind and its records. */
  icon: string;
  /** The view it appears in. */
  view: ResourceViewKey;
  /** Not listed in a resource view (opened from another view, such as vessels and captains). */
  hidden?: boolean;
  list(): Promise<ResourceRecord[]>;
  get?(id: string): Promise<ResourceRecord>;
  create?(data: ResourceRecord): Promise<unknown>;
  update?(id: string, data: ResourceRecord): Promise<unknown>;
  remove?(id: string): Promise<unknown>;
  /** The key the API addresses a record by (default "id"; personas and prompt templates use "name"). */
  idKey?: string;
  /** Fields of the create form (create is offered only with these). */
  createFields?: CreateField[];
  /** Fields never offered in the edit form beyond the common read-only ones. */
  readOnly?: string[];
  /** Extra one-line text for the tree item. */
  describe?(record: ResourceRecord): string;
  /** Record actions. */
  actions?: ResourceAction[];
  /** The dashboard route of a record, for "Open in Browser". */
  route?(record: ResourceRecord): string;
}

/** The sidebar views resources appear in. */
export type ResourceViewKey = 'backlog' | 'fleetActions' | 'delivery' | 'configuration' | 'activity' | 'system';

/** View ids per key (package.json contributes.views.armada). */
export const RESOURCE_VIEW_IDS: Record<ResourceViewKey, string> = {
  backlog: 'armadaBacklog',
  fleetActions: 'armadaFleetActions',
  delivery: 'armadaDelivery',
  configuration: 'armadaConfiguration',
  activity: 'armadaActivity',
  system: 'armadaSystem',
};

/** Fields no edit form offers (identity, ownership and timestamps). */
const COMMON_READ_ONLY = /^(id|tenantId|userId|ownerUserId|createdUtc|lastUpdateUtc|updatedUtc|createdByUserId|.*Count|.*Utc)$/;

/** The records of an API result: an array, or an enumeration's objects. */
export function rowsOf(result: unknown): ResourceRecord[] {
  if (Array.isArray(result)) return result as ResourceRecord[];
  if (result && typeof result === 'object') {
    const objects = (result as { objects?: unknown }).objects;
    if (Array.isArray(objects)) return objects as ResourceRecord[];
  }
  return [];
}

/** The id a record is addressed by. */
export function recordId(spec: ResourceSpec, record: ResourceRecord): string {
  const value = record[spec.idKey ?? 'id'] ?? record.id ?? record.name;
  return value === undefined || value === null ? '' : String(value);
}

/** The record's display title. */
export function recordTitle(record: ResourceRecord): string {
  for (const key of ['name', 'title', 'label', 'displayName', 'key', 'email', 'toolName', 'environmentName', 'message', 'eventType', 'type', 'id']) {
    const value = record[key];
    if (typeof value === 'string' && value.trim()) return value.trim().split('\n')[0].slice(0, 120);
  }
  return '(untitled)';
}

/** The record's status, if it has one. */
export function recordStatus(record: ResourceRecord): string {
  for (const key of ['status', 'state', 'severity', 'outcome']) {
    const value = record[key];
    if (typeof value === 'string' && value) return value;
  }
  if (record.active === false || record.enabled === false) return 'Disabled';
  return '';
}

/** "Last Update Utc" for "lastUpdateUtc". */
export function fieldLabel(key: string): string {
  const spaced = key.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/_/g, ' ');
  return spaced.charAt(0).toUpperCase() + spaced.slice(1).replace(/\bUtc\b/, '(UTC)').replace(/\bId\b/, 'ID').replace(/\bUrl\b/, 'URL');
}

/** True for a string shown and edited as a block of text. */
export function isLongText(key: string, value: string): boolean {
  return value.length > 100 || value.includes('\n') || /(instructions|content|prompt|description|body|template|script|command|text|context|notes|summary|markdown|yaml|json)$/i.test(key);
}

/** How an existing field is edited. */
export type FieldKind = 'string' | 'text' | 'number' | 'boolean' | 'json';

/** The kind of a field from its current value. */
export function fieldKind(key: string, value: unknown): FieldKind | undefined {
  if (typeof value === 'boolean') return 'boolean';
  if (typeof value === 'number') return 'number';
  if (typeof value === 'string') return isLongText(key, value) ? 'text' : 'string';
  if (value === null) return /(instructions|content|prompt|description|body|template|script|command|text|notes)$/i.test(key) ? 'text' : 'string';
  if (typeof value === 'object') return 'json';
  return undefined;
}

/** The fields of the edit form for a record. */
export function editFields(spec: ResourceSpec, record: ResourceRecord): Array<FormField & { fieldKind: FieldKind }> {
  const fields: Array<FormField & { fieldKind: FieldKind }> = [];
  for (const [key, value] of Object.entries(record)) {
    if (COMMON_READ_ONLY.test(key) || (spec.readOnly ?? []).includes(key) || key === (spec.idKey ?? 'id')) continue;
    const kind = fieldKind(key, value);
    if (!kind) continue;
    const base = { key, label: fieldLabel(key), fieldKind: kind };
    if (kind === 'boolean') fields.push({ ...base, type: 'select', value: String(value), options: [{ value: 'true', label: 'Yes' }, { value: 'false', label: 'No' }] });
    else if (kind === 'number') fields.push({ ...base, type: 'number', value: String(value) });
    else if (kind === 'json') fields.push({ ...base, type: 'textarea', value: JSON.stringify(value, null, 2), hint: 'JSON' });
    else if (kind === 'text') fields.push({ ...base, type: 'textarea', value: value === null ? '' : String(value) });
    else fields.push({ ...base, type: 'text', value: value === null ? '' : String(value) });
  }
  return fields;
}

/**
 * Converts edited form text back to field values, merged over the original record.
 * @throws Error naming the field when a number or JSON field does not parse.
 */
export function applyEdits(record: ResourceRecord, fields: Array<{ key: string; fieldKind: FieldKind }>, values: Record<string, string>): ResourceRecord {
  const result: ResourceRecord = { ...record };
  for (const field of fields) {
    const text = values[field.key];
    if (text === undefined) continue;
    result[field.key] = convert(field.key, field.fieldKind, text, record[field.key]);
  }
  return result;
}

/**
 * Converts create form text to values.
 * @throws Error naming the field when a number or JSON field does not parse.
 */
export function createValues(fields: CreateField[], values: Record<string, string>): ResourceRecord {
  const result: ResourceRecord = {};
  for (const field of fields) {
    const text = values[field.key] ?? '';
    if (!text.trim() && !field.required) continue;
    const kind = field.kind ?? 'string';
    if (kind === 'lines') result[field.key] = text.split(/\r?\n/).map((l) => l.trim()).filter(Boolean);
    else result[field.key] = convert(field.key, kind === 'string' ? 'string' : kind, text, undefined);
  }
  return result;
}

function convert(key: string, kind: FieldKind, text: string, original: unknown): unknown {
  switch (kind) {
    case 'boolean':
      return text === 'true';
    case 'number': {
      if (!text.trim()) return original === null ? null : 0;
      const value = Number(text);
      if (!Number.isFinite(value)) throw new Error(`${fieldLabel(key)} must be a number.`);
      return value;
    }
    case 'json':
      if (!text.trim()) return null;
      try {
        return JSON.parse(text);
      } catch {
        throw new Error(`${fieldLabel(key)} is not valid JSON.`);
      }
    default:
      return text === '' && original === null ? null : text;
  }
}
