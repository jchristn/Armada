import { useRef, type ReactNode, type RefObject } from 'react';
import { ScrollView, StyleSheet, View, type HostInstance, type Text } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { formSheetFits, useLayout } from '../../navigation/useLayout';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing } from '../../theme/typography';
import { AppText } from './AppText';
import { IconButton } from './IconButton';
import { StickyFooter } from './StickyFooter';
import { ModalOverlay } from './ModalOverlay';
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
 * A modal sheet (the mobile form of the dashboard's modals): anchored to the bottom on phones and short windows (a
 * phone in landscape), and a centered form sheet on windows with room around it (iPad, Android tablets, foldables),
 * as iPadOS presents forms. Built on ModalOverlay: screen readers stay inside it and start on its title, Android back
 * closes it, and tapping the backdrop closes it. The backdrop appears at once and stays put; only the sheet slides up
 * (fades under Reduce Motion) and lifts over the keyboard. The sheet's content is one tree in both forms, so a
 * rotation keeps what was typed.
 */
export function BottomSheet({ open, title, onClose, closeLabel, children, footer, returnFocusRef, testID }: BottomSheetProps) {
  const { colors } = useTheme();
  const insets = useSafeAreaInsets();
  const titleRef = useRef<Text | null>(null);
  const onShow = useModalFocus(open, titleRef, returnFocusRef);
  const { width, height } = useLayout();
  const centered = formSheetFits(width, height);
  // Android back closes the keyboard first, then the sheet (one press used to drop the sheet and its text).
  const onBack = useModalBack(onClose);
  return (
    <ModalOverlay
      visible={open}
      kind="sheet"
      onRequestClose={onBack}
      onShow={onShow}
      onBackdropPress={onClose}
      backdropLabel={closeLabel}
      contentStyle={[centered ? styles.centeredHost : styles.bottomHost, { paddingLeft: insets.left, paddingRight: insets.right }]}
    >
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
          <AppText ref={titleRef} variant="heading" accessibilityRole="header" style={styles.title}>{title}</AppText>
          <IconButton icon="close" label={closeLabel} onPress={onClose} color="textMuted" testID={testID ? `${testID}-close` : undefined} />
        </View>
        <ScrollView keyboardShouldPersistTaps="handled" contentContainerStyle={styles.body}>{children}</ScrollView>
        {footer ? <StickyFooter inset="sheet" testID={testID ? `${testID}-footer` : undefined}>{footer}</StickyFooter> : null}
      </View>
    </ModalOverlay>
  );
}

const styles = StyleSheet.create({
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
