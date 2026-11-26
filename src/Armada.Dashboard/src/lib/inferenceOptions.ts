import type { InferenceOptions, InferenceStreamingMode } from '../types/models';

export const defaultInferenceOptions: InferenceOptions = {
  toolsEnabled: true, readOnlyTools: false, mcpToolsEnabled: true, allowedTools: null,
  maxIterations: 100, maxTokens: null, temperature: null, topP: null, requireHarbor: false,
  contextLength: null, maxConcurrentMissions: null, streaming: false, firstResponseTimeoutMs: null,
  toolOutputWindowPercent: null,
};

/** The streaming mode of stored options; a server before 1.5.0 sends only the streaming flag. */
export function streamingModeOf(options: InferenceOptions): InferenceStreamingMode {
  return options.streamingMode ?? (options.streaming ? 'On' : 'Automatic');
}

/** Options with a streaming mode and the matching compatibility flag. */
export function withStreamingMode(options: InferenceOptions, mode: InferenceStreamingMode): InferenceOptions {
  return { ...options, streamingMode: mode, streaming: mode === 'On' };
}

/** Empty text permits all; use the tools switch to disable tools altogether. */
export function parseAllowedTools(value: string): string[] | null {
  return value.trim() ? value.split(/[\n,]/).map(s => s.trim()).filter(Boolean) : null;
}

export function validateInferenceOptions(options: InferenceOptions): string | null {
  if (!Number.isInteger(options.maxIterations) || options.maxIterations < 1 || options.maxIterations > 1000) return 'Maximum model turns must be between 1 and 1000.';
  if (options.maxTokens != null && (!Number.isInteger(options.maxTokens) || options.maxTokens < 1 || options.maxTokens > 10000000)) return 'Maximum output tokens must be between 1 and 10000000.';
  if (options.temperature != null && (!Number.isFinite(options.temperature) || options.temperature < 0 || options.temperature > 2)) return 'Temperature must be between 0 and 2.';
  if (options.topP != null && (!Number.isFinite(options.topP) || options.topP < 0 || options.topP > 1)) return 'Top P must be between 0 and 1.';
  if (options.contextLength != null && (!Number.isInteger(options.contextLength) || options.contextLength < 512 || options.contextLength > 2097152)) return 'Context window must be between 512 and 2097152.';
  if (options.maxConcurrentMissions != null && (!Number.isInteger(options.maxConcurrentMissions) || options.maxConcurrentMissions < 1 || options.maxConcurrentMissions > 64)) return 'Max concurrent missions must be between 1 and 64.';
  if (options.firstResponseTimeoutMs != null && (!Number.isInteger(options.firstResponseTimeoutMs) || options.firstResponseTimeoutMs < 1000 || options.firstResponseTimeoutMs > 1800000)) return 'First-response timeout must be between 1000 and 1800000 ms.';
  if (options.toolOutputWindowPercent != null && (!Number.isInteger(options.toolOutputWindowPercent) || options.toolOutputWindowPercent < 1 || options.toolOutputWindowPercent > 50)) return 'Tool output window must be between 1 and 50 percent.';
  if (options.allowedTools && (options.allowedTools.length > 256 || new Set(options.allowedTools).size !== options.allowedTools.length)) return 'Allowed tools must contain at most 256 distinct names.';
  return null;
}
