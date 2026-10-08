import { useRef, useState, type RefObject } from 'react';
import { StyleSheet, View, type HostInstance, type Text } from 'react-native';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing } from '../../theme/typography';
import { AppText } from './AppText';
import { Button } from './Button';
import { TextField } from './TextField';
import { useModalBack } from './useModalBack';
import { ModalOverlay } from './ModalOverlay';
import { useModalFocus } from './useModalFocus';

export interface ConfirmDialogProps {
  open: boolean;
  title: string;
  message: string;
  confirmLabel: string;
  cancelLabel: string;
  onConfirm: () => void;
  onCancel: () => void;
  /** Styles the confirm button as destructive. */
  danger?: boolean;
  /**
   * Typed confirmation (the dashboard's typed-delete): the confirm button stays disabled until the user types
   * exactly this word, e.g. 'delete'.
   */
  typedConfirmation?: string;
  /** Label of the typed-confirmation field (should name the word to type). */
  typedLabel?: string;
  /** The control that opened the dialog: screen-reader focus returns to it when the dialog closes. */
  returnFocusRef?: RefObject<HostInstance | null>;
  testID?: string;
}

/** A centered confirmation dialog, mirroring the dashboard's ConfirmDialog (including the typed-delete variant). */
export function ConfirmDialog(props: ConfirmDialogProps) {
  // Android back closes the keyboard (the typed confirmation) first, then the dialog.
  const onBack = useModalBack(props.onCancel);
  // Screen readers start on the title when the dialog appears (the card fades in over a backdrop that appears at once).
  const titleRef = useRef<Text | null>(null);
  const onShow = useModalFocus(props.open, titleRef, props.returnFocusRef);
  return (
    <ModalOverlay visible={props.open} kind="dialog" onRequestClose={onBack} onShow={onShow} contentStyle={styles.host}>
      {/* Mounted only while open, so the typed confirmation starts empty every time. */}
      {props.open ? <DialogCard {...props} titleRef={titleRef} /> : null}
    </ModalOverlay>
  );
}

function DialogCard({
  title, message, confirmLabel, cancelLabel, onConfirm, onCancel, danger, typedConfirmation, typedLabel, testID, titleRef,
}: ConfirmDialogProps & { titleRef: RefObject<Text | null> }) {
  const { colors } = useTheme();
  const [typed, setTyped] = useState('');
  const blocked = !!typedConfirmation && typed.trim() !== typedConfirmation;
  return (
    <View testID={testID} accessibilityViewIsModal style={[styles.card, { backgroundColor: colors.surfaceRaised, borderColor: colors.border }]}>
      <AppText ref={titleRef} variant="heading" accessibilityRole="header">{title}</AppText>
      <AppText muted>{message}</AppText>
      {typedConfirmation ? (
        <TextField
          testID={testID ? `${testID}-typed` : undefined}
          label={typedLabel ?? typedConfirmation}
          value={typed}
          onChangeText={setTyped}
          autoCapitalize="none"
          autoCorrect={false}
        />
      ) : null}
      <View style={styles.actions}>
        <Button label={cancelLabel} onPress={onCancel} variant="ghost" testID={testID ? `${testID}-cancel` : undefined} />
        <Button
          label={confirmLabel}
          onPress={onConfirm}
          variant={danger ? 'danger' : 'primary'}
          disabled={blocked}
          testID={testID ? `${testID}-confirm` : undefined}
        />
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  host: { alignItems: 'center', justifyContent: 'center', padding: spacing.xl },
  card: { width: '100%', maxWidth: 480, borderRadius: radius.lg, borderWidth: StyleSheet.hairlineWidth, padding: spacing.xl, gap: spacing.md },
  actions: { flexDirection: 'row', justifyContent: 'flex-end', flexWrap: 'wrap', gap: spacing.sm, marginTop: spacing.sm },
});
