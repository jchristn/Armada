import type { ReactNode } from 'react';
import { KeyboardAvoidingView, Modal, Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { formSheetFits, useLayout } from '../../navigation/useLayout';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing } from '../../theme/typography';
import { AppText } from './AppText';
import { IconButton } from './IconButton';
import { StickyFooter } from './StickyFooter';
import { useModalBack } from './useModalBack';
import { MODAL_ORIENTATIONS } from './modalOrientations';

export interface BottomSheetProps {
  open: boolean;
  title: string;
  onClose: () => void;
  closeLabel: string;
  children: ReactNode;
  /** The form's actions, kept below the scrolling body so a long form's Save is reachable without scrolling. */
  footer?: ReactNode;
  testID?: string;
}

/**
 * A modal sheet (the mobile form of the dashboard's modals): anchored to the bottom on phones and short windows (a
 * phone in landscape), and a centered form sheet on windows with room around it (iPad, Android tablets, foldables),
 * as iPadOS presents forms. Built on the platform Modal: screen readers stay inside it, Android back closes it, and
 * the backdrop closes it. The sheet's content is one tree in both forms, so a rotation keeps what was typed.
 */
export function BottomSheet({ open, title, onClose, closeLabel, children, footer, testID }: BottomSheetProps) {
  const { colors } = useTheme();
  const insets = useSafeAreaInsets();
  const { width, height } = useLayout();
  const centered = formSheetFits(width, height);
  // Android back closes the keyboard first, then the sheet (one press used to drop the sheet and its text).
  const onBack = useModalBack(onClose);
  return (
    <Modal supportedOrientations={MODAL_ORIENTATIONS} visible={open} transparent animationType="slide" onRequestClose={onBack} statusBarTranslucent>
      {/* Padding on both platforms: with Android edge-to-edge the window no longer resizes for the keyboard. */}
      <KeyboardAvoidingView
        style={[styles.fill, centered ? styles.centeredHost : styles.bottomHost, { paddingLeft: insets.left, paddingRight: insets.right }]}
        behavior="padding"
      >
        <Pressable style={[StyleSheet.absoluteFill, { backgroundColor: colors.overlay }]} onPress={onClose} accessibilityRole="button" accessibilityLabel={closeLabel} />
        <View
          testID={testID}
          accessibilityViewIsModal
          style={[
            styles.sheet,
            centered ? styles.formSheet : styles.bottomSheet,
            { backgroundColor: colors.surface, borderColor: colors.border, paddingBottom: centered ? spacing.lg : insets.bottom + spacing.lg },
          ]}
        >
          {centered ? null : <View style={[styles.grabber, { backgroundColor: colors.border }]} />}
          <View style={styles.header}>
            <AppText variant="heading" accessibilityRole="header" style={styles.title}>{title}</AppText>
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
  bottomHost: { justifyContent: 'flex-end' },
  centeredHost: { justifyContent: 'center', padding: spacing.xl },
  sheet: {
    width: '100%',
    alignSelf: 'center',
    borderWidth: StyleSheet.hairlineWidth,
  },
  bottomSheet: { maxHeight: '88%', maxWidth: 640, borderTopLeftRadius: radius.lg, borderTopRightRadius: radius.lg },
  formSheet: { maxHeight: '85%', maxWidth: 600, borderRadius: radius.lg, paddingTop: spacing.sm },
  grabber: { width: 40, height: 5, borderRadius: 3, alignSelf: 'center', marginTop: spacing.sm },
  header: { flexDirection: 'row', alignItems: 'center', paddingLeft: spacing.lg, paddingRight: spacing.xs },
  title: { flex: 1 },
  body: { paddingHorizontal: spacing.lg, paddingBottom: spacing.lg },
});
