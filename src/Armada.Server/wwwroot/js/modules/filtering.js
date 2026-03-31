// Armada Dashboard - Column filtering and client-side filter utilities
// This module is loaded via <script> tag and attaches to window.ArmadaModules

window.ArmadaModules = window.ArmadaModules || {};

window.ArmadaModules.filtering = {
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

    columnFilteredFleets() {
        let rows = this.filterRows(this.fleets);
        let f = this.fleetColFilters;
        return rows.filter(fl =>
            this.filterMatch(fl.name, f.name) &&
            this.filterMatch(fl.description, f.description)
        );
    },

    columnFilteredVoyages() {
        let rows = this.filteredVoyages();
        let f = this.voyageColFilters;
        return rows.filter(v =>
            this.filterMatch(v.title || v.id, f.title) &&
            this.filterMatch(v.status, f.status)
        );
    },

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

    columnFilteredCaptains() {
        let rows = this.filterRows(this.captains);
        let f = this.captainColFilters;
        return rows.filter(c =>
            this.filterMatch(c.name, f.name) &&
            this.filterMatch(c.state, f.state) &&
            this.filterMatch(c.runtime || 'ClaudeCode', f.runtime)
        );
    },

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

    filteredVesselsList() {
        let rows = this.allVessels;
        if (this.vesselFilters.fleetId) {
            rows = rows.filter(v => v.fleetId === this.vesselFilters.fleetId);
        }
        return this.filterRows(rows);
    },

    filteredMergeQueue() {
        let rows = this.filterRows(this.mergeQueue);
        if (this.mergeQueueStatusFilter) {
            let status = this.mergeQueueStatusFilter;
            rows = rows.filter(entry => entry.status === status);
        }
        if (this.mergeQueueVoyageFilter) {
            let voyageId = this.mergeQueueVoyageFilter;
            rows = rows.filter(entry => entry._voyageId === voyageId);
        }
        return rows;
    },

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

    filteredRecentMissions() {
        let rows = this.recentMissions;
        let f = this.recentMissionFilters;
        if (f.status) rows = rows.filter(m => m.status === f.status);
        if (f.vesselId) rows = rows.filter(m => m.vesselId === f.vesselId);
        if (f.captainId) rows = rows.filter(m => m.captainId === f.captainId);
        return rows;
    },

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
};
