/**
 * Background Service Worker
 *
 * Orchestrates clipping: receives commands from the popup, sends extraction
 * requests to content scripts, then posts results to the AgentX API.
 */

import { AgentXApi, ClipRequest, ExtensionHealthResponse } from '../api/agentx-api';
import { ExtractedPage } from '../content/extractors';

const api = new AgentXApi();

// ── Recent Clips Storage ────────────────────────────────────────────────────

interface RecentClip {
  title: string;
  url: string;
  mode: string;
  timestamp: number;
  inboxItemId?: number;
}

const MAX_RECENT_CLIPS = 10;

async function addRecentClip(clip: RecentClip): Promise<void> {
  const stored = await chrome.storage.local.get('recentClips') as { recentClips?: RecentClip[] };
  const clips: RecentClip[] = stored.recentClips ?? [];
  clips.unshift(clip);
  if (clips.length > MAX_RECENT_CLIPS) clips.length = MAX_RECENT_CLIPS;
  await chrome.storage.local.set({ recentClips: clips });
}

// ── Message Handling ────────────────────────────────────────────────────────

chrome.runtime.onMessage.addListener(
  (message: { action: string; mode?: string }, _sender: chrome.runtime.MessageSender, sendResponse: (response: unknown) => void) => {
    switch (message.action) {
      case 'clipPage':
        handleClipPage(message.mode ?? 'selection', sendResponse);
        return true; // async

      case 'checkConnection':
        handleCheckConnection(sendResponse);
        return true; // async

      case 'clipAllTabs':
        handleClipAllTabs(sendResponse);
        return true; // async

      default:
        return false;
    }
  }
);

// ── Handlers ───────────────────────────────────────────────────────────────

async function handleClipPage(mode: string, sendResponse: (r: unknown) => void): Promise<void> {
  try {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    if (!tab?.id) {
      sendResponse({ success: false, error: 'No active tab found.' });
      return;
    }

    // Send extraction request to content script
    const extraction = await chrome.tabs.sendMessage(tab.id, { action: 'extractPage', mode });

    if (!extraction || extraction.error) {
      sendResponse({ success: false, error: extraction?.error ?? 'Extraction returned no data.' });
      return;
    }

    const page = extraction as ExtractedPage;

    // Check for empty content
    if (!page.content || page.content.trim().length === 0) {
      sendResponse({ success: false, error: 'No content to clip. Select text on the page or use Full Page mode.' });
      return;
    }

    // Build the API request matching ApiClipRequest
    const clip: ClipRequest = {
      title: page.title,
      content: page.content,
      sourceUrl: page.url,
      author: page.author ?? undefined,
      publishedDate: page.publishedDate ?? undefined,
      clipMode: page.clipMode,
      wordCount: page.wordCount,
    };

    const result = await api.clipToInbox(clip);

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

async function handleCheckConnection(sendResponse: (r: unknown) => void): Promise<void> {
  try {
    const health = await api.checkHealth();
    sendResponse({ success: true, data: health });
  } catch {
    sendResponse({ success: false, data: { connected: false, version: '', inboxEnabled: false, provider: '' } });
  }
}

async function handleClipAllTabs(sendResponse: (r: unknown) => void): Promise<void> {
  try {
    const tabs = await chrome.tabs.query({ currentWindow: true });

    const results: { title: string; url: string; status: string; error?: string }[] = [];

    for (const tab of tabs) {
      if (!tab.id || !tab.url || tab.url.startsWith('chrome://') || tab.url.startsWith('chrome-extension://')) {
        continue;
      }

      try {
        const extraction = await chrome.tabs.sendMessage(tab.id, { action: 'extractPage', mode: 'reader' });

        if (!extraction || extraction.error || !extraction.content?.trim()) {
          results.push({ title: tab.title ?? 'Untitled', url: tab.url, status: 'skipped', error: extraction?.error ?? 'Empty content' });
          continue;
        }

        const page = extraction as ExtractedPage;

        const clip: ClipRequest = {
          title: page.title,
          content: page.content,
          sourceUrl: page.url,
          author: page.author ?? undefined,
          publishedDate: page.publishedDate ?? undefined,
          clipMode: 'reader',
          wordCount: page.wordCount,
        };

        const result = await api.clipToInbox(clip);

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
        results.push({ title: tab.title ?? 'Untitled', url: tab.url ?? '', status: 'error', error: errorMsg });
      }
    }

    sendResponse({ success: true, data: results });
  } catch (err) {
    const errorMsg = err instanceof Error ? err.message : 'Unknown error during batch clip.';
    sendResponse({ success: false, error: errorMsg });
  }
}