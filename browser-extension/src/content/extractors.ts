/**
 * Page Content Extractors
 *
 * Three extraction modes for the AgentX web clipper.
 * All content extraction uses textContent (never innerHTML) for security.
 */

export interface ExtractedPage {
  title: string;
  author: string | null;
  publishedDate: string | null;
  wordCount: number;
  url: string;
  content: string;
  clipMode: 'full' | 'selection' | 'reader';
}

// ── Metadata helpers ────────────────────────────────────────────────────────

function getMetaContent(name: string): string | null {
  // Try <meta name="..."> first, then <meta property="..."> (Open Graph)
  const byName = document.querySelector<HTMLMetaElement>(`meta[name="${name}"]`);
  if (byName?.content) return byName.content;

  const byProperty = document.querySelector<HTMLMetaElement>(`meta[property="${name}"]`);
  if (byProperty?.content) return byProperty.content;

  return null;
}

function extractTitle(): string {
  return document.title || getMetaContent('og:title') || getMetaContent('twitter:title') || 'Untitled';
}

function extractAuthor(): string | null {
  return getMetaContent('author') ?? getMetaContent('article:author') ?? null;
}

function extractPublishedDate(): string | null {
  const date = getMetaContent('article:published_time')
    ?? getMetaContent('date')
    ?? getMetaContent('pubdate')
    ?? getMetaContent('datePublished');

  if (date) return date;

  // Try <time> element
  const timeEl = document.querySelector('time[datetime]');
  if (timeEl) {
    const datetime = timeEl.getAttribute('datetime');
    if (datetime) return datetime;
  }

  return null;
}

function countWords(text: string): number {
  return text.trim().split(/\s+/).filter(w => w.length > 0).length;
}

// ── Extractors ─────────────────────────────────────────────────────────────

/** Extract the full page HTML. */
export function extractFullPage(): ExtractedPage {
  const content = document.documentElement.outerHTML;
  return {
    title: extractTitle(),
    author: extractAuthor(),
    publishedDate: extractPublishedDate(),
    wordCount: countWords(document.body?.textContent ?? ''),
    url: location.href,
    content,
    clipMode: 'full',
  };
}

/** Extract the user's current text selection. */
export function extractSelection(): ExtractedPage {
  const selection = window.getSelection();
  const content = selection?.toString().trim() ?? '';

  if (!content) {
    return {
      title: extractTitle(),
      author: extractAuthor(),
      publishedDate: extractPublishedDate(),
      wordCount: 0,
      url: location.href,
      content: '',
      clipMode: 'selection',
    };
  }

  return {
    title: extractTitle(),
    author: extractAuthor(),
    publishedDate: extractPublishedDate(),
    wordCount: countWords(content),
    url: location.href,
    content,
    clipMode: 'selection',
  };
}

/** Extract article content in reader mode (plaintext to markdown). */
export function extractReaderMode(): ExtractedPage {
  // Attempt to find the main article container
  const article = document.querySelector('article')
    ?? document.querySelector('[role="main"]')
    ?? document.querySelector('main')
    ?? document.body;

  if (!article) {
    return {
      title: extractTitle(),
      author: extractAuthor(),
      publishedDate: extractPublishedDate(),
      wordCount: 0,
      url: location.href,
      content: '',
      clipMode: 'reader',
    };
  }

  // Convert textContent to markdown-like structure
  // Using textContent (NOT innerHTML) per security requirement
  const markdown = nodeToMarkdown(article);

  return {
    title: extractTitle(),
    author: extractAuthor(),
    publishedDate: extractPublishedDate(),
    wordCount: countWords(markdown),
    url: location.href,
    content: markdown,
    clipMode: 'reader',
  };
}

// ── Markdown conversion ─────────────────────────────────────────────────────

/**
 * Walks the DOM tree and produces a simple markdown representation
 * using only textContent — never reads innerHTML.
 */
function nodeToMarkdown(root: Node): string {
  const lines: string[] = [];
  let lastWasBlank = false;

  function walk(node: Node, depth: number): void {
    if (node.nodeType === Node.TEXT_NODE) {
      const text = node.textContent?.trim() ?? '';
      if (text) {
        lines.push(text);
        lastWasBlank = false;
      }
      return;
    }

    if (node.nodeType !== Node.ELEMENT_NODE) return;

    const el = node as Element;
    const tag = el.tagName.toLowerCase();

    // Skip hidden elements and scripts
    const style = (el as HTMLElement).style;
    if (style?.display === 'none' || style?.visibility === 'hidden') return;
    if (tag === 'script' || tag === 'style' || tag === 'noscript' || tag === 'nav' || tag === 'footer') return;

    // Headings
    const headingMatch = tag.match(/^h([1-6])$/);
    if (headingMatch) {
      const level = parseInt(headingMatch[1], 10);
      const prefix = '#'.repeat(level);
      const text = (el.textContent ?? '').trim();
      if (text) {
        lines.push('');
        lines.push(`${prefix} ${text}`);
        lines.push('');
        lastWasBlank = true;
      }
      return;
    }

    // Paragraphs — add blank line separation
    if (tag === 'p') {
      const text = (el.textContent ?? '').trim();
      if (text) {
        if (!lastWasBlank) lines.push('');
        lines.push(text);
        lines.push('');
        lastWasBlank = true;
      }
      return;
    }

    // List items
    if (tag === 'li') {
      const text = (el.textContent ?? '').trim();
      if (text) {
        lines.push(`- ${text}`);
        lastWasBlank = false;
      }
      return;
    }

    // Line breaks
    if (tag === 'br') {
      lines.push('');
      lastWasBlank = true;
      return;
    }

    // Block-level elements get separation
    const blockTags = ['div', 'section', 'blockquote', 'pre', 'figure', 'figcaption', 'aside', 'details', 'summary'];
    if (blockTags.includes(tag)) {
      const text = (el.textContent ?? '').trim();
      if (text) {
        if (!lastWasBlank) lines.push('');
        // Recurse into children for better structure
        for (const child of Array.from(el.childNodes)) {
          walk(child, depth + 1);
        }
        if (!lastWasBlank) lines.push('');
        lastWasBlank = true;
      }
      return;
    }

    // For all other elements, recurse into children
    for (const child of Array.from(el.childNodes)) {
      walk(child, depth + 1);
    }
  }

  walk(root, 0);

  // Collapse excessive blank lines and trim
  return lines
    .join('\n')
    .replace(/\n{3,}/g, '\n\n')
    .trim();
}
