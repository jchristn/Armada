// Armada Dashboard - Alpine.js component
function dashboard() {
    const API = window.location.origin;
    const STORAGE_KEY = 'armada_api_key';

    // Spread extracted modules into the Alpine component
    const _modules = window.ArmadaModules || {};

    return {
        // Spread modules
        ...(_modules.status || {}),
        ...(_modules.pagination || {}),
        ...(_modules.sorting || {}),
        ...(_modules.filtering || {}),
        ...(_modules.selection || {}),
        ...(_modules.actionMenu || {}),
        ...(_modules.viewers || {}),
        ...(_modules.dataLoaders || {}),
        ...(_modules.navigation || {}),
        ...(_modules.websocket || {}),
        ...(_modules.partialLoader || {}),

        // Theme
        darkMode: false,

        // Sidebar
        sidebarCollapsed: false,
        sidebarSections: { operations: true, fleet: true, activity: true, system: true },

        // Navigation
        view: 'home',
        detailView: null,   // e.g. 'mission-detail', 'voyage-detail', etc.
        detailId: null,
        breadcrumbs: [],
        connected: false,
        wsConnected: false,
        apiConnected: false,
        ws: null,
        wsPort: null,
        pollTimer: null,

        // Auth
        authRequired: false,
        authenticated: false,
        apiKey: null,
        apiKeyInput: '',
        authError: null,
        sessionToken: null,
        whoami: null,
        loginStep: 'email',
        loginEmail: '',
        loginPassword: '',
        loginTenants: [],
        loginSelectedTenant: null,
        loginLoading: false,

        // Toast notifications
        toasts: [],
        toastCounter: 0,
        _lastSeenStates: {},

        // Notification history
        notificationHistory: [],
        unreadNotificationCount: 0,

        // Data
        status: {},
        fleets: [],
        voyages: [],
        captains: [],
        allVessels: [],
        allMissions: [],
        signals: [],
        events: [],
        mergeQueue: [],
        docks: [],
        healthInfo: null,
        backupLoading: false,
        serverSettings: null,
        doctorResults: [],
        doctorRunning: false,
        recentMissions: [],
        selectedVoyage: null,
        voyageMissions: [],

        // Detail data
        detail: null,
        detailMissions: [],
        detailDiff: null,
        detailDiffLoading: false,
        detailLog: null,

        // Viewer modal
        viewer: { open: false, title: '', content: '', copied: false },

        // Action dropdown menu
        openActionMenu: null,

        // JSON viewer modal
        jsonViewer: { open: false, title: '', subtitle: '', id: '', content: '' },

        // Log viewer modal (follow mode)
        logViewer: { open: false, title: '', content: '', entityType: '', entityId: '', following: false, lineCount: 200, timer: null, copied: false, totalLines: 0 },

        // Filters
        missionFilters: { status: '', vesselId: '', captainId: '', voyageId: '' },
        recentMissionFilters: { status: '', vesselId: '', captainId: '' },
        voyageFilters: { status: '' },
        voyageVesselFilter: '',
        voyageMissionMap: {},
        vesselFilters: { fleetId: '' },
        eventFilters: { type: '', captainId: '', missionId: '', vesselId: '', voyageId: '', limit: 50 },
        listSearch: '',

        // Pagination state per view
        missionPaging: { pageNumber: 1, totalPages: 0, totalRecords: 0, pageSize: 25, totalMs: 0 },
        voyagePaging: { pageNumber: 1, totalPages: 0, totalRecords: 0, pageSize: 25, totalMs: 0 },
        captainPaging: { pageNumber: 1, totalPages: 0, totalRecords: 0, pageSize: 25, totalMs: 0 },
        vesselPaging: { pageNumber: 1, totalPages: 0, totalRecords: 0, pageSize: 25, totalMs: 0 },
        fleetListPaging: { pageNumber: 1, totalPages: 0, totalRecords: 0, pageSize: 25, totalMs: 0 },
        signalPaging: { pageNumber: 1, totalPages: 0, totalRecords: 0, pageSize: 25, totalMs: 0 },
        eventPaging: { pageNumber: 1, totalPages: 0, totalRecords: 0, pageSize: 50, totalMs: 0 },
        mergeQueuePaging: { pageNumber: 1, totalPages: 0, totalRecords: 0, pageSize: 25, totalMs: 0 },
        dockPaging: { pageNumber: 1, totalPages: 0, totalRecords: 0, pageSize: 25, totalMs: 0 },
        mergeQueueStatusFilter: '',
        mergeQueueVoyageFilter: '',
        mergeQueueVoyages: [],
        dockFilters: { status: '' },
        // Per-column filter state
        voyageColFilters: { title: '', status: '' },
        fleetColFilters: { name: '', description: '' },
        vesselColFilters: { fleet: '', name: '', repoUrl: '', branch: '' },
        missionColFilters: { title: '', status: '', vessel: '', captain: '', branch: '' },
        captainColFilters: { name: '', state: '', runtime: '' },
        dockColFilters: { vessel: '', captain: '', branch: '', status: '' },
        signalColFilters: { type: '', from: '', to: '', payload: '' },
        eventColFilters: { type: '', message: '', entity: '', captain: '', mission: '' },
        selectedDocks: [],
        selectedMissions: [],
        selectedVoyages: [],
        selectedCaptains: [],
        selectedSignals: [],
        selectedMergeQueue: [],
        selectedEvents: [],
        selectedFleets: [],
        selectedVessels: [],

        // Sorting
        sortColumn: null,
        sortAsc: true,

        // Dispatch form
        dispatch: { title: '', description: '', vesselId: '', priority: 100, voyageId: '' },
        dispatching: false,
        dispatchResult: null,
        showDispatchForm: false,
        dispatchMode: 'quick',

        // Quick dispatch (NLP task parsing)
        quickDispatch: { prompt: '', vesselId: '' },
        quickParsedTasks: [],
        quickDispatching: false,
        quickDispatchResult: null,

        // CRUD modals
        modal: null,       // 'create-fleet', 'edit-fleet', 'create-vessel', etc.
        modalData: {},
        modalLoading: false,

        // Viewer modal (reusable for logs, diffs, etc.)
        viewerModal: false,
        viewerTitle: '',
        viewerContent: '',
        viewerRawContent: '',
        viewerIsHtml: false,
        viewerRawText: '',
        viewerLoading: false,

        // Diff viewer state
        diffViewerOpen: false,
        diffViewerTitle: '',
        diffViewerRawDiff: '',
        diffViewerFiles: [],
        diffViewerSelectedFile: null,
        diffViewerLoading: false,
        diffViewerContentHtml: '',

        // Confirm dialog
        confirmMessage: '',
        confirmResolve: null,
        confirmWidth: null,

        // Mission restart
        restartTarget: null,
        restartTitle: '',
        restartDescription: '',

        // Voyage creation
        voyageForm: { title: '', description: '', vesselId: '', missions: [{ title: '', description: '' }] },

        // ============================================================
        // Initialization
        // ============================================================
        async init() {
            // Apply theme from localStorage or system preference
            let savedTheme = localStorage.getItem('armada_theme');
            if (savedTheme) {
                this.darkMode = savedTheme === 'dark';
            } else {
                this.darkMode = window.matchMedia('(prefers-color-scheme: dark)').matches;
            }
            document.documentElement.setAttribute('data-theme', this.darkMode ? 'dark' : 'light');

            // Restore sidebar state from localStorage
            let savedCollapsed = localStorage.getItem('armada_sidebar_collapsed');
            if (savedCollapsed !== null) {
                this.sidebarCollapsed = JSON.parse(savedCollapsed);
            }
            let savedSections = localStorage.getItem('armada_sidebar_sections');
            if (savedSections !== null) {
                this.sidebarSections = JSON.parse(savedSections);
            }

            // Auto-collapse sidebar on narrow viewports
            if (window.innerWidth < 1024) {
                this.sidebarCollapsed = true;
            }
            window.addEventListener('resize', () => {
                if (window.innerWidth < 1024 && !this.sidebarCollapsed) {
                    this.sidebarCollapsed = true;
                    localStorage.setItem('armada_sidebar_collapsed', JSON.stringify(true));
                }
            });

            this.apiKey = localStorage.getItem(STORAGE_KEY);
            this.sessionToken = localStorage.getItem('armada_session_token');

            // Close action menus on click outside
            document.addEventListener('click', (e) => {
                if (this.openActionMenu && !e.target.closest('.action-menu-wrap')) {
                    this.openActionMenu = null;
                }
            });

            // Fetch health to discover WebSocket port
            try {
                let healthResp = await fetch(API + '/api/v1/status/health');
                if (healthResp.ok) {
                    let health = this.toCamel(await healthResp.json());
                    if (health.ports && health.ports.webSocket) {
                        this.wsPort = health.ports.webSocket;
                    }
                    this.healthInfo = health;
                }
            } catch (e) {
                console.warn('Failed to fetch health:', e);
            }

            // Fallback WebSocket port: admiral port + 2
            if (!this.wsPort) {
                this.wsPort = parseInt(window.location.port || '7890') + 2;
            }

            // Probe whether auth is required
            try {
                let headers = {};
                if (this.sessionToken) headers['X-Token'] = this.sessionToken;
                else if (this.apiKey) headers['X-Api-Key'] = this.apiKey;
                let resp = await fetch(API + '/api/v1/status', { headers });
                if (resp.status === 401 || resp.status === 403) {
                    this.authRequired = true;
                    // Clear stale credentials
                    if (this.sessionToken) {
                        localStorage.removeItem('armada_session_token');
                        this.sessionToken = null;
                    }
                    if (this.apiKey) {
                        localStorage.removeItem(STORAGE_KEY);
                        this.apiKey = null;
                    }
                    return;
                }
                this.apiConnected = true;
                if (this.sessionToken || this.apiKey) {
                    this.authRequired = true;
                    this.authenticated = true;
                    // Fetch whoami for user/tenant display
                    await this.fetchWhoami();
                }
            } catch (e) {
                console.warn('Failed to probe auth:', e);
            }
            await this.startDashboard();
        },

        async lookupTenants() {
            this.authError = null;
            this.loginLoading = true;
            try {
                let resp = await fetch(API + '/api/v1/tenants/lookup', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ email: this.loginEmail })
                });
                if (!resp.ok) {
                    let errText = await resp.text();
                    let errMsg = 'Lookup failed';
                    try { let e = JSON.parse(errText); errMsg = e.Message || e.message || e.Error || e.error || errMsg; } catch (_) { }
                    this.authError = errMsg;
                    return;
                }
                let data = this.toCamel(await resp.json());
                let tenants = Array.isArray(data) ? data : (data.tenants || data.objects || [data]);
                if (!tenants || tenants.length === 0) {
                    this.authError = 'No tenants found for this email.';
                    return;
                }
                this.loginTenants = tenants;
                if (tenants.length === 1) {
                    // Skip tenant selection if only one
                    this.loginSelectedTenant = tenants[0];
                    this.loginStep = 'password';
                } else {
                    this.loginStep = 'tenant';
                }
            } catch (e) {
                this.authError = 'Cannot reach server.';
            } finally {
                this.loginLoading = false;
            }
        },

        selectTenant(tenant) {
            this.loginSelectedTenant = tenant;
            this.authError = null;
            this.loginStep = 'password';
        },

        async authenticate() {
            this.authError = null;
            this.loginLoading = true;
            try {
                let tenantId = this.loginSelectedTenant
                    ? (this.loginSelectedTenant.id || this.loginSelectedTenant.tenantId)
                    : null;
                let body = {
                    email: this.loginEmail,
                    password: this.loginPassword
                };
                if (tenantId) body.tenantId = tenantId;
                let resp = await fetch(API + '/api/v1/authenticate', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(body)
                });
                if (resp.status === 401 || resp.status === 403) {
                    this.authError = 'Invalid credentials.';
                    return;
                }
                if (!resp.ok) {
                    let errText = await resp.text();
                    let errMsg = 'Authentication failed';
                    try { let e = JSON.parse(errText); errMsg = e.Message || e.message || e.Error || e.error || errMsg; } catch (_) { }
                    this.authError = errMsg;
                    return;
                }
                let data = this.toCamel(await resp.json());
                this.sessionToken = data.token || data.sessionToken || data.accessToken || null;
                if (!this.sessionToken) {
                    this.authError = 'No token returned from server.';
                    return;
                }
                localStorage.setItem('armada_session_token', this.sessionToken);
                this.loginPassword = '';
                this.authenticated = true;
                // Fetch whoami for user/tenant display
                await this.fetchWhoami();
                await this.startDashboard();
            } catch (e) {
                this.authError = 'Cannot reach server.';
            } finally {
                this.loginLoading = false;
            }
        },

        async fetchWhoami() {
            try {
                let headers = {};
                if (this.sessionToken) headers['X-Token'] = this.sessionToken;
                else if (this.apiKey) headers['X-Api-Key'] = this.apiKey;
                let resp = await fetch(API + '/api/v1/whoami', { headers });
                if (resp.ok) {
                    this.whoami = this.toCamel(await resp.json());
                }
            } catch (e) {
                console.warn('Failed to fetch whoami:', e);
            }
        },

        async login() {
            // Legacy API-key login (kept for backward compatibility)
            this.authError = null;
            try {
                let resp = await fetch(API + '/api/v1/status', {
                    headers: { 'X-Api-Key': this.apiKeyInput }
                });
                if (resp.status === 401 || resp.status === 403) {
                    this.authError = 'Invalid API key.';
                    return;
                }
                this.apiKey = this.apiKeyInput;
                this.apiKeyInput = '';
                localStorage.setItem(STORAGE_KEY, this.apiKey);
                this.authenticated = true;
                await this.fetchWhoami();
                await this.startDashboard();
            } catch (e) {
                this.authError = 'Cannot reach server.';
            }
        },

        toggleTheme() {
            this.darkMode = !this.darkMode;
            document.documentElement.setAttribute('data-theme', this.darkMode ? 'dark' : 'light');
            localStorage.setItem('armada_theme', this.darkMode ? 'dark' : 'light');
        },

        toggleSidebar() {
            this.sidebarCollapsed = !this.sidebarCollapsed;
            localStorage.setItem('armada_sidebar_collapsed', JSON.stringify(this.sidebarCollapsed));
        },

        toggleSection(section) {
            this.sidebarSections[section] = !this.sidebarSections[section];
            localStorage.setItem('armada_sidebar_sections', JSON.stringify(this.sidebarSections));
        },

        isSectionCollapsed(section) {
            return !this.sidebarSections[section];
        },

        logout() {
            localStorage.removeItem(STORAGE_KEY);
            localStorage.removeItem('armada_session_token');
            this.apiKey = null;
            this.sessionToken = null;
            this.whoami = null;
            this.authenticated = false;
            this.loginStep = 'email';
            this.loginEmail = '';
            this.loginPassword = '';
            this.loginTenants = [];
            this.loginSelectedTenant = null;
            this.authError = null;
            this.status = {};
            this.fleets = [];
            this.voyages = [];
            this.captains = [];
            this.allVessels = [];
            if (this.pollTimer) clearInterval(this.pollTimer);
            if (this.ws) this.ws.close();
        },

        async startDashboard() {
            await this.refresh();
            this.connectWebSocket();
            this.pollTimer = setInterval(() => this.refresh(), 10000);
            // Load modals partial once at startup (modals are used across all views)
            if (this.loadModalsPartial) {
                this.loadModalsPartial();
            }
            // Preload ALL view partials at startup so tab navigation only toggles
            // x-show visibility — no dynamic HTML injection during navigation.
            if (this.preloadAllPartials) {
                await this.preloadAllPartials();
            }
        },

        async refresh() {
            await Promise.all([
                this.loadStatus(),
                this.loadFleets(),
                this.loadVoyages(),
                this.loadCaptains(),
                this.loadVessels(),
                this.loadRecentMissions(),
                this.loadMergeQueue(),
                this.loadDocks(),
                this.refreshDoctorStatus()
            ]);
        },

        // refreshDoctorStatus: moved to modules/data-loaders.js

        /// <summary>
        /// Returns the aggregate health status: 'healthy', 'warning', or 'error'.
        /// Based on the worst status across all doctor check results.
        /// Note: getters cannot be spread from modules, so they remain here.
        /// </summary>
        get doctorOverallStatus() {
            if (!this.doctorResults || this.doctorResults.length === 0) return 'unknown';
            let hasWarn = false;
            for (let check of this.doctorResults) {
                let s = (check.status || '').toLowerCase();
                if (s === 'fail') return 'error';
                if (s === 'warn') hasWarn = true;
            }
            return hasWarn ? 'warning' : 'healthy';
        },

        get doctorStatusLabel() {
            let s = this.doctorOverallStatus;
            if (s === 'healthy') return 'Healthy';
            if (s === 'warning') return 'Warning';
            if (s === 'error') return 'Error';
            return '';
        },

        get doctorStatusTooltip() {
            let s = this.doctorOverallStatus;
            if (s === 'unknown') return 'Health status unknown -- click to run checks';
            let issues = (this.doctorResults || []).filter(function(c) {
                let st = (c.status || '').toLowerCase();
                return st === 'fail' || st === 'warn';
            });
            if (issues.length === 0) return 'All health checks passed';
            return issues.map(function(c) { return c.name + ': ' + c.message; }).join('; ');
        },

        async dashboardRefresh(event) {
            let btn = event.currentTarget;
            btn.disabled = true;
            btn.classList.add('refreshing');
            try {
                await this.refresh();
                btn.innerHTML = '&#x2714;';
                btn.classList.add('refresh-success');
                setTimeout(() => {
                    btn.innerHTML = '&#x21bb;';
                    btn.classList.remove('refresh-success');
                }, 1500);
            } catch (e) {
                console.warn('Dashboard refresh failed:', e);
                btn.innerHTML = '&#x21bb;';
            } finally {
                btn.disabled = false;
                btn.classList.remove('refreshing');
            }
        },

        // loadRecentMissions: moved to modules/data-loaders.js

        // ============================================================
        // API client
        // ============================================================

        // Convert object keys: camelCase -> PascalCase for request bodies
        toPascal(obj) {
            if (obj === null || obj === undefined || typeof obj !== 'object') return obj;
            if (Array.isArray(obj)) return obj.map(item => this.toPascal(item));
            let result = {};
            for (let [key, val] of Object.entries(obj)) {
                let pKey = key.charAt(0).toUpperCase() + key.slice(1);
                result[pKey] = this.toPascal(val);
            }
            return result;
        },

        // Convert object keys: PascalCase -> camelCase for response bodies
        toCamel(obj) {
            if (obj === null || obj === undefined || typeof obj !== 'object') return obj;
            if (Array.isArray(obj)) return obj.map(item => this.toCamel(item));
            let result = {};
            for (let [key, val] of Object.entries(obj)) {
                let cKey = key.charAt(0).toLowerCase() + key.slice(1);
                result[cKey] = this.toCamel(val);
            }
            return result;
        },

        async api(method, path, body) {
            let opts = {
                method: method,
                headers: { 'Content-Type': 'application/json' }
            };
            if (this.sessionToken) opts.headers['X-Token'] = this.sessionToken;
            if (this.apiKey) opts.headers['X-Api-Key'] = this.apiKey;
            if (body) opts.body = JSON.stringify(this.toPascal(body));
            let resp = await fetch(API + path, opts);
            if (resp.status === 401 || resp.status === 403) {
                this.authRequired = true;
                this.authenticated = false;
                this.loginStep = 'email';
                this.whoami = null;
                localStorage.removeItem(STORAGE_KEY);
                localStorage.removeItem('armada_session_token');
                this.apiKey = null;
                this.sessionToken = null;
                throw new Error('Authentication required');
            }
            if (resp.status === 204) return null;
            if (!resp.ok) {
                let errText = await resp.text();
                let errMsg = 'HTTP ' + resp.status;
                try { let e = JSON.parse(errText); errMsg = e.Message || e.message || e.Error || e.error || errMsg; } catch (_) { }
                throw new Error(errMsg);
            }
            let text = await resp.text();
            return text ? this.toCamel(JSON.parse(text)) : null;
        },

        // ============================================================
        // Data loaders: moved to modules/data-loaders.js
        // (loadStatus, loadFleets, loadVoyages, loadVoyageMissionMap, loadCaptains,
        //  loadVessels, loadVoyageMissions, loadMissions, loadSignals, loadEvents,
        //  loadMergeQueue, loadDocks)
        // ============================================================

        async deleteDock(id) {
            if (!await this.showConfirm('Delete dock ' + id + '? This will clean up the git worktree and cannot be undone.')) return;
            this.toast('Cleanup of dock ' + id + ' started in the background');
            this.selectedDocks = this.selectedDocks.filter(d => d !== id);
            if (this.detailView === 'dock-detail' && this.detailId === id) {
                this.goBack();
            }
            try {
                await this.api('DELETE', '/api/v1/docks/' + id);
                this.toast('Dock ' + id + ' deleted successfully');
            } catch (e) { this.toast('Failed to delete dock ' + id + ': ' + e.message, 'error'); }
            await this.loadDocks();
        },

        // Multi-select toggle, select-all, clear, deleteSelected: moved to modules/selection.js

        async deleteEvent(eventId) {
            if (!await this.showConfirm('Permanently delete event ' + eventId + '? This cannot be undone.')) return;
            try {
                await this.api('DELETE', '/api/v1/events/' + eventId);
                this.toast('Event deleted');
                if (this.detailView === 'event-detail' && this.detailId === eventId) this.goBack();
                await this.loadEvents();
            } catch (e) { this.toast('Failed: ' + e.message, 'error'); }
        },

        // loadHealth, runDoctorChecks: moved to modules/data-loaders.js

        // navigate, loadDetail: moved to modules/navigation.js and modules/data-loaders.js

        // formatDiffHtml, parseDiffFiles, renderFileDiff, renderAllDiffs, renderDiffLines,
        // openDiffViewer, closeDiffViewer, selectDiffFile, copyDiffRaw,
        // loadMissionDiff, loadMissionLog, loadCaptainLog: moved to modules/viewers.js

        // ============================================================
        // Copy to clipboard (works on both HTTP and HTTPS)
        // ============================================================
        fallbackCopy(text) {
            let ta = document.createElement('textarea');
            ta.value = text;
            ta.style.cssText = 'position:fixed;opacity:0;left:-9999px';
            document.body.appendChild(ta);
            ta.select();
            let ok = false;
            try { ok = document.execCommand('copy'); } catch (e) { }
            document.body.removeChild(ta);
            return ok;
        },

        async copyToClipboard(text, buttonEl) {
            let ok = false;
            // Strategy 1: Clipboard API (works in secure contexts)
            try {
                if (navigator.clipboard && typeof navigator.clipboard.writeText === 'function') {
                    await navigator.clipboard.writeText(text);
                    ok = true;
                }
            } catch (e) { /* fall through to fallback */ }
            // Strategy 2: textarea + execCommand fallback (works on HTTP)
            if (!ok) {
                ok = this.fallbackCopy(text);
            }
            // ALWAYS show visual feedback on the button
            if (buttonEl) {
                buttonEl.classList.add(ok ? 'copied' : 'copy-failed');
                setTimeout(() => buttonEl.classList.remove('copied', 'copy-failed'), 2000);
            }
            return ok;
        },

        copyId(id, $event) {
            let buttonEl = $event.currentTarget;
            this.copyToClipboard(id, buttonEl);
        },

        // updateBreadcrumbs, goBack: moved to modules/navigation.js

        // ============================================================
        // Confirm dialog (replaces native browser confirm())
        // ============================================================
        showConfirm(message, options) {
            this.confirmMessage = message;
            this.confirmWidth = (options && options.width) || null;
            this.modal = 'confirm-dialog';
            return new Promise((resolve) => {
                this.confirmResolve = resolve;
            });
        },

        confirmYes() {
            if (this.confirmResolve) this.confirmResolve(true);
            this.confirmResolve = null;
            this.modal = null;
            this.confirmMessage = '';
            this.confirmWidth = null;
        },

        confirmNo() {
            if (this.confirmResolve) this.confirmResolve(false);
            this.confirmResolve = null;
            this.modal = null;
            this.confirmMessage = '';
            this.confirmWidth = null;
        },

        // ============================================================
        // Toast notifications
        // ============================================================
        toast(message, type, onClick) {
            type = type || 'success';
            let id = ++this.toastCounter;
            this.toasts.push({ id, message, type, onClick: onClick || null });
            setTimeout(() => {
                this.dismissToast(id);
            }, 5000);
        },

        dismissToast(id) {
            this.toasts = this.toasts.filter(t => t.id !== id);
        },

        _stateToastSeverity(status) {
            if (!status) return 'info';
            let s = status.toLowerCase();
            if (s === 'completed' || s === 'complete' || s === 'landed' || s === 'passed') return 'success';
            if (s === 'failed' || s === 'error') return 'error';
            if (s === 'cancelled' || s === 'stalled' || s === 'stopping') return 'warning';
            return 'info';
        },

        _notifyStateChange(assetType, id, name, newStatus) {
            let key = assetType + ':' + id;
            if (this._lastSeenStates[key] === newStatus) return;
            this._lastSeenStates[key] = newStatus;
            let label = name || id;
            let truncatedLabel = label.length > 80 ? label.substring(0, 80) + '...' : label;
            let title = assetType + ' ' + newStatus;
            let message = assetType + ' "' + truncatedLabel + '" — ' + newStatus;
            let severity = this._stateToastSeverity(newStatus);
            let detailView = assetType === 'Mission' ? 'mission-detail' : (assetType === 'Voyage' ? 'voyage-detail' : 'captain-detail');
            let parentView = assetType === 'Mission' ? 'missions' : (assetType === 'Voyage' ? 'voyages' : 'captains');
            this.toast(message, severity, () => {
                this.navigate(parentView, detailView, id);
            });
            this._pushNotification({
                severity: severity,
                title: title,
                message: message,
                missionId: assetType === 'Mission' ? id : null,
                voyageId: assetType === 'Voyage' ? id : null,
                captainId: assetType === 'Captain' ? id : null,
            });
        },

        _pushNotification(opts) {
            let notification = {
                id: 'ntf_' + Date.now() + '_' + Math.random().toString(36).substr(2, 6),
                severity: opts.severity || 'info',
                title: opts.title || '',
                message: opts.message || '',
                timestampUtc: new Date().toISOString(),
                missionId: opts.missionId || null,
                voyageId: opts.voyageId || null,
                captainId: opts.captainId || null,
                read: false,
            };
            this.notificationHistory.unshift(notification);
            if (this.notificationHistory.length > 100) {
                this.notificationHistory = this.notificationHistory.slice(0, 100);
            }
            this.unreadNotificationCount = this.notificationHistory.filter(n => !n.read).length;
        },

        markNotificationRead(notification) {
            if (!notification.read) {
                notification.read = true;
                this.unreadNotificationCount = this.notificationHistory.filter(n => !n.read).length;
            }
            if (notification.missionId) {
                this.navigate('missions', 'mission-detail', notification.missionId);
            } else if (notification.voyageId) {
                this.navigate('voyages', 'voyage-detail', notification.voyageId);
            } else if (notification.captainId) {
                this.navigate('captains', 'captain-detail', notification.captainId);
            }
        },

        markAllNotificationsRead() {
            this.notificationHistory.forEach(n => n.read = true);
            this.unreadNotificationCount = 0;
        },

        clearNotificationHistory() {
            this.notificationHistory = [];
            this.unreadNotificationCount = 0;
        },

        // ============================================================
        // Captain actions
        // ============================================================
        async recallCaptain(captainId) {
            if (!await this.showConfirm('Recall captain ' + captainId + '? This will stop their current mission.')) return;
            try {
                await this.api('POST', '/api/v1/captains/' + captainId + '/stop');
                this.toast('Captain recalled');
                await this.refresh();
            } catch (e) { this.toast('Failed to recall captain: ' + e.message, 'error'); }
        },

        async stopAllCaptains() {
            if (!await this.showConfirm('Stop ALL working captains? This will halt all active missions.')) return;
            try {
                let working = this.captains.filter(c => c.state === 'Working' || c.state === 'Stalled');
                for (let cpt of working) {
                    await this.api('POST', '/api/v1/captains/' + cpt.id + '/stop');
                }
                this.toast('All captains stopped (' + working.length + ')');
                await this.refresh();
            } catch (e) { this.toast('Failed: ' + e.message, 'error'); }
        },

        async removeCaptain(captainId) {
            if (!await this.showConfirm('Remove captain ' + captainId + '? This cannot be undone.')) return;
            try {
                await this.api('DELETE', '/api/v1/captains/' + captainId);
                this.toast('Captain removed');
                await this.refresh();
                if (this.detailView === 'captain-detail' && this.detailId === captainId) this.goBack();
            } catch (e) { this.toast('Failed: ' + e.message, 'error'); }
        },

        // ============================================================
        // Dispatch / Mission CRUD
        // ============================================================
        async dispatchMission() {
            this.dispatching = true;
            this.dispatchResult = null;
            try {
                let body = {
                    title: this.dispatch.title,
                    description: this.dispatch.description,
                    vesselId: this.dispatch.vesselId,
                    priority: parseInt(this.dispatch.priority) || 100
                };
                if (this.dispatch.voyageId) body.voyageId = this.dispatch.voyageId;
                let mission = await this.api('POST', '/api/v1/missions', body);
                this.dispatchResult = { ok: true, message: 'Mission dispatched: ' + (mission.id || 'OK') };
                this.dispatch = { title: '', description: '', vesselId: '', priority: 100, voyageId: '' };
                this.toast('Mission dispatched');
                await this.refresh();
            } catch (e) {
                this.dispatchResult = { ok: false, message: 'Failed: ' + e.message };
                this.toast('Dispatch failed: ' + e.message, 'error');
            } finally {
                this.dispatching = false;
            }
        },

        parseTasks(prompt) {
            if (!prompt || !prompt.trim()) return [];
            // 1. Try numbered list regex
            let numbered = [];
            let re = /(?:^|\n)\s*(\d+)\.\s+(.+?)(?=\n\s*\d+\.\s|$)/gs;
            let m;
            while ((m = re.exec(prompt)) !== null) {
                let text = m[2].trim();
                if (text) numbered.push(text);
            }
            if (numbered.length >= 2) return numbered;
            // 2. Try semicolon split
            let parts = prompt.split(';').map(s => s.trim()).filter(s => s.length > 0);
            if (parts.length >= 2) return parts;
            // 3. Fallback: entire prompt is one task
            return [prompt.trim()];
        },

        previewQuickTasks() {
            this.quickParsedTasks = this.parseTasks(this.quickDispatch.prompt);
        },

        async quickDispatchVoyage() {
            let tasks = this.quickParsedTasks;
            if (!tasks.length) {
                tasks = this.parseTasks(this.quickDispatch.prompt);
                this.quickParsedTasks = tasks;
            }
            if (!tasks.length) return;
            let vesselId = this.quickDispatch.vesselId;
            if (!vesselId) { this.toast('Please select a vessel', 'error'); return; }

            this.quickDispatching = true;
            this.quickDispatchResult = null;
            try {
                let title = tasks.length > 1 ? 'Multi-task voyage' : tasks[0].substring(0, 80);
                let missions = tasks.map(t => ({ title: t, description: t }));
                let body = { title: title, vesselId: vesselId, missions: missions };
                let voyage = await this.api('POST', '/api/v1/voyages', body);
                let missionCount = missions.length;
                this.quickDispatchResult = { ok: true, message: 'Dispatched 1 voyage with ' + missionCount + ' mission' + (missionCount !== 1 ? 's' : '') };
                this.toast('Voyage dispatched: ' + voyage.id);
                this.quickDispatch = { prompt: '', vesselId: vesselId };
                this.quickParsedTasks = [];
                await this.refresh();
            } catch (e) {
                this.quickDispatchResult = { ok: false, message: 'Failed: ' + e.message };
                this.toast('Dispatch failed: ' + e.message, 'error');
            } finally {
                this.quickDispatching = false;
            }
        },

        async cancelMission(missionId) {
            if (!await this.showConfirm('Cancel mission ' + missionId + '?')) return;
            try {
                await this.api('DELETE', '/api/v1/missions/' + missionId);
                this.toast('Mission cancelled');
                await this.refresh();
                if (this.detailView === 'mission-detail') this.loadDetail('mission-detail', missionId);
            } catch (e) { this.toast('Failed: ' + e.message, 'error'); }
        },

        async deleteMission(missionId) {
            if (!await this.showConfirm('Permanently delete mission ' + missionId + '? This cannot be undone.')) return;
            try {
                await this.api('DELETE', '/api/v1/missions/' + missionId + '/purge');
                this.toast('Mission deleted');
                if (this.detailView === 'mission-detail' && this.detailId === missionId) this.goBack();
                await this.refresh();
            } catch (e) { this.toast('Failed: ' + e.message, 'error'); }
        },

        restartMissionPrompt(mission) {
            this.restartTarget = mission;
            this.restartTitle = mission.title || '';
            this.restartDescription = mission.description || '';
            this.modal = 'restart-mission';
        },

        async restartMission() {
            if (!this.restartTarget) return;
            try {
                this.modalLoading = true;
                let body = {};
                if (this.restartTitle !== this.restartTarget.title) body.title = this.restartTitle;
                if (this.restartDescription !== this.restartTarget.description) body.description = this.restartDescription;
                let missionId = this.restartTarget.id;
                await this.api('POST', '/api/v1/missions/' + missionId + '/restart', body);
                this.toast('Mission restarted: ' + missionId);
                this.modal = null;
                this.restartTarget = null;
                await this.refresh();
                if (this.detailView === 'mission-detail') this.loadDetail('mission-detail', missionId);
            } catch (e) { this.toast('Failed: ' + e.message, 'error'); }
            finally { this.modalLoading = false; }
        },

        async transitionMissionStatus(missionId, newStatus) {
            try {
                await this.api('PUT', '/api/v1/missions/' + missionId + '/status', { status: newStatus });
                this.toast('Mission status changed to ' + newStatus);
                if (this.detailView === 'mission-detail') this.loadDetail('mission-detail', missionId);
                await this.refresh();
            } catch (e) { this.toast('Failed: ' + e.message, 'error'); }
        },

        async saveMissionEdit() {
            this.modalLoading = true;
            try {
                await this.api('PUT', '/api/v1/missions/' + this.modalData.id, {
                    title: this.modalData.title,
                    description: this.modalData.description,
                    priority: parseInt(this.modalData.priority) || 100,
                    vesselId: this.modalData.vesselId,
                    voyageId: this.modalData.voyageId
                });
                this.toast('Mission updated');
                this.modal = null;
                if (this.detailView === 'mission-detail') this.loadDetail('mission-detail', this.modalData.id);
                await this.refresh();
            } catch (e) { this.toast('Failed: ' + e.message, 'error'); }
            finally { this.modalLoading = false; }
        },

        // ============================================================
        // Voyage CRUD
        // ============================================================
        async createVoyage() {
            this.modalLoading = true;
            try {
                let missions = this.voyageForm.missions
                    .filter(m => m.title.trim())
                    .map(m => ({ title: m.title, description: m.description }));
                let body = {
                    title: this.voyageForm.title,
                    description: this.voyageForm.description,
                    vesselId: this.voyageForm.vesselId,
                    missions: missions
                };
                let voyage = await this.api('POST', '/api/v1/voyages', body);
                this.toast('Voyage created: ' + voyage.id);
                this.modal = null;
                this.voyageForm = { title: '', description: '', vesselId: '', missions: [{ title: '', description: '' }] };
                await this.refresh();
            } catch (e) { this.toast('Failed: ' + e.message, 'error'); }
            finally { this.modalLoading = false; }
        },

        addVoyageMission() {
            this.voyageForm.missions.push({ title: '', description: '' });
        },

        removeVoyageMission(index) {
            this.voyageForm.missions.splice(index, 1);
        },

        async cancelVoyage(voyageId) {
            if (!await this.showConfirm('Cancel voyage ' + voyageId + '? All pending missions will be cancelled.')) return;
            try {
                await this.api('DELETE', '/api/v1/voyages/' + voyageId);
                this.toast('Voyage cancelled');
                await this.refresh();
                if (this.detailView === 'voyage-detail') this.loadDetail('voyage-detail', voyageId);
            } catch (e) { this.toast('Failed: ' + e.message, 'error'); }
        },

        async deleteVoyage(voyageId) {
            if (!await this.showConfirm('Permanently delete voyage ' + voyageId + ' and all its missions? This cannot be undone.')) return;
            try {
                await this.api('DELETE', '/api/v1/voyages/' + voyageId + '/purge');
                this.toast('Voyage deleted');
                if (this.detailView === 'voyage-detail' && this.detailId === voyageId) this.goBack();
                await this.refresh();
            } catch (e) { this.toast('Failed: ' + e.message, 'error'); }
        },

        async retryVoyageMissions(voyageId) {
            if (!await this.showConfirm('Retry all failed missions in this voyage?')) return;
            try {
                let result = await this.api('GET', '/api/v1/voyages/' + voyageId);
                let missions = result.missions || [];
                let failed = missions.filter(m => m.status === 'Failed');
                for (let m of failed) {
                    await this.api('POST', '/api/v1/missions', {
                        title: m.title,
                        description: m.description,
                        vesselId: m.vesselId,
                        voyageId: m.voyageId,
                        priority: m.priority
                    });
                }
                this.toast('Retried ' + failed.length + ' failed missions');
                await this.refresh();
                if (this.detailView === 'voyage-detail') this.loadDetail('voyage-detail', voyageId);
            } catch (e) { this.toast('Failed: ' + e.message, 'error'); }
        },

        // ============================================================
        // Fleet CRUD
        // ============================================================
        async createFleet() {
            this.modalLoading = true;
            try {
                let fleet = await this.api('POST', '/api/v1/fleets', {
                    name: this.modalData.name,
                    description: this.modalData.description
                });
                this.toast('Fleet created: ' + fleet.name);
                this.modal = null;
                await this.loadFleets();
            } catch (e) { this.toast('Failed: ' + e.message, 'error'); }
            finally { this.modalLoading = false; }
        },

        async saveFleetEdit() {
            this.modalLoading = true;
            try {
                await this.api('PUT', '/api/v1/fleets/' + this.modalData.id, {
                    name: this.modalData.name,
                    description: this.modalData.description
                });
                this.toast('Fleet updated');
                this.modal = null;
                await this.loadFleets();
                if (this.detailView === 'fleet-detail') this.loadDetail('fleet-detail', this.modalData.id);
            } catch (e) { this.toast('Failed: ' + e.message, 'error'); }
            finally { this.modalLoading = false; }
        },

        async deleteFleet(fleetId) {
            if (!await this.showConfirm('Delete fleet ' + fleetId + '? This cannot be undone.')) return;
            try {
                await this.api('DELETE', '/api/v1/fleets/' + fleetId);
                this.toast('Fleet deleted');
                await this.loadFleets();
                if (this.detailView === 'fleet-detail' && this.detailId === fleetId) this.goBack();
            } catch (e) { this.toast('Failed: ' + e.message, 'error'); }
        },

        // ============================================================
        // Vessel CRUD
        // ============================================================
        async createVessel() {
            this.modalLoading = true;
            try {
                let vessel = await this.api('POST', '/api/v1/vessels', {
                    name: this.modalData.name,
                    repoUrl: this.modalData.repoUrl,
                    defaultBranch: this.modalData.defaultBranch || 'main',
                    fleetId: this.modalData.fleetId || null,
                    localPath: this.modalData.localPath || null,
                    workingDirectory: this.modalData.workingDirectory || null,
                    projectContext: this.modalData.projectContext || null,
                    styleGuide: this.modalData.styleGuide || null
                });
                this.toast('Vessel created: ' + vessel.name);
                this.modal = null;
                await this.loadFleets();
                await this.loadVessels();
            } catch (e) { this.toast('Failed: ' + e.message, 'error'); }
            finally { this.modalLoading = false; }
        },

        async saveVesselEdit() {
            this.modalLoading = true;
            try {
                await this.api('PUT', '/api/v1/vessels/' + this.modalData.id, {
                    name: this.modalData.name,
                    repoUrl: this.modalData.repoUrl,
                    defaultBranch: this.modalData.defaultBranch || 'main',
                    fleetId: this.modalData.fleetId || null,
                    localPath: this.modalData.localPath || null,
                    workingDirectory: this.modalData.workingDirectory || null,
                    projectContext: this.modalData.projectContext || null,
                    styleGuide: this.modalData.styleGuide || null
                });
                this.toast('Vessel updated');
                this.modal = null;
                await this.loadFleets();
                await this.loadVessels();
                if (this.detailView === 'vessel-detail') this.loadDetail('vessel-detail', this.modalData.id);
            } catch (e) { this.toast('Failed: ' + e.message, 'error'); }
            finally { this.modalLoading = false; }
        },

        async deleteVessel(vesselId) {
            if (!await this.showConfirm('Delete vessel ' + vesselId + '? This cannot be undone.')) return;
            try {
                await this.api('DELETE', '/api/v1/vessels/' + vesselId);
                this.toast('Vessel deleted');
                await this.loadFleets();
                await this.loadVessels();
                if (this.detailView === 'vessel-detail' && this.detailId === vesselId) this.goBack();
            } catch (e) { this.toast('Failed: ' + e.message, 'error'); }
        },

        // ============================================================
        // Captain CRUD
        // ============================================================
        async addCaptain() {
            this.modalLoading = true;
            try {
                let captain = await this.api('POST', '/api/v1/captains', {
                    name: this.modalData.name,
                    runtime: this.modalData.runtime || 'ClaudeCode'
                });
                this.toast('Captain added: ' + captain.name);
                this.modal = null;
                await this.loadCaptains();
            } catch (e) { this.toast('Failed: ' + e.message, 'error'); }
            finally { this.modalLoading = false; }
        },

        async saveCaptainEdit() {
            this.modalLoading = true;
            try {
                await this.api('PUT', '/api/v1/captains/' + this.modalData.id, {
                    name: this.modalData.name,
                    runtime: this.modalData.runtime
                });
                this.toast('Captain updated');
                this.modal = null;
                if (this.detailView === 'captain-detail') this.loadDetail('captain-detail', this.modalData.id);
                await this.loadCaptains();
            } catch (e) { this.toast('Failed: ' + e.message, 'error'); }
            finally { this.modalLoading = false; }
        },

        // ============================================================
        // Signal CRUD
        // ============================================================
        async sendSignal() {
            this.modalLoading = true;
            try {
                await this.api('POST', '/api/v1/signals', {
                    type: this.modalData.type || 'Nudge',
                    payload: this.modalData.payload || '',
                    fromCaptainId: this.modalData.fromCaptainId || null,
                    toCaptainId: this.modalData.toCaptainId || null
                });
                this.toast('Signal sent');
                this.modal = null;
                await this.loadSignals();
            } catch (e) { this.toast('Failed: ' + e.message, 'error'); }
            finally { this.modalLoading = false; }
        },

        // ============================================================
        // Merge Queue
        // ============================================================
        async enqueueMerge() {
            this.modalLoading = true;
            try {
                let entry = await this.api('POST', '/api/v1/merge-queue', {
                    missionId: this.modalData.missionId || null,
                    vesselId: this.modalData.vesselId || null,
                    branchName: this.modalData.branchName,
                    targetBranch: this.modalData.targetBranch || 'main'
                });
                this.toast('Enqueued: ' + entry.id);
                this.modal = null;
                await this.loadMergeQueue();
            } catch (e) { this.toast('Failed: ' + e.message, 'error'); }
            finally { this.modalLoading = false; }
        },

        async cancelMergeEntry(entryId) {
            if (!await this.showConfirm('Cancel merge entry ' + entryId + '?')) return;
            try {
                await this.api('DELETE', '/api/v1/merge-queue/' + entryId);
                this.toast('Merge entry cancelled');
                await this.loadMergeQueue();
            } catch (e) { this.toast('Failed: ' + e.message, 'error'); }
        },

        async deleteMergeEntry(entry) {
            let vessel = entry.vesselId ? this.vesselName(entry.vesselId) : 'unknown repo';
            let msg = 'Permanently delete this merge entry?\n\n'
                + 'Branch: ' + (entry.branchName || '(none)') + '\n'
                + 'Repo: ' + vessel + '\n\n'
                + 'This will delete the branch from both local and remote repositories.';
            if (!await this.showConfirm(msg, { width: '880px' })) return;
            try {
                await this.api('DELETE', '/api/v1/merge-queue/' + entry.id);
                this.toast('Merge entry and branch deleted');
                await this.loadMergeQueue();
            } catch (e) { this.toast('Failed: ' + e.message, 'error'); }
        },

        async mergeSingleEntry(entryId) {
            if (!await this.showConfirm('Merge this entry now?')) return;
            try {
                await this.api('POST', '/api/v1/merge-queue/' + entryId + '/process');
                this.toast('Merge entry processed');
                await this.loadMergeQueue();
            } catch (e) { this.toast('Failed: ' + e.message, 'error'); }
        },

        async processMergeQueue() {
            if (!await this.showConfirm('Process the merge queue now?')) return;
            try {
                await this.api('POST', '/api/v1/merge-queue/process');
                this.toast('Merge queue processing triggered');
                await this.loadMergeQueue();
            } catch (e) { this.toast('Failed: ' + e.message, 'error'); }
        },

        // ============================================================
        // Server
        // ============================================================
        async stopServer() {
            if (!await this.showConfirm('Stop the Admiral server? This will shut down everything.')) return;
            try {
                await this.api('POST', '/api/v1/server/stop');
                this.toast('Server shutting down...');
            } catch (e) { this.toast('Failed: ' + e.message, 'error'); }
        },

        // loadSettings: moved to modules/data-loaders.js

        async saveServerConfig() {
            try {
                this.serverSettings = await this.api('PUT', '/api/v1/settings', {
                    admiralPort: this.serverSettings.admiralPort,
                    mcpPort: this.serverSettings.mcpPort,
                    maxCaptains: this.serverSettings.maxCaptains
                });
                this.toast('Server configuration saved');
            } catch (e) { this.toast('Failed: ' + e.message, 'error'); }
        },

        async saveAgentSettings() {
            try {
                this.serverSettings = await this.api('PUT', '/api/v1/settings', {
                    heartbeatIntervalSeconds: this.serverSettings.heartbeatIntervalSeconds,
                    stallThresholdMinutes: this.serverSettings.stallThresholdMinutes,
                    idleCaptainTimeoutSeconds: this.serverSettings.idleCaptainTimeoutSeconds,
                    autoCreatePr: this.serverSettings.autoCreatePr
                });
                this.toast('Agent settings saved');
            } catch (e) { this.toast('Failed: ' + e.message, 'error'); }
        },

        async healthCheck() {
            try {
                let result = await this.api('GET', '/api/v1/status/health');
                this.healthInfo = result;
                this.toast('Health: ' + result.status + ' | Uptime: ' + result.uptime);
            } catch (e) { this.toast('Health check failed: ' + e.message, 'error'); }
        },

        async factoryReset() {
            if (!await this.showConfirm('WARNING: Factory reset will delete ALL data including the database, logs, docks, and repos. Settings will be preserved. This cannot be undone. Continue?')) return;
            try {
                let result = await this.api('POST', '/api/v1/server/reset');
                this.toast(result.message || 'Factory reset complete', 'success');
            } catch (e) { this.toast('Factory reset failed: ' + e.message, 'error'); }
        },

        async backupNow() {
            this.backupLoading = true;
            try {
                let opts = { method: 'GET', headers: {} };
                if (this.apiKey) opts.headers['X-Api-Key'] = this.apiKey;
                let resp = await fetch(API + '/api/v1/backup', opts);
                if (!resp.ok) {
                    let errText = await resp.text();
                    let errMsg = 'HTTP ' + resp.status;
                    try { let e = JSON.parse(errText); errMsg = e.Message || e.message || e.Error || e.error || errMsg; } catch (_) { }
                    throw new Error(errMsg);
                }
                let disposition = resp.headers.get('Content-Disposition') || '';
                let filename = 'armada-backup.zip';
                let match = disposition.match(/filename="?([^";\s]+)"?/);
                if (match) filename = match[1];
                let blob = await resp.blob();
                let url = URL.createObjectURL(blob);
                let a = document.createElement('a');
                a.href = url;
                a.download = filename;
                document.body.appendChild(a);
                a.click();
                document.body.removeChild(a);
                URL.revokeObjectURL(url);
                this.toast('Backup downloaded: ' + filename, 'success');
            } catch (e) { this.toast('Backup failed: ' + e.message, 'error'); }
            finally { this.backupLoading = false; }
        },

        restoreFromBackup() {
            this.$refs.restoreFileInput.value = '';
            this.$refs.restoreFileInput.click();
        },

        async handleRestoreFile(event) {
            let file = event.target.files[0];
            if (!file) return;
            if (!await this.showConfirm('This will replace the current database with the backup. A safety backup will be created first. The server should be restarted after restore. Continue?')) return;
            try {
                let opts = { method: 'POST', headers: { 'Content-Type': 'application/zip', 'X-Original-Filename': file.name }, body: file };
                if (this.apiKey) opts.headers['X-Api-Key'] = this.apiKey;
                let resp = await fetch(API + '/api/v1/restore', opts);
                if (!resp.ok) {
                    let errText = await resp.text();
                    let errMsg = 'HTTP ' + resp.status;
                    try { let e = JSON.parse(errText); errMsg = e.Message || e.message || e.Error || e.error || errMsg; } catch (_) { }
                    throw new Error(errMsg);
                }
                let result = await resp.json();
                this.toast(result.Message || result.message || 'Restore complete', 'success');
            } catch (e) { this.toast('Restore failed: ' + e.message, 'error'); }
        },

        getMcpConfigHttp() {
            let port = this.serverSettings?.mcpPort || 8001;
            return JSON.stringify({ type: 'http', url: 'http://localhost:' + port }, null, 2);
        },

        getMcpConfigStdio() {
            return JSON.stringify({ type: 'stdio', command: 'armada', args: ['mcp', 'stdio'] }, null, 2);
        },

        copyMcpConfig(type, buttonEl) {
            let text = type === 'http' ? this.getMcpConfigHttp() : this.getMcpConfigStdio();
            this.copyToClipboard(text, buttonEl);
        },

        // connectWebSocket, handleWsMessage: moved to modules/websocket.js

        // ============================================================
        // Helpers
        // ============================================================
        totalMissions() {
            if (!this.status.missionsByStatus) return 0;
            return Object.values(this.status.missionsByStatus).reduce((a, b) => a + b, 0);
        },

        voyagePercent(vp) {
            if (!vp.totalMissions || vp.totalMissions === 0) return 0;
            return Math.round((vp.completedMissions / vp.totalMissions) * 100);
        },

        // formatTime, formatTimeAbsolute: moved to modules/status.js

        vesselName(vesselId) {
            if (!vesselId) return '-';
            let v = this.allVessels.find(v => v.id === vesselId);
            return v ? v.name : vesselId;
        },

        captainName(captainId) {
            if (!captainId) return '-';
            let c = this.captains.find(c => c.id === captainId);
            return c ? c.name : captainId;
        },

        fleetName(fleetId) {
            if (!fleetId) return '-';
            let f = this.fleets.find(f => f.id === fleetId);
            return f ? f.name : fleetId;
        },

        // statusTooltip: moved to modules/status.js

        // entityNav: moved to modules/navigation.js
        // filterMatch: moved to modules/sorting.js

        // Column filtering and client-side filters: moved to modules/filtering.js

        // filterRows: moved to modules/sorting.js

        // Client-side filter for Fleets & Vessels
        filteredFleets() {
            if (!this.listSearch) {
                return this.fleets.map(f => {
                    f._filteredVessels = f.vessels || [];
                    return f;
                });
            }
            let q = this.listSearch.toLowerCase();
            let result = [];
            for (let fleet of this.fleets) {
                let fleetNameMatch = (fleet.name || '').toLowerCase().includes(q) ||
                                     (fleet.id || '').toLowerCase().includes(q);
                if (fleetNameMatch) {
                    fleet._filteredVessels = fleet.vessels || [];
                    result.push(fleet);
                } else {
                    let matchingVessels = (fleet.vessels || []).filter(v =>
                        (v.name || '').toLowerCase().includes(q) ||
                        (v.id || '').toLowerCase().includes(q) ||
                        (v.repoUrl || '').toLowerCase().includes(q) ||
                        (v.defaultBranch || '').toLowerCase().includes(q)
                    );
                    if (matchingVessels.length > 0) {
                        fleet._filteredVessels = matchingVessels;
                        result.push(fleet);
                    }
                }
            }
            return result;
        },

        // filteredVesselsList, filteredRecentMissions, filteredMergeQueue, filteredVoyages: moved to modules/filtering.js

        // Column sorting
        // sortBy, sortedRows, sortIcon: moved to modules/sorting.js

        // Modal openers
        openCreateFleet() { this.modal = 'create-fleet'; this.modalData = { name: '', description: '' }; },
        openEditFleet(f) { this.modal = 'edit-fleet'; this.modalData = { id: f.id, name: f.name, description: f.description || '' }; },
        openCreateVessel(fleetId) { this.modal = 'create-vessel'; this.modalData = { name: '', repoUrl: '', defaultBranch: 'main', fleetId: fleetId || '', localPath: '', workingDirectory: '', projectContext: '', styleGuide: '' }; },
        openEditVessel(v) { this.modal = 'edit-vessel'; this.modalData = { id: v.id, name: v.name, repoUrl: v.repoUrl || '', defaultBranch: v.defaultBranch || 'main', fleetId: v.fleetId || '', localPath: v.localPath || '', workingDirectory: v.workingDirectory || '', projectContext: v.projectContext || '', styleGuide: v.styleGuide || '' }; },
        openAddCaptain() { this.modal = 'add-captain'; this.modalData = { name: '', runtime: 'ClaudeCode' }; },
        openEditCaptain(c) { this.modal = 'edit-captain'; this.modalData = { id: c.id, name: c.name, runtime: c.runtime || 'ClaudeCode' }; },
        openEditMission(m) { this.modal = 'edit-mission'; this.modalData = { id: m.id, title: m.title, description: m.description || '', priority: m.priority || 100, vesselId: m.vesselId || '', voyageId: m.voyageId || '' }; },
        openCreateVoyage() { this.modal = 'create-voyage'; this.voyageForm = { title: '', description: '', vesselId: '', missions: [{ title: '', description: '' }] }; },
        openSendSignal() { this.modal = 'send-signal'; this.modalData = { type: 'Nudge', payload: '', fromCaptainId: '', toCaptainId: '' }; },
        openEnqueueMerge() { this.modal = 'enqueue-merge'; this.modalData = { missionId: '', vesselId: '', branchName: '', targetBranch: 'main' }; },

        // toggleActionMenu, closeActionMenu: moved to modules/action-menu.js

        // showJson, closeJsonViewer, copyJsonContent, openViewer, closeViewer,
        // copyViewerContent, viewMissionDiff, viewMissionLog, viewCaptainLog,
        // openLogViewer, fetchLogContent, toggleLogFollow, stopLogFollow,
        // changeLogLineCount, closeLogViewer, copyLogContent: moved to modules/viewers.js

        // goToPage, nextPage, prevPage, changePageSize, paginateLocal, noopLoad: moved to modules/pagination.js

        // Keyboard shortcuts
        handleKeyboard(e) {
            if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA' || e.target.tagName === 'SELECT') return;
            if (this.jsonViewer.open && e.key === 'Escape') { this.closeJsonViewer(); return; }
            if (this.diffViewerOpen && e.key === 'Escape') { this.closeDiffViewer(); return; }
            if (this.logViewer.open && e.key === 'Escape') { this.closeLogViewer(); return; }
            if (this.viewer.open && e.key === 'Escape') { this.closeViewer(); return; }
            if (this.modal) {
                if (e.key === 'Escape') this.modal = null;
                return;
            }
            if (e.key === 'r') this.refresh();
        }
    };
}
