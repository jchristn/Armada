import type { RebuildStatus } from '../api/client';
import type { AskSettingsData, CliPermissionSettingsData, FleetActionSettingsData, RetentionSettingsData, VesselImportSettingsData } from '../types/models';
import { DEFAULT_GLOBAL_LANDING_MODE } from './vesselForm';

/**
 * Pure logic of the Server settings page (pages/Server.tsx), shared with the mobile app: the settings shape the
 * page edits, defaults and merging, the remote tunnel indicator, the MCP client snippets, and the rebuild state.
 */

/** Tunnel capability manifest reported in the health response. */
export interface RemoteTunnelCapabilityManifest {
  protocolVersion: string;
  armadaVersion: string;
  features: string[];
}

/** Remote tunnel status reported in the health response. */
export interface RemoteTunnelStatus {
  enabled: boolean;
  state: string;
  tunnelUrl: string | null;
  instanceId: string | null;
  lastConnectAttemptUtc: string | null;
  connectedUtc: string | null;
  lastHeartbeatUtc: string | null;
  lastDisconnectUtc: string | null;
  lastError: string | null;
  reconnectAttempts: number;
  latencyMs: number | null;
  capabilityManifest: RemoteTunnelCapabilityManifest;
}

/** GET /api/v1/status/health as the Server page reads it. */
export interface HealthInfo {
  status: string;
  timestamp: string;
  startUtc: string;
  uptime: string;
  version: string;
  ports: {
    admiral: number;
    mcp: number;
    webSocket: number;
  };
  remoteTunnel?: RemoteTunnelStatus;
}

/** Remote control (Armada.Proxy tunnel) settings; secrets come back redacted. */
export interface RemoteControlSettings {
  enabled: boolean;
  tunnelUrl: string | null;
  instanceId: string | null;
  enrollmentToken: string | null;
  password: string | null;
  connectTimeoutSeconds: number;
  heartbeatIntervalSeconds: number;
  reconnectBaseDelaySeconds: number;
  reconnectMaxDelaySeconds: number;
  allowInvalidCertificates: boolean;
}

/** GET /api/v1/settings as the Server page edits it. */
export interface ServerSettings {
  admiralPort: number;
  mcpPort: number;
  maxCaptains: number;
  heartbeatIntervalSeconds: number;
  stallThresholdMinutes: number;
  stageCheckTimeoutMinutes?: number;
  idleCaptainTimeoutSeconds: number;
  planningSessionInactivityTimeoutMinutes: number;
  planningSessionAbandonmentTimeoutMinutes: number;
  planningSessionRetentionDays: number;
  landingMode?: string | null;
  dataDirectory: string;
  databasePath: string;
  logDirectory: string;
  docksDirectory: string;
  reposDirectory: string;
  selfVesselId?: string | null;
  rebuildSlotRetentionCount?: number;
  /** True: missions wait for a Harbor instead of running on the Admiral host. Absent on older servers. */
  requireHarborForLaunch?: boolean;
  blockDependentsOfFailedStages?: boolean;
  remoteControl: RemoteControlSettings;
  import?: VesselImportSettingsData;
  fleetActions?: FleetActionSettingsData;
  retention?: RetentionSettingsData;
  storage?: import('../types/storage').StorageSettings;
  permissions?: CliPermissionSettingsData;
  /** Absent on servers older than the Ask settings group. */
  ask?: AskSettingsData;
  /** Mission and voyage black box (storage target and retention); storage secrets read back masked. */
  blackBox?: import('../types/blackbox').BlackBoxSettingsData;
}

/** English tooltip of the Require Harbor for Launch setting (translate with t()). */
export const REQUIRE_HARBOR_FOR_LAUNCH_TOOLTIP = 'Off (default): when no Harbor can take a mission (the laptop is disconnected, its Harbor is full, or it does not serve the vessel), the mission runs on the Admiral host instead. In Docker the Admiral has no claude, codex or mux installed, so those missions fail. On: the mission waits as Pending until a Harbor can take it.';

/** The redacted form of a stored secret in GET /api/v1/settings; sending it back keeps the stored value. */
export const REDACTED_SECRET = '********';

export const DEFAULT_REMOTE_TUNNEL_URL = 'http://proxy.armadago.ai:7893/tunnel';

export type McpClientKey = 'claude' | 'codex' | 'gemini' | 'cursor';

export interface McpClientReference {
  key: McpClientKey;
  title: string;
  location: string;
}

export const MCP_CLIENTS: McpClientReference[] = [
  { key: 'claude', title: 'Claude Code', location: '~/.claude.json -> mcpServers.armada' },
  { key: 'codex', title: 'Codex', location: '~/.codex/config.toml -> [mcp_servers.armada]' },
  { key: 'gemini', title: 'Gemini', location: '~/.gemini/settings.json -> mcpServers.armada' },
  { key: 'cursor', title: 'Cursor', location: '.cursor/mcp.json -> mcpServers.armada' },
];

export function getDefaultRemoteControlSettings(): RemoteControlSettings {
  return {
    enabled: false,
    tunnelUrl: DEFAULT_REMOTE_TUNNEL_URL,
    instanceId: null,
    enrollmentToken: null,
    password: 'armadaadmin',
    connectTimeoutSeconds: 15,
    heartbeatIntervalSeconds: 30,
    reconnectBaseDelaySeconds: 5,
    reconnectMaxDelaySeconds: 60,
    allowInvalidCertificates: false,
  };
}

/** Fills the landing mode and remote control defaults into settings from the server. */
export function mergeServerSettings(data: ServerSettings | null): ServerSettings | null {
  if (!data) return null;
  return {
    ...data,
    landingMode: data.landingMode || DEFAULT_GLOBAL_LANDING_MODE,
    remoteControl: {
      ...getDefaultRemoteControlSettings(),
      ...(data.remoteControl ?? {}),
    },
  };
}

/** Label key and dot class of the remote tunnel state. */
export function getRemoteTunnelIndicator(
  enabled: boolean,
  state: string | null | undefined,
): { labelKey: string; dotClass: 'connected' | 'warning' | 'disconnected' } {
  if (!enabled) {
    return { labelKey: 'Disabled', dotClass: 'disconnected' };
  }

  switch ((state || '').toLowerCase()) {
    case 'connected':
      return { labelKey: 'Connected', dotClass: 'connected' };
    case 'connecting':
      return { labelKey: 'Connecting', dotClass: 'warning' };
    case 'stopping':
      return { labelKey: 'Stopping', dotClass: 'warning' };
    case 'error':
      return { labelKey: 'Error', dotClass: 'disconnected' };
    case 'disconnected':
      return { labelKey: 'Disconnected', dotClass: 'disconnected' };
    default:
      return { labelKey: 'Checking status', dotClass: 'warning' };
  }
}

/** English key of a health status (Healthy, Degraded, ...), or the status itself when unknown, '-' when empty. */
export function healthStatusKey(status: string | null | undefined): string {
  switch ((status || '').toLowerCase()) {
    case 'healthy': return 'Healthy';
    case 'degraded': return 'Degraded';
    case 'unhealthy': return 'Unhealthy';
    case 'error': return 'Error';
    case 'unknown': return 'Unknown';
    default: return status || '-';
  }
}

/** True for the statuses healthStatusKey maps to a catalog key. */
export function isKnownHealthStatus(status: string | null | undefined): boolean {
  return ['healthy', 'degraded', 'unhealthy', 'error', 'unknown'].includes((status || '').toLowerCase());
}

/** The MCP JSON-RPC URL: the health-reported MCP port, then the configured one, then 7891. */
export function getMcpRpcUrl(healthMcpPort: number | null | undefined, settingsMcpPort: number | null | undefined): string {
  const port = healthMcpPort || settingsMcpPort || 7891;
  return `http://localhost:${port}/rpc`;
}

/** HTTP MCP client configuration for one client. Codex is a TOML table; the other clients stay JSON. */
export function getMcpConfigHttp(client: McpClientKey, rpcUrl: string): string {
  switch (client) {
    case 'codex':
      return '[mcp_servers.armada]\nurl = "' + codexMcpHttpUrl(rpcUrl) + '"\n';
    case 'claude':
      return JSON.stringify({ mcpServers: { armada: { type: 'http', url: rpcUrl } } }, null, 2);
    case 'gemini':
      return JSON.stringify({ mcpServers: { armada: { httpUrl: rpcUrl } } }, null, 2);
    case 'cursor':
      return JSON.stringify({ mcpServers: { armada: { url: rpcUrl } } }, null, 2);
  }
}

/** Codex HTTP url: the port already chosen for the JSON-RPC url, on localhost, with path /mcp. */
function codexMcpHttpUrl(rpcUrl: string): string {
  const parsed = new URL(rpcUrl);
  const port = parsed.port.length > 0 ? parsed.port : '7891';
  return 'http://localhost:' + port + '/mcp';
}

/** STDIO MCP client configuration (or command) for one client. */
export function getMcpConfigStdio(client: McpClientKey): string {
  switch (client) {
    case 'claude':
      return 'claude mcp add --scope user armada -- armada mcp stdio';
    case 'cursor':
      return JSON.stringify({ mcpServers: { armada: { command: 'armada', args: ['mcp', 'stdio'] } } }, null, 2);
    case 'codex':
      return '[mcp_servers.armada]\ncommand = "armada"\nargs = ["mcp", "stdio"]\n';
    case 'gemini':
      return JSON.stringify({ mcpServers: { armada: { type: 'stdio', command: 'armada', args: ['mcp', 'stdio'] } } }, null, 2);
  }
}

/** True once a rebuild has finished (succeeded, failed, or rolled back). */
export function isRebuildTerminal(s?: RebuildStatus | null): boolean {
  return !!s && (s.status === 'Succeeded' || s.status === 'Failed' || s.status === 'RolledBack');
}
