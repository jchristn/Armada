import { describe, expect, it } from 'vitest';
import type { CaptainChatMetrics } from '../types/models';
import { askTurnStatistics, chatTurnStatistics, formatCostUsd, formatMetricMs, toolStatistics } from './chatMetrics';
import type { ToolEvent } from './toolEvents';

const t = (text: string) => text;

const metrics: CaptainChatMetrics = {
  timeToFirstTokenMs: 812.4,
  streamingMs: 4210,
  totalMs: 5022,
  promptTokens: 1200,
  completionTokens: null,
  totalTokens: 1530,
  tokensPerSecond: 72.66,
};

const tools: ToolEvent[] = [
  { id: 'a', name: 'armada_status', status: 'success', elapsedMs: 420 },
  { id: 'b', name: 'Bash', status: 'failed', elapsedMs: 1250 },
  { id: 'c', name: 'Read', status: 'running', elapsedMs: null },
];

describe('chatMetrics', () => {
  it('formats milliseconds as the dashboard popover did', () => {
    expect(formatMetricMs(null)).toBe('-');
    expect(formatMetricMs(undefined)).toBe('-');
    expect(formatMetricMs(812.4)).toBe('812ms');
    expect(formatMetricMs(1000)).toBe('1.00s');
    expect(formatMetricMs(4210)).toBe('4.21s');
  });

  it('chat turn rows: time to first token, streaming, tokens/sec, tokens (completion or total), input, total, then tools', () => {
    expect(chatTurnStatistics(t, metrics, tools).map((r) => [r.key, r.label, r.value])).toEqual([
      ['timeToFirstToken', 'time to first token', '812ms'],
      ['streaming', 'streaming', '4.21s'],
      ['tokensPerSecond', 'tokens/sec', '72.7'],
      ['tokens', 'tokens', '1530'],
      ['inputTokens', 'input tokens', '1200'],
      ['total', 'total', '5.02s'],
      ['toolCalls', 'tool calls', '2'],
      ['toolTime', 'tool time', '1.67s'],
    ]);
  });

  it('the compact strip counts completion tokens only and has no tool rows', () => {
    const rows = chatTurnStatistics(t, metrics, null, 'completion');
    expect(rows.find((r) => r.key === 'tokens')?.value).toBe('-');
    expect(rows.map((r) => r.key)).not.toContain('toolCalls');
  });

  it('tool rows appear only when a tool call finished', () => {
    expect(toolStatistics(t, [])).toEqual([]);
    expect(toolStatistics(t, [{ id: 'x', name: 'Read', status: 'running' }])).toEqual([]);
  });

  it('an Ask reply: the persisted turn duration and its tool calls', () => {
    expect(askTurnStatistics(t, {
      durationMs: 6300,
      toolCalls: [
        { id: 'tc1', callId: 'c1', toolName: 'armada_status', ok: true, resultText: '{}', elapsedMs: 300 },
        { id: 'tc2', callId: 'c2', toolName: 'armada_enumerate', ok: true, resultText: '[]', elapsedMs: 950 },
      ],
    }).map((r) => [r.key, r.value])).toEqual([['total', '6.30s'], ['toolCalls', '2'], ['toolTime', '1.25s']]);
    expect(askTurnStatistics(t, { durationMs: null, toolCalls: null })).toEqual([]);
  });

  it('formats a cost in dollars', () => {
    expect(formatCostUsd(null)).toBe('-');
    expect(formatCostUsd(0.0123)).toBe('$0.0123');
    expect(formatCostUsd(1.5)).toBe('$1.50');
  });

  it('an Ask reply with recorded telemetry shows the full set, like a Planning reply', () => {
    const rows = askTurnStatistics(t, {
      durationMs: 2500,
      toolCalls: [{ id: 'tc1', callId: 'c1', toolName: 'armada_status', ok: true, resultText: '{}', elapsedMs: 250 }],
      metrics: {
        timeToFirstTokenMs: 400,
        timeToFirstTextMs: 1000,
        streamingMs: 2100,
        totalMs: 2500,
        promptTokens: 1050,
        completionTokens: 120,
        totalTokens: 1170,
        tokensPerSecond: 57.14,
        cachedTokens: 900,
        costUsd: 0.0123,
        tokensEstimated: false,
        toolCallCount: 1,
        toolTimeMs: 250,
      },
    });
    expect(rows.map((r) => [r.key, r.label, r.value])).toEqual([
      ['timeToFirstToken', 'time to first token', '400ms'],
      ['timeToFirstText', 'time to first text', '1.00s'],
      ['streaming', 'streaming', '2.10s'],
      ['tokensPerSecond', 'tokens/sec', '57.1'],
      ['tokens', 'output tokens', '120'],
      ['inputTokens', 'input tokens', '1050'],
      ['cachedTokens', 'cached tokens', '900'],
      ['cost', 'cost', '$0.0123'],
      ['total', 'total', '2.50s'],
      ['toolCalls', 'tool calls', '1'],
      ['toolTime', 'tool time', '250ms'],
    ]);
  });

  it('an Ask reply from a runtime without usage: estimated tokens read "~", no input, cache, or cost rows', () => {
    const rows = askTurnStatistics(t, {
      durationMs: 900,
      toolCalls: null,
      metrics: {
        timeToFirstTokenMs: 300,
        timeToFirstTextMs: 300,
        streamingMs: 600,
        totalMs: null,
        promptTokens: null,
        completionTokens: 9,
        totalTokens: null,
        tokensPerSecond: 15,
        tokensEstimated: true,
        toolCallCount: 0,
        toolTimeMs: 0,
      },
    });
    expect(rows.map((r) => [r.key, r.value])).toEqual([
      ['timeToFirstToken', '300ms'],
      ['streaming', '600ms'],
      ['tokensPerSecond', '~15.0'],
      ['tokens', '~9'],
      ['total', '900ms'],
    ]);
  });

  it('tool rows fall back to the recorded counts when no tool events are given', () => {
    const rows = chatTurnStatistics(t, { ...metrics, toolCallCount: 3, toolTimeMs: 1500 });
    expect(rows.slice(-2).map((r) => [r.key, r.value])).toEqual([['toolCalls', '3'], ['toolTime', '1.50s']]);
  });
});
