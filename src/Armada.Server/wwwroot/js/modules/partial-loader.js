// Armada Dashboard - Partial view loader
// This module is loaded via <script> tag and attaches to window.ArmadaModules
//
// Alpine.js 3.x uses a MutationObserver to automatically detect and initialize
// new DOM elements. We just need to inject HTML into the container and Alpine
// picks up the new directives. No explicit Alpine.initTree() call needed.

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
        } catch (e) {
            console.warn('[Armada] Failed to load modals:', e);
        }
    },

    async loadViewPartial(viewName) {
        let container = document.getElementById('view-' + viewName) || document.getElementById('view-container');
        if (!container) return;

        // Already loaded
        if (container.dataset.partialLoaded === viewName) return;

        // Fetch (or use cache)
        let html = this._partialCache[viewName];
        if (html === undefined) {
            try {
                let response = await fetch('/dashboard/views/' + viewName + '.html');
                if (!response.ok) { this._partialCache[viewName] = ''; return; }
                html = await response.text();
                this._partialCache[viewName] = html;
            } catch (e) {
                console.warn('[Armada] Failed to fetch partial:', viewName, e);
                return;
            }
        }
        if (!html) return;

        // Inject HTML -- Alpine's MutationObserver initializes the new elements
        container.innerHTML = html;
        container.dataset.partialLoaded = viewName;
    }
};
