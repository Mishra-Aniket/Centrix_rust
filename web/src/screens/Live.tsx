import {
  AlertCircle,
  Ban,
  CalendarX,
  CheckCircle2,
  Clock,
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
  rooms: string[];
  selectedRoom: string;
  onSelectRoom: (roomId: string) => void;
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

function formatRelativeTime(dateStr: string): string {
  try {
    const d = new Date(dateStr);
    const now = new Date();
    const diffMs = now.getTime() - d.getTime();
    if (diffMs < 0) return 'Just now';
    const diffSec = Math.floor(diffMs / 1000);
    if (diffSec < 60) return `${diffSec}s ago`;
    const diffMin = Math.floor(diffSec / 60);
    if (diffMin < 60) return `${diffMin}m ago`;
    const diffHr = Math.floor(diffMin / 60);
    if (diffHr < 24) return `${diffHr}h ago`;
    return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  } catch {
    return '';
  }
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
  rooms,
  selectedRoom,
  onSelectRoom,
  pendingReviewCount,
  missingSlots,
  busy,
  onRetryEntry,
  onCancelEntry,
  onRetryAllFailed,
}: LiveScreenProps) {
  const activeQueue = snapshot?.queue.filter((q) => q.status === 'Uploading' || q.status === 'Pending') ?? [];
  const failedToday = snapshot?.summary.failedToday ?? snapshot?.summary.failed ?? 0;
  const uploadedToday = snapshot?.summary.uploadedToday ?? snapshot?.summary.uploaded ?? 0;
  const slot = currentSlot(timetable);

  // Strictly filter queue to last 24h or active transfers
  const cutoff24h = Date.now() - 24 * 60 * 60 * 1000;
  const feedItems = (snapshot?.queue ?? []).filter((q) => {
    const isPendingOrUploading = q.status === 'Uploading' || q.status === 'Pending';
    const isRecent = new Date(q.updatedAt).getTime() >= cutoff24h;
    const matchesRoom = selectedRoom === 'ALL' || !selectedRoom || !q.roomId || q.roomId === selectedRoom;
    return (isPendingOrUploading || isRecent) && matchesRoom;
  });

  const isAllRooms = selectedRoom === 'ALL' || !selectedRoom;

  return (
    <div className="space-y-4">
      {/* Room Selector Chips */}
      <div className="space-y-1.5">
        <div className="flex items-center justify-between px-1">
          <span className="text-[10px] font-bold uppercase tracking-wider text-slate-500">
            Select Classroom Room
          </span>
          <span className="text-[10px] text-slate-400">
            {rooms.length} room{rooms.length !== 1 ? 's' : ''} available
          </span>
        </div>
        <div className="flex items-center gap-1.5 flex-wrap">
          <button
            onClick={() => onSelectRoom('ALL')}
            className={`px-3 py-1.5 text-xs rounded-xl font-semibold border transition active:scale-95 flex items-center gap-1.5 ${
              isAllRooms
                ? 'bg-cyan-600 text-white border-cyan-600 shadow-sm'
                : 'bg-white text-slate-700 border-slate-200 hover:bg-slate-50 shadow-xs'
            }`}
          >
            <span>All Rooms</span>
            <span
              className={`text-[10px] px-1.5 py-0.2 rounded-full ${
                isAllRooms ? 'bg-cyan-700 text-white' : 'bg-slate-100 text-slate-600'
              }`}
            >
              {rooms.length}
            </span>
          </button>

          {rooms.map((room) => {
            const isLocal = room === agentInfo?.roomId;
            const isSelected = selectedRoom === room;
            return (
              <button
                key={room}
                onClick={() => onSelectRoom(room)}
                className={`px-3 py-1.5 text-xs rounded-xl font-semibold border transition active:scale-95 flex items-center gap-1.5 ${
                  isSelected
                    ? 'bg-cyan-600 text-white border-cyan-600 shadow-sm'
                    : 'bg-white text-slate-700 border-slate-200 hover:bg-slate-50 shadow-xs'
                }`}
              >
                <span>Room {room}</span>
                {isLocal && (
                  <span
                    className={`text-[9px] px-1 py-0.2 rounded font-mono ${
                      isSelected ? 'bg-cyan-700 text-cyan-100' : 'bg-cyan-50 text-cyan-700 border border-cyan-200'
                    }`}
                  >
                    THIS PC
                  </span>
                )}
              </button>
            );
          })}
        </div>
      </div>

      {missingSlots.length > 0 && !isAllRooms && (
        <div className="bg-red-50 border border-red-200 rounded-2xl px-3.5 py-3 flex items-start gap-2.5">
          <CalendarX className="w-4 h-4 text-red-600 shrink-0 mt-0.5" />
          <div className="min-w-0">
            <p className="text-xs font-bold text-red-900">
              {missingSlots.length} lecture{missingSlots.length > 1 ? 's' : ''} missing today (Room {selectedRoom})
            </p>
            <ul className="text-[11px] text-red-700 leading-relaxed mt-1 space-y-0.5">
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
        <div className="bg-amber-50 border border-amber-200 rounded-2xl px-3.5 py-3 flex items-start gap-2.5">
          <PauseCircle className="w-4 h-4 text-amber-600 shrink-0 mt-0.5" />
          <p className="text-xs text-amber-900 leading-relaxed">
            {controlState?.uploadsPaused && <><strong>Uploads paused.</strong> New queue entries will wait. </>}
            {controlState?.monitoringPaused && <><strong>Monitoring paused.</strong> New files in the folder are ignored. </>}
            Resume from the Controls tab.
          </p>
        </div>
      )}

      {/* 4 Quick Metrics Cards (24h Window) */}
      <div className="grid grid-cols-2 gap-3">
        {/* Uploaded Today Card */}
        <div className="bg-white border border-slate-200/90 rounded-2xl p-3.5 shadow-sm flex flex-col justify-between">
          <div className="flex items-center justify-between text-slate-500">
            <span className="text-xs font-medium">Uploaded Today (24h)</span>
            <CheckCircle2 className="w-4 h-4 text-emerald-600" />
          </div>
          <div className="mt-2 flex items-baseline gap-2">
            <span className="text-2xl font-bold text-emerald-700">{uploadedToday}</span>
            <span className="text-[10px] text-emerald-600 font-semibold">in Drive</span>
          </div>
        </div>

        {/* Drive Queue Card */}
        <div className="bg-white border border-slate-200/90 rounded-2xl p-3.5 shadow-sm flex flex-col justify-between">
          <div className="flex items-center justify-between text-slate-500">
            <span className="text-xs font-medium">Drive Queue</span>
            <CloudUpload className="w-4 h-4 text-cyan-600" />
          </div>
          <div className="mt-2 flex items-baseline gap-2">
            <span className="text-2xl font-bold text-cyan-700">{activeQueue.length}</span>
            <span className="text-[10px] text-slate-400 font-medium">in transit</span>
          </div>
        </div>

        {/* Pending Reviews Card */}
        <div className="bg-white border border-slate-200/90 rounded-2xl p-3.5 shadow-sm flex flex-col justify-between">
          <div className="flex items-center justify-between text-slate-500">
            <span className="text-xs font-medium">Pending Reviews</span>
            <AlertCircle className={`w-4 h-4 ${pendingReviewCount > 0 ? 'text-amber-500' : 'text-slate-400'}`} />
          </div>
          <div className="mt-2 flex items-baseline gap-2">
            <span className={`text-2xl font-bold ${pendingReviewCount > 0 ? 'text-amber-600' : 'text-slate-800'}`}>
              {pendingReviewCount}
            </span>
            {pendingReviewCount > 0 && (
              <span className="text-[10px] text-amber-600 font-semibold">Action required</span>
            )}
          </div>
        </div>

        {/* Failed (24h) Card */}
        <div className="bg-white border border-slate-200/90 rounded-2xl p-3.5 shadow-sm flex flex-col justify-between">
          <div className="flex items-center justify-between text-slate-500">
            <span className="text-xs font-medium">Failed (24h)</span>
            <RotateCcw className={`w-4 h-4 ${failedToday > 0 ? 'text-red-500' : 'text-slate-400'}`} />
          </div>
          <div className="mt-2 flex items-baseline gap-2">
            <span className={`text-2xl font-bold ${failedToday > 0 ? 'text-red-600' : 'text-slate-800'}`}>
              {failedToday}
            </span>
            {failedToday > 0 ? (
              <span className="text-[10px] text-red-600 font-semibold">Needs retry</span>
            ) : (
              <span className="text-[10px] text-slate-400 font-medium">0 errors</span>
            )}
          </div>
        </div>
      </div>

      {/* Room status card */}
      <div className="bg-white border border-slate-200/90 rounded-2xl p-4 shadow-sm">
        <div className="flex items-center justify-between border-b border-slate-100 pb-2.5 mb-3">
          <div className="flex items-center gap-2">
            <span className="text-xs font-bold uppercase tracking-wider text-slate-700">Room Status</span>
            <span className="px-2 py-0.5 text-[11px] font-bold rounded-md bg-cyan-50 text-cyan-700 border border-cyan-200">
              {isAllRooms ? 'All Rooms (Center View)' : `Room ${selectedRoom}`}
            </span>
          </div>
          <Pill tone={agentInfo?.fileWatcherEnabled && !controlState?.monitoringPaused ? 'emerald' : 'amber'}>
            {agentInfo?.fileWatcherEnabled ? (controlState?.monitoringPaused ? 'Watcher Paused' : 'Watcher Active') : 'Watcher Off'}
          </Pill>
        </div>

        <div className="space-y-2 text-xs">
          {!isAllRooms ? (
            <>
              <div className="flex justify-between py-1 border-b border-slate-100">
                <span className="text-slate-500">Current Slot</span>
                {slot ? (
                  <span className="font-semibold text-slate-900">
                    {slot.batchId} · {slot.subjectId}
                  </span>
                ) : (
                  <span className="text-slate-400">No slot scheduled now</span>
                )}
              </div>
              <div className="flex justify-between py-1 border-b border-slate-100">
                <span className="text-slate-500">Monitored Folder</span>
                <span className="font-mono text-slate-800 truncate max-w-[200px]" title={agentInfo?.monitorFolder}>
                  {agentInfo?.monitorFolder || '...'}
                </span>
              </div>
              <div className="flex justify-between py-1">
                <span className="text-slate-500">Agent Uptime</span>
                <span className="font-semibold text-slate-800">
                  {agentInfo ? formatUptime(agentInfo.uptimeSeconds) : '...'}
                </span>
              </div>
            </>
          ) : (
            <>
              <div className="flex justify-between py-1 border-b border-slate-100">
                <span className="text-slate-500">Scope</span>
                <span className="font-semibold text-slate-900">All Classroom PCs across Center</span>
              </div>
              <div className="flex justify-between py-1 border-b border-slate-100">
                <span className="text-slate-500">Active Classroom PCs</span>
                <span className="font-semibold text-slate-800">{rooms.length} Rooms</span>
              </div>
              <div className="flex justify-between py-1">
                <span className="text-slate-500">Local PC Room</span>
                <span className="font-semibold text-cyan-700">Room {agentInfo?.roomId || '603'}</span>
              </div>
            </>
          )}
        </div>
      </div>

      {/* Live Upload Feed (Strictly Last 24 Hours) */}
      <div className="space-y-2">
        <div className="flex items-center justify-between px-1">
          <div>
            <h3 className="text-xs font-bold uppercase tracking-wider text-slate-700 flex items-center gap-1.5">
              <CloudUpload className="w-3.5 h-3.5 text-cyan-600" />
              Live Upload Feed
            </h3>
            <p className="text-[10px] text-slate-400 mt-0.5">
              Strict 24-hour window · Refreshes daily
            </p>
          </div>

          <div className="flex items-center gap-2">
            <span className="inline-flex items-center gap-1 text-[10px] font-semibold text-emerald-700 bg-emerald-50 px-2 py-0.5 rounded-full border border-emerald-200">
              <span className="w-1.5 h-1.5 rounded-full bg-emerald-500 animate-pulse"></span>
              Daily Auto-Refresh
            </span>

            {failedToday > 0 && (
              <button
                onClick={onRetryAllFailed}
                disabled={busy}
                className="text-[10px] font-bold px-2 py-1 rounded-lg bg-amber-50 text-amber-700 border border-amber-200 active:scale-95 transition disabled:opacity-40"
              >
                Retry all failed ({failedToday})
              </button>
            )}
          </div>
        </div>

        {feedItems.length === 0 ? (
          <div className="bg-white border border-slate-200/90 rounded-2xl p-6 text-center text-slate-500 text-xs shadow-sm space-y-1.5">
            <p className="font-medium text-slate-700">
              No uploads in the last 24 hours {isAllRooms ? 'for any room' : `for Room ${selectedRoom}`}.
            </p>
            <p className="text-[11px] text-slate-400">
              Drop a new recording or PDF into{' '}
              <code className="text-cyan-700 font-mono bg-cyan-50 px-1 py-0.5 rounded border border-cyan-200">
                {agentInfo?.monitorFolder || 'the monitored folder'}
              </code>{' '}
              to start a transfer!
            </p>
          </div>
        ) : (
          <div className="space-y-2">
            {feedItems.slice(0, 15).map((item) => {
              const canRetry = item.status === 'Failed' || item.status === 'FailedPermanently';
              const canCancel = item.status === 'Pending' || item.status === 'Failed' || item.status === 'FailedPermanently';
              const timeLabel = formatRelativeTime(item.updatedAt);

              return (
                <div key={item.queueEntryId} className="bg-white border border-slate-200/90 rounded-2xl p-3.5 shadow-sm space-y-2">
                  <div className="flex items-start justify-between gap-2">
                    <div className="flex items-center gap-2 min-w-0">
                      {item.fileType === 'VIDEO' ? (
                        <div className="w-7 h-7 rounded-lg bg-cyan-50 border border-cyan-200 flex items-center justify-center shrink-0">
                          <Video className="w-4 h-4 text-cyan-700" />
                        </div>
                      ) : (
                        <div className="w-7 h-7 rounded-lg bg-amber-50 border border-amber-200 flex items-center justify-center shrink-0">
                          <FileText className="w-4 h-4 text-amber-700" />
                        </div>
                      )}
                      <div className="min-w-0">
                        <span className="text-xs font-semibold text-slate-800 truncate block">{item.fileName}</span>
                        <div className="flex items-center gap-2 mt-0.5">
                          {item.roomId && (
                            <span className="text-[9px] font-bold px-1.5 py-0.2 rounded bg-slate-100 text-slate-700 border border-slate-200">
                              Room {item.roomId}
                            </span>
                          )}
                          <span className="text-[10px] text-slate-400 flex items-center gap-0.5">
                            <Clock className="w-2.5 h-2.5" /> {timeLabel}
                          </span>
                        </div>
                      </div>
                    </div>

                    <span className={`text-[10px] px-2 py-0.5 rounded-full font-bold uppercase shrink-0 border ${
                      item.status === 'Uploaded' ? 'bg-emerald-50 text-emerald-700 border-emerald-200' :
                      item.status === 'Uploading' ? 'bg-cyan-50 text-cyan-700 border-cyan-200 animate-pulse' :
                      item.status === 'FailedPermanently' ? 'bg-red-50 text-red-700 border-red-200' :
                      'bg-amber-50 text-amber-700 border-amber-200'
                    }`}>
                      {item.status}
                    </span>
                  </div>

                  <div className="space-y-1">
                    <div className="w-full bg-slate-100 rounded-full h-1.5 overflow-hidden">
                      <div
                        className={`h-full transition-all duration-300 rounded-full ${
                          item.status === 'Uploaded' ? 'bg-emerald-500' : 'bg-cyan-600'
                        }`}
                        style={{ width: `${item.progressPercentage}%` }}
                      />
                    </div>
                    <div className="flex justify-between text-[10px] text-slate-500 font-mono">
                      <span>{(item.fileSizeBytes / (1024 * 1024)).toFixed(1)} MB</span>
                      <span>{item.progressPercentage}%</span>
                    </div>
                  </div>

                  {(canRetry || canCancel) && (
                    <div className="flex items-center gap-2 pt-1 border-t border-slate-100">
                      {canRetry && (
                        <button
                          onClick={() => onRetryEntry(item.queueEntryId)}
                          disabled={busy}
                          className="flex items-center gap-1 text-[10px] font-bold px-2.5 py-1 rounded-lg bg-cyan-50 text-cyan-700 border border-cyan-200 hover:bg-cyan-100 active:scale-95 transition disabled:opacity-40"
                        >
                          <RotateCcw className="w-3 h-3" /> Retry
                        </button>
                      )}
                      {canCancel && (
                        <button
                          onClick={() => onCancelEntry(item.queueEntryId)}
                          disabled={busy}
                          className="flex items-center gap-1 text-[10px] font-bold px-2.5 py-1 rounded-lg bg-red-50 text-red-700 border border-red-200 hover:bg-red-100 active:scale-95 transition disabled:opacity-40"
                        >
                          <Ban className="w-3 h-3" /> Cancel
                        </button>
                      )}
                      {item.lastError && (
                        <span className="text-[10px] text-red-600 truncate flex items-center gap-1 min-w-0">
                          <AlertCircle className="w-3 h-3 shrink-0" />
                          {item.lastError}
                        </span>
                      )}
                    </div>
                  )}

                  {item.driveFileId && (
                    <div className="text-[10px] text-slate-500 flex items-center gap-1 truncate pt-1 border-t border-slate-100">
                      <CheckCircle2 className="w-3 h-3 text-emerald-600 shrink-0" />
                      <span className="truncate">Drive File ID: <strong className="text-slate-700">{item.driveFileId}</strong></span>
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        )}
      </div>

      {agentInfo && (
        <Card className="text-[11px] text-slate-500 flex items-center gap-2">
          <MonitorX className={`w-3.5 h-3.5 shrink-0 ${health?.database === 'Connected' ? 'text-emerald-600' : 'text-red-500'}`} />
          <span>
            DB: <strong className="text-slate-700">{health?.database ?? '...'}</strong>
          </span>
          <span className="text-slate-300">·</span>
          <Folder className="w-3.5 h-3.5 text-cyan-600 shrink-0" />
          <span>
            Drive: <strong className="text-slate-700">{agentInfo.googleDriveEnabled ? 'Live' : 'Mock mode'}</strong>
          </span>
        </Card>
      )}
    </div>
  );
}
