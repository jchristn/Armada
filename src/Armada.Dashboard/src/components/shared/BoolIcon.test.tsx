import { render, screen } from '@testing-library/react';
import BoolIcon from './BoolIcon';

describe('BoolIcon', () => {
  it('renders a labelled check for true', () => {
    render(<BoolIcon value={true} trueTitle="Built-in" falseTitle="Not built-in" />);
    const el = screen.getByRole('img', { name: 'Built-in' });
    expect(el).toBeInTheDocument();
    expect(el.querySelector('svg')).not.toBeNull();
  });

  it('renders a muted dash for false by default', () => {
    render(<BoolIcon value={false} trueTitle="Built-in" falseTitle="Not built-in" />);
    const el = screen.getByRole('img', { name: 'Not built-in' });
    expect(el).toBeInTheDocument();
    expect(el.querySelector('svg')).toBeNull();
  });

  it('renders a cross svg for false when falseVariant is cross', () => {
    render(<BoolIcon value={false} falseVariant="cross" trueTitle="Active" falseTitle="Inactive" />);
    const el = screen.getByRole('img', { name: 'Inactive' });
    expect(el).toBeInTheDocument();
    expect(el.querySelector('svg')).not.toBeNull();
  });
});
