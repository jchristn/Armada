/**
 * Dashboard detail routes for entities. Routes are chosen from the entity's type field (an event's `entityType`,
 * or which typed id field a notification carries), never from the id's prefix.
 */

// The server writes event EntityType values with mixed conventions for the same kind ("mission", "MergeEntry",
// "merge-entry", "merge_entry", "check-run", "CheckRun"). The key below folds case and the '-' / '_' separators of
// that identifier so each kind has a single entry; it is a normalization of a type identifier, not a search in text.
function entityTypeKey(entityType: string): string {
  return entityType.toLowerCase().replace(/[-_]/g, '');
}

const ENTITY_TYPE_ROUTES: Record<string, (id: string) => string> = {
  fleet: (id) => `/fleets/${id}`,
  vessel: (id) => `/vessels/${id}`,
  captain: (id) => `/captains/${id}`,
  mission: (id) => `/missions/${id}`,
  voyage: (id) => `/voyages/${id}`,
  signal: (id) => `/signals/${id}`,
  event: (id) => `/events/${id}`,
  dock: (id) => `/docks/${id}`,
  mergeentry: (id) => `/merge-queue/${id}`,
  deployment: (id) => `/deployments/${id}`,
  incident: (id) => `/incidents/${id}`,
  objective: (id) => `/objectives/${id}`,
  release: (id) => `/releases/${id}`,
  checkrun: (id) => `/checks/${id}`,
  planningsession: (id) => `/planning/${id}`,
  requesthistory: (id) => `/requests/${id}`,
  fleetactionrun: (id) => `/fleet-actions/runs/${id}`,
};

/** Return the dashboard detail route for an entity of the given type, or null when the type has no detail page. */
export function entityRoute(entityType: string | null | undefined, entityId: string | null | undefined): string | null {
  if (!entityType || !entityId) return null;
  const key = entityTypeKey(entityType);
  const route = Object.prototype.hasOwnProperty.call(ENTITY_TYPE_ROUTES, key) ? ENTITY_TYPE_ROUTES[key] : undefined;
  return route ? route(entityId) : null;
}

/** The typed id fields a notification carries. */
export interface NotificationTarget {
  missionId: string | null;
  voyageId: string | null;
  captainId: string | null;
}

/** Return the detail route for a notification from whichever typed id field it carries. */
export function notificationRoute(n: NotificationTarget): string | null {
  if (n.missionId) return `/missions/${n.missionId}`;
  if (n.voyageId) return `/voyages/${n.voyageId}`;
  if (n.captainId) return `/captains/${n.captainId}`;
  return null;
}
