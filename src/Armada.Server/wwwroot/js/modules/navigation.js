// Armada Dashboard - Navigation, breadcrumbs, and entity routing
// This module is loaded via <script> tag and attaches to window.ArmadaModules

window.ArmadaModules = window.ArmadaModules || {};

window.ArmadaModules.navigation = {
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
        if (view === 'captains') { this.selectedCaptains = []; this.loadCaptains(); }
        if (view === 'signals') this.selectedSignals = [];
        if (view === 'events') this.selectedEvents = [];
        if (view === 'merge-queue') this.selectedMergeQueue = [];
        if (view === 'fleets-list') { this.loadFleets(); }
        if (view === 'fleets') { this.selectedVessels = []; this.sortColumn = '_fleetName'; this.sortAsc = true; this.loadFleets().then(() => { this.loadVessels(); }); }
        if (view === 'docks') { this.selectedDocks = []; this.loadDocks(); }
        if (view === 'server') { this.loadHealth(); this.loadSettings(); }
        if (view === 'missions') this.loadMissions();
        if (view === 'voyages') { this.loadVoyages(); this.loadVoyageMissionMap(); }
        if (view === 'doctor') this.runDoctorChecks();

        if (detailView) this.loadDetail(detailView, detailId);

        // Load partial view if available
        if (this.loadViewPartial) {
            this.loadViewPartial(view);
            if (detailView) {
                this.loadViewPartial(detailView);
            }
        }
    },

    updateBreadcrumbs() {
        this.breadcrumbs = [];
        let viewLabels = {
            'home': 'Dashboard', 'fleets-list': 'Fleets', 'fleets': 'Vessels', 'voyages': 'Voyages',
            'captains': 'Captains', 'missions': 'Missions', 'dispatch': 'Dispatch',
            'signals': 'Signals', 'events': 'Events', 'merge-queue': 'Merge Queue',
            'docks': 'Docks', 'server': 'Server', 'doctor': 'Doctor', 'notifications': 'Notifications'
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

    /// <summary>
    /// Returns navigation info for an entity ID based on its prefix, or null if no detail view exists.
    /// </summary>
    entityNav(entityId) {
        if (!entityId) return null;
        let prefix = entityId.substring(0, 4);
        let map = {
            'flt_': { view: 'fleets-list', detail: 'fleet-detail' },
            'vsl_': { view: 'fleets', detail: 'vessel-detail' },
            'cpt_': { view: 'captains', detail: 'captain-detail' },
            'msn_': { view: 'missions', detail: 'mission-detail' },
            'vyg_': { view: 'voyages', detail: 'voyage-detail' },
            'sig_': { view: 'signals', detail: 'signal-detail' }
        };
        return map[prefix] || null;
    },
};
