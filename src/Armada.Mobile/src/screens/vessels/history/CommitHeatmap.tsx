import { useMemo, useRef } from 'react';
import { Pressable, ScrollView, StyleSheet, View } from 'react-native';
import type { VesselCommitActivity } from '@dashboard/types/models';
import { buildMonthLabels, buildWeeks, formatIsoDay, heatLevel, HEAT_LEVELS } from '@dashboard/lib/vesselHistory';
import { AppText } from '../../../components/ui';
import { useLocale } from '../../../i18n/LocaleContext';
import { useTheme } from '../../../theme/ThemeContext';
import { CHROME_MAX_FONT_SCALE, spacing } from '../../../theme/typography';

/** Cell size and gap in dp: small like the dashboard's, with the selected day outlined. */
const CELL = 15;
const GAP = 3;
const COLUMN = CELL + GAP;
const WEEKDAY_REFERENCE_SUNDAY = Date.UTC(2023, 0, 1);

/** Fill opacity per heat level 1..4 (level 0 is the empty-day color). */
const LEVEL_OPACITY = [0, 0.3, 0.5, 0.75, 1];

export interface CommitHeatmapProps {
  activity: VesselCommitActivity;
  selectedDate: string | null;
  onSelectDate: (date: string) => void;
  /** Already translated range ("the last year" or "2024"). */
  rangeLabel: string;
}

/**
 * The commit-activity heatmap (the dashboard's CommitHeatmap): one column per week (Sunday..Saturday), five levels
 * scaled by the busiest day, month labels, scrolled to the newest week. Each day is a button that jumps the timeline
 * to it; screen readers hear "3 commits on Tue, Oct 6, 2026".
 */
export function CommitHeatmap({ activity, selectedDate, onSelectDate, rangeLabel }: CommitHeatmapProps) {
  const { t, locale } = useLocale();
  const { colors } = useTheme();
  const scrollRef = useRef<ScrollView | null>(null);
  const weeks = useMemo(() => buildWeeks(activity.days), [activity.days]);
  const months = useMemo(() => buildMonthLabels(weeks), [weeks]);
  const monthFormatter = useMemo(() => new Intl.DateTimeFormat(locale, { month: 'short', timeZone: 'UTC' }), [locale]);
  const weekdays = useMemo(() => {
    const short = new Intl.DateTimeFormat(locale, { weekday: 'short', timeZone: 'UTC' });
    return Array.from({ length: 7 }, (_, i) => short.format(new Date(WEEKDAY_REFERENCE_SUNDAY + i * 86400000)));
  }, [locale]);

  const fill = (level: number) => (level === 0 ? colors.border : colors.success);
  const describe = (date: string, count: number) =>
    t('{count, plural, =0 {No commits} one {# commit} other {# commits}} on {{date}}', {
      count,
      date: formatIsoDay(locale, date, { weekday: 'short', year: 'numeric', month: 'short', day: 'numeric' }),
    });

  return (
    <View testID="vessel-heatmap">
      <View style={styles.grid}>
        <View style={styles.weekdays} importantForAccessibility="no-hide-descendants">
          <View style={{ height: 16 }} />
          {weekdays.map((name, row) => (
            <AppText key={name} variant="caption" muted maxFontSizeMultiplier={CHROME_MAX_FONT_SCALE} style={[styles.weekday, row % 2 === 0 ? styles.quiet : null]}>
              {name}
            </AppText>
          ))}
        </View>
        <ScrollView
          horizontal
          ref={scrollRef}
          onContentSizeChange={() => scrollRef.current?.scrollToEnd({ animated: false })}
          showsHorizontalScrollIndicator={false}
          accessibilityLabel={t('Commit activity')}
        >
          <View>
            <View style={[styles.months, { width: weeks.length * COLUMN }]} importantForAccessibility="no-hide-descendants">
              {months.map((m) => (
                <AppText
                  key={`${m.year}-${m.month}-${m.column}`}
                  variant="caption"
                  muted
                  maxFontSizeMultiplier={CHROME_MAX_FONT_SCALE}
                  style={[styles.month, { left: m.column * COLUMN }]}
                >
                  {monthFormatter.format(new Date(Date.UTC(m.year, m.month - 1, 1)))}
                </AppText>
              ))}
            </View>
            <View style={styles.columns}>
              {weeks.map((week, column) => (
                <View key={column} style={styles.column}>
                  {week.map((day, row) => {
                    if (!day) return <View key={`pad-${row}`} style={styles.cell} />;
                    const level = heatLevel(day.count, activity.maxDayCount);
                    const selected = day.date === selectedDate;
                    return (
                      <Pressable
                        key={day.date}
                        testID={`heat-${day.date}`}
                        accessibilityRole="button"
                        accessibilityLabel={describe(day.date, day.count)}
                        accessibilityState={{ selected }}
                        onPress={() => onSelectDate(day.date)}
                        style={[
                          styles.cell,
                          { backgroundColor: fill(level), opacity: level === 0 ? 1 : LEVEL_OPACITY[level] },
                          selected ? { borderWidth: 2, borderColor: colors.text } : null,
                        ]}
                      />
                    );
                  })}
                </View>
              ))}
            </View>
          </View>
        </ScrollView>
      </View>
      <View style={styles.footer}>
        <AppText variant="caption" muted style={styles.total} testID="vessel-heatmap-total">
          {t('{count, plural, one {# commit} other {# commits}} in {{range}}', { count: activity.totalCommits, range: rangeLabel })}
        </AppText>
        <View style={styles.legend} importantForAccessibility="no-hide-descendants">
          <AppText variant="caption" muted>{t('Less')}</AppText>
          {Array.from({ length: HEAT_LEVELS }, (_, level) => (
            <View key={level} style={[styles.cell, { backgroundColor: fill(level), opacity: level === 0 ? 1 : LEVEL_OPACITY[level] }]} />
          ))}
          <AppText variant="caption" muted>{t('More')}</AppText>
        </View>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  grid: { flexDirection: 'row' },
  weekdays: { marginRight: spacing.xs },
  weekday: { height: COLUMN, lineHeight: COLUMN, fontSize: 10 },
  quiet: { opacity: 0 },
  months: { height: 16 },
  month: { position: 'absolute', top: 0, fontSize: 10, lineHeight: 14 },
  columns: { flexDirection: 'row' },
  column: { marginRight: GAP },
  cell: { width: CELL, height: CELL, borderRadius: 3, marginBottom: GAP },
  footer: { flexDirection: 'row', flexWrap: 'wrap', alignItems: 'center', gap: spacing.sm, marginTop: spacing.sm },
  total: { flex: 1, minWidth: 160 },
  legend: { flexDirection: 'row', alignItems: 'center', gap: 3 },
});
