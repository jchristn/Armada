// Armada Dashboard - Sorting and filtering utilities
// This module is loaded via <script> tag and attaches to window.ArmadaModules

window.ArmadaModules = window.ArmadaModules || {};

window.ArmadaModules.sorting = {
    sortBy(column, rows) {
        if (this.sortColumn === column) {
            this.sortAsc = !this.sortAsc;
        } else {
            this.sortColumn = column;
            this.sortAsc = true;
        }
        return this.sortedRows(rows);
    },

    sortedRows(rows) {
        if (!this.sortColumn) return rows;
        let col = this.sortColumn;
        let asc = this.sortAsc;
        return [...rows].sort((a, b) => {
            let va = a[col], vb = b[col];
            if (va == null) va = '';
            if (vb == null) vb = '';
            if (typeof va === 'string') va = va.toLowerCase();
            if (typeof vb === 'string') vb = vb.toLowerCase();
            if (va < vb) return asc ? -1 : 1;
            if (va > vb) return asc ? 1 : -1;
            return 0;
        });
    },

    sortIcon(column) {
        if (this.sortColumn !== column) return '';
        return this.sortAsc ? ' \u25B2' : ' \u25BC';
    },

    filterRows(rows) {
        if (!this.listSearch) return rows;
        let q = this.listSearch.toLowerCase();
        return rows.filter(r => JSON.stringify(r).toLowerCase().includes(q));
    },

    // Case-insensitive substring match for column filters
    filterMatch(value, filter) {
        if (!filter) return true;
        return (String(value || '')).toLowerCase().includes(filter.toLowerCase());
    },
};
