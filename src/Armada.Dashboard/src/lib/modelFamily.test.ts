import { captainModelFamily, modelFamilyFromModel, modelFamilyFromRuntime } from './modelFamily';

describe('modelFamily', () => {
  it('matches the server: provider prefixes, sizes and versions dropped, aliases mapped', () => {
    expect(modelFamilyFromModel('qwen3.8:27b')).toBe('qwen');
    expect(modelFamilyFromModel('openai/gpt-oss-20b')).toBe('gpt');
    expect(modelFamilyFromModel('o3-mini')).toBe('gpt');
    expect(modelFamilyFromModel('claude-opus-5-5')).toBe('claude');
    expect(modelFamilyFromModel('DeepSeek-V4.1')).toBe('deepseek');
    expect(modelFamilyFromModel('  ')).toBeNull();
    expect(modelFamilyFromModel('7b')).toBeNull();
    expect(modelFamilyFromModel(null)).toBeNull();
  });

  it('falls back to the runtime', () => {
    expect(modelFamilyFromRuntime('ClaudeCode')).toBe('claude');
    expect(modelFamilyFromRuntime('Codex')).toBe('gpt');
    expect(modelFamilyFromRuntime('Mux')).toBe('runtime:mux');
    expect(captainModelFamily({ model: null, runtime: 'ClaudeCode' })).toBe('claude');
    expect(captainModelFamily({ model: 'qwen3:8b', runtime: 'ClaudeCode' })).toBe('qwen');
  });
});
