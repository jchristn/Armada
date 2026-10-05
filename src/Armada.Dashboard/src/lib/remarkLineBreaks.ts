/**
 * Minimal remark plugin that turns every newline inside a paragraph's text into a hard line break, the same
 * behavior as remark-breaks. Used where the source is line-oriented (mission logs) so a six-line log does not
 * collapse into one paragraph, while headings, lists, and code blocks still render as Markdown.
 */

interface MdastNode {
  type: string;
  value?: string;
  children?: MdastNode[];
}

function splitTextNode(node: MdastNode): MdastNode[] {
  const parts = (node.value ?? '').split(/\r?\n/);
  const result: MdastNode[] = [];
  parts.forEach((part, index) => {
    if (index > 0) result.push({ type: 'break' });
    if (part.length > 0) result.push({ type: 'text', value: part });
  });
  return result;
}

function transform(node: MdastNode): void {
  if (!node.children) return;
  const next: MdastNode[] = [];
  for (const child of node.children) {
    if (child.type === 'text' && child.value && /\r?\n/.test(child.value)) {
      next.push(...splitTextNode(child));
    } else {
      transform(child);
      next.push(child);
    }
  }
  node.children = next;
}

/** remark plugin entry point. */
export default function remarkLineBreaks() {
  return (tree: MdastNode) => {
    transform(tree);
  };
}
