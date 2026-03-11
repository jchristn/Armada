# Armada Desktop - Avalonia UI Application Plan

> **Goal**: A cross-platform desktop application providing real-time monitoring, fleet management, voyage management, and mission dispatch -- everything `armada watch` does and more, in a rich GUI.

---

## Status Legend

- `[ ]` Not started
- `[~]` In progress
- `[x]` Complete
- `[!]` Blocked (note reason inline)

---

## Table of Contents

1. [Architecture](#1-architecture)
2. [Phase 1 - Project Scaffolding](#2-phase-1---project-scaffolding)
3. [Phase 2 - API Client & Data Layer](#3-phase-2---api-client--data-layer)
4. [Phase 3 - Live Dashboard (Watch Parity)](#4-phase-3---live-dashboard-watch-parity)
5. [Phase 4 - Fleet Management](#5-phase-4---fleet-management)
6. [Phase 5 - Voyage Management](#6-phase-5---voyage-management)
7. [Phase 6 - Mission Monitoring & Control](#7-phase-6---mission-monitoring--control)
8. [Phase 7 - New Voyage Dispatch](#8-phase-7---new-voyage-dispatch)
9. [Phase 8 - Notifications & System Tray](#9-phase-8---notifications--system-tray)
10. [Phase 9 - Polish & Packaging](#10-phase-9---polish--packaging)

---

## 1. Architecture

```
+-----------------------------------------------------------+
|  Armada.Desktop (Avalonia MVVM)                           |
|                                                           |
|  Views/        - AXAML views (pages, controls, dialogs)   |
|  ViewModels/   - ReactiveUI view models                   |
|  Services/     - API polling, WebSocket, notifications    |
|  Converters/   - Value converters (status->color, etc.)   |
|  Assets/       - Icons, styles, branding                  |
|                                                           |
|  References:                                              |
|    Armada.Core  (models, enums, ArmadaApiClient)          |
|    Armada.Server (embedded server auto-start)             |
+-----------------------------------------------------------+
         |                          |
         | HTTP REST (polling)      | WebSocket (real-time)
         v                          v
+-----------------------------------------------------------+
|  Armada.Server (Admiral)                                  |
+-----------------------------------------------------------+
```

### Technology Choices

| Component | Library | Rationale |
|-----------|---------|-----------|
| UI Framework | Avalonia 11.2.7 | Cross-platform, XAML-based, mature |
| MVVM | ReactiveUI | Reactive bindings, Avalonia-native support |
| DataGrid | Avalonia.Controls.DataGrid 11.2.7 | Separate package in Avalonia 11 |
| HTTP Client | `Armada.Core.Client.ArmadaApiClient` | Already exists, typed, covers all endpoints |
| WebSocket | `System.Net.WebSockets.ClientWebSocket` | Built-in, no extra dependency |

### Design Principles

- **Thin views, rich view models** -- all logic in ViewModels, views are pure AXAML.
- **Polling-based updates** -- 5s default interval, configurable in Settings.
- **Reuse `Armada.Core`** -- models, enums, and `ArmadaApiClient` are shared; no duplication.
- **Follow existing coding standards** -- `_PascalCase` fields, explicit types, `#region` blocks, XML docs.
- **Splash screen + auto-start** -- splash screen on launch, auto-starts embedded Admiral if not running.

---

## 2. Phase 1 - Project Scaffolding

### 2.1 Create Avalonia Project

- [x] Create `src/Armada.Desktop/Armada.Desktop.csproj` targeting `net8.0`
- [x] Add NuGet references: `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `ReactiveUI.Avalonia`, `Avalonia.Controls.DataGrid`
- [x] Add project references to `Armada.Core` and `Armada.Server`
- [x] Add project to `Armada.sln`
- [x] Verify `dotnet build Armada.sln` succeeds (0 warnings, 0 errors)

### 2.2 Application Shell

- [x] Create `App.axaml` with Fluent theme (dark mode default)
- [x] Create `MainWindow.axaml` with navigation sidebar and content area
- [x] Implement `MainWindowViewModel` with page navigation (property swap pattern)
- [x] Define sidebar navigation items: Dashboard, Voyages, Missions, Fleet, Dispatch, Settings
- [x] Add Armada logo/branding to the sidebar header
- [x] Connection status indicator in sidebar footer

### 2.3 Splash Screen

- [x] Create `SplashWindow.axaml` with Armada logo, progress bar, status text
- [x] Auto-start embedded Admiral server if not already running
- [x] Show status updates during initialization (loading settings, checking server, connecting)
- [x] Transition to MainWindow once connected

### 2.4 Folder Structure

- [x] Create folder structure: ViewModels/, Views/, Services/, Converters/, Assets/
- [x] Create `ViewModelBase.cs` extending `ReactiveObject`
- [x] Create `Program.cs` with Avalonia app builder
- [x] Copy logo from `assets/logo-white.png` to Desktop assets

### 2.5 Theme & Branding

- [x] Create `Assets/Theme.axaml` with Armada color palette
- [x] Define status color brushes matching CLI `TableRenderer` (green, gold, red, grey, purple, orange)
- [x] Define surface colors for dark theme (SurfaceDark, SurfaceMid, SurfaceCard, SurfaceHover)
- [x] Style classes: card, badge, stat-card, nav-item, action button, danger button, section-header, page-title

---

## 3. Phase 2 - API Client & Data Layer

### 3.1 Connection Service

- [x] Create `ArmadaConnectionService` -- wraps `ArmadaApiClient`, manages connection lifecycle
- [x] Read Admiral URL from `~/.armada/settings.json` (default `http://localhost:7890`)
- [x] Expose `IsConnected` property with `ConnectionChanged` event
- [x] Auto-start embedded server if health check fails (mirrors `EmbeddedServer` pattern from Helm)
- [x] Expose connection status in the UI (sidebar footer with green/red dot)

### 3.2 Polling Service

- [x] Polling built into `ArmadaConnectionService` with configurable interval (default 5s)
- [x] Fetch in parallel: `/status`, `/captains`, `/missions`, `/vessels`, `/voyages`, `/fleets`
- [x] Expose `DataRefreshed` event for ViewModels to subscribe
- [x] Handle connection failures gracefully (update `IsConnected`, don't crash)
- [x] Start/stop polling support

### 3.3 Value Converters

- [x] `MissionStatusColorConverter` -- mission status enum to SolidColorBrush
- [x] `CaptainStateColorConverter` -- captain state enum to SolidColorBrush
- [x] `VoyageStatusColorConverter` -- voyage status enum to SolidColorBrush
- [x] `ConnectionStatusConverter` -- bool to "Connected"/"Disconnected" string
- [x] `ConnectionColorConverter` -- bool to green/red brush

### 3.4 Lookup Dictionaries

- [x] Built into `DashboardViewModel` and other ViewModels using `ToDictionary()` on each refresh
- [x] Name resolution (ID -> display name) used across dashboard, missions, voyages

---

## 4. Phase 3 - Live Dashboard (Watch Parity)

### 4.1 Dashboard Page

- [x] Create `DashboardView.axaml` and `DashboardViewModel.cs`
- [x] Wire as the default/home page in navigation

### 4.2 Summary Cards

- [x] Captain summary card: total count, idle (blue), working (green), stalled (red) with colored dots
- [x] Mission summary cards: active count (gold), queued count (dim), completed (green), failed (red)
- [x] Voyage count card (blue)
- [x] Large stat numbers with labels, wrapped in styled stat-card borders

### 4.3 Voyage Progress

- [x] List active voyages with title, completed/total text, progress bar
- [x] Color coding: blue progress bar, red text for failures
- [x] Shows in collapsible card section

### 4.4 Action Required Section

- [x] Stalled captains: captain chain (name > vessel > mission), heartbeat age
- [x] Failed missions (last 5): mission chain, voyage context, failure age
- [x] Recently completed missions (last 30 min): chain and completion age
- [x] Severity badges (Stalled, Failed, Done) with colored indicators
- [x] "All clear" message when no action items

### 4.5 Active Captains Panel

- [x] Shows all captains (filters idle when >5 total) with state dot, name, mission, vessel
- [x] Two-column layout alongside Action Required

### 4.6 Recent Signals

- [x] Show last 8 signals with timestamp (monospace), type (blue), source
- [x] Empty state message when no signals

### 4.7 Auto-Refresh

- [x] Dashboard updates automatically via polling (every 5s default)
- [x] Last refresh timestamp displayed in header
- [x] Change detection for completed/failed missions (tracking seen IDs)

---

## 5. Phase 4 - Fleet Management

### 5.1 Fleet View (Tabbed)

- [x] Create `FleetView.axaml` and `FleetViewModel.cs`
- [x] Tabbed interface: Vessels, Captains, Fleets
- [x] Tab switching via button clicks

### 5.2 Vessel Tab

- [x] Display vessels in DataGrid: Name, Repo URL, Branch, ID
- [x] Empty state message
- [x] Auto-refresh from polling

### 5.3 Captain Tab

- [x] Display captains in DataGrid: Name, Runtime, State, Mission, PID, Last Heartbeat, ID
- [x] "Stop All" button (danger style)
- [x] `StopCaptainAsync` and `StopAllCaptainsAsync` methods
- [x] `AddCaptainAsync` method

### 5.4 Fleet Tab

- [x] Display fleets in DataGrid: Name, Description, Active, ID
- [x] Auto-refresh from polling

---

## 6. Phase 5 - Voyage Management

### 6.1 Voyage List Page

- [x] Create `VoyageListView.axaml` and `VoyageListViewModel.cs`
- [x] Display voyages as clickable list items with title, status, date, progress bar
- [x] Status filter dropdown (All, Open, InProgress, Complete, Cancelled)
- [x] Click to select and view detail in right panel

### 6.2 Voyage Detail Panel

- [x] Title, ID, Status display
- [x] Mission table (DataGrid): Title, Status, Captain, Branch
- [x] "Retry Failed" and "Cancel Voyage" action buttons
- [x] Placeholder text when no voyage selected

### 6.3 Voyage Monitoring

- [x] Auto-refresh via polling
- [x] `RetryFailedAsync` creates new missions for failed/cancelled ones
- [x] `CancelVoyageAsync` calls DELETE endpoint

---

## 7. Phase 6 - Mission Monitoring & Control

### 7.1 Mission List Page

- [x] Create `MissionListView.axaml` and `MissionListViewModel.cs`
- [x] Display all missions in DataGrid: Title, Status, Captain, Vessel, Voyage, Branch, Priority, Created
- [x] Filter controls: by status (dropdown), by vessel (dropdown, dynamically populated)
- [x] Sortable columns
- [x] Selectable rows

### 7.2 Mission Detail Panel

- [x] Bottom panel showing selected mission details: Title, ID, Status, Branch, PR URL, Description
- [x] Action buttons: Retry, Cancel
- [x] `RetryMissionAsync` and `CancelMissionAsync` methods

---

## 8. Phase 7 - New Voyage Dispatch

### 8.1 Quick Dispatch (Go)

- [x] Create `DispatchView.axaml` and `DispatchViewModel.cs`
- [x] Prompt text box (multi-line, with placeholder)
- [x] Vessel selector dropdown (populated from API)
- [x] Dispatch button with loading indicator
- [x] `DetectMultipleTasks` logic (semicolons, numbered lists) matching `GoCommand`
- [x] Live preview of parsed tasks with bullet points
- [x] Status message after dispatch

### 8.2 Voyage Builder (Advanced)

- [x] Title field, vessel selector
- [x] Dynamic mission list: add/remove mission entries with title + description fields
- [x] "Launch Voyage" button with loading indicator
- [x] Mode toggle between Quick Dispatch and Voyage Builder

---

## 9. Phase 8 - Notifications & System Tray

### 9.1 In-App Change Detection

- [x] Track seen completed/failed mission IDs (matching `WatchCommand` logic)
- [x] Toast notification system (slide-in from corner, auto-dismiss after 8s)
- [x] Notification history panel (sidebar nav with unread badge, full history view)

### 9.2 Desktop Notifications

- [x] `DesktopNotificationService` with change detection (completed, failed, stalled captains)
- [x] Configurable: on/off toggle in Settings (wired to `_NotificationService.Enabled`)

### 9.3 System Tray

- [x] Minimize to system tray (closing window hides to tray)
- [x] Tray icon with Armada logo
- [x] Tray context menu (Show Armada, Exit)

---

## 10. Phase 9 - Polish & Packaging

### 10.1 Settings Page

- [x] Create `SettingsView.axaml` and `SettingsViewModel.cs`
- [x] Admiral port configuration
- [x] Refresh interval (seconds)
- [x] Max captains setting
- [x] Notification toggle
- [x] Auto-create PR toggle
- [x] Save button with status message

### 10.2 Theming & Branding

- [x] Armada color palette: DodgerBlue primary (#1E90FF), consistent with CLI branding
- [x] Status colors matching `TableRenderer` exactly
- [x] Armada logo in sidebar and splash screen
- [x] Dark mode as default

### 10.3 Error Handling & UX

- [x] Connection lost detection (updates `IsConnected`, sidebar indicator changes)
- [x] Empty state messages throughout (no voyages, no missions, no captains, etc.)
- [x] Global error handler -- `AppDomain.UnhandledException` + `TaskScheduler.UnobservedTaskException`
- [x] Confirmation dialogs for destructive actions (`ConfirmDialog` for cancel, stop, retry)
- [x] Loading spinners for async operations (Fleet, Voyages, Missions, Dispatch)

### 10.4 Packaging & Distribution

- [x] Configure `dotnet publish` for self-contained single-file builds (csproj publish properties)
- [ ] Windows: create installer or portable zip
- [ ] macOS: create `.app` bundle
- [ ] Linux: create AppImage or package
- [x] Add project to `Armada.sln`

### 10.5 Testing

- [ ] Unit tests for ViewModels
- [ ] Unit tests for services
- [ ] Integration test: app launches and connects
- [ ] Manual test matrix: Windows, macOS, Linux

---

## Appendix: File Inventory

| File | Type | Purpose |
|------|------|---------|
| `Program.cs` | Entry point | Avalonia app builder |
| `App.axaml` / `App.axaml.cs` | Application | Splash screen, initialization |
| `Assets/Theme.axaml` | Styles | Colors, brushes, style classes |
| `Assets/logo.png` | Image | Armada logo (white variant) |
| `Views/SplashWindow.axaml` | Window | Startup splash with progress |
| `Views/MainWindow.axaml` | Window | Sidebar + content area shell |
| `Views/DashboardView.axaml` | UserControl | Live dashboard (watch parity) |
| `Views/FleetView.axaml` | UserControl | Fleet/vessel/captain management |
| `Views/VoyageListView.axaml` | UserControl | Voyage list + detail |
| `Views/MissionListView.axaml` | UserControl | Mission list + detail |
| `Views/DispatchView.axaml` | UserControl | Quick dispatch + voyage builder |
| `Views/SettingsView.axaml` | UserControl | Settings page |
| `ViewModels/ViewModelBase.cs` | Base class | ReactiveObject base |
| `ViewModels/MainWindowViewModel.cs` | ViewModel | Navigation, connection state |
| `ViewModels/DashboardViewModel.cs` | ViewModel | Watch parity logic |
| `ViewModels/FleetViewModel.cs` | ViewModel | Fleet/vessel/captain CRUD |
| `ViewModels/VoyageListViewModel.cs` | ViewModel | Voyage list/detail/actions |
| `ViewModels/MissionListViewModel.cs` | ViewModel | Mission list/detail/actions |
| `ViewModels/DispatchViewModel.cs` | ViewModel | Dispatch + voyage builder |
| `ViewModels/SettingsViewModel.cs` | ViewModel | Settings persistence |
| `Views/ToastOverlay.axaml` | UserControl | Toast notification overlay |
| `Views/ConfirmDialog.axaml` | Window | Confirmation dialog for destructive actions |
| `Views/NotificationHistoryView.axaml` | UserControl | Notification history page |
| `ViewModels/NotificationHistoryViewModel.cs` | ViewModel | Notification history, mark read, clear |
| `Services/ArmadaConnectionService.cs` | Service | Connection, polling, server mgmt |
| `Services/NotificationService.cs` | Service | Change detection, toast notifications |
| `Converters/StatusColorConverter.cs` | Converters | Status-to-color converters |

## Appendix: NuGet Packages

```xml
<PackageReference Include="Avalonia" Version="11.2.7" />
<PackageReference Include="Avalonia.Desktop" Version="11.2.7" />
<PackageReference Include="Avalonia.Themes.Fluent" Version="11.2.7" />
<PackageReference Include="Avalonia.Fonts.Inter" Version="11.2.7" />
<PackageReference Include="Avalonia.ReactiveUI" Version="11.2.7" />
<PackageReference Include="Avalonia.Controls.DataGrid" Version="11.2.7" />
<PackageReference Include="Avalonia.Svg.Skia" Version="11.2.0.2" />
```
