/*
 * Renders the native detail pages (mission, voyage, captain, merge entry) as VS Code-themed HTML: the editor's fonts,
 * colors, buttons and focus rings from the --vscode-* tokens, no web framework. A page is data (DetailPage); buttons
 * and links run extension commands through the host, which accepts only armada.* commands. Pure, for tests.
 */

/** Tone of a status pill. */
export type Tone = 'info' | 'ok' | 'warn' | 'error' | 'muted';

/** A command a button or link runs. */
export interface DetailLink {
  command: string;
  args?: unknown[];
}

/** A toolbar button. */
export interface DetailAction extends DetailLink {
  label: string;
  primary?: boolean;
}

/** One row of a fields table. */
export interface DetailField {
  label: string;
  value: string;
  code?: boolean;
  link?: DetailLink;
}

/** One row of a list. */
export interface DetailListItem {
  label: string;
  description?: string;
  status?: { label: string; tone: Tone };
  link?: DetailLink;
}

/** A page section. */
export type DetailSection =
  | { kind: 'fields'; title?: string; rows: DetailField[] }
  | { kind: 'text'; title: string; text: string; tone?: Tone }
  | { kind: 'pre'; title: string; text: string; note?: string }
  | { kind: 'list'; title: string; items: DetailListItem[]; empty?: string }
  | { kind: 'progress'; title?: string; done: number; failed: number; running: number; total: number };

/** A detail page. */
export interface DetailPage {
  title: string;
  status?: { label: string; tone: Tone };
  subtitle?: string;
  actions: DetailAction[];
  sections: DetailSection[];
}

/** A message from the page's buttons and links. */
export interface DetailMessage {
  command: string;
  args: unknown[];
}

/** True for a message the host may run: an armada.* command with an argument array. */
export function isDetailMessage(value: unknown): value is DetailMessage {
  if (!value || typeof value !== 'object') return false;
  const { command, args } = value as { command?: unknown; args?: unknown };
  return typeof command === 'string' && /^armada\.[A-Za-z.]+$/.test(command) && Array.isArray(args);
}

/** Escapes text for HTML. */
export function escapeHtml(value: unknown): string {
  return String(value ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c] ?? c);
}

function linkAttrs(link: DetailLink): string {
  return `data-command="${escapeHtml(link.command)}" data-args="${escapeHtml(JSON.stringify(link.args ?? []))}"`;
}

function pill(status: { label: string; tone: Tone }): string {
  return `<span class="pill ${status.tone}">${escapeHtml(status.label)}</span>`;
}

function renderSection(section: DetailSection): string {
  switch (section.kind) {
    case 'fields': {
      const rows = section.rows.filter((r) => r.value).map((r) => {
        const value = r.code ? `<code>${escapeHtml(r.value)}</code>` : escapeHtml(r.value);
        return `<tr><th scope="row">${escapeHtml(r.label)}</th><td>${r.link ? `<a href="#" ${linkAttrs(r.link)}>${value}</a>` : value}</td></tr>`;
      }).join('');
      return `<section>${section.title ? `<h2>${escapeHtml(section.title)}</h2>` : ''}<table class="fields">${rows}</table></section>`;
    }
    case 'text':
      return `<section><h2>${escapeHtml(section.title)}</h2><div class="text ${section.tone ?? ''}">${escapeHtml(section.text)}</div></section>`;
    case 'pre':
      return `<section><h2>${escapeHtml(section.title)}${section.note ? ` <span class="muted">${escapeHtml(section.note)}</span>` : ''}</h2><pre class="pre">${escapeHtml(section.text)}</pre></section>`;
    case 'list': {
      if (!section.items.length) return `<section><h2>${escapeHtml(section.title)}</h2><p class="muted">${escapeHtml(section.empty ?? 'None.')}</p></section>`;
      const items = section.items.map((item) => {
        const label = item.link ? `<a href="#" ${linkAttrs(item.link)}>${escapeHtml(item.label)}</a>` : escapeHtml(item.label);
        return `<li>${item.status ? `${pill(item.status)} ` : ''}${label}${item.description ? ` <span class="muted">${escapeHtml(item.description)}</span>` : ''}</li>`;
      }).join('');
      return `<section><h2>${escapeHtml(section.title)}</h2><ul class="list">${items}</ul></section>`;
    }
    case 'progress': {
      const total = Math.max(section.total, 1);
      const pct = (n: number): string => `${Math.round((n / total) * 1000) / 10}%`;
      return `<section>${section.title ? `<h2>${escapeHtml(section.title)}</h2>` : ''}<div class="progress" role="img" aria-label="${section.done} of ${section.total} complete, ${section.failed} failed"><span class="ok" style="width:${pct(section.done)}"></span><span class="error" style="width:${pct(section.failed)}"></span><span class="info" style="width:${pct(section.running)}"></span></div><p class="muted">${section.done} complete · ${section.running} running · ${section.failed} failed · ${section.total} total</p></section>`;
    }
  }
}

/** The page body (everything that live updates replace). */
export function renderDetailContent(page: DetailPage): string {
  const actions = page.actions.map((a) => `<button class="${a.primary ? '' : 'secondary'}" ${linkAttrs(a)}>${escapeHtml(a.label)}</button>`).join('');
  return `<header><h1>${escapeHtml(page.title)} ${page.status ? pill(page.status) : ''}</h1>${page.subtitle ? `<div class="subtitle"><code>${escapeHtml(page.subtitle)}</code> <a href="#" ${linkAttrs({ command: 'armada.copyText', args: [page.subtitle] })} title="Copy">Copy</a></div>` : ''}</header>
<div class="toolbar" role="toolbar">${actions}</div>
${page.sections.map(renderSection).join('\n')}`;
}

/** The whole page. Updates arrive as {type:'update', html} messages and replace #content in place. */
export function renderDetailHtml(page: DetailPage, nonce: string): string {
  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}'">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<style>
body { font-family: var(--vscode-font-family); font-size: var(--vscode-font-size); color: var(--vscode-foreground); padding: 0 20px 24px; line-height: 1.5; overflow-wrap: anywhere; }
header { padding-top: 16px; }
h1 { font-size: 1.35em; font-weight: 600; margin: 0 0 2px; display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
h2 { font-size: 0.85em; text-transform: uppercase; letter-spacing: 0.04em; color: var(--vscode-descriptionForeground); font-weight: 600; margin: 22px 0 6px; }
.subtitle { color: var(--vscode-descriptionForeground); font-size: 0.92em; }
a { color: var(--vscode-textLink-foreground); text-decoration: none; cursor: pointer; }
a:hover { text-decoration: underline; color: var(--vscode-textLink-activeForeground); }
a:focus-visible, button:focus-visible { outline: 1px solid var(--vscode-focusBorder); outline-offset: 2px; }
code, .pre { font-family: var(--vscode-editor-font-family); font-size: var(--vscode-editor-font-size); }
.toolbar { position: sticky; top: 0; z-index: 1; background: var(--vscode-editor-background); padding: 10px 0; border-bottom: 1px solid var(--vscode-panel-border); display: flex; flex-wrap: wrap; gap: 6px; margin-top: 8px; }
button { font-family: inherit; font-size: inherit; line-height: 1.4; background: var(--vscode-button-background); color: var(--vscode-button-foreground); border: 1px solid var(--vscode-button-border, transparent); border-radius: 2px; padding: 4px 12px; cursor: pointer; }
button:hover { background: var(--vscode-button-hoverBackground); }
button.secondary { background: var(--vscode-button-secondaryBackground); color: var(--vscode-button-secondaryForeground); }
button.secondary:hover { background: var(--vscode-button-secondaryHoverBackground); }
table.fields { border-collapse: collapse; }
table.fields th { text-align: left; font-weight: normal; color: var(--vscode-descriptionForeground); padding: 3px 18px 3px 0; vertical-align: top; white-space: nowrap; }
table.fields td { padding: 3px 0; }
.text { white-space: pre-wrap; background: var(--vscode-textBlockQuote-background); border-left: 3px solid var(--vscode-textBlockQuote-border); padding: 8px 12px; }
.text.error { border-left-color: var(--vscode-errorForeground); }
.pre { white-space: pre; overflow: auto; max-height: 360px; background: var(--vscode-textCodeBlock-background); padding: 8px 10px; margin: 0; }
.muted { color: var(--vscode-descriptionForeground); font-weight: normal; text-transform: none; letter-spacing: 0; }
ul.list { list-style: none; padding: 0; margin: 0; }
ul.list li { padding: 4px 0; border-bottom: 1px solid var(--vscode-widget-border, transparent); }
.pill { font-size: 0.72rem; font-weight: 600; padding: 1px 8px; border-radius: 10px; white-space: nowrap; border: 1px solid; vertical-align: middle; }
.pill.info { color: var(--vscode-charts-blue); border-color: var(--vscode-charts-blue); }
.pill.ok { color: var(--vscode-testing-iconPassed); border-color: var(--vscode-testing-iconPassed); }
.pill.warn { color: var(--vscode-charts-yellow); border-color: var(--vscode-charts-yellow); }
.pill.error { color: var(--vscode-errorForeground); border-color: var(--vscode-errorForeground); }
.pill.muted { color: var(--vscode-descriptionForeground); border-color: var(--vscode-descriptionForeground); }
.progress { display: flex; height: 8px; border-radius: 4px; overflow: hidden; background: var(--vscode-progressBar-background, var(--vscode-panel-border)); opacity: 0.9; max-width: 560px; }
.progress span.ok { background: var(--vscode-testing-iconPassed); } .progress span.error { background: var(--vscode-errorForeground); } .progress span.info { background: var(--vscode-charts-blue); }
</style>
</head>
<body>
<div id="content">${renderDetailContent(page)}</div>
<script nonce="${nonce}">
const vscode = acquireVsCodeApi();
document.addEventListener('click', (event) => {
  const target = event.target instanceof Element ? event.target.closest('[data-command]') : null;
  if (!target) return;
  event.preventDefault();
  let args = [];
  try { args = JSON.parse(target.getAttribute('data-args') || '[]'); } catch { args = []; }
  vscode.postMessage({ command: target.getAttribute('data-command'), args });
});
window.addEventListener('message', (event) => {
  const message = event.data;
  if (!message || message.type !== 'update') return;
  const pres = Array.from(document.querySelectorAll('.pre')).map((p) => ({ top: p.scrollTop, atEnd: p.scrollTop + p.clientHeight >= p.scrollHeight - 4 }));
  document.getElementById('content').innerHTML = message.html;
  Array.from(document.querySelectorAll('.pre')).forEach((p, i) => { const s = pres[i]; p.scrollTop = !s || s.atEnd ? p.scrollHeight : s.top; });
});
Array.from(document.querySelectorAll('.pre')).forEach((p) => { p.scrollTop = p.scrollHeight; });
</script>
</body>
</html>`;
}
