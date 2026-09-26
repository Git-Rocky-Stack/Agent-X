/**
 * Page Content Extractors
 *
 * Three extraction modes for the AgentX web clipper.
 * All content extraction reads text nodes only (never innerHTML or outerHTML) for security.
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

  if (date) return normalizePublishedDate(date);

  // Try <time> element
  const timeEl = document.querySelector('time[datetime]');
  if (timeEl) {
    const datetime = timeEl.getAttribute('datetime');
    if (datetime) return normalizePublishedDate(datetime);
  }

  return null;
}

function toIsoDate(year: number, month: number, day: number): string | null {
  const date = new Date(Date.UTC(year, month - 1, day));
  if (date.getUTCFullYear() !== year || date.getUTCMonth() !== month - 1 || date.getUTCDate() !== day) {
    return null; // e.g. 2024-13-45 rolled over into another date
  }
  return date.toISOString().slice(0, 10);
}

/**
 * Normalizes a page's published-date string to an ISO 8601 calendar date (YYYY-MM-DD), or null
 * when it cannot be read. Pages publish dates in many shapes ("+0000" offsets,
 * "2024-03-05 10:00:00", "2024-03", "20240305"), and AgentX builds before the lenient server-side
 * parser rejected the whole clip for anything but strict ISO. The date is kept as the page states
 * it: an ISO-style date prefix is used as written instead of being shifted into UTC.
 */
export function normalizePublishedDate(raw: string | null): string | null {
  const value = raw?.trim();
  if (!value) return null;

  const compact = /^(\d{4})(\d{2})(\d{2})$/.exec(value);
  if (compact) {
    return toIsoDate(Number(compact[1]), Number(compact[2]), Number(compact[3]));
  }

  const isoPrefix = /^(\d{4})-(\d{2})(?:-(\d{2}))?(?=$|[T\s])/.exec(value);
  if (isoPrefix) {
    return toIsoDate(Number(isoPrefix[1]), Number(isoPrefix[2]), Number(isoPrefix[3] ?? '1'));
  }

  // Anything else (RFC 1123, "March 5, 2024", ...) must at least name a four-digit year: Date
  // silently invents one (2001) for strings such as "5 March".
  if (!/\d{4}/.test(value)) return null;

  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return null;

  // A string with an explicit zone is read in UTC; one without is local wall time as written.
  const hasZone = /(?:Z|GMT|UTC|[+-]\d{2}:?\d{2})$/i.test(value);
  return hasZone
    ? toIsoDate(parsed.getUTCFullYear(), parsed.getUTCMonth() + 1, parsed.getUTCDate())
    : toIsoDate(parsed.getFullYear(), parsed.getMonth() + 1, parsed.getDate());
}

function countWords(text: string): number {
  return text.trim().split(/\s+/).filter(w => w.length > 0).length;
}

// ── Extractors ─────────────────────────────────────────────────────────────

/**
 * Extract the whole page body as markdown text. This used to send
 * document.documentElement.outerHTML, which saved and indexed scripts, styles, hidden inputs
 * (CSRF tokens) and serialized app state; the page text is converted the same way as reader mode.
 */
export function extractFullPage(): ExtractedPage {
  const content = document.body ? nodeToMarkdown(document.body) : '';
  return {
    title: extractTitle(),
    author: extractAuthor(),
    publishedDate: extractPublishedDate(),
    wordCount: countWords(content),
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

/** Elements whose text is never page content: code, templates, and navigation chrome. */
const SKIPPED_TAGS = new Set(['script', 'style', 'noscript', 'template', 'nav', 'footer']);

/** True for elements whose subtree must not contribute text (skipped tags or hidden). */
function isSkipped(el: Element): boolean {
  if (SKIPPED_TAGS.has(el.tagName.toLowerCase()) || el.hasAttribute('hidden')) return true;
  const style = (el as HTMLElement).style;
  return style?.display === 'none' || style?.visibility === 'hidden';
}

/**
 * The text of an element without the text of skipped descendants. Plain textContent would also
 * return the source of a script or style nested inside a paragraph or list item.
 */
function textOf(el: Element): string {
  const walker = document.createTreeWalker(el, NodeFilter.SHOW_ELEMENT | NodeFilter.SHOW_TEXT, {
    acceptNode(node: Node): number {
      if (node.nodeType === Node.ELEMENT_NODE) {
        return isSkipped(node as Element) ? NodeFilter.FILTER_REJECT : NodeFilter.FILTER_SKIP;
      }
      return NodeFilter.FILTER_ACCEPT;
    },
  });

  const parts: string[] = [];
  for (let node = walker.nextNode(); node; node = walker.nextNode()) {
    parts.push(node.textContent ?? '');
  }
  return parts.join('').trim();
}

/**
 * Walks the DOM tree and produces a simple markdown representation
 * using only text nodes (never innerHTML or outerHTML).
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

    // Skip hidden elements, scripts, styles, templates and navigation chrome
    if (isSkipped(el)) return;

    // Headings
    const headingMatch = tag.match(/^h([1-6])$/);
    if (headingMatch) {
      const level = parseInt(headingMatch[1], 10);
      const prefix = '#'.repeat(level);
      const text = textOf(el);
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
      const text = textOf(el);
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
      const text = textOf(el);
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
      const text = textOf(el);
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
