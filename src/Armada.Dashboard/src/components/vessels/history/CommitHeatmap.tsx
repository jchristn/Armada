import { useMemo, useRef, useState, type KeyboardEvent } from 'react';
import type { VesselCommitActivity } from '../../../types/models';
import { useLocale } from '../../../context/LocaleContext';
import { addDays, buildMonthLabels, buildWeeks, formatIsoDay, heatLevel } from '../../../lib/vesselHistory';

interface CommitHeatmapProps {
  activity: VesselCommitActivity;
  /** Selected day (yyyy-MM-dd) or null. */
  selectedDate: string | null;
  onSelectDate: (date: string) => void;
  /** Already-localized range description for the total line, e.g. "the last year" or "2024". */
  rangeLabel: string;
}

const WEEKDAY_REFERENCE_SUNDAY = Date.UTC(2023, 0, 1); // a Sunday

/**
 * GitHub-style contribution heatmap: one column per week, rows Sunday..Saturday, five intensity
 * levels scaled by the range's busiest day. The grid is a single tab stop with arrow-key navigation
 * (left/right = week, up/down = day, Home/End = range ends); Enter or Space selects the focused day.
 */
export default function CommitHeatmap({ activity, selectedDate, onSelectDate, rangeLabel }: CommitHeatmapProps) {
  const { t, locale } = useLocale();
  const gridRef = useRef<HTMLDivElement>(null);
  const weeks = useMemo(() => buildWeeks(activity.days), [activity.days]);
  const monthLabels = useMemo(() => buildMonthLabels(weeks), [weeks]);
  const firstDate = activity.days[0]?.date ?? null;
  const lastDate = activity.days[activity.days.length - 1]?.date ?? null;
  const [focusDate, setFocusDate] = useState<string | null>(null);

  const inRange = (date: string | null) => !!date && !!firstDate && !!lastDate && date >= firstDate && date <= lastDate;
  const activeDate = inRange(focusDate) ? focusDate : inRange(selectedDate) ? selectedDate : lastDate;

  const monthFormatter = useMemo(() => new Intl.DateTimeFormat(locale, { month: 'short', timeZone: 'UTC' }), [locale]);
  const weekdayNames = useMemo(() => {
    const short = new Intl.DateTimeFormat(locale, { weekday: 'short', timeZone: 'UTC' });
    const long = new Intl.DateTimeFormat(locale, { weekday: 'long', timeZone: 'UTC' });
    return Array.from({ length: 7 }, (_, i) => {
      const date = new Date(WEEKDAY_REFERENCE_SUNDAY + i * 86400000);
      return { short: short.format(date), long: long.format(date) };
    });
  }, [locale]);

  function describe(date: string, count: number): string {
    return t('{count, plural, =0 {No commits} one {# commit} other {# commits}} on {{date}}', { count, date: formatIsoDay(locale, date, { weekday: 'short', year: 'numeric', month: 'short', day: 'numeric' }) });
  }

  function moveFocus(date: string) {
    if (!inRange(date)) return;
    setFocusDate(date);
    const cell = gridRef.current?.querySelector<HTMLElement>(`[data-date="${date}"]`);
    cell?.focus();
  }

  function handleKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    const current = (event.target as HTMLElement).getAttribute('data-date');
    if (!current) return;
    let target: string | null = null;
    switch (event.key) {
      case 'ArrowLeft': target = addDays(current, -7); break;
      case 'ArrowRight': target = addDays(current, 7); break;
      case 'ArrowUp': target = addDays(current, -1); break;
      case 'ArrowDown': target = addDays(current, 1); break;
      case 'Home': target = firstDate; break;
      case 'End': target = lastDate; break;
      case 'Enter':
      case ' ':
        event.preventDefault();
        onSelectDate(current);
        return;
      default:
        return;
    }
    event.preventDefault();
    if (target) moveFocus(target);
  }

  const columnTemplate = { ['--heat-weeks' as string]: String(weeks.length) };

  return (
    <div className="vhist-heatmap">
      <div className="vhist-heatmap-scroll">
        <div className="vhist-heatmap-months" style={columnTemplate} aria-hidden="true" data-testid="heatmap-months">
          <span className="vhist-heatmap-corner" />
          {monthLabels.map((label) => (
            <span
              key={`${label.year}-${label.month}-${label.column}`}
              className="vhist-heatmap-month"
              style={{ gridColumn: `${label.column + 2} / span 3` }}
            >
              {monthFormatter.format(new Date(Date.UTC(label.year, label.month - 1, 1)))}
            </span>
          ))}
        </div>
        <div
          ref={gridRef}
          role="grid"
          aria-label={t('Commit activity')}
          className="vhist-heatmap-grid"
          style={columnTemplate}
          onKeyDown={handleKeyDown}
        >
          {weekdayNames.map((weekday, row) => (
            <div role="row" key={weekday.long} className="vhist-heatmap-row">
              <span role="rowheader" className={`vhist-heatmap-weekday${row % 2 === 1 ? '' : ' vhist-heatmap-weekday-quiet'}`}>
                <span aria-hidden="true">{weekday.short}</span>
                <span className="sr-only">{weekday.long}</span>
              </span>
              {weeks.map((week, column) => {
                const day = week[row];
                if (!day) return <span role="gridcell" key={`pad-${column}`} className="vhist-heat-cell vhist-heat-pad" aria-hidden="true" />;
                const level = heatLevel(day.count, activity.maxDayCount);
                const label = describe(day.date, day.count);
                const selected = day.date === selectedDate;
                return (
                  <span
                    role="gridcell"
                    key={day.date}
                    data-date={day.date}
                    data-level={level}
                    tabIndex={day.date === activeDate ? 0 : -1}
                    aria-label={label}
                    aria-selected={selected}
                    title={label}
                    className={`vhist-heat-cell level-${level}${selected ? ' selected' : ''}`}
                    onClick={() => { setFocusDate(day.date); onSelectDate(day.date); }}
                    onFocus={() => setFocusDate(day.date)}
                  />
                );
              })}
            </div>
          ))}
        </div>
      </div>
      <div className="vhist-heatmap-footer">
        <span className="vhist-heatmap-total">
          {t('{count, plural, one {# commit} other {# commits}} in {{range}}', { count: activity.totalCommits, range: rangeLabel })}
        </span>
        <span className="vhist-heatmap-legend" aria-hidden="true">
          <span>{t('Less')}</span>
          {[0, 1, 2, 3, 4].map((level) => <span key={level} className={`vhist-heat-cell level-${level}`} />)}
          <span>{t('More')}</span>
        </span>
      </div>
    </div>
  );
}
