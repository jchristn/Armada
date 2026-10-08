import { useRef, type ReactNode, type RefObject } from 'react';
import { KeyboardAvoidingView, Modal, Pressable, ScrollView, StyleSheet, View, type HostInstance, type Text } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useReducedMotion } from '../../lib/accessibility';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing } from '../../theme/typography';
import { AppText } from './AppText';
import { IconButton } from './IconButton';
import { StickyFooter } from './StickyFooter';
import { useModalBack } from './useModalBack';
import { useModalFocus } from './useModalFocus';

export interface BottomSheetProps {
  open: boolean;
  title: string;
  onClose: () => void;
  closeLabel: string;
  children: ReactNode;
  /** The form's actions, kept below the scrolling body so a long form's Save is reachable without scrolling. */
  footer?: ReactNode;
  /** The control that opened the sheet: screen-reader focus returns to it when the sheet closes. */
  returnFocusRef?: RefObject<HostInstance | null>;
  testID?: string;
}

/**
 * A modal sheet anchored to the bottom (the mobile form of the dashboard's modals). Built on the platform Modal:
 * screen readers stay inside it and start on its title, Android back closes it, and the backdrop closes it. It
 * fades instead of sliding when the system asks for reduced motion.
 */
export function BottomSheet({ open, title, onClose, closeLabel, children, footer, returnFocusRef, testID }: BottomSheetProps) {
  const { colors } = useTheme();
  const insets = useSafeAreaInsets();
  const reduceMotion = useReducedMotion();
  const titleRef = useRef<Text | null>(null);
  const onShow = useModalFocus(open, titleRef, returnFocusRef);
  // Android back closes the keyboard first, then the sheet (one press used to drop the sheet and its text).
  const onBack = useModalBack(onClose);
  return (
    <Modal visible={open} transparent animationType={reduceMotion ? 'fade' : 'slide'} onRequestClose={onBack} onShow={onShow} statusBarTranslucent>
      {/* Padding on both platforms: with Android edge-to-edge the window no longer resizes for the keyboard. */}
      <KeyboardAvoidingView style={styles.fill} behavior="padding">
        <Pressable style={[styles.fill, { backgroundColor: colors.overlay }]} onPress={onClose} accessibilityRole="button" accessibilityLabel={closeLabel} />
        <View
          testID={testID}
          accessibilityViewIsModal
          style={[styles.sheet, { backgroundColor: colors.surface, borderColor: colors.border, paddingBottom: insets.bottom + spacing.lg }]}
        >
          <View style={[styles.grabber, { backgroundColor: colors.border }]} />
          <View style={styles.header}>
            <AppText ref={titleRef} variant="heading" accessibilityRole="header" style={styles.title}>{title}</AppText>
            <IconButton icon="close" label={closeLabel} onPress={onClose} color="textMuted" testID={testID ? `${testID}-close` : undefined} />
          </View>
          <ScrollView keyboardShouldPersistTaps="handled" contentContainerStyle={styles.body}>{children}</ScrollView>
          {footer ? <StickyFooter inset="sheet" testID={testID ? `${testID}-footer` : undefined}>{footer}</StickyFooter> : null}
        </View>
      </KeyboardAvoidingView>
    </Modal>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  sheet: {
    maxHeight: '88%',
    width: '100%',
    maxWidth: 640,
    alignSelf: 'center',
    borderTopLeftRadius: radius.lg,
    borderTopRightRadius: radius.lg,
    borderWidth: StyleSheet.hairlineWidth,
  },
  grabber: { width: 40, height: 5, borderRadius: 3, alignSelf: 'center', marginTop: spacing.sm },
  header: { flexDirection: 'row', alignItems: 'center', paddingLeft: spacing.lg, paddingRight: spacing.xs },
  title: { flex: 1 },
  body: { paddingHorizontal: spacing.lg, paddingBottom: spacing.lg },
});
