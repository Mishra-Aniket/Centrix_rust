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
  RefreshCw,
  RotateCcw,
  Trash2,
  UploadCloud,
  Video,
} from 'lucide-react';
import { DriveFolderPicker } from '../components/DriveFolderPicker';
import { VideoThumbnail } from '../components/VideoThumbnail';
import type { LectureSession, QueueEntry } from '../types';
import { ActionButton, Modal, Pill, inputClass } from '../ui';

interface ReviewScreenProps {
  pendingReviews: LectureSession[];
  duplicates: LectureSession[];
  failedQueueItems?: QueueEntry[];
  queueItems?: QueueEntry[];
  rooms?: string[];
  busy: boolean;
  onApprove: (lecture: LectureSession) => void;
  onEdit: (lecture: LectureSession) => void;
  onCancelLecture: (lecture: LectureSession) => void;
  onEnqueueUpload?: (lecture: LectureSession) => void;
  onRematch: (lecture: LectureSession) => void;
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
  duplicates,
  failedQueueItems = [],
  queueItems = [],
  rooms = ['603'],
  busy,
  onApprove,
  onEdit,
  onCancelLecture,
  onEnqueueUpload,
  onRematch,
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
      if (uploadRoom && uploadRoom !== 'ALL') formData.append('roomId', uploadRoom);
      if (uploadBatch.trim()) formData.append('batchId', uploadBatch.trim());
      if (uploadSubject.trim()) formData.append('subjectId', uploadSubject.trim());
      if (uploadDriveFolder.trim()) formData.append('driveFolderPath', uploadDriveFolder.trim());

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

  return (
    <div className="space-y-4">
      {/* Header Banner */}
      <div className="flex items-center justify-between px-1">
        <div>
          <h2 className="text-sm font-bold text-slate-900">Review & Approval Hub</h2>
          <p className="text-[11px] text-slate-500">
            Lectures stay here until successfully uploaded to Google Drive
          </p>
        </div>
        <Pill tone={totalPendingAction > 0 ? 'amber' : 'emerald'}>
          {totalPendingAction > 0 ? `${totalPendingAction} Action Required` : 'All Clear'}
        </Pill>
      </div>

      {/* Quick Upload from PC button */}
      <div className="bg-white border border-slate-200/90 rounded-2xl p-3 shadow-xs flex items-center justify-between gap-3">
        <div className="min-w-0">
          <span className="text-xs font-bold text-slate-900 block">Missing a recording or notes file?</span>
          <p className="text-[11px] text-slate-500 truncate">
            Select a file directly from your PC and pick the Google Drive destination
          </p>
        </div>
        <button
          type="button"
          onClick={() => handleOpenUpload()}
          disabled={busy}
          className="px-3 py-2 rounded-xl bg-cyan-600 hover:bg-cyan-500 text-white font-bold text-xs shadow-xs flex items-center gap-1.5 shrink-0 active:scale-95 transition"
        >
          <UploadCloud className="w-4 h-4" />
          <span>Upload File</span>
        </button>
      </div>

      {/* 1. URGENT: Failed Uploads Needing Folder / Batch Selection */}
      {failedQueueItems.length > 0 && (
        <div className="space-y-2.5">
          <div className="flex items-center gap-1.5 px-1 text-xs font-bold uppercase tracking-wider text-rose-700">
            <AlertTriangle className="w-3.5 h-3.5 text-rose-600" />
            <span>Upload Failed — Action Needed ({failedQueueItems.length})</span>
          </div>

          <div className="grid grid-cols-1 xl:grid-cols-2 gap-4">
            {failedQueueItems.map((item) => (
              <div
                key={item.queueEntryId}
                className="bg-rose-50/70 border border-rose-200 rounded-2xl p-4 space-y-3 shadow-xs flex flex-col justify-between"
              >
                <div className="space-y-3">
                  <div className="flex items-start justify-between gap-2">
                    <div className="min-w-0">
                      <div className="flex items-center gap-1.5 flex-wrap">
                        <span className="text-xs font-bold text-rose-900 truncate">
                          {item.fileName}
                        </span>
                        {item.roomId && (
                          <span className="text-[9px] font-bold px-1.5 py-0.2 rounded bg-white text-rose-700 border border-rose-200">
                            Room {item.roomId}
                          </span>
                        )}
                      </div>
                      <p className="text-[10px] text-rose-600 font-mono mt-0.5">
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
                  <div className="bg-white border border-rose-200 rounded-xl p-2.5 space-y-1">
                    <div className="flex items-center gap-1 text-[11px] text-rose-700 font-semibold">
                      <AlertCircle className="w-3.5 h-3.5 shrink-0 text-rose-600" />
                      <span>Failure Reason:</span>
                    </div>
                    <p className="text-[11px] text-rose-800 leading-relaxed font-mono">
                      {item.lastError || 'Google Drive folder not found or unreachable'}
                    </p>
                    {item.driveFolderPath && (
                      <p className="text-[10px] text-slate-500 pt-0.5">
                        Current target folder: <code className="text-slate-800">{item.driveFolderPath}</code>
                      </p>
                    )}
                  </div>
                </div>

                {/* Action Buttons */}
                <div className="flex items-center gap-2 pt-2 border-t border-rose-200/60">
                  {onEditUploadFolder && (
                    <button
                      onClick={() => onEditUploadFolder(item)}
                      disabled={busy}
                      className="flex-1 py-2 px-3 rounded-xl font-semibold text-xs bg-indigo-600 hover:bg-indigo-700 text-white shadow-xs flex items-center justify-center gap-1.5 active:scale-95 transition disabled:opacity-40"
                    >
                      <FolderEdit className="w-3.5 h-3.5" />
                      <span>Select Drive Folder</span>
                    </button>
                  )}

                  {onRetryUpload && (
                    <button
                      onClick={() => onRetryUpload(item.queueEntryId)}
                      disabled={busy}
                      className="py-2 px-3 rounded-xl font-semibold text-xs bg-white hover:bg-slate-50 border border-slate-200 text-slate-700 shadow-xs flex items-center justify-center gap-1.5 active:scale-95 transition disabled:opacity-40"
                    >
                      <RotateCcw className="w-3.5 h-3.5 text-cyan-600" />
                      <span>Retry</span>
                    </button>
                  )}

                  {onCancelUpload && (
                    <button
                      onClick={() => onCancelUpload(item.queueEntryId)}
                      disabled={busy}
                      className="py-2 px-3 rounded-xl font-semibold text-xs bg-rose-50 hover:bg-rose-100 border border-rose-200 text-rose-700 shadow-xs flex items-center justify-center gap-1 active:scale-95 transition disabled:opacity-40"
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
          <div className="flex items-center gap-1.5 px-1 text-xs font-bold uppercase tracking-wider text-amber-700">
            <CopyCheck className="w-3.5 h-3.5" />
            Duplicate Holds ({duplicates.length})
          </div>
          <div className="grid grid-cols-1 xl:grid-cols-2 gap-4">
            {duplicates.map((item) => (
              <div key={item.lectureSessionId} className="bg-amber-50 border border-amber-200 rounded-2xl p-4 space-y-2.5 shadow-sm flex flex-col justify-between">
                <div className="space-y-2">
                  <div className="flex items-start justify-between gap-2">
                    <div className="min-w-0">
                      <p className="text-xs font-bold text-amber-900 truncate">
                        {item.batchId || 'Unassigned'} / {item.subjectId || '—'}
                      </p>
                      <p className="text-[11px] text-amber-700 font-mono truncate">
                        Room {item.roomId} ·{' '}
                        {new Date(item.detectedStartTime).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                      </p>
                    </div>
                    <Pill tone="amber">Duplicate</Pill>
                  </div>
                  <p className="text-[11px] text-amber-800 leading-relaxed">
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
          <span className="text-xs font-bold uppercase tracking-wider text-slate-600">
            Matches Pending Verification ({needsReview.length})
          </span>
          <Pill tone={needsReview.length > 0 ? 'amber' : 'emerald'}>
            {needsReview.length} Pending
          </Pill>
        </div>

        {needsReview.length === 0 ? (
          <div className="bg-white border border-slate-200/90 rounded-2xl p-6 text-center space-y-1.5 shadow-xs">
            <CheckCircle2 className="w-8 h-8 text-emerald-600 mx-auto" />
            <p className="text-xs font-bold text-slate-900">All lecture matches verified!</p>
            <p className="text-[11px] text-slate-500">No lectures waiting for batch confirmation.</p>
          </div>
        ) : (
          <div className="grid grid-cols-1 xl:grid-cols-2 gap-4">
            {needsReview.map((item) => {
              const file = getLectureFileInfo(item);
              return (
                <div key={item.lectureSessionId} className="bg-white border border-slate-200/90 rounded-2xl p-4 shadow-xs hover:border-slate-300 transition space-y-3 flex flex-col justify-between">
                  <div className="space-y-3">
                    <div className="flex items-start justify-between gap-2">
                      <div>
                        <div className="flex items-center gap-1.5">
                          <span className="text-xs font-bold text-slate-900">Room {item.roomId}</span>
                          <span className="text-[10px] text-slate-500 font-mono">
                            {new Date(item.detectedStartTime).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                            {' - '}
                            {new Date(item.detectedEndTime).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
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
                        <Pill tone={file.status === 'Uploaded' ? 'emerald' : file.status === 'Uploading' ? 'cyan' : file.status === 'Failed' ? 'red' : 'slate'}>
                          {file.status === 'Uploading' ? `${file.progress}%` : file.status}
                        </Pill>
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

                    <button
                      onClick={() => onRematch(item)}
                      disabled={busy}
                      className="w-full flex items-center justify-center gap-1.5 text-[10px] text-slate-500 hover:text-cyan-700 active:scale-95 transition disabled:opacity-40 py-0.5"
                    >
                      <RefreshCw className="w-3 h-3" />
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
        <div className="space-y-2.5 pt-2">
          <div className="flex items-center justify-between px-1">
            <span className="text-xs font-bold uppercase tracking-wider text-slate-600 flex items-center gap-1.5">
              <UploadCloud className="w-3.5 h-3.5 text-cyan-600" />
              <span>In Transit to Google Drive ({awaitingUpload.length})</span>
            </span>
            <span className="text-[10px] text-slate-400">Leaves hub once uploaded</span>
          </div>

          <div className="grid grid-cols-1 xl:grid-cols-2 gap-4">
            {awaitingUpload.map((item) => {
              const file = getLectureFileInfo(item);
              return (
                <div
                  key={item.lectureSessionId}
                  className="bg-white border border-slate-200/90 rounded-2xl p-4 shadow-xs space-y-3 flex flex-col justify-between hover:border-slate-300 transition"
                >
                  <div className="space-y-2.5">
                    <div className="flex items-start justify-between gap-2">
                      <div className="min-w-0">
                        <div className="flex items-center gap-1.5">
                          <span className="text-xs font-bold text-slate-900 truncate">
                            {item.batchId || 'Unassigned'} · {item.subjectId || 'Lecture'}
                          </span>
                          <span className="text-[9px] font-bold px-1.5 py-0.2 rounded bg-slate-100 text-slate-700 border border-slate-200">
                            Room {item.roomId}
                          </span>
                        </div>
                        <p className="text-[10px] text-slate-400 font-mono truncate mt-0.5">
                          {item.lectureSessionId}
                        </p>
                      </div>

                      <div className="flex items-center gap-2 shrink-0">
                        <Pill tone={item.status === 'Uploading' ? 'cyan' : 'slate'}>
                          {item.status === 'Uploading' ? 'Uploading...' : 'Queued'}
                        </Pill>
                        <button
                          onClick={() => onEdit(item)}
                          disabled={busy}
                          title="Change target batch or Google Drive folder"
                          className="p-1.5 rounded-lg bg-slate-50 hover:bg-slate-100 text-slate-600 border border-slate-200 active:scale-95 transition"
                        >
                          <Edit3 className="w-3.5 h-3.5" />
                        </button>
                      </div>
                    </div>

                    {/* File information box */}
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
                        {file.progress > 0 && (
                          <span className="text-[11px] font-bold font-mono text-cyan-700 shrink-0">
                            {file.progress}%
                          </span>
                        )}
                      </div>
                    ) : (
                      <div className="bg-amber-50/60 border border-amber-200 rounded-xl p-2 flex items-center justify-between gap-2 text-xs text-amber-800">
                        <span className="text-[11px]">No recording file attached</span>
                        <button
                          type="button"
                          onClick={() => handleOpenUpload(item)}
                          className="text-[10px] font-bold text-amber-700 hover:text-amber-900 underline"
                        >
                          + Attach File
                        </button>
                      </div>
                    )}

                    {/* Target Google Drive folder bar with direct selector */}
                    <div className="flex items-center justify-between pt-0.5 text-xs">
                      <div className="flex items-center gap-1.5 text-[11px] text-slate-600 font-mono truncate">
                        <Folder className="w-3.5 h-3.5 text-cyan-600 shrink-0" />
                        <span className="truncate">
                          Target Folder: <strong className="text-slate-900">{item.driveFolderPath || item.batchId || 'Default'}</strong>
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

                  {/* Actions Bar for in-transit lectures */}
                  <div className="flex items-center gap-2 pt-2 border-t border-slate-100">
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
                      className="flex-1 py-2 px-2.5 rounded-xl bg-cyan-600 hover:bg-cyan-500 text-white font-bold text-xs shadow-xs flex items-center justify-center gap-1.5 active:scale-95 transition disabled:opacity-40"
                    >
                      <UploadCloud className="w-3.5 h-3.5" />
                      <span>Upload Now</span>
                    </button>

                    <button
                      type="button"
                      onClick={() => onEdit(item)}
                      disabled={busy}
                      className="py-2 px-2.5 rounded-xl bg-slate-50 hover:bg-slate-100 border border-slate-200 text-slate-700 font-semibold text-xs flex items-center justify-center gap-1.5 active:scale-95 transition"
                    >
                      <FolderEdit className="w-3.5 h-3.5 text-cyan-600" />
                      <span>Folder</span>
                    </button>

                    <button
                      type="button"
                      onClick={() => onCancelLecture(item)}
                      disabled={busy}
                      className="py-2 px-2.5 rounded-xl bg-rose-50 hover:bg-rose-100 border border-rose-200 text-rose-700 font-semibold text-xs flex items-center justify-center gap-1 active:scale-95 transition"
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

      {/* Manual Upload from PC Modal */}
      <Modal
        open={uploadModalOpen}
        title="Upload Recording or Notes from PC"
        icon={<UploadCloud className="w-5 h-5 text-cyan-500" />}
        maxWidth="max-w-xl sm:max-w-2xl"
        onClose={() => !uploading && setUploadModalOpen(false)}
      >
        <form onSubmit={handleFileSubmit} className="space-y-4 text-xs">
          {uploadError && (
            <div className="p-2.5 rounded-xl bg-rose-50 border border-rose-200 text-rose-700 text-xs font-medium">
              ❌ {uploadError}
            </div>
          )}

          {/* Drag & Drop File Picker */}
          <div>
            <label className="block text-[11px] font-semibold text-slate-700 mb-1.5">
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
                    const isPdf = file.name.toLowerCase().endsWith('.pdf');
                    setUploadSubject(isPdf ? 'NOTES' : 'LECTURE');
                  }
                }
              }}
              className={`border-2 border-dashed rounded-xl p-3.5 text-center cursor-pointer transition ${
                selectedFile
                  ? 'border-cyan-400 bg-cyan-50/50'
                  : 'border-slate-200 hover:border-cyan-300 hover:bg-slate-50/80 bg-slate-50/30'
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
                      const isPdf = file.name.toLowerCase().endsWith('.pdf');
                      setUploadSubject(isPdf ? 'NOTES' : 'LECTURE');
                    }
                  }
                }}
                className="hidden"
              />
              {selectedFile ? (
                <div className="flex items-center justify-center gap-2.5 text-left">
                  <div className="p-2 rounded-lg bg-white border border-cyan-200 text-cyan-600 shadow-2xs">
                    {selectedFile.name.toLowerCase().endsWith('.pdf') ? (
                      <FileText className="w-4 h-4" />
                    ) : (
                      <Video className="w-4 h-4" />
                    )}
                  </div>
                  <div className="min-w-0">
                    <p className="text-xs font-bold text-slate-900 truncate max-w-[260px]">{selectedFile.name}</p>
                    <p className="text-[10px] text-slate-500 font-mono">
                      {formatFileSize(selectedFile.size)} · Click or drop another file to change
                    </p>
                  </div>
                </div>
              ) : (
                <div className="space-y-1">
                  <UploadCloud className="w-5 h-5 text-slate-400 mx-auto" />
                  <p className="text-xs font-semibold text-slate-700">Click or drag & drop video/PDF here</p>
                  <p className="text-[10px] text-slate-400">Supports .mp4, .mkv, .mov, .webm, .pdf</p>
                </div>
              )}
            </div>
          </div>

          {/* Room Selector */}
          <div>
            <label className="block text-[11px] font-semibold text-slate-700 mb-1">
              Classroom
            </label>
            <select
              value={uploadRoom}
              onChange={(e) => setUploadRoom(e.target.value)}
              className={inputClass}
            >
              {rooms.map((r) => (
                <option key={r} value={r}>
                  Room {r}
                </option>
              ))}
            </select>
          </div>

          {/* Google Drive Folder Picker Component */}
          <DriveFolderPicker
            value={uploadDriveFolder}
            onChange={(folder) => {
              setUploadDriveFolder(folder);
              if (!uploadBatch) setUploadBatch(folder);
            }}
          />

          <div className="grid grid-cols-2 gap-2">
            <div>
              <label className="block text-[11px] font-semibold text-slate-700 mb-1">
                Batch Name
              </label>
              <input
                type="text"
                value={uploadBatch}
                onChange={(e) => setUploadBatch(e.target.value)}
                placeholder="e.g. JEE-2026"
                className={inputClass}
                required
              />
            </div>
            <div>
              <label className="block text-[11px] font-semibold text-slate-700 mb-1">
                Subject
              </label>
              <input
                type="text"
                value={uploadSubject}
                onChange={(e) => setUploadSubject(e.target.value)}
                placeholder="e.g. PHYSICS"
                className={inputClass}
                required
              />
            </div>
          </div>

          <div className="pt-2">
            <button
              type="submit"
              disabled={uploading || !selectedFile}
              className="w-full py-2.5 rounded-xl bg-cyan-600 hover:bg-cyan-500 font-bold text-white text-xs shadow-md shadow-cyan-600/20 active:scale-95 transition disabled:opacity-40 flex items-center justify-center gap-2"
            >
              {uploading ? (
                <>
                  <Loader2 className="w-4 h-4 animate-spin" />
                  <span>Uploading to PC & Queuing to Drive...</span>
                </>
              ) : (
                <>
                  <UploadCloud className="w-4 h-4" />
                  <span>Upload & Queue to Google Drive</span>
                </>
              )}
            </button>
          </div>
        </form>
      </Modal>
    </div>
  );
}
