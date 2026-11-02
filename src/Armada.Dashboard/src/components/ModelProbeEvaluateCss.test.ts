import { readFileSync } from 'node:fs';

const css = readFileSync('./src/App.css', 'utf8');
const sources = [
  readFileSync('./src/components/ModelProbeEvaluate.tsx', 'utf8'),
  readFileSync('./src/components/ModelEvaluateDialog.tsx', 'utf8'),
  readFileSync('./src/components/evaluations/ModelProbeRunDetails.tsx', 'utf8'),
].join('\n');

const classes = [
  'model-probe-evaluate',
  'model-probe-evaluate-actions',
  'model-probe-evaluate-counts',
  'model-probe-evaluate-status',
  'model-probe-evaluate-timing',
  'model-probe-evaluate-tokens',
  'evaluation-probes-table',
  'model-evaluation-report',
  'model-evaluation-personas',
  'model-evaluate-depth',
  'text-warning',
];

describe('ModelProbeEvaluate / ModelEvaluateDialog CSS contract', () => {
  it.each(classes)('defines a rule for .%s used by the components', (name) => {
    expect(sources).toContain(name);
    expect(css).toMatch(new RegExp(`\\.${name}(?![\\w-])[^{]*\\{`));
  });

  it('lays out the action row as a wrapping flex row with a gap', () => {
    const rule = css.match(/\.model-probe-evaluate-actions\s*\{([^}]*)\}/);
    expect(rule?.[1]).toMatch(/display:\s*flex/);
    expect(rule?.[1]).toMatch(/flex-wrap:\s*wrap/);
    expect(rule?.[1]).toMatch(/gap:/);
  });

  it('colors .text-warning with a theme variable like .text-danger', () => {
    expect(css).toMatch(/\.text-warning\s*\{[^}]*color:\s*var\(--orange, var\(--yellow\)\)/);
  });

  it('keeps no inline margin styles on the evaluate panel lists', () => {
    expect(sources).not.toMatch(/className="model-(probe-results|evaluation-personas|evaluation-report|evaluate-depth)"[^>]*style=/);
  });
});
