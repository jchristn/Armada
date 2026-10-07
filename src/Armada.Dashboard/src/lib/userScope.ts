import type { UserMaster } from '../types/models';

/** How a user is named in the admin "view records for a specific user" filter: "First Last (email)" or the email. */
export function userScopeLabel(user: Pick<UserMaster, 'firstName' | 'lastName' | 'email'>): string {
  const name = [user.firstName, user.lastName].filter(Boolean).join(' ').trim();
  return name ? `${name} (${user.email})` : user.email;
}
