// Armada Dashboard - Diff, Log, and JSON viewer utilities
// This module is loaded via <script> tag and attaches to window.ArmadaModules

window.ArmadaModules = window.ArmadaModules || {};

window.ArmadaModules.viewers = {
    formatDiffHtml(text) {
        if (!text) return '';
        let escaped = text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
        return escaped.split('\n').map(line => {
            if (line.startsWith('@@')) return '<span class="diff-line-section">' + line + '</span>';
            if (line.startsWith('+')) return '<span class="diff-line-add">' + line + '</span>';
            if (line.startsWith('-')) return '<span class="diff-line-remove">' + line + '</span>';
            return line;
        }).join('\n');
    },

    parseDiffFiles(rawDiff) {
        if (!rawDiff || rawDiff === 'No changes') return [];
        let files = [];
        let lines = rawDiff.split('\n');
        let currentFile = null;
        for (let i = 0; i < lines.length; i++) {
            let line = lines[i];
            if (line.startsWith('diff --git ')) {
                if (currentFile) files.push(currentFile);
                let match = line.match(/diff --git a\/(.*?) b\/(.*)/);
                let name = match ? match[2] : line.substring(11);
                currentFile = { name: name, additions: 0, deletions: 0, startLine: i };
            } else if (currentFile) {
                if (line.startsWith('+') && !line.startsWith('+++')) currentFile.additions++;
                else if (line.startsWith('-') && !line.startsWith('---')) currentFile.deletions++;
            }
        }
        if (currentFile) files.push(currentFile);
        return files;
    },

    renderFileDiff(rawDiff, fileName) {
        if (!rawDiff) return '';
        let lines = rawDiff.split('\n');
        let inFile = false;
        let fileLines = [];
        for (let i = 0; i < lines.length; i++) {
            let line = lines[i];
            if (line.startsWith('diff --git ')) {
                if (inFile) break;
                let match = line.match(/diff --git a\/(.*?) b\/(.*)/);
                let name = match ? match[2] : '';
                if (name === fileName) inFile = true;
            }
            if (inFile) fileLines.push(line);
        }
        return this.renderDiffLines(fileLines);
    },

    renderAllDiffs(rawDiff) {
        if (!rawDiff) return '';
        return this.renderDiffLines(rawDiff.split('\n'));
    },

    renderDiffLines(lines) {
        let html = '';
        let oldNum = 0, newNum = 0;
        for (let i = 0; i < lines.length; i++) {
            let line = lines[i];
            let escaped = line.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
            if (line.startsWith('diff --git ')) {
                html += '<div class="diff-file-header">' + escaped + '</div>';
            } else if (line.startsWith('@@')) {
                let hunkMatch = line.match(/@@ -(\d+)/);
                if (hunkMatch) { oldNum = parseInt(hunkMatch[1]); }
                let newMatch = line.match(/@@ -\d+(?:,\d+)? \+(\d+)/);
                if (newMatch) { newNum = parseInt(newMatch[1]); }
                html += '<div class="diff-hunk-header">' + escaped + '</div>';
            } else if (line.startsWith('---') || line.startsWith('+++') || line.startsWith('index ') || line.startsWith('new file') || line.startsWith('deleted file') || line.startsWith('old mode') || line.startsWith('new mode') || line.startsWith('similarity index') || line.startsWith('rename from') || line.startsWith('rename to') || line.startsWith('Binary files')) {
                html += '<div class="diff-meta-line">' + escaped + '</div>';
            } else if (line.startsWith('+')) {
                html += '<div class="diff-line diff-line-add"><span class="diff-line-num diff-line-num-old"></span><span class="diff-line-num diff-line-num-new">' + newNum + '</span><span class="diff-line-content">' + escaped + '</span></div>';
                newNum++;
            } else if (line.startsWith('-')) {
                html += '<div class="diff-line diff-line-del"><span class="diff-line-num diff-line-num-old">' + oldNum + '</span><span class="diff-line-num diff-line-num-new"></span><span class="diff-line-content">' + escaped + '</span></div>';
                oldNum++;
            } else {
                html += '<div class="diff-line diff-line-ctx"><span class="diff-line-num diff-line-num-old">' + (oldNum || '') + '</span><span class="diff-line-num diff-line-num-new">' + (newNum || '') + '</span><span class="diff-line-content">' + escaped + '</span></div>';
                if (oldNum) oldNum++;
                if (newNum) newNum++;
            }
        }
        return html;
    },

    openDiffViewer(title, rawDiff) {
        this.diffViewerTitle = title;
        this.diffViewerRawDiff = rawDiff;
        this.diffViewerSelectedFile = null;
        this.diffViewerOpen = true;
        let isEmpty = !rawDiff || !rawDiff.trim() || rawDiff === 'No changes' || rawDiff === 'No modified files';
        this.diffViewerFiles = isEmpty ? [] : this.parseDiffFiles(rawDiff);
        if (isEmpty) {
            this.diffViewerContentHtml = '<div class="diff-empty-state"><span class="text-dim">No modified files</span></div>';
        } else {
            this.diffViewerContentHtml = this.renderAllDiffs(rawDiff);
        }
    },

    closeDiffViewer() {
        this.diffViewerOpen = false;
        this.diffViewerRawDiff = '';
        this.diffViewerFiles = [];
        this.diffViewerSelectedFile = null;
        this.diffViewerContentHtml = '';
    },

    selectDiffFile(fileName) {
        if (this.diffViewerSelectedFile === fileName) {
            this.diffViewerSelectedFile = null;
            this.diffViewerContentHtml = this.renderAllDiffs(this.diffViewerRawDiff);
        } else {
            this.diffViewerSelectedFile = fileName;
            this.diffViewerContentHtml = this.renderFileDiff(this.diffViewerRawDiff, fileName);
        }
    },

    copyDiffRaw(buttonEl) {
        let text = this.diffViewerRawDiff;
        if (!text) return;
        this.copyToClipboard(text, buttonEl);
    },

    async loadMissionDiff(missionId) {
        let title = 'Diff: ' + (this.detail ? this.detail.title : missionId);
        this.diffViewerTitle = title;
        this.diffViewerLoading = true;
        this.diffViewerOpen = true;
        this.diffViewerFiles = [];
        this.diffViewerSelectedFile = null;
        this.diffViewerRawDiff = '';
        this.diffViewerContentHtml = '';
        this.detailDiffLoading = true;
        try {
            let controller = new AbortController();
            let timeoutId = setTimeout(() => controller.abort(), 30000);
            let opts = {
                method: 'GET',
                headers: { 'Content-Type': 'application/json' },
                signal: controller.signal
            };
            if (this.apiKey) opts.headers['X-Api-Key'] = this.apiKey;
            let resp = await fetch(window.location.origin + '/api/v1/missions/' + missionId + '/diff', opts);
            clearTimeout(timeoutId);
            if (!resp.ok) {
                let errText = await resp.text();
                let errMsg = 'HTTP ' + resp.status;
                try { let e = JSON.parse(errText); errMsg = e.Message || e.message || e.Error || e.error || errMsg; } catch (_) { }
                throw new Error(errMsg);
            }
            let text = await resp.text();
            let result = text ? this.toCamel(JSON.parse(text)) : null;
            let rawDiff = (result && result.diff) ? result.diff : '';
            this.openDiffViewer(title, rawDiff);
        } catch (e) {
            let errMsg = e.name === 'AbortError' ? 'Request timed out after 30 seconds' : e.message;
            let isNotFound = errMsg.toLowerCase().includes('not found') || errMsg.includes('404') || errMsg.toLowerCase().includes('no diff available');
            if (isNotFound) {
                this.openDiffViewer(title, '');
            } else {
                this.diffViewerRawDiff = 'Error: ' + errMsg;
                this.diffViewerFiles = [];
                this.diffViewerContentHtml = '<div class="diff-empty-state"><span class="text-dim">Error: ' + errMsg.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;') + '</span></div>';
            }
        } finally {
            this.detailDiffLoading = false;
            this.diffViewerLoading = false;
        }
    },

    async loadMissionLog(missionId) {
        let title = this.detail ? this.detail.title : missionId;
        this.openViewer('Log: ' + title, 'Loading…');
        try {
            let result = await this.api('GET', '/api/v1/missions/' + missionId + '/log?lines=500');
            let logText = result.log || 'No log output';
            let lineInfo = '(' + (result.lines || 0) + ' of ' + (result.totalLines || 0) + ' lines)';
            this.viewerTitle = 'Log: ' + title + ' ' + lineInfo;
            this.viewerContent = logText;
            this.viewerRawText = logText;
        } catch (e) {
            this.viewerContent = 'Log unavailable: ' + e.message;
            this.viewerRawText = '';
        }
    },

    async loadCaptainLog(captainId) {
        let captainName = this.detail ? this.detail.name : captainId;
        this.viewerTitle = 'Log: ' + captainName;
        this.viewerContent = '';
        this.viewerRawContent = '';
        this.viewerIsHtml = false;
        this.viewerLoading = true;
        this.modal = 'viewer';
        try {
            let result = await this.api('GET', '/api/v1/captains/' + captainId + '/log?lines=500');
            this.viewerContent = result.log || 'No log output';
            this.viewerRawContent = this.viewerContent;
        } catch (e) {
            this.viewerContent = 'Log unavailable: ' + e.message;
            this.viewerRawContent = this.viewerContent;
        } finally {
            this.viewerLoading = false;
        }
    },

    // JSON viewer modal
    showJson(title, id, obj) {
        this.jsonViewer = {
            open: true,
            title: title || '',
            subtitle: '',
            id: id || '',
            content: JSON.stringify(obj, null, 2)
        };
    },

    closeJsonViewer() {
        this.jsonViewer = { open: false, title: '', subtitle: '', id: '', content: '' };
    },

    copyJsonContent(buttonEl) {
        this.copyToClipboard(this.jsonViewer.content, buttonEl);
    },

    // Viewer modal (simple text viewer)
    openViewer(title, content) {
        this.viewer = { open: true, title: title, content: content || '' };
    },

    closeViewer() {
        this.viewer = { open: false, title: '', content: '' };
    },

    copyViewerContent(buttonEl) {
        this.copyToClipboard(this.viewer.content, buttonEl);
    },

    async viewMissionDiff(missionId) {
        this.diffViewerTitle = 'Loading diff...';
        this.diffViewerLoading = true;
        this.diffViewerOpen = true;
        this.diffViewerFiles = [];
        this.diffViewerSelectedFile = null;
        this.diffViewerRawDiff = '';
        this.diffViewerContentHtml = '';
        try {
            let response = await this.api('GET', '/api/v1/missions/' + missionId + '/diff', null, 30000);
            let diff = response ? this.toCamel(response) : null;
            if (diff && !diff.error) {
                let title = 'Mission Diff' + (diff.branch ? ' (' + diff.branch + ')' : '');
                let rawDiff = diff.diff || '';
                this.openDiffViewer(title, rawDiff);
            } else {
                this.diffViewerTitle = 'Mission Diff';
                this.diffViewerContentHtml = '<div class="diff-empty-state"><p>No diff available for this mission.</p><p class="text-dim" style="font-size:0.85rem; margin-top:0.5rem">This can happen if the mission has not produced any work yet, the worktree has been cleaned up, or the branch was already merged and deleted.</p></div>';
            }
        } catch (e) {
            let errMsg = e.message || 'Request failed';
            let isNotFound = errMsg.toLowerCase().includes('not found') || errMsg.includes('404') || errMsg.toLowerCase().includes('no diff available');
            this.diffViewerTitle = 'Mission Diff';
            let noDiffMsg = '<div class="diff-empty-state"><p>No diff available for this mission.</p><p class="text-dim" style="font-size:0.85rem; margin-top:0.5rem">This can happen if the mission has not produced any work yet, the worktree has been cleaned up, or the branch was already merged and deleted.</p></div>';
            let failMsg = '<div class="diff-empty-state"><p>Failed to load diff</p><p class="text-dim" style="font-size:0.85rem; margin-top:0.5rem">' + errMsg.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;') + '</p></div>';
            this.diffViewerContentHtml = isNotFound ? noDiffMsg : failMsg;
        } finally {
            this.diffViewerLoading = false;
        }
    },

    async viewMissionLog(missionId) {
        this.openLogViewer('Mission Log', 'mission', missionId, 200);
    },

    async viewCaptainLog(captainId) {
        this.openLogViewer('Captain Log', 'captain', captainId, 50);
    },

    async openLogViewer(title, entityType, entityId, defaultLines) {
        this.logViewer = { open: true, title: title, content: '', entityType: entityType, entityId: entityId, following: false, lineCount: defaultLines || 200, timer: null, totalLines: 0 };
        await this.fetchLogContent();
    },

    async fetchLogContent() {
        let lv = this.logViewer;
        if (!lv.open || !lv.entityId) return;
        let endpoint = lv.entityType === 'mission'
            ? '/api/v1/missions/' + lv.entityId + '/log?lines=' + lv.lineCount
            : '/api/v1/captains/' + lv.entityId + '/log?lines=' + lv.lineCount;
        try {
            let result = await this.api('GET', endpoint);
            if (!this.logViewer.open) return;
            if (result && !result.error) {
                this.logViewer.content = result.log || 'No log output';
                this.logViewer.totalLines = result.totalLines || 0;
                let showing = result.lines || 0;
                let total = result.totalLines || 0;
                this.logViewer.title = (lv.entityType === 'mission' ? 'Mission' : 'Captain') + ' Log (' + showing + ' of ' + total + ' lines)';
                if (this.logViewer.following) {
                    this.$nextTick(() => {
                        let el = document.getElementById('log-viewer-content');
                        if (el) el.scrollTop = el.scrollHeight;
                    });
                }
            } else {
                this.logViewer.title = 'Log Error';
                this.logViewer.content = (result && result.error) || 'Failed to load log';
            }
        } catch (e) {
            if (!this.logViewer.open) return;
            this.logViewer.title = 'Log Error';
            this.logViewer.content = e.message || 'Request failed';
        }
    },

    toggleLogFollow() {
        this.logViewer.following = !this.logViewer.following;
        if (this.logViewer.following) {
            this.fetchLogContent();
            this.logViewer.timer = setInterval(() => { this.fetchLogContent(); }, 1000);
        } else {
            this.stopLogFollow();
        }
    },

    stopLogFollow() {
        if (this.logViewer.timer) {
            clearInterval(this.logViewer.timer);
            this.logViewer.timer = null;
        }
        this.logViewer.following = false;
    },

    changeLogLineCount(count) {
        this.logViewer.lineCount = parseInt(count) || 200;
        this.fetchLogContent();
    },

    closeLogViewer() {
        this.stopLogFollow();
        this.logViewer = { open: false, title: '', content: '', entityType: '', entityId: '', following: false, lineCount: 200, timer: null, totalLines: 0 };
    },

    copyLogContent(buttonEl) {
        this.copyToClipboard(this.logViewer.content, buttonEl);
    },
};
