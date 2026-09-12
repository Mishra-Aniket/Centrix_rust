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
  driveFolderPath?: string;
  driveVideoFileId?: string;
  createdAt: string;
  updatedAt: string;
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

export interface MonitorSnapshot {
  generatedAt: string;
  summary: {
    pending: number;
    uploading: number;
    uploaded: number;
    failed: number;
    totalLectures: number;
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
