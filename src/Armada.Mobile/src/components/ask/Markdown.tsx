import { useRouter, type Href } from 'expo-router';
import { lexer, type Token, type Tokens } from 'marked';
import { memo, useMemo, type ReactNode } from 'react';
import { Linking, ScrollView, StyleSheet, Text, View, type TextStyle } from 'react-native';
import { appPathFromLink } from '../../navigation/deepLinks';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing, typography } from '../../theme/typography';
import type { Palette } from '../../theme/palette';

/**
 * GitHub-flavored Markdown for captain replies, summaries, and milestones: the mobile form of the dashboard's
 * Markdown component (react-markdown + remark-gfm). Headings, paragraphs, emphasis, code spans and blocks, lists
 * (including task lists), block quotes, tables, rules, and links. Raw HTML is dropped, as react-markdown drops it.
 * Links to Armada pages (relative paths, armada:// links, or dashboard URLs on any host) open in the app; other
 * http(s) and mailto links open outside it; anything else is shown as text.
 */

const ENTITIES: Record<string, string> = { amp: '&', lt: '<', gt: '>', quot: '"', apos: "'", nbsp: ' ', '#39': "'" };

/** Decode the HTML entities the Markdown lexer leaves in text (named, decimal, and hex). */
export function decodeEntities(text: string): string {
  return text.replace(/&(#x[0-9a-f]+|#[0-9]+|[a-z]+);/gi, (match, name: string) => {
    const lower = name.toLowerCase();
    if (ENTITIES[lower] !== undefined) return ENTITIES[lower];
    if (lower.startsWith('#x')) {
      const code = parseInt(lower.slice(2), 16);
      return Number.isFinite(code) && code > 0 && code <= 0x10ffff ? String.fromCodePoint(code) : match;
    }
    if (lower.startsWith('#')) {
      const code = parseInt(lower.slice(1), 10);
      return Number.isFinite(code) && code > 0 && code <= 0x10ffff ? String.fromCodePoint(code) : match;
    }
    return match;
  });
}

/** Where a Markdown link goes: an in-app path, an external URL, or nowhere (rendered as plain text). */
export type LinkTarget = { kind: 'app'; path: string } | { kind: 'external'; url: string } | { kind: 'none' };

/** Classify a link. Dashboard URLs and app paths stay in the app; http(s) and mailto open outside. */
export function linkTarget(href: string | null | undefined): LinkTarget {
  const value = (href ?? '').trim();
  if (!value) return { kind: 'none' };
  if (/^mailto:/i.test(value)) return { kind: 'external', url: value };
  if (/^armada:/i.test(value) || value.startsWith('/')) {
    const path = appPathFromLink(value);
    return path ? { kind: 'app', path } : { kind: 'none' };
  }
  if (/^https?:\/\//i.test(value)) {
    if (/^https?:\/\/[^/?#]*\/dashboard(\/|$|\?)/i.test(value)) {
      const path = appPathFromLink(value);
      if (path) return { kind: 'app', path };
    }
    return { kind: 'external', url: value };
  }
  return { kind: 'none' };
}

interface RenderContext {
  colors: Palette;
  preserveLineBreaks: boolean;
  onLink: (target: LinkTarget) => void;
}

function inlineText(raw: string, ctx: RenderContext): string {
  const decoded = decodeEntities(raw);
  return ctx.preserveLineBreaks ? decoded : decoded.replace(/\n/g, ' ');
}

function renderInline(tokens: Token[] | undefined, ctx: RenderContext, keyPrefix: string): ReactNode[] {
  if (!tokens) return [];
  return tokens.map((token, index) => renderInlineToken(token, ctx, `${keyPrefix}.${index}`));
}

function renderInlineToken(token: Token, ctx: RenderContext, key: string): ReactNode {
  switch (token.type) {
    case 'text':
    case 'escape': {
      const t = token as Tokens.Text;
      if (t.tokens && t.tokens.length > 0) return <Text key={key}>{renderInline(t.tokens, ctx, key)}</Text>;
      return <Text key={key}>{inlineText(t.text, ctx)}</Text>;
    }
    case 'strong':
      return <Text key={key} style={styles.strong}>{renderInline((token as Tokens.Strong).tokens, ctx, key)}</Text>;
    case 'em':
      return <Text key={key} style={styles.em}>{renderInline((token as Tokens.Em).tokens, ctx, key)}</Text>;
    case 'del':
      return <Text key={key} style={styles.del}>{renderInline((token as Tokens.Del).tokens, ctx, key)}</Text>;
    case 'codespan':
      return (
        <Text key={key} style={[typography.mono, { backgroundColor: ctx.colors.surfaceRaised, color: ctx.colors.text }]}>
          {decodeEntities((token as Tokens.Codespan).text)}
        </Text>
      );
    case 'br':
      return <Text key={key}>{'\n'}</Text>;
    case 'link': {
      const link = token as Tokens.Link;
      const target = linkTarget(link.href);
      const children = renderInline(link.tokens, ctx, key);
      if (target.kind === 'none') return <Text key={key}>{children}</Text>;
      return (
        <Text
          key={key}
          accessibilityRole="link"
          style={[styles.link, { color: ctx.colors.primary }]}
          onPress={() => ctx.onLink(target)}
        >
          {children}
        </Text>
      );
    }
    case 'image': {
      const image = token as Tokens.Image;
      const target = linkTarget(image.href);
      const label = image.text || image.href;
      if (target.kind === 'none') return <Text key={key}>{label}</Text>;
      return (
        <Text key={key} accessibilityRole="link" style={[styles.link, { color: ctx.colors.primary }]} onPress={() => ctx.onLink(target)}>
          {label}
        </Text>
      );
    }
    case 'html':
      return null;
    default: {
      const generic = token as Tokens.Generic;
      if (generic.tokens) return <Text key={key}>{renderInline(generic.tokens, ctx, key)}</Text>;
      return typeof generic.text === 'string' ? <Text key={key}>{inlineText(generic.text, ctx)}</Text> : null;
    }
  }
}

const HEADING_SIZES: TextStyle[] = [
  { fontSize: 22, lineHeight: 28, fontWeight: '700' },
  { fontSize: 20, lineHeight: 26, fontWeight: '700' },
  { fontSize: 18, lineHeight: 24, fontWeight: '600' },
  { fontSize: 16, lineHeight: 22, fontWeight: '600' },
];

function renderBlocks(tokens: Token[], ctx: RenderContext, keyPrefix: string): ReactNode[] {
  const out: ReactNode[] = [];
  tokens.forEach((token, index) => {
    const node = renderBlock(token, ctx, `${keyPrefix}.${index}`);
    if (node !== null) out.push(node);
  });
  return out;
}

function renderBlock(token: Token, ctx: RenderContext, key: string): ReactNode {
  const { colors } = ctx;
  switch (token.type) {
    case 'space':
    case 'def':
    case 'html':
      return null;
    case 'heading': {
      const heading = token as Tokens.Heading;
      const size = HEADING_SIZES[Math.min(heading.depth, HEADING_SIZES.length) - 1];
      return (
        <Text key={key} accessibilityRole="header" style={[size, styles.block, { color: colors.text }]}>
          {renderInline(heading.tokens, ctx, key)}
        </Text>
      );
    }
    case 'paragraph':
      return (
        <Text key={key} style={[typography.body, styles.block, { color: colors.text }]}>
          {renderInline((token as Tokens.Paragraph).tokens, ctx, key)}
        </Text>
      );
    case 'text': {
      const text = token as Tokens.Text;
      return (
        <Text key={key} style={[typography.body, { color: colors.text }]}>
          {text.tokens ? renderInline(text.tokens, ctx, key) : inlineText(text.text, ctx)}
        </Text>
      );
    }
    case 'code': {
      const code = token as Tokens.Code;
      return (
        <ScrollView
          key={key}
          horizontal
          style={[styles.block, styles.codeBlock, { backgroundColor: colors.surfaceRaised, borderColor: colors.border }]}
          contentContainerStyle={styles.codeContent}
        >
          <Text selectable style={[typography.mono, { color: colors.text }]}>{code.text}</Text>
        </ScrollView>
      );
    }
    case 'blockquote':
      return (
        <View key={key} style={[styles.block, styles.quote, { borderLeftColor: colors.border }]}>
          {renderBlocks((token as Tokens.Blockquote).tokens, ctx, key)}
        </View>
      );
    case 'hr':
      return <View key={key} style={[styles.block, styles.hr, { backgroundColor: colors.border }]} />;
    case 'list': {
      const list = token as Tokens.List;
      const start = typeof list.start === 'number' ? list.start : 1;
      return (
        <View key={key} style={styles.block} accessibilityRole="list">
          {list.items.map((item, i) => {
            const checkbox = item.task ? (item.checked ? '☑' : '☐') : null;
            const bullet = checkbox ?? (list.ordered ? `${start + i}.` : '•');
            const body = item.tokens.filter((t) => t.type !== 'checkbox');
            return (
              <View key={`${key}.${i}`} style={styles.listItem}>
                <Text
                  style={[typography.body, styles.bullet, { color: colors.textMuted }]}
                  accessibilityLabel={item.task ? (item.checked ? 'checked' : 'unchecked') : undefined}
                >
                  {bullet}
                </Text>
                <View style={styles.listBody}>{renderBlocks(body, ctx, `${key}.${i}`)}</View>
              </View>
            );
          })}
        </View>
      );
    }
    case 'table': {
      const table = token as Tokens.Table;
      const cell = (c: Tokens.TableCell, i: number, header: boolean) => (
        <View
          key={i}
          style={[styles.cell, { borderColor: colors.border, backgroundColor: header ? colors.surfaceRaised : 'transparent' }]}
        >
          <Text
            style={[typography.body, { color: colors.text, textAlign: c.align ?? 'left' }, header ? styles.strong : null]}
          >
            {renderInline(c.tokens, ctx, `${key}.${header ? 'h' : 'r'}${i}`)}
          </Text>
        </View>
      );
      return (
        <ScrollView key={key} horizontal style={styles.block}>
          <View style={[styles.table, { borderColor: colors.border }]}>
            <View style={styles.row}>{table.header.map((c, i) => cell(c, i, true))}</View>
            {table.rows.map((row, r) => (
              <View key={r} style={styles.row}>{row.map((c, i) => cell(c, i, false))}</View>
            ))}
          </View>
        </ScrollView>
      );
    }
    default: {
      const generic = token as Tokens.Generic;
      if (generic.tokens) return <Text key={key} style={[typography.body, { color: colors.text }]}>{renderInline(generic.tokens, ctx, key)}</Text>;
      return null;
    }
  }
}

export interface MarkdownProps {
  children: string;
  /** Keep single newlines as line breaks (line-oriented text such as logs). */
  preserveLineBreaks?: boolean;
  testID?: string;
}

/** Render Markdown text as native views. Parsing is memoized per text. */
export const Markdown = memo(function Markdown({ children, preserveLineBreaks = false, testID }: MarkdownProps) {
  const { colors } = useTheme();
  const router = useRouter();
  const tokens = useMemo(() => {
    try {
      return lexer(children ?? '', { gfm: true });
    } catch {
      return null;
    }
  }, [children]);

  const ctx: RenderContext = {
    colors,
    preserveLineBreaks,
    onLink: (target) => {
      if (target.kind === 'app') router.push(target.path as Href);
      else if (target.kind === 'external') void Linking.openURL(target.url).catch(() => undefined);
    },
  };

  if (!tokens) return <Text testID={testID} style={[typography.body, { color: colors.text }]}>{children}</Text>;
  return <View testID={testID} style={styles.root}>{renderBlocks(tokens, ctx, 'md')}</View>;
});

const styles = StyleSheet.create({
  root: { gap: spacing.sm },
  block: {},
  strong: { fontWeight: '700' },
  em: { fontStyle: 'italic' },
  del: { textDecorationLine: 'line-through' },
  link: { textDecorationLine: 'underline' },
  codeBlock: { borderWidth: StyleSheet.hairlineWidth, borderRadius: radius.sm },
  codeContent: { padding: spacing.sm },
  quote: { borderLeftWidth: 3, paddingLeft: spacing.md, gap: spacing.xs },
  hr: { height: StyleSheet.hairlineWidth, marginVertical: spacing.xs },
  listItem: { flexDirection: 'row', gap: spacing.sm },
  bullet: { minWidth: 18 },
  listBody: { flex: 1, gap: spacing.xs },
  table: { borderWidth: StyleSheet.hairlineWidth, borderRadius: radius.sm, overflow: 'hidden' },
  row: { flexDirection: 'row' },
  cell: { minWidth: 96, maxWidth: 280, padding: spacing.sm, borderWidth: StyleSheet.hairlineWidth },
});
