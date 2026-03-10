// Armada Dashboard - Alpine.js component
function dashboard() {
    const API = window.location.origin;
    const WS_PROTOCOL = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
    const STORAGE_KEY = 'armada_api_key';

    return {
        // Theme
        darkMode: false,

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
        healthInfo: null,
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
        signalPaging: { pageNumber: 1, totalPages: 0, totalRecords: 0, pageSize: 25, totalMs: 0 },
        eventPaging: { pageNumber: 1, totalPages: 0, totalRecords: 0, pageSize: 50, totalMs: 0 },
        mergeQueuePaging: { pageNumber: 1, totalPages: 0, totalRecords: 0, pageSize: 25, totalMs: 0 },

        // Sorting
        sortColumn: null,
        sortAsc: true,

        // Dispatch form
        dispatch: { title: '', description: '', vesselId: '', priority: 100, voyageId: '' },
        dispatching: false,
        dispatchResult: null,
        showDispatchForm: false,

        // CRUD modals
        modal: null,       // 'create-fleet', 'edit-fleet', 'create-vessel', etc.
        modalData: {},
        modalLoading: false,

        // Viewer modal
        viewerTitle: '',
        viewerContent: '',
        viewerRawContent: '',
        viewerIsHtml: false,
        viewerLoading: false,

        // Confirm dialog
        confirmMessage: '',
        confirmResolve: null,

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

            this.apiKey = localStorage.getItem(STORAGE_KEY);

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
                this.loadMergeQueue()
            ]);
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
            } catch (e) { console.warn('Failed to load merge queue:', e); }
        },

        async loadHealth() {
            try { this.healthInfo = await this.api('GET', '/api/v1/status/health'); } catch (e) { console.warn('Failed to load health:', e); }
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
            this.showDispatchForm = false;
            this.dispatchResult = null;
            this.updateBreadcrumbs();

            // Reset pagination to page 1 when navigating to a view
            if (view === 'missions') this.missionPaging.pageNumber = 1;
            if (view === 'voyages') this.voyagePaging.pageNumber = 1;
            if (view === 'captains') this.captainPaging.pageNumber = 1;
            if (view === 'signals') this.signalPaging.pageNumber = 1;
            if (view === 'events') this.eventPaging.pageNumber = 1;
            if (view === 'merge-queue') this.mergeQueuePaging.pageNumber = 1;

            // Load data for new views
            if (view === 'signals') this.loadSignals();
            if (view === 'events') this.loadEvents();
            if (view === 'merge-queue') this.loadMergeQueue();
            if (view === 'server') this.loadHealth();
            if (view === 'missions') this.loadMissions();
            if (view === 'voyages') this.loadVoyageMissionMap();

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

        async loadMissionDiff(missionId) {
            let title = 'Diff: ' + (this.detail ? this.detail.title : missionId);
            this.viewerTitle = title;
            this.viewerContent = '';
            this.viewerRawContent = '';
            this.viewerIsHtml = true;
            this.viewerLoading = true;
            this.modal = 'viewer';
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
                let rawDiff = (result && result.diff) ? result.diff : 'No changes';
                this.viewerRawContent = rawDiff;
                this.viewerContent = this.formatDiffHtml(rawDiff);
            } catch (e) {
                let errMsg = e.name === 'AbortError' ? 'Request timed out after 30 seconds' : e.message;
                this.viewerRawContent = 'Error: ' + errMsg;
                this.viewerContent = 'Error: ' + errMsg;
                this.viewerIsHtml = false;
            } finally {
                this.detailDiffLoading = false;
                this.viewerLoading = false;
            }
        },

        async loadMissionLog(missionId) {
            this.detailLog = null;
            try {
                let result = await this.api('GET', '/api/v1/missions/' + missionId + '/log?lines=500');
                this.detailLog = result;
            } catch (e) {
                this.detailLog = { error: e.message };
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
                'home': 'Dashboard', 'fleets': 'Fleets', 'voyages': 'Voyages',
                'captains': 'Captains', 'missions': 'Missions', 'dispatch': 'Dispatch',
                'signals': 'Signals', 'events': 'Events', 'merge-queue': 'Merge Queue',
                'server': 'Server', 'config': 'Config'
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
        showConfirm(message) {
            this.confirmMessage = message;
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
        },

        confirmNo() {
            if (this.confirmResolve) this.confirmResolve(false);
            this.confirmResolve = null;
            this.modal = null;
            this.confirmMessage = '';
        },

        // ============================================================
        // Toast notifications
        // ============================================================
        toast(message, type) {
            type = type || 'success';
            let id = ++this.toastCounter;
            this.toasts.push({ id, message, type });
            setTimeout(() => {
                this.toasts = this.toasts.filter(t => t.id !== id);
            }, 4000);
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
                    runtime: this.modalData.runtime || 'ClaudeCode',
                    maxParallelism: parseInt(this.modalData.maxParallelism, 10) || 1
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
                    runtime: this.modalData.runtime,
                    maxParallelism: parseInt(this.modalData.maxParallelism, 10) || 1
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
            let tooltips = {
                'Idle': 'Captain is available and waiting for mission assignment',
                'Working': 'Captain is actively executing a mission',
                'Stalled': 'Captain process is unresponsive — may need intervention',
                'Pending': 'Mission created, waiting to be assigned to a captain',
                'Assigned': 'Mission has been assigned to a captain but not yet started',
                'InProgress': 'Captain is actively working on this mission',
                'Testing': 'Mission work is being tested or validated',
                'Review': 'Mission work is awaiting code review',
                'Complete': 'Mission finished successfully',
                'Failed': 'Mission encountered an error and did not complete',
                'Cancelled': 'Mission was cancelled before completion',
                'Open': 'Voyage is open and accepting missions',
                'Queued': 'Merge entry is queued and waiting to be processed',
                'Merged': 'Branch has been successfully merged',
            };
            return tooltips[status] || status;
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

        // Client-side filter for Recent Missions on dashboard home
        filteredRecentMissions() {
            let rows = this.recentMissions;
            let f = this.recentMissionFilters;
            if (f.status) rows = rows.filter(m => m.status === f.status);
            if (f.vesselId) rows = rows.filter(m => m.vesselId === f.vesselId);
            if (f.captainId) rows = rows.filter(m => m.captainId === f.captainId);
            return rows;
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
        openAddCaptain() { this.modal = 'add-captain'; this.modalData = { name: '', runtime: 'ClaudeCode', maxParallelism: 1 }; },
        openEditCaptain(c) { this.modal = 'edit-captain'; this.modalData = { id: c.id, name: c.name, runtime: c.runtime || 'ClaudeCode', maxParallelism: c.maxParallelism ?? 1 }; },
        openEditMission(m) { this.modal = 'edit-mission'; this.modalData = { id: m.id, title: m.title, description: m.description || '', priority: m.priority || 100, vesselId: m.vesselId || '', voyageId: m.voyageId || '' }; },
        openCreateVoyage() { this.modal = 'create-voyage'; this.voyageForm = { title: '', description: '', vesselId: '', missions: [{ title: '', description: '' }] }; },
        openSendSignal() { this.modal = 'send-signal'; this.modalData = { type: 'Nudge', payload: '', fromCaptainId: '', toCaptainId: '' }; },
        openEnqueueMerge() { this.modal = 'enqueue-merge'; this.modalData = { missionId: '', vesselId: '', branchName: '', targetBranch: 'main' }; },

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
            this.openViewer('Loading diff...', '');
            try {
                let response = await this.api('GET', '/api/v1/missions/' + missionId + '/diff', null, 30000);
                let diff = response ? this.toCamel(response) : null;
                if (diff && !diff.error) {
                    let title = 'Mission Diff' + (diff.branch ? ' (' + diff.branch + ')' : '');
                    this.viewer.title = title;
                    this.viewer.content = diff.diff || 'No changes';
                } else {
                    this.viewer.title = 'Diff Error';
                    this.viewer.content = (diff && diff.error) || 'Failed to load diff';
                }
            } catch (e) {
                this.viewer.title = 'Diff Error';
                this.viewer.content = e.message || 'Request failed';
            }
        },
        async viewMissionLog(missionId) {
            this.openViewer('Loading log...', '');
            try {
                let result = await this.api('GET', '/api/v1/missions/' + missionId + '/log?lines=500');
                if (result && !result.error) {
                    let title = 'Mission Log (' + (result.lines || 0) + ' of ' + (result.totalLines || 0) + ' lines)';
                    this.viewer.title = title;
                    this.viewer.content = result.log || 'No log output';
                } else {
                    this.viewer.title = 'Log Error';
                    this.viewer.content = (result && result.error) || 'Failed to load log';
                }
            } catch (e) {
                this.viewer.title = 'Log Error';
                this.viewer.content = e.message || 'Request failed';
            }
        },
        async viewCaptainLog(captainId) {
            this.openViewer('Loading captain log...', '');
            try {
                let result = await this.api('GET', '/api/v1/captains/' + captainId + '/log?lines=500');
                if (result && !result.error) {
                    let title = 'Captain Log (' + (result.lines || 0) + ' of ' + (result.totalLines || 0) + ' lines)';
                    this.viewer.title = title;
                    this.viewer.content = result.log || 'No log output';
                } else {
                    this.viewer.title = 'Log Error';
                    this.viewer.content = (result && result.error) || 'Failed to load log';
                }
            } catch (e) {
                this.viewer.title = 'Log Error';
                this.viewer.content = e.message || 'Request failed';
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

        // Keyboard shortcuts
        handleKeyboard(e) {
            if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA' || e.target.tagName === 'SELECT') return;
            if (this.viewer.open && e.key === 'Escape') { this.closeViewer(); return; }
            if (this.modal) {
                if (e.key === 'Escape') this.modal = null;
                return;
            }
            if (e.key === 'r') this.refresh();
        }
    };
}
