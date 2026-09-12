import { Check, CheckCircle2, CopyCheck, Edit3, Folder, RefreshCw, UploadCloud } from 'lucide-react';
import type { LectureSession } from '../types';
import { ActionButton, Pill } from '../ui';

interface ReviewScreenProps {
  pendingReviews: LectureSession[];
  duplicates: LectureSession[];
  busy: boolean;
  onApprove: (lecture: LectureSession) => void;
  onEdit: (lecture: LectureSession) => void;
  onRematch: (lecture: LectureSession) => void;
  onForceEnqueue: (lecture: LectureSession) => void;
}

export function ReviewScreen({ pendingReviews, duplicates, busy, onApprove, onEdit, onRematch, onForceEnqueue }: ReviewScreenProps) {
  return (
    <div className="space-y-3">
      {duplicates.length > 0 && (
        <div className="space-y-2">
          <div className="flex items-center gap-1.5 px-1 text-[10px] font-bold uppercase tracking-wider text-amber-400">
            <CopyCheck className="w-3.5 h-3.5" />
            Duplicate Holds ({duplicates.length})
          </div>
          {duplicates.map((item) => (
            <div key={item.lectureSessionId} className="bg-amber-500/5 border border-amber-500/30 rounded-2xl p-3.5 space-y-2">
              <div className="flex items-start justify-between gap-2">
                <div className="min-w-0">
                  <p className="text-[11px] font-semibold text-amber-100 truncate">{item.batchId || 'Unassigned'} / {item.subjectId || '—'}</p>
                  <p className="text-[10px] text-amber-300/70 font-mono truncate">
                    Room {item.roomId} ·{' '}
                    {new Date(item.detectedStartTime).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                  </p>
                </div>
                <Pill tone="amber">Duplicate</Pill>
              </div>
              <p className="text-[10px] text-amber-200/70 leading-relaxed">
                This slot already has an accepted lecture, so this recording was held back.
              </p>
              <ActionButton tone="primary" onClick={() => onForceEnqueue(item)} disabled={busy} className="w-full">
                <UploadCloud className="w-3.5 h-3.5" />
                Upload Anyway
              </ActionButton>
            </div>
          ))}
        </div>
      )}

      <div className="space-y-3">
        <div className="flex items-center justify-between px-1">
          <div>
            <h2 className="text-sm font-bold text-white">Review & Approval Queue</h2>
            <p className="text-[11px] text-slate-400">1-tap verify matches or override batch/subject</p>
          </div>
          <Pill tone={pendingReviews.length > 0 ? 'amber' : 'emerald'}>
            {pendingReviews.length} Pending
          </Pill>
        </div>

      {pendingReviews.length === 0 ? (
        <div className="bg-slate-900/40 border border-slate-800/60 rounded-2xl p-8 text-center space-y-2">
          <CheckCircle2 className="w-10 h-10 text-emerald-400 mx-auto" />
          <p className="text-sm font-semibold text-slate-200">All lectures verified!</p>
          <p className="text-xs text-slate-400">Matching engine auto-assigned high-confidence slots.</p>
        </div>
      ) : (
        <div className="space-y-3">
          {pendingReviews.map((item) => (
            <div key={item.lectureSessionId} className="bg-slate-900/90 border border-slate-800 rounded-2xl p-4 shadow-lg space-y-3">
              <div className="flex items-start justify-between gap-2">
                <div>
                  <div className="flex items-center gap-1.5">
                    <span className="text-xs font-bold text-white">Room {item.roomId}</span>
                    <span className="text-[10px] text-slate-400 font-mono">
                      {new Date(item.detectedStartTime).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                      {' - '}
                      {new Date(item.detectedEndTime).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                    </span>
                  </div>
                  <p className="text-[10px] text-slate-500 font-mono truncate max-w-[220px]">{item.lectureSessionId}</p>
                </div>

                <div className={`px-2 py-1 rounded-lg text-xs font-bold flex items-center gap-1 ${
                  item.confidenceScore >= 80 ? 'bg-emerald-500/10 text-emerald-400 border border-emerald-500/30' :
                  item.confidenceScore >= 60 ? 'bg-amber-500/10 text-amber-400 border border-amber-500/30' :
                  'bg-red-500/10 text-red-400 border border-red-500/30'
                }`}>
                  <span>{item.confidenceScore}%</span>
                  <span className="text-[9px] uppercase font-normal">Conf.</span>
                </div>
              </div>

              <div className="bg-slate-800/40 border border-slate-800/80 rounded-xl p-2.5 flex items-center justify-between">
                <div>
                  <span className="text-[10px] uppercase tracking-wider text-slate-400 block font-semibold">Suggested Match</span>
                  <span className="text-sm font-bold text-cyan-400">{item.batchId || 'Unassigned'}</span>
                  <span className="text-xs text-slate-300 ml-1.5">/ {item.subjectId || 'Manual'}</span>
                </div>
                <span className="text-[11px] text-slate-400 font-mono">
                  ~{Math.round(item.detectedDurationSeconds / 60)} min
                </span>
              </div>

              <div className="text-[11px] text-slate-400 flex items-center gap-1.5 font-mono truncate">
                <Folder className="w-3.5 h-3.5 text-cyan-400 shrink-0" />
                <span className="truncate">
                  LectureRecordings/{item.centerId}/{item.batchId || 'Unassigned'}/{item.subjectId || ''}
                </span>
              </div>

              <div className="grid grid-cols-2 gap-2 pt-1 border-t border-slate-800">
                <ActionButton tone="emerald" onClick={() => onApprove(item)} disabled={busy}>
                  <Check className="w-4 h-4" />
                  Approve Match
                </ActionButton>

                <ActionButton onClick={() => onEdit(item)} disabled={busy}>
                  <Edit3 className="w-3.5 h-3.5 text-cyan-400" />
                  Change / Edit
                </ActionButton>
              </div>

              <button
                onClick={() => onRematch(item)}
                disabled={busy}
                className="w-full flex items-center justify-center gap-1.5 text-[10px] text-slate-400 hover:text-cyan-300 active:scale-95 transition disabled:opacity-40 py-1"
              >
                <RefreshCw className="w-3 h-3" />
                Re-run matching engine for this lecture
              </button>
            </div>
          ))}
        </div>
      )}
      </div>
    </div>
  );
}
