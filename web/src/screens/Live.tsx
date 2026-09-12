import {
  AlertCircle,
  Ban,
  CalendarX,
  CheckCircle2,
  CloudUpload,
  FileText,
  Folder,
  MonitorX,
  PauseCircle,
  RotateCcw,
  Video,
} from 'lucide-react';
import type { AgentInfo, ControlState, HealthStatus, MissingSlot, MonitorSnapshot, TimetableEntry } from '../types';
import { Card, Pill } from '../ui';

interface LiveScreenProps {
  health: HealthStatus | null;
  snapshot: MonitorSnapshot | null;
  agentInfo: AgentInfo | null;
  controlState: ControlState | null;
  timetable: TimetableEntry[];
  selectedRoom: string;
  pendingReviewCount: number;
  missingSlots: MissingSlot[];
  busy: boolean;
  onRetryEntry: (queueEntryId: string) => void;
  onCancelEntry: (queueEntryId: string) => void;
  onRetryAllFailed: () => void;
}

function formatUptime(seconds: number): string {
  const h = Math.floor(seconds / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  return h > 0 ? `${h}h ${m}m` : `${m}m`;
}

function currentSlot(timetable: TimetableEntry[]): TimetableEntry | null {
  const now = new Date();
  const nowMinutes = now.getHours() * 60 + now.getMinutes();

  const toMinutes = (time: string) => {
    const [h, m] = time.split(':').map(Number);
    return (h || 0) * 60 + (m || 0);
  };

  return (
    timetable.find((slot) => {
      const start = toMinutes(slot.slotStartTime);
      const end = toMinutes(slot.slotEndTime);
      return nowMinutes >= start && nowMinutes < end;
    }) ?? null
  );
}

export function LiveScreen({
  health,
  snapshot,
  agentInfo,
  controlState,
  timetable,
  selectedRoom,
  pendingReviewCount,
  missingSlots,
  busy,
  onRetryEntry,
  onCancelEntry,
  onRetryAllFailed,
}: LiveScreenProps) {
  const activeQueue = snapshot?.queue.filter((q) => q.status === 'Uploading' || q.status === 'Pending') ?? [];
  const failedCount = snapshot?.summary.failed ?? 0;
  const slot = currentSlot(timetable);

  return (
    <div className="space-y-4">
      {missingSlots.length > 0 && (
        <div className="bg-red-500/10 border border-red-500/40 rounded-xl px-3 py-2.5 flex items-start gap-2">
          <CalendarX className="w-4 h-4 text-red-400 shrink-0 mt-0.5" />
          <div className="min-w-0">
            <p className="text-[11px] font-bold text-red-200">
              {missingSlots.length} lecture{missingSlots.length > 1 ? 's' : ''} missing today (Room {selectedRoom})
            </p>
            <ul className="text-[10px] text-red-300/90 leading-relaxed mt-0.5 space-y-0.5">
              {missingSlots.slice(0, 4).map((m) => (
                <li key={m.slotId || m.timetableEntryId} className="truncate">
                  {m.slotStartTime.slice(0, 5)}–{m.slotEndTime.slice(0, 5)} · {m.batchId} / {m.subjectId}
                  {m.teacherId ? ` · ${m.teacherId}` : ''}
                </li>
              ))}
              {missingSlots.length > 4 && <li>...and {missingSlots.length - 4} more</li>}
            </ul>
          </div>
        </div>
      )}

      {(controlState?.uploadsPaused || controlState?.monitoringPaused) && (
        <div className="bg-amber-500/10 border border-amber-500/40 rounded-xl px-3 py-2.5 flex items-start gap-2">
          <PauseCircle className="w-4 h-4 text-amber-400 shrink-0 mt-0.5" />
          <p className="text-[11px] text-amber-200 leading-relaxed">
            {controlState?.uploadsPaused && <><strong>Uploads paused.</strong> New queue entries will wait. </>}
            {controlState?.monitoringPaused && <><strong>Monitoring paused.</strong> New files in the folder are ignored. </>}
            Resume from the Controls tab.
          </p>
        </div>
      )}

      {/* Quick Metrics */}
      <div className="grid grid-cols-2 gap-2.5">
        <div className="bg-slate-900/60 border border-slate-800 rounded-xl p-3 flex flex-col justify-between">
          <div className="flex items-center justify-between text-slate-400">
            <span className="text-xs">Pending Reviews</span>
            <AlertCircle className={`w-4 h-4 ${pendingReviewCount > 0 ? 'text-amber-400' : 'text-slate-500'}`} />
          </div>
          <div className="mt-2 flex items-baseline gap-2">
            <span className={`text-2xl font-bold ${pendingReviewCount > 0 ? 'text-amber-400' : 'text-slate-200'}`}>
              {pendingReviewCount}
            </span>
            {pendingReviewCount > 0 && (
              <span className="text-[10px] text-amber-500/90 font-medium">Action required</span>
            )}
          </div>
        </div>

        <div className="bg-slate-900/60 border border-slate-800 rounded-xl p-3 flex flex-col justify-between">
          <div className="flex items-center justify-between text-slate-400">
            <span className="text-xs">Drive Queue</span>
            <CloudUpload className="w-4 h-4 text-cyan-400" />
          </div>
          <div className="mt-2 flex items-baseline gap-2">
            <span className="text-2xl font-bold text-cyan-400">{activeQueue.length}</span>
            <span className="text-[10px] text-slate-400">in transit</span>
          </div>
        </div>
      </div>

      {/* Room status */}
      <div className="bg-gradient-to-br from-slate-900 to-indigo-950/40 border border-slate-800/80 rounded-2xl p-4 shadow-xl">
        <div className="flex items-center justify-between border-b border-slate-800 pb-2.5 mb-3">
          <div className="flex items-center gap-2">
            <span className="text-xs font-semibold uppercase tracking-wider text-cyan-400">Room Status</span>
            <span className="px-2 py-0.5 text-[11px] font-bold rounded bg-slate-800 text-slate-200 border border-slate-700">
              {selectedRoom}
            </span>
          </div>
          <Pill tone={agentInfo?.fileWatcherEnabled && !controlState?.monitoringPaused ? 'emerald' : 'amber'}>
            {agentInfo?.fileWatcherEnabled ? (controlState?.monitoringPaused ? 'Watcher Paused' : 'Watcher Active') : 'Watcher Off'}
          </Pill>
        </div>

        <div className="space-y-2 text-xs">
          <div className="flex justify-between py-1 border-b border-slate-800/50">
            <span className="text-slate-400">Current Slot</span>
            {slot ? (
              <span className="font-semibold text-white">
                {slot.batchId} · {slot.subjectId}
              </span>
            ) : (
              <span className="text-slate-500">No slot scheduled now</span>
            )}
          </div>
          <div className="flex justify-between py-1 border-b border-slate-800/50">
            <span className="text-slate-400">Monitored Folder</span>
            <span className="font-mono text-slate-300 truncate max-w-[180px]" title={agentInfo?.monitorFolder}>
              {agentInfo?.monitorFolder || '...'}
            </span>
          </div>
          <div className="flex justify-between py-1">
            <span className="text-slate-400">Agent Uptime</span>
            <span className="font-semibold text-slate-200">
              {agentInfo ? formatUptime(agentInfo.uptimeSeconds) : '...'}
            </span>
          </div>
        </div>
      </div>

      {/* Live uploads */}
      <div className="space-y-2">
        <div className="flex items-center justify-between px-1">
          <h3 className="text-xs font-bold uppercase tracking-wider text-slate-400 flex items-center gap-1.5">
            <CloudUpload className="w-3.5 h-3.5 text-cyan-400" />
            Live Upload Feed
          </h3>
          {failedCount > 0 && (
            <button
              onClick={onRetryAllFailed}
              disabled={busy}
              className="text-[10px] font-bold px-2 py-1 rounded-lg bg-amber-500/10 text-amber-300 border border-amber-500/40 active:scale-95 transition disabled:opacity-40"
            >
              Retry all failed ({failedCount})
            </button>
          )}
        </div>

        {snapshot?.queue.length === 0 ? (
          <div className="bg-slate-900/40 border border-slate-800/60 rounded-xl p-6 text-center text-slate-500 text-xs">
            No active uploads in queue. Drop a recording into{' '}
            <code className="text-cyan-400">{agentInfo?.monitorFolder || 'the monitored folder'}</code> to test!
          </div>
        ) : (
          <div className="space-y-2">
            {snapshot?.queue.slice(0, 8).map((item) => {
              const canRetry = item.status === 'Failed' || item.status === 'FailedPermanently';
              const canCancel = item.status === 'Pending' || item.status === 'Failed' || item.status === 'FailedPermanently';

              return (
                <div key={item.queueEntryId} className="bg-slate-900/80 border border-slate-800 rounded-xl p-3.5 shadow-sm space-y-2">
                  <div className="flex items-start justify-between gap-2">
                    <div className="flex items-center gap-2 min-w-0">
                      {item.fileType === 'VIDEO' ? (
                        <Video className="w-4 h-4 text-cyan-400 shrink-0" />
                      ) : (
                        <FileText className="w-4 h-4 text-amber-400 shrink-0" />
                      )}
                      <span className="text-xs font-medium text-slate-200 truncate">{item.fileName}</span>
                    </div>
                    <span className={`text-[10px] px-2 py-0.5 rounded-full font-bold uppercase ${
                      item.status === 'Uploaded' ? 'bg-emerald-500/10 text-emerald-400 border border-emerald-500/30' :
                      item.status === 'Uploading' ? 'bg-cyan-500/10 text-cyan-400 border border-cyan-500/30 animate-pulse' :
                      item.status === 'FailedPermanently' ? 'bg-red-500/10 text-red-400 border border-red-500/30' :
                      'bg-amber-500/10 text-amber-400 border border-amber-500/30'
                    }`}>
                      {item.status}
                    </span>
                  </div>

                  <div className="space-y-1">
                    <div className="w-full bg-slate-800 rounded-full h-1.5 overflow-hidden">
                      <div
                        className={`h-full transition-all duration-300 rounded-full ${
                          item.status === 'Uploaded' ? 'bg-emerald-500' : 'bg-gradient-to-r from-cyan-500 to-indigo-500'
                        }`}
                        style={{ width: `${item.progressPercentage}%` }}
                      />
                    </div>
                    <div className="flex justify-between text-[10px] text-slate-400 font-mono">
                      <span>{(item.fileSizeBytes / (1024 * 1024)).toFixed(1)} MB</span>
                      <span>{item.progressPercentage}%</span>
                    </div>
                  </div>

                  {(canRetry || canCancel) && (
                    <div className="flex items-center gap-2 pt-1 border-t border-slate-800/60">
                      {canRetry && (
                        <button
                          onClick={() => onRetryEntry(item.queueEntryId)}
                          disabled={busy}
                          className="flex items-center gap-1 text-[10px] font-bold px-2 py-1 rounded-lg bg-cyan-500/10 text-cyan-300 border border-cyan-500/40 active:scale-95 transition disabled:opacity-40"
                        >
                          <RotateCcw className="w-3 h-3" /> Retry
                        </button>
                      )}
                      {canCancel && (
                        <button
                          onClick={() => onCancelEntry(item.queueEntryId)}
                          disabled={busy}
                          className="flex items-center gap-1 text-[10px] font-bold px-2 py-1 rounded-lg bg-red-500/10 text-red-300 border border-red-500/40 active:scale-95 transition disabled:opacity-40"
                        >
                          <Ban className="w-3 h-3" /> Cancel
                        </button>
                      )}
                      {item.lastError && (
                        <span className="text-[10px] text-red-300/80 truncate flex items-center gap-1 min-w-0">
                          <AlertCircle className="w-3 h-3 shrink-0" />
                          {item.lastError}
                        </span>
                      )}
                    </div>
                  )}

                  {item.driveFileId && (
                    <div className="text-[10px] text-slate-400 flex items-center gap-1 truncate pt-1 border-t border-slate-800/60">
                      <CheckCircle2 className="w-3 h-3 text-emerald-400 shrink-0" />
                      <span className="truncate">Drive File ID: <strong className="text-slate-300">{item.driveFileId}</strong></span>
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        )}
      </div>

      {agentInfo && (
        <Card className="text-[11px] text-slate-400 flex items-center gap-2">
          <MonitorX className={`w-3.5 h-3.5 shrink-0 ${health?.database === 'Connected' ? 'text-emerald-400' : 'text-red-400'}`} />
          <span>
            DB: <strong className="text-slate-200">{health?.database ?? '...'}</strong>
          </span>
          <span className="text-slate-600">·</span>
          <Folder className="w-3.5 h-3.5 text-cyan-400 shrink-0" />
          <span>
            Drive: <strong className="text-slate-200">{agentInfo.googleDriveEnabled ? 'Live' : 'Mock mode'}</strong>
          </span>
        </Card>
      )}
    </div>
  );
}
