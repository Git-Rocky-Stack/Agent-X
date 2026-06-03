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

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function isExtensionHealthResponse(value: unknown): value is ExtensionHealthResponse {
  return isRecord(value)
    && typeof value.connected === 'boolean'
    && typeof value.version === 'string'
    && typeof value.inboxEnabled === 'boolean'
    && typeof value.provider === 'string';
}

function isClipResponse(value: unknown): value is ClipResponse {
  return isRecord(value)
    && typeof value.inboxItemId === 'number'
    && typeof value.status === 'string'
    && typeof value.message === 'string';
}

function parseApiResponse<T>(
  value: unknown,
  isData: (data: unknown) => data is T,
  errorContext: string
): ApiResponse<T> {
  if (!isRecord(value) || typeof value.success !== 'boolean') {
    throw new Error(`${errorContext} returned an invalid response envelope`);
  }

  if (value.error !== undefined && typeof value.error !== 'string') {
    throw new Error(`${errorContext} returned an invalid error payload`);
  }

  if (value.data !== undefined && !isData(value.data)) {
    throw new Error(`${errorContext} returned an invalid data payload`);
  }

  return {
    success: value.success,
    data: value.data,
    error: value.error,
  };
}

// ── Client ──────────────────────────────────────────────────────────────────

export class AgentXApi {
  private readonly baseUrl: string;
  private token: string | null;

  constructor(baseUrl: string = API_BASE, token: string | null = null) {
    this.baseUrl = baseUrl;
    this.token = token;
  }

  /** Update the bearer token used to authenticate data requests (set during pairing). */
  setToken(token: string | null): void {
    this.token = token;
  }

  /** Builds request headers, attaching the bearer token when the extension is paired. */
  private authHeaders(base: Record<string, string> = {}): Record<string, string> {
    const headers: Record<string, string> = { ...base };
    if (this.token) {
      headers['Authorization'] = `Bearer ${this.token}`;
    }
    return headers;
  }

  /** Check if AgentX is running and the inbox is available. */
  async checkHealth(): Promise<ExtensionHealthResponse> {
    const response = await fetch(`${this.baseUrl}/api/extension/health`);
    if (!response.ok) {
      throw new Error(`Health check failed: ${response.status} ${response.statusText}`);
    }
    const envelope = parseApiResponse(
      await response.json() as unknown,
      isExtensionHealthResponse,
      'Health check'
    );

    if (!envelope.success || !envelope.data) {
      throw new Error(envelope.error ?? 'Health check returned unsuccessful response');
    }

    return envelope.data;
  }

  /** Clip content to the AgentX Smart Inbox. */
  async clipToInbox(clip: ClipRequest): Promise<ClipResponse> {
    const response = await fetch(`${this.baseUrl}/api/inbox/clip`, {
      method: 'POST',
      headers: this.authHeaders({ 'Content-Type': 'application/json' }),
      body: JSON.stringify(clip),
    });

    if (response.status === 401) {
      throw new Error(
        'Not paired with AgentX. Open the extension popup and paste the API token from ' +
        'AgentX → Settings → Connections.'
      );
    }

    if (!response.ok) {
      const errorBody = await response.text().catch(() => '');
      throw new Error(`Clip failed: ${response.status} ${response.statusText}${errorBody ? ` — ${errorBody}` : ''}`);
    }

    const envelope = parseApiResponse(
      await response.json() as unknown,
      isClipResponse,
      'Clip request'
    );

    if (!envelope.success || !envelope.data) {
      throw new Error(envelope.error ?? 'Clip request returned unsuccessful response');
    }

    return envelope.data;
  }
}
