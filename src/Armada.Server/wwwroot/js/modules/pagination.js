// Armada Dashboard - Pagination utilities
// This module is loaded via <script> tag and attaches to window.ArmadaModules

window.ArmadaModules = window.ArmadaModules || {};

window.ArmadaModules.pagination = {
    goToPage(pagingObj, page, loadFn) {
        if (page < 1 || page > pagingObj.totalPages) return;
        pagingObj.pageNumber = page;
        loadFn.call(this);
    },

    nextPage(pagingObj, loadFn) {
        this.goToPage(pagingObj, pagingObj.pageNumber + 1, loadFn);
    },

    prevPage(pagingObj, loadFn) {
        this.goToPage(pagingObj, pagingObj.pageNumber - 1, loadFn);
    },

    changePageSize(pagingObj, newSize, loadFn) {
        pagingObj.pageSize = parseInt(newSize) || 25;
        pagingObj.pageNumber = 1;
        loadFn.call(this);
    },

    /// <summary>
    /// Client-side pagination: slice an array and update paging metadata.
    /// </summary>
    paginateLocal(arr, pagingObj) {
        pagingObj.totalRecords = arr.length;
        pagingObj.totalPages = Math.ceil(arr.length / pagingObj.pageSize) || 1;
        if (pagingObj.pageNumber > pagingObj.totalPages) pagingObj.pageNumber = pagingObj.totalPages;
        let start = (pagingObj.pageNumber - 1) * pagingObj.pageSize;
        return arr.slice(start, start + pagingObj.pageSize);
    },

    /// <summary>
    /// No-op load function for client-side paginated tables.
    /// </summary>
    noopLoad() {},
};
