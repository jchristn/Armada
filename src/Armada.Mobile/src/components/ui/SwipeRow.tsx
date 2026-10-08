import { Children, cloneElement, isValidElement, useRef, type ReactElement, type ReactNode } from 'react';
import { Pressable, StyleSheet, View, type AccessibilityActionEvent, type AccessibilityActionInfo } from 'react-native';
import ReanimatedSwipeable, { type SwipeableMethods } from 'react-native-gesture-handler/ReanimatedSwipeable';
import { useTheme } from '../../theme/ThemeContext';
import { spacing } from '../../theme/typography';
import type { Palette } from '../../theme/palette';
import { AppText } from './AppText';
import { Icon, type IconName } from './Icon';

export interface SwipeAction {
  key: string;
  label: string;
  icon: IconName;
  onPress: () => void;
  tone?: 'primary' | 'danger';
}

/** The accessibility-action props a row element takes (Pressable, ListRow). */
export interface RowActionProps {
  accessibilityActions?: readonly AccessibilityActionInfo[];
  onAccessibilityAction?: (event: AccessibilityActionEvent) => void;
}

export interface SwipeRowProps {
  /** One row element (a ListRow or a Pressable): it receives the actions, since it is what screen readers focus. */
  children: ReactNode;
  actions: SwipeAction[];
  testID?: string;
}

/**
 * Swipe-left row actions (the mobile replacement for the dashboard's row menu). The same actions are exposed as
 * accessibility actions on the row element itself (VoiceOver's Actions rotor, TalkBack's actions menu), so screen
 * reader users reach them without the gesture. They must be on the focused element: actions on a wrapper View that
 * is not itself an accessibility element are never offered.
 */
export function SwipeRow({ children, actions, testID }: SwipeRowProps) {
  const { colors } = useTheme();
  const ref = useRef<SwipeableMethods | null>(null);

  const run = (action: SwipeAction) => {
    ref.current?.close();
    action.onPress();
  };

  const row = Children.only(children);
  const withActions = isValidElement<RowActionProps>(row)
    // eslint-disable-next-line react-hooks/refs -- the handler reads the swipeable's ref when an action runs, not during render
    ? cloneElement(row as ReactElement<RowActionProps>, {
      accessibilityActions: [...(row.props.accessibilityActions ?? []), ...actions.map((a) => ({ name: a.key, label: a.label }))],
      onAccessibilityAction: (event: AccessibilityActionEvent) => {
        const action = actions.find((a) => a.key === event.nativeEvent.actionName);
        if (action) run(action);
        else row.props.onAccessibilityAction?.(event);
      },
    })
    : row;

  return (
    <View testID={testID}>
      <ReanimatedSwipeable
        ref={ref}
        friction={2}
        rightThreshold={40}
        overshootRight={false}
        renderRightActions={() => (
          <View style={styles.actions}>
            {actions.map((action) => {
              const tone: keyof Palette = action.tone === 'danger' ? 'danger' : 'primary';
              const fg: keyof Palette = action.tone === 'danger' ? 'dangerText' : 'primaryText';
              return (
                <Pressable
                  key={action.key}
                  accessibilityRole="button"
                  accessibilityLabel={action.label}
                  onPress={() => run(action)}
                  style={[styles.action, { backgroundColor: colors[tone] }]}
                  testID={testID ? `${testID}-${action.key}` : undefined}
                >
                  <Icon name={action.icon} color={fg} />
                  <AppText variant="caption" color={fg}>{action.label}</AppText>
                </Pressable>
              );
            })}
          </View>
        )}
      >
        {withActions}
      </ReanimatedSwipeable>
    </View>
  );
}

const styles = StyleSheet.create({
  actions: { flexDirection: 'row' },
  action: { width: 84, alignItems: 'center', justifyContent: 'center', gap: spacing.xs, paddingHorizontal: spacing.xs },
});
