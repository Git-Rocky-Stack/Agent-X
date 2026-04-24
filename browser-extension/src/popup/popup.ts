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
  bindEvents();
});

function bindEvents(): void {
  if (clipFullBtn) clipFullBtn.addEventListener('click', () => clipPage('full'));
  if (clipSelectionBtn) clipSelectionBtn.addEventListener('click', () => clipPage('selection'));
  if (clipReaderBtn) clipReaderBtn.addEventListener('click', () => clipPage('reader'));
  if (clipAllTabsBtn) clipAllTabsBtn.addEventListener('click', clipAllTabs);
}

// ── Connection Check ───────────────────────────────────────────────────────

async function checkConnection(): Promise<void> {
  if (!statusDot || !statusText) return;

  try {
    const response = await sendMessage({ action: 'checkConnection' });
    if (response?.success && response.data?.connected) {
      statusDot.classList.add('connected');
      statusDot.classList.remove('disconnected');
      statusText.textContent = `v${response.data.version ?? '\u2014'}`;
    } else {
      setDisconnected();
    }
  } catch {
    setDisconnected();
  }
}

function setDisconnected(): void {
  if (!statusDot || !statusText) return;
  statusDot.classList.add('disconnected');
  statusDot.classList.remove('connected');
  statusText.textContent = 'Offline';
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

async function clipAllTabs(): Promise<void> {
  setButtonsEnabled(false);
  showFeedback('Clipping all tabs...', 'info');

  try {
    const response = await sendMessage({ action: 'clipAllTabs' });

    if (response?.success && Array.isArray(response.data)) {
      const results = response.data as { status: string }[];
      const clipped = results.filter(r => r.status === 'clipped').length;
      const total = results.length;
      showFeedback(`Clipped ${clipped}/${total} tabs.`, clipped > 0 ? 'success' : 'error');
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

function sendMessage(message: { action: string; mode?: string }): Promise<ExtensionResponse | undefined> {
  return new Promise((resolve) => {
    chrome.runtime.sendMessage(message, (response: ExtensionResponse) => {
      resolve(response);
    });
  });
}
