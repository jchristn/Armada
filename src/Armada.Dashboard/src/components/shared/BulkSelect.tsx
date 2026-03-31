interface BulkSelectProps {
  /** All item IDs on the current page */
  allIds: string[];
  /** Currently selected IDs */
  selectedIds: string[];
  /** Toggle selection of one item */
  onToggle: (id: string) => void;
  /** Select all items */
  onSelectAll: () => void;
  /** Clear selection */
  onClearSelection: () => void;
  /** Bulk action buttons to render when items are selected */
  actions?: { label: string; onClick: () => void; danger?: boolean }[];
}

export function BulkSelectCheckbox({
  id,
  selectedIds,
  onToggle,
}: {
  id: string;
  selectedIds: string[];
  onToggle: (id: string) => void;
}) {
  return (
    <input
      type="checkbox"
      checked={selectedIds.includes(id)}
      onChange={(e) => { e.stopPropagation(); onToggle(id); }}
      onClick={(e) => e.stopPropagation()}
    />
  );
}

export function BulkSelectHeader({
  allIds,
  selectedIds,
  onSelectAll,
  onClearSelection,
}: {
  allIds: string[];
  selectedIds: string[];
  onSelectAll: () => void;
  onClearSelection: () => void;
}) {
  const allSelected = allIds.length > 0 && allIds.every(id => selectedIds.includes(id));
  const someSelected = selectedIds.length > 0 && !allSelected;

  return (
    <input
      type="checkbox"
      checked={allSelected}
      ref={(el) => {
        if (el) el.indeterminate = someSelected;
      }}
      onChange={() => {
        if (allSelected) {
          onClearSelection();
        } else {
          onSelectAll();
        }
      }}
    />
  );
}

export default function BulkSelectBar({
  selectedIds,
  onClearSelection,
  actions = [],
}: Pick<BulkSelectProps, 'selectedIds' | 'onClearSelection' | 'actions'>) {
  if (selectedIds.length === 0) return null;

  return (
    <div className="view-actions" style={{ marginBottom: '0.5rem' }}>
      <span className="text-dim" style={{ fontSize: '0.85rem' }}>
        {selectedIds.length} selected
      </span>
      {actions.map((action, i) => (
        <button
          key={i}
          className={`btn btn-sm${action.danger ? ' btn-danger' : ''}`}
          onClick={action.onClick}
        >
          {action.label}
        </button>
      ))}
      <button className="btn btn-sm" onClick={onClearSelection}>
        Clear
      </button>
    </div>
  );
}
