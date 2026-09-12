interface BoolIconProps {
  /** The boolean value to render. */
  value: boolean;
  /** How to render a false value: a muted dash (default) or a red cross. */
  falseVariant?: 'dash' | 'cross';
  /** Accessible label + tooltip for the true state. */
  trueTitle: string;
  /** Accessible label + tooltip for the false state. */
  falseTitle: string;
}

/**
 * Compact boolean indicator for dense tables: a green check for true, and either a muted dash or a red
 * cross for false. The shape (check / dash / cross) carries the meaning, so it never relies on color alone.
 */
export default function BoolIcon({ value, falseVariant = 'dash', trueTitle, falseTitle }: BoolIconProps) {
  if (value) {
    return (
      <span className="bool-icon" role="img" aria-label={trueTitle} title={trueTitle}>
        <svg width="16" height="16" viewBox="0 0 16 16" aria-hidden="true" focusable="false">
          <path d="M13 4.5 6.5 11 3 7.5" fill="none" stroke="#22c55e" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
      </span>
    );
  }

  if (falseVariant === 'cross') {
    return (
      <span className="bool-icon" role="img" aria-label={falseTitle} title={falseTitle}>
        <svg width="16" height="16" viewBox="0 0 16 16" aria-hidden="true" focusable="false">
          <path d="M4 4l8 8M12 4l-8 8" fill="none" stroke="#ef4444" strokeWidth="2" strokeLinecap="round" />
        </svg>
      </span>
    );
  }

  return (
    <span className="text-dim" role="img" aria-label={falseTitle} title={falseTitle}>&ndash;</span>
  );
}
