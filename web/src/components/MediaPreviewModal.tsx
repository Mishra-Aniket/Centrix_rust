import { useState, useEffect, useRef } from 'react';
import {
  FileText,
  Video,
  ExternalLink,
  Download,
  Clock,
  HardDrive,
  Folder,
} from 'lucide-react';
import type { LectureSession } from '../types';
import { Modal, Pill } from '../ui';

function YoutubeIcon({ className = 'w-4 h-4' }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="currentColor">
      <path d="M23.498 6.186a3.016 3.016 0 0 0-2.122-2.136C19.505 3.545 12 3.545 12 3.545s-7.505 0-9.377.505A3.017 3.017 0 0 0 .502 6.186C0 8.07 0 12 0 12s0 3.93.502 5.814a3.016 3.016 0 0 0 2.122 2.136c1.871.505 9.376.505 9.376.505s7.505 0 9.377-.505a3.015 3.015 0 0 0 2.122-2.136C24 15.93 24 12 24 12s0-3.93-.502-5.814zM9.545 15.568V8.432L15.818 12l-6.273 3.568z" />
    </svg>
  );
}

interface MediaPreviewModalProps {
  open: boolean;
  onClose: () => void;
  lecture?: LectureSession | null;
  file?: File | null;
  mediaType?: 'video' | 'pdf' | 'auto';
  onPublishToYouTube?: (lectureId: string) => Promise<unknown>;
}

function formatBytes(bytes?: number): string {
  if (!bytes || bytes === 0) return '0 B';
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return `${(bytes / Math.pow(k, i)).toFixed(1)} ${sizes[i]}`;
}

function formatSeconds(secs?: number): string {
  if (!secs || secs <= 0) return '0m';
  const h = Math.floor(secs / 3600);
  const m = Math.floor((secs % 3600) / 60);
  const s = secs % 60;
  if (h > 0) return `${h}h ${m}m`;
  return `${m}m ${s}s`;
}

export function MediaPreviewModal({
  open,
  onClose,
  lecture,
  file,
  mediaType = 'auto',
  onPublishToYouTube,
}: MediaPreviewModalProps) {
  const [localBlobUrl, setLocalBlobUrl] = useState<string | null>(null);
  const [viewSource, setViewSource] = useState<'local' | 'youtube'>('local');
  const [playbackRate, setPlaybackRate] = useState<number>(1);
  const [publishingYt, setPublishingYt] = useState(false);
  const videoRef = useRef<HTMLVideoElement>(null);

  // Generate object URL for raw File if provided
  useEffect(() => {
    if (file) {
      const url = URL.createObjectURL(file);
      setLocalBlobUrl(url);
      return () => {
        URL.revokeObjectURL(url);
        setLocalBlobUrl(null);
      };
    } else {
      setLocalBlobUrl(null);
    }
  }, [file]);

  // Reset viewSource when opening/switching
  useEffect(() => {
    if (open) {
      setViewSource('local');
    }
  }, [open, lecture?.lectureSessionId]);

  // Detect resolved media type
  let isPdf = false;
  if (mediaType === 'pdf') {
    isPdf = true;
  } else if (mediaType === 'video') {
    isPdf = false;
  } else if (file) {
    isPdf = file.name.toLowerCase().endsWith('.pdf');
  } else if (lecture) {
    const p = (lecture.pdfFilePath || lecture.videoFilePath || '').toLowerCase();
    isPdf = p.endsWith('.pdf');
  }

  // Determine media URL
  const mediaUrl = localBlobUrl
    ? localBlobUrl
    : lecture
    ? `/api/lectures/${lecture.lectureSessionId}/media/${isPdf ? 'pdf' : 'video'}`
    : '';

  const hasYouTube = Boolean(lecture?.youTubeId);

  const handleSpeedChange = (speed: number) => {
    setPlaybackRate(speed);
    if (videoRef.current) {
      videoRef.current.playbackRate = speed;
    }
  };

  const handlePublishYouTube = async () => {
    if (!lecture || !onPublishToYouTube) return;
    setPublishingYt(true);
    try {
      await onPublishToYouTube(lecture.lectureSessionId);
    } finally {
      setPublishingYt(false);
    }
  };

  const title = file
    ? `Preview: ${file.name}`
    : lecture
    ? `${lecture.batchId || 'Lecture'} — ${lecture.subjectId || 'Recording'}`
    : 'Media Preview';

  return (
    <Modal
      open={open}
      title={title}
      icon={
        isPdf ? (
          <FileText className="w-5 h-5 text-amber-500" />
        ) : (
          <Video className="w-5 h-5 text-cyan-500" />
        )
      }
      maxWidth="max-w-4xl"
      onClose={onClose}
    >
      <div className="space-y-4">
        {/* Source Switcher if YouTube is Available */}
        {hasYouTube && (
          <div className="flex items-center justify-between bg-slate-100/80 p-1.5 rounded-2xl border border-slate-200">
            <div className="flex gap-1">
              <button
                type="button"
                onClick={() => setViewSource('local')}
                className={`px-3 py-1.5 rounded-xl text-xs font-bold flex items-center gap-1.5 transition ${
                  viewSource === 'local'
                    ? 'bg-white text-slate-900 shadow-xs'
                    : 'text-slate-600 hover:text-slate-900'
                }`}
              >
                <Video className="w-3.5 h-3.5 text-cyan-600" />
                <span>Local Raw Video</span>
              </button>
              <button
                type="button"
                onClick={() => setViewSource('youtube')}
                className={`px-3 py-1.5 rounded-xl text-xs font-bold flex items-center gap-1.5 transition ${
                  viewSource === 'youtube'
                    ? 'bg-rose-600 text-white shadow-xs'
                    : 'text-slate-600 hover:text-rose-600'
                }`}
              >
                <YoutubeIcon className="w-3.5 h-3.5" />
                <span>YouTube Player</span>
              </button>
            </div>
            <a
              href={`https://www.youtube.com/watch?v=${lecture?.youTubeId}`}
              target="_blank"
              rel="noreferrer"
              className="text-xs font-semibold text-rose-600 hover:text-rose-700 flex items-center gap-1 px-2.5 py-1 rounded-lg hover:bg-rose-50 transition"
            >
              <span>Watch on YouTube</span>
              <ExternalLink className="w-3 h-3" />
            </a>
          </div>
        )}

        {/* Media Player Area */}
        <div className="relative rounded-2xl overflow-hidden bg-slate-950 border border-slate-800 shadow-2xl flex items-center justify-center">
          {viewSource === 'youtube' && lecture?.youTubeId ? (
            <div className="w-full aspect-video">
              <iframe
                src={`https://www.youtube.com/embed/${lecture.youTubeId}?autoplay=1`}
                title="YouTube lecture preview"
                className="w-full h-full border-0"
                allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture"
                allowFullScreen
              />
            </div>
          ) : isPdf ? (
            <div className="w-full h-[65vh] bg-slate-100 flex flex-col">
              <iframe
                src={`${mediaUrl}#toolbar=1`}
                title="PDF Document Preview"
                className="w-full h-full border-0"
              />
            </div>
          ) : (
            <div className="w-full flex flex-col items-center">
              <video
                ref={videoRef}
                controls
                autoPlay
                playsInline
                className="w-full max-h-[62vh] object-contain bg-black"
                src={mediaUrl}
              >
                Your browser does not support HTML5 video preview.
              </video>
            </div>
          )}
        </div>

        {/* Video Speed & Quick Actions Bar */}
        {!isPdf && viewSource === 'local' && (
          <div className="flex flex-wrap items-center justify-between gap-2 px-1 text-xs">
            <div className="flex items-center gap-1.5">
              <span className="text-slate-500 font-semibold">Speed:</span>
              {[1, 1.25, 1.5, 2].map((speed) => (
                <button
                  key={speed}
                  type="button"
                  onClick={() => handleSpeedChange(speed)}
                  className={`px-2 py-0.5 rounded-lg font-bold font-mono transition ${
                    playbackRate === speed
                      ? 'bg-slate-900 text-white'
                      : 'bg-slate-100 text-slate-700 hover:bg-slate-200'
                  }`}
                >
                  {speed}x
                </button>
              ))}
            </div>

            <div className="flex items-center gap-2">
              {lecture && !hasYouTube && onPublishToYouTube && (
                <button
                  type="button"
                  onClick={handlePublishYouTube}
                  disabled={publishingYt}
                  className="px-3 py-1.5 rounded-xl bg-rose-600 hover:bg-rose-700 text-white font-semibold flex items-center gap-1.5 active:scale-95 transition shadow-xs disabled:opacity-50 cursor-pointer"
                >
                  <YoutubeIcon className="w-3.5 h-3.5" />
                  <span>{publishingYt ? 'Queuing to YouTube...' : 'Publish to YouTube'}</span>
                </button>
              )}

              {mediaUrl && (
                <a
                  href={mediaUrl}
                  target="_blank"
                  rel="noreferrer"
                  download={file?.name || (lecture ? `${lecture.batchId || 'lecture'}_video.mp4` : undefined)}
                  className="px-3 py-1.5 rounded-xl bg-slate-100 hover:bg-slate-200 text-slate-700 font-semibold flex items-center gap-1.5 active:scale-95 transition cursor-pointer"
                >
                  <Download className="w-3.5 h-3.5" />
                  <span>Download / Open Tab</span>
                </a>
              )}
            </div>
          </div>
        )}

        {/* Metadata Details Card */}
        <div className="bg-slate-50 border border-slate-200/90 rounded-2xl p-3.5 space-y-2 text-xs">
          <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-200/70 pb-2">
            <div className="flex items-center gap-2 flex-wrap">
              <span className="font-bold text-slate-900">
                {file ? file.name : lecture?.batchId || 'Classroom Recording'}
              </span>
              {lecture?.subjectId && (
                <Pill tone="cyan">{lecture.subjectId}</Pill>
              )}
              {lecture?.roomId && (
                <Pill tone="slate">Room {lecture.roomId}</Pill>
              )}
              {hasYouTube && (
                <Pill tone="red">YouTube Unlisted</Pill>
              )}
            </div>

            <div className="flex items-center gap-3 text-slate-500 font-mono text-[11px]">
              <span className="flex items-center gap-1">
                <HardDrive className="w-3 h-3 text-slate-400" />
                {formatBytes(file?.size || lecture?.videoFileSize || lecture?.pdfFileSize)}
              </span>
              {lecture?.detectedDurationSeconds ? (
                <span className="flex items-center gap-1">
                  <Clock className="w-3 h-3 text-slate-400" />
                  {formatSeconds(lecture.detectedDurationSeconds)}
                </span>
              ) : null}
            </div>
          </div>

          {(lecture?.videoFilePath || lecture?.pdfFilePath) && (
            <div className="flex items-start gap-1.5 text-[11px] text-slate-500 font-mono">
              <Folder className="w-3.5 h-3.5 text-slate-400 shrink-0 mt-0.5" />
              <span className="truncate" title={lecture.videoFilePath || lecture.pdfFilePath}>
                Local Path: {lecture.videoFilePath || lecture.pdfFilePath}
              </span>
            </div>
          )}

          {lecture?.driveFolderPath && (
            <div className="flex items-start gap-1.5 text-[11px] text-emerald-700 font-mono">
              <Folder className="w-3.5 h-3.5 text-emerald-600 shrink-0 mt-0.5" />
              <span className="truncate">
                Google Drive: {lecture.driveFolderPath}
              </span>
            </div>
          )}
        </div>
      </div>
    </Modal>
  );
}
