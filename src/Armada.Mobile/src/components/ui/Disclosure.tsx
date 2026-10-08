import { useState, type ReactNode } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { MIN_TOUCH, spacing } from '../../theme/typography';
import { AppText } from './AppText';
import { Icon } from './Icon';

export interface DisclosureProps {
  title: string;
  children: ReactNode;
  /** Open on first render (the dashboard's `<details open>`). */
  initiallyOpen?: boolean;
  /** Controlled open state; when set, `onToggle` reports changes. */
  open?: boolean;
  onToggle?: (open: boolean) => void;
  testID?: string;
}

/** A collapsible section (the mobile form of the dashboard's `<details>`): a header button that shows or hides content. */
export function Disclosure({ title, children, initiallyOpen = false, open, onToggle, testID }: DisclosureProps) {
  const [ownOpen, setOwnOpen] = useState(initiallyOpen);
  const isOpen = open ?? ownOpen;
  const toggle = () => {
    if (open === undefined) setOwnOpen(!isOpen);
    onToggle?.(!isOpen);
  };
  return (
    <View>
      <Pressable
        testID={testID}
        accessibilityRole="button"
        accessibilityLabel={title}
        accessibilityState={{ expanded: isOpen }}
        onPress={toggle}
        style={styles.head}
      >
        <Icon name={isOpen ? 'chevron-down' : 'chevron-forward'} size={16} color="textMuted" />
        <AppText variant="caption" muted style={styles.title}>{title}</AppText>
      </Pressable>
      {isOpen ? <View style={styles.body}>{children}</View> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  head: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs, minHeight: MIN_TOUCH },
  title: { flex: 1, fontWeight: '600' },
  body: { paddingTop: spacing.xs },
});
