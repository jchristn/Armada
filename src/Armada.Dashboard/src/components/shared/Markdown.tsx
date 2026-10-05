import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import remarkLineBreaks from '../../lib/remarkLineBreaks';

interface MarkdownProps {
  children: string;
  className?: string;
  /** Keep single newlines as line breaks (for line-oriented text such as logs). */
  preserveLineBreaks?: boolean;
}

/**
 * Render markdown text (GitHub-flavored) as sanitized React elements. Used for chat/assistant
 * content so captain replies show headings, lists, code blocks, tables, and links instead of raw
 * markdown source. Links open in a new tab.
 */
export default function Markdown({ children, className, preserveLineBreaks }: MarkdownProps) {
  return (
    <div className={`markdown${className ? ` ${className}` : ''}`}>
      <ReactMarkdown
        remarkPlugins={preserveLineBreaks ? [remarkGfm, remarkLineBreaks] : [remarkGfm]}
        components={{
          a: ({ node: _node, ...props }) => <a {...props} target="_blank" rel="noopener noreferrer" />,
        }}
      >
        {children}
      </ReactMarkdown>
    </div>
  );
}
