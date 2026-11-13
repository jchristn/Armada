import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const css = readFileSync('./src/App.css', 'utf8');
describe('captain checkbox form conventions', () => {
  it.each(['Captains', 'CaptainDetail'])('uses inline native checkbox labels in %s', (page) => {
    const source = readFileSync(`./src/pages/${page}.tsx`, 'utf8');
    expect(source).toContain('className="checkbox-row captain-active-field"');
    expect(source).toContain('className="checkbox-row captain-auto-approve-field"');
    expect(source).toContain('checked={form.active ?? true}');
  });
  it('overrides modal column labels and full-width input styling for both switches', () => {
    const row = css.slice(css.indexOf('.modal label.captain-auto-approve-field,'));
    expect(row.slice(0, row.indexOf('}'))).toMatch(/flex-direction:\s*row/);
    const input = row.slice(row.indexOf('input[type="checkbox"]'));
    const declarations = input.slice(0, input.indexOf('}'));
    expect(declarations).toMatch(/width:\s*auto/);
    expect(declarations).toMatch(/margin:\s*0/);
    expect(declarations).toMatch(/flex:\s*0 0 auto/);
  });
});
