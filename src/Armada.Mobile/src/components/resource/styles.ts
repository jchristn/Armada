import { StyleSheet } from 'react-native';
import { spacing } from '../../theme/typography';

/** Layout shared by the resource screens. */
export const resourceStyles = StyleSheet.create({
  /** A full-width create button under a list's summary cards. */
  create: { marginHorizontal: spacing.md, marginBottom: spacing.sm },
  /** A button inside an ActionBar (the bar sets the gaps). */
  action: { marginBottom: 0 },
  /** Horizontal padding for free content inside a detail body. */
  pad: { marginHorizontal: spacing.lg, marginBottom: spacing.md },
  row: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm, alignItems: 'center' },
});
