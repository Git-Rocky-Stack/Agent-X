/**
 * Background Service Worker
 *
 * Orchestrates clipping: receives commands from the popup, injects the extractor into the target
 * tab on demand (chrome.scripting), then posts results to the AgentX API. It is the only context
 * that uses the API token.
 */

import { AgentXApi, ClipRequest } from '../api/agentx-api';
import { ExtractedPage } from '../content/extractors';

const api = new AgentXApi();

// -- API token (pairing) -----------------------------------------------------
// The desktop app requires a per-install bearer token on all data routes. The user pairs by
// pasting it into the popup; the worker validates it against the authenticated /api/auth/check
// route before storing it under chrome.storage.local.apiToken.

const API_TOKEN_KEY = 'apiToken';

async function loadApiToken(): Promise<void> {
  try {
    const stored = await chrome.storage.local.get<{ apiToken?: string }>(API_TOKEN_KEY);
    api.setToken(stored.apiToken ?? null);
  } catch {
    api.setToken(null);
  }
}

// MV3 workers are started on demand, often by the very message that needs the token, so every
// handler awaits this before calling the API (it used to be fire-and-forget, and the first clip
// after a wake-up could go out without a token and fail with 401).
const tokenReady = loadApiToken();

/**
 * Keeps chrome.storage.local (which holds the token) out of reach of content scripts. Local
 * storage is readable from content scripts by default; ours never read it, but a compromised
 * page renderer could. Best effort: browsers without setAccessLevel for this area keep the
 * default, and the extractor still never reads storage.
 */
async function restrictStorageToTrustedContexts(): Promise<void> {
  try {
    if (typeof chrome.storage.local.setAccessLevel === 'function') {
      await chrome.storage.local.setAccessLevel({ accessLevel: 'TRUSTED_CONTEXTS' });
    }
  } catch (err) {
    console.warn('AgentX Clipper: could not restrict storage to trusted contexts', err);
  }
}

void restrictStorageToTrustedContexts();

chrome.storage.onChanged.addListener((changes, areaName) => {
  if (areaName === 'local' && API_TOKEN_KEY in changes) {
    const newValue = changes[API_TOKEN_KEY].newValue;
    api.setToken(typeof newValue === 'string' ? newValue : null);
  }
});

// -- Recent Clips Storage ----------------------------------------------------

interface RecentClip {
  title: string;
  url: string;
  mode: string;
  timestamp: number;
  inboxItemId?: number;
}

const MAX_RECENT_CLIPS = 10;

interface ExtractionErrorResponse {
  error: string;
}

type ExtractPageResponse = ExtractedPage | ExtractionErrorResponse;

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function isExtractedPage(value: unknown): value is ExtractedPage {
  return isRecord(value)
    && typeof value.title === 'string'
    && (typeof value.author === 'string' || value.author === null)
    && (typeof value.publishedDate === 'string' || value.publishedDate === null)
    && typeof value.wordCount === 'number'
    && typeof value.url === 'string'
    && typeof value.content === 'string'
    && (value.clipMode === 'full' || value.clipMode === 'selection' || value.clipMode === 'reader');
}

function isExtractionErrorResponse(value: unknown): value is ExtractionErrorResponse {
  return isRecord(value) && typeof value.error === 'string';
}

function parseExtractPageResponse(value: unknown): ExtractPageResponse | undefined {
  if (isExtractedPage(value) || isExtractionErrorResponse(value)) {
    return value;
  }

  return undefined;
}

async function addRecentClip(clip: RecentClip): Promise<void> {
  const stored = await chrome.storage.local.get<{ recentClips?: RecentClip[] }>('recentClips');
  const clips: RecentClip[] = stored.recentClips ?? [];
  clips.unshift(clip);
  if (clips.length > MAX_RECENT_CLIPS) clips.length = MAX_RECENT_CLIPS;
  await chrome.storage.local.set({ recentClips: clips });
}

// --- Extraction (on-demand injection) ---

/** Thrown when the browser does not let the extension run in a tab. */
class NoAccessError extends Error {}

/**
 * Injects the extractor into a tab and runs it. Works under the activeTab grant for the tab the
 * user clicked the toolbar button on, and in other tabs only with the optional host permission
 * requested by "Clip All Tabs". Browser pages (chrome://, the Web Store, the PDF viewer) never
 * allow injection.
 */
async function extractFromTab(tabId: number, mode: string): Promise<ExtractPageResponse | undefined> {
  let results: chrome.scripting.InjectionResult<unknown>[];
  try {
    await chrome.scripting.executeScript({ target: { tabId }, files: ['content.js'] });
    results = await chrome.scripting.executeScript({
      target: { tabId },
      // Serialized into the page's isolated world: it may only use what content.js defined there.
      func: (clipMode: string): unknown => {
        const clipper = (globalThis as { __agentxClipper?: { extract(m: string): unknown } }).__agentxClipper;
        return clipper ? clipper.extract(clipMode) : { error: 'The clipper did not load on this page.' };
      },
      args: [mode],
    });
  } catch (err) {
    const detail = err instanceof Error ? err.message : String(err);
    throw new NoAccessError(`AgentX cannot read this page (${detail}).`);
  }

  return parseExtractPageResponse(results[0]?.result);
}

// -- Message Handling --------------------------------------------------------

interface ExtensionMessage {
  action: string;
  mode?: string;
  token?: string;
}

chrome.runtime.onMessage.addListener(
  (message: ExtensionMessage, sender: chrome.runtime.MessageSender, sendResponse: (response: unknown) => void) => {
    // Only the extension's own pages (the popup) may drive the worker.
    if (sender.id !== chrome.runtime.id || sender.tab) {
      return false;
    }

    switch (message.action) {
      case 'clipPage':
        void handleClipPage(message.mode ?? 'selection', sendResponse);
        return true; // async

      case 'checkConnection':
        void handleCheckConnection(sendResponse);
        return true; // async

      case 'clipAllTabs':
        void handleClipAllTabs(sendResponse);
        return true; // async

      case 'pair':
        void handlePair(message.token ?? '', sendResponse);
        return true; // async

      default:
        return false;
    }
  }
);

// -- Handlers ---------------------------------------------------------------

function toClipRequest(page: ExtractedPage, clipMode: ClipRequest['clipMode']): ClipRequest {
  return {
    title: page.title,
    content: page.content,
    sourceUrl: page.url,
    author: page.author ?? undefined,
    publishedDate: page.publishedDate ?? undefined,
    clipMode,
    wordCount: page.wordCount,
  };
}

async function handleClipPage(mode: string, sendResponse: (r: unknown) => void): Promise<void> {
  try {
    await tokenReady;

    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    if (!tab?.id) {
      sendResponse({ success: false, error: 'No active tab found.' });
      return;
    }

    const extraction = await extractFromTab(tab.id, mode);

    if (!extraction || isExtractionErrorResponse(extraction)) {
      sendResponse({ success: false, error: extraction?.error ?? 'Extraction returned no data.' });
      return;
    }

    const page = extraction;

    // Check for empty content
    if (!page.content || page.content.trim().length === 0) {
      sendResponse({ success: false, error: 'No content to clip. Select text on the page or use Full Page mode.' });
      return;
    }

    const result = await api.clipToInbox(toClipRequest(page, page.clipMode));

    // Save to recent clips
    await addRecentClip({
      title: page.title,
      url: page.url,
      mode: page.clipMode,
      timestamp: Date.now(),
      inboxItemId: result.inboxItemId,
    });

    sendResponse({ success: true, data: result });
  } catch (err) {
    const errorMsg = err instanceof Error ? err.message : 'Unknown error during clip.';
    sendResponse({ success: false, error: errorMsg });
  }
}

/**
 * Reports whether AgentX is reachable (public probe) and whether the stored token is accepted
 * (authenticated probe), so the popup never shows "connected" for an unpaired or revoked token.
 */
async function handleCheckConnection(sendResponse: (r: unknown) => void): Promise<void> {
  await tokenReady;

  const health = await api.checkHealth().catch(() => null);
  if (!health) {
    sendResponse({ success: false, data: { connected: false, paired: false, version: '' } });
    return;
  }

  const paired = (await api.checkAuth().catch(() => 'rejected' as const)) === 'valid';

  sendResponse({ success: true, data: { ...health, paired } });
}

/**
 * Validates a pasted token against AgentX before storing it. A token AgentX rejects is not
 * stored; one that cannot be checked because AgentX is not running is stored and reported as
 * unverified. An empty token unpairs.
 */
async function handlePair(rawToken: string, sendResponse: (r: unknown) => void): Promise<void> {
  const token = rawToken.trim();

  try {
    if (token.length === 0) {
      await chrome.storage.local.remove(API_TOKEN_KEY);
      api.setToken(null);
      sendResponse({ success: true, data: { status: 'cleared' } });
      return;
    }

    let verdict: 'valid' | 'rejected' | 'unverified';
    try {
      verdict = await api.checkAuth(token);
    } catch {
      verdict = 'unverified';
    }

    if (verdict === 'rejected') {
      sendResponse({
        success: false,
        error: 'AgentX rejected this token. Copy the current token from AgentX > Settings > Connections.',
      });
      return;
    }

    await chrome.storage.local.set({ [API_TOKEN_KEY]: token });
    api.setToken(token);
    sendResponse({ success: true, data: { status: verdict } });
  } catch (err) {
    const errorMsg = err instanceof Error ? err.message : 'Could not save the token.';
    sendResponse({ success: false, error: errorMsg });
  }
}

interface TabClipResult {
  title: string;
  url: string;
  status: 'clipped' | 'skipped' | 'error';
  error?: string;
}

/**
 * Clips every tab in the current window in reader mode and reports every tab: clipped, skipped
 * (the browser does not let the extension read it, or it has no text) or error. The source URL is
 * the one the page itself reports (location.href): without the "tabs" permission, tab.url is
 * hidden for every tab except the active one, which made the old loop skip all other tabs and
 * report "Clipped 1/1".
 */
async function handleClipAllTabs(sendResponse: (r: unknown) => void): Promise<void> {
  try {
    await tokenReady;

    const tabs = await chrome.tabs.query({ currentWindow: true });
    const results: TabClipResult[] = [];

    for (const tab of tabs) {
      const title = tab.title ?? 'Untitled';
      const url = tab.url ?? '';

      if (tab.id === undefined) {
        results.push({ title, url, status: 'skipped', error: 'Tab has no id.' });
        continue;
      }

      if (tab.discarded) {
        results.push({ title, url, status: 'skipped', error: 'Tab is unloaded. Open it, then clip again.' });
        continue;
      }

      try {
        const extraction = await extractFromTab(tab.id, 'reader');

        if (!extraction || isExtractionErrorResponse(extraction) || !extraction.content.trim()) {
          const skippedError = isExtractionErrorResponse(extraction)
            ? extraction.error
            : 'No text to clip.';

          results.push({ title, url, status: 'skipped', error: skippedError });
          continue;
        }

        const page = extraction;
        const result = await api.clipToInbox(toClipRequest(page, 'reader'));

        await addRecentClip({
          title: page.title,
          url: page.url,
          mode: 'reader',
          timestamp: Date.now(),
          inboxItemId: result.inboxItemId,
        });

        results.push({ title: page.title, url: page.url, status: 'clipped' });
      } catch (err) {
        const errorMsg = err instanceof Error ? err.message : 'Unknown error';
        const status = err instanceof NoAccessError ? 'skipped' : 'error';
        results.push({ title, url, status, error: errorMsg });
      }
    }

    sendResponse({ success: true, data: results });
  } catch (err) {
    const errorMsg = err instanceof Error ? err.message : 'Unknown error during batch clip.';
    sendResponse({ success: false, error: errorMsg });
  }
}
