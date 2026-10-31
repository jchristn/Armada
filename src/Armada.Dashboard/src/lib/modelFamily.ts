// The model family of a captain, as the server's ModelFamily (src/Armada.Core/Services/ModelFamily.cs) computes it:
// the leading letters of the model name after any provider prefix, with a few aliases, falling back to the runtime.
// The Evaluate dialog uses it only to warn that an evaluator shares the evaluated captain's family.

const ALIASES: Record<string, string> = {
  o: 'gpt',
  chatgpt: 'gpt',
  codex: 'gpt',
  openai: 'gpt',
  sonnet: 'claude',
  opus: 'claude',
  haiku: 'claude',
  anthropic: 'claude',
  meta: 'llama',
  codellama: 'llama',
};

/** The family of a model name (for example qwen for qwen3.8:27b), or null when the name has no leading letters. */
export function modelFamilyFromModel(model: string | null | undefined): string | null {
  if (!model || !model.trim()) return null;
  let name = model.trim();
  const slash = name.lastIndexOf('/');
  if (slash >= 0 && slash < name.length - 1) name = name.substring(slash + 1);
  const match = /^[A-Za-z]+/.exec(name);
  if (!match) return null;
  const family = match[0].toLowerCase();
  return ALIASES[family] ?? family;
}

/** The family of a vendor runtime, used when the model name gives none. */
export function modelFamilyFromRuntime(runtime: string | null | undefined): string {
  switch (runtime) {
    case 'ClaudeCode': return 'claude';
    case 'Codex': return 'gpt';
    case 'Gemini': return 'gemini';
    default: return `runtime:${(runtime ?? '').toLowerCase()}`;
  }
}

/** The family of a captain: from its model, else from its runtime. */
export function captainModelFamily(captain: { model?: string | null; runtime?: string | null }): string {
  return modelFamilyFromModel(captain.model) ?? modelFamilyFromRuntime(captain.runtime);
}
