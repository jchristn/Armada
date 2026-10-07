import { useRef, type ReactNode } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
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

export interface SwipeRowProps {
  children: ReactNode;
  actions: SwipeAction[];
  testID?: string;
}

/**
 * Swipe-left row actions (the mobile replacement for the dashboard's row menu). The same actions are exposed as
 * accessibility actions, so VoiceOver and TalkBack users reach them without the gesture.
 */
export function SwipeRow({ children, actions, testID }: SwipeRowProps) {
  const { colors } = useTheme();
  const ref = useRef<SwipeableMethods | null>(null);

  const run = (action: SwipeAction) => {
    ref.current?.close();
    action.onPress();
  };

  return (
    <View
      testID={testID}
      accessibilityActions={actions.map((a) => ({ name: a.key, label: a.label }))}
      onAccessibilityAction={(event) => {
        const action = actions.find((a) => a.key === event.nativeEvent.actionName);
        if (action) run(action);
      }}
    >
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
        {children}
      </ReanimatedSwipeable>
    </View>
  );
}

const styles = StyleSheet.create({
  actions: { flexDirection: 'row' },
  action: { width: 84, alignItems: 'center', justifyContent: 'center', gap: spacing.xs, paddingHorizontal: spacing.xs },
});
