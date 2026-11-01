import { readFileSync } from 'node:fs';

const css = readFileSync('./src/App.css', 'utf8');
const page = readFileSync('./src/pages/Jobs.tsx', 'utf8');
const badge = readFileSync('./src/components/shared/StatusBadge.tsx', 'utf8');

function rule(selector: string): string {
  const start = css.indexOf(`${selector} {`);
  expect(start).toBeGreaterThanOrEqual(0);
  return css.slice(start, css.indexOf('}', start));
}

describe('Jobs status badge styling', () => {
  it('renders the job status through the shared StatusBadge, which carries the tag classes', () => {
    expect(page).toContain('<StatusBadge status={job.status} />');
    expect(badge).toContain('className={`tag ${normalized} ${className}`.trim()}');
  });

  it('colors a Running job like every other in-progress status', () => {
    // Jobs reports Running, Succeeded, Failed, Cancelled and Queued. Before this mapping a Running badge
    // fell back to the neutral .tag default and read as "no state", unlike Working everywhere else.
    const working = rule('.tag.working, .tag.inprogress, .tag.assigned, .tag.open, .tag.testing, .tag.verifying, .tag.running');
    expect(working).toContain('.tag.running');
    expect(working).toMatch(/background:\s*var\(--tag-working-bg\)/);
    expect(working).toMatch(/color:\s*var\(--tag-working-fg\)/);
  });

  it('colors a Succeeded job like every other completed status', () => {
    const complete = rule('.tag.complete, .tag.completed, .tag.succeeded, .tag.landed, .tag.passed, .tag.pass, .tag.healthy, .tag.valid');
    expect(complete).toContain('.tag.succeeded');
    expect(complete).toMatch(/background:\s*var\(--tag-complete-bg\)/);
    expect(complete).toMatch(/color:\s*var\(--tag-complete-fg\)/);
  });

  it('keeps the failed and cancelled job states on their own pairs', () => {
    const failed = rule('.tag.failed, .tag.error, .tag.stalled, .tag.landingfailed, .tag.invalid');
    expect(failed).toMatch(/background:\s*var\(--tag-failed-bg\)/);
    const cancelled = rule('.tag.cancelled, .tag.stopping');
    expect(cancelled).toMatch(/background:\s*var\(--tag-cancelled-bg\)/);
    const queued = rule('.tag.idle, .tag.pending, .tag.queued');
    expect(queued).toMatch(/background:\s*var\(--tag-idle-bg\)/);
  });
});