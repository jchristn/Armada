// Armada Dashboard - Alpine.js component
function dashboard() {
    const API = window.location.origin;
    const WS_PROTOCOL = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
    const STORAGE_KEY = 'armada_api_key';

    return {
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
        diffViewerCopied: false,

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
                let resp = await fetch(API + '/api/v1/status', {
                    headers: this.apiKey ? { 'X-Api-Key': this.apiKey } : {}
                });
                if (resp.status === 401 || resp.status === 403) {
                    this.authRequired = true;
                    if (this.apiKey) {
                        localStorage.removeItem(STORAGE_KEY);
                        this.apiKey = null;
                    }
                    return;
                }
                this.apiConnected = true;
                if (this.apiKey) {
                    this.authRequired = true;
                    this.authenticated = true;
                }
            } catch (e) {
                console.warn('Failed to probe auth:', e);
            }
            await this.startDashboard();
        },

        async login() {
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
            this.apiKey = null;
            this.authenticated = false;
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

        /// <summary>
        /// Silently refresh doctor health checks for the top-bar indicator.
        /// Unlike runDoctorChecks(), this does not set doctorRunning or clear results on start.
        /// </summary>
        async refreshDoctorStatus() {
            try {
                this.doctorResults = await this.api('GET', '/api/v1/doctor');
            } catch (e) {
                // Silently fail -- top-bar will show unknown state
            }
        },

        /// <summary>
        /// Returns the aggregate health status: 'healthy', 'warning', or 'error'.
        /// Based on the worst status across all doctor check results.
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

        /// <summary>
        /// Returns the label text for the top-bar health indicator.
        /// </summary>
        get doctorStatusLabel() {
            let s = this.doctorOverallStatus;
            if (s === 'healthy') return 'Healthy';
            if (s === 'warning') return 'Warning';
            if (s === 'error') return 'Error';
            return '';
        },

        /// <summary>
        /// Returns a tooltip for the top-bar health indicator with details.
        /// </summary>
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

        async loadRecentMissions() {
            try {
                let result = await this.api('GET', '/api/v1/missions?pageSize=10&order=CreatedDescending');
                this.recentMissions = (result && result.objects) ? result.objects : [];
                if (!Array.isArray(this.recentMissions)) this.recentMissions = [];
            } catch (e) { console.warn('Failed to load recent missions:', e); }
        },

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
            if (this.apiKey) opts.headers['X-Api-Key'] = this.apiKey;
            if (body) opts.body = JSON.stringify(this.toPascal(body));
            let resp = await fetch(API + path, opts);
            if (resp.status === 401 || resp.status === 403) {
                this.authRequired = true;
                this.authenticated = false;
                localStorage.removeItem(STORAGE_KEY);
                this.apiKey = null;
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
        // Data loaders
        // ============================================================
        async loadStatus() {
            try {
                this.status = await this.api('GET', '/api/v1/status');
                this.apiConnected = true;
                this.connected = true;
            } catch (e) {
                console.warn('Failed to load status:', e);
                this.apiConnected = false;
                this.connected = this.wsConnected;
            }
        },

        async loadFleets() {
            try {
                let result = await this.api('GET', '/api/v1/fleets?pageSize=1000');
                let fleets = (result && result.objects) ? result.objects : [];
                if (!Array.isArray(fleets)) { console.warn('Fleets response is not an array:', fleets); fleets = []; }
                for (let fleet of fleets) {
                    try {
                        let vesselResult = await this.api('GET', '/api/v1/vessels?fleetId=' + fleet.id + '&pageSize=1000');
                        fleet.vessels = (vesselResult && vesselResult.objects) ? vesselResult.objects : [];
                        if (!Array.isArray(fleet.vessels)) fleet.vessels = [];
                    } catch (e) { fleet.vessels = []; }
                    fleet._vesselCount = (fleet.vessels || []).length;
                }
                this.fleets = fleets;
            } catch (e) { console.warn('Failed to load fleets:', e); }
        },

        async loadVoyages() {
            try {
                let params = [];
                if (this.voyageFilters.status) params.push('status=' + this.voyageFilters.status);
                if (this.view === 'voyages') {
                    params.push('pageNumber=' + this.voyagePaging.pageNumber);
                    params.push('pageSize=' + this.voyagePaging.pageSize);
                } else {
                    params.push('pageSize=1000');
                }
                let url = '/api/v1/voyages' + (params.length ? '?' + params.join('&') : '');
                let result = await this.api('GET', url);
                this.voyages = (result && result.objects) ? result.objects : [];
                if (!Array.isArray(this.voyages)) this.voyages = [];
                if (result && this.view === 'voyages') {
                    this.voyagePaging.pageNumber = result.pageNumber || 1;
                    this.voyagePaging.totalPages = result.totalPages || 0;
                    this.voyagePaging.totalRecords = result.totalRecords || 0;
                    this.voyagePaging.totalMs = result.totalMs || 0;
                }
            } catch (e) { console.warn('Failed to load voyages:', e); }
        },

        async loadVoyageMissionMap() {
            try {
                let result = await this.api('GET', '/api/v1/missions?pageSize=1000');
                let missions = (result && result.objects) ? result.objects : [];
                let map = {};
                for (let m of missions) {
                    if (m.voyageId) {
                        if (!map[m.voyageId]) map[m.voyageId] = [];
                        if (m.vesselId && !map[m.voyageId].includes(m.vesselId)) map[m.voyageId].push(m.vesselId);
                    }
                }
                this.voyageMissionMap = map;
            } catch (e) { console.warn('Failed to load voyage mission map:', e); }
        },

        async loadCaptains() {
            try {
                let params = [];
                if (this.view === 'captains') {
                    params.push('pageNumber=' + this.captainPaging.pageNumber);
                    params.push('pageSize=' + this.captainPaging.pageSize);
                } else {
                    params.push('pageSize=1000');
                }
                let url = '/api/v1/captains' + (params.length ? '?' + params.join('&') : '');
                let result = await this.api('GET', url);
                this.captains = (result && result.objects) ? result.objects : [];
                if (!Array.isArray(this.captains)) this.captains = [];
                if (result && this.view === 'captains') {
                    this.captainPaging.pageNumber = result.pageNumber || 1;
                    this.captainPaging.totalPages = result.totalPages || 0;
                    this.captainPaging.totalRecords = result.totalRecords || 0;
                    this.captainPaging.totalMs = result.totalMs || 0;
                }
            } catch (e) { console.warn('Failed to load captains:', e); }
        },

        async loadVessels() {
            try {
                let result = await this.api('GET', '/api/v1/vessels?pageSize=1000');
                this.allVessels = (result && result.objects) ? result.objects : [];
                if (!Array.isArray(this.allVessels)) this.allVessels = [];
                for (let vessel of this.allVessels) {
                    vessel._fleetName = this.fleetName(vessel.fleetId);
                }
            } catch (e) { console.warn('Failed to load vessels:', e); }
        },

        async loadVoyageMissions(voyageId) {
            try {
                let result = await this.api('GET', '/api/v1/missions?voyageId=' + voyageId + '&pageSize=1000');
                this.voyageMissions = (result && result.objects) ? result.objects : [];
                if (!Array.isArray(this.voyageMissions)) this.voyageMissions = [];
            } catch (e) { console.warn('Failed to load voyage missions:', e); this.voyageMissions = []; }
        },

        async loadMissions() {
            try {
                let params = [];
                if (this.missionFilters.status) params.push('status=' + this.missionFilters.status);
                if (this.missionFilters.vesselId) params.push('vesselId=' + this.missionFilters.vesselId);
                if (this.missionFilters.captainId) params.push('captainId=' + this.missionFilters.captainId);
                if (this.missionFilters.voyageId) params.push('voyageId=' + this.missionFilters.voyageId);
                params.push('pageNumber=' + this.missionPaging.pageNumber);
                params.push('pageSize=' + this.missionPaging.pageSize);
                let url = '/api/v1/missions' + (params.length ? '?' + params.join('&') : '');
                let result = await this.api('GET', url);
                this.allMissions = (result && result.objects) ? result.objects : [];
                if (!Array.isArray(this.allMissions)) this.allMissions = [];
                if (result) {
                    this.missionPaging.pageNumber = result.pageNumber || 1;
                    this.missionPaging.totalPages = result.totalPages || 0;
                    this.missionPaging.totalRecords = result.totalRecords || 0;
                    this.missionPaging.totalMs = result.totalMs || 0;
                }
            } catch (e) { console.warn('Failed to load missions:', e); }
        },

        async loadSignals() {
            try {
                let params = [];
                params.push('pageNumber=' + this.signalPaging.pageNumber);
                params.push('pageSize=' + this.signalPaging.pageSize);
                let url = '/api/v1/signals?' + params.join('&');
                let result = await this.api('GET', url);
                this.signals = (result && result.objects) ? result.objects : [];
                if (!Array.isArray(this.signals)) this.signals = [];
                if (result) {
                    this.signalPaging.pageNumber = result.pageNumber || 1;
                    this.signalPaging.totalPages = result.totalPages || 0;
                    this.signalPaging.totalRecords = result.totalRecords || 0;
                    this.signalPaging.totalMs = result.totalMs || 0;
                }
            } catch (e) { console.warn('Failed to load signals:', e); }
        },

        async loadEvents() {
            this.selectedEvents = [];
            try {
                let params = [];
                if (this.eventFilters.type) params.push('type=' + this.eventFilters.type);
                if (this.eventFilters.captainId) params.push('captainId=' + this.eventFilters.captainId);
                if (this.eventFilters.missionId) params.push('missionId=' + this.eventFilters.missionId);
                if (this.eventFilters.vesselId) params.push('vesselId=' + this.eventFilters.vesselId);
                if (this.eventFilters.voyageId) params.push('voyageId=' + this.eventFilters.voyageId);
                params.push('pageNumber=' + this.eventPaging.pageNumber);
                params.push('pageSize=' + (this.eventFilters.limit || this.eventPaging.pageSize));
                let url = '/api/v1/events' + (params.length ? '?' + params.join('&') : '');
                let result = await this.api('GET', url);
                this.events = (result && result.objects) ? result.objects : [];
                if (!Array.isArray(this.events)) this.events = [];
                if (result) {
                    this.eventPaging.pageNumber = result.pageNumber || 1;
                    this.eventPaging.totalPages = result.totalPages || 0;
                    this.eventPaging.totalRecords = result.totalRecords || 0;
                    this.eventPaging.totalMs = result.totalMs || 0;
                }
            } catch (e) { console.warn('Failed to load events:', e); }
        },

        async loadMergeQueue() {
            try {
                let params = [];
                params.push('pageNumber=' + this.mergeQueuePaging.pageNumber);
                params.push('pageSize=' + this.mergeQueuePaging.pageSize);
                let url = '/api/v1/merge-queue?' + params.join('&');
                let result = await this.api('GET', url);
                this.mergeQueue = (result && result.objects) ? result.objects : [];
                if (!Array.isArray(this.mergeQueue)) this.mergeQueue = [];
                if (result) {
                    this.mergeQueuePaging.pageNumber = result.pageNumber || 1;
                    this.mergeQueuePaging.totalPages = result.totalPages || 0;
                    this.mergeQueuePaging.totalRecords = result.totalRecords || 0;
                    this.mergeQueuePaging.totalMs = result.totalMs || 0;
                }

                // Enrich entries with mission titles and voyage names
                let missionIds = [...new Set(this.mergeQueue.filter(e => e.missionId).map(e => e.missionId))];
                let missionMap = {};
                await Promise.all(missionIds.map(async (mid) => {
                    try {
                        let mission = this.toCamel(await this.api('GET', '/api/v1/missions/' + mid));
                        missionMap[mid] = mission;
                    } catch (e) { console.warn('Failed to load mission ' + mid, e); }
                }));

                let voyageIds = [...new Set(Object.values(missionMap).filter(m => m && m.voyageId).map(m => m.voyageId))];
                let voyageMap = {};
                await Promise.all(voyageIds.map(async (vid) => {
                    try {
                        let voyage = this.toCamel(await this.api('GET', '/api/v1/voyages/' + vid));
                        voyageMap[vid] = voyage;
                    } catch (e) { console.warn('Failed to load voyage ' + vid, e); }
                }));

                for (let entry of this.mergeQueue) {
                    if (entry.missionId && missionMap[entry.missionId]) {
                        let mission = missionMap[entry.missionId];
                        entry._missionTitle = mission.title || null;
                        entry._voyageId = mission.voyageId || null;
                        let voyage = mission.voyageId ? voyageMap[mission.voyageId] : null;
                        entry._voyageName = voyage ? (voyage.title || null) : null;
                    } else {
                        entry._missionTitle = null;
                        entry._voyageId = null;
                        entry._voyageName = null;
                    }
                }

                // Build unique voyage list for filter dropdown
                let voyageSet = {};
                for (let entry of this.mergeQueue) {
                    if (entry._voyageId && entry._voyageName) {
                        voyageSet[entry._voyageId] = entry._voyageName;
                    }
                }
                this.mergeQueueVoyages = Object.keys(voyageSet).map(id => ({ id: id, name: voyageSet[id] }));
            } catch (e) { console.warn('Failed to load merge queue:', e); }
        },

        async loadDocks() {
            try {
                let params = [];
                params.push('pageNumber=' + this.dockPaging.pageNumber);
                params.push('pageSize=' + this.dockPaging.pageSize);
                let url = '/api/v1/docks?' + params.join('&');
                let result = await this.api('GET', url);
                let allDocks = (result && result.objects) ? result.objects : [];
                if (!Array.isArray(allDocks)) allDocks = [];
                // Apply client-side status filter
                if (this.dockFilters.status === 'active') {
                    allDocks = allDocks.filter(d => d.active === true);
                } else if (this.dockFilters.status === 'inactive') {
                    allDocks = allDocks.filter(d => d.active === false);
                }
                this.docks = allDocks;
                if (result) {
                    this.dockPaging.pageNumber = result.pageNumber || 1;
                    this.dockPaging.totalPages = result.totalPages || 0;
                    this.dockPaging.totalRecords = result.totalRecords || 0;
                    this.dockPaging.totalMs = result.totalMs || 0;
                }
            } catch (e) { console.warn('Failed to load docks:', e); }
        },

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

        async deleteSelectedDocks() {
            if (this.selectedDocks.length === 0) return;
            if (!await this.showConfirm('Delete ' + this.selectedDocks.length + ' selected dock(s)? This will clean up git worktrees and cannot be undone.')) return;
            let count = this.selectedDocks.length;
            let ids = [...this.selectedDocks];
            this.selectedDocks = [];
            this.toast('Cleanup of ' + count + ' dock(s) started in the background');
            let failed = 0;
            for (let id of ids) {
                try {
                    await this.api('DELETE', '/api/v1/docks/' + id);
                } catch (e) {
                    failed++;
                    console.warn('Failed to delete dock ' + id + ':', e);
                }
            }
            if (failed > 0) {
                this.toast('Deleted ' + (ids.length - failed) + ' docks, ' + failed + ' failed', 'warning');
            } else {
                this.toast('All ' + count + ' dock(s) cleaned up successfully');
            }
            await this.loadDocks();
        },

        toggleDockSelection(id) {
            let idx = this.selectedDocks.indexOf(id);
            if (idx >= 0) {
                this.selectedDocks.splice(idx, 1);
            } else {
                this.selectedDocks.push(id);
            }
        },

        selectAllDocks() {
            this.selectedDocks = this.docks.map(d => d.id);
        },

        clearDockSelection() {
            this.selectedDocks = [];
        },

        // Mission multi-select
        toggleMissionSelection(id) {
            let idx = this.selectedMissions.indexOf(id);
            if (idx >= 0) { this.selectedMissions.splice(idx, 1); } else { this.selectedMissions.push(id); }
        },
        selectAllMissions() { this.selectedMissions = this.allMissions.map(m => m.id); },
        clearMissionSelection() { this.selectedMissions = []; },
        async deleteSelectedMissions() {
            if (this.selectedMissions.length === 0) return;
            if (!await this.showConfirm('Delete ' + this.selectedMissions.length + ' selected mission(s)? This cannot be undone.')) return;
            let ids = [...this.selectedMissions];
            let failed = 0;
            for (let id of ids) {
                try { await this.api('DELETE', '/api/v1/missions/' + id + '/purge'); }
                catch (e) { failed++; console.warn('Failed to delete mission ' + id + ':', e); }
            }
            this.selectedMissions = [];
            if (failed > 0) { this.toast('Deleted ' + (ids.length - failed) + ' missions, ' + failed + ' failed', 'warning'); }
            else { this.toast('Deleted ' + ids.length + ' mission(s)'); }
            await this.loadMissions();
        },

        // Voyage multi-select
        toggleVoyageSelection(id) {
            let idx = this.selectedVoyages.indexOf(id);
            if (idx >= 0) { this.selectedVoyages.splice(idx, 1); } else { this.selectedVoyages.push(id); }
        },
        selectAllVoyages() { this.selectedVoyages = this.voyages.map(v => v.id); },
        clearVoyageSelection() { this.selectedVoyages = []; },
        async deleteSelectedVoyages() {
            if (this.selectedVoyages.length === 0) return;
            if (!await this.showConfirm('Delete ' + this.selectedVoyages.length + ' selected voyage(s) and all their missions? This cannot be undone.')) return;
            let ids = [...this.selectedVoyages];
            let failed = 0;
            for (let id of ids) {
                try { await this.api('DELETE', '/api/v1/voyages/' + id + '/purge'); }
                catch (e) { failed++; console.warn('Failed to delete voyage ' + id + ':', e); }
            }
            this.selectedVoyages = [];
            if (failed > 0) { this.toast('Deleted ' + (ids.length - failed) + ' voyages, ' + failed + ' failed', 'warning'); }
            else { this.toast('Deleted ' + ids.length + ' voyage(s)'); }
            await this.loadVoyages();
        },

        // Captain multi-select
        toggleCaptainSelection(id) {
            let idx = this.selectedCaptains.indexOf(id);
            if (idx >= 0) { this.selectedCaptains.splice(idx, 1); } else { this.selectedCaptains.push(id); }
        },
        selectAllCaptains() { this.selectedCaptains = this.captains.map(c => c.id); },
        clearCaptainSelection() { this.selectedCaptains = []; },
        async deleteSelectedCaptains() {
            if (this.selectedCaptains.length === 0) return;
            if (!await this.showConfirm('Remove ' + this.selectedCaptains.length + ' selected captain(s)? This cannot be undone.')) return;
            let ids = [...this.selectedCaptains];
            let failed = 0;
            for (let id of ids) {
                try { await this.api('DELETE', '/api/v1/captains/' + id); }
                catch (e) { failed++; console.warn('Failed to remove captain ' + id + ':', e); }
            }
            this.selectedCaptains = [];
            if (failed > 0) { this.toast('Removed ' + (ids.length - failed) + ' captains, ' + failed + ' failed', 'warning'); }
            else { this.toast('Removed ' + ids.length + ' captain(s)'); }
            await this.loadCaptains();
        },

        // Signal multi-select
        toggleSignalSelection(id) {
            let idx = this.selectedSignals.indexOf(id);
            if (idx >= 0) { this.selectedSignals.splice(idx, 1); } else { this.selectedSignals.push(id); }
        },
        selectAllSignals() { this.selectedSignals = this.signals.map(s => s.id); },
        clearSignalSelection() { this.selectedSignals = []; },
        async deleteSelectedSignals() {
            if (this.selectedSignals.length === 0) return;
            if (!await this.showConfirm('Delete ' + this.selectedSignals.length + ' selected signal(s)?')) return;
            try {
                let result = await this.api('POST', '/api/v1/signals/delete/multiple', { Ids: [...this.selectedSignals] });
                this.selectedSignals = [];
                let deleted = result.deleted || 0;
                let skipped = (result.skipped || []).length;
                if (skipped > 0) { this.toast('Deleted ' + deleted + ', skipped ' + skipped, 'warning'); }
                else { this.toast('Deleted ' + deleted + ' signal(s)'); }
            } catch (e) {
                this.selectedSignals = [];
                this.toast('Failed to delete signals: ' + e.message, 'error');
            }
            await this.loadSignals();
        },

        // Merge Queue multi-select
        toggleMergeQueueSelection(id) {
            let idx = this.selectedMergeQueue.indexOf(id);
            if (idx >= 0) { this.selectedMergeQueue.splice(idx, 1); } else { this.selectedMergeQueue.push(id); }
        },
        selectAllMergeQueue() { this.selectedMergeQueue = this.mergeQueue.map(e => e.id); },
        clearMergeQueueSelection() { this.selectedMergeQueue = []; },
        async deleteSelectedMergeQueue() {
            if (this.selectedMergeQueue.length === 0) return;
            if (!await this.showConfirm('Delete ' + this.selectedMergeQueue.length + ' selected merge queue entry(ies)? This will delete branches from local and remote repositories.', { width: '880px' })) return;
            let ids = [...this.selectedMergeQueue];
            let failed = 0;
            for (let id of ids) {
                try { await this.api('DELETE', '/api/v1/merge-queue/' + id); }
                catch (e) { failed++; console.warn('Failed to delete merge entry ' + id + ':', e); }
            }
            this.selectedMergeQueue = [];
            if (failed > 0) { this.toast('Deleted ' + (ids.length - failed) + ' entries, ' + failed + ' failed', 'warning'); }
            else { this.toast('Deleted ' + ids.length + ' merge queue entry(ies)'); }
            await this.loadMergeQueue();
        },

        // Event multi-select
        toggleEventSelection(id) {
            let idx = this.selectedEvents.indexOf(id);
            if (idx >= 0) { this.selectedEvents.splice(idx, 1); } else { this.selectedEvents.push(id); }
        },
        selectAllEvents() { this.selectedEvents = this.columnFilteredEvents().map(e => e.id); },
        clearEventSelection() { this.selectedEvents = []; },
        async deleteEvent(eventId) {
            if (!await this.showConfirm('Permanently delete event ' + eventId + '? This cannot be undone.')) return;
            try {
                await this.api('DELETE', '/api/v1/events/' + eventId);
                this.toast('Event deleted');
                if (this.detailView === 'event-detail' && this.detailId === eventId) this.goBack();
                await this.loadEvents();
            } catch (e) { this.toast('Failed: ' + e.message, 'error'); }
        },
        async deleteSelectedEvents() {
            if (this.selectedEvents.length === 0) return;
            if (!await this.showConfirm('Delete ' + this.selectedEvents.length + ' selected event(s)? This cannot be undone.')) return;
            try {
                let result = await this.api('POST', '/api/v1/events/delete/multiple', { Ids: [...this.selectedEvents] });
                this.selectedEvents = [];
                let deleted = result.deleted || 0;
                let skipped = (result.skipped || []).length;
                if (skipped > 0) { this.toast('Deleted ' + deleted + ', skipped ' + skipped, 'warning'); }
                else { this.toast('Deleted ' + deleted + ' event(s)'); }
            } catch (e) {
                this.selectedEvents = [];
                this.toast('Failed to delete events: ' + e.message, 'error');
            }
            await this.loadEvents();
        },

        // Fleet multi-select
        toggleFleetSelection(id) {
            let idx = this.selectedFleets.indexOf(id);
            if (idx >= 0) { this.selectedFleets.splice(idx, 1); } else { this.selectedFleets.push(id); }
        },
        selectAllFleets() { this.selectedFleets = this.fleets.map(f => f.id); },
        clearFleetSelection() { this.selectedFleets = []; },
        async deleteSelectedFleets() {
            if (this.selectedFleets.length === 0) return;
            if (!await this.showConfirm('Delete ' + this.selectedFleets.length + ' selected fleet(s)? This cannot be undone.')) return;
            try {
                let result = await this.api('POST', '/api/v1/fleets/delete/multiple', { Ids: [...this.selectedFleets] });
                this.selectedFleets = [];
                let deleted = result.deleted || 0;
                let skipped = (result.skipped || []).length;
                if (skipped > 0) { this.toast('Deleted ' + deleted + ', skipped ' + skipped, 'warning'); }
                else { this.toast('Deleted ' + deleted + ' fleet(s)'); }
            } catch (e) {
                this.selectedFleets = [];
                this.toast('Failed to delete fleets: ' + e.message, 'error');
            }
            await this.loadFleets();
        },

        // Vessel multi-select
        toggleVesselSelection(id) {
            let idx = this.selectedVessels.indexOf(id);
            if (idx >= 0) { this.selectedVessels.splice(idx, 1); } else { this.selectedVessels.push(id); }
        },
        selectAllVessels() { this.selectedVessels = this.allVessels.map(v => v.id); },
        clearVesselSelection() { this.selectedVessels = []; },
        async deleteSelectedVessels() {
            if (this.selectedVessels.length === 0) return;
            if (!await this.showConfirm('Delete ' + this.selectedVessels.length + ' selected vessel(s)? This cannot be undone.')) return;
            let ids = [...this.selectedVessels];
            let failed = 0;
            for (let id of ids) {
                try { await this.api('DELETE', '/api/v1/vessels/' + id); }
                catch (e) { failed++; console.warn('Failed to delete vessel ' + id + ':', e); }
            }
            this.selectedVessels = [];
            if (failed > 0) { this.toast('Deleted ' + (ids.length - failed) + ' vessels, ' + failed + ' failed', 'warning'); }
            else { this.toast('Deleted ' + ids.length + ' vessel(s)'); }
            await this.loadFleets();
            await this.loadVessels();
        },

        async loadHealth() {
            try { this.healthInfo = await this.api('GET', '/api/v1/status/health'); } catch (e) { console.warn('Failed to load health:', e); }
        },

        async runDoctorChecks() {
            this.doctorRunning = true;
            this.doctorResults = [];
            try {
                this.doctorResults = await this.api('GET', '/api/v1/doctor');
            } catch (e) {
                console.warn('Failed to run doctor checks:', e);
                this.doctorResults = [{ name: 'Error', status: 'Fail', message: 'Failed to run health checks: ' + e.message }];
            } finally {
                this.doctorRunning = false;
            }
        },

        // ============================================================
        // Navigation
        // ============================================================
        navigate(view, detailView, detailId) {
            this.view = view;
            this.detailView = detailView || null;
            this.detailId = detailId || null;
            this.detail = null;
            this.detailMissions = [];
            this.detailDiff = null;
            this.detailLog = null;
            this.listSearch = '';
            this.clearColumnFilters();
            this.showDispatchForm = false;
            this.dispatchResult = null;
            this.quickDispatchResult = null;
            this.quickParsedTasks = [];
            this.updateBreadcrumbs();

            // Reset pagination to page 1 when navigating to a view
            if (view === 'missions') this.missionPaging.pageNumber = 1;
            if (view === 'voyages') this.voyagePaging.pageNumber = 1;
            if (view === 'captains') this.captainPaging.pageNumber = 1;
            if (view === 'signals') this.signalPaging.pageNumber = 1;
            if (view === 'events') this.eventPaging.pageNumber = 1;
            if (view === 'merge-queue') this.mergeQueuePaging.pageNumber = 1;
            if (view === 'docks') this.dockPaging.pageNumber = 1;

            // Load data for new views
            if (view === 'signals') this.loadSignals();
            if (view === 'events') this.loadEvents();
            if (view === 'merge-queue') this.loadMergeQueue();
            if (view === 'missions') this.selectedMissions = [];
            if (view === 'voyages') this.selectedVoyages = [];
            if (view === 'captains') this.selectedCaptains = [];
            if (view === 'signals') this.selectedSignals = [];
            if (view === 'events') this.selectedEvents = [];
            if (view === 'merge-queue') this.selectedMergeQueue = [];
            if (view === 'fleets-list') { this.loadFleets(); }
            if (view === 'fleets') { this.selectedVessels = []; this.sortColumn = '_fleetName'; this.sortAsc = true; this.loadFleets().then(() => { this.loadVessels(); }); }
            if (view === 'docks') { this.selectedDocks = []; this.loadDocks(); }
            if (view === 'server') { this.loadHealth(); this.loadSettings(); }
            if (view === 'missions') this.loadMissions();
            if (view === 'voyages') this.loadVoyageMissionMap();
            if (view === 'doctor') this.runDoctorChecks();

            if (detailView) this.loadDetail(detailView, detailId);
        },

        async loadDetail(detailView, id) {
            try {
                if (detailView === 'mission-detail') {
                    this.detail = await this.api('GET', '/api/v1/missions/' + id);
                } else if (detailView === 'voyage-detail') {
                    let result = await this.api('GET', '/api/v1/voyages/' + id);
                    this.detail = result.voyage || result;
                    this.detailMissions = result.missions || [];
                } else if (detailView === 'captain-detail') {
                    this.detail = await this.api('GET', '/api/v1/captains/' + id);
                    try {
                        let missionResult = await this.api('GET', '/api/v1/missions?captainId=' + id + '&pageSize=10&order=CreatedDescending');
                        this.detailMissions = (missionResult && missionResult.objects) ? missionResult.objects : [];
                        if (!Array.isArray(this.detailMissions)) this.detailMissions = [];
                    } catch (_) { }
                } else if (detailView === 'vessel-detail') {
                    this.detail = await this.api('GET', '/api/v1/vessels/' + id);
                    try {
                        let missionResult = await this.api('GET', '/api/v1/missions?vesselId=' + id + '&pageSize=1000');
                        this.detailMissions = (missionResult && missionResult.objects) ? missionResult.objects : [];
                        if (!Array.isArray(this.detailMissions)) this.detailMissions = [];
                    } catch (_) { }
                } else if (detailView === 'fleet-detail') {
                    this.detail = await this.api('GET', '/api/v1/fleets/' + id);
                    this.detail.vessels = [];
                    try {
                        let vesselResult = await this.api('GET', '/api/v1/vessels?fleetId=' + id + '&pageSize=1000');
                        this.detail.vessels = (vesselResult && vesselResult.objects) ? vesselResult.objects : [];
                        if (!Array.isArray(this.detail.vessels)) this.detail.vessels = [];
                    } catch (_) { }
                } else if (detailView === 'dock-detail') {
                    this.detail = await this.api('GET', '/api/v1/docks/' + id);
                } else if (detailView === 'merge-detail') {
                    this.detail = await this.api('GET', '/api/v1/merge-queue/' + id);
                } else if (detailView === 'signal-detail') {
                    this.detail = this.signals.find(s => s.id === id) || null;
                    if (!this.detail) {
                        try {
                            let sigResult = await this.api('GET', '/api/v1/signals?pageSize=1000');
                            let allSigs = (sigResult && sigResult.objects) ? sigResult.objects : [];
                            this.detail = allSigs.find(s => s.id === id) || null;
                        } catch (_) { }
                    }
                } else if (detailView === 'event-detail') {
                    this.detail = this.events.find(e => e.id === id) || null;
                    if (!this.detail) {
                        try {
                            let evtResult = await this.api('GET', '/api/v1/events?limit=1000');
                            let allEvts = (evtResult && evtResult.objects) ? evtResult.objects : [];
                            this.detail = allEvts.find(e => e.id === id) || null;
                        } catch (_) { }
                    }
                }
            } catch (e) {
                this.toast('Failed to load detail: ' + e.message, 'error');
            }
        },

        formatDiffHtml(text) {
            if (!text) return '';
            let escaped = text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
            return escaped.split('\n').map(line => {
                if (line.startsWith('@@')) return '<span class="diff-line-section">' + line + '</span>';
                if (line.startsWith('+')) return '<span class="diff-line-add">' + line + '</span>';
                if (line.startsWith('-')) return '<span class="diff-line-remove">' + line + '</span>';
                return line;
            }).join('\n');
        },

        parseDiffFiles(rawDiff) {
            if (!rawDiff || rawDiff === 'No changes') return [];
            let files = [];
            let lines = rawDiff.split('\n');
            let currentFile = null;
            for (let i = 0; i < lines.length; i++) {
                let line = lines[i];
                if (line.startsWith('diff --git ')) {
                    if (currentFile) files.push(currentFile);
                    let match = line.match(/diff --git a\/(.*?) b\/(.*)/);
                    let name = match ? match[2] : line.substring(11);
                    currentFile = { name: name, additions: 0, deletions: 0, startLine: i };
                } else if (currentFile) {
                    if (line.startsWith('+') && !line.startsWith('+++')) currentFile.additions++;
                    else if (line.startsWith('-') && !line.startsWith('---')) currentFile.deletions++;
                }
            }
            if (currentFile) files.push(currentFile);
            return files;
        },

        renderFileDiff(rawDiff, fileName) {
            if (!rawDiff) return '';
            let lines = rawDiff.split('\n');
            let inFile = false;
            let fileLines = [];
            for (let i = 0; i < lines.length; i++) {
                let line = lines[i];
                if (line.startsWith('diff --git ')) {
                    if (inFile) break;
                    let match = line.match(/diff --git a\/(.*?) b\/(.*)/);
                    let name = match ? match[2] : '';
                    if (name === fileName) inFile = true;
                }
                if (inFile) fileLines.push(line);
            }
            return this.renderDiffLines(fileLines);
        },

        renderAllDiffs(rawDiff) {
            if (!rawDiff) return '';
            return this.renderDiffLines(rawDiff.split('\n'));
        },

        renderDiffLines(lines) {
            let html = '';
            let oldNum = 0, newNum = 0;
            for (let i = 0; i < lines.length; i++) {
                let line = lines[i];
                let escaped = line.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
                if (line.startsWith('diff --git ')) {
                    html += '<div class="diff-file-header">' + escaped + '</div>';
                } else if (line.startsWith('@@')) {
                    let hunkMatch = line.match(/@@ -(\d+)/);
                    if (hunkMatch) { oldNum = parseInt(hunkMatch[1]); }
                    let newMatch = line.match(/@@ -\d+(?:,\d+)? \+(\d+)/);
                    if (newMatch) { newNum = parseInt(newMatch[1]); }
                    html += '<div class="diff-hunk-header">' + escaped + '</div>';
                } else if (line.startsWith('---') || line.startsWith('+++') || line.startsWith('index ') || line.startsWith('new file') || line.startsWith('deleted file') || line.startsWith('old mode') || line.startsWith('new mode') || line.startsWith('similarity index') || line.startsWith('rename from') || line.startsWith('rename to') || line.startsWith('Binary files')) {
                    html += '<div class="diff-meta-line">' + escaped + '</div>';
                } else if (line.startsWith('+')) {
                    html += '<div class="diff-line diff-line-add"><span class="diff-line-num diff-line-num-old"></span><span class="diff-line-num diff-line-num-new">' + newNum + '</span><span class="diff-line-content">' + escaped + '</span></div>';
                    newNum++;
                } else if (line.startsWith('-')) {
                    html += '<div class="diff-line diff-line-del"><span class="diff-line-num diff-line-num-old">' + oldNum + '</span><span class="diff-line-num diff-line-num-new"></span><span class="diff-line-content">' + escaped + '</span></div>';
                    oldNum++;
                } else {
                    html += '<div class="diff-line diff-line-ctx"><span class="diff-line-num diff-line-num-old">' + (oldNum || '') + '</span><span class="diff-line-num diff-line-num-new">' + (newNum || '') + '</span><span class="diff-line-content">' + escaped + '</span></div>';
                    if (oldNum) oldNum++;
                    if (newNum) newNum++;
                }
            }
            return html;
        },

        openDiffViewer(title, rawDiff) {
            this.diffViewerTitle = title;
            this.diffViewerRawDiff = rawDiff;
            this.diffViewerSelectedFile = null;
            this.diffViewerCopied = false;
            this.diffViewerOpen = true;
            let isEmpty = !rawDiff || !rawDiff.trim() || rawDiff === 'No changes' || rawDiff === 'No modified files';
            this.diffViewerFiles = isEmpty ? [] : this.parseDiffFiles(rawDiff);
            this.$nextTick(() => {
                let el = document.getElementById('diff-content-area');
                if (el) {
                    if (isEmpty) {
                        el.innerHTML = '<div class="diff-empty-state"><span class="text-dim">No modified files</span></div>';
                    } else {
                        el.innerHTML = this.renderAllDiffs(rawDiff);
                    }
                }
            });
        },

        closeDiffViewer() {
            this.diffViewerOpen = false;
            this.diffViewerRawDiff = '';
            this.diffViewerFiles = [];
            this.diffViewerSelectedFile = null;
        },

        selectDiffFile(fileName) {
            if (this.diffViewerSelectedFile === fileName) {
                this.diffViewerSelectedFile = null;
                this.$nextTick(() => {
                    let el = document.getElementById('diff-content-area');
                    if (el) el.innerHTML = this.renderAllDiffs(this.diffViewerRawDiff);
                });
            } else {
                this.diffViewerSelectedFile = fileName;
                this.$nextTick(() => {
                    let el = document.getElementById('diff-content-area');
                    if (el) el.innerHTML = this.renderFileDiff(this.diffViewerRawDiff, fileName);
                });
            }
        },

        copyDiffRaw() {
            let text = this.diffViewerRawDiff;
            if (!text) return;
            let onSuccess = () => {
                this.diffViewerCopied = true;
                setTimeout(() => { this.diffViewerCopied = false; }, 2000);
            };
            if (navigator.clipboard && window.isSecureContext) {
                navigator.clipboard.writeText(text).then(onSuccess).catch(() => {
                    this.fallbackCopy(text);
                    onSuccess();
                });
            } else {
                this.fallbackCopy(text);
                onSuccess();
            }
        },

        async loadMissionDiff(missionId) {
            let title = 'Diff: ' + (this.detail ? this.detail.title : missionId);
            this.diffViewerTitle = title;
            this.diffViewerLoading = true;
            this.diffViewerOpen = true;
            this.diffViewerFiles = [];
            this.diffViewerSelectedFile = null;
            this.diffViewerRawDiff = '';
            this.detailDiffLoading = true;
            try {
                let controller = new AbortController();
                let timeoutId = setTimeout(() => controller.abort(), 30000);
                let opts = {
                    method: 'GET',
                    headers: { 'Content-Type': 'application/json' },
                    signal: controller.signal
                };
                if (this.apiKey) opts.headers['X-Api-Key'] = this.apiKey;
                let resp = await fetch(API + '/api/v1/missions/' + missionId + '/diff', opts);
                clearTimeout(timeoutId);
                if (!resp.ok) {
                    let errText = await resp.text();
                    let errMsg = 'HTTP ' + resp.status;
                    try { let e = JSON.parse(errText); errMsg = e.Message || e.message || e.Error || e.error || errMsg; } catch (_) { }
                    throw new Error(errMsg);
                }
                let text = await resp.text();
                let result = text ? this.toCamel(JSON.parse(text)) : null;
                let rawDiff = (result && result.diff) ? result.diff : '';
                this.openDiffViewer(title, rawDiff);
            } catch (e) {
                let errMsg = e.name === 'AbortError' ? 'Request timed out after 30 seconds' : e.message;
                let isNotFound = errMsg.toLowerCase().includes('not found') || errMsg.includes('404') || errMsg.toLowerCase().includes('no diff available');
                if (isNotFound) {
                    this.openDiffViewer(title, '');
                } else {
                    this.diffViewerRawDiff = 'Error: ' + errMsg;
                    this.diffViewerFiles = [];
                    this.$nextTick(() => {
                        let el = document.getElementById('diff-content-area');
                        if (el) el.textContent = 'Error: ' + errMsg;
                    });
                }
            } finally {
                this.detailDiffLoading = false;
                this.diffViewerLoading = false;
            }
        },

        async loadMissionLog(missionId) {
            let title = this.detail ? this.detail.title : missionId;
            this.openViewer('Log: ' + title, 'Loading…');
            try {
                let result = await this.api('GET', '/api/v1/missions/' + missionId + '/log?lines=500');
                let logText = result.log || 'No log output';
                let lineInfo = '(' + (result.lines || 0) + ' of ' + (result.totalLines || 0) + ' lines)';
                this.viewerTitle = 'Log: ' + title + ' ' + lineInfo;
                this.viewerContent = logText;
                this.viewerRawText = logText;
            } catch (e) {
                this.viewerContent = 'Log unavailable: ' + e.message;
                this.viewerRawText = '';
            }
        },

        async loadCaptainLog(captainId) {
            let captainName = this.detail ? this.detail.name : captainId;
            this.viewerTitle = 'Log: ' + captainName;
            this.viewerContent = '';
            this.viewerRawContent = '';
            this.viewerIsHtml = false;
            this.viewerLoading = true;
            this.modal = 'viewer';
            try {
                let result = await this.api('GET', '/api/v1/captains/' + captainId + '/log?lines=500');
                this.viewerContent = result.log || 'No log output';
                this.viewerRawContent = this.viewerContent;
            } catch (e) {
                this.viewerContent = 'Log unavailable: ' + e.message;
                this.viewerRawContent = this.viewerContent;
            } finally {
                this.viewerLoading = false;
            }
        },

        copyViewerContent() {
            let text = this.viewerRawContent || this.viewerContent;
            if (!text) return;
            if (navigator.clipboard && window.isSecureContext) {
                navigator.clipboard.writeText(text).catch(() => this.fallbackCopy(text));
            } else {
                this.fallbackCopy(text);
            }
        },

        // ============================================================
        // Copy to clipboard (works on both HTTP and HTTPS)
        // ============================================================
        copyId(text, event) {
            if (!text) return;
            let btn = event?.currentTarget;
            let onSuccess = () => {
                if (!btn) return;
                let orig = btn.textContent;
                btn.textContent = '\u2713';
                btn.classList.add('copied');
                setTimeout(() => { btn.textContent = orig; btn.classList.remove('copied'); }, 1500);
            };
            if (navigator.clipboard && window.isSecureContext) {
                navigator.clipboard.writeText(text).then(onSuccess).catch(() => {
                    if (this.fallbackCopy(text)) onSuccess();
                });
            } else {
                if (this.fallbackCopy(text)) onSuccess();
            }
        },

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

        updateBreadcrumbs() {
            this.breadcrumbs = [];
            let viewLabels = {
                'home': 'Dashboard', 'fleets-list': 'Fleets', 'fleets': 'Vessels', 'voyages': 'Voyages',
                'captains': 'Captains', 'missions': 'Missions', 'dispatch': 'Dispatch',
                'signals': 'Signals', 'events': 'Events', 'merge-queue': 'Merge Queue',
                'docks': 'Docks', 'server': 'Server', 'config': 'Config'
            };
            this.breadcrumbs.push({ label: viewLabels[this.view] || this.view, view: this.view });
            if (this.detailView) {
                let label = this.detailId;
                if (this.detail) {
                    label = this.detail.title || this.detail.name || this.detail.id || this.detailId;
                }
                this.breadcrumbs.push({ label: label, id: this.detailId });
            }
        },

        goBack() {
            this.detailView = null;
            this.detailId = null;
            this.detail = null;
            this.detailMissions = [];
            this.detailDiff = null;
            this.detailLog = null;
            this.updateBreadcrumbs();
        },

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

        async loadSettings() {
            try { this.serverSettings = await this.api('GET', '/api/v1/settings'); } catch (e) { console.warn('Failed to load settings:', e); }
        },

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

        async copyMcpConfig(type) {
            let text = type === 'http' ? this.getMcpConfigHttp() : this.getMcpConfigStdio();
            try {
                await navigator.clipboard.writeText(text);
                this.toast('Copied to clipboard');
            } catch (e) { this.toast('Failed to copy', 'error'); }
        },

        // ============================================================
        // WebSocket
        // ============================================================
        connectWebSocket() {
            try {
                let wsUrl = WS_PROTOCOL + '//' + window.location.hostname + ':' + this.wsPort;
                console.log('Connecting WebSocket to', wsUrl);
                this.ws = new WebSocket(wsUrl);
                this.ws.onopen = () => {
                    this.wsConnected = true;
                    this.connected = true;
                    console.log('WebSocket connected');
                    this.ws.send(JSON.stringify({ Route: 'subscribe' }));
                };
                this.ws.onmessage = (evt) => {
                    try { this.handleWsMessage(this.toCamel(JSON.parse(evt.data))); } catch (e) { }
                };
                this.ws.onclose = () => {
                    this.wsConnected = false;
                    this.connected = this.apiConnected;
                    setTimeout(() => this.connectWebSocket(), 3000);
                };
                this.ws.onerror = (e) => {
                    console.warn('WebSocket error:', e);
                    this.wsConnected = false;
                    this.connected = this.apiConnected;
                };
            } catch (e) {
                console.warn('WebSocket connection failed:', e);
                this.wsConnected = false;
                this.connected = this.apiConnected;
            }
        },

        handleWsMessage(data) {
            if (data.type === 'status.snapshot') {
                this.status = data.data || this.status;
                return;
            }
            if (data.type && (
                data.type.includes('changed') || data.type.includes('mission') ||
                data.type.includes('captain') || data.type.includes('voyage') ||
                data.type.includes('signal') || data.type.includes('merge') ||
                data.type.includes('event')
            )) {
                this.refresh();
                // Refresh current view data
                if (this.view === 'signals') this.loadSignals();
                if (this.view === 'events') this.loadEvents();
                if (this.view === 'merge-queue') this.loadMergeQueue();
                if (this.view === 'missions') this.loadMissions();
            }

            // Toast notifications for mission state changes
            if (data.type === 'mission.changed' && data.data) {
                let m = data.data;
                if (m.status) {
                    this._notifyStateChange('Mission', m.id, m.title, m.status);
                }
            }

            // Toast notifications for voyage state changes
            if (data.type === 'voyage.changed' && data.data) {
                let v = data.data;
                if (v.status) {
                    this._notifyStateChange('Voyage', v.id, v.title, v.status);
                }
            }

            // Captain state changes
            if (data.type === 'captain.changed' && data.data) {
                let c = data.data;
                if (c.id && c.state) {
                    this._notifyStateChange('Captain', c.id, c.name || c.id, c.state);
                }
            }
        },

        // ============================================================
        // Copy to clipboard
        // ============================================================
        copyId(text, event) {
            if (!text) return;
            let btn = event.currentTarget;
            let copyPromise;
            if (navigator.clipboard && navigator.clipboard.writeText && window.isSecureContext) {
                copyPromise = navigator.clipboard.writeText(text);
            } else {
                copyPromise = new Promise((resolve, reject) => {
                    let textarea = document.createElement('textarea');
                    textarea.value = text;
                    textarea.style.position = 'fixed';
                    textarea.style.opacity = '0';
                    textarea.style.left = '-9999px';
                    document.body.appendChild(textarea);
                    textarea.focus();
                    textarea.select();
                    try {
                        document.execCommand('copy') ? resolve() : reject();
                    } catch (err) {
                        reject(err);
                    } finally {
                        document.body.removeChild(textarea);
                    }
                });
            }
            copyPromise.then(() => {
                btn.classList.add('copied');
                setTimeout(() => { btn.classList.remove('copied'); }, 1500);
            }).catch(() => {
                btn.classList.add('copy-failed');
                setTimeout(() => { btn.classList.remove('copy-failed'); }, 1500);
            });
        },

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

        formatTime(utcStr) {
            if (!utcStr) return '';
            let d = new Date(utcStr);
            let now = new Date();
            let diffMs = now - d;
            let diffMin = Math.floor(diffMs / 60000);
            if (diffMin < 1) return 'just now';
            if (diffMin < 60) return diffMin + 'm ago';
            let diffHr = Math.floor(diffMin / 60);
            if (diffHr < 24) return diffHr + 'h ago';
            return d.toLocaleDateString();
        },

        formatTimeAbsolute(utcStr) {
            if (!utcStr) return '';
            return new Date(utcStr).toLocaleString();
        },

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

        /// <summary>
        /// Returns a tooltip description for a given status value.
        /// </summary>
        statusTooltip(status) {
            if (!status && status !== false) return '';
            let tooltips = {
                // Mission statuses
                'pending': 'Mission created, waiting to be assigned to a captain',
                'assigned': 'Mission has been assigned to a captain but work has not started yet',
                'inprogress': 'A captain is actively working on this mission',
                'workproduced': 'The captain finished and code exists on a branch, awaiting landing to main',
                'testing': 'Mission work is being tested or validated',
                'review': 'Mission work is ready for code review',
                'complete': 'Mission completed successfully and work has been integrated',
                'failed': 'Mission encountered an error and did not complete successfully',
                'landingfailed': 'Code was produced but landing failed (merge conflict, push error, etc.)',
                'cancelled': 'Mission was cancelled before completion',
                // Voyage statuses
                'open': 'Voyage is open and missions are being dispatched',
                // Captain states
                'idle': 'Captain is available and waiting for a mission assignment',
                'working': 'Captain is actively executing a mission',
                'stalled': 'Captain process appears unresponsive -- may need intervention',
                'stopping': 'Captain is in the process of being stopped',
                // Merge queue statuses
                'queued': 'Entry is queued and waiting to be processed',
                'passed': 'Tests passed -- ready to land',
                'landed': 'Branch has been successfully merged into the target branch',
                // Signal types
                'assignment': 'Captain was assigned a new mission',
                'progress': 'Progress update from a captain or the system',
                'completion': 'A captain has finished its work',
                'error': 'An error occurred during mission execution',
                'heartbeat': 'Periodic heartbeat indicating the captain is alive',
                'nudge': 'A nudge signal sent to prompt action',
                'mail': 'A message sent between the admiral and a captain',
                // Dock statuses
                'active': 'Dock worktree is active and may be in use by a captain',
                'inactive': 'Dock worktree is no longer active',
                'true': 'Dock worktree is active and may be in use by a captain',
                'false': 'Dock worktree is no longer active',
                // Doctor health check results
                'pass': 'Health check passed -- no issues detected',
                'warn': 'Health check has warnings -- review recommended',
                'fail': 'Health check failed -- action required',
            };
            let key = String(status).toLowerCase();
            return tooltips[key] || '';
        },

        /// <summary>
        /// Returns navigation info for an entity ID based on its prefix, or null if no detail view exists.
        /// </summary>
        entityNav(entityId) {
            if (!entityId) return null;
            let prefix = entityId.substring(0, 4);
            let map = {
                'flt_': { view: 'fleets', detail: 'fleet-detail' },
                'vsl_': { view: 'vessels', detail: 'vessel-detail' },
                'cpt_': { view: 'captains', detail: 'captain-detail' },
                'msn_': { view: 'missions', detail: 'mission-detail' },
                'vyg_': { view: 'voyages', detail: 'voyage-detail' },
                'sig_': { view: 'signals', detail: 'signal-detail' }
            };
            return map[prefix] || null;
        },

        // Case-insensitive substring match for column filters
        filterMatch(value, filter) {
            if (!filter) return true;
            return (String(value || '')).toLowerCase().includes(filter.toLowerCase());
        },

        // Column-filtered vessels
        columnFilteredVessels() {
            let rows = this.filteredVesselsList();
            let f = this.vesselColFilters;
            return rows.filter(v =>
                this.filterMatch(this.fleetName(v.fleetId), f.fleet) &&
                this.filterMatch(v.name, f.name) &&
                this.filterMatch(v.repoUrl, f.repoUrl) &&
                this.filterMatch(v.defaultBranch || 'main', f.branch)
            );
        },

        // Column-filtered fleets
        columnFilteredFleets() {
            let rows = this.filterRows(this.fleets);
            let f = this.fleetColFilters;
            return rows.filter(fl =>
                this.filterMatch(fl.name, f.name) &&
                this.filterMatch(fl.description, f.description)
            );
        },

        // Column-filtered voyages
        columnFilteredVoyages() {
            let rows = this.filteredVoyages();
            let f = this.voyageColFilters;
            return rows.filter(v =>
                this.filterMatch(v.title || v.id, f.title) &&
                this.filterMatch(v.status, f.status)
            );
        },

        // Column-filtered missions
        columnFilteredMissions() {
            let rows = this.filterRows(this.allMissions);
            let f = this.missionColFilters;
            return rows.filter(m =>
                this.filterMatch(m.title, f.title) &&
                this.filterMatch(m.status, f.status) &&
                this.filterMatch(this.vesselName(m.vesselId), f.vessel) &&
                this.filterMatch(this.captainName(m.captainId), f.captain) &&
                this.filterMatch(m.branchName, f.branch)
            );
        },

        // Column-filtered captains
        columnFilteredCaptains() {
            let rows = this.filterRows(this.captains);
            let f = this.captainColFilters;
            return rows.filter(c =>
                this.filterMatch(c.name, f.name) &&
                this.filterMatch(c.state, f.state) &&
                this.filterMatch(c.runtime || 'ClaudeCode', f.runtime)
            );
        },

        // Column-filtered docks
        columnFilteredDocks() {
            let rows = this.filterRows(this.docks);
            let f = this.dockColFilters;
            return rows.filter(d =>
                this.filterMatch(this.vesselName(d.vesselId), f.vessel) &&
                this.filterMatch(this.captainName(d.captainId), f.captain) &&
                this.filterMatch(d.branchName, f.branch) &&
                this.filterMatch(d.active ? 'Active' : 'Inactive', f.status)
            );
        },

        // Column-filtered signals
        columnFilteredSignals() {
            let rows = this.filterRows(this.signals);
            let f = this.signalColFilters;
            return rows.filter(s =>
                this.filterMatch(s.type, f.type) &&
                this.filterMatch(s.fromCaptainId || 'Admiral', f.from) &&
                this.filterMatch(s.toCaptainId || 'Admiral', f.to) &&
                this.filterMatch(s.payload, f.payload)
            );
        },

        // Column-filtered events
        columnFilteredEvents() {
            let rows = this.events;
            let f = this.eventColFilters;
            return rows.filter(e =>
                this.filterMatch(e.eventType, f.type) &&
                this.filterMatch(e.message, f.message) &&
                this.filterMatch(e.entityId, f.entity) &&
                this.filterMatch(e.captainId, f.captain) &&
                this.filterMatch(e.missionId, f.mission)
            );
        },

        // Clear all column filters
        clearColumnFilters() {
            this.voyageColFilters = { title: '', status: '' };
            this.fleetColFilters = { name: '', description: '' };
            this.vesselColFilters = { fleet: '', name: '', repoUrl: '', branch: '' };
            this.missionColFilters = { title: '', status: '', vessel: '', captain: '', branch: '' };
            this.captainColFilters = { name: '', state: '', runtime: '' };
            this.dockColFilters = { vessel: '', captain: '', branch: '', status: '' };
            this.signalColFilters = { type: '', from: '', to: '', payload: '' };
            this.eventColFilters = { type: '', message: '', entity: '', captain: '', mission: '' };
        },

        // Client-side text search filter
        filterRows(rows) {
            if (!this.listSearch) return rows;
            let q = this.listSearch.toLowerCase();
            return rows.filter(r => JSON.stringify(r).toLowerCase().includes(q));
        },

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

        // Client-side filter for Vessels view (flat table with fleet filter)
        filteredVesselsList() {
            let rows = this.allVessels;
            if (this.vesselFilters.fleetId) {
                rows = rows.filter(v => v.fleetId === this.vesselFilters.fleetId);
            }
            return this.filterRows(rows);
        },

        // Client-side filter for Recent Missions on dashboard home
        filteredRecentMissions() {
            let rows = this.recentMissions;
            let f = this.recentMissionFilters;
            if (f.status) rows = rows.filter(m => m.status === f.status);
            if (f.vesselId) rows = rows.filter(m => m.vesselId === f.vesselId);
            if (f.captainId) rows = rows.filter(m => m.captainId === f.captainId);
            return rows;
        },

        // Client-side voyage filter for Merge Queue view
        filteredMergeQueue() {
            let rows = this.filterRows(this.mergeQueue);
            if (!this.mergeQueueVoyageFilter) return rows;
            let voyageId = this.mergeQueueVoyageFilter;
            return rows.filter(entry => entry._voyageId === voyageId);
        },

        // Client-side vessel filter for Voyages view
        filteredVoyages() {
            let rows = this.filterRows(this.voyages);
            if (!this.voyageVesselFilter) return rows;
            let vesselId = this.voyageVesselFilter;
            let map = this.voyageMissionMap || {};
            return rows.filter(v => {
                if (!(v.id in map)) return true; // show if map not yet loaded for this voyage
                return map[v.id].includes(vesselId);
            });
        },

        // Column sorting
        sortBy(column, rows) {
            if (this.sortColumn === column) {
                this.sortAsc = !this.sortAsc;
            } else {
                this.sortColumn = column;
                this.sortAsc = true;
            }
            return this.sortedRows(rows);
        },

        sortedRows(rows) {
            if (!this.sortColumn) return rows;
            let col = this.sortColumn;
            let asc = this.sortAsc;
            return [...rows].sort((a, b) => {
                let va = a[col], vb = b[col];
                if (va == null) va = '';
                if (vb == null) vb = '';
                if (typeof va === 'string') va = va.toLowerCase();
                if (typeof vb === 'string') vb = vb.toLowerCase();
                if (va < vb) return asc ? -1 : 1;
                if (va > vb) return asc ? 1 : -1;
                return 0;
            });
        },

        sortIcon(column) {
            if (this.sortColumn !== column) return '';
            return this.sortAsc ? ' \u25B2' : ' \u25BC';
        },

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

        // Action dropdown menu
        toggleActionMenu(event, id) {
            this.openActionMenu = this.openActionMenu === id ? null : id;
            if (this.openActionMenu && event) {
                this.$nextTick(() => {
                    let btn = event.target.closest('.action-menu-wrap');
                    if (!btn) return;
                    let dropdown = btn.querySelector('.action-menu-dropdown');
                    if (!dropdown) return;
                    let rect = btn.getBoundingClientRect();
                    let spaceBelow = window.innerHeight - rect.bottom;
                    if (spaceBelow < 200) {
                        dropdown.classList.add('drop-up');
                    } else {
                        dropdown.classList.remove('drop-up');
                    }
                });
            }
        },
        closeActionMenu() {
            this.openActionMenu = null;
        },

        // JSON viewer modal
        showJson(title, id, obj) {
            this.jsonViewer = {
                open: true,
                title: title || '',
                subtitle: '',
                id: id || '',
                content: JSON.stringify(obj, null, 2)
            };
        },
        closeJsonViewer() {
            this.jsonViewer = { open: false, title: '', subtitle: '', id: '', content: '' };
        },
        async copyJsonContent() {
            try {
                await navigator.clipboard.writeText(this.jsonViewer.content);
                this.toast('JSON copied to clipboard');
            } catch (e) {
                this.fallbackCopy(this.jsonViewer.content);
                this.toast('JSON copied to clipboard');
            }
        },

        // Viewer modal
        openViewer(title, content) {
            this.viewer = { open: true, title: title, content: content || '', copied: false };
        },
        closeViewer() {
            this.viewer = { open: false, title: '', content: '', copied: false };
        },
        async copyViewerContent() {
            try {
                await navigator.clipboard.writeText(this.viewer.content);
                this.viewer.copied = true;
                setTimeout(() => { this.viewer.copied = false; }, 2000);
            } catch (e) {
                this.toast('Failed to copy to clipboard', 'error');
            }
        },
        async viewMissionDiff(missionId) {
            this.diffViewerTitle = 'Loading diff...';
            this.diffViewerLoading = true;
            this.diffViewerOpen = true;
            this.diffViewerFiles = [];
            this.diffViewerSelectedFile = null;
            this.diffViewerRawDiff = '';
            try {
                let response = await this.api('GET', '/api/v1/missions/' + missionId + '/diff', null, 30000);
                let diff = response ? this.toCamel(response) : null;
                if (diff && !diff.error) {
                    let title = 'Mission Diff' + (diff.branch ? ' (' + diff.branch + ')' : '');
                    let rawDiff = diff.diff || '';
                    this.openDiffViewer(title, rawDiff);
                } else {
                    this.diffViewerTitle = 'Diff Error';
                    this.$nextTick(() => {
                        let el = document.getElementById('diff-content-area');
                        if (el) el.textContent = (diff && diff.error) || 'Failed to load diff';
                    });
                }
            } catch (e) {
                let errMsg = e.message || 'Request failed';
                let isNotFound = errMsg.toLowerCase().includes('not found') || errMsg.includes('404') || errMsg.toLowerCase().includes('no diff available');
                if (isNotFound) {
                    this.openDiffViewer('Mission Diff', '');
                } else {
                    this.diffViewerTitle = 'Diff Error';
                    this.$nextTick(() => {
                        let el = document.getElementById('diff-content-area');
                        if (el) el.textContent = errMsg;
                    });
                }
            } finally {
                this.diffViewerLoading = false;
            }
        },
        async viewMissionLog(missionId) {
            this.openLogViewer('Mission Log', 'mission', missionId, 200);
        },
        async viewCaptainLog(captainId) {
            this.openLogViewer('Captain Log', 'captain', captainId, 50);
        },
        async openLogViewer(title, entityType, entityId, defaultLines) {
            this.logViewer = { open: true, title: title, content: '', entityType: entityType, entityId: entityId, following: false, lineCount: defaultLines || 200, timer: null, copied: false, totalLines: 0 };
            await this.fetchLogContent();
        },
        async fetchLogContent() {
            let lv = this.logViewer;
            if (!lv.open || !lv.entityId) return;
            let endpoint = lv.entityType === 'mission'
                ? '/api/v1/missions/' + lv.entityId + '/log?lines=' + lv.lineCount
                : '/api/v1/captains/' + lv.entityId + '/log?lines=' + lv.lineCount;
            try {
                let result = await this.api('GET', endpoint);
                if (!this.logViewer.open) return;
                if (result && !result.error) {
                    this.logViewer.content = result.log || 'No log output';
                    this.logViewer.totalLines = result.totalLines || 0;
                    let showing = result.lines || 0;
                    let total = result.totalLines || 0;
                    this.logViewer.title = (lv.entityType === 'mission' ? 'Mission' : 'Captain') + ' Log (' + showing + ' of ' + total + ' lines)';
                    if (this.logViewer.following) {
                        this.$nextTick(() => {
                            let el = document.getElementById('log-viewer-content');
                            if (el) el.scrollTop = el.scrollHeight;
                        });
                    }
                } else {
                    this.logViewer.title = 'Log Error';
                    this.logViewer.content = (result && result.error) || 'Failed to load log';
                }
            } catch (e) {
                if (!this.logViewer.open) return;
                this.logViewer.title = 'Log Error';
                this.logViewer.content = e.message || 'Request failed';
            }
        },
        toggleLogFollow() {
            this.logViewer.following = !this.logViewer.following;
            if (this.logViewer.following) {
                this.fetchLogContent();
                this.logViewer.timer = setInterval(() => { this.fetchLogContent(); }, 1000);
            } else {
                this.stopLogFollow();
            }
        },
        stopLogFollow() {
            if (this.logViewer.timer) {
                clearInterval(this.logViewer.timer);
                this.logViewer.timer = null;
            }
            this.logViewer.following = false;
        },
        changeLogLineCount(count) {
            this.logViewer.lineCount = parseInt(count) || 200;
            this.fetchLogContent();
        },
        closeLogViewer() {
            this.stopLogFollow();
            this.logViewer = { open: false, title: '', content: '', entityType: '', entityId: '', following: false, lineCount: 200, timer: null, copied: false, totalLines: 0 };
        },
        async copyLogContent() {
            try {
                await navigator.clipboard.writeText(this.logViewer.content);
                this.logViewer.copied = true;
                setTimeout(() => { this.logViewer.copied = false; }, 2000);
            } catch (e) {
                this.toast('Failed to copy to clipboard', 'error');
            }
        },

        // Pagination helpers
        goToPage(pagingObj, page, loadFn) {
            if (page < 1 || page > pagingObj.totalPages) return;
            pagingObj.pageNumber = page;
            loadFn.call(this);
        },

        nextPage(pagingObj, loadFn) {
            this.goToPage(pagingObj, pagingObj.pageNumber + 1, loadFn);
        },

        prevPage(pagingObj, loadFn) {
            this.goToPage(pagingObj, pagingObj.pageNumber - 1, loadFn);
        },

        changePageSize(pagingObj, newSize, loadFn) {
            pagingObj.pageSize = parseInt(newSize) || 25;
            pagingObj.pageNumber = 1;
            loadFn.call(this);
        },

        /// <summary>
        /// Client-side pagination: slice an array and update paging metadata.
        /// </summary>
        paginateLocal(arr, pagingObj) {
            pagingObj.totalRecords = arr.length;
            pagingObj.totalPages = Math.ceil(arr.length / pagingObj.pageSize) || 1;
            if (pagingObj.pageNumber > pagingObj.totalPages) pagingObj.pageNumber = pagingObj.totalPages;
            let start = (pagingObj.pageNumber - 1) * pagingObj.pageSize;
            return arr.slice(start, start + pagingObj.pageSize);
        },

        /// <summary>
        /// No-op load function for client-side paginated tables.
        /// </summary>
        noopLoad() {},

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
