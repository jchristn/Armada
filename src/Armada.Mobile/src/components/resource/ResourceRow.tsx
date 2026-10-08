import { useState, type ReactElement, type ReactNode } from 'react';
import { StyleSheet, View } from 'react-native';
import { useLocale } from '../../i18n/LocaleContext';
import { spacing } from '../../theme/typography';
import { AppText } from '../ui/AppText';
import { ConfirmDialog } from '../ui/ConfirmDialog';
import { ListRow } from '../ui/ListRow';
import { StatusBadge, type StatusTone } from '../ui/StatusBadge';
import { SwipeRow, type SwipeAction } from '../ui/SwipeRow';

export interface ResourceRowProps {
  title: string;
  subtitle?: string | null;
  /** A status pill on the right (label always shown, so color is never the only signal). */
  badge?: { label: string; tone: StatusTone } | null;
  /** Small right-aligned text (relative time, count) when there is no badge, or under it. */
  meta?: string | null;
  onPress?: () => void;
  /** Swipe-left row actions (also screen-reader actions): the dashboard's row menu. */
  actions?: SwipeAction[];
  selected?: boolean;
  testID?: string;
}

/** One row of a resource list: title, subtitle, status badge, and the row actions behind a swipe. */
export function ResourceRow({ title, subtitle, badge, meta, onPress, actions = [], selected, testID }: ResourceRowProps) {
  const accessory: ReactNode = badge || meta ? (
    <View style={styles.accessory}>
      {badge ? <StatusBadge label={badge.label} tone={badge.tone} /> : null}
      {meta ? <AppText variant="caption" muted numberOfLines={1}>{meta}</AppText> : null}
    </View>
  ) : null;
  const row = <ListRow title={title} subtitle={subtitle} accessory={accessory} onPress={onPress} selected={selected} testID={testID} accessibilityValue={[badge?.label, meta]} />;
  if (actions.length === 0) return row;
  return <SwipeRow actions={actions} testID={testID ? `${testID}-swipe` : undefined}>{row}</SwipeRow>;
}

export interface ConfirmRequest {
  title: string;
  message: string;
  confirmLabel: string;
  danger?: boolean;
  /** The dashboard's typed delete (requireDeleteConfirm): pass 'delete'; the confirm button waits for the word. */
  typed?: 'delete';
  onConfirm: () => void | Promise<void>;
}

/**
 * One confirmation dialog per screen, opened with `confirm({...})` (the dashboard's ConfirmDialog state pattern).
 * Render `dialog` once in the screen.
 */
export function useConfirm(testID = 'confirm'): { confirm: (request: ConfirmRequest) => void; dialog: ReactElement } {
  const { t } = useLocale();
  const [request, setRequest] = useState<ConfirmRequest | null>(null);
  const dialog = (
    <ConfirmDialog
      open={request !== null}
      title={request?.title ?? ''}
      message={request?.message ?? ''}
      confirmLabel={request?.confirmLabel ?? t('Confirm')}
      cancelLabel={t('Cancel')}
      danger={request?.danger}
      typedConfirmation={request?.typed}
      typedLabel={request?.typed ? t('Type `delete` into the confirmation box to continue.') : undefined}
      onCancel={() => setRequest(null)}
      onConfirm={() => {
        const current = request;
        setRequest(null);
        if (current) void current.onConfirm();
      }}
      testID={testID}
    />
  );
  return { confirm: setRequest, dialog };
}

const styles = StyleSheet.create({
  accessory: { alignItems: 'flex-end', gap: spacing.xs, maxWidth: 160, flexShrink: 1 },
});
