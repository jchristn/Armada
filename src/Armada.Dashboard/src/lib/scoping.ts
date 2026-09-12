import type { ScopeEnum } from '../types/models';

/**
 * Frontend mirror of the backend `ScopedVisibility` rules for Category B configuration entities.
 * Keep these in sync with `Armada.Core.Models.ScopedVisibility`.
 */

/** The caller identity needed to evaluate scope rules (from `useAuth()`). */
export interface ScopeViewer {
  isAdmin: boolean;
  isTenantAdmin: boolean;
  tenantId: string | null | undefined;
  userId: string | null | undefined;
}

/** A scoped object's ownership fields. */
export interface ScopedObject {
  scope: ScopeEnum;
  tenantId: string | null | undefined;
  userId: string | null | undefined;
}

/**
 * Whether the viewer may see an object with the given scope and ownership. Mirrors backend CanView:
 * global admins see everything; a different tenant is never visible; tenant admins see their whole
 * tenant; otherwise the object must be tenant-wide or owned by the viewer.
 */
export function canView(viewer: ScopeViewer, obj: ScopedObject): boolean {
  if (viewer.isAdmin) return true;
  if (obj.tenantId !== viewer.tenantId) return false;
  if (viewer.isTenantAdmin) return true;
  return obj.scope === 'TenantWide' || (obj.userId != null && obj.userId === viewer.userId);
}

/**
 * Whether the viewer may edit or delete an object. Mirrors backend CanEdit: global admins can edit
 * anything; a different tenant is never editable; tenant admins edit their whole tenant; otherwise
 * only a user-specific object owned by the viewer is editable.
 */
export function canEdit(viewer: ScopeViewer, obj: ScopedObject): boolean {
  if (viewer.isAdmin) return true;
  if (obj.tenantId !== viewer.tenantId) return false;
  if (viewer.isTenantAdmin) return true;
  return obj.scope === 'UserSpecific' && obj.userId != null && obj.userId === viewer.userId;
}

/**
 * The scope a newly created object should carry for this viewer. Regular users can only create
 * user-specific objects; tenant/global admins may create either and default to tenant-wide.
 */
export function resolveCreateScope(viewer: ScopeViewer, requested?: ScopeEnum | null): ScopeEnum {
  if (viewer.isAdmin || viewer.isTenantAdmin) return requested ?? 'TenantWide';
  return 'UserSpecific';
}

/** Whether the viewer is allowed to choose an object's ownership scope (admins only). */
export function canChooseScope(viewer: ScopeViewer): boolean {
  return viewer.isAdmin || viewer.isTenantAdmin;
}

/** Human-readable label for a scope value. */
export function scopeLabel(scope: ScopeEnum): string {
  return scope === 'UserSpecific' ? 'Personal' : 'Tenant-wide';
}
