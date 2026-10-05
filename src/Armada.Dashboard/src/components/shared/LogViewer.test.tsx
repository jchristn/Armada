import { render } from '@testing-library/react';
import LogViewer from './LogViewer';

vi.mock('../../context/LocaleContext', () => ({ useLocale: () => ({ t: (text: string) => text }) }));

describe('LogViewer markdown mode (F18)', () => {
  it('keeps single newlines of a mission log as line breaks', () => {
    const log = ['[08:00:01] Mission started', '[08:00:02] Cloning repository', '[08:00:03] Running captain', '', '## Summary', '- one', '- two'].join('\n');
    const { container } = render(<LogViewer open title="Log" content={log} markdown onClose={() => {}} />);

    const body = container.querySelector('#log-viewer-content')!;
    const paragraph = body.querySelector('p')!;
    expect(paragraph.querySelectorAll('br')).toHaveLength(2);
    expect(paragraph.textContent).toContain('[08:00:01] Mission started');
    expect(paragraph.textContent).toContain('[08:00:03] Running captain');
    // Markdown structure still renders.
    expect(body.querySelector('h2')?.textContent).toBe('Summary');
    expect(body.querySelectorAll('li')).toHaveLength(2);
  });

  it('leaves fenced code blocks untouched', () => {
    const { container } = render(<LogViewer open title="Log" content={'```\nline a\nline b\n```'} markdown onClose={() => {}} />);
    const code = container.querySelector('#log-viewer-content code')!;
    expect(code.textContent).toBe('line a\nline b\n');
    expect(code.querySelectorAll('br')).toHaveLength(0);
  });
});
