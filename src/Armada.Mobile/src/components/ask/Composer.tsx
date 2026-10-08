import { forwardRef, useEffect, useImperativeHandle, useMemo, useRef, useState } from 'react';
import { Pressable, StyleSheet, Switch, TextInput, View, useWindowDimensions, type LayoutChangeEvent } from 'react-native';
import type { AskQuickAction } from '@dashboard/types/models';
import { filterQuickActions, quickActionForm } from '@dashboard/lib/askQuickActions';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { MIN_TOUCH, radius, spacing, typography } from '../../theme/typography';
import { AppText } from '../ui/AppText';
import { BottomSheet } from '../ui/BottomSheet';
import { Button } from '../ui/Button';
import { DispatchForm } from './DispatchForm';
import { FleetActionForm } from './FleetActionForm';

export interface ComposerProps {
  quickActions: AskQuickAction[];
  /** A captain turn is running (Stop instead of Send). */
  turnActive: boolean;
  stopping: boolean;
  onStop: () => void;
  onSend: (text: string) => void;
  /** Runs a quick action; resolves true when it succeeded so the form can close. */
  onQuickAction: (action: AskQuickAction, args: Record<string, unknown>) => Promise<boolean>;
  actionBusy: boolean;
  onOpenImport: () => void;
  /** No captain is selected: plain messages cannot be sent, quick actions still work. */
  noCaptain: boolean;
  showThinking: boolean;
  onShowThinkingChange: (value: boolean) => void;
}

export interface ComposerHandle {
  /** Open a quick action as if it had been chosen from the `/` menu. */
  choose: (action: AskQuickAction) => void;
  focus: () => void;
}

/**
 * The message composer (the dashboard's AskComposer). Plain text goes to the thread's captain; `/` opens the
 * quick-action menu, and each quick action opens its form in a sheet (or runs immediately when it needs no input).
 */
export const Composer = forwardRef<ComposerHandle, ComposerProps>(function Composer(props, ref) {
  const { quickActions, turnActive, stopping, onStop, onSend, onQuickAction, actionBusy, onOpenImport, noCaptain, showThinking, onShowThinkingChange } = props;
  const { t } = useLocale();
  const { colors } = useTheme();
  const [input, setInput] = useState('');
  const [menuDismissed, setMenuDismissed] = useState(false);
  const [openForm, setOpenForm] = useState<AskQuickAction | null>(null);
  const inputRef = useRef<TextInput>(null);
  // The message box and the Send (or Stop) button share one resting height: each is measured at its natural single-line
  // size and both are drawn at the larger of the two, so they match at every text size. A typed message can still grow
  // the box past it. Measurements restart when the text size changes, so a smaller size can shrink them again.
  const { fontScale } = useWindowDimensions();
  const [buttonHeight, setButtonHeight] = useState(0);
  const [emptyInputHeight, setEmptyInputHeight] = useState(0);
  useEffect(() => { setButtonHeight(0); setEmptyInputHeight(0); }, [fontScale]);
  const restingHeight = Math.max(MIN_TOUCH, buttonHeight, emptyInputHeight);

  const matches = useMemo(() => filterQuickActions(quickActions, input), [quickActions, input]);
  const menuOpen = !menuDismissed && !openForm && matches.length > 0;
  const formKind = openForm ? quickActionForm(openForm) : null;

  async function choose(action: AskQuickAction) {
    setInput('');
    const kind = quickActionForm(action);
    if (kind === 'import') { onOpenImport(); return; }
    if (kind === 'none') { await onQuickAction(action, {}); return; }
    setOpenForm(action);
  }

  useImperativeHandle(ref, () => ({
    choose: (action: AskQuickAction) => { void choose(action); },
    focus: () => inputRef.current?.focus(),
  }));

  async function submitForm(args: Record<string, unknown>) {
    if (!openForm) return;
    const ok = await onQuickAction(openForm, args);
    if (ok) setOpenForm(null);
  }

  const trimmed = input.trim();
  const canSend = !!trimmed && !noCaptain && !trimmed.startsWith('/') && !turnActive;

  function send() {
    if (!canSend) return;
    onSend(trimmed);
    setInput('');
  }

  function measureInput(event: LayoutChangeEvent) {
    // Only an empty box gives the resting height; a typed message may be several lines.
    if (input.length === 0) setEmptyInputHeight(Math.ceil(event.nativeEvent.layout.height));
  }

  function measureButton(event: LayoutChangeEvent) {
    setButtonHeight((current) => Math.max(current, Math.ceil(event.nativeEvent.layout.height)));
  }

  const placeholder = noCaptain
    ? t('Choose a captain to chat, or type / for quick actions')
    : t('Message the captain, or type / for quick actions');

  return (
    <View style={[styles.wrap, { borderTopColor: colors.border, backgroundColor: colors.surface }]} testID="ask-composer">
      {menuOpen ? (
        <View style={[styles.menu, { borderColor: colors.border, backgroundColor: colors.surfaceRaised }]} accessibilityLabel={t('Quick actions')} testID="ask-quick-menu">
          <AppText variant="caption" muted style={styles.menuHead}>{t('Quick actions')}</AppText>
          {matches.map((action) => (
            <Pressable
              key={action.name}
              testID={`ask-quick-${action.name}`}
              accessibilityRole="button"
              accessibilityLabel={`${action.command || `/${action.name}`}, ${action.title ? t(action.title) : action.name}`}
              accessibilityHint={action.description ? t(action.description) : undefined}
              onPress={() => void choose(action)}
              style={({ pressed }) => [styles.option, { opacity: pressed ? 0.6 : 1 }]}
            >
              <AppText variant="mono" color="primary">{action.command || `/${action.name}`}</AppText>
              <View style={styles.flex}>
                <AppText variant="label">{action.title ? t(action.title) : action.name}</AppText>
                {action.description ? <AppText variant="caption" muted numberOfLines={2}>{t(action.description)}</AppText> : null}
              </View>
            </Pressable>
          ))}
          <Button label={t('Close')} variant="ghost" onPress={() => setMenuDismissed(true)} />
        </View>
      ) : null}

      <View style={styles.row}>
        <View style={styles.inputWrap}>
          <TextInput
            ref={inputRef}
            testID="ask-input"
            value={input}
            multiline
            accessibilityLabel={t('Message')}
            accessibilityHint={placeholder}
            onChangeText={(value) => { setInput(value); setMenuDismissed(false); }}
            onLayout={measureInput}
            style={[styles.input, typography.body, { minHeight: restingHeight, color: colors.text, borderColor: colors.control, backgroundColor: colors.background }]}
          />
          {input.length === 0 ? (
            // A native placeholder wraps and makes an empty multiline box taller than the button; this one stays on one
            // line. Screen readers hear it as the box's hint.
            <AppText
              testID="ask-input-placeholder"
              muted
              numberOfLines={1}
              pointerEvents="none"
              importantForAccessibility="no-hide-descendants"
              accessibilityElementsHidden
              style={styles.placeholder}
            >
              {placeholder}
            </AppText>
          ) : null}
        </View>
        <View onLayout={measureButton} testID="ask-send-wrap">
          {turnActive ? (
            <Button label={stopping ? t('Stopping...') : t('Stop')} variant="secondary" onPress={onStop} disabled={stopping} testID="ask-stop" style={[styles.sendButton, { minHeight: restingHeight }]} />
          ) : (
            <Button label={t('Send')} onPress={send} disabled={!canSend} testID="ask-send" style={[styles.sendButton, { minHeight: restingHeight }]} />
          )}
        </View>
      </View>

      <View style={styles.foot}>
        <View style={styles.toggle}>
          <Switch
            testID="ask-show-thinking"
            value={showThinking}
            onValueChange={onShowThinkingChange}
            accessibilityLabel={t('Show thinking')}
            accessibilityHint={t('Ask the captain to include its reasoning, shown collapsed above each reply')}
            trackColor={{ true: colors.primary, false: colors.control }}
          />
          <AppText variant="caption" muted>{t('Show thinking')}</AppText>
        </View>
        <AppText
          variant="caption"
          color="primary"
          accessibilityRole="button"
          testID="ask-quick-actions"
          onPress={() => { setInput('/'); setMenuDismissed(false); inputRef.current?.focus(); }}
          style={styles.link}
        >
          {t('Quick actions')}
        </AppText>
      </View>
      <AppText variant="caption" muted style={styles.disclaimer}>{t('AI can make mistakes. Check answers.')}</AppText>

      <BottomSheet
        open={!!openForm && formKind === 'dispatch'}
        title={t('Dispatch a voyage')}
        onClose={() => { if (!actionBusy) setOpenForm(null); }}
        closeLabel={t('Cancel')}
        testID="ask-dispatch-sheet"
      >
        <DispatchForm busy={actionBusy} onSubmit={(args) => void submitForm(args)} onCancel={() => setOpenForm(null)} />
      </BottomSheet>
      <BottomSheet
        open={!!openForm && formKind === 'fleet-action'}
        title={t('Run a fleet action')}
        onClose={() => { if (!actionBusy) setOpenForm(null); }}
        closeLabel={t('Cancel')}
        testID="ask-fleet-action-sheet"
      >
        <FleetActionForm busy={actionBusy} onSubmit={(args) => void submitForm(args)} onCancel={() => setOpenForm(null)} />
      </BottomSheet>
    </View>
  );
});

const styles = StyleSheet.create({
  wrap: { borderTopWidth: StyleSheet.hairlineWidth, paddingHorizontal: spacing.md, paddingTop: spacing.sm, paddingBottom: spacing.xs },
  menu: { borderWidth: 1, borderRadius: radius.md, marginBottom: spacing.sm, paddingHorizontal: spacing.sm },
  menuHead: { paddingTop: spacing.sm },
  option: { flexDirection: 'row', alignItems: 'center', gap: spacing.md, minHeight: MIN_TOUCH, paddingVertical: spacing.xs },
  flex: { flex: 1 },
  row: { flexDirection: 'row', alignItems: 'flex-end', gap: spacing.sm },
  inputWrap: { flex: 1, justifyContent: 'center' },
  placeholder: { position: 'absolute', left: spacing.md + 1, right: spacing.md + 1, top: spacing.sm + 1 },
  input: { maxHeight: 160, borderWidth: 1, borderRadius: radius.md, paddingHorizontal: spacing.md, paddingTop: spacing.sm, paddingBottom: spacing.sm },
  sendButton: { marginBottom: 0 },
  foot: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', marginTop: spacing.xs },
  toggle: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  link: { paddingVertical: spacing.sm, paddingHorizontal: spacing.xs },
  disclaimer: { textAlign: 'center' },
});
