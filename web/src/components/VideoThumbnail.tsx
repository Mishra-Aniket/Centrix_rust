import { useEffect, useState } from 'react';
import {
  AlertTriangle,
  CheckCircle2,
  Eye,
  FileText,
  Film,
  Loader2,
  Video,
  X,
} from 'lucide-react';
import {
  getVideoThumbnail,
  validateRecordingFile,
  type FileValidationReport,
  type ThumbnailResult,
  isTauri,
} from '../tauri';

interface VideoThumbnailProps {
  filePath?: string;
  fileName?: string;
  fileType?: string;
  compact?: boolean;
}

export function VideoThumbnail({
  filePath,
  fileName,
  fileType = 'VIDEO',
  compact = false,
}: VideoThumbnailProps) {
  const [thumb, setThumb] = useState<ThumbnailResult | null>(null);
  const [validation, setValidation] = useState<FileValidationReport | null>(null);
  const [loading, setLoading] = useState(false);
  const [modalOpen, setModalOpen] = useState(false);

  useEffect(() => {
    if (!filePath || !isTauri()) return;

    let mounted = true;
    setLoading(true);

    Promise.all([
      getVideoThumbnail(filePath).catch(() => null),
      validateRecordingFile(filePath).catch(() => null),
    ]).then(([t, v]) => {
      if (!mounted) return;
      if (t) setThumb(t);
      if (v) setValidation(v);
      setLoading(false);
    });

    return () => {
      mounted = false;
    };
  }, [filePath]);

  if (fileType === 'PDF') {
    return (
      <div className="flex items-center gap-1.5 text-[10px] text-rose-700 bg-rose-50 border border-rose-200 px-2 py-1 rounded-lg font-mono">
        <FileText className="w-3.5 h-3.5 shrink-0" />
        <span className="truncate">{fileName || 'Class Notes PDF'}</span>
        {validation && !validation.isValid && (
          <span className="ml-auto text-red-600 font-bold flex items-center gap-0.5">
            <AlertTriangle className="w-3 h-3" /> Corrupted
          </span>
        )}
      </div>
    );
  }

  const isCorrupted = validation && !validation.isValid && validation.status !== 'Healthy';
  const isHealthy = validation && validation.isValid;

  return (
    <>
      <div className="space-y-1.5">
        {/* Thumbnail Box */}
        <div
          onClick={() => thumb?.dataUrl && setModalOpen(true)}
          className={`relative rounded-xl overflow-hidden bg-slate-900 border border-slate-200 flex items-center justify-center transition group ${
            compact ? 'h-24' : 'h-32 sm:h-36'
          } ${thumb?.dataUrl ? 'cursor-pointer hover:ring-2 hover:ring-cyan-500/50' : ''}`}
        >
          {loading ? (
            <div className="flex flex-col items-center gap-1.5 text-slate-400">
              <Loader2 className="w-5 h-5 animate-spin" />
              <span className="text-[10px] font-mono">Analyzing frame...</span>
            </div>
          ) : thumb?.dataUrl ? (
            <>
              <img
                src={thumb.dataUrl}
                alt={fileName || 'Video Thumbnail'}
                className="w-full h-full object-cover group-hover:scale-105 transition duration-300"
              />
              <div className="absolute inset-0 bg-black/20 group-hover:bg-black/40 transition flex items-center justify-center">
                <div className="p-1.5 rounded-full bg-white/80 group-hover:bg-white text-slate-900 shadow-md transform group-hover:scale-110 transition">
                  <Eye className="w-3.5 h-3.5" />
                </div>
              </div>
              <span className="absolute bottom-1 right-1 px-1.5 py-0.5 rounded bg-black/70 text-white text-[9px] font-mono font-bold">
                Preview
              </span>
            </>
          ) : (
            <div className="flex flex-col items-center gap-1 text-slate-400 p-2 text-center">
              <Film className="w-6 h-6 text-slate-500" />
              <span className="text-[10px] font-medium text-slate-400 truncate max-w-[180px]">
                {fileName || 'Recording Video'}
              </span>
            </div>
          )}

          {/* Validation Status Overlay Banner */}
          {isCorrupted && (
            <div className="absolute top-1 left-1 right-1 bg-rose-600/95 text-white text-[9px] font-bold px-1.5 py-0.5 rounded flex items-center gap-1 shadow-xs">
              <AlertTriangle className="w-3 h-3 shrink-0" />
              <span className="truncate">{validation.status}: {validation.errorMessage}</span>
            </div>
          )}
        </div>

        {/* Validation Details Footer */}
        {validation && (
          <div className="flex items-center justify-between text-[10px] px-0.5">
            <div className="flex items-center gap-1 min-w-0">
              {isHealthy ? (
                <span className="text-emerald-700 font-semibold flex items-center gap-1">
                  <CheckCircle2 className="w-3 h-3 text-emerald-600" /> Container Healthy
                </span>
              ) : (
                <span className="text-rose-600 font-bold flex items-center gap-1 truncate" title={validation.recommendation}>
                  <AlertTriangle className="w-3 h-3 text-rose-600 shrink-0" /> {validation.recommendation}
                </span>
              )}
            </div>
            <span className="text-slate-400 font-mono text-[9px] shrink-0">
              {validation.fileFormat?.toUpperCase() || 'FILE'}
            </span>
          </div>
        )}
      </div>

      {/* Snapshot Preview Modal */}
      {modalOpen && thumb?.dataUrl && (
        <div
          className="fixed inset-0 z-50 bg-black/80 backdrop-blur-xs flex items-center justify-center p-4"
          onClick={() => setModalOpen(false)}
        >
          <div
            className="bg-white rounded-2xl overflow-hidden max-w-2xl w-full shadow-2xl border border-slate-700"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="flex items-center justify-between px-4 py-2.5 bg-slate-900 text-white">
              <div className="flex items-center gap-2 min-w-0">
                <Video className="w-4 h-4 text-cyan-400" />
                <span className="text-xs font-bold font-mono truncate">{fileName}</span>
              </div>
              <button
                onClick={() => setModalOpen(false)}
                className="p-1 rounded-lg hover:bg-slate-800 text-slate-400 hover:text-white transition"
              >
                <X className="w-4 h-4" />
              </button>
            </div>
            <div className="bg-black flex items-center justify-center max-h-[70vh] overflow-hidden">
              <img src={thumb.dataUrl} alt={fileName} className="max-h-[70vh] w-auto object-contain" />
            </div>
            <div className="p-3 bg-slate-50 flex items-center justify-between text-xs border-t border-slate-200">
              <span className="text-slate-500 font-mono text-[11px] truncate max-w-[360px]" title={filePath}>
                {filePath}
              </span>
              <button
                onClick={() => setModalOpen(false)}
                className="px-3 py-1.5 rounded-xl bg-slate-200 hover:bg-slate-300 text-slate-800 font-semibold text-xs transition"
              >
                Close
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
