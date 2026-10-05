import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import DiffViewer from './DiffViewer';

vi.mock('../../context/LocaleContext', () => ({
  useLocale: () => ({ t: (text: string) => text }),
}));

const RAW = [
  'diff --git a/a.txt b/a.txt',
  '--- a/a.txt',
  '+++ b/a.txt',
  '@@ -1,2 +1,3 @@',
  ' keep',
  '+++ added line that starts with ++',
  '-old',
  '+new',
  'diff --git a/b.txt b/b.txt',
  'deleted file mode 100644',
  '--- a/b.txt',
  '+++ /dev/null',
  '@@ -1 +0,0 @@',
  '-gone',
].join('\n');

describe('DiffViewer', () => {
  it('counts files and +/- from hunk ranges, including an added "++ " line', () => {
    const { container } = render(<DiffViewer open title="Diff" rawDiff={RAW} onClose={() => {}} />);
    expect(container.querySelector('.diff-stat-files')?.textContent).toContain('2');
    expect(container.querySelector('.diff-stat-add')?.textContent).toBe('+2');
    expect(container.querySelector('.diff-stat-del')?.textContent).toBe('-2');
    // The "+++ added line" is rendered as an added line, not as a meta header.
    const added = Array.from(container.querySelectorAll('.diff-line-add .diff-line-content')).map((n) => n.textContent);
    expect(added).toContain('+++ added line that starts with ++');
  });

  it('shows one file section when a file is selected, using the deleted file old path', () => {
    const { container } = render(<DiffViewer open title="Diff" rawDiff={RAW} onClose={() => {}} />);
    fireEvent.click(screen.getByText('b.txt'));
    const headers = Array.from(container.querySelectorAll('.diff-file-header')).map((n) => n.textContent);
    expect(headers).toEqual(['diff --git a/b.txt b/b.txt']);
  });
});
