import { type ReactNode } from 'react';

/**
 * Nav icon set, keyed by a short slug so `navConfig` items stay one-liners and
 * the consolidation phases can move/rename nav entries without wrangling inline
 * SVG. All icons are 16x16 stroke glyphs matching the sidebar style.
 */

function svg(children: ReactNode): ReactNode {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      {children}
    </svg>
  );
}

export const icons: Record<string, ReactNode> = {
  dashboard: svg(<><rect x="3" y="3" width="7" height="7" /><rect x="14" y="3" width="7" height="7" /><rect x="14" y="14" width="7" height="7" /><rect x="3" y="14" width="7" height="7" /></>),
  ask: svg(<><path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" /><path d="M12 7v4" /><path d="M12 15h.01" /></>),
  needsYou: svg(<><path d="M22 12h-6l-2 3h-4l-2-3H2" /><path d="M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z" /></>),
  planning: svg(<><path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" /><path d="M8 9h8" /><path d="M8 13h5" /></>),
  dispatch: svg(<><path d="M22 2 11 13" /><polygon points="22 2 15 22 11 13 2 9 22 2" /></>),
  backlog: svg(<><path d="M4 4h16v16H4z" /><path d="M8 8h8" /><path d="M8 12h8" /><path d="M8 16h5" /></>),
  voyages: svg(<><circle cx="12" cy="12" r="10" /><polyline points="12 6 12 12 16 14" /></>),
  missions: svg(<><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" /><polyline points="14 2 14 8 20 8" /><line x1="16" y1="13" x2="8" y2="13" /><line x1="16" y1="17" x2="8" y2="17" /></>),
  mergeQueue: svg(<><circle cx="18" cy="18" r="3" /><circle cx="6" cy="6" r="3" /><path d="M6 21V9a9 9 0 0 0 9 9" /></>),
  configuration: svg(<><path d="M4 7h16" /><path d="M4 12h10" /><path d="M4 17h7" /><rect x="3" y="3" width="18" height="18" rx="2" /></>),
  workflowProfiles: svg(<><path d="M4 7h16" /><path d="M4 12h10" /><path d="M4 17h7" /><rect x="3" y="3" width="18" height="18" rx="2" /></>),
  projectProfiles: svg(<><path d="M12 2 2 7l10 5 10-5-10-5Z" /><path d="m2 17 10 5 10-5" /><path d="m2 12 10 5 10-5" /></>),
  skills: svg(<path d="m12 2 3 7 7 .5-5.5 4.5 2 7-6.5-4-6.5 4 2-7L2 9.5 9 9Z" />),
  checks: svg(<path d="M20 6 9 17l-5-5" />),
  environments: svg(<><path d="M3 11h18" /><path d="M6 7h12" /><path d="M8 15h8" /><path d="M10 19h4" /><path d="M12 3v4" /></>),
  deployments: svg(<><path d="M12 3v12" /><path d="m7 10 5 5 5-5" /><path d="M5 21h14" /></>),
  fleetActions: svg(<><path d="M13 2 4 14h7l-1 8 9-12h-7l1-8z" /></>),
  delivery: svg(<><path d="M12 3v12" /><path d="m7 10 5 5 5-5" /><path d="M5 21h14" /></>),
  releases: svg(<><path d="M7 3h10l4 4v14H3V3h4" /><path d="M7 3v6h10" /><path d="M9 13h6" /><path d="M9 17h6" /></>),
  incidents: svg(<><path d="M12 9v4" /><path d="M12 17h.01" /><path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z" /></>),
  runbooks: svg(<><path d="M4 19.5A2.5 2.5 0 0 1 6.5 17H20" /><path d="M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2z" /><path d="M8 7h8" /><path d="M8 11h8" /></>),
  fleets: svg(<><rect x="2" y="2" width="20" height="8" rx="2" ry="2" /><rect x="2" y="14" width="20" height="8" rx="2" ry="2" /></>),
  workspace: svg(<><path d="M3 6h18" /><path d="M7 12h10" /><path d="M10 18h4" /><rect x="3" y="3" width="18" height="18" rx="2" /></>),
  vessels: svg(<><path d="M8 6h13" /><path d="M8 12h13" /><path d="M8 18h13" /><path d="M3 6h.01" /><path d="M3 12h.01" /><path d="M3 18h.01" /></>),
  captains: svg(<><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2" /><circle cx="12" cy="7" r="4" /></>),
  docks: svg(<><rect x="2" y="7" width="20" height="14" rx="2" ry="2" /><path d="M16 21V5a2 2 0 0 0-2-2h-4a2 2 0 0 0-2 2v16" /></>),
  activity: svg(<><path d="M3 12h18" /><path d="M12 3v18" /><circle cx="12" cy="12" r="9" /></>),
  history: svg(<><path d="M3 12h18" /><path d="M12 3v18" /><circle cx="12" cy="12" r="9" /></>),
  signals: svg(<polyline points="22 12 18 12 15 21 9 3 6 12 2 12" />),
  events: svg(<><path d="M12 20h9" /><path d="M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4L16.5 3.5z" /></>),
  notifications: svg(<><path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9" /><path d="M13.73 21a2 2 0 0 1-3.46 0" /></>),
  personas: svg(<><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2" /><circle cx="12" cy="7" r="4" /></>),
  pipelines: svg(<><polyline points="16 3 21 3 21 8" /><line x1="4" y1="20" x2="21" y2="3" /><polyline points="21 16 21 21 16 21" /><line x1="15" y1="15" x2="21" y2="21" /><line x1="4" y1="4" x2="9" y2="9" /></>),
  prompts: svg(<><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" /><polyline points="14 2 14 8 20 8" /><line x1="16" y1="13" x2="8" y2="13" /><line x1="16" y1="17" x2="8" y2="17" /><line x1="10" y1="9" x2="8" y2="9" /></>),
  playbooks: svg(<><path d="M4 19.5A2.5 2.5 0 0 1 6.5 17H20" /><path d="M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2z" /></>),
  requests: svg(<><path d="M3 12h18" /><path d="M8 7l-5 5 5 5" /><path d="M16 17l5-5-5-5" /></>),
  apiExplorer: svg(<><circle cx="11" cy="11" r="8" /><path d="m21 21-4.35-4.35" /><path d="M11 8v6" /><path d="M8 11h6" /></>),
  server: svg(<><rect x="2" y="2" width="20" height="8" rx="2" ry="2" /><rect x="2" y="14" width="20" height="8" rx="2" ry="2" /><line x1="6" y1="6" x2="6.01" y2="6" /><line x1="6" y1="18" x2="6.01" y2="18" /></>),
  doctor: svg(<path d="M22 12h-4l-3 9L9 3l-3 9H2" />),
  tenants: svg(<><path d="M3 21h18" /><path d="M5 21V7l7-4 7 4v14" /><path d="M9 9h.01" /><path d="M9 13h.01" /><path d="M15 9h.01" /><path d="M15 13h.01" /></>),
  users: svg(<><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" /><circle cx="9" cy="7" r="4" /><path d="M23 21v-2a4 4 0 0 0-3-3.87" /><path d="M16 3.13a4 4 0 0 1 0 7.75" /></>),
  credentials: svg(<><path d="M21 2l-2 2m-7.61 7.61a5.5 5.5 0 1 1 7.78 7.78l-3.19-3.19" /><path d="M5.5 12.5 2 16l6 6 3.5-3.5" /><path d="m14.5 8.5 1 1" /><path d="m10.5 12.5 1 1" /></>),
};
