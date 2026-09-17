import { useRef, useState } from 'react';
import {
  AlertCircle,
  AlertTriangle,
  Check,
  CheckCircle2,
  CopyCheck,
  Edit3,
  FileText,
  Folder,
  FolderEdit,
  Loader2,
  Play,
  RefreshCw,
  RotateCcw,
  ShieldCheck,
  ShieldAlert,
  Trash2,
  UploadCloud,
  Video,
} from 'lucide-react';
import { VideoThumbnail } from '../components/VideoThumbnail';
import { MediaPreviewModal } from '../components/MediaPreviewModal';
import { SearchableRoomSelect } from '../components/SearchableRoomSelect';
import type { ActionResponse, LectureSession, LectureSummary, QcReport, QueueEntry } from '../types';
import { STANDARD_SUBJECTS } from '../types';
import { ActionButton, Modal, Pill, inputClass } from '../ui';

interface ReviewScreenProps {
  pendingReviews: LectureSession[];
  completedLectures?: LectureSession[];
  duplicates: LectureSession[];
  summary?: LectureSummary;
  failedQueueItems?: QueueEntry[];
  queueItems?: QueueEntry[];
  rooms?: string[];
  busy: boolean;
  onApprove: (lecture: LectureSession) => void;
  onEdit: (lecture: LectureSession) => void;
  onCancelLecture: (lecture: LectureSession) => void;
  onEnqueueUpload?: (lecture: LectureSession) => void;
  onRematch: (lecture: LectureSession) => Promise<{ success: boolean; message: string }>;
  onRetryLectureUpload?: (lectureId: string) => Promise<{ success: boolean; message: string }>;
  onRunQc?: (lectureId: string) => Promise<ActionResponse<QcReport>>;
  onPublishToYouTube?: (lectureId: string) => Promise<ActionResponse<LectureSession>>;
  onUnpublishFromYouTube?: (lectureId: string) => Promise<ActionResponse<LectureSession>>;
  onForceEnqueue: (lecture: LectureSession) => void;
  onEditUploadFolder?: (queueEntry: QueueEntry) => void;
  onRetryUpload?: (queueEntryId: string) => void;
  onCancelUpload?: (queueEntryId: string) => void;
  onManualUpload?: (formData: FormData) => Promise<void>;
}

function formatFileSize(bytes?: number): string {
  if (!bytes || bytes <= 0) return '0 B';
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

export function ReviewScreen({
  pendingReviews,
  completedLectures = [],
  duplicates,
  summary,
  failedQueueItems = [],
  queueItems = [],
  rooms = ['603'],
  busy,
  onApprove,
  onEdit,
  onCancelLecture,
  onEnqueueUpload,
  onRematch,
  onRetryLectureUpload,
  onRunQc,
  onPublishToYouTube,
  onForceEnqueue,
  onEditUploadFolder,
  onRetryUpload,
  onCancelUpload,
  onManualUpload,
}: ReviewScreenProps) {
  // Separate pure pending review vs already approved/in-transit
  const needsReview = pendingReviews.filter(
    (l) =>
      l.reviewStatus === 'Pending' ||
      l.status === 'ReviewRequired' ||
      l.status === 'Detected' ||
      l.status === 'ExtraLecture' ||
      l.status === 'Processing'
  );

  const awaitingUpload = pendingReviews.filter(
    (l) =>
      !needsReview.some((nr) => nr.lectureSessionId === l.lectureSessionId) &&
      (l.status === 'Confirmed' || l.status === 'AutoAssigned' || l.status === 'Uploading')
  );

  const totalPendingAction = needsReview.length + failedQueueItems.length + duplicates.length;

  // Manual upload modal state
  const [uploadModalOpen, setUploadModalOpen] = useState(false);
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [uploadRoom, setUploadRoom] = useState(rooms[0] || '603');
  const [uploadDriveFolder, setUploadDriveFolder] = useState('');
  const [uploadBatch, setUploadBatch] = useState('');
  const [uploadSubject, setUploadSubject] = useState('');
  const [uploading, setUploading] = useState(false);
  const [uploadError, setUploadError] = useState<string | null>(null);
  const [rematchingId, setRematchingId] = useState<string | null>(null);
  const [rematchResult, setRematchResult] = useState<{ lectureId: string; success: boolean; message: string } | null>(null);
  const [qcReports, setQcReports] = useState<Record<string, QcReport>>({});
  const [qcLoadingId, setQcLoadingId] = useState<string | null>(null);
  const [previewLecture, setPreviewLecture] = useState<LectureSession | null>(null);
  const [previewUploadFile, setPreviewUploadFile] = useState<File | null>(null);
  const [manualYouTubeLecture, setManualYouTubeLecture] = useState<LectureSession | null>(null);
  const [copiedInfo, setCopiedInfo] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const handleOpenUpload = (presetLecture?: LectureSession) => {
    setSelectedFile(null);
    setUploadError(null);
    if (presetLecture) {
      setUploadRoom(presetLecture.roomId || rooms[0] || '603');
      setUploadBatch(presetLecture.batchId || '');
      setUploadSubject(presetLecture.subjectId || '');
      setUploadDriveFolder(presetLecture.driveFolderPath || presetLecture.batchId || '');
    } else {
      setUploadRoom(rooms[0] || '603');
      setUploadBatch('');
      setUploadSubject('');
      setUploadDriveFolder('');
    }
    setUploadModalOpen(true);
  };

  const handleFileSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedFile || !onManualUpload) return;

    setUploading(true);
    setUploadError(null);

    try {
      const formData = new FormData();
      formData.append('file', selectedFile);
      const batch = uploadBatch.trim();
      const sub = uploadSubject.trim();
      const folder = uploadDriveFolder.trim() || batch;
      const finalFolder = folder.includes('/') || !sub ? folder : `${folder}/${sub}`;

      if (uploadRoom && uploadRoom !== 'ALL') formData.append('roomId', uploadRoom);
      if (batch) formData.append('batchId', batch);
      if (sub) formData.append('subjectId', sub);
      if (finalFolder) formData.append('driveFolderPath', finalFolder);

      await onManualUpload(formData);
      setUploadModalOpen(false);
      setSelectedFile(null);
    } catch (err) {
      setUploadError(err instanceof Error ? err.message : 'File upload failed');
    } finally {
      setUploading(false);
    }
  };

  // Helper to find file information for a lecture session
  const getLectureFileInfo = (lecture: LectureSession) => {
    const queue = queueItems.find((q) => q.lectureSessionId === lecture.lectureSessionId);
    if (queue) {
      return {
        hasFile: true,
        fileName: queue.fileName,
        fileSize: queue.fileSizeBytes,
        fileType: queue.fileType,
        status: queue.status,
        progress: queue.progressPercentage,
        driveFolder: queue.driveFolderPath || lecture.driveFolderPath,
      };
    }

    const videoPath = lecture.videoFilePath;
    const pdfPath = lecture.pdfFilePath;

    if (videoPath) {
      const fileName = videoPath.split(/[/\\]/).pop() || 'recording.mkv';
      return {
        hasFile: true,
        fileName,
        fileSize: lecture.videoFileSize,
        fileType: 'VIDEO',
        status: lecture.status,
        progress: 0,
        driveFolder: lecture.driveFolderPath,
      };
    }

    if (pdfPath) {
      const fileName = pdfPath.split(/[/\\]/).pop() || 'lecture_notes.pdf';
      return {
        hasFile: true,
        fileName,
        fileSize: lecture.pdfFileSize,
        fileType: 'PDF',
        status: lecture.status,
        progress: 0,
        driveFolder: lecture.driveFolderPath,
      };
    }

    return {
      hasFile: false,
      fileName: '',
      fileSize: 0,
      fileType: '',
      status: 'NoFile',
      progress: 0,
      driveFolder: lecture.driveFolderPath,
    };
  };

  const handleRematch = async (lecture: LectureSession) => {
    setRematchingId(lecture.lectureSessionId);
    setRematchResult(null);
    try {
      const result = await onRematch(lecture);
      setRematchResult({ lectureId: lecture.lectureSessionId, ...result });
    } catch (err) {
      setRematchResult({
        lectureId: lecture.lectureSessionId,
        success: false,
        message: err instanceof Error ? err.message : 'Matching could not be re-run'
      });
    } finally {
      setRematchingId(null);
    }
  };

  const handleRunQc = async (lectureId: string) => {
    if (!onRunQc) return;
    setQcLoadingId(lectureId);
    try {
      const response = await onRunQc(lectureId);
      if (response.data) {
        setQcReports((current) => ({ ...current, [lectureId]: response.data! }));
      }
    } finally {
      setQcLoadingId(null);
    }
  };


  const renderQcPanel = (lecture: LectureSession) => {
    const report = qcReports[lecture.lectureSessionId];
    return (
      <div className="rounded-xl border border-slate-200 bg-slate-50/70 p-2.5 space-y-2">
        <div className="flex items-center justify-between gap-2">
          <span className="text-[10px] font-bold uppercase tracking-wide text-slate-600 flex items-center gap-1">
            {report?.status === 'Passed' || lecture.qcStatus === 'Passed'
              ? <ShieldCheck className="w-3.5 h-3.5 text-emerald-600" />
              : <ShieldAlert className="w-3.5 h-3.5 text-amber-600" />}
            QC {report?.status || lecture.qcStatus || 'Pending'}
          </span>
          {onRunQc && (
            <button
              type="button"
              onClick={() => void handleRunQc(lecture.lectureSessionId)}
              disabled={busy || qcLoadingId === lecture.lectureSessionId}
              className="text-[10px] font-bold text-cyan-700 hover:text-cyan-800 disabled:opacity-40"
            >
              {qcLoadingId === lecture.lectureSessionId ? 'Checking…' : report ? 'Re-run QC' : 'Run QC'}
            </button>
          )}
        </div>
        {report && (
          <div className="flex flex-wrap gap-1.5">
            {report.checks.map((check) => (
              <span
                key={check.name}
                title={check.detail}
                className={`px-1.5 py-0.5 rounded-md border text-[9px] font-semibold ${
                  check.passed
                    ? 'bg-emerald-50 text-emerald-700 border-emerald-200'
                    : 'bg-rose-50 text-rose-700 border-rose-200'
                }`}
              >
                {check.passed ? '✓' : '!' } {check.name}
              </span>
            ))}
          </div>
        )}
      </div>
    );
  };

  const renderYouTubePanel = (lecture: LectureSession) => {
    return (
      <div className="border border-[var(--rule)] bg-[var(--cream)] p-3 space-y-2 text-xs font-mono">
        <div className="flex items-center justify-between gap-2">
          <div className="flex items-center gap-1.5 min-w-0">
            <span className="w-2 h-2 rounded-full bg-red-600 shrink-0" />
            <span className="text-[11px] font-semibold text-[var(--ink)] truncate">
              YouTube Publishing (Manual)
            </span>
          </div>
          <span className="text-[9px] px-1.5 py-0.2 border border-emerald-300 bg-emerald-50 text-emerald-800 uppercase">
            Drive Ready
          </span>
        </div>
        <p className="text-[10px] text-[var(--stone)] leading-relaxed">
          Lecture safely uploaded to Google Drive. Center members can publish to YouTube manually anytime.
        </p>
        <div className="flex items-center gap-2 pt-0.5">
          <button
            type="button"
            onClick={() => setManualYouTubeLecture(lecture)}
            className="px-2.5 py-1 text-[10px] uppercase tracking-wider bg-[var(--ink)] text-[var(--cream)] hover:opacity-90 transition cursor-pointer flex items-center gap-1"
          >
            <span>Publish to YouTube</span>
          </button>
          <a
            href="https://studio.youtube.com"
            target="_blank"
            rel="noreferrer"
            className="px-2.5 py-1 text-[10px] uppercase tracking-wider border border-[var(--rule)] bg-[var(--paper)] text-[var(--ink)] hover:bg-[var(--cream)] transition flex items-center gap-1"
          >
            <span>Open Studio ↗</span>
          </a>
        </div>
      </div>
    );
  };

  return (
    <div className="space-y-4">
      {/* Header Banner */}
      <div className="flex items-center justify-between px-1">
        <div>
          <div className="font-mono text-[10px] uppercase tracking-wider text-[var(--stone)]">
            QUEUE · LECTURE VERIFICATION
          </div>
          <h2 className="font-serif text-xl font-normal text-[var(--ink)]">Review & Approval Hub</h2>
          <p className="text-xs font-mono text-[var(--stone)] mt-0.5">
            Lectures stay here until successfully uploaded to Google Drive
          </p>
        </div>
        <Pill tone={totalPendingAction > 0 ? 'amber' : 'emerald'}>
          {totalPendingAction > 0 ? `${totalPendingAction} Action Required` : 'All Clear'}
        </Pill>
      </div>

      {/* Quick Upload from PC button */}
      <div className="bg-[var(--paper)] border border-[var(--rule)] p-4 flex items-center justify-between gap-3">
        <div className="min-w-0">
          <span className="font-serif text-base text-[var(--ink)] block">Missing a recording or notes file?</span>
          <p className="text-[11px] font-mono text-[var(--stone)] truncate">
            Select a file directly from your PC and pick the Google Drive destination
          </p>
        </div>
        <button
          type="button"
          onClick={() => handleOpenUpload()}
          disabled={busy}
          className="px-4 py-2 bg-[var(--ink)] hover:opacity-90 text-[var(--cream)] font-mono text-xs uppercase tracking-wider flex items-center gap-1.5 shrink-0 transition cursor-pointer"
        >
          <UploadCloud className="w-3.5 h-3.5" />
          <span>Upload File →</span>
        </button>
      </div>

      {/* Pipeline Summary matching 6-stat box bar */}
      {(() => {
        const total = (summary && summary.total > 0) ? summary.total : (pendingReviews.length + completedLectures.length + failedQueueItems.length);
        const uploaded = (summary && summary.total > 0) ? summary.uploaded : completedLectures.length;
        const matched = (summary && summary.total > 0) ? summary.matched : (completedLectures.length + awaitingUpload.length);
        const unmatched = (summary && summary.total > 0) ? summary.unmatched : needsReview.filter(p => !p.batchId || p.batchId === 'Unassigned').length;
        const failed = (summary && summary.total > 0) ? summary.failedUpload : failedQueueItems.length;
        const pending = (summary && summary.total > 0) ? summary.pendingReview : needsReview.length;
        const uploadedPct = total > 0 ? Math.round((uploaded / total) * 100) : 0;
        const matchedPct = total > 0 ? Math.round((matched / total) * 100) : 0;

        return (
          <div className="grid grid-cols-3 sm:grid-cols-6 border border-[var(--rule)] bg-[var(--paper)]">
            {[
              { label: 'Total', value: total },
              { label: 'Uploaded', value: uploaded, pct: uploadedPct },
              { label: 'Matched', value: matched, pct: matchedPct },
              { label: 'Unmatched', value: unmatched },
              { label: 'Failed', value: failed },
              { label: 'Pending', value: pending },
            ].map((card, idx) => {
              return (
                <div key={card.label} className={`p-3 text-center flex flex-col justify-center ${idx > 0 ? 'border-l border-[var(--rule)]' : ''}`}>
                  <span className="font-serif text-2xl font-light text-[var(--ink)] leading-none">{card.value}</span>
                  <span className="font-mono text-[9px] uppercase tracking-wider text-[var(--stone)] mt-1.5">{card.label}</span>
                  {card.pct !== undefined && <span className="font-mono text-[9px] text-[var(--stone)] mt-0.5">{card.pct}%</span>}
                </div>
              );
            })}
          </div>
        );
      })()}

      {/* 1. URGENT: Failed Uploads Needing Folder / Batch Selection */}
      {failedQueueItems.length > 0 && (
        <div className="space-y-2.5">
          <div className="flex items-center gap-1.5 px-1 text-xs font-mono uppercase tracking-wider text-rose-500">
            <AlertTriangle className="w-3.5 h-3.5 text-rose-500" />
            <span>Upload Failed — Action Needed ({failedQueueItems.length})</span>
          </div>

          <div className="grid grid-cols-1 xl:grid-cols-2 gap-4">
            {failedQueueItems.map((item) => (
              <div
                key={item.queueEntryId}
                className="bg-[var(--paper)] border border-rose-800/50 p-5 space-y-3 flex flex-col justify-between font-mono"
              >
                <div className="space-y-3">
                  <div className="flex items-start justify-between gap-2">
                    <div className="min-w-0">
                      <div className="flex items-center gap-2 flex-wrap">
                        <span className="font-serif text-base text-[var(--ink)] truncate">
                          {item.fileName}
                        </span>
                        {item.roomId && (
                          <span className="text-[9px] px-1.5 py-0.2 border border-[var(--rule)] bg-[var(--cream)] text-[var(--stone)]">
                            Room {item.roomId}
                          </span>
                        )}
                      </div>
                      <p className="text-[10px] text-[var(--stone)] font-mono mt-0.5">
                        Queue ID: {item.queueEntryId} · Size: {(item.fileSizeBytes / (1024 * 1024)).toFixed(1)} MB
                      </p>
                    </div>
                    <Pill tone="red">Upload Failed</Pill>
                  </div>

                  {/* Thumbnail / Integrity Indicator */}
                  {item.localFilePath && (
                    <VideoThumbnail
                      filePath={item.localFilePath}
                      fileName={item.fileName}
                      fileType={item.fileType}
                      compact
                    />
                  )}

                  {/* Error Detail Box */}
                  <div className="bg-[var(--cream)] border border-rose-800/30 p-2.5 space-y-1">
                    <div className="flex items-center gap-1 text-[11px] text-rose-400 font-mono">
                      <AlertCircle className="w-3.5 h-3.5 shrink-0 text-rose-400" />
                      <span>Failure Reason:</span>
                    </div>
                    <p className="text-[11px] text-[var(--ink)] leading-relaxed font-mono">
                      {item.lastError || 'Google Drive folder not found or unreachable'}
                    </p>
                    {item.driveFolderPath && (
                      <p className="text-[10px] text-[var(--stone)] pt-0.5 font-mono">
                        Target folder: <code>{item.driveFolderPath}</code>
                      </p>
                    )}
                  </div>
                </div>

                {/* Action Buttons */}
                <div className="flex items-center gap-2 pt-2 border-t border-[var(--rule)]">
                  {onEditUploadFolder && (
                    <button
                      onClick={() => onEditUploadFolder(item)}
                      disabled={busy}
                      className="flex-1 py-2 px-3 bg-[var(--ink)] hover:opacity-90 text-[var(--cream)] text-xs font-mono uppercase tracking-wider flex items-center justify-center gap-1.5 transition cursor-pointer disabled:opacity-40"
                    >
                      <FolderEdit className="w-3.5 h-3.5" />
                      <span>Select Drive Folder</span>
                    </button>
                  )}

                  {(item.lectureSessionId ? onRetryLectureUpload : onRetryUpload) && (
                    <button
                      onClick={() => {
                        if (item.lectureSessionId && onRetryLectureUpload) {
                          void onRetryLectureUpload(item.lectureSessionId);
                        } else {
                          onRetryUpload?.(item.queueEntryId);
                        }
                      }}
                      disabled={busy}
                      className="py-2 px-3 border border-[var(--rule)] bg-[var(--cream)] text-[var(--ink)] hover:bg-[var(--paper)] text-xs font-mono uppercase tracking-wider flex items-center justify-center gap-1.5 transition disabled:opacity-40 cursor-pointer"
                    >
                      <RotateCcw className="w-3.5 h-3.5 text-[var(--stone)]" />
                      <span>Retry</span>
                    </button>
                  )}

                  {onCancelUpload && (
                    <button
                      onClick={() => onCancelUpload(item.queueEntryId)}
                      disabled={busy}
                      className="py-2 px-3 border border-rose-800/40 bg-[var(--cream)] text-rose-500 hover:bg-rose-950/20 text-xs font-mono uppercase tracking-wider flex items-center justify-center gap-1 transition disabled:opacity-40 cursor-pointer"
                      title="Cancel and dismiss this failed upload"
                    >
                      <Trash2 className="w-3.5 h-3.5" />
                      <span>Cancel</span>
                    </button>
                  )}
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* 2. Duplicate Holds */}
      {duplicates.length > 0 && (
        <div className="space-y-2">
          <div className="flex items-center gap-1.5 px-1 text-xs font-mono uppercase tracking-wider text-amber-500">
            <CopyCheck className="w-3.5 h-3.5" />
            Duplicate Holds ({duplicates.length})
          </div>
          <div className="grid grid-cols-1 xl:grid-cols-2 gap-4">
            {duplicates.map((item) => (
              <div key={item.lectureSessionId} className="bg-[var(--paper)] border border-amber-800/50 p-5 space-y-3 flex flex-col justify-between font-mono">
                <div className="space-y-2">
                  <div className="flex items-start justify-between gap-2">
                    <div className="min-w-0">
                      <p className="font-serif text-base text-[var(--ink)] truncate">
                        {item.batchId || 'Unassigned'} / {item.subjectId || '—'}
                      </p>
                      <p className="text-[11px] text-[var(--stone)] font-mono truncate mt-0.5">
                        Room {item.roomId} ·{' '}
                        {new Date(item.detectedStartTime).toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' })}
                      </p>
                    </div>
                    <Pill tone="amber">Duplicate</Pill>
                  </div>
                  <p className="text-[11px] text-[var(--stone)] leading-relaxed">
                    This slot already has an accepted lecture, so this recording was held back.
                  </p>
                </div>
                <ActionButton tone="primary" onClick={() => onForceEnqueue(item)} disabled={busy} className="w-full">
                  <UploadCloud className="w-3.5 h-3.5" />
                  Upload Anyway
                </ActionButton>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* 3. Pending Match Approval */}
      <div className="space-y-3">
        <div className="flex items-center justify-between px-1">
          <span className="text-xs font-mono uppercase tracking-wider text-[var(--stone)]">
            Matches Pending Verification ({needsReview.length})
          </span>
          <Pill tone={needsReview.length > 0 ? 'amber' : 'emerald'}>
            {needsReview.length} Pending
          </Pill>
        </div>

        {needsReview.length === 0 ? (
          <div className="bg-[var(--paper)] border border-[var(--rule)] p-8 text-center space-y-2">
            <CheckCircle2 className="w-6 h-6 text-emerald-500 mx-auto" />
            <p className="font-serif text-base text-[var(--ink)]">All lecture matches verified</p>
            <p className="text-[11px] font-mono text-[var(--stone)]">// no lectures waiting for batch confirmation.</p>
          </div>
        ) : (
          <div className="grid grid-cols-1 xl:grid-cols-2 gap-4">
            {needsReview.map((item) => {
              const file = getLectureFileInfo(item);
              return (
                <div key={item.lectureSessionId} className="bg-[var(--paper)] border border-[var(--rule)] p-5 hover:border-[var(--stone)] transition-colors space-y-3 flex flex-col justify-between">
                  <div className="space-y-3">
                    <div className="flex items-start justify-between gap-2">
                      <div>
                        <div className="flex items-center gap-1.5">
                          <span className="text-xs font-bold text-slate-900">Room {item.roomId}</span>
                          <span className="text-[10px] text-slate-500 font-mono">
                            {new Date(item.detectedStartTime).toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' })}
                            {' - '}
                            {new Date(item.detectedEndTime).toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' })}
                          </span>
                        </div>
                        <p className="text-[10px] text-slate-400 font-mono truncate max-w-[220px]">{item.lectureSessionId}</p>
                      </div>

                      <div className={`px-2 py-1 rounded-lg text-xs font-bold flex items-center gap-1 border ${
                        item.confidenceScore >= 80 ? 'bg-emerald-50 text-emerald-700 border-emerald-200' :
                        item.confidenceScore >= 60 ? 'bg-amber-50 text-amber-700 border-amber-200' :
                        'bg-red-50 text-red-700 border-red-200'
                      }`}>
                        <span>{item.confidenceScore}%</span>
                        <span className="text-[9px] uppercase font-medium">Conf.</span>
                      </div>
                    </div>

                    {/* Visual Video Thumbnail & Integrity Check */}
                    {file.hasFile && (
                      <VideoThumbnail
                        filePath={item.videoFilePath || item.pdfFilePath}
                        fileName={file.fileName}
                        fileType={file.fileType}
                      />
                    )}

                    {/* Prominent File Display */}
                    {file.hasFile ? (
                      <div className="bg-slate-50 border border-slate-200 rounded-xl p-2.5 flex items-center justify-between gap-2">
                        <div className="flex items-center gap-2 min-w-0">
                          <div className={`p-1.5 rounded-lg shrink-0 ${file.fileType === 'PDF' ? 'bg-rose-100 text-rose-700' : 'bg-cyan-100 text-cyan-700'}`}>
                            {file.fileType === 'PDF' ? <FileText className="w-4 h-4" /> : <Video className="w-4 h-4" />}
                          </div>
                          <div className="min-w-0">
                            <span className="text-xs font-mono font-bold text-slate-800 truncate block">
                              {file.fileName}
                            </span>
                            <span className="text-[10px] text-slate-500 font-mono">
                              {formatFileSize(file.fileSize)} · {file.fileType === 'PDF' ? 'PDF Notes' : 'Video Recording'}
                            </span>
                          </div>
                        </div>
                        <div className="flex items-center gap-1.5 shrink-0">
                          <button
                            type="button"
                            onClick={() => setPreviewLecture(item)}
                            className="px-2.5 py-1 text-[11px] font-bold rounded-lg bg-cyan-50 hover:bg-cyan-100 text-cyan-700 border border-cyan-200 flex items-center gap-1 active:scale-95 transition cursor-pointer"
                          >
                            <Play className="w-3 h-3 fill-cyan-600" />
                            <span>Preview</span>
                          </button>
                          <Pill tone={file.status === 'Uploaded' ? 'emerald' : file.status === 'Uploading' ? 'cyan' : file.status === 'Failed' ? 'red' : 'slate'}>
                            {file.status === 'Uploading' ? `${file.progress}%` : file.status}
                          </Pill>
                        </div>
                      </div>
                    ) : (
                      <div className="bg-amber-50/70 border border-amber-200 rounded-xl p-2.5 flex items-center justify-between gap-2">
                        <div className="flex items-center gap-2 min-w-0">
                          <AlertCircle className="w-4 h-4 text-amber-600 shrink-0" />
                          <div className="min-w-0">
                            <span className="text-xs font-semibold text-amber-900 block truncate">No file attached yet</span>
                            <span className="text-[10px] text-amber-700">Attach file from PC if recording already finished</span>
                          </div>
                        </div>
                        <button
                          type="button"
                          onClick={() => handleOpenUpload(item)}
                          className="px-2.5 py-1 text-[11px] font-bold rounded-lg bg-amber-600 hover:bg-amber-700 text-white shadow-xs active:scale-95 transition shrink-0"
                        >
                          + Attach File
                        </button>
                      </div>
                    )}

                    {/* Suggested Match Details */}
                    <div className="bg-slate-50 border border-slate-200 rounded-xl p-2.5 flex items-center justify-between">
                      <div>
                        <span className="text-[10px] uppercase tracking-wider text-slate-500 block font-semibold">Suggested Match</span>
                        <span className="text-sm font-bold text-cyan-700">{item.batchId || 'Unassigned'}</span>
                        <span className="text-xs text-slate-600 ml-1.5">/ {item.subjectId || 'Manual'}</span>
                      </div>
                      <span className="text-[11px] text-slate-500 font-mono">
                        ~{Math.round(item.detectedDurationSeconds / 60)} min
                      </span>
                    </div>

                    {item.matchStatus === 'NoMatch' && item.failureReason && (
                      <div className="bg-rose-50 border border-rose-200 rounded-xl p-2 text-[11px] text-rose-700 leading-relaxed">
                        <AlertCircle className="w-3 h-3 inline mr-1 align-[-1px]" />
                        {item.failureReason}
                      </div>
                    )}

                    {renderQcPanel(item)}
                    {renderYouTubePanel(item)}

                    {/* Target Drive Folder with Direct Selector */}
                    <div className="flex items-center justify-between pt-0.5">
                      <div className="text-[11px] text-slate-600 flex items-center gap-1.5 font-mono truncate">
                        <Folder className="w-3.5 h-3.5 text-cyan-600 shrink-0" />
                        <span className="truncate">
                          Target Folder: <strong className="text-slate-800">{item.driveFolderPath || item.batchId || 'Default'}</strong>
                        </span>
                      </div>
                      <button
                        type="button"
                        onClick={() => onEdit(item)}
                        className="text-[11px] font-semibold text-cyan-700 hover:text-cyan-800 flex items-center gap-1 px-2 py-0.5 rounded-md hover:bg-cyan-50 border border-cyan-200/80 active:scale-95 transition shrink-0 ml-2"
                      >
                        <FolderEdit className="w-3 h-3" />
                        <span>Select Folder</span>
                      </button>
                    </div>
                  </div>

                  <div className="space-y-2 pt-2 border-t border-slate-100">
                    <div className="grid grid-cols-3 gap-2">
                      <ActionButton
                        tone="emerald"
                        onClick={() => {
                          if (!item.batchId || item.batchId === 'Unassigned') {
                            onEdit(item);
                          } else {
                            onApprove(item);
                          }
                        }}
                        disabled={busy}
                        className="py-2 text-xs font-semibold"
                      >
                        <Check className="w-3.5 h-3.5" />
                        {!item.batchId || item.batchId === 'Unassigned' ? 'Assign & Upload' : 'Approve'}
                      </ActionButton>

                      <ActionButton onClick={() => onEdit(item)} disabled={busy} className="py-2 text-xs">
                        <FolderEdit className="w-3.5 h-3.5 text-slate-700" />
                        Folder / Edit
                      </ActionButton>

                      <ActionButton tone="danger" onClick={() => onCancelLecture(item)} disabled={busy} className="py-2 text-xs">
                        <Trash2 className="w-3.5 h-3.5" />
                        Cancel
                      </ActionButton>
                    </div>

                    {rematchResult?.lectureId === item.lectureSessionId && (
                      <div className={`rounded-lg px-2 py-1.5 text-[10px] ${rematchResult.success ? 'bg-emerald-50 text-emerald-700 border border-emerald-200' : 'bg-rose-50 text-rose-700 border border-rose-200'}`}>
                        {rematchResult.message}
                      </div>
                    )}

                    <button
                      onClick={() => void handleRematch(item)}
                      disabled={busy || rematchingId === item.lectureSessionId}
                      className="w-full flex items-center justify-center gap-1.5 text-[10px] text-slate-500 hover:text-cyan-700 active:scale-95 transition disabled:opacity-40 py-0.5"
                    >
                      {rematchingId === item.lectureSessionId ? (
                        <Loader2 className="w-3 h-3 animate-spin" />
                      ) : (
                        <RefreshCw className="w-3 h-3" />
                      )}
                      Re-run matching engine for this lecture
                    </button>
                  </div>
                </div>
              );
            })}
          </div>
        )}
      </div>

      {/* 4. Approved & In-Transit Uploads (Waiting for Google Drive) */}
      {awaitingUpload.length > 0 && (
        <div className="space-y-3 pt-2">
          <div className="flex items-center justify-between px-1">
            <span className="text-xs font-mono uppercase tracking-wider text-[var(--stone)] flex items-center gap-1.5">
              <UploadCloud className="w-3.5 h-3.5 text-[var(--ink)]" />
              <span>In Transit to Google Drive ({awaitingUpload.length})</span>
            </span>
            <span className="text-[10px] font-mono text-[var(--stone)]">// leaves hub once uploaded</span>
          </div>

          <div className="grid grid-cols-1 xl:grid-cols-2 gap-4">
            {awaitingUpload.map((item) => {
              const file = getLectureFileInfo(item);
              return (
                <div
                  key={item.lectureSessionId}
                  className="bg-[var(--paper)] border border-[var(--rule)] p-5 space-y-3 flex flex-col justify-between hover:border-[var(--stone)] transition-colors"
                >
                  <div className="space-y-2.5 font-mono">
                    <div className="flex items-start justify-between gap-2">
                      <div className="min-w-0">
                        <div className="flex items-center gap-2 flex-wrap">
                          <span className="font-serif text-base text-[var(--ink)] truncate">
                            {item.batchId || 'Unassigned'} · {item.subjectId || 'Lecture'}
                          </span>
                          <span className="text-[9px] px-1.5 py-0.2 border border-[var(--rule)] bg-[var(--cream)] text-[var(--stone)]">
                            Room {item.roomId}
                          </span>
                        </div>
                        <p className="text-[10px] text-[var(--stone)] truncate mt-0.5">
                          {item.lectureSessionId}
                        </p>
                      </div>

                      <div className="flex items-center gap-1.5 shrink-0">
                        <Pill tone={item.status === 'Uploading' ? 'cyan' : 'slate'}>
                          {item.status === 'Uploading' ? 'Uploading...' : 'Queued'}
                        </Pill>
                        <button
                          onClick={() => onEdit(item)}
                          disabled={busy}
                          title="Change target batch or Google Drive folder"
                          className="p-1 border border-[var(--rule)] bg-[var(--cream)] hover:bg-[var(--paper)] text-[var(--ink)] cursor-pointer transition"
                        >
                          <Edit3 className="w-3.5 h-3.5" />
                        </button>
                      </div>
                    </div>

                    {/* File information box */}
                    {file.hasFile ? (
                      <div className="bg-[var(--cream)] border border-[var(--rule)] p-2.5 flex items-center justify-between gap-2">
                        <div className="flex items-center gap-2 min-w-0">
                          <div className="w-6 h-6 border border-[var(--rule)] bg-[var(--paper)] flex items-center justify-center text-[var(--stone)] shrink-0">
                            {file.fileType === 'PDF' ? <FileText className="w-3.5 h-3.5" /> : <Video className="w-3.5 h-3.5" />}
                          </div>
                          <div className="min-w-0">
                            <span className="text-xs font-mono text-[var(--ink)] truncate block">
                              {file.fileName}
                            </span>
                            <span className="text-[10px] text-[var(--stone)] font-mono">
                              {formatFileSize(file.fileSize)} · {file.fileType === 'PDF' ? 'PDF Notes' : 'Video Recording'}
                            </span>
                          </div>
                        </div>
                        {file.progress > 0 && (
                          <span className="text-[11px] font-mono text-[var(--ink)] shrink-0">
                            {file.progress}%
                          </span>
                        )}
                      </div>
                    ) : (
                      <div className="bg-[var(--cream)] border border-[var(--rule)] p-2 flex items-center justify-between gap-2 text-xs text-[var(--stone)]">
                        <span className="text-[11px]">No recording file attached</span>
                        <button
                          type="button"
                          onClick={() => handleOpenUpload(item)}
                          className="text-[10px] font-mono uppercase text-[var(--ink)] hover:underline"
                        >
                          + Attach File
                        </button>
                      </div>
                    )}

                    {/* Target Google Drive folder bar with direct selector */}
                    <div className="flex items-center justify-between pt-0.5 text-xs font-mono">
                      <div className="flex items-center gap-1.5 text-[11px] text-[var(--stone)] truncate">
                        <Folder className="w-3.5 h-3.5 text-[var(--stone)] shrink-0" />
                        <span className="truncate">
                          Target Folder: <strong className="text-[var(--ink)] font-medium">{item.driveFolderPath || item.batchId || 'Default'}</strong>
                        </span>
                      </div>
                      <button
                        type="button"
                        onClick={() => onEdit(item)}
                        className="text-[10px] font-mono uppercase tracking-wider text-[var(--ink)] hover:underline flex items-center gap-1 shrink-0 ml-2 cursor-pointer"
                      >
                        <FolderEdit className="w-3 h-3" />
                        <span>Select Folder</span>
                      </button>
                    </div>
                  </div>

                  {/* Actions Bar for in-transit lectures */}
                  <div className="flex items-center gap-2 pt-2 border-t border-[var(--rule)] font-mono">
                    <button
                      type="button"
                      onClick={() =>
                        onEnqueueUpload
                          ? onEnqueueUpload(item)
                          : file.hasFile && onRetryUpload
                          ? onRetryUpload(item.lectureSessionId)
                          : undefined
                      }
                      disabled={busy || !file.hasFile}
                      className="flex-1 py-2 px-2.5 bg-[var(--ink)] hover:opacity-90 text-[var(--cream)] text-xs uppercase tracking-wider flex items-center justify-center gap-1.5 transition disabled:opacity-40 cursor-pointer"
                    >
                      <UploadCloud className="w-3.5 h-3.5" />
                      <span>Upload Now</span>
                    </button>

                    <button
                      type="button"
                      onClick={() => onEdit(item)}
                      disabled={busy}
                      className="py-2 px-3 border border-[var(--rule)] bg-[var(--cream)] hover:bg-[var(--paper)] text-[var(--ink)] text-xs uppercase tracking-wider flex items-center justify-center gap-1.5 transition cursor-pointer"
                    >
                      <FolderEdit className="w-3.5 h-3.5 text-[var(--stone)]" />
                      <span>Folder</span>
                    </button>

                    <button
                      type="button"
                      onClick={() => onCancelLecture(item)}
                      disabled={busy}
                      className="py-2 px-3 border border-rose-800/40 bg-[var(--cream)] hover:bg-rose-950/20 text-rose-500 text-xs uppercase tracking-wider flex items-center justify-center gap-1 transition cursor-pointer"
                      title="Cancel and remove this lecture"
                    >
                      <Trash2 className="w-3.5 h-3.5" />
                      <span>Cancel</span>
                    </button>
                  </div>
                </div>
              );
            })}
          </div>
        </div>
      )}

      {/* 5. Completed lectures: optional QC and YouTube publishing controls */}
      {completedLectures.length > 0 && (
        <div className="space-y-3 pt-2">
          <div className="flex items-baseline justify-between px-1 pb-2 border-b border-[var(--rule)]">
            <div>
              <div className="font-mono text-[10px] uppercase tracking-wider text-[var(--stone)]">
                ARCHIVE · VERIFIED UPLOADS
              </div>
              <h3 className="font-serif text-lg font-normal text-[var(--ink)]">
                Delivered Lectures ({completedLectures.length})
              </h3>
            </div>
            <span className="text-[10px] font-mono text-[var(--stone)]">// QC & distribution ready</span>
          </div>
          <div className="grid grid-cols-1 xl:grid-cols-2 gap-4">
            {completedLectures.slice(0, 12).map((item) => (
              <div key={item.lectureSessionId} className="bg-[var(--paper)] border border-[var(--rule)] p-5 space-y-3 transition-colors hover:border-[var(--stone)]">
                <div className="flex items-start justify-between gap-2">
                  <div className="min-w-0">
                    <p className="font-serif text-base text-[var(--ink)] truncate">{item.batchId || 'Unassigned'} / {item.subjectId || 'Lecture'}</p>
                    <p className="text-[10px] text-[var(--stone)] font-mono truncate">Room {item.roomId} · {item.lectureSessionId}</p>
                  </div>
                  <div className="flex items-center gap-2 shrink-0">
                    <button
                      type="button"
                      onClick={() => setPreviewLecture(item)}
                      className="px-2.5 py-1 text-[10px] font-mono uppercase tracking-wider border border-[var(--rule)] bg-[var(--cream)] text-[var(--ink)] hover:bg-[var(--paper)] flex items-center gap-1 transition cursor-pointer"
                    >
                      <Play className="w-2.5 h-2.5 fill-current" />
                      <span>Preview</span>
                    </button>
                    <Pill tone="emerald">{item.status}</Pill>
                  </div>
                </div>
                {renderQcPanel(item)}
                {renderYouTubePanel(item)}
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Manual Upload from PC Modal */}
      <Modal
        open={uploadModalOpen}
        title="Upload Recording or Notes from PC"
        icon={<UploadCloud className="w-4 h-4 text-[var(--ink)]" />}
        maxWidth="max-w-xl sm:max-w-2xl"
        onClose={() => !uploading && setUploadModalOpen(false)}
      >
        <form onSubmit={handleFileSubmit} className="space-y-4 font-mono text-xs">
          {uploadError && (
            <div className="p-3 border border-rose-800/40 bg-rose-950/20 text-rose-400 text-xs">
              ❌ {uploadError}
            </div>
          )}

          {/* Drag & Drop File Picker */}
          <div>
            <label className="block text-[10px] uppercase tracking-widest text-[var(--stone)] mb-1.5">
              Select Lecture File (Video or Notes PDF)
            </label>
            <div
              onClick={() => fileInputRef.current?.click()}
              onDragOver={(e) => { e.preventDefault(); e.stopPropagation(); }}
              onDrop={(e) => {
                e.preventDefault();
                e.stopPropagation();
                if (e.dataTransfer.files && e.dataTransfer.files.length > 0) {
                  const file = e.dataTransfer.files[0];
                  setSelectedFile(file);
                  if (!uploadSubject) {
                    const lower = file.name.toLowerCase();
                    const detected = STANDARD_SUBJECTS.find((s) => lower.includes(s.toLowerCase()));
                    if (detected) {
                      setUploadSubject(detected);
                    } else {
                      const isPdf = file.name.toLowerCase().endsWith('.pdf');
                      setUploadSubject(isPdf ? 'Notes' : 'Physics');
                    }
                  }
                }
              }}
              className={`border border-dashed p-5 text-center cursor-pointer transition ${
                selectedFile
                  ? 'border-[var(--ink)] bg-[var(--cream)]'
                  : 'border-[var(--rule)] hover:border-[var(--stone)] bg-[var(--cream)]'
              }`}
            >
              <input
                ref={fileInputRef}
                type="file"
                accept=".mp4,.mkv,.mov,.avi,.webm,.pdf"
                onChange={(e) => {
                  if (e.target.files && e.target.files.length > 0) {
                    const file = e.target.files[0];
                    setSelectedFile(file);
                    if (!uploadSubject) {
                      const lower = file.name.toLowerCase();
                      const detected = STANDARD_SUBJECTS.find((s) => lower.includes(s.toLowerCase()));
                      if (detected) {
                        setUploadSubject(detected);
                      } else {
                        const isPdf = file.name.toLowerCase().endsWith('.pdf');
                        setUploadSubject(isPdf ? 'Notes' : 'Physics');
                      }
                    }
                  }
                }}
                className="hidden"
              />
              {selectedFile ? (
                <div className="flex items-center justify-between gap-3 text-left">
                  <div className="flex items-center gap-2.5 min-w-0">
                    <div className="w-8 h-8 border border-[var(--rule)] bg-[var(--paper)] flex items-center justify-center text-[var(--stone)] shrink-0">
                      {selectedFile.name.toLowerCase().endsWith('.pdf') ? (
                        <FileText className="w-4 h-4" />
                      ) : (
                        <Video className="w-4 h-4" />
                      )}
                    </div>
                    <div className="min-w-0">
                      <p className="text-xs text-[var(--ink)] truncate max-w-[200px] sm:max-w-[280px]">{selectedFile.name}</p>
                      <p className="text-[10px] text-[var(--stone)] font-mono">
                        {formatFileSize(selectedFile.size)} · Click to replace
                      </p>
                    </div>
                  </div>
                  <button
                    type="button"
                    onClick={(e) => {
                      e.stopPropagation();
                      setPreviewUploadFile(selectedFile);
                    }}
                    className="px-3 py-1.5 border border-[var(--rule)] bg-[var(--paper)] text-[var(--ink)] text-xs uppercase tracking-wider flex items-center gap-1.5 transition cursor-pointer"
                  >
                    <Play className="w-3 h-3 fill-current" />
                    <span>Preview</span>
                  </button>
                </div>
              ) : (
                <div className="space-y-1">
                  <UploadCloud className="w-6 h-6 text-[var(--stone)] mx-auto" />
                  <p className="text-xs text-[var(--ink)]">Click or drag & drop lecture file here</p>
                  <p className="text-[10px] text-[var(--stone)]">Supports .mp4, .mkv, .mov, .webm, .pdf</p>
                </div>
              )}
            </div>
          </div>

          {/* Room Selector with Search */}
          <div>
            <label className="block text-[10px] uppercase tracking-widest text-[var(--stone)] mb-1">
              Classroom Room
            </label>
            <SearchableRoomSelect
              rooms={rooms}
              selectedRoom={uploadRoom}
              onSelectRoom={setUploadRoom}
              includeAllOption={false}
              placeholder="Select classroom..."
            />
          </div>

          {/* Single Batch / Google Drive Folder Name */}
          <div className="space-y-1">
            <label className="block text-[10px] uppercase tracking-widest text-[var(--stone)]">
              Batch / Drive Folder Name <span className="text-[var(--clay)]">*</span>
            </label>
            <input
              type="text"
              value={uploadBatch}
              onChange={(e) => {
                const val = e.target.value;
                setUploadBatch(val);
                setUploadDriveFolder(val);
              }}
              placeholder="e.g. 27-AJ273MA 2027"
              className={inputClass}
              required
            />
            {uploadBatch.trim() && (
              <div className="border border-[var(--rule)] bg-[var(--cream)] px-2.5 py-1.5 flex items-center gap-1.5 text-[10px] font-mono text-[var(--stone)]">
                <Folder className="w-3 h-3 text-[var(--stone)] shrink-0" />
                <span>Google Drive / </span>
                <strong className="text-[var(--ink)] font-normal">{uploadBatch.trim()}</strong>
                <span>/</span>
                <strong className="text-[var(--ink)] font-normal">{uploadSubject.trim() || 'Physics'}</strong>
              </div>
            )}
          </div>

          {/* Subject with Quick Chips */}
          <div className="space-y-1.5">
            <label className="block text-[10px] uppercase tracking-widest text-[var(--stone)]">
              Subject <span className="text-[var(--clay)]">*</span>
            </label>
            <input
              type="text"
              value={uploadSubject}
              onChange={(e) => setUploadSubject(e.target.value)}
              placeholder="e.g. Physics"
              className={inputClass}
              required
            />
            <div className="flex flex-wrap gap-1.5 pt-0.5">
              {STANDARD_SUBJECTS.map((sub) => {
                const active = uploadSubject.trim().toUpperCase() === sub.toUpperCase();
                return (
                  <button
                    key={sub}
                    type="button"
                    onClick={() => setUploadSubject(sub)}
                    className={`px-2 py-0.5 text-[10px] uppercase tracking-wider border transition cursor-pointer ${
                      active
                        ? 'bg-[var(--ink)] text-[var(--cream)] border-[var(--ink)] font-bold'
                        : 'bg-[var(--cream)] hover:bg-[var(--paper)] text-[var(--ink)] border-[var(--rule)]'
                    }`}
                  >
                    {sub}
                  </button>
                );
              })}
            </div>
          </div>

          <div className="pt-2">
            <button
              type="submit"
              disabled={uploading || !selectedFile}
              className="w-full py-3 bg-[var(--ink)] hover:opacity-90 active:opacity-100 text-[var(--cream)] font-mono text-xs uppercase tracking-widest flex items-center justify-center gap-2 transition disabled:opacity-40 cursor-pointer"
            >
              {uploading ? (
                <>
                  <Loader2 className="w-4 h-4 animate-spin" />
                  <span>Uploading to PC & Queuing to Drive...</span>
                </>
              ) : (
                <>
                  <UploadCloud className="w-4 h-4" />
                  <span>Upload & Queue to Google Drive →</span>
                </>
              )}
            </button>
          </div>
        </form>
      </Modal>

      {/* Media Preview Player (Video, PDF & YouTube) */}
      <MediaPreviewModal
        open={Boolean(previewLecture || previewUploadFile)}
        onClose={() => {
          setPreviewLecture(null);
          setPreviewUploadFile(null);
        }}
        lecture={previewLecture}
        file={previewUploadFile}
        onPublishToYouTube={onPublishToYouTube}
      />

      {/* Manual YouTube Publishing Helper Dialog */}
      {manualYouTubeLecture && (
        <Modal
          open={!!manualYouTubeLecture}
          title="Publish Lecture to YouTube (Manual)"
          icon={<Video className="w-4 h-4 text-red-600" />}
          maxWidth="max-w-lg"
          onClose={() => setManualYouTubeLecture(null)}
        >
          <div className="space-y-4 font-mono text-xs">
            <div className="p-3 border border-emerald-300 bg-emerald-50 text-emerald-900 text-xs leading-relaxed space-y-1">
              <div className="font-bold flex items-center gap-1.5">
                <span>✓</span>
                <span>Verified in Google Drive</span>
              </div>
              <p className="text-[11px] text-emerald-800">
                This lecture is safely archived in Google Drive. Center members can publish it to YouTube Studio using the pre-formatted title and details below.
              </p>
            </div>

            <div className="space-y-3 bg-[var(--cream)] p-3 border border-[var(--rule)]">
              <div>
                <span className="text-[10px] uppercase tracking-wider text-[var(--stone)] block mb-0.5">Title:</span>
                <span className="font-bold text-[var(--ink)] text-xs select-all">
                  {`[${manualYouTubeLecture.batchId || 'Batch'}] ${manualYouTubeLecture.subjectId || 'Lecture'} - Room ${manualYouTubeLecture.roomId} (${new Date(manualYouTubeLecture.detectedStartTime).toLocaleDateString()})`}
                </span>
              </div>
              <div className="grid grid-cols-2 gap-2 text-[11px]">
                <div>
                  <span className="text-[10px] uppercase tracking-wider text-[var(--stone)] block">Batch / Room:</span>
                  <span className="text-[var(--ink)] font-medium">{manualYouTubeLecture.batchId || 'Unassigned'} · Room {manualYouTubeLecture.roomId}</span>
                </div>
                <div>
                  <span className="text-[10px] uppercase tracking-wider text-[var(--stone)] block">Subject:</span>
                  <span className="text-[var(--ink)] font-medium">{manualYouTubeLecture.subjectId || 'Standard'}</span>
                </div>
              </div>
              <div>
                <span className="text-[10px] uppercase tracking-wider text-[var(--stone)] block">Drive Location:</span>
                <span className="text-[var(--ink)] text-[11px] truncate block">{manualYouTubeLecture.driveFolderPath || 'Root Classroom Folder'}</span>
              </div>
            </div>

            <div className="flex flex-col sm:flex-row items-center gap-2 pt-2">
              <a
                href="https://studio.youtube.com"
                target="_blank"
                rel="noreferrer"
                className="w-full sm:flex-1 py-2.5 px-3 bg-red-600 hover:bg-red-700 text-white font-bold text-center text-xs uppercase tracking-wider transition flex items-center justify-center gap-1.5 cursor-pointer"
              >
                <span>Open YouTube Studio ↗</span>
              </a>
              <button
                type="button"
                onClick={() => {
                  const title = `[${manualYouTubeLecture.batchId || 'Batch'}] ${manualYouTubeLecture.subjectId || 'Lecture'} - Room ${manualYouTubeLecture.roomId} (${new Date(manualYouTubeLecture.detectedStartTime).toLocaleDateString()})`;
                  navigator.clipboard.writeText(title);
                  setCopiedInfo(true);
                  setTimeout(() => setCopiedInfo(false), 2000);
                }}
                className="w-full sm:w-auto py-2.5 px-4 border border-[var(--rule)] bg-[var(--paper)] hover:bg-[var(--cream)] text-[var(--ink)] text-xs uppercase tracking-wider transition cursor-pointer"
              >
                {copiedInfo ? '✓ Copied!' : 'Copy Title'}
              </button>
            </div>
          </div>
        </Modal>
      )}

    </div>
  );
}
