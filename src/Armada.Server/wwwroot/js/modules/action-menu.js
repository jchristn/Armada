// Armada Dashboard - Action dropdown menu utilities
// This module is loaded via <script> tag and attaches to window.ArmadaModules

window.ArmadaModules = window.ArmadaModules || {};

window.ArmadaModules.actionMenu = {
    toggleActionMenu(event, id) {
        this.openActionMenu = this.openActionMenu === id ? null : id;
        if (this.openActionMenu && event) {
            this.$nextTick(() => {
                let btn = event.target.closest('.action-menu-wrap');
                if (!btn) return;
                let dropdown = btn.querySelector('.action-menu-dropdown');
                if (!dropdown) return;
                let rect = btn.getBoundingClientRect();
                let spaceBelow = window.innerHeight - rect.bottom;
                if (spaceBelow < 200) {
                    dropdown.classList.add('drop-up');
                } else {
                    dropdown.classList.remove('drop-up');
                }
            });
        }
    },

    closeActionMenu() {
        this.openActionMenu = null;
    },
};
