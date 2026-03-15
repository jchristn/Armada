// Armada Dashboard - Partial view loader
// This module is loaded via <script> tag and attaches to window.ArmadaModules

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
            // Delay initTree to next frame so Alpine's reactive cycle is idle
            requestAnimationFrame(() => {
                try { Alpine.initTree(container); } catch (e) { /* modals will lack reactivity */ }
            });
        } catch (e) { /* network error */ }
    },

    async loadViewPartial(viewName) {
        let container = document.getElementById('view-' + viewName) || document.getElementById('view-container');
        if (!container) return;

        // Already loaded and initialized
        if (container.dataset.partialLoaded === viewName) return;

        // Fetch (or use cache)
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

        // Inject HTML
        container.innerHTML = html;
        container.dataset.partialLoaded = viewName;

        // Initialize Alpine directives on the next animation frame.
        // This ensures Alpine's reactive cycle (from navigate setting this.view)
        // has completed before we try to initialize the new subtree.
        requestAnimationFrame(() => {
            try {
                let children = Array.from(container.children);
                for (let i = 0; i < children.length; i++) {
                    Alpine.initTree(children[i]);
                }
            } catch (e) {
                console.warn('[Armada] initTree error for', viewName, e);
            }
        });
    }
};
