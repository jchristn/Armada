const DEPLOYMENT_STORAGE_KEY = 'armada_proxy_instance_id';
const THEME_STORAGE_KEY = 'armada_proxy_theme';
const BUILT_IN_PIPELINES = [
  { id: 'WorkerOnly', name: 'WorkerOnly' },
  { id: 'Reviewed', name: 'Reviewed' },
  { id: 'Tested', name: 'Tested' },
  { id: 'FullPipeline', name: 'FullPipeline' },
];

const state = {
  instances: [],
  selectedInstanceId: null,
  isAuthenticated: false,
  sidebarOpen: false,
  summary: null,
  fleets: [],
  vessels: [],
  pipelines: [],
  theme: 'light',
  selectedFleetId: null,
  selectedVesselId: null,
  selectedMissionId: null,
  editingFleet: null,
  editingVessel: null,
  editingMission: null,
  detailModal: null,
};

const elements = {
  loginView: document.getElementById('loginView'),
  loginLogo: document.getElementById('loginLogo'),
  loginForm: document.getElementById('loginForm'),
  loginInstanceId: document.getElementById('loginInstanceId'),
  loginStatus: document.getElementById('loginStatus'),
  loginRefreshButton: document.getElementById('loginRefreshButton'),
  loginThemeToggleButton: document.getElementById('loginThemeToggleButton'),
  instanceCount: document.getElementById('instanceCount'),
  instanceList: document.getElementById('instanceList'),
  appView: document.getElementById('appView'),
  sidebar: document.getElementById('sidebar'),
  sidebarOverlay: document.getElementById('sidebarOverlay'),
  sidebarDeploymentId: document.getElementById('sidebarDeploymentId'),
  sidebarDeploymentState: document.getElementById('sidebarDeploymentState'),
  switchDeploymentButton: document.getElementById('switchDeploymentButton'),
  sidebarSwitchDeploymentButton: document.getElementById('sidebarSwitchDeploymentButton'),
  mobileMenuButton: document.getElementById('mobileMenuButton'),
  currentDeploymentLabel: document.getElementById('currentDeploymentLabel'),
  currentDeploymentState: document.getElementById('currentDeploymentState'),
  refreshButton: document.getElementById('refreshButton'),
  themeToggleButton: document.getElementById('themeToggleButton'),
  openDispatchButton: document.getElementById('openDispatchButton'),
  emptyState: document.getElementById('emptyState'),
  instanceWorkspace: document.getElementById('instanceWorkspace'),
  summaryTitle: document.getElementById('summaryTitle'),
  summarySubtitle: document.getElementById('summarySubtitle'),
  summaryCards: document.getElementById('summaryCards'),
  activityFeed: document.getElementById('activityFeed'),
  missionList: document.getElementById('missionList'),
  voyageList: document.getElementById('voyageList'),
  captainList: document.getElementById('captainList'),
  fleetList: document.getElementById('fleetList'),
  vesselList: document.getElementById('vesselList'),
  missionBrowseForm: document.getElementById('missionBrowseForm'),
  missionBrowseStatus: document.getElementById('missionBrowseStatus'),
  missionBrowseLimit: document.getElementById('missionBrowseLimit'),
  missionBrowseVoyageId: document.getElementById('missionBrowseVoyageId'),
  missionBrowseVesselId: document.getElementById('missionBrowseVesselId'),
  missionBrowseRecentButton: document.getElementById('missionBrowseRecentButton'),
  missionBrowseStatusText: document.getElementById('missionBrowseStatusText'),
  voyageBrowseForm: document.getElementById('voyageBrowseForm'),
  voyageBrowseStatus: document.getElementById('voyageBrowseStatus'),
  voyageBrowseLimit: document.getElementById('voyageBrowseLimit'),
  voyageBrowseRecentButton: document.getElementById('voyageBrowseRecentButton'),
  voyageBrowseStatusText: document.getElementById('voyageBrowseStatusText'),
  openFleetModalButton: document.getElementById('openFleetModalButton'),
  openVesselModalButton: document.getElementById('openVesselModalButton'),
  entityCardTemplate: document.getElementById('entityCardTemplate'),
  detailModal: document.getElementById('detailModal'),
  detailModalTitle: document.getElementById('detailModalTitle'),
  detailModalSubtitle: document.getElementById('detailModalSubtitle'),
  detailModalBody: document.getElementById('detailModalBody'),
  dispatchModal: document.getElementById('dispatchModal'),
  dispatchForm: document.getElementById('dispatchForm'),
  dispatchVesselId: document.getElementById('dispatchVesselId'),
  dispatchPipelineId: document.getElementById('dispatchPipelineId'),
  dispatchPriority: document.getElementById('dispatchPriority'),
  dispatchTitle: document.getElementById('dispatchTitle'),
  dispatchDescription: document.getElementById('dispatchDescription'),
  dispatchMissions: document.getElementById('dispatchMissions'),
  dispatchSubmitButton: document.getElementById('dispatchSubmitButton'),
  dispatchFormStatus: document.getElementById('dispatchFormStatus'),
  fleetModal: document.getElementById('fleetModal'),
  fleetModalTitle: document.getElementById('fleetModalTitle'),
  fleetModalSubtitle: document.getElementById('fleetModalSubtitle'),
  fleetForm: document.getElementById('fleetForm'),
  fleetName: document.getElementById('fleetName'),
  fleetDescription: document.getElementById('fleetDescription'),
  fleetDefaultPipelineId: document.getElementById('fleetDefaultPipelineId'),
  fleetActive: document.getElementById('fleetActive'),
  fleetResetButton: document.getElementById('fleetResetButton'),
  fleetFormStatus: document.getElementById('fleetFormStatus'),
  vesselModal: document.getElementById('vesselModal'),
  vesselModalTitle: document.getElementById('vesselModalTitle'),
  vesselModalSubtitle: document.getElementById('vesselModalSubtitle'),
  vesselForm: document.getElementById('vesselForm'),
  vesselFleetId: document.getElementById('vesselFleetId'),
  vesselName: document.getElementById('vesselName'),
  vesselRepoUrl: document.getElementById('vesselRepoUrl'),
  vesselWorkingDirectory: document.getElementById('vesselWorkingDirectory'),
  vesselDefaultBranch: document.getElementById('vesselDefaultBranch'),
  vesselDefaultPipelineId: document.getElementById('vesselDefaultPipelineId'),
  vesselAllowConcurrentMissions: document.getElementById('vesselAllowConcurrentMissions'),
  vesselActive: document.getElementById('vesselActive'),
  vesselResetButton: document.getElementById('vesselResetButton'),
  vesselFormStatus: document.getElementById('vesselFormStatus'),
  missionModal: document.getElementById('missionModal'),
  missionModalTitle: document.getElementById('missionModalTitle'),
  missionModalSubtitle: document.getElementById('missionModalSubtitle'),
  missionForm: document.getElementById('missionForm'),
  missionTitle: document.getElementById('missionTitle'),
  missionDescription: document.getElementById('missionDescription'),
  missionVesselId: document.getElementById('missionVesselId'),
  missionVoyageId: document.getElementById('missionVoyageId'),
  missionPersona: document.getElementById('missionPersona'),
  missionPriority: document.getElementById('missionPriority'),
  missionResetButton: document.getElementById('missionResetButton'),
  missionFormStatus: document.getElementById('missionFormStatus'),
};

function escapeHtml(text) {
  return String(text ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;');
}

function formatTimestamp(value) {
  if (!value) return '-';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '-';
  return date.toLocaleString();
}

function badgeClass(value) {
  return String(value || '').toLowerCase().replace(/[^a-z0-9]+/g, '');
}

function renderBadge(text) {
  const value = String(text || 'unknown');
  return `<span class="badge ${badgeClass(value)}">${escapeHtml(value)}</span>`;
}

function renderStatusPill(label, value, extraClass = '') {
  const className = badgeClass(value) || 'unknown';
  return `
    <span class="summary-meta-item ${extraClass}">
      <span class="summary-meta-label">${escapeHtml(label)}</span>
      <span class="summary-meta-value ${extraClass ? `summary-meta-value-${extraClass}` : ''} ${className}">${escapeHtml(value || '-')}</span>
    </span>
  `;
}

function setFormStatus(element, message, kind) {
  if (!element) return;
  element.textContent = message || '';
  element.className = 'form-status';
  if (kind) element.classList.add(kind);
}

async function fetchJson(url, options = {}) {
  const request = {
    cache: 'no-store',
    ...options,
    headers: {
      ...(options.headers || {}),
    },
  };

  if (request.body !== undefined && typeof request.body !== 'string') {
    request.headers['Content-Type'] = 'application/json';
    request.body = JSON.stringify(request.body);
  }

  const response = await fetch(url, request);
  const text = await response.text();
  let body = {};

  if (text) {
    try {
      body = JSON.parse(text);
    } catch {
      body = { error: text };
    }
  }

  if (!response.ok) {
    throw new Error(body.error || body.message || `Request failed: ${response.status}`);
  }

  return body;
}

function getStoredDeploymentId() {
  try {
    return localStorage.getItem(DEPLOYMENT_STORAGE_KEY);
  } catch {
    return null;
  }
}

function storeDeploymentId(instanceId) {
  try {
    if (instanceId) {
      localStorage.setItem(DEPLOYMENT_STORAGE_KEY, instanceId);
    } else {
      localStorage.removeItem(DEPLOYMENT_STORAGE_KEY);
    }
  } catch {
  }
}

function getStoredTheme() {
  try {
    return localStorage.getItem(THEME_STORAGE_KEY);
  } catch {
    return null;
  }
}

function storeTheme(theme) {
  try {
    localStorage.setItem(THEME_STORAGE_KEY, theme);
  } catch {
  }
}

function getPreferredTheme() {
  const stored = getStoredTheme();
  if (stored === 'light' || stored === 'dark') return stored;
  if (window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches) return 'dark';
  return 'light';
}

function syncThemeButtons() {
  const nextLabel = state.theme === 'dark' ? 'Light' : 'Dark';
  if (elements.themeToggleButton) elements.themeToggleButton.textContent = nextLabel;
  if (elements.loginThemeToggleButton) elements.loginThemeToggleButton.textContent = nextLabel;
}

function syncThemeLogos() {
  if (elements.loginLogo) {
    elements.loginLogo.src = state.theme === 'dark'
      ? '/img/logo-light-grey.png'
      : '/img/logo-dark-grey.png';
  }
}

function applyTheme(theme) {
  state.theme = theme === 'dark' ? 'dark' : 'light';
  document.documentElement.setAttribute('data-theme', state.theme);
  syncThemeButtons();
  syncThemeLogos();
}

function toggleTheme() {
  const nextTheme = state.theme === 'dark' ? 'light' : 'dark';
  applyTheme(nextTheme);
  storeTheme(nextTheme);
}

function getInstanceIdValue(instance) {
  return instance?.instanceId || instance?.InstanceId || '';
}

function getInstanceStateValue(instance) {
  return instance?.state || instance?.State || '';
}

function getInstanceArmadaVersionValue(instance) {
  return instance?.armadaVersion || instance?.ArmadaVersion || '';
}

function getInstanceProtocolVersionValue(instance) {
  return instance?.protocolVersion || instance?.ProtocolVersion || '';
}

function getInstanceLastErrorValue(instance) {
  return instance?.lastError || instance?.LastError || '';
}

function getInstanceRemoteAddressValue(instance) {
  return instance?.remoteAddress || instance?.RemoteAddress || '';
}

function getInstanceById(instanceId) {
  const normalized = String(instanceId || '').trim().toLowerCase();
  return state.instances.find((instance) => getInstanceIdValue(instance).toLowerCase() === normalized) || null;
}

function setLoginStatus(message, kind) {
  setFormStatus(elements.loginStatus, message, kind);
}

function setDeploymentChrome() {
  const instance = getInstanceById(state.selectedInstanceId);
  const summaryHealth = state.summary?.health || {};
  const tunnel = summaryHealth.remoteTunnel || {};
  const statusValue = tunnel.state || getInstanceStateValue(instance) || 'Offline';

  elements.currentDeploymentLabel.textContent = state.selectedInstanceId || '-';
  elements.currentDeploymentState.textContent = String(statusValue);
  elements.currentDeploymentState.className = `tag ${badgeClass(statusValue) || 'idle'}`;

  elements.sidebarDeploymentId.textContent = state.selectedInstanceId || '-';
  elements.sidebarDeploymentState.textContent = String(statusValue);
  elements.sidebarDeploymentState.className = `tag ${badgeClass(statusValue) || 'idle'}`;
}

function closeSidebar() {
  state.sidebarOpen = false;
  elements.sidebar.classList.remove('sidebar-open');
  elements.sidebarOverlay.classList.add('hidden');
}

function openSidebar() {
  state.sidebarOpen = true;
  elements.sidebar.classList.add('sidebar-open');
  elements.sidebarOverlay.classList.remove('hidden');
}

function renderSessionState() {
  const authenticated = state.isAuthenticated && Boolean(state.selectedInstanceId);
  elements.loginView.classList.toggle('hidden', authenticated);
  elements.appView.classList.toggle('hidden', !authenticated);
  if (authenticated) setDeploymentChrome();
  else closeSidebar();
}

function isAnyModalOpen() {
  return document.querySelector('.modal-shell:not(.hidden)') !== null;
}

function openModal(element) {
  if (!element) return;
  element.classList.remove('hidden');
  document.body.classList.add('modal-open');
  closeSidebar();
}

function closeModal(element) {
  if (!element) return;
  element.classList.add('hidden');
  if (!isAnyModalOpen()) document.body.classList.remove('modal-open');
}

function closeModalById(id) {
  if (!id) return;
  const element = document.getElementById(id);
  if (!element) return;
  closeModal(element);
  if (id === 'detailModal') state.detailModal = null;
}

function closeAllModals() {
  document.querySelectorAll('.modal-shell').forEach((modal) => modal.classList.add('hidden'));
  document.body.classList.remove('modal-open');
  state.detailModal = null;
}

function buildOptionMarkup(rows, placeholder, getValue, getLabel) {
  const options = [`<option value="">${escapeHtml(placeholder)}</option>`];
  for (const row of rows) {
    const value = String(getValue(row) ?? '');
    const label = String(getLabel(row) ?? value);
    options.push(`<option value="${escapeHtml(value)}">${escapeHtml(label)}</option>`);
  }
  return options.join('');
}

function populateSelect(select, rows, placeholder, getValue, getLabel, selectedValue = '') {
  if (!select) return;
  select.innerHTML = buildOptionMarkup(rows, placeholder, getValue, getLabel);
  select.value = selectedValue || '';
}

function vesselOptionLabel(vessel) {
  const name = vessel?.name || vessel?.id || 'Unnamed vessel';
  const id = vessel?.id ? ` | ${vessel.id}` : '';
  return `${name}${id}`;
}

function pipelineOptionLabel(pipeline) {
  const name = getPipelineNameValue(pipeline) || 'Unnamed pipeline';
  const id = getPipelineIdValue(pipeline);
  return id && id !== name ? `${name} | ${id}` : name;
}

function fleetOptionLabel(fleet) {
  const name = fleet?.name || fleet?.id || 'Unnamed fleet';
  const id = fleet?.id ? ` | ${fleet.id}` : '';
  return `${name}${id}`;
}

function getPipelineIdValue(pipeline) {
  return pipeline?.id || pipeline?.Id || pipeline?.name || pipeline?.Name || '';
}

function getPipelineNameValue(pipeline) {
  return pipeline?.name || pipeline?.Name || pipeline?.id || pipeline?.Id || '';
}

function buildPipelineOptionRows() {
  const rows = [];
  const seenIds = new Set();
  const seenNames = new Set();

  const addPipeline = (pipeline) => {
    const id = String(getPipelineIdValue(pipeline) || '').trim();
    const name = String(getPipelineNameValue(pipeline) || '').trim();
    if (!id && !name) return;

    const normalizedId = id.toLowerCase();
    const normalizedName = name.toLowerCase();
    if ((normalizedId && seenIds.has(normalizedId)) || (normalizedName && seenNames.has(normalizedName))) return;

    rows.push({
      id: id || name,
      name: name || id,
    });

    if (normalizedId) seenIds.add(normalizedId);
    if (normalizedName) seenNames.add(normalizedName);
  };

  for (const pipeline of state.pipelines || []) addPipeline(pipeline);
  for (const pipeline of BUILT_IN_PIPELINES) addPipeline(pipeline);
  for (const row of [...(state.fleets || []), ...(state.vessels || [])]) {
    if (row?.defaultPipelineId) addPipeline({ id: row.defaultPipelineId, name: row.defaultPipelineId });
  }
  for (const selectedValue of [
    elements.dispatchPipelineId?.value,
    elements.fleetDefaultPipelineId?.value,
    elements.vesselDefaultPipelineId?.value,
  ]) {
    if (selectedValue) addPipeline({ id: selectedValue, name: selectedValue });
  }

  return rows.sort((left, right) => pipelineOptionLabel(left).localeCompare(pipelineOptionLabel(right)));
}

function populateFormSelects() {
  const pipelineRows = buildPipelineOptionRows();
  populateSelect(elements.dispatchVesselId, state.vessels, 'Select a vessel', (row) => row.id, vesselOptionLabel, elements.dispatchVesselId.value);
  populateSelect(elements.dispatchPipelineId, pipelineRows, 'Use default pipeline', getPipelineIdValue, pipelineOptionLabel, elements.dispatchPipelineId.value);
  populateSelect(elements.fleetDefaultPipelineId, pipelineRows, 'No default pipeline', getPipelineIdValue, pipelineOptionLabel, elements.fleetDefaultPipelineId.value);
  populateSelect(elements.vesselFleetId, state.fleets, 'No fleet', (row) => row.id, fleetOptionLabel, elements.vesselFleetId.value);
  populateSelect(elements.vesselDefaultPipelineId, pipelineRows, 'No default pipeline', getPipelineIdValue, pipelineOptionLabel, elements.vesselDefaultPipelineId.value);
  populateSelect(elements.missionVesselId, state.vessels, 'Select a vessel', (row) => row.id, vesselOptionLabel, elements.missionVesselId.value);
}

function resetDispatchForm() {
  elements.dispatchForm.reset();
  populateFormSelects();
  elements.dispatchPriority.value = '100';
  setButtonBusy(elements.dispatchSubmitButton, false, 'Dispatch', 'Dispatching...');
  setFormStatus(elements.dispatchFormStatus, '', null);
}

function populateFleetForm(fleet) {
  state.selectedFleetId = fleet?.id || null;
  state.editingFleet = fleet || null;
  elements.fleetName.value = fleet?.name || '';
  elements.fleetDescription.value = fleet?.description || '';
  populateFormSelects();
  elements.fleetDefaultPipelineId.value = fleet?.defaultPipelineId || '';
  elements.fleetActive.checked = fleet?.active !== false;
  setFormStatus(elements.fleetFormStatus, '', null);
}

function resetFleetForm() {
  if (state.editingFleet && state.selectedFleetId) {
    populateFleetForm(state.editingFleet);
    return;
  }

  state.selectedFleetId = null;
  state.editingFleet = null;
  elements.fleetForm.reset();
  populateFormSelects();
  elements.fleetActive.checked = true;
  setFormStatus(elements.fleetFormStatus, '', null);
}

function openFleetModal(fleet = null) {
  populateFleetForm(fleet);
  elements.fleetModalTitle.textContent = fleet ? 'Edit Fleet' : 'Add Fleet';
  elements.fleetModalSubtitle.textContent = fleet?.id || 'Create a new fleet.';
  openModal(elements.fleetModal);
}

function populateVesselForm(vessel) {
  state.selectedVesselId = vessel?.id || null;
  state.editingVessel = vessel || null;
  elements.vesselName.value = vessel?.name || '';
  elements.vesselRepoUrl.value = vessel?.repoUrl || '';
  elements.vesselWorkingDirectory.value = vessel?.workingDirectory || '';
  elements.vesselDefaultBranch.value = vessel?.defaultBranch || 'main';
  populateFormSelects();
  elements.vesselFleetId.value = vessel?.fleetId || '';
  elements.vesselDefaultPipelineId.value = vessel?.defaultPipelineId || '';
  elements.vesselAllowConcurrentMissions.checked = Boolean(vessel?.allowConcurrentMissions);
  elements.vesselActive.checked = vessel?.active !== false;
  setFormStatus(elements.vesselFormStatus, '', null);
}

function resetVesselForm() {
  if (state.editingVessel && state.selectedVesselId) {
    populateVesselForm(state.editingVessel);
    return;
  }

  state.selectedVesselId = null;
  state.editingVessel = null;
  elements.vesselForm.reset();
  populateFormSelects();
  elements.vesselDefaultBranch.value = 'main';
  elements.vesselAllowConcurrentMissions.checked = false;
  elements.vesselActive.checked = true;
  setFormStatus(elements.vesselFormStatus, '', null);
}

function openVesselModal(vessel = null) {
  populateVesselForm(vessel);
  elements.vesselModalTitle.textContent = vessel ? 'Edit Vessel' : 'Add Vessel';
  elements.vesselModalSubtitle.textContent = vessel?.id || 'Register a repository for remote work.';
  openModal(elements.vesselModal);
}

function setButtonBusy(button, isBusy, idleLabel, busyLabel) {
  if (!button) return;
  button.disabled = Boolean(isBusy);
  button.textContent = isBusy ? busyLabel : idleLabel;
}

function populateMissionForm(mission) {
  state.selectedMissionId = mission?.id || null;
  state.editingMission = mission || null;
  elements.missionTitle.value = mission?.title || '';
  elements.missionDescription.value = mission?.description || '';
  populateFormSelects();
  elements.missionVesselId.value = mission?.vesselId || '';
  elements.missionVoyageId.value = mission?.voyageId || '';
  elements.missionPersona.value = mission?.persona || '';
  elements.missionPriority.value = mission?.priority != null ? String(mission.priority) : '100';
  setFormStatus(elements.missionFormStatus, '', null);
}

function resetMissionForm() {
  if (state.editingMission && state.selectedMissionId) {
    populateMissionForm(state.editingMission);
    return;
  }

  state.selectedMissionId = null;
  state.editingMission = null;
  elements.missionForm.reset();
  populateFormSelects();
  elements.missionPriority.value = '100';
  setFormStatus(elements.missionFormStatus, '', null);
}

function openMissionModal(mission) {
  populateMissionForm(mission);
  elements.missionModalTitle.textContent = 'Edit Mission';
  elements.missionModalSubtitle.textContent = mission?.id || 'Update mission details.';
  openModal(elements.missionModal);
}

function resetProxyState() {
  state.summary = null;
  state.fleets = [];
  state.vessels = [];
  state.pipelines = [];
  state.selectedFleetId = null;
  state.selectedVesselId = null;
  state.selectedMissionId = null;
  state.editingFleet = null;
  state.editingVessel = null;
  state.editingMission = null;
  closeAllModals();
  resetFleetForm();
  resetVesselForm();
  resetDispatchForm();
  resetMissionForm();
}

async function authenticateInstance(instanceId) {
  const normalized = String(instanceId || '').trim();
  if (!normalized) {
    setLoginStatus('Deployment ID is required.', 'error');
    return;
  }

  let instance = getInstanceById(normalized);
  if (!instance) {
    try {
      await loadInstances();
    } catch {
    }
    instance = getInstanceById(normalized);
  }

  if (!instance) {
    setLoginStatus(`No Armada deployment with identifier "${normalized}" is connected to this proxy.`, 'error');
    return;
  }

  const resolvedInstanceId = getInstanceIdValue(instance);
  state.selectedInstanceId = resolvedInstanceId;
  state.isAuthenticated = true;
  storeDeploymentId(resolvedInstanceId);
  elements.loginInstanceId.value = resolvedInstanceId;
  setLoginStatus('', null);
  resetProxyState();
  renderSessionState();
  await loadSelectedInstance();
}

function logoutToLogin(message = '', prefill = '') {
  state.isAuthenticated = false;
  state.selectedInstanceId = null;
  storeDeploymentId(null);
  resetProxyState();
  renderSessionState();
  elements.loginInstanceId.value = prefill || '';
  if (message) setLoginStatus(message, 'error');
  else setLoginStatus('', null);
}

function instanceBaseUrl() {
  return `/api/v1/instances/${encodeURIComponent(state.selectedInstanceId)}`;
}

function buildQuery(params) {
  const query = new URLSearchParams();
  Object.entries(params || {}).forEach(([key, value]) => {
    if (value === null || value === undefined) return;
    if (String(value).trim() === '') return;
    query.set(key, String(value).trim());
  });
  const serialized = query.toString();
  return serialized ? `?${serialized}` : '';
}

async function loadInstances() {
  const data = await fetchJson('/api/v1/instances');
  state.instances = data.instances || data.Instances || [];
  elements.instanceCount.textContent = String(data.count || data.Count || state.instances.length || 0);
  renderInstanceList();

  if (state.isAuthenticated && state.selectedInstanceId) {
    if (!getInstanceById(state.selectedInstanceId)) {
      const missingId = state.selectedInstanceId;
      logoutToLogin(`Deployment "${missingId}" is no longer registered with this proxy.`, missingId);
      return;
    }
    await loadSelectedInstance();
  } else {
    renderSessionState();
  }
}

async function loadSelectedInstance() {
  if (!state.selectedInstanceId) return;

  try {
    const base = instanceBaseUrl();
    const [summary, fleets, vessels, pipelines] = await Promise.all([
      fetchJson(`${base}/summary`),
      fetchJson(`${base}/fleets?limit=24`),
      fetchJson(`${base}/vessels?limit=48`),
      fetchJson(`${base}/pipelines?limit=48`).catch(() => ({ pipelines: [] })),
    ]);

    state.summary = summary;
    state.fleets = fleets.fleets || [];
    state.vessels = vessels.vessels || [];
    state.pipelines = pipelines.pipelines || [];
    populateFormSelects();
    renderSessionState();
    renderSelectedInstance();
  } catch (error) {
    state.summary = null;
    renderSessionState();
    elements.emptyState.classList.remove('hidden');
    elements.instanceWorkspace.classList.add('hidden');
    elements.emptyState.innerHTML = `
      <h2>Deployment Unavailable</h2>
      <p>${escapeHtml(error instanceof Error ? error.message : 'Unable to load deployment summary through the proxy.')}</p>
    `;
    throw error;
  }
}

function renderInstanceList() {
  elements.instanceList.innerHTML = '';
  if (state.instances.length === 0) return;

  for (const instance of state.instances) {
    const instanceId = getInstanceIdValue(instance);
    const instanceState = getInstanceStateValue(instance);
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'deployment-card';
    button.innerHTML = `
      <div class="entity-card-top">
        <span class="entity-title">${escapeHtml(instanceId)}</span>
        ${renderBadge(instanceState)}
      </div>
      <div class="entity-meta">${escapeHtml(getInstanceArmadaVersionValue(instance) || 'unknown version')} | ${escapeHtml(getInstanceProtocolVersionValue(instance) || 'unknown protocol')}</div>
      <div class="entity-meta-secondary">${escapeHtml(getInstanceLastErrorValue(instance) || getInstanceRemoteAddressValue(instance) || 'No current error')}</div>
    `;

    button.addEventListener('click', async () => {
      elements.loginInstanceId.value = instanceId;
      await authenticateInstance(instanceId);
    });

    if (state.selectedInstanceId === instanceId) {
      button.classList.add('is-selected');
    }

    elements.instanceList.appendChild(button);
  }
}

function renderSummaryMeta(health, generatedUtc) {
  return `
    ${renderStatusPill('Version', health.version || 'unknown version')}
    ${renderStatusPill('Tunnel', health.remoteTunnel?.state || 'unknown', 'state')}
    ${renderStatusPill('Generated', formatTimestamp(generatedUtc))}
  `;
}

function makeSummaryCard(label, valueHtml, detailHtml = '', extraClass = '') {
  return `
    <article class="summary-card${extraClass ? ` ${extraClass}` : ''}">
      <div class="summary-label">${escapeHtml(label)}</div>
      <div class="summary-value">${valueHtml}</div>
      ${detailHtml ? `<div class="summary-detail">${detailHtml}</div>` : ''}
    </article>
  `;
}

function renderMissionStateMarkup(states) {
  const entries = Object.entries(states || {})
    .sort((left, right) => Number(right[1]) - Number(left[1]));

  if (entries.length === 0) {
    return '<div class="text-muted">No recent mission states.</div>';
  }

  return `
    <div class="state-chip-grid">
      ${entries.map(([key, value]) => `
        <div class="state-chip ${badgeClass(key)}">
          <span class="state-chip-label">${escapeHtml(key)}</span>
          <span class="state-chip-value">${escapeHtml(String(value))}</span>
        </div>
      `).join('')}
    </div>
  `;
}

function renderSelectedInstance() {
  if (!state.selectedInstanceId || !state.summary) {
    elements.emptyState.classList.remove('hidden');
    elements.instanceWorkspace.classList.add('hidden');
    setDeploymentChrome();
    return;
  }

  const summary = state.summary;
  const health = summary.health || {};
  const status = summary.status || {};

  elements.emptyState.classList.add('hidden');
  elements.instanceWorkspace.classList.remove('hidden');
  elements.summaryTitle.textContent = state.selectedInstanceId;
  elements.summarySubtitle.innerHTML = renderSummaryMeta(health, summary.generatedUtc);
  setDeploymentChrome();

  const tunnelState = health.remoteTunnel?.state || 'unknown';
  const latencyValue = health.remoteTunnel?.latencyMs != null ? `${health.remoteTunnel.latencyMs} ms` : '-';
  const activeVoyages = String(status.activeVoyages ?? 0);
  const workingCaptains = String(status.workingCaptains ?? 0);

  elements.summaryCards.innerHTML = [
    makeSummaryCard('Tunnel', renderBadge(tunnelState), escapeHtml(health.remoteTunnel?.tunnelUrl || 'No tunnel URL configured')),
    makeSummaryCard('Latency', escapeHtml(latencyValue), escapeHtml(`Last heartbeat ${formatTimestamp(health.remoteTunnel?.lastHeartbeatUtc)}`)),
    makeSummaryCard('Active Voyages', escapeHtml(activeVoyages), escapeHtml(`Working captains ${workingCaptains}`)),
    makeSummaryCard('Mission States', renderMissionStateMarkup(status.missionsByStatus || {}), '', 'summary-card-wide'),
  ].join('');

  renderActivity(summary.recentActivity || []);
  loadRecentMissionList();
  loadRecentVoyageList();
  renderEntityList(elements.captainList, summary.recentCaptains || [], 'captain');
  renderEntityList(elements.fleetList, state.fleets || [], 'fleet');
  renderEntityList(elements.vesselList, state.vessels || [], 'vessel');
}

function loadRecentMissionList() {
  const rows = state.summary?.recentMissions || [];
  renderEntityList(elements.missionList, rows, 'mission');
  setFormStatus(elements.missionBrowseStatusText, `Showing ${rows.length} recent mission${rows.length === 1 ? '' : 's'}.`, null);
}

function loadRecentVoyageList() {
  const rows = state.summary?.recentVoyages || [];
  renderEntityList(elements.voyageList, rows, 'voyage');
  setFormStatus(elements.voyageBrowseStatusText, `Showing ${rows.length} recent voyage${rows.length === 1 ? '' : 's'}.`, null);
}

function renderActivity(activity) {
  elements.activityFeed.innerHTML = '';
  if (activity.length === 0) {
    elements.activityFeed.innerHTML = '<div class="text-muted">No recent activity available.</div>';
    return;
  }

  for (const item of activity) {
    const node = document.createElement('div');
    node.className = 'feed-item';
    node.innerHTML = `
      <div class="feed-type">${escapeHtml(item.eventType || 'event')}</div>
      <div class="feed-message">${escapeHtml(item.message || 'No message')}</div>
      <div class="feed-meta">${formatTimestamp(item.createdUtc)} | ${escapeHtml(item.entityType || 'system')} ${escapeHtml(item.entityId || '')}</div>
    `;
    elements.activityFeed.appendChild(node);
  }
}

function buildEntityMeta(kind, row) {
  if (kind === 'mission') {
    return {
      meta: `${row.persona || 'Worker'} | ${row.id || '-'}`,
      secondary: `Priority ${row.priority ?? 100} | Updated ${formatTimestamp(row.lastUpdateUtc)}`,
    };
  }

  if (kind === 'captain') {
    return {
      meta: `${row.runtime || 'runtime'} | ${row.id || '-'}`,
      secondary: `Heartbeat ${formatTimestamp(row.lastHeartbeatUtc || row.lastUpdateUtc)}`,
    };
  }

  if (kind === 'voyage') {
    return {
      meta: row.id || '-',
      secondary: `Updated ${formatTimestamp(row.lastUpdateUtc)}`,
    };
  }

  if (kind === 'fleet') {
    return {
      meta: row.id || '-',
      secondary: row.description || 'No description',
    };
  }

  if (kind === 'vessel') {
    return {
      meta: row.repoUrl || row.id || '-',
      secondary: row.workingDirectory || 'No working directory',
    };
  }

  return {
    meta: row.id || '-',
    secondary: '',
  };
}

async function handleEntitySelection(kind, row) {
  if (!row || !row.id) return;

  if (kind === 'mission') {
    await openMissionDetailModal(row.id);
    return;
  }

  if (kind === 'voyage') {
    await openVoyageDetailModal(row.id);
    return;
  }

  if (kind === 'captain') {
    await openCaptainDetailModal(row.id);
    return;
  }

  if (kind === 'fleet') {
    openFleetModal(row);
    return;
  }

  if (kind === 'vessel') {
    openVesselModal(row);
  }
}

function renderEntityList(container, rows, kind) {
  container.innerHTML = '';
  if (!rows || rows.length === 0) {
    container.innerHTML = '<div class="text-muted">Nothing recent to show.</div>';
    return;
  }

  for (const row of rows) {
    const fragment = elements.entityCardTemplate.content.cloneNode(true);
    const card = fragment.querySelector('.entity-card');
    const title = fragment.querySelector('.entity-title');
    const badge = fragment.querySelector('.badge');
    const meta = fragment.querySelector('.entity-meta');
    const secondary = fragment.querySelector('.entity-meta-secondary');
    const titleValue = row.title || row.name || row.id;
    const badgeValue = row.status || row.state || row.persona || 'detail';
    const description = buildEntityMeta(kind, row);

    title.textContent = titleValue;
    badge.textContent = String(badgeValue);
    badge.classList.add(badgeClass(badgeValue));
    meta.textContent = description.meta;
    secondary.textContent = description.secondary;

    card.addEventListener('click', async () => {
      await handleEntitySelection(kind, row);
    });

    container.appendChild(fragment);
  }
}

async function loadDetailPayload(kind, id) {
  const base = instanceBaseUrl();
  if (kind === 'mission') return fetchJson(`${base}/missions/${encodeURIComponent(id)}`);
  if (kind === 'voyage') return fetchJson(`${base}/voyages/${encodeURIComponent(id)}`);
  if (kind === 'captain') return fetchJson(`${base}/captains/${encodeURIComponent(id)}`);
  throw new Error(`Unsupported detail type: ${kind}`);
}

async function loadMissionLog(id) {
  const base = instanceBaseUrl();
  return fetchJson(`${base}/missions/${encodeURIComponent(id)}/log?lines=240&offset=0`);
}

async function loadMissionDiff(id) {
  const base = instanceBaseUrl();
  return fetchJson(`${base}/missions/${encodeURIComponent(id)}/diff`);
}

function renderKeyValueCard(title, rows, extraClass = '') {
  const safeRows = rows && rows.length > 0 ? rows : [['Value', '-']];
  return `
    <section class="detail-card${extraClass ? ` ${extraClass}` : ''}">
      <h3>${escapeHtml(title)}</h3>
      ${safeRows.map(([key, value]) => `
        <div class="detail-row">
          <span class="detail-key">${escapeHtml(String(key))}</span>
          <span class="detail-value mono">${escapeHtml(String(value ?? '-'))}</span>
        </div>
      `).join('')}
    </section>
  `;
}

function renderMissionListCard(title, rows) {
  return `
    <section class="detail-card">
      <h3>${escapeHtml(title)}</h3>
      <div class="detail-list">
        ${rows.length === 0 ? '<div class="text-muted">Nothing to show.</div>' : rows.map((mission) => `
          <button type="button" class="detail-list-item" data-detail-action="open-mission" data-id="${escapeHtml(mission.id || '')}">
            <span class="detail-list-copy">
              <span class="detail-list-title">${escapeHtml(mission.title || mission.id || 'Mission')}</span>
              <span class="detail-list-meta mono">${escapeHtml(mission.id || '')}</span>
            </span>
            ${renderBadge(mission.status || 'unknown')}
          </button>
        `).join('')}
      </div>
    </section>
  `;
}

function setDetailModalFrame(title, subtitleHtml, bodyHtml) {
  elements.detailModalTitle.textContent = title;
  elements.detailModalSubtitle.innerHTML = subtitleHtml || '';
  elements.detailModalBody.innerHTML = bodyHtml;
}

function renderMissionDetailModal() {
  const modal = state.detailModal;
  const payload = modal?.payload || {};
  const mission = payload.mission || {};
  const captain = payload.captain || {};
  const voyage = payload.voyage || {};
  const vessel = payload.vessel || {};
  const dock = payload.dock || {};
  const activeTab = modal?.tab || 'overview';
  const log = modal?.log || '';
  const diff = modal?.diff || '';
  const logMeta = modal?.logMeta || {};

  let tabMarkup = '';
  if (activeTab === 'overview') {
    tabMarkup = `
      <div class="detail-grid">
        ${renderKeyValueCard('Mission', [
          ['Status', mission.status],
          ['Persona', mission.persona || 'Worker'],
          ['Priority', mission.priority ?? 100],
          ['Branch', mission.branchName || '-'],
          ['Runtime', mission.totalRuntimeMs != null ? `${mission.totalRuntimeMs} ms` : '-'],
          ['Created', formatTimestamp(mission.createdUtc)],
          ['Updated', formatTimestamp(mission.lastUpdateUtc)],
        ])}
        ${renderKeyValueCard('Routing', [
          ['Captain', captain.name || mission.captainId || '-'],
          ['Voyage', voyage.title || mission.voyageId || '-'],
          ['Vessel', vessel.name || mission.vesselId || '-'],
          ['Dock', dock.id || mission.dockId || '-'],
          ['Path', dock.worktreePath || '-'],
          ['Failure', mission.failureReason || '-'],
        ])}
      </div>
    `;
  } else if (activeTab === 'instructions') {
    tabMarkup = `
      <section class="detail-card detail-card-full">
        <h3>Instructions</h3>
        <div class="detail-prose">${escapeHtml(mission.description || 'No instructions provided.')}</div>
      </section>
    `;
  } else if (activeTab === 'log') {
    tabMarkup = `
      <div class="detail-actions detail-actions-inline">
        <button type="button" class="button" data-detail-action="mission-log-refresh" data-id="${escapeHtml(mission.id || '')}">Reload Log</button>
        <span class="text-muted">Showing ${escapeHtml(String(logMeta.totalLines ?? logMeta.lines ?? 0))} log lines</span>
      </div>
      <pre class="code-view">${escapeHtml(log || 'No log content available.')}</pre>
    `;
  } else if (activeTab === 'diff') {
    tabMarkup = `
      <div class="detail-actions detail-actions-inline">
        <button type="button" class="button" data-detail-action="mission-diff-refresh" data-id="${escapeHtml(mission.id || '')}">Reload Diff</button>
      </div>
      <pre class="code-view">${escapeHtml(diff || 'No diff content available.')}</pre>
    `;
  }

  setDetailModalFrame(
    mission.title || mission.id || 'Mission Detail',
    `${renderBadge(mission.status || 'unknown')} <span class="mono">${escapeHtml(mission.id || '')}</span>`,
    `
      <div class="detail-body">
        <div class="detail-actions">
          <button type="button" class="button" data-detail-action="mission-edit" data-id="${escapeHtml(mission.id || '')}">Edit</button>
          <button type="button" class="button" data-detail-action="mission-restart" data-id="${escapeHtml(mission.id || '')}">Restart</button>
          <button type="button" class="button" data-detail-action="mission-cancel" data-id="${escapeHtml(mission.id || '')}">Cancel</button>
        </div>
        <div class="tab-strip">
          <button type="button" class="tab-button${activeTab === 'overview' ? ' active' : ''}" data-detail-tab="overview">Overview</button>
          <button type="button" class="tab-button${activeTab === 'instructions' ? ' active' : ''}" data-detail-tab="instructions">Instructions</button>
          <button type="button" class="tab-button${activeTab === 'log' ? ' active' : ''}" data-detail-tab="log">Log</button>
          <button type="button" class="tab-button${activeTab === 'diff' ? ' active' : ''}" data-detail-tab="diff">Diff</button>
        </div>
        ${tabMarkup}
      </div>
    `,
  );
}

function renderVoyageDetailModal() {
  const payload = state.detailModal?.payload || {};
  const voyage = payload.voyage || {};
  const missions = payload.missions || [];

  setDetailModalFrame(
    voyage.title || voyage.id || 'Voyage Detail',
    `${renderBadge(voyage.status || 'unknown')} <span class="mono">${escapeHtml(voyage.id || '')}</span>`,
    `
      <div class="detail-body">
        <div class="detail-actions">
          <button type="button" class="button" data-detail-action="voyage-cancel" data-id="${escapeHtml(voyage.id || '')}">Cancel Voyage</button>
        </div>
        <div class="detail-grid">
          ${renderKeyValueCard('Voyage', [
            ['Status', voyage.status],
            ['Created', formatTimestamp(voyage.createdUtc)],
            ['Updated', formatTimestamp(voyage.lastUpdateUtc)],
            ['Completed', formatTimestamp(voyage.completedUtc)],
            ['Missions', missions.length],
          ])}
          ${renderKeyValueCard('Description', [['Summary', voyage.description || '-']])}
        </div>
        ${renderMissionListCard('Mission Chain', missions)}
      </div>
    `,
  );
}

function renderCaptainDetailModal() {
  const payload = state.detailModal?.payload || {};
  const captain = payload.captain || {};
  const currentMission = payload.currentMission || {};
  const currentDock = payload.currentDock || {};
  const recentMissions = payload.recentMissions || [];

  setDetailModalFrame(
    captain.name || captain.id || 'Captain Detail',
    `${renderBadge(captain.state || 'unknown')} <span class="mono">${escapeHtml(captain.id || '')}</span>`,
    `
      <div class="detail-body">
        <div class="detail-actions">
          <button type="button" class="button" data-detail-action="captain-log" data-id="${escapeHtml(captain.id || '')}">Load Captain Log</button>
          <button type="button" class="button" data-detail-action="captain-stop" data-id="${escapeHtml(captain.id || '')}">Stop Captain</button>
        </div>
        <div class="detail-grid">
          ${renderKeyValueCard('Captain', [
            ['State', captain.state],
            ['Runtime', captain.runtime || '-'],
            ['Model', captain.model || 'auto'],
            ['Heartbeat', formatTimestamp(captain.lastHeartbeatUtc)],
            ['Current Mission', currentMission.title || captain.currentMissionId || '-'],
            ['Current Dock', currentDock.id || captain.currentDockId || '-'],
          ])}
          ${renderMissionListCard('Recent Work', recentMissions.slice(0, 8))}
        </div>
        <pre class="code-view">${escapeHtml(state.detailModal?.log || 'Select "Load Captain Log" to inspect the active captain session.')}</pre>
      </div>
    `,
  );
}

function renderDetailModal() {
  if (!state.detailModal) {
    setDetailModalFrame('Detail', '', '<div class="detail-empty">No detail selected.</div>');
    return;
  }

  if (state.detailModal.loading) {
    setDetailModalFrame(
      state.detailModal.title || 'Loading detail',
      state.detailModal.subtitle || '',
      '<div class="detail-empty">Loading detail from the deployment...</div>',
    );
    return;
  }

  if (state.detailModal.type === 'mission') {
    renderMissionDetailModal();
    return;
  }

  if (state.detailModal.type === 'voyage') {
    renderVoyageDetailModal();
    return;
  }

  if (state.detailModal.type === 'captain') {
    renderCaptainDetailModal();
    return;
  }

  setDetailModalFrame('Detail', '', '<div class="detail-empty">Unsupported detail type.</div>');
}

function showDetailLoading(type, title, subtitle = '') {
  state.detailModal = { type, title, subtitle, loading: true, tab: 'overview' };
  renderDetailModal();
  openModal(elements.detailModal);
}

async function openMissionDetailModal(id, preferredTab = 'overview') {
  showDetailLoading('mission', 'Mission Detail');
  try {
    const [payload, logData, diffData] = await Promise.all([
      loadDetailPayload('mission', id),
      loadMissionLog(id).catch(() => ({ log: 'Unable to load mission log.', lines: 0, totalLines: 0 })),
      loadMissionDiff(id).catch(() => ({ diff: 'Unable to load mission diff.' })),
    ]);

    state.detailModal = {
      type: 'mission',
      id,
      tab: preferredTab,
      loading: false,
      payload,
      log: logData.log || '',
      logMeta: logData,
      diff: diffData.diff || '',
    };
    renderDetailModal();
    openModal(elements.detailModal);
  } catch (error) {
    state.detailModal = { type: 'mission', id, loading: false, payload: null };
    setDetailModalFrame(
      'Mission Detail',
      '',
      `<div class="detail-empty">${escapeHtml(error instanceof Error ? error.message : 'Unable to load mission detail.')}</div>`,
    );
    openModal(elements.detailModal);
  }
}

async function openVoyageDetailModal(id) {
  showDetailLoading('voyage', 'Voyage Detail');
  try {
    const payload = await loadDetailPayload('voyage', id);
    state.detailModal = { type: 'voyage', id, loading: false, payload };
    renderDetailModal();
    openModal(elements.detailModal);
  } catch (error) {
    setDetailModalFrame(
      'Voyage Detail',
      '',
      `<div class="detail-empty">${escapeHtml(error instanceof Error ? error.message : 'Unable to load voyage detail.')}</div>`,
    );
    openModal(elements.detailModal);
  }
}

async function openCaptainDetailModal(id) {
  showDetailLoading('captain', 'Captain Detail');
  try {
    const payload = await loadDetailPayload('captain', id);
    state.detailModal = { type: 'captain', id, loading: false, payload, log: '' };
    renderDetailModal();
    openModal(elements.detailModal);
  } catch (error) {
    setDetailModalFrame(
      'Captain Detail',
      '',
      `<div class="detail-empty">${escapeHtml(error instanceof Error ? error.message : 'Unable to load captain detail.')}</div>`,
    );
    openModal(elements.detailModal);
  }
}

async function performDetailAction(action, id) {
  const base = instanceBaseUrl();

  try {
    if (action === 'open-mission') {
      await openMissionDetailModal(id);
      return;
    }

    if (action === 'mission-edit') {
      if (state.detailModal?.payload?.mission) openMissionModal(state.detailModal.payload.mission);
      return;
    }

    if (action === 'mission-log-refresh') {
      const logData = await loadMissionLog(id);
      if (state.detailModal) {
        state.detailModal.log = logData.log || '';
        state.detailModal.logMeta = logData;
      }
      renderDetailModal();
      return;
    }

    if (action === 'mission-diff-refresh') {
      const diffData = await loadMissionDiff(id);
      if (state.detailModal) state.detailModal.diff = diffData.diff || '';
      renderDetailModal();
      return;
    }

    if (action === 'mission-cancel') {
      if (!confirm('Cancel this mission?')) return;
      await fetchJson(`${base}/missions/${encodeURIComponent(id)}`, { method: 'DELETE' });
      await loadSelectedInstance();
      await openMissionDetailModal(id, state.detailModal?.tab || 'overview');
      return;
    }

    if (action === 'mission-restart') {
      if (!confirm('Restart this mission?')) return;
      await fetchJson(`${base}/missions/${encodeURIComponent(id)}/restart`, { method: 'POST', body: {} });
      await loadSelectedInstance();
      await openMissionDetailModal(id, state.detailModal?.tab || 'overview');
      return;
    }

    if (action === 'voyage-cancel') {
      if (!confirm('Cancel this voyage and its active work?')) return;
      await fetchJson(`${base}/voyages/${encodeURIComponent(id)}`, { method: 'DELETE' });
      await loadSelectedInstance();
      await openVoyageDetailModal(id);
      return;
    }

    if (action === 'captain-log') {
      const data = await fetchJson(`${base}/captains/${encodeURIComponent(id)}/log?lines=120&offset=0`);
      if (state.detailModal) state.detailModal.log = data.log || 'No log content available.';
      renderDetailModal();
      return;
    }

    if (action === 'captain-stop') {
      if (!confirm('Stop this captain?')) return;
      await fetchJson(`${base}/captains/${encodeURIComponent(id)}/stop`, { method: 'POST' });
      await loadSelectedInstance();
      await openCaptainDetailModal(id);
    }
  } catch (error) {
    if (state.detailModal?.type === 'mission') {
      state.detailModal.log = error instanceof Error ? error.message : 'Action failed.';
      renderDetailModal();
      return;
    }

    setDetailModalFrame(
      elements.detailModalTitle.textContent || 'Detail',
      elements.detailModalSubtitle.innerHTML,
      `<div class="detail-empty">${escapeHtml(error instanceof Error ? error.message : 'Action failed.')}</div>`,
    );
  }
}

function parseDispatchMissions(raw) {
  return String(raw || '')
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean)
    .map((line) => {
      const separator = line.indexOf('::');
      if (separator >= 0) {
        return {
          title: line.slice(0, separator).trim(),
          description: line.slice(separator + 2).trim(),
        };
      }

      return {
        title: line,
        description: line,
      };
    })
    .filter((mission) => mission.title);
}

async function applyDispatchPriority(voyageId, priority) {
  if (!voyageId) return;
  const targetPriority = Number(priority);
  if (!Number.isFinite(targetPriority) || targetPriority === 100) return;

  const base = instanceBaseUrl();
  const detail = await fetchJson(`${base}/voyages/${encodeURIComponent(voyageId)}`);
  const missions = detail.missions || [];

  await Promise.all(missions.map((mission) => fetchJson(`${base}/missions/${encodeURIComponent(mission.id)}`, {
    method: 'PUT',
    body: {
      title: mission.title,
      description: mission.description || null,
      priority: targetPriority,
      vesselId: mission.vesselId || null,
      voyageId: mission.voyageId || null,
      branchName: mission.branchName || null,
      prUrl: mission.prUrl || null,
      parentMissionId: mission.parentMissionId || null,
      persona: mission.persona || null,
    },
  })));
}

async function submitFleetForm(event) {
  event.preventDefault();
  if (!state.selectedInstanceId) return;

  const payload = {
    name: elements.fleetName.value.trim(),
    description: elements.fleetDescription.value.trim() || null,
    defaultPipelineId: elements.fleetDefaultPipelineId.value.trim() || null,
    active: elements.fleetActive.checked,
  };

  if (!payload.name) {
    setFormStatus(elements.fleetFormStatus, 'Fleet name is required.', 'error');
    return;
  }

  try {
    const base = instanceBaseUrl();
    if (state.selectedFleetId) {
      await fetchJson(`${base}/fleets/${encodeURIComponent(state.selectedFleetId)}`, { method: 'PUT', body: payload });
      setFormStatus(elements.fleetFormStatus, 'Fleet updated.', 'success');
    } else {
      await fetchJson(`${base}/fleets`, { method: 'POST', body: payload });
      setFormStatus(elements.fleetFormStatus, 'Fleet created.', 'success');
    }

    await loadSelectedInstance();
    closeModal(elements.fleetModal);
    state.selectedFleetId = null;
    state.editingFleet = null;
  } catch (error) {
    setFormStatus(elements.fleetFormStatus, error instanceof Error ? error.message : 'Fleet save failed.', 'error');
  }
}

async function submitVesselForm(event) {
  event.preventDefault();
  if (!state.selectedInstanceId) return;

  const payload = {
    fleetId: elements.vesselFleetId.value.trim() || null,
    name: elements.vesselName.value.trim(),
    repoUrl: elements.vesselRepoUrl.value.trim(),
    workingDirectory: elements.vesselWorkingDirectory.value.trim() || null,
    defaultBranch: elements.vesselDefaultBranch.value.trim() || 'main',
    defaultPipelineId: elements.vesselDefaultPipelineId.value.trim() || null,
    allowConcurrentMissions: elements.vesselAllowConcurrentMissions.checked,
    active: elements.vesselActive.checked,
  };

  if (!payload.name || !payload.repoUrl) {
    setFormStatus(elements.vesselFormStatus, 'Vessel name and repo URL are required.', 'error');
    return;
  }

  try {
    const base = instanceBaseUrl();
    if (state.selectedVesselId) {
      await fetchJson(`${base}/vessels/${encodeURIComponent(state.selectedVesselId)}`, { method: 'PUT', body: payload });
      setFormStatus(elements.vesselFormStatus, 'Vessel updated.', 'success');
    } else {
      await fetchJson(`${base}/vessels`, { method: 'POST', body: payload });
      setFormStatus(elements.vesselFormStatus, 'Vessel created.', 'success');
    }

    await loadSelectedInstance();
    closeModal(elements.vesselModal);
    state.selectedVesselId = null;
    state.editingVessel = null;
  } catch (error) {
    setFormStatus(elements.vesselFormStatus, error instanceof Error ? error.message : 'Vessel save failed.', 'error');
  }
}

async function submitDispatchForm(event) {
  event.preventDefault();
  if (!state.selectedInstanceId) return;

  const missions = parseDispatchMissions(elements.dispatchMissions.value);
  const payload = {
    title: elements.dispatchTitle.value.trim(),
    description: elements.dispatchDescription.value.trim(),
    vesselId: elements.dispatchVesselId.value.trim(),
    pipelineId: elements.dispatchPipelineId.value.trim() || null,
    missions,
  };
  const priority = Number.parseInt(elements.dispatchPriority.value || '100', 10) || 100;

  if (!payload.title) {
    setFormStatus(elements.dispatchFormStatus, 'Voyage title is required.', 'error');
    return;
  }

  if (!payload.vesselId) {
    setFormStatus(elements.dispatchFormStatus, 'Select a vessel for dispatch.', 'error');
    return;
  }

  if (missions.length === 0) {
    setFormStatus(elements.dispatchFormStatus, 'Provide at least one mission line.', 'error');
    return;
  }

  setButtonBusy(elements.dispatchSubmitButton, true, 'Dispatch', 'Dispatching...');
  setFormStatus(elements.dispatchFormStatus, 'Dispatching voyage...', null);

  try {
    const base = instanceBaseUrl();
    const voyage = await fetchJson(`${base}/voyages/dispatch`, { method: 'POST', body: payload });
    await applyDispatchPriority(voyage.id, priority);
    setFormStatus(elements.dispatchFormStatus, `Voyage dispatched: ${voyage.id || voyage.title || 'created'}`, 'success');
    await loadSelectedInstance();
    closeModal(elements.dispatchModal);
    if (voyage.id) await openVoyageDetailModal(voyage.id);
  } catch (error) {
    setFormStatus(elements.dispatchFormStatus, error instanceof Error ? error.message : 'Dispatch failed.', 'error');
  } finally {
    setButtonBusy(elements.dispatchSubmitButton, false, 'Dispatch', 'Dispatching...');
  }
}

async function submitMissionForm(event) {
  event.preventDefault();
  if (!state.selectedInstanceId) return;

  const payload = {
    title: elements.missionTitle.value.trim(),
    description: elements.missionDescription.value.trim() || null,
    vesselId: elements.missionVesselId.value.trim() || null,
    voyageId: elements.missionVoyageId.value.trim() || null,
    persona: elements.missionPersona.value.trim() || null,
    priority: Number.parseInt(elements.missionPriority.value || '100', 10) || 100,
  };

  if (!payload.title) {
    setFormStatus(elements.missionFormStatus, 'Mission title is required.', 'error');
    return;
  }

  try {
    const base = instanceBaseUrl();
    if (state.selectedMissionId) {
      await fetchJson(`${base}/missions/${encodeURIComponent(state.selectedMissionId)}`, { method: 'PUT', body: payload });
      setFormStatus(elements.missionFormStatus, 'Mission updated.', 'success');
    } else {
      await fetchJson(`${base}/missions`, { method: 'POST', body: payload });
      setFormStatus(elements.missionFormStatus, 'Mission created.', 'success');
    }

    const currentMissionId = state.selectedMissionId;
    await loadSelectedInstance();
    closeModal(elements.missionModal);
    if (currentMissionId) await openMissionDetailModal(currentMissionId);
    state.selectedMissionId = null;
    state.editingMission = null;
  } catch (error) {
    setFormStatus(elements.missionFormStatus, error instanceof Error ? error.message : 'Mission save failed.', 'error');
  }
}

async function submitMissionBrowseForm(event) {
  event.preventDefault();
  if (!state.selectedInstanceId) return;

  const query = buildQuery({
    limit: elements.missionBrowseLimit.value || '12',
    status: elements.missionBrowseStatus.value,
    voyageId: elements.missionBrowseVoyageId.value,
    vesselId: elements.missionBrowseVesselId.value,
  });

  try {
    const base = instanceBaseUrl();
    const data = await fetchJson(`${base}/missions${query}`);
    const rows = data.missions || [];
    renderEntityList(elements.missionList, rows, 'mission');
    setFormStatus(elements.missionBrowseStatusText, `Loaded ${rows.length} mission${rows.length === 1 ? '' : 's'} from the deployment.`, 'success');
  } catch (error) {
    setFormStatus(elements.missionBrowseStatusText, error instanceof Error ? error.message : 'Mission browse failed.', 'error');
  }
}

async function submitVoyageBrowseForm(event) {
  event.preventDefault();
  if (!state.selectedInstanceId) return;

  const query = buildQuery({
    limit: elements.voyageBrowseLimit.value || '12',
    status: elements.voyageBrowseStatus.value,
  });

  try {
    const base = instanceBaseUrl();
    const data = await fetchJson(`${base}/voyages${query}`);
    const rows = data.voyages || [];
    renderEntityList(elements.voyageList, rows, 'voyage');
    setFormStatus(elements.voyageBrowseStatusText, `Loaded ${rows.length} voyage${rows.length === 1 ? '' : 's'} from the deployment.`, 'success');
  } catch (error) {
    setFormStatus(elements.voyageBrowseStatusText, error instanceof Error ? error.message : 'Voyage browse failed.', 'error');
  }
}

function bindSidebarNavigation() {
  document.querySelectorAll('[data-scroll-target]').forEach((button) => {
    button.addEventListener('click', () => {
      const targetId = button.getAttribute('data-scroll-target');
      if (!targetId) return;

      const target = document.getElementById(targetId);
      if (!target) return;

      document.querySelectorAll('.sidebar-nav-item').forEach((item) => item.classList.remove('active'));
      button.classList.add('active');
      target.scrollIntoView({ behavior: 'smooth', block: 'start' });
      closeSidebar();
    });
  });
}

async function initializeProxyShell() {
  const storedDeploymentId = getStoredDeploymentId();

  try {
    await loadInstances();
    renderSessionState();

    if (storedDeploymentId) {
      elements.loginInstanceId.value = storedDeploymentId;
      if (!getInstanceById(storedDeploymentId)) {
        setLoginStatus(`Deployment "${storedDeploymentId}" is not currently connected to this proxy.`, 'error');
      }
    }
  } catch {
    renderSessionState();
    setLoginStatus('Unable to load connected deployments right now.', 'error');
  }
}

elements.loginForm.addEventListener('submit', async (event) => {
  event.preventDefault();
  await authenticateInstance(elements.loginInstanceId.value);
});

elements.loginRefreshButton.addEventListener('click', async () => {
  try {
    setLoginStatus('Refreshing proxy registry...', null);
    await loadInstances();
    setLoginStatus('Registry refreshed.', 'success');
  } catch (error) {
    setLoginStatus(error instanceof Error ? error.message : 'Failed to refresh proxy registry.', 'error');
  }
});

elements.refreshButton.addEventListener('click', async () => {
  await loadInstances();
});

elements.themeToggleButton.addEventListener('click', toggleTheme);
elements.loginThemeToggleButton.addEventListener('click', toggleTheme);

elements.openDispatchButton.addEventListener('click', () => {
  resetDispatchForm();
  openModal(elements.dispatchModal);
});

elements.openFleetModalButton.addEventListener('click', () => {
  state.selectedFleetId = null;
  state.editingFleet = null;
  resetFleetForm();
  openFleetModal();
});

elements.openVesselModalButton.addEventListener('click', () => {
  state.selectedVesselId = null;
  state.editingVessel = null;
  resetVesselForm();
  openVesselModal();
});

elements.fleetForm.addEventListener('submit', submitFleetForm);
elements.fleetResetButton.addEventListener('click', resetFleetForm);
elements.vesselForm.addEventListener('submit', submitVesselForm);
elements.vesselResetButton.addEventListener('click', resetVesselForm);
elements.dispatchForm.addEventListener('submit', submitDispatchForm);
elements.missionForm.addEventListener('submit', submitMissionForm);
elements.missionResetButton.addEventListener('click', resetMissionForm);
elements.missionBrowseForm.addEventListener('submit', submitMissionBrowseForm);
elements.missionBrowseRecentButton.addEventListener('click', loadRecentMissionList);
elements.voyageBrowseForm.addEventListener('submit', submitVoyageBrowseForm);
elements.voyageBrowseRecentButton.addEventListener('click', loadRecentVoyageList);

elements.switchDeploymentButton.addEventListener('click', () => {
  logoutToLogin('', state.selectedInstanceId || '');
});

elements.sidebarSwitchDeploymentButton.addEventListener('click', () => {
  logoutToLogin('', state.selectedInstanceId || '');
});

elements.mobileMenuButton.addEventListener('click', () => {
  if (state.sidebarOpen) closeSidebar();
  else openSidebar();
});

elements.sidebarOverlay.addEventListener('click', closeSidebar);

document.querySelectorAll('[data-close-modal]').forEach((button) => {
  button.addEventListener('click', () => {
    closeModalById(button.getAttribute('data-close-modal'));
  });
});

elements.detailModalBody.addEventListener('click', async (event) => {
  const tabButton = event.target.closest('[data-detail-tab]');
  if (tabButton && state.detailModal?.type === 'mission') {
    state.detailModal.tab = tabButton.getAttribute('data-detail-tab') || 'overview';
    renderDetailModal();
    return;
  }

  const actionButton = event.target.closest('[data-detail-action]');
  if (!actionButton || !state.selectedInstanceId) return;

  const action = actionButton.getAttribute('data-detail-action');
  const id = actionButton.getAttribute('data-id');
  if (!action || !id) return;
  await performDetailAction(action, id);
});

window.addEventListener('keydown', (event) => {
  if (event.key !== 'Escape') return;
  if (isAnyModalOpen()) {
    closeAllModals();
    return;
  }
  if (state.sidebarOpen) closeSidebar();
});

bindSidebarNavigation();
applyTheme(getPreferredTheme());
renderSessionState();
initializeProxyShell().catch(() => {
  renderSessionState();
  elements.instanceList.innerHTML = '<div class="text-muted">Unable to load connected deployments right now.</div>';
  setLoginStatus('Unable to load connected deployments right now.', 'error');
});
