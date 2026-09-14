import type {
  AgentInfo,
  CenterRoom,
  ControlState,
  HealthStatus,
  LectureSession,
  MissingSlot,
  MonitorSnapshot,
  RoomOverview,
  TimetableEntry,
  TimetableOverride,
} from './types';
import { getStoredAgentUrl, getStoredApiKey, getStoredSessionToken, notifyUnauthorized } from './config';

export class ApiError extends Error {
  status: number;

  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

function baseUrl(): string {
  return getStoredAgentUrl();
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  const apiKey = getStoredApiKey();
  if (apiKey) headers.set('X-Agent-Key', apiKey);
  const sessionToken = getStoredSessionToken();
  if (sessionToken) headers.set('X-Session', sessionToken);
  if (init.body && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json');

  let res: Response;
  try {
    res = await fetch(`${baseUrl()}${path}`, { ...init, headers });
  } catch {
    throw new ApiError(0, 'Agent is unreachable (network error)');
  }

  if (res.status === 401) {
    notifyUnauthorized();
    throw new ApiError(401, 'Authentication required');
  }

  if (!res.ok) {
    let message = `${res.status} ${res.statusText}`.trim();
    try {
      const body = await res.json();
      if (body?.error) message = body.error;
    } catch {
      // non-JSON error body; keep the status text
    }
    throw new ApiError(res.status, message);
  }

  return (await res.json()) as T;
}

/** Verifies credentials against the agent before storing them. */
export async function testConnection(agentUrl: string, apiKey: string): Promise<AgentInfo> {
  const base = agentUrl.trim().replace(/\/+$/, '');
  const headers: Record<string, string> = {};
  if (apiKey) headers['X-Agent-Key'] = apiKey;
  const session = getStoredSessionToken();
  if (session) headers['X-Session'] = session;
  const res = await fetch(`${base}/api/agent/info`, { headers });
  if (res.status === 401) throw new ApiError(401, 'Authentication rejected by agent');
  if (!res.ok) throw new ApiError(res.status, `Agent responded ${res.status} ${res.statusText}`);
  return (await res.json()) as AgentInfo;
}

export interface AuthConfig {
  googleLoginEnabled: boolean;
  emailsRegistered: boolean;
  bootstrapMode: boolean;
  domains: string[];
}

export async function fetchAuthConfig(): Promise<AuthConfig> {
  return request<AuthConfig>('/api/auth/config');
}

export async function startGoogleLogin(): Promise<{ flowId: string; consentUrl: string; redirectUri: string }> {
  return request('/api/auth/google/start', { method: 'POST', body: JSON.stringify({}) });
}

export async function pollGoogleLogin(flowId: string): Promise<{ status: string; sessionToken?: string; email?: string }> {
  return request(`/api/auth/google/poll/${flowId}`);
}

// ---------- Read ----------

export async function fetchHealth(): Promise<HealthStatus> {
  return request<HealthStatus>('/health');
}

export async function fetchAgentInfo(): Promise<AgentInfo> {
  return request<AgentInfo>('/api/agent/info');
}

export async function fetchControlState(): Promise<ControlState> {
  return request<ControlState>('/api/control/state');
}

export async function fetchSnapshot(): Promise<MonitorSnapshot> {
  return request<MonitorSnapshot>('/api/monitor/snapshot');
}

export async function fetchLectures(centerId: string, limit = 50): Promise<{ total: number; count: number; items: LectureSession[] }> {
  return request(`/api/lectures?centerId=${encodeURIComponent(centerId)}&limit=${limit}`);
}

export async function fetchReviewQueue(centerId: string): Promise<LectureSession[]> {
  return request(`/api/lectures/review-queue?centerId=${encodeURIComponent(centerId)}`);
}

export async function fetchTimetable(centerId: string, roomId: string, date?: string): Promise<TimetableEntry[]> {
  const params = new URLSearchParams({ centerId, roomId });
  if (date) params.set('date', date);
  return request(`/api/timetable?${params.toString()}`);
}

export async function fetchOverrides(centerId: string, roomId?: string, date?: string): Promise<TimetableOverride[]> {
  const params = new URLSearchParams({ centerId });
  if (roomId) params.set('roomId', roomId);
  if (date) params.set('date', date);
  return request(`/api/timetable/overrides?${params.toString()}`);
}

export async function fetchMissingLectures(centerId: string, roomId: string): Promise<MissingSlot[]> {
  return request(`/api/monitor/missing?centerId=${encodeURIComponent(centerId)}&roomId=${encodeURIComponent(roomId)}`);
}

export async function forceEnqueueLecture(lectureId: string): Promise<LectureSession> {
  return request(`/api/lectures/${lectureId}/force-enqueue`, { method: 'POST' });
}

// ---------- Center (all rooms) ----------

export async function fetchCenterOverview(): Promise<RoomOverview[]> {
  return request('/api/center/overview');
}

export async function fetchCenterRooms(): Promise<CenterRoom[]> {
  return request('/api/center/rooms');
}

export async function saveCenterRoom(room: CenterRoom): Promise<CenterRoom[]> {
  return request('/api/center/rooms', { method: 'POST', body: JSON.stringify(room) });
}

export async function deleteCenterRoom(name: string): Promise<CenterRoom[]> {
  return request(`/api/center/rooms/${encodeURIComponent(name)}`, { method: 'DELETE' });
}

// ---------- Lecture actions ----------

export async function confirmLecture(
  lectureId: string,
  batchId: string,
  subjectId: string,
  teacherId = '',
  reviewedBy = 'Dashboard User'
): Promise<LectureSession> {
  return request(`/api/lectures/${lectureId}/confirm`, {
    method: 'PUT',
    body: JSON.stringify({ batchId, subjectId, teacherId, reviewedBy }),
  });
}

export async function rematchLecture(lectureId: string): Promise<LectureSession> {
  return request(`/api/lectures/${lectureId}/rematch`, { method: 'POST' });
}

// ---------- Timetable actions ----------

export async function createTimetableEntry(entry: {
  organizationId: string;
  centerId: string;
  roomId: string;
  scheduledDate: string;
  slotStartTime: string;
  slotEndTime: string;
  slotId: string;
  batchId: string;
  subjectId: string;
  teacherId?: string;
}): Promise<TimetableEntry> {
  return request('/api/timetable', {
    method: 'POST',
    body: JSON.stringify(entry),
  });
}

export async function cancelTimetableSlot(entry: {
  organizationId: string;
  centerId: string;
  roomId: string;
  timetableEntryId: string;
  scheduledDate: string;
  slotId: string;
}): Promise<TimetableOverride> {
  return request('/api/timetable/overrides', {
    method: 'POST',
    body: JSON.stringify({
      organizationId: entry.organizationId,
      centerId: entry.centerId,
      roomId: entry.roomId,
      originalTimetableEntryId: entry.timetableEntryId,
      originalDate: entry.scheduledDate,
      originalSlotId: entry.slotId,
      overrideType: 'Cancelled',
      effectiveDate: entry.scheduledDate,
    }),
  });
}

export async function deactivateOverride(overrideId: string): Promise<TimetableOverride> {
  return request(`/api/timetable/overrides/${overrideId}/deactivate`, { method: 'POST' });
}

// ---------- Runtime control ----------

export async function pauseUploads(): Promise<ControlState> {
  return request('/api/control/uploads/pause', { method: 'POST' });
}

export async function resumeUploads(): Promise<ControlState> {
  return request('/api/control/uploads/resume', { method: 'POST' });
}

export async function retryAllFailedUploads(): Promise<{ retried: number }> {
  return request('/api/control/uploads/retry-failed', { method: 'POST' });
}

export async function retryUpload(queueEntryId: string): Promise<unknown> {
  return request(`/api/control/uploads/${queueEntryId}/retry`, { method: 'POST' });
}

export async function cancelUpload(queueEntryId: string): Promise<unknown> {
  return request(`/api/control/uploads/${queueEntryId}/cancel`, { method: 'POST' });
}

export async function pauseMonitoring(): Promise<ControlState> {
  return request('/api/control/monitoring/pause', { method: 'POST' });
}

export async function resumeMonitoring(): Promise<ControlState> {
  return request('/api/control/monitoring/resume', { method: 'POST' });
}

export async function rescanFolder(): Promise<{ newlyTracked: number }> {
  return request('/api/control/monitoring/rescan', { method: 'POST' });
}

export async function syncTimetableNow(): Promise<{ synced: boolean }> {
  return request('/api/control/timetable/sync', { method: 'POST' });
}

export async function pauseTimetableSync(): Promise<ControlState> {
  return request('/api/control/timetable/sync/pause', { method: 'POST' });
}

export async function resumeTimetableSync(): Promise<ControlState> {
  return request('/api/control/timetable/sync/resume', { method: 'POST' });
}
