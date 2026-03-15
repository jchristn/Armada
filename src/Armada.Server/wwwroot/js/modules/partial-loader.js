// Armada Dashboard - Partial view loader
// This module is loaded via <script> tag and attaches to window.ArmadaModules

window.ArmadaModules = window.ArmadaModules || {};

window.ArmadaModules.partialLoader = {
    _partialCache: {},

    async loadViewPartial(viewName) {
        let container = document.getElementById('view-container');
        if (!container) return;

        // Hide container if no partial exists for this view
        let cached = this._partialCache[viewName];
        if (cached !== undefined) {
            container.innerHTML = cached;
            Alpine.initTree(container);
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
            Alpine.initTree(container);
        } catch (e) {
            // Network error or similar -- fall back gracefully
            this._partialCache[viewName] = '';
            container.innerHTML = '';
        }
    }
};
