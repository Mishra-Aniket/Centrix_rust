// Connection + auth settings for the agent. The API key is entered once on the
// login screen and kept in localStorage; the agent URL is empty by default which
// means "same origin" (the C# agent serves this app). Later, when the Firebase
// cloud phase lands, only this module needs to change.

const API_KEY_STORAGE_KEY = 'lasrs.apiKey';
const AGENT_URL_STORAGE_KEY = 'lasrs.agentUrl';

type UnauthorizedListener = () => void;

const unauthorizedListeners = new Set<UnauthorizedListener>();

/** Registers a listener fired when the agent rejects the stored key (401). */
export function onUnauthorized(listener: UnauthorizedListener): () => void {
  unauthorizedListeners.add(listener);
  return () => unauthorizedListeners.delete(listener);
}

export function notifyUnauthorized(): void {
  unauthorizedListeners.forEach((listener) => listener());
}

export function isConfigured(): boolean {
  return Boolean(localStorage.getItem(API_KEY_STORAGE_KEY));
}

export function getStoredApiKey(): string | null {
  return localStorage.getItem(API_KEY_STORAGE_KEY);
}

export function storeApiKey(key: string): void {
  localStorage.setItem(API_KEY_STORAGE_KEY, key.trim());
}

export function clearApiKey(): void {
  localStorage.removeItem(API_KEY_STORAGE_KEY);
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
