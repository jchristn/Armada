import type { Playbook, Skill } from '@dashboard/types/models';
import { resolveCreateScope, type ScopeViewer } from '@dashboard/lib/scoping';
import { bool, str, type FormField, type FormValues } from '../../components/resource/FormSheet';
import type { Translate } from '../../i18n/LocaleContext';
import { scopeField, scopeValue } from './common';

// ------------------------------------------------------------------ Playbooks

export const NEW_PLAYBOOK_CONTENT = '# Playbook\n\nDescribe the rules the model must follow.\n';

export function playbookValues(viewer: ScopeViewer, p: Playbook | null): FormValues {
  if (!p) return { fileName: 'NEW_PLAYBOOK.md', description: '', content: NEW_PLAYBOOK_CONTENT, active: true, scope: resolveCreateScope(viewer) };
  return { fileName: p.fileName, description: p.description || '', content: p.content, active: p.active, scope: p.scope };
}

export function playbookPayload(viewer: ScopeViewer, v: FormValues, existing: Playbook | null): Partial<Playbook> {
  return {
    fileName: str(v, 'fileName'),
    description: str(v, 'description').trim() || null,
    content: str(v, 'content'),
    active: bool(v, 'active'),
    scope: scopeValue(viewer, v, existing?.scope ?? null),
  };
}

export function playbookFields(t: Translate, viewer: ScopeViewer): FormField[] {
  return [
    { kind: 'text', key: 'fileName', label: t('File Name'), required: true, placeholder: t('CSHARP_BACKEND_ARCHITECTURE.md') },
    { kind: 'text', key: 'description', label: t('Description'), placeholder: t('Optional summary shown during playbook selection') },
    { kind: 'multiline', key: 'content', label: t('Markdown Content') },
    { kind: 'switch', key: 'active', label: t('Active and selectable during dispatch') },
    scopeField(t, viewer),
  ];
}

/** Characters, lines, and markdown headings of a playbook (the detail page's stats). */
export function playbookStats(content: string): { characters: number; lines: number; headings: number } {
  return {
    characters: content.length,
    lines: content.length === 0 ? 0 : content.split(/\r?\n/).length,
    headings: (content.match(/^#+\s/gm) || []).length,
  };
}

// ------------------------------------------------------------------ Skills

export function skillValues(viewer: ScopeViewer, s: Skill | null): FormValues {
  if (!s) return { name: 'Untitled Skill', category: '', description: '', content: '', active: true, scope: resolveCreateScope(viewer) };
  return { name: s.name, category: s.category || '', description: s.description || '', content: s.content, active: s.active, scope: s.scope };
}

export function skillPayload(viewer: ScopeViewer, v: FormValues, existing: Skill | null): Partial<Skill> {
  return {
    name: str(v, 'name'),
    description: str(v, 'description') || null,
    category: str(v, 'category') || null,
    content: str(v, 'content'),
    active: bool(v, 'active'),
    scope: scopeValue(viewer, v, existing?.scope ?? null),
  };
}

export function skillFields(t: Translate, viewer: ScopeViewer): FormField[] {
  return [
    { kind: 'text', key: 'name', label: t('Name'), required: true },
    { kind: 'text', key: 'category', label: t('Category'), placeholder: 'engineering' },
    { kind: 'text', key: 'description', label: t('Description') },
    { kind: 'multiline', key: 'content', label: t('Content'), placeholder: t('Markdown or plain text injected into mission prompts for projects that attach this skill.') },
    { kind: 'switch', key: 'active', label: t('Active') },
    scopeField(t, viewer),
  ];
}
