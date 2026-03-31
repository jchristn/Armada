// Armada Dashboard - Status tooltips and formatting utilities
// This module is loaded via <script> tag and attaches to window.ArmadaModules

window.ArmadaModules = window.ArmadaModules || {};

window.ArmadaModules.status = {
    formatTime(utcStr) {
        if (!utcStr) return '';
        let d = new Date(utcStr);
        let now = new Date();
        let diffMs = now - d;
        let diffMin = Math.floor(diffMs / 60000);
        if (diffMin < 1) return 'just now';
        if (diffMin < 60) return diffMin + 'm ago';
        let diffHr = Math.floor(diffMin / 60);
        if (diffHr < 24) return diffHr + 'h ago';
        return d.toLocaleDateString();
    },

    formatTimeAbsolute(utcStr) {
        if (!utcStr) return '';
        return new Date(utcStr).toLocaleString();
    },

    statusTooltip(status) {
        if (!status && status !== false) return '';
        let tooltips = {
            // Mission statuses
            'pending': 'Mission created, waiting to be assigned to a captain',
            'assigned': 'Mission has been assigned to a captain but work has not started yet',
            'inprogress': 'A captain is actively working on this mission',
            'workproduced': 'The captain finished and code exists on a branch, awaiting landing to main',
            'testing': 'Mission work is being tested or validated',
            'review': 'Mission work is ready for code review',
            'complete': 'Mission completed successfully and work has been integrated',
            'failed': 'Mission encountered an error and did not complete successfully',
            'landingfailed': 'Code was produced but landing failed (merge conflict, push error, etc.)',
            'cancelled': 'Mission was cancelled before completion',
            // Voyage statuses
            'open': 'Voyage is open and missions are being dispatched',
            // Captain states
            'idle': 'Captain is available and waiting for a mission assignment',
            'working': 'Captain is actively executing a mission',
            'stalled': 'Captain process appears unresponsive -- may need intervention',
            'stopping': 'Captain is in the process of being stopped',
            // Merge queue statuses
            'queued': 'Entry is queued and waiting to be processed',
            'passed': 'Tests passed -- ready to land',
            'landed': 'Branch has been successfully merged into the target branch',
            // Signal types
            'assignment': 'Captain was assigned a new mission',
            'progress': 'Progress update from a captain or the system',
            'completion': 'A captain has finished its work',
            'error': 'An error occurred during mission execution',
            'heartbeat': 'Periodic heartbeat indicating the captain is alive',
            'nudge': 'A nudge signal sent to prompt action',
            'mail': 'A message sent between the admiral and a captain',
            // Dock statuses
            'active': 'Dock worktree is active and may be in use by a captain',
            'inactive': 'Dock worktree is no longer active',
            'true': 'Dock worktree is active and may be in use by a captain',
            'false': 'Dock worktree is no longer active',
            // Doctor health check results
            'pass': 'Health check passed -- no issues detected',
            'warn': 'Health check has warnings -- review recommended',
            'fail': 'Health check failed -- action required',
        };
        let key = String(status).toLowerCase();
        return tooltips[key] || '';
    },
};
