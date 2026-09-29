/**
 * Content Script - runs in the extension's isolated world of the page being clipped.
 *
 * Not declared in manifest.json: the background service worker injects this file on demand with
 * chrome.scripting.executeScript (under activeTab, or an optional host permission for "Clip All
 * Tabs"), so it also works in tabs that were already open when the extension was installed or
 * updated. Injecting it defines one extraction entry point; the worker then calls
 * globalThis.__agentxClipper.extract(mode) and receives the result as the injection result.
 *
 * This script never touches chrome.storage: the API token stays in the service worker.
 */

import { extractFullPage, extractSelection, extractReaderMode, ExtractedPage } from './extractors';

interface ClipperEntryPoint {
  extract(mode: string): ExtractedPage | { error: string };
}

function extract(mode: string): ExtractedPage | { error: string } {
  try {
    switch (mode) {
      case 'full':
        return extractFullPage();
      case 'reader':
        return extractReaderMode();
      case 'selection':
      default:
        return extractSelection();
    }
  } catch (err) {
    return { error: err instanceof Error ? err.message : 'Unknown extraction error' };
  }
}

// Re-injection on a later clip is harmless: the entry point is simply replaced.
(globalThis as { __agentxClipper?: ClipperEntryPoint }).__agentxClipper = { extract };
