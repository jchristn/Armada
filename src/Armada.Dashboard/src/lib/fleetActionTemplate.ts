import type { Vessel } from '../types/models';

const VARIABLE_PATTERN = /\{\{\s*([^{}]*?)\s*\}\}/g;

const KNOWN = ['vessel.name', 'vessel.id', 'vessel.defaultbranch', 'vessel.workingdirectory', 'vessel.buildcommand', 'vessel.ecosystems', 'vessel.testcommand', 'vessel.lintcommand', 'vessel.auditcommand', 'vessel.outdatedcommand', 'vessel.licensescommand', 'health.summary', 'scan.summary'];

/** Return every `{{name}}` in the text that the server would reject (case-insensitive, de-duplicated). */
export function findUnknownTemplateVariables(text: string | null | undefined, inputNames: string[] = []): string[] {
  if (!text) return [];
  const unknown: string[] = [];
  for (const match of text.matchAll(VARIABLE_PATTERN)) {
    const name = match[1].trim();
    if (!KNOWN.includes(name.toLowerCase()) && !inputNames.some(n => `input.${n}`.toLowerCase() === name.toLowerCase()) && !unknown.includes(name)) unknown.push(name);
  }
  return unknown;
}

export interface TemplatePreview {
  text: string;
  /** True when the template references `{{health.summary}}`, which only the server can render. */
  usesHealthSummary: boolean;
  /**
   * Always false: the server now falls back to the repo build script or ecosystem default for `{{vessel.buildCommand}}`,
   * so a preview cannot tell whether a vessel will be skipped. Kept for existing callers.
   */
  missingBuildCommand: boolean;
}

/**
 * Client-side approximation of the server's single-pass template renderer, used only for previews.
 * `{{health.summary}}` is replaced with `healthPlaceholder` because it is rendered on the server.
 */
export function renderTemplatePreview(text: string, vessel: Pick<Vessel, 'name' | 'id' | 'defaultBranch' | 'workingDirectory'> & { definitionOfDoneBuildCommand?: string | null; definitionOfDoneTestCommand?: string | null; testCommand?: string | null; lintCommand?: string | null; auditCommand?: string | null }, healthPlaceholder: string): TemplatePreview {
  let usesHealthSummary = false;
  const missingBuildCommand = false;
  const rendered = text.replace(VARIABLE_PATTERN, (whole, rawName: string) => {
    const name = rawName.trim().toLowerCase();
    switch (name) {
      case 'vessel.name': return vessel.name ?? '';
      case 'vessel.id': return vessel.id ?? '';
      case 'vessel.defaultbranch': return vessel.defaultBranch || 'main';
      case 'vessel.workingdirectory': return vessel.workingDirectory ?? '';
      // Without a definition-of-done build command the server falls back to the repo build script or the ecosystem
      // default, which depend on the working tree, so the variable is left as written for the server to render.
      case 'vessel.buildcommand': return vessel.definitionOfDoneBuildCommand || whole;
      // Ecosystems and resolved commands depend on the working tree, so only explicit vessel overrides preview;
      // otherwise the variable is left as written for the server to render.
      case 'vessel.testcommand': return vessel.testCommand || vessel.definitionOfDoneTestCommand || whole;
      case 'vessel.lintcommand': return vessel.lintCommand || whole;
      case 'vessel.auditcommand': return vessel.auditCommand || whole;
      case 'health.summary':
        usesHealthSummary = true;
        return healthPlaceholder;
      default:
        return whole;
    }
  });
  return { text: rendered, usesHealthSummary, missingBuildCommand };
}
