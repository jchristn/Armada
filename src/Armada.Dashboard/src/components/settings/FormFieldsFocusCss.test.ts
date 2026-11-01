import { readFileSync } from 'node:fs';

const a11y = readFileSync('./src/a11y.css', 'utf8');
const app = readFileSync('./src/App.css', 'utf8');

describe('form field keyboard focus styling', () => {
  it('keeps the app-wide keyboard focus ring for every interactive element', () => {
    expect(a11y).toContain(':where(a, button, summary, select, input, textarea, [tabindex]):focus-visible');
  });

  it('gives the shared form fields their keyboard focus ring back', () => {
    // .form-group fields declare `outline: none` in App.css, which outranks the global rule above
    // (:where() adds no specificity), so the settings and captain forms showed only a border-color
    // change on keyboard focus while every other control on the page showed the ring.
    const rule = a11y.slice(
      a11y.indexOf('.form-group input:focus-visible'),
      a11y.indexOf('}', a11y.indexOf('.form-group input:focus-visible')),
    );
    expect(rule).toContain('.form-group input:focus-visible');
    expect(rule).toContain('.form-group textarea:focus-visible');
    expect(rule).toContain('.form-group select:focus-visible');
    expect(rule).toMatch(/outline:\s*2px solid var\(--accent\)/);
  });

  it('does not change the fields\' mouse focus, which stays the border-color change', () => {
    const focus = app.slice(
      app.indexOf('.form-group input:focus,'),
      app.indexOf('}', app.indexOf('.form-group input:focus,')),
    );
    expect(focus).toContain('.form-group select:focus');
    expect(focus).toMatch(/border-color:\s*var\(--accent\)/);
  });
});