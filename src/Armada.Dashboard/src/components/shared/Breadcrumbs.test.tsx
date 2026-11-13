import { render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import Breadcrumbs from './Breadcrumbs';
import PageHeader from './PageHeader';

vi.mock('../../context/LocaleContext', () => ({ useLocale: () => ({ t: (text: string) => text }) }));

describe('Dashboard breadcrumbs', () => {
  it('uses the same structure for captain and nested evaluation pages', () => {
    const captain = [{ label: 'Captains', to: '/captains' }, { label: 'model-one', to: '/captains/cpt_1' }];
    const view = render(<MemoryRouter><PageHeader title="model-one" breadcrumb={captain} /></MemoryRouter>);
    const first = screen.getByRole('navigation', { name: 'Breadcrumbs' });
    expect(within(first).getByRole('list')).toBeInTheDocument();
    expect(within(first).getByRole('link', { name: 'Captains' })).toHaveAttribute('href', '/captains');
    expect(within(first).getByText('model-one')).toHaveAttribute('aria-current', 'page');
    const ancestorMarkup = first.querySelector('li')!.outerHTML;

    view.rerender(<MemoryRouter><PageHeader title="Evaluation mpr_1" breadcrumb={[
      ...captain, { label: 'Evaluations', to: '/evaluations?captainId=cpt_1' }, { label: 'mpr_1' },
    ]} /></MemoryRouter>);
    const nested = screen.getByRole('navigation', { name: 'Breadcrumbs' });
    expect(nested.querySelector('li')!.outerHTML).toBe(ancestorMarkup);
    expect(within(nested).getByRole('link', { name: 'model-one' })).toHaveAttribute('href', '/captains/cpt_1');
    expect(within(nested).getByRole('link', { name: 'Evaluations' })).toHaveAttribute('href', '/evaluations?captainId=cpt_1');
    expect(nested.querySelectorAll('[aria-current="page"]')).toHaveLength(1);
    expect(within(nested).getByText('mpr_1')).toHaveAttribute('aria-current', 'page');
    const separators = nested.querySelectorAll('.breadcrumb-sep');
    expect(separators).toHaveLength(3);
    for (const separator of separators) {
      expect(separator.textContent).toBe('>');
      expect(separator).toHaveAttribute('aria-hidden', 'true');
    }
  });

  it('preserves router basenames, escaping and titles without page-specific formatting', () => {
    render(<MemoryRouter basename="/dashboard" initialEntries={['/dashboard/captains/cpt_1']}>
      <Breadcrumbs items={[{ label: 'Captains', to: '/captains', title: 'Captain list' }, { label: '<model & name>' }]} />
    </MemoryRouter>);
    expect(screen.getByRole('link', { name: 'Captains' })).toHaveAttribute('href', '/dashboard/captains');
    expect(screen.getByRole('link', { name: 'Captains' })).toHaveAttribute('title', 'Captain list');
    expect(screen.getByText('<model & name>')).toHaveAttribute('aria-current', 'page');
    expect(document.querySelector('.breadcrumb [style], .breadcrumb .mono')).toBeNull();
  });

  it('omits an empty trail', () => {
    render(<MemoryRouter><Breadcrumbs items={[]} /></MemoryRouter>);
    expect(screen.queryByRole('navigation')).not.toBeInTheDocument();
  });

  // Audit every page so an ad hoc implementation cannot silently reintroduce different spacing or separators.
  const pages = import.meta.glob<string>('../../pages/**/*.tsx', { eager: true, query: '?raw', import: 'default' });
  const pageSources = Object.entries(pages).filter(([name]) => !name.includes('.test.'));
  it.each(pageSources)('%s delegates breadcrumb markup and styling to the shared component', (_page, source) => {
    expect(source).not.toMatch(/className=["'](?:breadcrumb|breadcrumbs|breadcrumb-sep|bc-sep)["']/);
    expect(source).not.toMatch(/<Link\b[^>]*>[^]*?<\/Link>\s*(?:\/\s*<Link|<span[^>]*>\s*(?:\/|&gt;)\s*<\/span>)/);
    if (/\bbreadcrumb=|<Breadcrumbs\b/.test(source)) {
      expect(source).toMatch(/(?:<PageHeader\b|<Breadcrumbs\b)/);
    }
  });
});
