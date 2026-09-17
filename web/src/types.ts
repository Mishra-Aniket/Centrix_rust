export interface HealthStatus {
  status: string;
  timestamp: string;
  database: string;
  fileWatcherActive: boolean;
  statistics: {
    totalLectures: number;
    pendingReview: number;
    queuedUploads: number;
    failedUploads: number;
  };
}

export interface LectureSession {
  lectureSessionId: string;
  organizationId: string;
  centerId: string;
  roomId: string;
  deviceId?: string;
  videoFilePath?: string;
  videoFileSize?: number;
  detectedStartTime: string;
  detectedEndTime: string;
  detectedDurationSeconds: number;
  scheduledSlotId?: string;
  scheduledStartTime?: string;
  scheduledEndTime?: string;
  batchId?: string;
  subjectId?: string;
  teacherId?: string;
  assignmentSource?: string;
  confidenceScore: number;
  status: string;
  reviewStatus: string;
  reviewerId?: string;
  matchingReason?: string;
  matchStatus?: string;
  failureCode?: string;
  failureReason?: string;
  matchedAt?: string;
  matchAttempts?: number;
  youTubeId?: string;
  youTubePublishStatus?: string;
  youTubeThumbnailUrl?: string;
  youTubeFailureReason?: string;
  youTubePublishedAt?: string;
  qcStatus?: string;
  qcResults?: string;
  qcPassedAt?: string;
  driveFolderPath?: string;
  driveVideoFileId?: string;
  pdfFilePath?: string;
  pdfFileSize?: number;
  drivePdfFileId?: string;
  createdAt: string;
  updatedAt: string;
}

export interface LectureSummary {
  total: number;
  uploaded: number;
  matched: number;
  unmatched: number;
  failedUpload: number;
  pendingReview: number;
  uploadedPercentage: number;
  matchedPercentage: number;
}

export interface ActionResponse<T> {
  success: boolean;
  message: string;
  data?: T;
}

export interface QcCheckResult {
  name: string;
  passed: boolean;
  detail: string;
}

export interface QcReport {
  status: string;
  checkedAt: string;
  checks: QcCheckResult[];
}

export interface AuditEntry {
  auditEntryId: string;
  entityType: string;
  entityId: string;
  actionType: string;
  userId?: string | null;
  changeSummary?: string | null;
  createdAt: string;
}

export interface QueueEntry {
  queueEntryId: string;
  fileType: string;
  fileName: string;
  localFilePath: string;
  fileSizeBytes: number;
  bytesUploaded: number;
  status: string;
  progressPercentage: number;
  retryCount: number;
  driveFileId?: string | null;
  lastError?: string | null;
  updatedAt: string;
  roomId?: string | null;
  driveFolderPath?: string | null;
  lectureSessionId?: string | null;
  batchId?: string | null;
}

export interface TimetableEntry {
  timetableEntryId: string;
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
  createdAt: string;
  updatedAt: string;
}

export interface TimetableSummary {
  totalLectures: number;
  totalRooms: number;
  dayCounts: Record<string, number>;
  roomCounts: Record<string, number>;
}

export interface MonitorSnapshot {
  generatedAt: string;
  summary: {
    pending: number;
    uploading: number;
    uploaded: number;
    failed: number;
    totalLectures: number;
    uploadedToday?: number;
    failedToday?: number;
    totalToday?: number;
  };
  queue: QueueEntry[];
  lectures: {
    lectureSessionId: string;
    fileName: string;
    status: string;
    confidenceScore: number;
    centerId: string;
    roomId: string;
    updatedAt: string;
  }[];
}

export interface AgentInfo {
  agentVersion: string;
  organizationId: string;
  centerId: string;
  roomId: string;
  deviceId: string;
  machineName: string;
  monitorFolder: string;
  fileWatcherEnabled: boolean;
  googleDriveEnabled: boolean;
  driveRootFolder: string;
  queueUnmatchedFiles: boolean;
  sheetSyncIntervalMinutes: number;
  lanIpv4Addresses: string[];
  httpPort: number;
  httpsPort: number;
  startedAtUtc: string;
  uptimeSeconds: number;
  uploadsPaused: boolean;
  monitoringPaused: boolean;
  timetableSyncPaused: boolean;
  youTubeEnabled?: boolean;
}

export interface ControlState {
  uploadsPaused: boolean;
  uploadsPausedAt?: string | null;
  monitoringPaused: boolean;
  monitoringPausedAt?: string | null;
  timetableSyncPaused: boolean;
  timetableSyncPausedAt?: string | null;
}

export interface TimetableOverride {
  overrideId: string;
  organizationId: string;
  centerId: string;
  roomId: string;
  originalTimetableEntryId?: string | null;
  originalDate?: string | null;
  originalSlotId?: string | null;
  overrideType: string;
  newBatchId?: string | null;
  newSubjectId?: string | null;
  newTeacherId?: string | null;
  effectiveDate: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface MissingSlot {
  timetableEntryId: string;
  slotId: string;
  batchId: string;
  subjectId: string;
  teacherId?: string | null;
  scheduledDate: string;
  slotStartTime: string;
  slotEndTime: string;
}

export interface CenterRoom {
  name: string;
  url: string;
  apiKey: string;
  roomId: string;
}

export interface RoomOverview {
  name: string;
  roomId: string;
  url: string;
  live: boolean;
  error?: string | null;
  pending: number;
  uploading: number;
  uploaded: number;
  failed: number;
  totalLectures: number;
  missingCount: number;
  missingItems: { time: string; batch: string; subject: string }[];
}

// ── PW Studio API Types ──

export interface StudioFile {
  _id: string;
  fileName: string;
  fileType: string;
  startTime: string;
  stopTime: string;
  center: string;
  room: string;
  batchName?: string | null;
  batchId?: string | null;
  uploaded: boolean;
  scheduled: boolean;
  isProcessed: boolean;
  deleted: boolean;
  youtubeId?: string | null;
  fileKey?: string | null;
  fileDetails?: { baseUrl?: string; key?: string } | null;
}

export interface StudioCenter {
  center: string;
  rooms: string[];
  batches: { batchName: string; batchId: string }[];
}

export interface StudioTeacher {
  center: string;
  name: string;
  email: string;
  driveLink: string;
  driveId: string;
}

export interface StudioStatus {
  connected: boolean;
  reason?: string;
  baseUrl?: string;
}

export const STANDARD_SUBJECTS = [
  'Physics',
  'Chemistry',
  'Maths',
  'Botany',
  'Zoology',
  'Biology',
  'SST',
  'English',
] as const;

