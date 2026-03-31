// Armada Dashboard - Multi-select toggle, select-all, clear, and bulk delete utilities
// This module is loaded via <script> tag and attaches to window.ArmadaModules

window.ArmadaModules = window.ArmadaModules || {};

window.ArmadaModules.selection = {
    // Dock multi-select
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
};
