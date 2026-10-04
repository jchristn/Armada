import type { BacklogGroupDefinition, BacklogGroupKey } from './backlogUtils';

interface BacklogGroupPillsProps {
  groups: BacklogGroupDefinition[];
  activeGroup: BacklogGroupKey;
  counts: Record<BacklogGroupKey, number>;
  onChange: (group: BacklogGroupKey) => void;
}

export default function BacklogGroupPills({
  groups,
  activeGroup,
  counts,
  onChange,
}: BacklogGroupPillsProps) {
  return (
    <div className="backlog-group-pills" role="group" aria-label="Backlog group views">
      {groups.map((group) => (
        <button
          key={group.key}
          type="button"
          className={`backlog-group-pill${activeGroup === group.key ? ' active' : ''}`}
          title={group.description}
          aria-pressed={activeGroup === group.key}
          onClick={() => onChange(group.key)}
        >
          <span>{group.label}</span>
          <strong>{counts[group.key] || 0}</strong>
        </button>
      ))}
    </div>
  );
}
