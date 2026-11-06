import { describe, expect, it } from 'vitest';
import { MCP_CLIENTS, getMcpConfigHttp, getMcpConfigStdio } from './serverSettings';

describe('Codex MCP copy text', () => {
  it('copies a TOML table for config.toml and an HTTP url that ends in /mcp', () => {
    const http = getMcpConfigHttp('codex', 'http://localhost:7891/rpc');
    expect(http).toContain('[mcp_servers.armada]');
    expect(http).toContain('http://localhost:7891/mcp');
    expect(http).not.toContain('mcpServers');

    const otherPort = getMcpConfigHttp('codex', 'http://localhost:7900/rpc');
    expect(otherPort).toContain('http://localhost:7900/mcp');

    const stdio = getMcpConfigStdio('codex');
    expect(stdio).toContain('[mcp_servers.armada]');
    expect(stdio).toContain('command = "armada"');
    expect(stdio).toContain('args = ["mcp", "stdio"]');
    expect(stdio).not.toContain('mcpServers');

    const codex = MCP_CLIENTS.find((client) => client.key === 'codex');
    expect(codex?.location).toContain('config.toml');
    expect(codex?.location).toContain('[mcp_servers.armada]');

    const claude = getMcpConfigHttp('claude', 'http://localhost:7891/rpc');
    expect(claude).toContain('mcpServers');
    expect(claude).toContain('http://localhost:7891/rpc');
  });
});
