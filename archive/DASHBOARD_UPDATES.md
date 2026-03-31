# Dashboard Updates Plan

Bring the web dashboard (`/dashboard`) to full feature parity with the CLI (`armada`) and REST API (`/api/v1/*`).

**Current state:** The dashboard has 5 views (Home, Fleets, Voyages, Captains, Dispatch) with mostly read-only capabilities. The only write operations are "Recall captain" and "Dispatch mission."

**Target state:** Full CRUD and management for all entities, filtering/search, detail views, and operational controls -- matching everything available via CLI and API.

---

## Legend

- `[ ]` Not started
- `[~]` In progress
- `[x]` Complete
- `[!]` Blocked

---

## Phase 1: Entity Detail Views

Add detail/show views for individual entities. Clicking an item in any list navigates to its detail view.

### 1.1 Mission Detail View
- [x] Create mission detail panel/page showing all mission fields (ID, title, description, status, vessel, captain, branch, priority, timestamps)
- [ ] Display last N lines of session log (fetch from signals or dedicated endpoint)
- [x] Show mission diff viewer with syntax-highlighted unified diff (`GET /api/v1/missions/{id}/diff`)
- [x] Show assigned captain info with link to captain detail
- [x] Show parent voyage info with link to voyage detail
- [x] Show vessel info with link to vessel detail

### 1.2 Voyage Detail View
- [x] Create voyage detail panel showing all voyage fields (ID, title, description, status, timestamps)
- [x] Inline table of all missions in the voyage with status badges (already partially done in list -- promote to full detail view)
- [x] Progress bar with completed/total/failed counts
- [x] Action buttons: Cancel voyage, Retry failed missions (see Phase 2)

### 1.3 Captain Detail View
- [x] Create captain detail panel showing all fields (ID, name, runtime, state, current mission, process ID, heartbeat timestamp)
- [x] Link to current mission detail (if working)
- [x] Mission history for this captain (filter missions by captainId)
- [x] Action buttons: Stop, Remove (see Phase 2)

### 1.4 Vessel Detail View
- [x] Create vessel detail panel showing all fields (ID, name, repo URL, default branch, fleet, active status)
- [x] List missions targeting this vessel (filter missions by vesselId)
- [x] Show parent fleet with link to fleet detail
- [x] Action buttons: Edit, Remove (see Phase 2)

### 1.5 Fleet Detail View
- [x] Create fleet detail panel showing all fields (ID, name, description, active status, timestamps)
- [x] Inline table of all vessels in the fleet
- [x] Action buttons: Edit, Remove (see Phase 2)

---

## Phase 2: Full CRUD Operations

Add create, update, and delete capabilities for all entities.

### 2.1 Fleet CRUD
- [x] **Create:** Form with name and description fields (`POST /api/v1/fleets`)
- [x] **Edit:** Inline or modal edit form for fleet name/description (`PUT /api/v1/fleets/{id}`)
- [x] **Delete:** Delete button with confirmation dialog (`DELETE /api/v1/fleets/{id}`)

### 2.2 Vessel CRUD
- [x] **Create:** Form with name, repo URL, default branch, fleet selector (`POST /api/v1/vessels`)
- [x] **Edit:** Inline or modal edit form for vessel fields (`PUT /api/v1/vessels/{id}`)
- [x] **Delete:** Delete button with confirmation dialog (`DELETE /api/v1/vessels/{id}`)

### 2.3 Voyage CRUD
- [x] **Create:** Form with title, description, vessel selector, and repeatable mission entries (`POST /api/v1/voyages`)
- [x] **Cancel:** Cancel button with confirmation (`DELETE /api/v1/voyages/{id}`)
- [x] **Retry:** Retry button that re-creates failed missions in a voyage (create new missions mirroring failed ones)

### 2.4 Mission CRUD
- [x] **Create:** Enhance existing Dispatch view -- add priority field, voyage selector, richer description input (`POST /api/v1/missions`)
- [x] **Edit:** Modal/inline edit for title, description, priority (`PUT /api/v1/missions/{id}`)
- [x] **Status transition:** Dropdown or button bar for valid status transitions (`PUT /api/v1/missions/{id}/status`)
- [x] **Cancel:** Cancel button with confirmation (`DELETE /api/v1/missions/{id}`)
- [x] **Retry:** Button to create a new mission with same details from a failed mission

### 2.5 Captain CRUD
- [x] **Add:** Form with name and runtime selector (claude, codex, custom) (`POST /api/v1/captains`)
- [x] **Stop:** Already exists -- verify it works, add confirmation dialog
- [x] **Stop All:** Emergency button to recall all captains (iterate `POST /api/v1/captains/{id}/stop` for each)
- [x] **Remove:** Delete button with confirmation (`DELETE /api/v1/captains/{id}`)

---

## Phase 3: New Views

Add entirely new dashboard sections for entities/features not currently represented.

### 3.1 Signals View
- [x] New nav tab: "Signals"
- [x] Table of recent signals with columns: ID, type, direction, source, target, message, timestamp (`GET /api/v1/signals`)
- [x] Color-coded signal types (assignment, completion, error, heartbeat, etc.)
- [x] **Send signal:** Form to compose and send a signal (`POST /api/v1/signals`)

### 3.2 Events View
- [x] New nav tab: "Events"
- [x] Filterable event log table (`GET /api/v1/events`)
- [x] Filter controls: event type dropdown, captain/mission/vessel/voyage ID inputs, limit slider
- [x] Relative timestamps with hover for absolute time
- [x] Auto-refresh via WebSocket or polling

### 3.3 Merge Queue View
- [x] New nav tab: "Merge Queue"
- [x] Table of queued entries with status badges (`GET /api/v1/merge-queue`)
- [x] **Enqueue:** Form to add a branch to the merge queue (mission selector, branch, target branch) (`POST /api/v1/merge-queue`)
- [x] **Cancel:** Remove entry from queue with confirmation (`DELETE /api/v1/merge-queue/{id}`)
- [x] **Process:** Button to trigger queue processing (`POST /api/v1/merge-queue/process`)
- [x] Entry detail view (`GET /api/v1/merge-queue/{id}`)

### 3.4 Server Management View
- [x] New nav tab or section in header/footer: "Server"
- [x] Health status display (`GET /api/v1/status/health`)
- [x] Server stop button with confirmation (`POST /api/v1/server/stop`)
- [~] Display server version, uptime, port info from status endpoint (ports shown; version/uptime require a new API field)

### 3.5 Configuration View
- [ ] New nav tab: "Config" (or settings gear icon)
- [ ] Read-only display of current configuration (data directory, ports, PR settings, runtime config, etc.)
- [ ] Note: Server-side config set API does not currently exist -- this view is read-only unless a config API is added

---

## Phase 4: List View Enhancements

Upgrade all existing list views with filtering, search, and sorting.

### 4.1 Mission List Filters
- [x] Status filter dropdown (Pending, Assigned, InProgress, Testing, Review, Complete, Failed, Cancelled)
- [x] Vessel filter dropdown
- [x] Captain filter dropdown
- [~] Voyage filter dropdown (available via voyage detail view; standalone filter deferred)
- [x] All filters use query parameters: `GET /api/v1/missions?status=X&vesselId=Y&captainId=Z&voyageId=W`

### 4.2 Voyage List Filters
- [x] Status filter dropdown (Active, Complete, Cancelled) using `GET /api/v1/voyages?status=X`

### 4.3 Vessel List Filters
- [x] Fleet filter dropdown using `GET /api/v1/vessels?fleetId=X` (vessels shown per-fleet in Fleets view)

### 4.4 Universal List Improvements
- [x] Client-side text search/filter across visible rows
- [x] Column sorting (click column headers to sort)
- [ ] Pagination or virtual scroll for large lists
- [ ] Bulk selection with bulk actions (e.g., cancel multiple missions)

---

## Phase 5: UX Polish and Operational Features

### 5.1 Navigation & Layout
- [x] Breadcrumb navigation for detail views (e.g., Fleets > Fleet Name > Vessel Name)
- [x] Consistent back-navigation from detail views to list views
- [x] Responsive mobile layout review and fixes
- [x] Keyboard shortcuts for common actions (e.g., `r` for refresh, `Escape` to close modals)

### 5.2 Notifications & Feedback
- [x] Toast notifications for successful CRUD operations (created, updated, deleted)
- [x] Error toast with message for failed API calls
- [x] Confirmation dialogs for all destructive operations (delete, cancel, stop)
- [~] Loading spinners/skeletons during API calls (modalLoading state used; full skeleton placeholders deferred)

### 5.3 Real-Time Updates
- [x] Verify WebSocket updates refresh all new views (signals, events, merge queue)
- [x] Add WebSocket event handlers for new entity types
- [ ] Visual indicator when data has been updated (subtle flash or badge)

### 5.4 Dispatch View Upgrade
- [x] Dispatch view enhanced with priority field, voyage selector
- [x] Add "New Voyage" flow: title + description + multiple mission entries with vessel selector
- [ ] Quick-dispatch: single text input (like `armada go`) that creates a voyage from natural language

---

## Gap Summary: Dashboard vs CLI

| Capability | CLI | API | Dashboard (Current) | Dashboard (Planned) |
|---|---|---|---|---|
| System status | `status`, `watch` | `GET /status` | Home view | Phase 5.3 |
| Fleet list | `fleet list` | `GET /fleets` | Fleets view | Phase 4.3 |
| Fleet create | `fleet add` | `POST /fleets` | -- | Phase 2.1 |
| Fleet delete | `fleet remove` | `DELETE /fleets/{id}` | -- | Phase 2.1 |
| Vessel list | `vessel list` | `GET /vessels` | Under Fleets | Phase 4.3 |
| Vessel create | `vessel add` | `POST /vessels` | -- | Phase 2.2 |
| Vessel delete | `vessel remove` | `DELETE /vessels/{id}` | -- | Phase 2.2 |
| Voyage list | `voyage list` | `GET /voyages` | Voyages view | Phase 4.2 |
| Voyage create | `voyage create` | `POST /voyages` | -- | Phase 2.3 |
| Voyage cancel | `voyage cancel` | `DELETE /voyages/{id}` | -- | Phase 2.3 |
| Voyage retry | `voyage retry` | (compose) | -- | Phase 2.3 |
| Voyage detail | `voyage show` | `GET /voyages/{id}` | Expandable card | Phase 1.2 |
| Mission list | `mission list` | `GET /missions` | Under Voyages | Phase 4.1 |
| Mission create | `mission create`, `go` | `POST /missions` | Dispatch view | Phase 2.4 |
| Mission detail | `mission show` | `GET /missions/{id}` | -- | Phase 1.1 |
| Mission edit | -- | `PUT /missions/{id}` | -- | Phase 2.4 |
| Mission status | -- | `PUT /missions/{id}/status` | -- | Phase 2.4 |
| Mission cancel | `mission cancel` | `DELETE /missions/{id}` | -- | Phase 2.4 |
| Mission retry | `mission retry` | (compose) | -- | Phase 2.4 |
| Mission diff | `diff` | `GET /missions/{id}/diff` | -- | Phase 1.1 |
| Captain list | `captain list` | `GET /captains` | Captains view | Done |
| Captain add | `captain add` | `POST /captains` | -- | Phase 2.5 |
| Captain stop | `captain stop` | `POST /captains/{id}/stop` | Recall button | Done |
| Captain stop-all | `captain stop-all` | (compose) | -- | Phase 2.5 |
| Captain remove | `captain remove` | `DELETE /captains/{id}` | -- | Phase 2.5 |
| Captain detail | -- | `GET /captains/{id}` | -- | Phase 1.3 |
| Signals list | -- | `GET /signals` | Recent in Home | Phase 3.1 |
| Signal send | -- | `POST /signals` | -- | Phase 3.1 |
| Events list | -- | `GET /events` | -- | Phase 3.2 |
| Merge queue | -- | `GET /merge-queue` | -- | Phase 3.3 |
| Merge enqueue | -- | `POST /merge-queue` | -- | Phase 3.3 |
| Merge process | -- | `POST /merge-queue/process` | -- | Phase 3.3 |
| Server health | `server status` | `GET /status/health` | -- | Phase 3.4 |
| Server stop | `server stop` | `POST /server/stop` | -- | Phase 3.4 |
| Config view | `config show` | -- | -- | Phase 3.5 |
| Doctor | `doctor` | -- | -- | Out of scope (CLI-only) |
| Reset | `reset` | -- | -- | Out of scope (CLI-only) |
| Log tailing | `log` | -- | -- | Out of scope (CLI-only) |
| MCP install | `mcp install/stdio` | -- | -- | Out of scope (CLI-only) |

---

## Implementation Notes

- **Tech stack:** Alpine.js + HTMX (existing). No build step. All JS in `dashboard.js`, HTML in `index.html`, CSS in `dashboard.css`.
- **File splitting:** If `dashboard.js` exceeds ~500 lines, consider splitting into per-view modules loaded via `<script>` tags (no bundler).
- **API client:** Centralize all `fetch()` calls through the existing `apiFetch()` helper in `dashboard.js`.
- **Routing:** Use Alpine.js `x-show` view switching (existing pattern). Detail views can use a sub-state (e.g., `view: 'mission-detail', detailId: 'msn_xxx'`).
- **No new dependencies:** Keep the zero-build, CDN-loaded approach. Alpine.js and HTMX are sufficient.
- **Testing:** Manual testing against a running Admiral server. Consider adding Playwright e2e tests if the dashboard grows significantly.
