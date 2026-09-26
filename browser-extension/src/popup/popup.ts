/**
 * Popup Script — UI logic for the AgentX Web Clipper popup.
 *
 * Security: ALL dynamic content uses textContent or DOM APIs.
 * NEVER uses innerHTML, eval(), or Function() constructor.
 */

// ── DOM References ──────────────────────────────────────────────────────────

const statusDot = document.getElementById('statusDot');
const statusText = document.getElementById('statusText');
const feedbackArea = document.getElementById('feedbackArea');
const clipList = document.getElementById('clipList');

const clipFullBtn = getButton('clipFull');
const clipSelectionBtn = getButton('clipSelection');
const clipReaderBtn = getButton('clipReader');
const clipAllTabsBtn = getButton('clipAllTabs');
const saveTokenBtn = getButton('saveToken');

const API_TOKEN_KEY = 'apiToken';

// ── Types ──────────────────────────────────────────────────────────────────

interface RecentClip {
  title: string;
  url: string;
  mode: string;
  timestamp: number;
  inboxItemId?: number;
}

// ── Initialization ──────────────────────────────────────────────────────────

document.addEventListener('DOMContentLoaded', () => {
  void checkConnection();
  void loadRecentClips();
  void loadApiToken();
  bindEvents();
});

function bindEvents(): void {
  if (clipFullBtn) clipFullBtn.addEventListener('click', () => clipPage('full'));
  if (clipSelectionBtn) clipSelectionBtn.addEventListener('click', () => clipPage('selection'));
  if (clipReaderBtn) clipReaderBtn.addEventListener('click', () => clipPage('reader'));
  if (clipAllTabsBtn) clipAllTabsBtn.addEventListener('click', clipAllTabs);
  if (saveTokenBtn) saveTokenBtn.addEventListener('click', saveApiToken);
}

// ── Pairing (API token) ──────────────────────────────────────────────────────

function getTokenInput(): HTMLInputElement | null {
  const element = document.getElementById('apiToken');
  return element instanceof HTMLInputElement ? element : null;
}

async function loadApiToken(): Promise<void> {
  const input = getTokenInput();
  if (!input) return;

  try {
    const stored = await chrome.storage.local.get<{ apiToken?: string }>(API_TOKEN_KEY);
    if (stored.apiToken) input.value = stored.apiToken;
  } catch {
    // Non-critical — leave the field empty.
  }
}

/**
 * Pairs by asking the background worker to validate the token against AgentX's authenticated
 * /api/auth/check route. "Paired" is only shown for a token AgentX accepted; the public health
 * probe used before accepts any string.
 */
async function saveApiToken(): Promise<void> {
  const input = getTokenInput();
  if (!input) return;

  try {
    const response = await sendMessage({ action: 'pair', token: input.value });

    if (!response?.success) {
      showFeedback(response?.error ?? 'Could not save the token.', 'error');
    } else if (response.data?.status === 'cleared') {
      showFeedback('Token cleared. The extension is unpaired.', 'info');
    } else if (response.data?.status === 'unverified') {
      showFeedback('Token saved, but not verified: AgentX is not running. Start AgentX and check the status above.', 'info');
    } else {
      showFeedback('Paired with AgentX.', 'success');
    }

    // Re-probe so the status reflects the new pairing immediately.
    await checkConnection();
  } catch {
    showFeedback('Could not save the token.', 'error');
  }
}

// ── Connection Check ───────────────────────────────────────────────────────

async function checkConnection(): Promise<void> {
  if (!statusDot || !statusText) return;

  try {
    const response = await sendMessage({ action: 'checkConnection' });
    if (response?.success && response.data?.connected && response.data.paired) {
      statusDot.classList.add('connected');
      statusDot.classList.remove('disconnected');
      statusText.textContent = `v${response.data.version ?? '\u2014'}`;
    } else if (response?.success && response.data?.connected) {
      // Reachable, but the stored token is missing, wrong, or was regenerated in AgentX.
      setDisconnected('Not paired');
    } else {
      setDisconnected('Offline');
    }
  } catch {
    setDisconnected('Offline');
  }
}

function setDisconnected(label: string): void {
  if (!statusDot || !statusText) return;
  statusDot.classList.add('disconnected');
  statusDot.classList.remove('connected');
  statusText.textContent = label;
}

// ── Clip Page ──────────────────────────────────────────────────────────────

async function clipPage(mode: string): Promise<void> {
  setButtonsEnabled(false);
  showFeedback('Clipping...', 'info');

  try {
    const response = await sendMessage({ action: 'clipPage', mode });

    if (response?.success) {
      showFeedback(`Clipped as inbox item #${response.data?.inboxItemId ?? ''}`, 'success');
      await loadRecentClips();
    } else {
      showFeedback(response?.error ?? 'Clip failed.', 'error');
    }
  } catch (err) {
    const msg = err instanceof Error ? err.message : 'Unknown error';
    showFeedback(msg, 'error');
  } finally {
    setButtonsEnabled(true);
  }
}

// ── Clip All Tabs ─────────────────────────────────────────────────────────

/** Hosts "Clip All Tabs" needs to read tabs other than the one the toolbar button was clicked on. */
const ALL_SITES = { origins: ['http://*/*', 'https://*/*'] };

/**
 * Asks for the optional all-sites host permission. It is the first await in the click handler so
 * it still runs inside the user gesture the browser requires; a permission that is already granted
 * resolves without a prompt. If the user declines, only tabs the extension may already read are
 * clipped and the rest are reported as skipped.
 */
async function requestAllSitesAccess(): Promise<void> {
  try {
    await chrome.permissions.request(ALL_SITES);
  } catch {
    // Unavailable in this browser: carry on with whatever access the extension already has.
  }
}

async function clipAllTabs(): Promise<void> {
  setButtonsEnabled(false);

  try {
    await requestAllSitesAccess();
    showFeedback('Clipping all tabs...', 'info');

    const response = await sendMessage({ action: 'clipAllTabs' });

    if (response?.success && Array.isArray(response.data)) {
      const results = response.data as { status: string }[];
      const clipped = results.filter(r => r.status === 'clipped').length;
      const skipped = results.filter(r => r.status === 'skipped').length;
      const failed = results.filter(r => r.status === 'error').length;

      const details: string[] = [];
      if (skipped > 0) details.push(`${skipped} skipped (no access or no text)`);
      if (failed > 0) details.push(`${failed} failed`);
      const suffix = details.length > 0 ? ` ${details.join(', ')}.` : '';

      showFeedback(
        `Clipped ${clipped} of ${results.length} tabs.${suffix}`,
        clipped > 0 && failed === 0 ? 'success' : clipped > 0 ? 'info' : 'error'
      );
      await loadRecentClips();
    } else {
      showFeedback(response?.error ?? 'Batch clip failed.', 'error');
    }
  } catch (err) {
    const msg = err instanceof Error ? err.message : 'Unknown error';
    showFeedback(msg, 'error');
  } finally {
    setButtonsEnabled(true);
  }
}

// ── Feedback Display ───────────────────────────────────────────────────────

function showFeedback(message: string, type: 'success' | 'error' | 'info'): void {
  if (!feedbackArea) return;

  // Keep the live region's politeness in sync with severity so screen readers
  // interrupt for errors but announce successes/info politely (AX-QA-015).
  feedbackArea.setAttribute('role', type === 'error' ? 'alert' : 'status');
  feedbackArea.setAttribute('aria-live', type === 'error' ? 'assertive' : 'polite');

  // Remove existing feedback — using DOM APIs, never innerHTML
  while (feedbackArea.firstChild) {
    feedbackArea.removeChild(feedbackArea.firstChild);
  }

  const div = document.createElement('div');
  div.className = `feedback-message ${type}`;

  const textNode = document.createTextNode(message);
  div.appendChild(textNode);

  feedbackArea.appendChild(div);

  // Auto-dismiss after 5s
  setTimeout(() => {
    if (div.parentNode) {
      div.parentNode.removeChild(div);
    }
  }, 5000);
}

// ── Recent Clips List ──────────────────────────────────────────────────────

async function loadRecentClips(): Promise<void> {
  if (!clipList) return;

  try {
    const stored = await chrome.storage.local.get<{ recentClips?: RecentClip[] }>('recentClips');
    const clips: RecentClip[] = stored.recentClips ?? [];

    // Clear existing items — using DOM APIs, never innerHTML
    while (clipList.firstChild) {
      clipList.removeChild(clipList.firstChild);
    }

    if (clips.length === 0) {
      const emptyItem = document.createElement('li');
      emptyItem.className = 'clip-list-empty';
      emptyItem.textContent = 'No clips yet';
      clipList.appendChild(emptyItem);
      return;
    }

    for (const clip of clips) {
      const item = createClipItem(clip);
      clipList.appendChild(item);
    }
  } catch {
    // Silently fail — non-critical UI
  }
}

function createClipItem(clip: RecentClip): HTMLLIElement {
  const li = document.createElement('li');
  li.className = 'clip-item';

  // Mode badge
  const modeBadge = document.createElement('span');
  modeBadge.className = `clip-item-mode ${clip.mode}`;
  modeBadge.textContent = clip.mode;
  li.appendChild(modeBadge);

  // Info container
  const infoDiv = document.createElement('div');
  infoDiv.className = 'clip-item-info';

  const titleSpan = document.createElement('span');
  titleSpan.className = 'clip-item-title';
  titleSpan.textContent = clip.title || 'Untitled';
  titleSpan.title = clip.title || 'Untitled';
  infoDiv.appendChild(titleSpan);

  const urlSpan = document.createElement('span');
  urlSpan.className = 'clip-item-url';
  urlSpan.textContent = clip.url || '';
  urlSpan.title = clip.url || '';
  infoDiv.appendChild(urlSpan);

  li.appendChild(infoDiv);

  // Timestamp
  const timeSpan = document.createElement('span');
  timeSpan.className = 'clip-item-time';
  timeSpan.textContent = formatTimeAgo(clip.timestamp);
  li.appendChild(timeSpan);

  return li;
}

function formatTimeAgo(timestamp: number): string {
  const seconds = Math.floor((Date.now() - timestamp) / 1000);
  if (seconds < 60) return 'just now';
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  return `${days}d ago`;
}

// ── Helpers ────────────────────────────────────────────────────────────────

function setButtonsEnabled(enabled: boolean): void {
  const buttons = [clipFullBtn, clipSelectionBtn, clipReaderBtn, clipAllTabsBtn];
  for (const btn of buttons) {
    if (btn) btn.disabled = !enabled;
  }
}

function getButton(id: string): HTMLButtonElement | null {
  const element = document.getElementById(id);
  return element instanceof HTMLButtonElement ? element : null;
}

interface ExtensionResponse {
  success: boolean;
  data?: {
    connected?: boolean;
    paired?: boolean;
    version?: string;
    inboxEnabled?: boolean;
    provider?: string;
    inboxItemId?: number;
    status?: string;
    message?: string;
    [key: string]: unknown;
  };
  error?: string;
}

function sendMessage(message: { action: string; mode?: string; token?: string }): Promise<ExtensionResponse | undefined> {
  return new Promise((resolve) => {
    chrome.runtime.sendMessage(message, (response: ExtensionResponse) => {
      resolve(response);
    });
  });
}
