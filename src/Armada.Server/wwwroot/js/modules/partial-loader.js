// Armada Dashboard - Partial view loader
// This module is loaded via <script> tag and attaches to window.ArmadaModules

window.ArmadaModules = window.ArmadaModules || {};

window.ArmadaModules.partialLoader = {
    _partialCache: {},

    /// <summary>
    /// Initialize Alpine directives on newly injected children without
    /// re-initializing the container itself (which is already managed by
    /// the parent x-data scope).
    /// </summary>
    _initChildren(container) {
        for (let child of container.children) {
            Alpine.initTree(child);
        }
    },

    async loadModalsPartial() {
        let container = document.getElementById('modals-container');
        if (!container) return;

        let url = '/dashboard/views/modals.html';
        try {
            let response = await fetch(url);
            if (!response.ok) return;
            let html = await response.text();
            container.innerHTML = html;
            this._initChildren(container);
        } catch (e) {
            // Network error -- modals will not be available
        }
    },

    async loadViewPartial(viewName) {
        // Look for a view-specific container first, then fall back to generic container
        let container = document.getElementById('view-' + viewName) || document.getElementById('view-container');
        if (!container) return;

        // Return cached partial if already loaded
        let cached = this._partialCache[viewName];
        if (cached !== undefined) {
            container.innerHTML = cached;
            this._initChildren(container);
            return;
        }

        let url = '/dashboard/views/' + viewName + '.html';
        try {
            let response = await fetch(url);
            if (!response.ok) {
                // No partial for this view -- that is fine, not all views have partials yet
                this._partialCache[viewName] = '';
                container.innerHTML = '';
                return;
            }
            let html = await response.text();
            this._partialCache[viewName] = html;
            container.innerHTML = html;
            this._initChildren(container);
        } catch (e) {
            // Network error or similar -- fall back gracefully
            this._partialCache[viewName] = '';
            container.innerHTML = '';
        }
    }
};
