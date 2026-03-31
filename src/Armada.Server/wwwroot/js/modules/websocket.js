// Armada Dashboard - WebSocket connection and message handling
// This module is loaded via <script> tag and attaches to window.ArmadaModules

window.ArmadaModules = window.ArmadaModules || {};

window.ArmadaModules.websocket = {
    connectWebSocket() {
        try {
            let WS_PROTOCOL = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
            let wsUrl = WS_PROTOCOL + '//' + window.location.hostname + ':' + this.wsPort;
            console.log('Connecting WebSocket to', wsUrl);
            this.ws = new WebSocket(wsUrl);
            this.ws.onopen = () => {
                this.wsConnected = true;
                this.connected = true;
                console.log('WebSocket connected');
                this.ws.send(JSON.stringify({ Route: 'subscribe' }));
            };
            this.ws.onmessage = (evt) => {
                try { this.handleWsMessage(this.toCamel(JSON.parse(evt.data))); } catch (e) { }
            };
            this.ws.onclose = () => {
                this.wsConnected = false;
                this.connected = this.apiConnected;
                setTimeout(() => this.connectWebSocket(), 3000);
            };
            this.ws.onerror = (e) => {
                console.warn('WebSocket error:', e);
                this.wsConnected = false;
                this.connected = this.apiConnected;
            };
        } catch (e) {
            console.warn('WebSocket connection failed:', e);
            this.wsConnected = false;
            this.connected = this.apiConnected;
        }
    },

    handleWsMessage(data) {
        if (data.type === 'status.snapshot') {
            this.status = data.data || this.status;
            return;
        }
        if (data.type && (
            data.type.includes('changed') || data.type.includes('mission') ||
            data.type.includes('captain') || data.type.includes('voyage') ||
            data.type.includes('signal') || data.type.includes('merge') ||
            data.type.includes('event')
        )) {
            this.refresh();
            // Refresh current view data
            if (this.view === 'signals') this.loadSignals();
            if (this.view === 'events') this.loadEvents();
            if (this.view === 'merge-queue') this.loadMergeQueue();
            if (this.view === 'missions') this.loadMissions();
        }

        // Toast notifications for mission state changes
        if (data.type === 'mission.changed' && data.data) {
            let m = data.data;
            if (m.status) {
                this._notifyStateChange('Mission', m.id, m.title, m.status);
            }
        }

        // Toast notifications for voyage state changes
        if (data.type === 'voyage.changed' && data.data) {
            let v = data.data;
            if (v.status) {
                this._notifyStateChange('Voyage', v.id, v.title, v.status);
            }
        }

        // Captain state changes
        if (data.type === 'captain.changed' && data.data) {
            let c = data.data;
            if (c.id && c.state) {
                this._notifyStateChange('Captain', c.id, c.name || c.id, c.state);
            }
        }
    },
};
