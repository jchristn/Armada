// Armada Dashboard - Partial view loader
// This module is loaded via <script> tag and attaches to window.ArmadaModules
//
// Strategy: preload ALL view partials at startup so that tab navigation only
// toggles x-show visibility. This eliminates timing issues between Alpine's
// reactive cycle and dynamic HTML injection during navigation.

window.ArmadaModules = window.ArmadaModules || {};

window.ArmadaModules.partialLoader = {
    _partialCache: {},

    async loadModalsPartial() {
        let container = document.getElementById('modals-container');
        if (!container) return;

        try {
            let response = await fetch('/dashboard/views/modals.html');
            if (!response.ok) return;
            container.innerHTML = await response.text();
        } catch (e) { /* network error */ }
    },

    async preloadAllPartials() {
        let views = [
            'home', 'fleets-list', 'fleets', 'voyages', 'missions', 'dispatch',
            'captains', 'docks', 'signals', 'events', 'merge-queue', 'server', 'doctor'
        ];

        // Fetch all view HTML files in parallel
        await Promise.all(views.map(viewName =>
            fetch('/dashboard/views/' + viewName + '.html')
                .then(r => r.ok ? r.text() : '')
                .then(html => { this._partialCache[viewName] = html; })
                .catch(() => { this._partialCache[viewName] = ''; })
        ));

        // Inject each into its container
        for (let i = 0; i < views.length; i++) {
            let viewName = views[i];
            let html = this._partialCache[viewName];
            if (!html) continue;

            let container = document.getElementById('view-' + viewName) || document.getElementById('view-container');
            if (!container) continue;
            if (container.dataset.partialLoaded === viewName) continue;

            container.innerHTML = html;
            container.dataset.partialLoaded = viewName;
        }
    },

    async loadViewPartial(viewName) {
        // If already preloaded, nothing to do — x-show handles visibility
        let container = document.getElementById('view-' + viewName) || document.getElementById('view-container');
        if (!container) return;
        if (container.dataset.partialLoaded === viewName) return;

        // Fallback: lazy-load if somehow not preloaded
        let html = this._partialCache[viewName];
        if (html === undefined) {
            try {
                let response = await fetch('/dashboard/views/' + viewName + '.html');
                if (!response.ok) { this._partialCache[viewName] = ''; return; }
                html = await response.text();
                this._partialCache[viewName] = html;
            } catch (e) { return; }
        }
        if (!html) return;

        container.innerHTML = html;
        container.dataset.partialLoaded = viewName;
    }
};
