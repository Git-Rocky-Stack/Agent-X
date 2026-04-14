/**
 * AgentX API Client
 *
 * Communicates with the AgentX desktop app's local REST API.
 * All requests go to localhost:9846 only.
 */

const API_BASE = 'http://localhost:9846';

// ── Types (mirrors ApiClipModels.cs) ────────────────────────────────────────

export interface ClipRequest {
  title: string;
  content: string;
  sourceUrl: string;
  author?: string;
  publishedDate?: string;
  clipMode: 'full' | 'selection' | 'reader';
  wordCount: number;
  metadata?: Record<string, string>;
}

export interface ClipResponse {
  inboxItemId: number;
  status: string;
  message: string;
}

export interface ExtensionHealthResponse {
  connected: boolean;
  version: string;
  inboxEnabled: boolean;
  provider: string;
}

/** Generic API envelope matching AgentX's ApiResponse<T> */
interface ApiResponse<T> {
  success: boolean;
  data?: T;
  error?: string;
}

// ── Client ──────────────────────────────────────────────────────────────────

export class AgentXApi {
  private readonly baseUrl: string;

  constructor(baseUrl: string = API_BASE) {
    this.baseUrl = baseUrl;
  }

  /** Check if AgentX is running and the inbox is available. */
  async checkHealth(): Promise<ExtensionHealthResponse> {
    const response = await fetch(`${this.baseUrl}/api/extension/health`);
    if (!response.ok) {
      throw new Error(`Health check failed: ${response.status} ${response.statusText}`);
    }
    const envelope: ApiResponse<ExtensionHealthResponse> = await response.json();
    if (!envelope.success || !envelope.data) {
      throw new Error(envelope.error ?? 'Health check returned unsuccessful response');
    }
    return envelope.data;
  }

  /** Clip content to the AgentX Smart Inbox. */
  async clipToInbox(clip: ClipRequest): Promise<ClipResponse> {
    const response = await fetch(`${this.baseUrl}/api/inbox/clip`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(clip),
    });

    if (!response.ok) {
      const errorBody = await response.text().catch(() => '');
      throw new Error(`Clip failed: ${response.status} ${response.statusText}${errorBody ? ` — ${errorBody}` : ''}`);
    }

    const envelope: ApiResponse<ClipResponse> = await response.json();
    if (!envelope.success || !envelope.data) {
      throw new Error(envelope.error ?? 'Clip request returned unsuccessful response');
    }
    return envelope.data;
  }
}