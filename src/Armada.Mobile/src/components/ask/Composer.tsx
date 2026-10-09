import { forwardRef, useEffect, useImperativeHandle, useMemo, useRef, useState } from 'react';
import { Pressable, ScrollView, StyleSheet, Switch, TextInput, View, useWindowDimensions, type LayoutChangeEvent } from 'react-native';
import type { AskQuickAction } from '@dashboard/types/models';
import { quickActionForm } from '@dashboard/lib/askQuickActions';
import {
  buildCommandCatalog,
  filterCommands,
  resolveSubmit,
  unknownCommandHint,
  type AskCommandItem,
  type AskCommandOutcome,
  type AskLocalCommand,
} from '@dashboard/lib/askCommands';
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
  /** Runs a local command (/new, /summarize, ...); `/help` is handled here. */
  onLocalCommand: (command: AskLocalCommand, args: string) => Promise<AskCommandOutcome>;
  actionBusy: boolean;
  onOpenImport: () => void;
  /** No captain is selected: plain messages cannot be sent, commands still work. */
  noCaptain: boolean;
  showThinking: boolean;
  onShowThinkingChange: (value: boolean) => void;
}

export interface ComposerResetOptions {
  /** Text to show (default empty). */
  text?: string;
  focus?: boolean;
}

export interface ComposerHandle {
  /** Open a quick action as if it had been chosen from the `/` menu. */
  choose: (action: AskQuickAction) => void;
  focus: () => void;
  /** The current draft. */
  getText: () => string;
  /** Replace the draft and close the menu, any open form, and the hint (a new or different conversation). */
  reset: (options?: ComposerResetOptions) => void;
}

interface Hint {
  text: string;
  params?: Record<string, string>;
}

/**
 * The message composer (the dashboard's AskComposer). Plain text goes to the thread's captain. Text starting with
 * `/` is a command (shared lib/askCommands): the menu lists matching quick actions and local commands with the first
 * one highlighted, Return or Send runs the highlighted entry or the exact command typed (with its arguments), and an
 * unknown command shows a hint and keeps the text. Quick actions with input open their form in a sheet.
 */
export const Composer = forwardRef<ComposerHandle, ComposerProps>(function Composer(props, ref) {
  const { quickActions, turnActive, stopping, onStop, onSend, onQuickAction, onLocalCommand, actionBusy, onOpenImport, noCaptain, showThinking, onShowThinkingChange } = props;
  const { t } = useLocale();
  const { colors } = useTheme();
  const [input, setInput] = useState('');
  const [menuDismissed, setMenuDismissed] = useState(false);
  const [openForm, setOpenForm] = useState<AskQuickAction | null>(null);
  const [hint, setHint] = useState<Hint | null>(null);
  const inputRef = useRef<TextInput>(null);
  // The message box and the Send (or Stop) button share one resting height: each is measured at its natural single-line
  // size and both are drawn at the larger of the two, so they match at every text size. A typed message can still grow
  // the box past it. Measurements restart when the text size changes, so a smaller size can shrink them again.
  const { fontScale } = useWindowDimensions();
  const [buttonHeight, setButtonHeight] = useState(0);
  const [emptyInputHeight, setEmptyInputHeight] = useState(0);
  useEffect(() => { setButtonHeight(0); setEmptyInputHeight(0); }, [fontScale]);
  const restingHeight = Math.max(MIN_TOUCH, buttonHeight, emptyInputHeight);

  const catalog = useMemo(() => buildCommandCatalog(quickActions), [quickActions]);
  const matches = useMemo(() => filterCommands(catalog, input), [catalog, input]);
  const menuOpen = !menuDismissed && !openForm && matches.length > 0;
  // Touch has no arrow keys: the first match is highlighted and Return runs it, as on the dashboard.
  const highlighted = menuOpen ? matches[0] : null;
  const pending = resolveSubmit(catalog, input, highlighted);
  const formKind = openForm ? quickActionForm(openForm) : null;

  function setText(text: string) {
    setInput(text);
    setMenuDismissed(false);
  }

  function reset(options?: ComposerResetOptions) {
    setText(options?.text ?? '');
    setOpenForm(null);
    setHint(null);
    if (options?.focus) inputRef.current?.focus();
  }

  async function choose(action: AskQuickAction) {
    setText('');
    setHint(null);
    const kind = quickActionForm(action);
    if (kind === 'import') { onOpenImport(); return; }
    if (kind === 'none') { await onQuickAction(action, {}); return; }
    setOpenForm(action);
  }

  async function runItem(item: AskCommandItem, args: string) {
    if (item.action) { await choose(item.action); return; }
    const local = item.local;
    if (!local) return;
    if (local.name === 'help') { reset({ text: '/', focus: true }); return; }
    if (local.requiresArgs && !args) { reset({ text: `${local.command} `, focus: true }); return; }
    // A new conversation must not inherit the command as the old conversation's saved draft.
    if (local.name === 'new') setText('');
    const outcome = await onLocalCommand(local, args);
    if (outcome.ok) setText('');
    setHint(outcome.hint ? { text: outcome.hint, params: outcome.hintParams } : null);
  }

  useImperativeHandle(ref, () => ({
    choose: (action: AskQuickAction) => { void choose(action); },
    focus: () => inputRef.current?.focus(),
    getText: () => input,
    reset,
  }));

  async function submitForm(args: Record<string, unknown>) {
    if (!openForm) return;
    const ok = await onQuickAction(openForm, args);
    if (ok) setOpenForm(null);
  }

  const trimmed = input.trim();
  const canSendText = pending.kind === 'text' && !!trimmed && !noCaptain && !turnActive;
  const canSubmit = pending.kind === 'command' || canSendText;

  function submit() {
    if (pending.kind === 'command') { void runItem(pending.item, pending.args); return; }
    if (pending.kind === 'unknown') {
      const outcome = unknownCommandHint(pending.command);
      setHint({ text: outcome.hint ?? '', params: outcome.hintParams });
      return;
    }
    if (!canSendText) return;
    onSend(trimmed);
    setText('');
    setHint(null);
  }

  function measureInput(event: LayoutChangeEvent) {
    // Only an empty box gives the resting height; a typed message may be several lines.
    if (input.length === 0) setEmptyInputHeight(Math.ceil(event.nativeEvent.layout.height));
  }

  function measureButton(event: LayoutChangeEvent) {
    // Read the height now: the updater runs after the handler returns, when React Native has already released the
    // event (its nativeEvent is null then, which crashed the Release build on open).
    const height = Math.ceil(event.nativeEvent.layout.height);
    setButtonHeight((current) => Math.max(current, height));
  }

  const placeholder = noCaptain
    ? t('Choose a captain to chat, or type / for commands')
    : t('Message the captain, or type / for commands');

  return (
    <View style={[styles.wrap, { borderTopColor: colors.border, backgroundColor: colors.surface }]} testID="ask-composer">
      {menuOpen ? (
        <View style={[styles.menu, { borderColor: colors.border, backgroundColor: colors.surfaceRaised }]} accessibilityLabel={t('Commands')} testID="ask-quick-menu">
          <AppText variant="caption" muted style={styles.menuHead}>{t('Commands')}</AppText>
          <ScrollView style={styles.menuList} keyboardShouldPersistTaps="handled" nestedScrollEnabled>
            {matches.map((item) => (
              <Pressable
                key={item.key}
                testID={`ask-quick-${item.local ? item.local.name : item.action?.name}`}
                accessibilityRole="button"
                accessibilityLabel={[`${item.command}${item.usage ? ` ${item.usage}` : ''}`, t(item.title), item.description ? t(item.description) : ''].filter(Boolean).join(', ')}
                accessibilityState={{ selected: item === highlighted }}
                onPress={() => void runItem(item, '')}
                style={({ pressed }) => [styles.option, { borderLeftColor: item === highlighted ? colors.primary : colors.surfaceRaised, opacity: pressed ? 0.6 : 1 }]}
              >
                <AppText variant="mono" color="primary">{item.command}{item.usage ? ` ${item.usage}` : ''}</AppText>
                <View style={styles.flex}>
                  <AppText variant="label">{t(item.title)}</AppText>
                  {item.description ? <AppText variant="caption" muted numberOfLines={2}>{t(item.description)}</AppText> : null}
                </View>
              </Pressable>
            ))}
          </ScrollView>
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
            // Return (on-screen or a hardware keyboard) sends, as the Send button does, and keeps the keyboard up.
            returnKeyType="send"
            submitBehavior="submit"
            onSubmitEditing={submit}
            onChangeText={(value) => { setText(value); setHint(null); }}
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
          {turnActive && pending.kind !== 'command' ? (
            <Button label={stopping ? t('Stopping...') : t('Stop')} variant="secondary" onPress={onStop} disabled={stopping} testID="ask-stop" style={[styles.sendButton, { minHeight: restingHeight }]} />
          ) : (
            <Button label={t('Send')} onPress={submit} disabled={!canSubmit} testID="ask-send" style={[styles.sendButton, { minHeight: restingHeight }]} />
          )}
        </View>
      </View>

      {hint ? (
        <AppText variant="caption" muted accessibilityLiveRegion="polite" accessibilityRole="alert" testID="ask-composer-hint" style={styles.hint}>
          {t(hint.text, hint.params)}
        </AppText>
      ) : null}

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
          onPress={() => reset({ text: '/', focus: true })}
          style={styles.link}
        >
          {t('Commands')}
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
  menuList: { maxHeight: 280 },
  hint: { marginTop: spacing.xs },
  // The highlighted entry (what Return runs) has a primary bar on its left edge.
  option: { flexDirection: 'row', alignItems: 'center', gap: spacing.md, minHeight: MIN_TOUCH, paddingVertical: spacing.xs, paddingLeft: spacing.xs, borderLeftWidth: 3 },
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
