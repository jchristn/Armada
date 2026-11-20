/*
 * A form in an editor tab, drawn with the VS Code theme tokens (modeled on Mux's FormPanel): text, number, select and
 * textarea fields, Submit and Cancel. Resolves with the values, or undefined when cancelled or closed. The host may
 * reject a submit with a message (validate), which the form shows without closing.
 */

import * as crypto from 'crypto';
import * as vscode from 'vscode';
import { escapeHtml } from './detailHtml';

/** One form field. */
export interface FormField {
  key: string;
  label: string;
  type: 'text' | 'number' | 'select' | 'textarea';
  value?: string;
  options?: Array<{ value: string; label: string }>;
  hint?: string;
  required?: boolean;
  placeholder?: string;
}

/** A form. */
export interface FormSpec {
  title: string;
  intro?: string;
  submitLabel: string;
  fields: FormField[];
}

/** Renders the form page. Pure, for tests. */
export function renderFormHtml(spec: FormSpec, nonce: string): string {
  const controls = spec.fields.map((f) => {
    const id = `f_${escapeHtml(f.key)}`;
    const required = f.required ? ' required aria-required="true"' : '';
    const placeholder = f.placeholder ? ` placeholder="${escapeHtml(f.placeholder)}"` : '';
    let control: string;
    if (f.type === 'select') {
      control = `<select id="${id}" name="${escapeHtml(f.key)}"${required}>${(f.options ?? []).map((o) => `<option value="${escapeHtml(o.value)}"${o.value === f.value ? ' selected' : ''}>${escapeHtml(o.label)}</option>`).join('')}</select>`;
    } else if (f.type === 'textarea') {
      control = `<textarea id="${id}" name="${escapeHtml(f.key)}"${required}${placeholder}>${escapeHtml(f.value ?? '')}</textarea>`;
    } else {
      control = `<input id="${id}" name="${escapeHtml(f.key)}" type="${f.type}" value="${escapeHtml(f.value ?? '')}"${required}${placeholder}>`;
    }
    return `<div class="field"><label for="${id}">${escapeHtml(f.label)}${f.required ? ' <span class="req">*</span>' : ''}</label>${control}${f.hint ? `<div class="hint">${escapeHtml(f.hint)}</div>` : ''}</div>`;
  }).join('\n');
  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}'">
<style>
body { font-family: var(--vscode-font-family); font-size: var(--vscode-font-size); color: var(--vscode-foreground); padding: 16px 20px; max-width: 760px; }
h1 { font-size: 1.35em; font-weight: 600; margin: 0 0 6px; }
.intro, .hint { color: var(--vscode-descriptionForeground); }
.intro { margin-bottom: 18px; }
.field { margin-bottom: 14px; }
label { display: block; margin-bottom: 4px; font-weight: 600; }
.req { color: var(--vscode-errorForeground); }
.hint { font-size: 0.9em; margin-top: 3px; }
input, select, textarea { width: 100%; box-sizing: border-box; padding: 5px 6px; color: var(--vscode-input-foreground); background: var(--vscode-input-background); border: 1px solid var(--vscode-input-border, var(--vscode-panel-border)); border-radius: 2px; font-family: var(--vscode-font-family); font-size: var(--vscode-font-size); }
select { color: var(--vscode-dropdown-foreground); background: var(--vscode-dropdown-background); border-color: var(--vscode-dropdown-border, var(--vscode-panel-border)); }
textarea { min-height: 160px; resize: vertical; font-family: var(--vscode-editor-font-family); }
input:focus, select:focus, textarea:focus { outline: 1px solid var(--vscode-focusBorder); outline-offset: -1px; }
.actions { margin-top: 20px; display: flex; gap: 8px; }
button { font-family: inherit; font-size: inherit; color: var(--vscode-button-foreground); background: var(--vscode-button-background); border: 1px solid var(--vscode-button-border, transparent); border-radius: 2px; padding: 5px 14px; cursor: pointer; }
button:hover { background: var(--vscode-button-hoverBackground); }
button.secondary { color: var(--vscode-button-secondaryForeground); background: var(--vscode-button-secondaryBackground); }
button:focus-visible { outline: 1px solid var(--vscode-focusBorder); outline-offset: 2px; }
button:disabled { opacity: 0.6; cursor: default; }
.error { color: var(--vscode-errorForeground); margin-top: 10px; min-height: 18px; }
</style>
</head>
<body>
<h1>${escapeHtml(spec.title)}</h1>
${spec.intro ? `<div class="intro">${escapeHtml(spec.intro)}</div>` : ''}
<form id="form" novalidate>
${controls}
<div class="error" id="error" role="alert"></div>
<div class="actions"><button type="submit" id="submit">${escapeHtml(spec.submitLabel)}</button><button type="button" class="secondary" id="cancel">Cancel</button></div>
</form>
<script nonce="${nonce}">
const vscode = acquireVsCodeApi();
const form = document.getElementById('form');
const error = document.getElementById('error');
const submit = document.getElementById('submit');
document.getElementById('cancel').addEventListener('click', () => vscode.postMessage({ type: 'cancel' }));
form.addEventListener('submit', (event) => {
  event.preventDefault();
  const values = {};
  for (const el of form.querySelectorAll('[name]')) values[el.name] = el.value;
  const missing = Array.from(form.querySelectorAll('[required]')).find((el) => !String(el.value || '').trim());
  if (missing) { error.textContent = 'Fill in ' + (form.querySelector('label[for="' + missing.id + '"]')?.textContent || 'the required fields').replace(' *', '') + '.'; missing.focus(); return; }
  error.textContent = '';
  submit.disabled = true;
  vscode.postMessage({ type: 'submit', values });
});
window.addEventListener('message', (event) => {
  if (event.data && event.data.type === 'error') { error.textContent = event.data.message; submit.disabled = false; }
});
document.querySelector('input, select, textarea')?.focus();
</script>
</body>
</html>`;
}

/**
 * Shows a form. onSubmit runs with the values; when it returns an error message the form stays open and shows it,
 * otherwise the form closes and the promise resolves with onSubmit's result.
 */
export function showForm<T>(spec: FormSpec, onSubmit: (values: Record<string, string>) => Promise<{ error: string } | { result: T }>): Promise<T | undefined> {
  const panel = vscode.window.createWebviewPanel('armada.form', spec.title, vscode.ViewColumn.Active, { enableScripts: true, retainContextWhenHidden: true });
  panel.webview.html = renderFormHtml(spec, crypto.randomBytes(16).toString('base64'));
  return new Promise((resolve) => {
    let settled = false;
    panel.webview.onDidReceiveMessage(async (message: { type?: string; values?: Record<string, string> }) => {
      if (message?.type === 'cancel') {
        settled = true;
        resolve(undefined);
        panel.dispose();
      } else if (message?.type === 'submit' && message.values) {
        const outcome = await onSubmit(message.values);
        if ('error' in outcome) {
          void panel.webview.postMessage({ type: 'error', message: outcome.error });
          return;
        }
        settled = true;
        resolve(outcome.result);
        panel.dispose();
      }
    });
    panel.onDidDispose(() => {
      if (!settled) resolve(undefined);
    });
  });
}
