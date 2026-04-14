/**
 * Content Script — runs in the page context.
 *
 * Listens for messages from the background service worker and popup,
 * dispatches to the appropriate extractor, and returns the result.
 */

import { extractFullPage, extractSelection, extractReaderMode, ExtractedPage } from './extractors';

chrome.runtime.onMessage.addListener(
  (message: { action: string }, _sender: chrome.runtime.MessageSender, sendResponse: (response: ExtractedPage | { error: string }) => void) => {
    if (message.action !== 'extractPage') return false;

    try {
      let result: ExtractedPage;

      // The mode is passed via message; default to selection
      const mode = (message as { action: string; mode?: string }).mode ?? 'selection';

      switch (mode) {
        case 'full':
          result = extractFullPage();
          break;
        case 'reader':
          result = extractReaderMode();
          break;
        case 'selection':
        default:
          result = extractSelection();
          break;
      }

      sendResponse(result);
    } catch (err) {
      const errorMsg = err instanceof Error ? err.message : 'Unknown extraction error';
      sendResponse({ error: errorMsg });
    }

    // Return true to indicate async response (though our extractors are sync)
    return true;
  }
);