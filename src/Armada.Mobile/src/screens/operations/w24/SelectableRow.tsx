import type { ReactNode } from 'react';
import { ListRow, SwipeRow, type SwipeAction } from '../../../components/ui';
import { Icon } from '../../../components/ui/Icon';

/**
 * A list row with swipe actions (the dashboard's row menu) and bulk selection: long press starts selecting, and
 * while selecting a tap toggles the row instead of opening it.
 */
export function SelectableRow({ id, title, subtitle, accessory, accessibilityValue, actions, selecting, checked, highlighted, onOpen, onToggle, onStartSelect, testID }: {
  id: string;
  title: string;
  subtitle?: string | null;
  accessory?: ReactNode;
  /** What the accessory shows (the status), read with the row. */
  accessibilityValue?: string | null;
  actions: SwipeAction[];
  selecting: boolean;
  checked: boolean;
  highlighted?: boolean;
  onOpen: (id: string) => void;
  onToggle: (id: string) => void;
  onStartSelect: (id: string) => void;
  testID: string;
}) {
  const row = (
    <ListRow
      testID={testID}
      title={title}
      subtitle={subtitle}
      accessory={
        selecting
          ? <Icon name={checked ? 'checkbox' : 'square-outline'} color={checked ? 'primary' : 'textMuted'} />
          : accessory
      }
      accessibilityValue={selecting ? undefined : accessibilityValue ?? undefined}
      selected={checked || highlighted}
      onPress={() => (selecting ? onToggle(id) : onOpen(id))}
      onLongPress={() => onStartSelect(id)}
    />
  );
  if (selecting) return row;
  return <SwipeRow actions={actions} testID={`${testID}-swipe`}>{row}</SwipeRow>;
}
