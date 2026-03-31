// Armada Dashboard - Data loading methods
// This module is loaded via <script> tag and attaches to window.ArmadaModules

window.ArmadaModules = window.ArmadaModules || {};

window.ArmadaModules.dataLoaders = {
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

    async loadRecentMissions() {
        try {
            let result = await this.api('GET', '/api/v1/missions?pageSize=10&order=CreatedDescending');
            this.recentMissions = (result && result.objects) ? result.objects : [];
            if (!Array.isArray(this.recentMissions)) this.recentMissions = [];
        } catch (e) { console.warn('Failed to load recent missions:', e); }
    },

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
            if (this.mergeQueueStatusFilter) params.push('status=' + encodeURIComponent(this.mergeQueueStatusFilter));
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

    async loadSettings() {
        try { this.serverSettings = await this.api('GET', '/api/v1/settings'); } catch (e) { console.warn('Failed to load settings:', e); }
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
};
