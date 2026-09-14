// Connection + auth settings for Centrix.
// Supports direct API key, PW Google session token, and 1-click local PC access.

const API_KEY_STORAGE_KEY = 'lasrs.apiKey';
const SESSION_TOKEN_STORAGE_KEY = 'lasrs.sessionToken';
const AGENT_URL_STORAGE_KEY = 'lasrs.agentUrl';
const LOCAL_ACCESS_KEY = 'lasrs.localAccess';

type UnauthorizedListener = () => void;

const unauthorizedListeners = new Set<UnauthorizedListener>();

/** Registers a listener fired when the agent rejects stored credentials (401). */
export function onUnauthorized(listener: UnauthorizedListener): () => void {
  unauthorizedListeners.add(listener);
  return () => unauthorizedListeners.delete(listener);
}

export function notifyUnauthorized(): void {
  unauthorizedListeners.forEach((listener) => listener());
}

export function isConfigured(): boolean {
  return (
    Boolean(localStorage.getItem(API_KEY_STORAGE_KEY)) ||
    Boolean(localStorage.getItem(SESSION_TOKEN_STORAGE_KEY)) ||
    localStorage.getItem(LOCAL_ACCESS_KEY) === 'true'
  );
}

export function getStoredApiKey(): string | null {
  return localStorage.getItem(API_KEY_STORAGE_KEY);
}

export function storeApiKey(key: string): void {
  if (key.trim()) {
    localStorage.setItem(API_KEY_STORAGE_KEY, key.trim());
  } else {
    localStorage.removeItem(API_KEY_STORAGE_KEY);
  }
}

export function clearApiKey(): void {
  localStorage.removeItem(API_KEY_STORAGE_KEY);
  localStorage.removeItem(SESSION_TOKEN_STORAGE_KEY);
  localStorage.removeItem(LOCAL_ACCESS_KEY);
}

export function getStoredSessionToken(): string | null {
  return localStorage.getItem(SESSION_TOKEN_STORAGE_KEY);
}

export function storeSessionToken(token: string): void {
  localStorage.setItem(SESSION_TOKEN_STORAGE_KEY, token.trim());
}

export function setLocalAccess(): void {
  localStorage.setItem(LOCAL_ACCESS_KEY, 'true');
}

/** Empty string means "same origin as this page". */
export function getStoredAgentUrl(): string {
  return localStorage.getItem(AGENT_URL_STORAGE_KEY) ?? '';
}

export function storeAgentUrl(url: string): void {
  const trimmed = url.trim().replace(/\/+$/, '');
  if (trimmed) {
    localStorage.setItem(AGENT_URL_STORAGE_KEY, trimmed);
  } else {
    localStorage.removeItem(AGENT_URL_STORAGE_KEY);
  }
}
