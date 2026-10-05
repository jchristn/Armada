/**
 * Unified diff parser (TypeScript port of Armada.Core UnifiedDiffParser).
 *
 * Hunk bodies are consumed by the line counts in their @@ headers, so content lines that look like headers (for
 * example an added line whose text starts with "++ ", which renders as "+++ ...") are always content. File paths
 * come from the structural headers (diff --git, rename/copy from/to, ---/+++ outside hunks) with C-quoting decoded,
 * so deletions, pure renames, mode-only and binary changes all report their paths.
 */

/** Kind of change for one file section (subset of GitChangeKindEnum the text form can express). */
export type UnifiedDiffChangeKind = 'Modified' | 'Added' | 'Deleted' | 'Renamed' | 'Copied';

/** Classification of one raw diff line. */
export type UnifiedDiffLineKind =
  | 'fileHeader' // diff --git ...
  | 'meta' // extended headers, ---/+++ headers, "\ No newline at end of file"
  | 'hunkHeader' // @@ -a,b +c,d @@
  | 'add' // added line inside a hunk
  | 'del' // deleted line inside a hunk
  | 'context' // context line inside a hunk
  | 'other'; // anything outside a hunk and outside a file header block

/** One classified raw line. Line numbers are set for hunk body lines only. */
export interface UnifiedDiffLine {
  kind: UnifiedDiffLineKind;
  text: string;
  oldNumber: number | null;
  newNumber: number | null;
}

/** One file section of a unified diff. */
export interface UnifiedDiffFileSection {
  /** Display path: the new path, else the old path (for deletions), C-quoting decoded. */
  path: string;
  oldPath: string | null;
  newPath: string | null;
  kind: UnifiedDiffChangeKind;
  additions: number;
  deletions: number;
  isBinary: boolean;
  /** Index of the section's first raw line (inclusive). */
  startLine: number;
  /** Index just past the section's last raw line (exclusive). */
  endLine: number;
}

/** Parse result: file sections plus the classification of every raw line (same indexes as the split text). */
export interface ParsedUnifiedDiff {
  files: UnifiedDiffFileSection[];
  lines: UnifiedDiffLine[];
}

interface MutableSection {
  oldPath: string | null;
  newPath: string | null;
  kind: UnifiedDiffChangeKind;
  additions: number;
  deletions: number;
  isBinary: boolean;
  startLine: number;
}

function trimCr(value: string): string {
  return value.endsWith('\r') ? value.substring(0, value.length - 1) : value;
}

function isOctal(c: string): boolean {
  return c >= '0' && c <= '7';
}

const utf8Encoder = new TextEncoder();
const utf8Decoder = new TextDecoder('utf-8');

/**
 * Read one C-quoted token starting at `start` (which must be the opening quote). Returns the decoded value and
 * the index just past the closing quote, or null when the text is not a well-formed quoted token.
 */
export function readQuotedPath(text: string, start: number): { value: string; end: number } | null {
  if (start < 0 || start >= text.length || text[start] !== '"') return null;
  const bytes: number[] = [];
  let i = start + 1;
  while (i < text.length) {
    const c = text[i];
    if (c === '"') {
      return { value: utf8Decoder.decode(new Uint8Array(bytes)), end: i + 1 };
    }
    if (c !== '\\') {
      const cp = text.codePointAt(i) ?? 0;
      const ch = String.fromCodePoint(cp);
      for (const b of utf8Encoder.encode(ch)) bytes.push(b);
      i += ch.length;
      continue;
    }
    if (i + 1 >= text.length) return null;
    const e = text[i + 1];
    const simple: Record<string, number> = { a: 0x07, b: 0x08, t: 0x09, n: 0x0a, v: 0x0b, f: 0x0c, r: 0x0d, '"': 0x22, '\\': 0x5c };
    if (Object.prototype.hasOwnProperty.call(simple, e)) {
      bytes.push(simple[e]);
      i += 2;
      continue;
    }
    if (i + 3 < text.length && isOctal(e) && isOctal(text[i + 2]) && isOctal(text[i + 3])) {
      const value = (Number(e) * 64) + (Number(text[i + 2]) * 8) + Number(text[i + 3]);
      if (value > 255) return null;
      bytes.push(value);
      i += 4;
      continue;
    }
    return null;
  }
  return null;
}

/** Decode a path token that may be C-quoted; unquoted input is returned unchanged. */
export function unquoteGitPath(token: string): string {
  if (!token) return '';
  if (token.length < 2 || token[0] !== '"' || token[token.length - 1] !== '"') return token;
  const decoded = readQuotedPath(token, 0);
  return decoded && decoded.end === token.length ? decoded.value : token;
}

function normalizePath(path: string): string {
  return path.replace(/\\/g, '/');
}

function stripPrefix(value: string, prefix: string): string {
  return normalizePath(value.startsWith(prefix) ? value.substring(prefix.length) : value);
}

function parseHeaderPath(token: string, prefix: string): string | null {
  let value = token;
  const tab = value.indexOf('\t');
  if (tab >= 0 && !value.startsWith('"')) value = value.substring(0, tab);
  value = unquoteGitPath(value);
  if (value === '/dev/null') return null;
  return stripPrefix(value, prefix);
}

function parseDiffGitHeader(rest: string, file: MutableSection): void {
  let first: string | null = null;
  let second: string | null = null;
  if (rest.startsWith('"')) {
    const quoted = readQuotedPath(rest, 0);
    if (quoted && quoted.end < rest.length && rest[quoted.end] === ' ') {
      first = quoted.value;
      second = unquoteGitPath(rest.substring(quoted.end + 1));
    }
  } else if (rest.endsWith('"')) {
    const openQuote = rest.lastIndexOf(' "');
    if (openQuote > 0) {
      first = rest.substring(0, openQuote);
      second = unquoteGitPath(rest.substring(openQuote + 1));
    }
  } else {
    // Unquoted: when both sides name the same path ("a/P b/P") the split point is exact.
    if (rest.length >= 5 && (rest.length - 1) % 2 === 0) {
      const half = (rest.length - 1) / 2;
      const left = rest.substring(0, half);
      const right = rest.substring(half + 1);
      if (rest[half] === ' ' && left.length > 2 && right.length > 2 && left.substring(2) === right.substring(2)) {
        first = left;
        second = right;
      }
    }
    if (first === null) {
      const split = rest.indexOf(' b/');
      if (split > 0) {
        first = rest.substring(0, split);
        second = rest.substring(split + 1);
      }
    }
  }
  if (first !== null) file.oldPath = stripPrefix(first, 'a/');
  if (second !== null) file.newPath = stripPrefix(second, 'b/');
}

function parseRange(range: string): { start: number; count: number } | null {
  const comma = range.indexOf(',');
  const startText = comma >= 0 ? range.substring(0, comma) : range;
  if (!/^\d+$/.test(startText)) return null;
  let count = 1;
  if (comma >= 0) {
    const countText = range.substring(comma + 1);
    if (!/^\d+$/.test(countText)) return null;
    count = Number(countText);
  }
  return { start: Number(startText), count };
}

/** Parse "@@ -oldStart[,oldCount] +newStart[,newCount] @@ optional section". */
export function parseHunkHeader(line: string): { oldStart: number; oldCount: number; newStart: number; newCount: number } | null {
  if (!line.startsWith('@@ ')) return null;
  const end = line.indexOf(' @@', 3);
  if (end < 0) return null;
  const ranges = line.substring(3, end).split(' ').filter((r) => r.length > 0);
  if (ranges.length !== 2 || !ranges[0].startsWith('-') || !ranges[1].startsWith('+')) return null;
  const oldRange = parseRange(ranges[0].substring(1));
  const newRange = parseRange(ranges[1].substring(1));
  if (!oldRange || !newRange) return null;
  return { oldStart: oldRange.start, oldCount: oldRange.count, newStart: newRange.start, newCount: newRange.count };
}

function applyExtendedHeader(line: string, file: MutableSection): void {
  if (line.startsWith('new file mode ')) {
    file.kind = 'Added';
    file.oldPath = null;
  } else if (line.startsWith('deleted file mode ')) {
    file.kind = 'Deleted';
    file.newPath = null;
  } else if (line.startsWith('rename from ')) {
    file.kind = 'Renamed';
    file.oldPath = normalizePath(unquoteGitPath(line.substring('rename from '.length)));
  } else if (line.startsWith('rename to ')) {
    file.kind = 'Renamed';
    file.newPath = normalizePath(unquoteGitPath(line.substring('rename to '.length)));
  } else if (line.startsWith('copy from ')) {
    file.kind = 'Copied';
    file.oldPath = normalizePath(unquoteGitPath(line.substring('copy from '.length)));
  } else if (line.startsWith('copy to ')) {
    file.kind = 'Copied';
    file.newPath = normalizePath(unquoteGitPath(line.substring('copy to '.length)));
  } else if (line.startsWith('Binary files ') || line === 'GIT binary patch') {
    file.isBinary = true;
  }
}

/** Parse unified diff text into file sections and per-line classifications. */
export function parseUnifiedDiff(diffText: string | null | undefined): ParsedUnifiedDiff {
  if (!diffText) return { files: [], lines: [] };

  const rawLines = diffText.split('\n');
  const lines: UnifiedDiffLine[] = [];
  const sections: MutableSection[] = [];
  let current: MutableSection | null = null;
  let currentHasHunk = false;
  let remainingOld = 0;
  let remainingNew = 0;
  let oldLine = 0;
  let newLine = 0;

  const push = (kind: UnifiedDiffLineKind, text: string, oldNumber: number | null = null, newNumber: number | null = null) => {
    lines.push({ kind, text, oldNumber, newNumber });
  };
  const startSection = (startLine: number): MutableSection => {
    const section: MutableSection = { oldPath: null, newPath: null, kind: 'Modified', additions: 0, deletions: 0, isBinary: false, startLine };
    sections.push(section);
    currentHasHunk = false;
    return section;
  };

  let index = 0;
  while (index < rawLines.length) {
    const raw = rawLines[index];

    if (current && (remainingOld > 0 || remainingNew > 0)) {
      const body = trimCr(raw);
      const marker = body.length === 0 ? ' ' : body[0];
      if (marker === '+' && remainingNew > 0) {
        current.additions++;
        remainingNew--;
        push('add', raw, null, newLine);
        newLine++;
        index++;
        continue;
      }
      if (marker === '-' && remainingOld > 0) {
        current.deletions++;
        remainingOld--;
        push('del', raw, oldLine, null);
        oldLine++;
        index++;
        continue;
      }
      if (marker === ' ' && remainingOld > 0 && remainingNew > 0) {
        remainingOld--;
        remainingNew--;
        push('context', raw, oldLine, newLine);
        oldLine++;
        newLine++;
        index++;
        continue;
      }
      if (marker === '\\') {
        push('meta', raw);
        index++;
        continue;
      }
      // Malformed or truncated hunk: stop consuming and treat this line as a header.
      remainingOld = 0;
      remainingNew = 0;
    }

    const line = trimCr(raw);

    if (line.startsWith('\\')) {
      push('meta', raw);
      index++;
      continue;
    }

    if (line.startsWith('diff --git ')) {
      current = startSection(index);
      parseDiffGitHeader(line.substring('diff --git '.length), current);
      push('fileHeader', raw);
      index++;
      continue;
    }

    if (line.startsWith('--- ') && index + 1 < rawLines.length && rawLines[index + 1].startsWith('+++ ')) {
      // Plain unified diff without a diff --git header, or a second file in one.
      if (!current || currentHasHunk) current = startSection(index);
      const oldPath = parseHeaderPath(trimCr(line.substring(4)), 'a/');
      const newPath = parseHeaderPath(trimCr(rawLines[index + 1].substring(4)), 'b/');
      current.oldPath = oldPath;
      current.newPath = newPath;
      if (oldPath === null && newPath !== null) current.kind = 'Added';
      else if (newPath === null && oldPath !== null) current.kind = 'Deleted';
      push('meta', raw);
      push('meta', rawLines[index + 1]);
      index += 2;
      continue;
    }

    if (!current) {
      push('other', raw);
      index++;
      continue;
    }

    if (line.startsWith('@@ ')) {
      const hunk = parseHunkHeader(line);
      if (hunk) {
        remainingOld = hunk.oldCount;
        remainingNew = hunk.newCount;
        oldLine = hunk.oldStart;
        newLine = hunk.newStart;
        currentHasHunk = true;
      }
      push('hunkHeader', raw);
      index++;
      continue;
    }

    if (!currentHasHunk) {
      applyExtendedHeader(line, current);
      push('meta', raw);
    } else {
      push('other', raw);
    }
    index++;
  }

  const files: UnifiedDiffFileSection[] = sections.map((s, i) => ({
    path: s.newPath ?? s.oldPath ?? '',
    oldPath: s.oldPath,
    newPath: s.newPath,
    kind: s.kind,
    additions: s.additions,
    deletions: s.deletions,
    isBinary: s.isBinary,
    startLine: s.startLine,
    endLine: i + 1 < sections.length ? sections[i + 1].startLine : rawLines.length,
  }));

  return { files, lines };
}
