import { fireEvent, render, screen } from '@testing-library/react-native';
import { Markdown, decodeEntities, linkTarget } from '../components/ask/Markdown';
import { ToolChips } from '../components/ask/ToolChips';
import { AppProviders } from '../test/render';

const mockPush = jest.fn();
jest.mock('expo-router', () => ({ useRouter: () => ({ push: mockPush }) }));

async function renderMd(text: string, preserveLineBreaks = false) {
  await render(<AppProviders><Markdown testID="md" preserveLineBreaks={preserveLineBreaks}>{text}</Markdown></AppProviders>);
}

describe('Markdown (GitHub-flavored, like the dashboard)', () => {
  it('renders headings, emphasis, code, lists, task lists, quotes, and tables', async () => {
    await renderMd([
      '# Status', '', 'Two **voyages**, one _mission_, ~~none~~ and `code`.', '',
      '- first', '- second', '', '1. one', '2. two', '', '- [x] done', '- [ ] todo', '',
      '> quoted', '', '```', 'dotnet build', '```', '', '| A | B |', '|---|---|', '| 1 | 2 |',
    ].join('\n'));
    expect(screen.getByRole('header', { name: 'Status' })).toBeTruthy();
    expect(screen.getByText('voyages')).toBeTruthy();
    expect(screen.getByText('code')).toBeTruthy();
    expect(screen.getByText('second')).toBeTruthy();
    expect(screen.getByText('2.')).toBeTruthy();
    expect(screen.getByText('\u2611')).toBeTruthy();
    expect(screen.getByText('\u2610')).toBeTruthy();
    expect(screen.getByText('quoted')).toBeTruthy();
    expect(screen.getByText('dotnet build')).toBeTruthy();
    expect(screen.getByText('B')).toBeTruthy();
  });

  it('decodes entities, drops raw HTML, and joins soft line breaks unless asked to keep them', async () => {
    await renderMd('a &amp; b <span>x</span>\nnext');
    expect(screen.getByTestId('md')).toHaveTextContent('a & b x next');
    expect(screen.queryByText('<span>')).toBeNull();
  });

  it('keeps line breaks for line-oriented text', async () => {
    await renderMd('line1\nline2', true);
    expect(screen.getByText('line1\nline2')).toBeTruthy();
  });

  it('classifies links: app pages in the app, web and mail outside, anything else as text', () => {
    expect(linkTarget('/missions/msn_1')).toEqual({ kind: 'app', path: '/missions/msn_1' });
    expect(linkTarget('armada://voyages/vyg_1')).toEqual({ kind: 'app', path: '/voyages/vyg_1' });
    expect(linkTarget('https://admiral:7890/dashboard/captains/cpt_1')).toEqual({ kind: 'app', path: '/captains/cpt_1' });
    expect(linkTarget('https://github.com/x/y/pull/1')).toEqual({ kind: 'external', url: 'https://github.com/x/y/pull/1' });
    expect(linkTarget('mailto:ops@example.com')).toEqual({ kind: 'external', url: 'mailto:ops@example.com' });
    expect(linkTarget('javascript:alert(1)')).toEqual({ kind: 'none' });
    expect(linkTarget('/missions/../server')).toEqual({ kind: 'none' });
    expect(linkTarget('')).toEqual({ kind: 'none' });
  });

  it('decodes named, decimal, and hex entities', () => {
    expect(decodeEntities('&lt;b&gt; &quot;x&quot; &#39;y&#39; &#x41;&#66; &bogus;')).toBe('<b> "x" \'y\' AB &bogus;');
  });
});

describe('tool chips', () => {
  it('show the status, name, and preview; the details open on tap', async () => {
    await render(
      <AppProviders>
        <ToolChips tools={[{ id: 'c1', name: 'status', status: 'success', arguments: '{"a":1}', result: '{"ok":true}', elapsedMs: 1500 }, { id: 'c2', name: 'Bash', status: 'failed', permissionDenied: true }]} permissionDeniedNote="Refused: policy" />
      </AppProviders>,
    );
    expect(screen.getByText('1.50s')).toBeTruthy();
    expect(screen.getByText('{"ok":true}')).toBeTruthy();
    expect(screen.getByText('Refused: policy')).toBeTruthy();
    expect(screen.getByLabelText('Bash, Refused for lack of permission')).toBeTruthy();
    expect(screen.queryByText('Arguments')).toBeNull();
    await fireEvent.press(screen.getByTestId('tool-chip-c1'));
    expect(screen.getByText('Arguments')).toBeTruthy();
    expect(screen.getByText('{\n  "a": 1\n}')).toBeTruthy();
  });
});
